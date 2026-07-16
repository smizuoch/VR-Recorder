using VRRecorder.Application.Ports;
using VRRecorder.Application.Settings;

namespace VRRecorder.Application.Tests.Settings;

public sealed class WristOverlayPlacementCoordinatorTests
{
    [Fact]
    public async Task SavedDeviceAndHandProfileIsAppliedVerifiedAndPersisted()
    {
        var device = Device();
        var selected = Placement(
            OverlayPlacementMode.WorldPin,
            [1, 2, 3]);
        var settings = VRRecorderSettings.CreateDefault() with
        {
            Vr = VRRecorderSettings.CreateDefault().Vr with
            {
                Hand = VrHand.Right,
                PlacementProfiles =
                [
                    Profile(device, VrHand.Left, Placement(
                        OverlayPlacementMode.WristDock,
                        [9, 9, 9])),
                    Profile(device, VrHand.Right, selected),
                ],
            },
        };
        var store = new TrackingSettingsStore(settings);
        var runtime = new TrackingRuntime(device);
        var coordinator = new WristOverlayPlacementCoordinator(store, runtime);

        var applied = await coordinator.ApplySavedAsync(
            CancellationToken.None);

        Assert.Equal(selected, applied);
        Assert.Equal(
            [(VrHand.Right, selected.PlacementMode, selected.Transform)],
            runtime.Applied);
        Assert.Equal(1, store.SaveCount);
        Assert.Equal(
            selected,
            VrOverlayPlacementProfiles.Resolve(
                store.Current.Vr,
                device,
                VrHand.Right));
    }

    [Fact]
    public async Task UnknownDeviceUsesGlobalFallbackThenCreatesExactProfile()
    {
        var device = Device();
        var fallback = Placement(
            OverlayPlacementMode.WorldPin,
            [4, 5, 6]);
        var settings = VRRecorderSettings.CreateDefault() with
        {
            Vr = VRRecorderSettings.CreateDefault().Vr with
            {
                PlacementMode = fallback.PlacementMode,
                Transform = fallback.Transform,
            },
        };
        var store = new TrackingSettingsStore(settings);
        var coordinator = new WristOverlayPlacementCoordinator(
            store,
            new TrackingRuntime(device));

        await coordinator.ApplySavedAsync(CancellationToken.None);

        var profile = Assert.Single(store.Current.Vr.PlacementProfiles);
        Assert.Equal(device, profile.Device);
        Assert.Equal(settings.Vr.Hand, profile.Hand);
        Assert.Equal(fallback.PlacementMode, profile.PlacementMode);
        Assert.Equal(fallback.Transform, profile.Transform);
        Assert.Equal(settings.Vr.PlacementMode, store.Current.Vr.PlacementMode);
        Assert.Same(settings.Vr.Transform, store.Current.Vr.Transform);
    }

    [Fact]
    public async Task NudgeMovesResolvedProfileAndPersistsVerifiedPose()
    {
        var device = Device();
        var start = Placement(
            OverlayPlacementMode.WorldPin,
            [1, 2, 3]);
        var settings = VRRecorderSettings.CreateDefault() with
        {
            Vr = VRRecorderSettings.CreateDefault().Vr with
            {
                PlacementProfiles =
                [
                    Profile(device, VrHand.Left, start),
                ],
            },
        };
        var store = new TrackingSettingsStore(settings);
        var runtime = new TrackingRuntime(device);
        var coordinator = new WristOverlayPlacementCoordinator(store, runtime);

        var nudged = await coordinator.NudgeAsync(
            WristOverlayNudgeDirection.Left,
            WristOverlayNudgeSize.Large,
            CancellationToken.None);

        Assert.Equal(OverlayPlacementMode.WorldPin, nudged.PlacementMode);
        Assert.Equal([0.98, 2, 3], nudged.Transform.Position);
        Assert.Equal(nudged, VrOverlayPlacementProfiles.Resolve(
            store.Current.Vr,
            device,
            VrHand.Left));
    }

    [Fact]
    public async Task RecenterRestoresSelectedHandToDefaultWristDock()
    {
        var device = Device();
        var settings = VRRecorderSettings.CreateDefault() with
        {
            Vr = VRRecorderSettings.CreateDefault().Vr with
            {
                Hand = VrHand.Right,
                PlacementMode = OverlayPlacementMode.WorldPin,
                Transform = new OverlayTransform([9, 8, 7], [6, 5, 4]),
            },
        };
        var store = new TrackingSettingsStore(settings);
        var runtime = new TrackingRuntime(device);
        var coordinator = new WristOverlayPlacementCoordinator(store, runtime);

        var recentered = await coordinator.RecenterAsync(
            CancellationToken.None);

        Assert.Equal(OverlayPlacementMode.WristDock, recentered.PlacementMode);
        var expected = WristOverlayPoseContract
            .CreateDefaultWristDockTransform();
        Assert.Equal(expected.Position, recentered.Transform.Position);
        Assert.Equal(
            expected.RotationEuler,
            recentered.Transform.RotationEuler);
        Assert.Equal(
            [(VrHand.Right, recentered.PlacementMode, recentered.Transform)],
            runtime.Applied);
        Assert.Equal(recentered, VrOverlayPlacementProfiles.Resolve(
            store.Current.Vr,
            device,
            VrHand.Right));
    }

