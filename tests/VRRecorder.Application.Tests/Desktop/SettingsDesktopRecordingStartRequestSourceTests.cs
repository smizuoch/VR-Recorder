using VRRecorder.Application.Desktop;
using VRRecorder.Application.Ports;
using VRRecorder.Application.Recording;
using VRRecorder.Application.Settings;
using VRRecorder.Domain.Audio;
using VRRecorder.Domain.Encoding;
using VRRecorder.Domain.Storage;

namespace VRRecorder.Application.Tests.Desktop;

public sealed class SettingsDesktopRecordingStartRequestSourceTests
{
    [Fact]
    public async Task MapsEveryRecordingSettingWithoutGuessingHardwareIdentity()
    {
        var outputFolder = AbsolutePath("custom-output");
        var settings = CreateSettings(
            outputFolder: outputFolder,
            selfTimerSeconds: 5,
            autoStopSeconds: 30,
            resolutionPolicy: ResolutionChangePolicy.ExactFollowSegments,
            frameRate: 120,
            encoder: EncoderPreference.Amf,
            qualityPreset: VideoQualityPreset.Standard,
            routing: AudioRouting.MicOnly,
            desktopEndpointId: "render-日本語",
            microphoneEndpointId: "capture-日本語",
            desktopGainDb: -96,
            microphoneGainDb: 24);
        var defaults = new TrackingDefaultOutputPathProvider(
            AbsolutePath("unused-default"));
        var source = new SettingsDesktopRecordingStartRequestSource(
            new QueueSettingsStore(settings),
            defaults);

        var request = await source.GetAsync(CancellationToken.None);

        Assert.Null(request.SelectedServiceId);
        Assert.Equal(5, request.Command.SelfTimer.Seconds);
        Assert.Equal(30, request.Command.AutoStop.Seconds);
        Assert.Equal(Path.GetFullPath(outputFolder), request.Command.OutputPath.FullPath);
        Assert.Equal(120, request.Command.FrameRate.Value);
        Assert.Equal(EncoderPreference.Amf, request.Command.EncoderPreference);
        Assert.Equal(GpuVendor.Unknown, request.Command.GpuVendor);
        Assert.Equal(
            ResolutionChangePolicy.ExactFollowSegments,
            request.Command.ResolutionChangePolicy);
        var media = Assert.IsType<RecordingMediaConfiguration>(
            request.Command.Media);
        Assert.Equal(AudioRouting.MicOnly, media.AudioRouting);
        Assert.Equal("render-日本語", media.DesktopEndpointId);
        Assert.Equal("capture-日本語", media.MicrophoneEndpointId);
        Assert.Equal(-96, media.DesktopGainDb);
        Assert.Equal(24, media.MicrophoneGainDb);
        Assert.Equal(VideoQualityPreset.Standard, media.QualityPreset);
        Assert.Equal("unidentified-spout-sender", media.SpoutSenderIdentity);
        Assert.Equal(0ul, media.SpoutAdapterLuid);
        Assert.Equal(0ul, media.EncoderAdapterLuid);
        Assert.Equal("unidentified-gpu", media.GpuIdentity);
        Assert.Equal(0, defaults.CallCount);
    }

    [Fact]
    public async Task DownloadsTokenUsesKnownFolderAndNullAutoStopMapsToInfinite()
    {
        var defaultOutput = AbsolutePath("downloads-known-folder");
        var defaults = new TrackingDefaultOutputPathProvider(defaultOutput);
        var source = new SettingsDesktopRecordingStartRequestSource(
            new QueueSettingsStore(CreateSettings()),
            defaults);

        var request = await source.GetAsync(CancellationToken.None);

        Assert.Equal(
            Path.GetFullPath(defaultOutput),
            request.Command.OutputPath.FullPath);
        Assert.True(request.Command.AutoStop.IsInfinite);
        Assert.Equal(1, defaults.CallCount);
    }

    [Fact]
    public async Task EveryStartReloadsTheLatestSettings()
    {
        var first = CreateSettings(
            outputFolder: AbsolutePath("first"),
            frameRate: 30);
        var second = CreateSettings(
            outputFolder: AbsolutePath("second"),
            frameRate: 90);
        var store = new QueueSettingsStore(first, second);
        var source = new SettingsDesktopRecordingStartRequestSource(
            store,
            new TrackingDefaultOutputPathProvider(AbsolutePath("unused")));

        var firstRequest = await source.GetAsync(CancellationToken.None);
        var secondRequest = await source.GetAsync(CancellationToken.None);

        Assert.Equal(2, store.LoadCount);
        Assert.Equal(30, firstRequest.Command.FrameRate.Value);
        Assert.Equal(90, secondRequest.Command.FrameRate.Value);
        Assert.NotEqual(
            firstRequest.Command.OutputPath.FullPath,
            secondRequest.Command.OutputPath.FullPath);
    }

