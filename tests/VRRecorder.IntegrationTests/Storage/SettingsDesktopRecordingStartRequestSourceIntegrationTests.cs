using VRRecorder.Application.Desktop;
using VRRecorder.Application.Ports;
using VRRecorder.Application.Recording;
using VRRecorder.Application.Settings;
using VRRecorder.Domain.Audio;
using VRRecorder.Domain.Encoding;
using VRRecorder.Domain.Storage;
using VRRecorder.Infrastructure.Storage;

namespace VRRecorder.IntegrationTests.Storage;

public sealed class SettingsDesktopRecordingStartRequestSourceIntegrationTests
{
    [Fact]
    public async Task PersistedSettingsAreReloadedAndMappedForEveryStart()
    {
        using var directory = TemporaryDirectory.Create();
        var store = new JsonFileSettingsStore(
            Path.Combine(directory.Path, "settings.json"));
        var downloads = Path.Combine(directory.Path, "Downloads");
        var defaults = new FixedDefaultOutputPathProvider(downloads);
        var source = new SettingsDesktopRecordingStartRequestSource(
            store,
            defaults);
        var initial = VRRecorderSettings.CreateDefault();
        await store.SaveAsync(initial, CancellationToken.None);

        var first = await source.GetAsync(CancellationToken.None);

        var updated = initial with
        {
            Recording = initial.Recording with
            {
                SelfTimerSeconds = 10,
                AutoStopSeconds = 60,
                ResolutionChangePolicy =
                    ResolutionChangePolicy.ExactFollowSegments,
            },
            Video = initial.Video with
            {
                FrameRate = 90,
                Encoder = EncoderPreference.Qsv,
                QualityPreset = VideoQualityPreset.Standard,
            },
            Audio = initial.Audio with
            {
                Routing = AudioRouting.DesktopOnly,
                DesktopEndpointId = "persisted-render",
                MicrophoneEndpointId = "persisted-capture",
                DesktopGainDb = -12.5,
                MicrophoneGainDb = 3.25,
            },
        };
        await store.SaveAsync(updated, CancellationToken.None);

        var second = await source.GetAsync(CancellationToken.None);

        Assert.Equal(30, first.Command.FrameRate.Value);
        Assert.Equal(90, second.Command.FrameRate.Value);
        Assert.Equal(10, second.Command.SelfTimer.Seconds);
        Assert.Equal(60, second.Command.AutoStop.Seconds);
        Assert.Equal(EncoderPreference.Qsv, second.Command.EncoderPreference);
        Assert.Equal(
            ResolutionChangePolicy.ExactFollowSegments,
            second.Command.ResolutionChangePolicy);
        Assert.Equal(Path.GetFullPath(downloads), second.Command.OutputPath.FullPath);
        var media = Assert.IsType<RecordingMediaConfiguration>(second.Command.Media);
        Assert.Equal(AudioRouting.DesktopOnly, media.AudioRouting);
        Assert.Equal("persisted-render", media.DesktopEndpointId);
        Assert.Equal("persisted-capture", media.MicrophoneEndpointId);
        Assert.Equal(-12.5, media.DesktopGainDb);
        Assert.Equal(3.25, media.MicrophoneGainDb);
        Assert.Equal(VideoQualityPreset.Standard, media.QualityPreset);
        Assert.Equal(2, defaults.CallCount);
    }

    [Fact]
    public async Task ExternallyCorruptedGainIsBackedUpBeforeDefaultsAreMapped()
    {
        using var directory = TemporaryDirectory.Create();
        var settingsPath = Path.Combine(directory.Path, "settings.json");
        var store = new JsonFileSettingsStore(
            settingsPath);
        var settings = VRRecorderSettings.CreateDefault();
        await store.SaveAsync(settings, CancellationToken.None);
        var json = await File.ReadAllTextAsync(settingsPath);
        await File.WriteAllTextAsync(
            settingsPath,
            json.Replace(
                "\"desktopGainDb\": -6",
                "\"desktopGainDb\": 25",
                StringComparison.Ordinal));
        var defaults = new FixedDefaultOutputPathProvider(
            Path.Combine(directory.Path, "Downloads"));
        var source = new SettingsDesktopRecordingStartRequestSource(
            store,
            defaults);

        var request = await source.GetAsync(CancellationToken.None);

        var media = Assert.IsType<RecordingMediaConfiguration>(
            request.Command.Media);
        Assert.Equal(
            RecordingMediaConfiguration.DefaultInputGainDb,
            media.DesktopGainDb);
        Assert.False(File.Exists(settingsPath));
        Assert.Single(Directory.EnumerateFiles(
            directory.Path,
            "settings.corrupt-*.json",
            SearchOption.TopDirectoryOnly));
        Assert.Equal(1, defaults.CallCount);
    }

    [Fact]
    public async Task OutOfRangeGainCannotBePersisted()
    {
        using var directory = TemporaryDirectory.Create();
        var settingsPath = Path.Combine(directory.Path, "settings.json");
        var store = new JsonFileSettingsStore(settingsPath);
        var settings = VRRecorderSettings.CreateDefault();

        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(
            settings with
            {
                Audio = settings.Audio with { MicrophoneGainDb = 25 },
            },
            CancellationToken.None));

        Assert.False(File.Exists(settingsPath));
    }

    private sealed class FixedDefaultOutputPathProvider(string path)
        : IDefaultOutputPathProvider
    {
        public int CallCount { get; private set; }

        public OutputPath GetDefault()
        {
            CallCount++;
            return new OutputPath(path);
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TemporaryDirectory Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"vr-recorder-start-settings-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return new TemporaryDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
