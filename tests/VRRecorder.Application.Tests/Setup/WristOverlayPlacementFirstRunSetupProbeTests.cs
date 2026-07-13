using VRRecorder.Application.Ports;
using VRRecorder.Application.Settings;
using VRRecorder.Application.Setup;

namespace VRRecorder.Application.Tests.Setup;

public sealed class WristOverlayPlacementFirstRunSetupProbeTests
{
    [Fact]
    public async Task VisibleMatchingRuntimePlacementWithUserEvidenceVerifies()
    {
        var settings = VRRecorderSettings.CreateDefault();
        var verifier = new StubVerifier(new WristOverlayPlacementEvidence(
            settings.Vr.PlacementMode,
            settings.Vr.Transform,
            IsVisible: true,
            IsReadableConfirmed: true,
            IsInteractionUnobstructedConfirmed: true));
        var probe = new WristOverlayPlacementFirstRunSetupProbe(
            new StubSettingsStore(settings),
            verifier);

        var verified = await probe.VerifyAsync(
            FirstRunSetupStep.WristOverlayPlacement,
            CancellationToken.None);

        Assert.True(verified);
        Assert.Equal(settings.Vr, verifier.RequestedSettings);
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public async Task MissingRuntimeOrUserEvidenceDoesNotVerify(
        bool visible,
        bool readable,
        bool unobstructed)
    {
        var settings = VRRecorderSettings.CreateDefault();
        var probe = new WristOverlayPlacementFirstRunSetupProbe(
            new StubSettingsStore(settings),
            new StubVerifier(new WristOverlayPlacementEvidence(
                settings.Vr.PlacementMode,
                settings.Vr.Transform,
                visible,
                readable,
                unobstructed)));

        Assert.False(await probe.VerifyAsync(
            FirstRunSetupStep.WristOverlayPlacement,
            CancellationToken.None));
    }

    [Fact]
    public async Task RuntimeReadbackMustMatchSavedModeAndTransform()
    {
        var settings = VRRecorderSettings.CreateDefault();
        var changed = new OverlayTransform(
            [9, 8, 7],
            [6, 5, 4]);
        var probe = new WristOverlayPlacementFirstRunSetupProbe(
            new StubSettingsStore(settings),
            new StubVerifier(new WristOverlayPlacementEvidence(
                OverlayPlacementMode.WorldPin,
                changed,
                IsVisible: true,
                IsReadableConfirmed: true,
                IsInteractionUnobstructedConfirmed: true)));

        Assert.False(await probe.VerifyAsync(
            FirstRunSetupStep.WristOverlayPlacement,
            CancellationToken.None));
    }

    private sealed class StubVerifier(WristOverlayPlacementEvidence? evidence)
        : IWristOverlayPlacementVerifier
    {
        public VrSettings? RequestedSettings { get; private set; }

        public Task<WristOverlayPlacementEvidence?> VerifyAsync(
            VrSettings settings,
            CancellationToken cancellationToken)
        {
            RequestedSettings = settings;
            return Task.FromResult(evidence);
        }
    }

    private sealed class StubSettingsStore(VRRecorderSettings settings)
        : ISettingsStore
    {
        public Task<VRRecorderSettings> LoadAsync(
            CancellationToken cancellationToken) => Task.FromResult(settings);

        public Task SaveAsync(
            VRRecorderSettings updated,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
