using VRRecorder.Application.Audio;
using VRRecorder.Application.Desktop;
using VRRecorder.Application.Ports;
using VRRecorder.Application.Settings;
using VRRecorder.Domain.Audio;
using VRRecorder.Domain.Storage;

namespace VRRecorder.Application.Tests.Desktop;

public sealed class DesktopRecordingSettingsControllerTests
{
    [Fact]
    public async Task OutputFolderChangeMirrorsBeforePersistingLatestSettings()
    {
        var events = new List<string>();
        var initial = VRRecorderSettings.CreateDefault();
        var store = new TrackingSettingsStore(initial, events);
        var mirror = new TrackingLegalBundleOutputMirror(events);
        var controller = new DesktopRecordingSettingsController(
            store,
            new RecordingOutputPathResolver(
                new FixedDefaultOutputPathProvider(AbsolutePath("downloads"))),
            mirror);
        var draft = await controller.LoadAsync(CancellationToken.None);
        events.Clear();
        var changedOutput = AbsolutePath("selected-output");

        await controller.SaveAsync(
            draft,
            draft with
            {
                OutputFolder = changedOutput,
                SelfTimerSeconds = 5,
            },
            CancellationToken.None);

        Assert.Equal(["load", "mirror", "save"], events);
        Assert.Equal(
            Path.GetFullPath(changedOutput),
            Assert.Single(mirror.OutputPaths).FullPath);
        Assert.Equal(changedOutput, store.Current.Recording.OutputFolder);
        Assert.Equal(5, store.Current.Recording.SelfTimerSeconds);
        Assert.Equivalent(initial.Audio, store.Current.Audio, strict: true);
        Assert.Equivalent(initial.Vr, store.Current.Vr, strict: true);
        Assert.Equivalent(initial.Osc, store.Current.Osc, strict: true);
    }

