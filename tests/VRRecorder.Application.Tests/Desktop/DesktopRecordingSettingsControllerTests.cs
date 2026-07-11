using VRRecorder.Application.Desktop;
using VRRecorder.Application.Ports;
using VRRecorder.Application.Settings;
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
