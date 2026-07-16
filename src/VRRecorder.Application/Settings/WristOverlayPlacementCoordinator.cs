using VRRecorder.Application.Ports;

namespace VRRecorder.Application.Settings;

public sealed class WristOverlayPlacementCoordinator : IDisposable
{
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly ISettingsStore _settings;
    private readonly IWristOverlayPlacementRuntime _runtime;
    private bool _disposed;

    public WristOverlayPlacementCoordinator(
        ISettingsStore settings,
        IWristOverlayPlacementRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(runtime);
        _settings = settings;
        _runtime = runtime;
    }

    public Task<VrOverlayPlacement> ApplySavedAsync(
        CancellationToken cancellationToken) =>
        ExecuteAsync(static placement => placement, cancellationToken);

    public Task<VrOverlayPlacement> NudgeAsync(
        WristOverlayNudgeDirection direction,
        WristOverlayNudgeSize size,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            placement => placement with
            {
                Transform = WristOverlayPoseContract.Nudge(
                    placement.Transform,
                    direction,
                    size),
            },
            cancellationToken);

    public Task<VrOverlayPlacement> RecenterAsync(
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            static _ => new VrOverlayPlacement(
                OverlayPlacementMode.WristDock,
                WristOverlayPoseContract.CreateDefaultWristDockTransform()),
            cancellationToken);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _operationGate.Dispose();
    }

    private async Task<VrOverlayPlacement> ExecuteAsync(
        Func<VrOverlayPlacement, VrOverlayPlacement> selectPlacement,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _operationGate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var settings = await _settings
                .LoadAsync(cancellationToken)
                .ConfigureAwait(false);
            ArgumentNullException.ThrowIfNull(settings);
            if (!Enum.IsDefined(settings.Vr.Hand))
            {
                throw new InvalidDataException(
                    "The selected VR hand is not supported.");
            }

            var hand = settings.Vr.Hand;
            var device = _runtime.ReadDeviceProfile(hand);
            ArgumentNullException.ThrowIfNull(device);
            var current = VrOverlayPlacementProfiles.Resolve(
                settings.Vr,
                device,
                hand);
            var selected = selectPlacement(current);
            ValidatePlacement(selected);

            _runtime.ApplyPlacement(
                hand,
                selected.PlacementMode,
                selected.Transform);
            var readback = _runtime.ReadPlacement();
            if (!MatchesReadback(readback, hand, selected))
            {
                throw new InvalidDataException(
                    "The SteamVR overlay placement readback did not match the applied pose.");
            }

            var updatedVr = VrOverlayPlacementProfiles.Upsert(
                settings.Vr,
                new VrOverlayPlacementProfile(
                    device,
                    hand,
                    selected.PlacementMode,
                    selected.Transform));
            await _settings
                .SaveAsync(
                    settings with { Vr = updatedVr },
                    cancellationToken)
                .ConfigureAwait(false);
            return selected;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private static void ValidatePlacement(VrOverlayPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(placement);
        if (!Enum.IsDefined(placement.PlacementMode))
        {
            throw new InvalidDataException(
                "The overlay placement mode is not supported.");
        }

        _ = WristOverlayPoseContract.ToOpenVrMatrix34(placement.Transform);
    }

    private static bool MatchesReadback(
        WristOverlayPlacementReadback readback,
        VrHand hand,
        VrOverlayPlacement expected)
    {
        ArgumentNullException.ThrowIfNull(readback);
        if (readback.PlacementMode != expected.PlacementMode ||
            !WristOverlayPoseContract.MatchesReadback(
                readback.Transform,
                expected.Transform))
        {
            return false;
        }

        return expected.PlacementMode switch
        {
            OverlayPlacementMode.WristDock =>
                readback.DockHand == hand &&
                readback.TrackingOrigin is null,
            OverlayPlacementMode.WorldPin =>
                readback.DockHand is null &&
                readback.TrackingOrigin ==
                    WristOverlayPoseContract.WorldPinTrackingOrigin,
            _ => false,
        };
    }
}