    [Fact]
    public async Task MirrorFailureLeavesPreviousOutputAndAllSettingsUntouched()
    {
        var events = new List<string>();
        var initial = VRRecorderSettings.CreateDefault();
        var store = new TrackingSettingsStore(initial, events);
        var failure = new IOException("Legal Bundle mirror failed");
        var mirror = new TrackingLegalBundleOutputMirror(events, failure);
        var controller = new DesktopRecordingSettingsController(
            store,
            new RecordingOutputPathResolver(
                new FixedDefaultOutputPathProvider(AbsolutePath("downloads"))),
            mirror);
        var draft = await controller.LoadAsync(CancellationToken.None);
        events.Clear();

        var thrown = await Assert.ThrowsAsync<IOException>(() =>
            controller.SaveAsync(
                draft,
                draft with
                {
                    OutputFolder = AbsolutePath("rejected-output"),
                    FrameRate = 60,
                },
                CancellationToken.None));

        Assert.Same(failure, thrown);
        Assert.Equal(["load", "mirror"], events);
        Assert.Equivalent(initial, store.Current, strict: true);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task SaveMergesOnlyUserChangesIntoLatestEditableSettings()
    {
        var events = new List<string>();
        var initial = VRRecorderSettings.CreateDefault();
        var store = new TrackingSettingsStore(initial, events);
        var controller = new DesktopRecordingSettingsController(
            store,
            new RecordingOutputPathResolver(
                new FixedDefaultOutputPathProvider(AbsolutePath("downloads"))),
            new TrackingLegalBundleOutputMirror(events));
        var original = await controller.LoadAsync(CancellationToken.None);
        store.Replace(initial with
        {
            Video = initial.Video with
            {
                FrameRate = 120,
                QualityPreset = VideoQualityPreset.Standard,
            },
        });

        await controller.SaveAsync(
            original,
            original with { SelfTimerSeconds = 3 },
            CancellationToken.None);

        Assert.Equal(3, store.Current.Recording.SelfTimerSeconds);
        Assert.Equal(120, store.Current.Video.FrameRate);
        Assert.Equal(
            VideoQualityPreset.Standard,
            store.Current.Video.QualityPreset);
    }

    [Fact]
    public async Task ExplicitAudioRoutingAndGainChangesPreserveEndpointIds()
    {
        var events = new List<string>();
        var defaults = VRRecorderSettings.CreateDefault();
        var initial = defaults with
        {
            Audio = defaults.Audio with
            {
                DesktopEndpointId = "persisted-render-endpoint",
                MicrophoneEndpointId = "persisted-capture-endpoint",
            },
        };
        var store = new TrackingSettingsStore(initial, events);
        var controller = new DesktopRecordingSettingsController(
            store,
            new RecordingOutputPathResolver(
                new FixedDefaultOutputPathProvider(AbsolutePath("downloads"))),
            new TrackingLegalBundleOutputMirror(events));
        var original = await controller.LoadAsync(CancellationToken.None);

        await controller.SaveAsync(
            original,
            original with
            {
                AudioRouting = AudioRouting.Muted,
                DesktopGainDb = -12.5,
                MicrophoneGainDb = 3.25,
            },
            CancellationToken.None);

        Assert.Equal(AudioRouting.Muted, store.Current.Audio.Routing);
        Assert.Equal(-12.5, store.Current.Audio.DesktopGainDb);
        Assert.Equal(3.25, store.Current.Audio.MicrophoneGainDb);
        Assert.Equal(
            "persisted-render-endpoint",
            store.Current.Audio.DesktopEndpointId);
        Assert.Equal(
            "persisted-capture-endpoint",
            store.Current.Audio.MicrophoneEndpointId);
    }

    [Fact]
    public async Task ExplicitDesktopEndpointChangePreservesConcurrentMicrophone()
    {
        var events = new List<string>();
        var defaults = VRRecorderSettings.CreateDefault();
        var initial = defaults with
        {
            Audio = defaults.Audio with
            {
                DesktopEndpointId = "initial-render",
                MicrophoneEndpointId = "initial-capture",
            },
        };
        var store = new TrackingSettingsStore(initial, events);
        var controller = new DesktopRecordingSettingsController(
            store,
            new RecordingOutputPathResolver(
                new FixedDefaultOutputPathProvider(AbsolutePath("downloads"))),
            new TrackingLegalBundleOutputMirror(events));
        var original = await controller.LoadAsync(CancellationToken.None);
        Assert.Equal("initial-render", original.DesktopEndpointId);
        Assert.Equal("initial-capture", original.MicrophoneEndpointId);
        store.Replace(initial with
        {
            Audio = initial.Audio with
            {
                MicrophoneEndpointId = "concurrent-capture",
            },
        });

        await controller.SaveAsync(
            original,
            original with { DesktopEndpointId = "selected-render" },
            CancellationToken.None);

        Assert.Equal("selected-render", store.Current.Audio.DesktopEndpointId);
        Assert.Equal(
            "concurrent-capture",
            store.Current.Audio.MicrophoneEndpointId);
    }

    [Fact]
    public async Task EndpointOptionsRetainInactiveSelectionAndDeduplicateCatalog()
    {
        var events = new List<string>();
        var defaults = VRRecorderSettings.CreateDefault();
        var settings = defaults with
        {
            Audio = defaults.Audio with
            {
                DesktopEndpointId = "saved-render",
                MicrophoneEndpointId = "saved-capture",
            },
        };
        var catalog = new StubAudioEndpointCatalog(
            [
                new AudioEndpointOption("active-render", "Speakers"),
                new AudioEndpointOption("active-render", "Duplicate"),
            ],
            [new AudioEndpointOption("saved-capture", "Studio mic")]);
        var controller = new DesktopRecordingSettingsController(
            new TrackingSettingsStore(settings, events),
            new RecordingOutputPathResolver(
                new FixedDefaultOutputPathProvider(AbsolutePath("downloads"))),
            new TrackingLegalBundleOutputMirror(events),
            catalog);
        var draft = await controller.LoadAsync(CancellationToken.None);

        var options = await controller.LoadAudioEndpointOptionsAsync(
            draft,
            CancellationToken.None);

        Assert.Equal(
            [
                new AudioEndpointOption("saved-render", "saved-render"),
                new AudioEndpointOption("active-render", "Speakers"),
            ],
            options.Desktop);
        Assert.Equal(
            [new AudioEndpointOption("saved-capture", "Studio mic")],
            options.Microphone);
    }

    private static string AbsolutePath(string name) => Path.Combine(
        Path.GetTempPath(),
        "vr-recorder-settings-controller-tests",
        name);

    private sealed class TrackingSettingsStore(
        VRRecorderSettings initial,
        List<string> events) : ISettingsStore
    {
        public VRRecorderSettings Current { get; private set; } = initial;

        public int SaveCount { get; private set; }

        public void Replace(VRRecorderSettings settings) => Current = settings;

        public Task<VRRecorderSettings> LoadAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            events.Add("load");
            return Task.FromResult(Current);
        }

        public Task SaveAsync(
            VRRecorderSettings settings,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            events.Add("save");
            SaveCount++;
            Current = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class StubAudioEndpointCatalog(
        IReadOnlyList<AudioEndpointOption> desktop,
        IReadOnlyList<AudioEndpointOption> microphone)
        : IAudioEndpointCatalog
    {
        public Task<IReadOnlyList<AudioEndpointOption>> GetActiveAsync(
            AudioInput input,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(
                input == AudioInput.Desktop ? desktop : microphone);
        }
    }

    private sealed class FixedDefaultOutputPathProvider(string path)
        : IDefaultOutputPathProvider
    {
        public OutputPath GetDefault() => new(path);
    }

    private sealed class TrackingLegalBundleOutputMirror(
        List<string> events,
        Exception? failure = null) : ILegalBundleOutputMirror
    {
        public List<OutputPath> OutputPaths { get; } = [];

        public Task MirrorAsync(
            OutputPath outputPath,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            events.Add("mirror");
            OutputPaths.Add(outputPath);
            return failure is null
                ? Task.CompletedTask
                : Task.FromException(failure);
        }
    }
}
