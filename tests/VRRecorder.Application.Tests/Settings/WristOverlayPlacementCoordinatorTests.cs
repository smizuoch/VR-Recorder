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
    [InlineData(
        OverlayPlacementMode.WristDock,
        OverlayPlacementMode.WorldPin)]
    [InlineData(
        OverlayPlacementMode.WorldPin,
        OverlayPlacementMode.WristDock)]
    public async Task PlacementModeSwitchUsesRuntimeParentSpaceConversion(
        OverlayPlacementMode currentMode,
        OverlayPlacementMode targetMode)
    {
        var device = Device();
        var current = Placement(currentMode, [1, 2, 3]);
        var converted = new OverlayTransform(
            [7, 8, -9],
            [15, 25, 35]);
        var settings = VRRecorderSettings.CreateDefault() with
        {
            Vr = VRRecorderSettings.CreateDefault().Vr with
            {
                Hand = VrHand.Right,
                PlacementProfiles =
                [
                    Profile(device, VrHand.Right, current),
                ],
            },
        };
        var store = new TrackingSettingsStore(settings);
        var runtime = new TrackingRuntime(device)
        {
            ConvertedTransform = converted,
        };
        var coordinator = new WristOverlayPlacementCoordinator(store, runtime);

        var selected = await coordinator.SetPlacementModeAsync(
            targetMode,
            CancellationToken.None);

        Assert.Equal([(VrHand.Right, targetMode)], runtime.Conversions);
        Assert.Equal(targetMode, selected.PlacementMode);
        Assert.Equal(
            converted.Position,
            selected.Transform.Position,
            DoublePrecisionComparer.Instance);
        Assert.Equal(
            converted.RotationEuler,
            selected.Transform.RotationEuler,
            DoublePrecisionComparer.Instance);
        Assert.Equal(
            [(VrHand.Right, targetMode, selected.Transform)],
            runtime.Applied);
        Assert.Equal(
            selected,
            VrOverlayPlacementProfiles.Resolve(
                store.Current.Vr,
                device,
            VrHand.Right));
    }

    [Fact]
    public async Task DragReleaseDetachesThroughAConvertedWorldPose()
    {
        var device = Device();
        var dock = new VrOverlayPlacement(
            OverlayPlacementMode.WristDock,
            WristOverlayPoseContract.CreateDefaultWristDockTransform());
        var settings = VRRecorderSettings.CreateDefault() with
        {
            Vr = VRRecorderSettings.CreateDefault().Vr with
            {
                Hand = VrHand.Right,
                PlacementProfiles =
                [
                    Profile(device, VrHand.Right, dock),
                ],
            },
        };
        var world = new OverlayTransform(
            [1.25, 1.5, -2],
            [5, 10, 15]);
        var store = new TrackingSettingsStore(settings);
        var runtime = new TrackingRuntime(device)
        {
            ConvertedTransform = world,
        };
        var coordinator = new WristOverlayPlacementCoordinator(store, runtime);

        var selected = await coordinator.DragReleaseAsync(
            new WristOverlayDragDelta(0.12, 0),
            CancellationToken.None);

        Assert.Equal(OverlayPlacementMode.WorldPin, selected.PlacementMode);
        Assert.Equal(
            world.Position,
            selected.Transform.Position,
            DoublePrecisionComparer.Instance);
        Assert.Equal(
            world.RotationEuler,
            selected.Transform.RotationEuler,
            DoublePrecisionComparer.Instance);
        Assert.Equal(
            [(VrHand.Right, OverlayPlacementMode.WorldPin)],
            runtime.Conversions);
        Assert.Equal(2, runtime.Applied.Count);
        Assert.Equal(
            OverlayPlacementMode.WristDock,
            runtime.Applied[0].Mode);
        Assert.Equal(
            0.15,
            runtime.Applied[0].Transform.Position[0],
            precision: 9);
        Assert.Equal(VrHand.Right, runtime.Applied[1].Hand);
        Assert.Equal(
            OverlayPlacementMode.WorldPin,
            runtime.Applied[1].Mode);
        Assert.Equal(
            world.Position,
            runtime.Applied[1].Transform.Position,
            DoublePrecisionComparer.Instance);
        Assert.Equal(
            selected,
            VrOverlayPlacementProfiles.Resolve(
                store.Current.Vr,
                device,
                VrHand.Right));
    }

    [Fact]
    public async Task DragReleaseReattachesPinnedPoseInsideHysteresis()
    {
        var device = Device();
        var pin = Placement(OverlayPlacementMode.WorldPin, [1, 2, 3]);
        var settings = VRRecorderSettings.CreateDefault() with
        {
            Vr = VRRecorderSettings.CreateDefault().Vr with
            {
                Hand = VrHand.Left,
                PlacementProfiles =
                [
                    Profile(device, VrHand.Left, pin),
                ],
            },
        };
        var defaultDock = WristOverlayPoseContract
            .CreateDefaultWristDockTransform();
        var nearDock = defaultDock with
        {
            Position =
            [
                defaultDock.Position[0] + 0.081,
                defaultDock.Position[1],
                defaultDock.Position[2],
            ],
        };
        var store = new TrackingSettingsStore(settings);
        var runtime = new TrackingRuntime(device)
        {
            ConvertedTransform = nearDock,
        };
        var coordinator = new WristOverlayPlacementCoordinator(store, runtime);

        var selected = await coordinator.DragReleaseAsync(
            new WristOverlayDragDelta(-0.0011, 0),
            CancellationToken.None);

        Assert.Equal(OverlayPlacementMode.WristDock, selected.PlacementMode);
        Assert.Equal(
            defaultDock.Position[0] + 0.0799,
            selected.Transform.Position[0],
            precision: 6);
        Assert.Equal(
            [(VrHand.Left, OverlayPlacementMode.WristDock)],
            runtime.Conversions);
        Assert.Single(runtime.Applied);
        Assert.Equal(
            selected,
            VrOverlayPlacementProfiles.Resolve(
                store.Current.Vr,
                device,
            VrHand.Left));
    }

    [Fact]
    public async Task FailedDragConversionRestoresThePreviouslyAppliedPose()
    {
        var device = Device();
        var dock = new VrOverlayPlacement(
            OverlayPlacementMode.WristDock,
            WristOverlayPoseContract.CreateDefaultWristDockTransform());
        var settings = VRRecorderSettings.CreateDefault() with
        {
            Vr = VRRecorderSettings.CreateDefault().Vr with
            {
                PlacementProfiles =
                [
                    Profile(device, VrHand.Left, dock),
                ],
            },
        };
        var store = new TrackingSettingsStore(settings);
        var runtime = new TrackingRuntime(device)
        {
            ConversionException = new InvalidOperationException(
                "controller disconnected"),
        };
        var coordinator = new WristOverlayPlacementCoordinator(store, runtime);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.DragReleaseAsync(
                new WristOverlayDragDelta(0.12, 0),
                CancellationToken.None));

        Assert.Equal(2, runtime.Applied.Count);
        Assert.Equal(
            OverlayPlacementMode.WristDock,
            runtime.Applied[0].Mode);
        Assert.Equal(
            dock.Transform.Position,
            runtime.Applied[1].Transform.Position);
        Assert.Equal(0, store.SaveCount);
        Assert.Same(settings, store.Current);
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

    [Fact]
    public async Task ConstructionModeAndDisposedStateEnforceContracts()
    {
        var settings = VRRecorderSettings.CreateDefault();
        var store = new TrackingSettingsStore(settings);
        var runtime = new TrackingRuntime(Device());
        Assert.Throws<ArgumentNullException>(() =>
            new WristOverlayPlacementCoordinator(null!, runtime));
        Assert.Throws<ArgumentNullException>(() =>
            new WristOverlayPlacementCoordinator(store, null!));
        var coordinator = new WristOverlayPlacementCoordinator(store, runtime);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            coordinator.SetPlacementModeAsync(
                (OverlayPlacementMode)int.MaxValue,
                CancellationToken.None));
        var unchanged = await coordinator.SetPlacementModeAsync(
            OverlayPlacementMode.WristDock,
            CancellationToken.None);
        Assert.Equal(OverlayPlacementMode.WristDock, unchanged.PlacementMode);
        Assert.Empty(runtime.Conversions);

        coordinator.Dispose();
        coordinator.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            coordinator.ApplySavedAsync(CancellationToken.None));
    }

    [Fact]
    public async Task MissingSettingsDeviceAndInvalidHandFailBeforeApplying()
    {
        var runtime = new TrackingRuntime(Device());
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            new WristOverlayPlacementCoordinator(
                new NullSettingsStore(),
                runtime).ApplySavedAsync(CancellationToken.None));

        var defaults = VRRecorderSettings.CreateDefault();
        var invalidHand = defaults with
        {
            Vr = defaults.Vr with { Hand = (VrHand)int.MaxValue },
        };
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new WristOverlayPlacementCoordinator(
                new TrackingSettingsStore(invalidHand),
                runtime).ApplySavedAsync(CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            new WristOverlayPlacementCoordinator(
                new TrackingSettingsStore(defaults),
                new NullDeviceRuntime()).ApplySavedAsync(
                    CancellationToken.None));
    }

    [Fact]
    public async Task ConversionFailureSurvivesBestEffortRestoreFailure()
    {
        var settings = VRRecorderSettings.CreateDefault();
        var failure = new InvalidOperationException("conversion failed");
        var runtime = new TrackingRuntime(Device())
        {
            ConversionException = failure,
            ApplyFailureCall = 2,
        };
        var coordinator = new WristOverlayPlacementCoordinator(
            new TrackingSettingsStore(settings),
            runtime);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.DragReleaseAsync(
                new WristOverlayDragDelta(0.12, 0),
                CancellationToken.None));

        Assert.Same(failure, thrown);
        Assert.Single(runtime.Applied);
    }

    [Fact]
    public async Task NullRuntimeReadbackFailsBeforePersistence()
    {
        var store = new TrackingSettingsStore(VRRecorderSettings.CreateDefault());
        var coordinator = new WristOverlayPlacementCoordinator(
            store,
            new TrackingRuntime(Device()) { ReturnNullReadback = true });

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            coordinator.ApplySavedAsync(CancellationToken.None));

        Assert.Equal(0, store.SaveCount);
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

    private sealed class NullSettingsStore : ISettingsStore
    {
        public Task<VRRecorderSettings> LoadAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<VRRecorderSettings>(null!);

        public Task SaveAsync(
            VRRecorderSettings settings,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class NullDeviceRuntime : IWristOverlayPlacementRuntime
    {
        public VrDeviceProfile ReadDeviceProfile(VrHand hand) => null!;

        public void ApplyPlacement(
            VrHand hand,
            OverlayPlacementMode placementMode,
            OverlayTransform transform) => throw new NotSupportedException();

        public WristOverlayPlacementReadback ReadPlacement() =>
            throw new NotSupportedException();

        public OpenVrMatrix34 ConvertPlacement(
            VrHand hand,
            OverlayPlacementMode placementMode) =>
            throw new NotSupportedException();
    }

    private sealed class TrackingRuntime(VrDeviceProfile device)
        : IWristOverlayPlacementRuntime
    {
        private WristOverlayPlacementReadback? _readback;

        public List<(VrHand Hand, OverlayPlacementMode Mode,
            OverlayTransform Transform)> Applied
        { get; } = [];

        public List<(VrHand Hand, OverlayPlacementMode Mode)> Conversions
        { get; } = [];

        public OverlayTransform ConvertedTransform { get; init; } =
            WristOverlayPoseContract.CreateDefaultWristDockTransform();

        public Queue<OverlayTransform> ConvertedTransforms { get; } = [];

        public Exception? ConversionException { get; init; }

        public int? ApplyFailureCall { get; init; }

        public bool ReturnNullReadback { get; init; }

        private int _applyCallCount;

        public ReadbackMismatch Mismatch { get; init; }

        public VrDeviceProfile ReadDeviceProfile(VrHand hand) => device;

        public void ApplyPlacement(
            VrHand hand,
            OverlayPlacementMode placementMode,
            OverlayTransform transform)
        {
            _applyCallCount++;
            if (_applyCallCount == ApplyFailureCall)
            {
                throw new InvalidOperationException("apply failed");
            }
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
            ReturnNullReadback
                ? null!
                : _readback ?? throw new InvalidOperationException(
                "No placement was applied.");

        public OpenVrMatrix34 ConvertPlacement(
            VrHand hand,
            OverlayPlacementMode placementMode)
        {
            Conversions.Add((hand, placementMode));
            if (ConversionException is not null)
            {
                throw ConversionException;
            }
            return WristOverlayPoseContract.ToOpenVrMatrix34(
                ConvertedTransforms.Count == 0
                    ? ConvertedTransform
                    : ConvertedTransforms.Dequeue());
        }

        private static VrHand Opposite(VrHand hand) =>
            hand == VrHand.Left ? VrHand.Right : VrHand.Left;
    }

    private sealed class DoublePrecisionComparer : IEqualityComparer<double>
    {
        public static DoublePrecisionComparer Instance { get; } = new();

        public bool Equals(double x, double y) =>
            Math.Abs(x - y) <= 0.000001;

        public int GetHashCode(double value) => value.GetHashCode();
    }
}