    [Theory]
    [InlineData("knownfolder:Desktop")]
    [InlineData("KNOWNFOLDER:Downloads")]
    [InlineData("relative/output")]
    public async Task UnknownTokenOrRelativePathFailsBeforeDefaultResolution(
        string outputFolder)
    {
        var defaults = new TrackingDefaultOutputPathProvider(
            AbsolutePath("unused"));
        var source = new SettingsDesktopRecordingStartRequestSource(
            new QueueSettingsStore(CreateSettings(outputFolder: outputFolder)),
            defaults);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            source.GetAsync(CancellationToken.None));

        Assert.Equal(0, defaults.CallCount);
    }

    [Fact]
    public async Task NonCanonicalAbsolutePathFailsClosed()
    {
        var nonCanonicalPath = Path.Combine(
            AbsolutePath("parent"),
            "..",
            "output");
        var source = new SettingsDesktopRecordingStartRequestSource(
            new QueueSettingsStore(CreateSettings(
                outputFolder: nonCanonicalPath)),
            new TrackingDefaultOutputPathProvider(AbsolutePath("unused")));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            source.GetAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData(-96.001)]
    [InlineData(24.001)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public async Task InvalidGainFailsBeforeDefaultResolution(double gain)
    {
        var defaults = new TrackingDefaultOutputPathProvider(
            AbsolutePath("unused"));
        var source = new SettingsDesktopRecordingStartRequestSource(
            new QueueSettingsStore(CreateSettings(desktopGainDb: gain)),
            defaults);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            source.GetAsync(CancellationToken.None));

        Assert.Equal(0, defaults.CallCount);
    }

    [Fact]
    public async Task CancellationIsPassedToStoreAndRecheckedBeforeMapping()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var store = new QueueSettingsStore(CreateSettings());
        var defaults = new TrackingDefaultOutputPathProvider(
            AbsolutePath("unused"));
        var source = new SettingsDesktopRecordingStartRequestSource(
            store,
            defaults);

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => source.GetAsync(cancellation.Token));

        Assert.Equal(cancellation.Token, store.LastCancellationToken);
        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.Equal(0, defaults.CallCount);
    }

    private static VRRecorderSettings CreateSettings(
        string outputFolder = "knownfolder:Downloads",
        int selfTimerSeconds = 0,
        int? autoStopSeconds = null,
        ResolutionChangePolicy resolutionPolicy =
            ResolutionChangePolicy.SingleFileFit,
        int frameRate = 30,
        EncoderPreference encoder = EncoderPreference.Auto,
        VideoQualityPreset qualityPreset = VideoQualityPreset.High,
        AudioRouting routing = AudioRouting.Mixed,
        string desktopEndpointId = "default-render",
        string microphoneEndpointId = "default-capture",
        double desktopGainDb = -6,
        double microphoneGainDb = -6)
    {
        var defaults = VRRecorderSettings.CreateDefault();
        return defaults with
        {
            Recording = defaults.Recording with
            {
                OutputFolder = outputFolder,
                SelfTimerSeconds = selfTimerSeconds,
                AutoStopSeconds = autoStopSeconds,
                ResolutionChangePolicy = resolutionPolicy,
            },
            Video = defaults.Video with
            {
                FrameRate = frameRate,
                Encoder = encoder,
                QualityPreset = qualityPreset,
            },
            Audio = defaults.Audio with
            {
                Routing = routing,
                DesktopEndpointId = desktopEndpointId,
                MicrophoneEndpointId = microphoneEndpointId,
                DesktopGainDb = desktopGainDb,
                MicrophoneGainDb = microphoneGainDb,
            },
        };
    }

    private static string AbsolutePath(string name) => Path.Combine(
        Path.GetTempPath(),
        $"vr-recorder-settings-source-{name}");

    private sealed class QueueSettingsStore(params VRRecorderSettings[] settings)
        : ISettingsStore
    {
        private readonly Queue<VRRecorderSettings> values = new(settings);

        public int LoadCount { get; private set; }

        public CancellationToken LastCancellationToken { get; private set; }

        public Task<VRRecorderSettings> LoadAsync(
            CancellationToken cancellationToken)
        {
            LoadCount++;
            LastCancellationToken = cancellationToken;
            return Task.FromResult(values.Dequeue());
        }

        public Task SaveAsync(
            VRRecorderSettings settings,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class TrackingDefaultOutputPathProvider(string path)
        : IDefaultOutputPathProvider
    {
        public int CallCount { get; private set; }

        public OutputPath GetDefault()
        {
            CallCount++;
            return new OutputPath(path);
        }
    }
}
