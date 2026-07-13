using VRRecorder.Application.Desktop;
using VRRecorder.Application.Ports;
using VRRecorder.Application.Settings;
using VRRecorder.Application.Setup;
using VRRecorder.Domain.Storage;

namespace VRRecorder.Application.Tests.Setup;

public sealed class LegalBundleVerificationFirstRunSetupProbeTests
{
    [Fact]
    public async Task MirrorsAndVerifiesConfiguredOutputBeforeCompletingStep()
    {
        var output = Path.GetFullPath(Path.Combine("test-output", "recordings"));
        var settings = SettingsWithOutput(output);
        var mirror = new StubMirror();
        var probe = new LegalBundleVerificationFirstRunSetupProbe(
            new StubSettingsStore(settings),
            new RecordingOutputPathResolver(new StubDefaultOutputProvider()),
            mirror);

        var verified = await probe.VerifyAsync(
            FirstRunSetupStep.LegalBundleVerification,
            CancellationToken.None);

        Assert.True(verified);
        Assert.Equal(Path.GetFullPath(output), Assert.Single(mirror.Paths).FullPath);
    }

    [Fact]
    public async Task OtherStepDoesNotLoadSettingsOrMirrorLegalBundle()
    {
        var settings = new StubSettingsStore(
            SettingsWithOutput(Path.GetFullPath("recordings")));
        var mirror = new StubMirror();
        var probe = new LegalBundleVerificationFirstRunSetupProbe(
            settings,
            new RecordingOutputPathResolver(new StubDefaultOutputProvider()),
            mirror);

        Assert.False(await probe.VerifyAsync(
            FirstRunSetupStep.OfflineLegalAccess,
            CancellationToken.None));
        Assert.Equal(0, settings.LoadCount);
        Assert.Empty(mirror.Paths);
    }

    private static VRRecorderSettings SettingsWithOutput(string output)
    {
        var settings = VRRecorderSettings.CreateDefault();
        return settings with
        {
            Recording = settings.Recording with { OutputFolder = output },
        };
    }

    private sealed class StubMirror : ILegalBundleOutputMirror
    {
        public List<OutputPath> Paths { get; } = [];

        public Task MirrorAsync(
            OutputPath outputPath,
            CancellationToken cancellationToken)
        {
            Paths.Add(outputPath);
            return Task.CompletedTask;
        }
    }

    private sealed class StubDefaultOutputProvider : IDefaultOutputPathProvider
    {
        public OutputPath GetDefault() => new(Path.GetFullPath("unused-default"));
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
}
