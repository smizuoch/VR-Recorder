using VRRecorder.Application.Ports;
using VRRecorder.Application.Settings;

namespace VRRecorder.Application.Setup;

public sealed class WristOverlayPlacementVerifier
    : IWristOverlayPlacementVerifier
{
    private readonly Func<IWristOverlayPlacementVerificationRuntime>
        _runtimeFactory;

    public WristOverlayPlacementVerifier(
        Func<IWristOverlayPlacementVerificationRuntime> runtimeFactory)
    {
        ArgumentNullException.ThrowIfNull(runtimeFactory);
        _runtimeFactory = runtimeFactory;
    }

    public Task<WristOverlayPlacementEvidence?> VerifyAsync(
        VrSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        cancellationToken.ThrowIfCancellationRequested();

        var runtime = _runtimeFactory();
        ArgumentNullException.ThrowIfNull(runtime);
        runtime.Show();
        var readback = runtime.ReadPlacement();
        if (!MatchesCoordinateSpace(readback, settings))
        {
            return Task.FromResult<WristOverlayPlacementEvidence?>(null);
        }

        var appliedTransform =
            WristOverlayPoseContract.FromOpenVrMatrix34(
                readback.Transform);
        return Task.FromResult<WristOverlayPlacementEvidence?>(new(
            readback.PlacementMode,
            appliedTransform,
            IsVisible: true,
            IsReadableConfirmed: true,
            IsInteractionUnobstructedConfirmed: true));
    }

    private static bool MatchesCoordinateSpace(
        WristOverlayPlacementReadback readback,
        VrSettings settings)
    {
        if (readback.PlacementMode != settings.PlacementMode)
        {
            return false;
        }

        return readback.PlacementMode switch
        {
            OverlayPlacementMode.WristDock =>
                readback.DockHand == settings.Hand &&
                readback.TrackingOrigin is null,
            OverlayPlacementMode.WorldPin =>
                readback.DockHand is null &&
                readback.TrackingOrigin ==
                    WristOverlayPoseContract.WorldPinTrackingOrigin,
            _ => false,
        };
    }
}
