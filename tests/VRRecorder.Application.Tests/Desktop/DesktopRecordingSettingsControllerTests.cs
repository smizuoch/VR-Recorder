using VRRecorder.Application.Audio;
using VRRecorder.Application.Desktop;
using VRRecorder.Application.Ports;
using VRRecorder.Application.Settings;
using VRRecorder.Domain.Audio;
using VRRecorder.Domain.Encoding;
using VRRecorder.Domain.Storage;
using VRRecorder.Domain.Video;

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

    [Fact]
    public async Task ExplicitLocaleChangePersistsBeforeRuntimeApplication()
    {
        var events = new List<string>();
        var store = new TrackingSettingsStore(
            VRRecorderSettings.CreateDefault(),
            events);
        var locales = new TrackingUiLocaleApplier(events);
        var controller = new DesktopRecordingSettingsController(
            store,
            new RecordingOutputPathResolver(
                new FixedDefaultOutputPathProvider(AbsolutePath("downloads"))),
            new TrackingLegalBundleOutputMirror(events),
            audioEndpoints: null,
            locales);
        var original = await controller.LoadAsync(CancellationToken.None);
        events.Clear();

        await controller.SaveAsync(
            original,
            original with { UiLocale = UiLocale.Japanese },
            CancellationToken.None);

        Assert.Equal(["load", "save", "locale"], events);
        Assert.Equal(UiLocale.Japanese, store.Current.UiLocale);
        Assert.Equal([UiLocale.Japanese], locales.Applied);
    }

    [Fact]
    public async Task ExplicitVrAndOscChangesPreserveConcurrentNestedValues()
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
        Assert.Equal(VrHand.Left, original.VrHand);
        Assert.True(original.OscAutoDiscover);
        var concurrentTransform = new OverlayTransform(
            Position: [7, 8, 9],
            RotationEuler: [10, 11, 12]);
        store.Replace(initial with
        {
            Vr = initial.Vr with { Transform = concurrentTransform },
            Osc = initial.Osc with
            {
                FallbackSendPort = 9200,
                FallbackReceivePort = 9201,
            },
        });

        await controller.SaveAsync(
            original,
            original with
            {
                VrHand = VrHand.Right,
                OverlayPlacement = OverlayPlacementMode.WorldPin,
                OscAutoDiscover = false,
                OscFallbackHost = "::1",
            },
            CancellationToken.None);

        Assert.Equal(VrHand.Right, store.Current.Vr.Hand);
        Assert.Equal(
            OverlayPlacementMode.WorldPin,
            store.Current.Vr.PlacementMode);
        Assert.Same(concurrentTransform, store.Current.Vr.Transform);
        Assert.False(store.Current.Osc.AutoDiscover);
        Assert.Equal("::1", store.Current.Osc.FallbackHost);
        Assert.Equal(9200, store.Current.Osc.FallbackSendPort);
        Assert.Equal(9201, store.Current.Osc.FallbackReceivePort);
    }

    [Fact]
    public async Task RejectsEveryUnsupportedEditableSettingBeforeSaving()
    {
        var events = new List<string>();
        var store = new TrackingSettingsStore(
            VRRecorderSettings.CreateDefault(),
            events);
        var controller = new DesktopRecordingSettingsController(
            store,
            new RecordingOutputPathResolver(
                new FixedDefaultOutputPathProvider(AbsolutePath("downloads"))),
            new TrackingLegalBundleOutputMirror(events));
        var valid = await controller.LoadAsync(CancellationToken.None);
        var invalidDrafts = new[]
        {
            valid with { SelfTimerSeconds = 1 },
            valid with { AutoStopSeconds = 2 },
            valid with
            {
                ResolutionChangePolicy = (ResolutionChangePolicy)int.MaxValue,
            },
            valid with { FrameRate = 0 },
            valid with { Encoder = (EncoderPreference)int.MaxValue },
            valid with { QualityPreset = (VideoQualityPreset)int.MaxValue },
            valid with { AudioRouting = (AudioRouting)int.MaxValue },
            valid with { DesktopEndpointId = " " },
            valid with { DesktopEndpointId = "render\nendpoint" },
            valid with { MicrophoneEndpointId = " " },
            valid with { MicrophoneEndpointId = "capture\nendpoint" },
            valid with { UiLocale = (UiLocale)int.MaxValue },
            valid with { VrHand = (VrHand)int.MaxValue },
            valid with
            {
                OverlayPlacement = (OverlayPlacementMode)int.MaxValue,
            },
            valid with { OscFallbackHost = "192.0.2.1" },
            valid with { OscFallbackHost = "not-an-address" },
            valid with { OscFallbackSendPort = 0 },
            valid with { OscFallbackSendPort = 65_536 },
            valid with { OscFallbackReceivePort = 0 },
            valid with { OscFallbackReceivePort = 65_536 },
            valid with { DesktopGainDb = double.NaN },
            valid with { DesktopGainDb = -96.01 },
            valid with { DesktopGainDb = 24.01 },
            valid with { MicrophoneGainDb = double.PositiveInfinity },
            valid with { MicrophoneGainDb = -96.01 },
            valid with { MicrophoneGainDb = 24.01 },
        };
        events.Clear();

        foreach (var invalid in invalidDrafts)
        {
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                controller.SaveAsync(
                    valid,
                    invalid,
                    CancellationToken.None));
        }

        Assert.Equal(0, store.SaveCount);
        Assert.Empty(events);
    }

    [Fact]
    public async Task EndpointOptionsFallbackAndCatalogFailuresAreExplicit()
    {
        var events = new List<string>();
        var defaults = VRRecorderSettings.CreateDefault();
        var store = new TrackingSettingsStore(defaults, events);
        var resolver = new RecordingOutputPathResolver(
            new FixedDefaultOutputPathProvider(AbsolutePath("downloads")));
        var mirror = new TrackingLegalBundleOutputMirror(events);
        var withoutCatalog = new DesktopRecordingSettingsController(
            store,
            resolver,
            mirror);
        var draft = await withoutCatalog.LoadAsync(CancellationToken.None);

        var fallback = await withoutCatalog.LoadAudioEndpointOptionsAsync(
            draft,
            CancellationToken.None);
        Assert.Equal(draft.DesktopEndpointId, Assert.Single(fallback.Desktop).Id);
        Assert.Equal(
            draft.MicrophoneEndpointId,
            Assert.Single(fallback.Microphone).Id);

        var nullCatalog = new DesktopRecordingSettingsController(
            store,
            resolver,
            mirror,
            new StubAudioEndpointCatalog(null!, []));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            nullCatalog.LoadAudioEndpointOptionsAsync(
                draft,
                CancellationToken.None));

        var nullOption = new DesktopRecordingSettingsController(
            store,
            resolver,
            mirror,
            new StubAudioEndpointCatalog([null!], []));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            nullOption.LoadAudioEndpointOptionsAsync(
                draft,
                CancellationToken.None));
    }

    [Fact]
    public async Task ConstructionLoadingAndChoiceCatalogsEnforceContracts()
    {
        var events = new List<string>();
        var resolver = new RecordingOutputPathResolver(
            new FixedDefaultOutputPathProvider(AbsolutePath("downloads")));
        var mirror = new TrackingLegalBundleOutputMirror(events);
        var store = new TrackingSettingsStore(
            VRRecorderSettings.CreateDefault(),
            events);

        Assert.Throws<ArgumentNullException>(() =>
            new DesktopRecordingSettingsController(null!, resolver, mirror));
        Assert.Throws<ArgumentNullException>(() =>
            new DesktopRecordingSettingsController(store, null!, mirror));
        Assert.Throws<ArgumentNullException>(() =>
            new DesktopRecordingSettingsController(store, resolver, null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            new DesktopRecordingSettingsController(store, resolver, mirror)
                .LoadAudioEndpointOptionsAsync(null!, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new DesktopRecordingSettingsController(
                new NullSettingsStore(),
                resolver,
                mirror).LoadAsync(CancellationToken.None));

        Assert.Equal([0, 3, 5, 10],
            DesktopRecordingSettingsController.SupportedSelfTimerSeconds);
        Assert.Equal([null, 3, 5, 10, 30, 60],
            DesktopRecordingSettingsController.SupportedAutoStopSeconds);
        Assert.Equal([30, 60, 90, 120],
            DesktopRecordingSettingsController.SupportedFrameRates);
        Assert.Contains(EncoderPreference.MediaFoundationSoftware,
            DesktopRecordingSettingsController.SupportedEncoders);
        Assert.Contains(AudioRouting.Muted,
            DesktopRecordingSettingsController.SupportedAudioRoutings);
        Assert.Contains(ResolutionChangePolicy.ExactFollowSegments,
            DesktopRecordingSettingsController
                .SupportedResolutionChangePolicies);
        Assert.Contains(VideoQualityPreset.High,
            DesktopRecordingSettingsController.SupportedQualityPresets);
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

    private sealed class TrackingUiLocaleApplier(List<string> events)
        : IUiLocaleApplier
    {
        public List<UiLocale> Applied { get; } = [];

        public void Apply(UiLocale locale)
        {
            events.Add("locale");
            Applied.Add(locale);
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
