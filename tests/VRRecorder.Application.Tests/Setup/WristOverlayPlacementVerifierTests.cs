using VRRecorder.Application.Ports;
using VRRecorder.Application.Settings;
using VRRecorder.Application.Setup;

namespace VRRecorder.Application.Tests.Setup;

public sealed class WristOverlayPlacementVerifierTests
{
    [Fact]
    public async Task ExplicitVerificationShowsAndReadsMatchingWristDock()
    {
        var settings = VRRecorderSettings.CreateDefault().Vr;
        var runtime = new CapturingRuntime(new WristOverlayPlacementReadback(
            OverlayPlacementMode.WristDock,
            settings.Hand,
            TrackingOrigin: null,
            WristOverlayPoseContract.ToOpenVrMatrix34(settings.Transform)));
        var verifier = new WristOverlayPlacementVerifier(() => runtime);

        var evidence = await verifier.VerifyAsync(
            settings,
            CancellationToken.None);

        Assert.NotNull(evidence);
        Assert.Equal(["show", "read"], runtime.Calls);
        Assert.Equal(settings.PlacementMode, evidence.PlacementMode);
        Assert.True(WristOverlayPoseContract.MatchesReadback(
            evidence.AppliedTransform,
            settings.Transform));
        Assert.True(evidence.IsVisible);
        Assert.True(evidence.IsReadableConfirmed);
        Assert.True(evidence.IsInteractionUnobstructedConfirmed);
    }

    [Fact]
    public async Task WorldPinRequiresStandingAbsoluteReadback()
    {
        var settings = VRRecorderSettings.CreateDefault().Vr with
        {
            PlacementMode = OverlayPlacementMode.WorldPin,
            Transform = new OverlayTransform(
                [1.25, 1.5, -2],
                [0, 45, 0]),
        };
        var wrongOrigin = new CapturingRuntime(
            new WristOverlayPlacementReadback(
                OverlayPlacementMode.WorldPin,
                DockHand: null,
                TrackingOrigin: null,
                WristOverlayPoseContract.ToOpenVrMatrix34(
                    settings.Transform)));
        var validOrigin = new CapturingRuntime(
            new WristOverlayPlacementReadback(
                OverlayPlacementMode.WorldPin,
                DockHand: null,
                WristOverlayTrackingOrigin.Standing,
                WristOverlayPoseContract.ToOpenVrMatrix34(
                    settings.Transform)));

        Assert.Null(await new WristOverlayPlacementVerifier(
                () => wrongOrigin)
            .VerifyAsync(settings, CancellationToken.None));
        Assert.NotNull(await new WristOverlayPlacementVerifier(
                () => validOrigin)
            .VerifyAsync(settings, CancellationToken.None));
    }

    [Fact]
    public async Task WristDockRequiresTheSelectedControllerRole()
    {
        var settings = VRRecorderSettings.CreateDefault().Vr;
        var runtime = new CapturingRuntime(new WristOverlayPlacementReadback(
            OverlayPlacementMode.WristDock,
            settings.Hand == VrHand.Left ? VrHand.Right : VrHand.Left,
            TrackingOrigin: null,
            WristOverlayPoseContract.ToOpenVrMatrix34(settings.Transform)));

        var evidence = await new WristOverlayPlacementVerifier(() => runtime)
            .VerifyAsync(settings, CancellationToken.None);

        Assert.Null(evidence);
    }

    [Fact]
    public async Task CancellationDoesNotCreateTheProductionRuntime()
    {
        var factoryCalls = 0;
        var verifier = new WristOverlayPlacementVerifier(() =>
        {
            factoryCalls++;
            throw new InvalidOperationException("must remain lazy");
        });
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            verifier.VerifyAsync(
                VRRecorderSettings.CreateDefault().Vr,
                cancellation.Token));

        Assert.Equal(0, factoryCalls);
    }

    [Fact]
    public async Task MissingRuntimeSettingsAndCoordinateMismatchesFailClosed()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new WristOverlayPlacementVerifier(null!));
        var valid = VRRecorderSettings.CreateDefault().Vr;
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            new WristOverlayPlacementVerifier(() =>
                new CapturingRuntime(Readback(valid)))
                .VerifyAsync(null!, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            new WristOverlayPlacementVerifier(() => null!)
                .VerifyAsync(valid, CancellationToken.None));

        var modeMismatch = Readback(valid) with
        {
            PlacementMode = OverlayPlacementMode.WorldPin,
            DockHand = null,
            TrackingOrigin = WristOverlayTrackingOrigin.Standing,
        };
        Assert.Null(await VerifyAsync(valid, modeMismatch));
        Assert.Null(await VerifyAsync(
            valid,
            Readback(valid) with
            {
                TrackingOrigin = WristOverlayTrackingOrigin.Standing,
            }));

        var world = valid with
        {
            PlacementMode = OverlayPlacementMode.WorldPin,
        };
        Assert.Null(await VerifyAsync(
            world,
            Readback(world) with
            {
                DockHand = VrHand.Left,
                TrackingOrigin = WristOverlayTrackingOrigin.Standing,
            }));

        var unknown = valid with
        {
            PlacementMode = (OverlayPlacementMode)int.MaxValue,
        };
        Assert.Null(await VerifyAsync(
            unknown,
            Readback(unknown)));
    }

    private static Task<WristOverlayPlacementEvidence?> VerifyAsync(
        VrSettings settings,
        WristOverlayPlacementReadback readback) =>
        new WristOverlayPlacementVerifier(() =>
            new CapturingRuntime(readback)).VerifyAsync(
                settings,
                CancellationToken.None);

    private static WristOverlayPlacementReadback Readback(
        VrSettings settings) =>
        new(
            settings.PlacementMode,
            settings.PlacementMode == OverlayPlacementMode.WristDock
                ? settings.Hand
                : null,
            settings.PlacementMode == OverlayPlacementMode.WorldPin
                ? WristOverlayTrackingOrigin.Standing
                : null,
            WristOverlayPoseContract.ToOpenVrMatrix34(settings.Transform));

    private sealed class CapturingRuntime(
        WristOverlayPlacementReadback readback)
        : IWristOverlayPlacementVerificationRuntime
    {
        public List<string> Calls { get; } = [];

        public void Show() => Calls.Add("show");

        public WristOverlayPlacementReadback ReadPlacement()
        {
            Calls.Add("read");
            return readback;
        }
    }
}