    [Theory]
    [InlineData(ReadbackMismatch.Mode)]
    [InlineData(ReadbackMismatch.Hand)]
    [InlineData(ReadbackMismatch.TrackingOrigin)]
    [InlineData(ReadbackMismatch.Transform)]
    public async Task MismatchedRuntimeReadbackNeverPersists(
        ReadbackMismatch mismatch)
    {
        var device = Device();
        var settings = VRRecorderSettings.CreateDefault();
        var store = new TrackingSettingsStore(settings);
        var runtime = new TrackingRuntime(device)
        {
            Mismatch = mismatch,
        };
        var coordinator = new WristOverlayPlacementCoordinator(store, runtime);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            coordinator.ApplySavedAsync(CancellationToken.None));

        Assert.Contains("readback", exception.Message,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, store.SaveCount);
        Assert.Same(settings, store.Current);
    }

    private static VrDeviceProfile Device() =>
        new(
            "lighthouse",
            "index-hmd",
            "/input/index_controller_profile.json");

    private static VrOverlayPlacement Placement(
        OverlayPlacementMode mode,
        double[] position) =>
        new(mode, new OverlayTransform(position, [10, 20, 30]));

    private static VrOverlayPlacementProfile Profile(
        VrDeviceProfile device,
        VrHand hand,
        VrOverlayPlacement placement) =>
        new(
            device,
            hand,
            placement.PlacementMode,
            placement.Transform);

    public enum ReadbackMismatch
    {
        None,
        Mode,
        Hand,
        TrackingOrigin,
        Transform,
    }

    private sealed class TrackingSettingsStore(VRRecorderSettings initial)
        : ISettingsStore
    {
        public VRRecorderSettings Current { get; private set; } = initial;

        public int SaveCount { get; private set; }

        public Task<VRRecorderSettings> LoadAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Current);
        }

        public Task SaveAsync(
            VRRecorderSettings settings,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Current = settings;
            SaveCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class TrackingRuntime(VrDeviceProfile device)
        : IWristOverlayPlacementRuntime
    {
        private WristOverlayPlacementReadback? _readback;

        public List<(VrHand Hand, OverlayPlacementMode Mode,
            OverlayTransform Transform)> Applied
        { get; } = [];

        public ReadbackMismatch Mismatch { get; init; }

        public VrDeviceProfile ReadDeviceProfile(VrHand hand) => device;

        public void ApplyPlacement(
            VrHand hand,
            OverlayPlacementMode placementMode,
            OverlayTransform transform)
        {
            Applied.Add((hand, placementMode, transform));
            var matrix = WristOverlayPoseContract.ToOpenVrMatrix34(transform);
            if (Mismatch == ReadbackMismatch.Transform)
            {
                matrix = matrix with { M03 = matrix.M03 + 1 };
            }

            _readback = placementMode == OverlayPlacementMode.WristDock
                ? new WristOverlayPlacementReadback(
                    Mismatch == ReadbackMismatch.Mode
                        ? OverlayPlacementMode.WorldPin
                        : OverlayPlacementMode.WristDock,
                    Mismatch == ReadbackMismatch.Hand
                        ? Opposite(hand)
                        : hand,
                    Mismatch == ReadbackMismatch.TrackingOrigin
                        ? WristOverlayTrackingOrigin.Standing
                        : null,
                    matrix)
                : new WristOverlayPlacementReadback(
                    Mismatch == ReadbackMismatch.Mode
                        ? OverlayPlacementMode.WristDock
                        : OverlayPlacementMode.WorldPin,
                    Mismatch == ReadbackMismatch.Hand ? hand : null,
                    Mismatch == ReadbackMismatch.TrackingOrigin
                        ? null
                        : WristOverlayTrackingOrigin.Standing,
                    matrix);
        }

        public WristOverlayPlacementReadback ReadPlacement() =>
            _readback ?? throw new InvalidOperationException(
                "No placement was applied.");

        private static VrHand Opposite(VrHand hand) =>
            hand == VrHand.Left ? VrHand.Right : VrHand.Left;
    }
}
