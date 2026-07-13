using VRRecorder.Application.Audio;
using VRRecorder.Application.Ports;
using VRRecorder.Application.Settings;
using VRRecorder.Application.Setup;
using VRRecorder.Domain.Audio;

namespace VRRecorder.Application.Tests.Setup;

public sealed class MicrophonePrivacyAndDeviceFirstRunSetupProbeTests
{
    [Fact]
    public async Task AllowedPrivacyAndExactActiveSavedDeviceVerifyStep()
    {
        var settings = SettingsWithMicrophone("capture-id");
        var catalog = new StubCatalog(
            [new AudioEndpointOption("capture-id", "Studio microphone")]);
        var probe = new MicrophonePrivacyAndDeviceFirstRunSetupProbe(
            new StubSettingsStore(settings),
            catalog,
            new StubPrivacyAccess(isAllowed: true));

        var verified = await probe.VerifyAsync(
            FirstRunSetupStep.MicrophonePrivacyAndDevice,
            CancellationToken.None);

        Assert.True(verified);
        Assert.Equal([AudioInput.Microphone], catalog.RequestedInputs);
    }

    [Theory]
    [InlineData(false, "capture-id")]
    [InlineData(true, "inactive-id")]
    [InlineData(true, "default-capture")]
    public async Task DeniedPrivacyOrUnconfirmedDeviceDoesNotVerify(
        bool privacyAllowed,
        string savedDeviceId)
    {
        var probe = new MicrophonePrivacyAndDeviceFirstRunSetupProbe(
            new StubSettingsStore(SettingsWithMicrophone(savedDeviceId)),
            new StubCatalog(
                [new AudioEndpointOption("capture-id", "Studio microphone")]),
            new StubPrivacyAccess(privacyAllowed));

        Assert.False(await probe.VerifyAsync(
            FirstRunSetupStep.MicrophonePrivacyAndDevice,
            CancellationToken.None));
    }

    [Fact]
    public async Task OtherStepDoesNotReadPrivacySettingsOrDevices()
    {
        var settings = new StubSettingsStore(
            SettingsWithMicrophone("capture-id"));
        var catalog = new StubCatalog([]);
        var privacy = new StubPrivacyAccess(isAllowed: true);
        var probe = new MicrophonePrivacyAndDeviceFirstRunSetupProbe(
            settings,
            catalog,
            privacy);

        Assert.False(await probe.VerifyAsync(
            FirstRunSetupStep.EncoderSelfTest,
            CancellationToken.None));
        Assert.Equal(0, settings.LoadCount);
        Assert.Equal(0, privacy.CallCount);
        Assert.Empty(catalog.RequestedInputs);
    }

    private static VRRecorderSettings SettingsWithMicrophone(string id)
    {
        var settings = VRRecorderSettings.CreateDefault();
        return settings with
        {
            Audio = settings.Audio with { MicrophoneEndpointId = id },
        };
    }

    private sealed class StubPrivacyAccess(bool isAllowed)
        : IMicrophonePrivacyAccess
    {
        public int CallCount { get; private set; }

        public Task<bool> IsAllowedAsync(CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(isAllowed);
        }
    }

    private sealed class StubSettingsStore(VRRecorderSettings settings)
        : ISettingsStore
    {
        public int LoadCount { get; private set; }

        public Task<VRRecorderSettings> LoadAsync(
            CancellationToken cancellationToken)
        {
            LoadCount++;
            return Task.FromResult(settings);
        }

        public Task SaveAsync(
            VRRecorderSettings updated,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class StubCatalog(IReadOnlyList<AudioEndpointOption> options)
        : IAudioEndpointCatalog
    {
        public List<AudioInput> RequestedInputs { get; } = [];

        public Task<IReadOnlyList<AudioEndpointOption>> GetActiveAsync(
            AudioInput input,
            CancellationToken cancellationToken)
        {
            RequestedInputs.Add(input);
            return Task.FromResult(options);
        }
    }
}
