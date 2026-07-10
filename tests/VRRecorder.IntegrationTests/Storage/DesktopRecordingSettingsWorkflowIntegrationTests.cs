using VRRecorder.Application.Desktop;
using VRRecorder.Application.Ports;
using VRRecorder.Application.Settings;
using VRRecorder.Domain.Audio;
using VRRecorder.Domain.Encoding;
using VRRecorder.Domain.Storage;
using VRRecorder.Infrastructure.Storage;

namespace VRRecorder.IntegrationTests.Storage;

public sealed class DesktopRecordingSettingsWorkflowIntegrationTests
{
    [Fact]
    public async Task EditedRecordingOptionsPreserveLatestUneditedSettingsAndDriveNextStart()
    {
        using var directory = TemporaryDirectory.Create();
        var store = new JsonFileSettingsStore(
            Path.Combine(directory.Path, "settings.json"));
        var defaults = VRRecorderSettings.CreateDefault();
        var initial = defaults with
        {
            Recording = defaults.Recording with
            {
                OutputFolder = Path.Combine(directory.Path, "output"),
            },
            Audio = defaults.Audio with
            {
                Routing = AudioRouting.DesktopOnly,
                DesktopEndpointId = "initial-render",
                MicrophoneEndpointId = "initial-capture",
            },
            Vr = defaults.Vr with
            {
                Hand = VrHand.Right,
                PlacementMode = OverlayPlacementMode.WorldPin,
                Transform = new OverlayTransform(
                    Position: [1, 2, 3],
                    RotationEuler: [4, 5, 6]),
            },
            Osc = defaults.Osc with
            {
                AutoDiscover = false,
                FallbackSendPort = 9100,
                FallbackReceivePort = 9101,
            },
        };
        await store.SaveAsync(initial, CancellationToken.None);
        var controller = new DesktopRecordingSettingsController(store);
        var draft = await controller.LoadAsync(CancellationToken.None);

        var latest = initial with
        {
            Audio = initial.Audio with
            {
                DesktopGainDb = -18.5,
                MicrophoneGainDb = 2.5,
            },
            Vr = initial.Vr with
            {
                Transform = new OverlayTransform(
                    Position: [7, 8, 9],
                    RotationEuler: [10, 11, 12]),
            },
            Osc = initial.Osc with
            {
                FallbackSendPort = 9200,
                FallbackReceivePort = 9201,
            },
        };
        await store.SaveAsync(latest, CancellationToken.None);

        await controller.SaveAsync(
            draft with
            {
                SelfTimerSeconds = 10,
                AutoStopSeconds = 60,
                ResolutionChangePolicy =
                    ResolutionChangePolicy.ExactFollowSegments,
                FrameRate = 90,
                Encoder = EncoderPreference.Qsv,
                QualityPreset = VideoQualityPreset.Standard,
            },
            CancellationToken.None);

        var persisted = await store.LoadAsync(CancellationToken.None);
        Assert.Equal(latest.SchemaVersion, persisted.SchemaVersion);
        Assert.Equal(latest.Recording.OutputFolder, persisted.Recording.OutputFolder);
        Assert.Equivalent(latest.Audio, persisted.Audio, strict: true);
        Assert.Equivalent(latest.Vr, persisted.Vr, strict: true);
        Assert.Equivalent(latest.Osc, persisted.Osc, strict: true);
        Assert.Equal(10, persisted.Recording.SelfTimerSeconds);
        Assert.Equal(60, persisted.Recording.AutoStopSeconds);
        Assert.Equal(
            ResolutionChangePolicy.ExactFollowSegments,
            persisted.Recording.ResolutionChangePolicy);
        Assert.Equal(90, persisted.Video.FrameRate);
        Assert.Equal(EncoderPreference.Qsv, persisted.Video.Encoder);
        Assert.Equal(
            VideoQualityPreset.Standard,
            persisted.Video.QualityPreset);

        var request = await new SettingsDesktopRecordingStartRequestSource(
                store,
                new UnexpectedDefaultOutputPathProvider())
            .GetAsync(CancellationToken.None);
        Assert.Equal(10, request.Command.SelfTimer.Seconds);
        Assert.Equal(60, request.Command.AutoStop.Seconds);
        Assert.Equal(90, request.Command.FrameRate.Value);
        Assert.Equal(EncoderPreference.Qsv, request.Command.EncoderPreference);
        Assert.Equal(
            ResolutionChangePolicy.ExactFollowSegments,
            request.Command.ResolutionChangePolicy);
        var media = Assert.IsType<
            VRRecorder.Application.Recording.RecordingMediaConfiguration>(
                request.Command.Media);
        Assert.Equal(AudioRouting.DesktopOnly, media.AudioRouting);
        Assert.Equal(-18.5, media.DesktopGainDb);
        Assert.Equal(2.5, media.MicrophoneGainDb);
        Assert.Equal(VideoQualityPreset.Standard, media.QualityPreset);
    }

    [Fact]
    public async Task ValidNonMenuFrameRateSurvivesEditingAnotherOption()
    {
        using var directory = TemporaryDirectory.Create();
        var store = new JsonFileSettingsStore(
            Path.Combine(directory.Path, "settings.json"));
        var settings = VRRecorderSettings.CreateDefault();
        await store.SaveAsync(
            settings with
            {
                Video = settings.Video with { FrameRate = 45 },
            },
            CancellationToken.None);
        var controller = new DesktopRecordingSettingsController(store);
        var draft = await controller.LoadAsync(CancellationToken.None);

        await controller.SaveAsync(
            draft with { SelfTimerSeconds = 3 },
            CancellationToken.None);

        var persisted = await store.LoadAsync(CancellationToken.None);
        Assert.Equal(45, persisted.Video.FrameRate);
        Assert.Equal(3, persisted.Recording.SelfTimerSeconds);
        Assert.DoesNotContain(
            45,
            DesktopRecordingSettingsController.SupportedFrameRates);
    }

    private sealed class UnexpectedDefaultOutputPathProvider
        : IDefaultOutputPathProvider
    {
        public OutputPath GetDefault() => throw new InvalidOperationException(
            "An absolute configured output path must not use the default provider.");
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
                $"vr-recorder-settings-workflow-{Guid.NewGuid():N}");
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
