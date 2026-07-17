using System.Text;
using VRRecorder.Application.Ports;
using VRRecorder.Application.Settings;
using VRRecorder.Infrastructure.Storage;

namespace VRRecorder.IntegrationTests.Storage;

public sealed class JsonFileSettingsStoreTests
{
    [Fact]
    public async Task CorruptDocumentIsBackedUpBeforeDefaultsAreReturned()
    {
        using var directory = TemporaryDirectory.Create();
        var settingsPath = Path.Combine(directory.Path, "settings.json");
        byte[] corruptContent = "{ invalid json"u8.ToArray();
        await File.WriteAllBytesAsync(settingsPath, corruptContent);
        var clock = new FixedWallClock(new DateTimeOffset(
            2026,
            7,
            10,
            12,
            34,
            56,
            TimeSpan.FromHours(9)));
        var store = new JsonFileSettingsStore(settingsPath, clock);

        var loaded = await store.LoadAsync(CancellationToken.None);

        Assert.Equivalent(
            VRRecorderSettings.CreateDefault(),
            loaded,
            strict: true);
        Assert.False(File.Exists(settingsPath));
        var backupPath = Path.Combine(
            directory.Path,
            "settings.corrupt-20260710T033456000Z.json");
        Assert.Equal(corruptContent, await File.ReadAllBytesAsync(backupPath));
    }

    [Fact]
    public async Task DefaultsRoundTripAsDeterministicAtomicSchemaV3Json()
    {
        using var directory = TemporaryDirectory.Create();
        var settingsPath = Path.Combine(directory.Path, "settings.json");
        var store = new JsonFileSettingsStore(settingsPath);
        var expected = VRRecorderSettings.CreateDefault();

        await store.SaveAsync(expected, CancellationToken.None);
        var firstBytes = await File.ReadAllBytesAsync(settingsPath);
        var loaded = await store.LoadAsync(CancellationToken.None);
        await store.SaveAsync(loaded, CancellationToken.None);
        var secondBytes = await File.ReadAllBytesAsync(settingsPath);

        Assert.Equivalent(expected, loaded, strict: true);
        Assert.Equal(firstBytes, secondBytes);
        var json = Encoding.UTF8.GetString(firstBytes);
        Assert.Contains("\"schemaVersion\": 3", json, StringComparison.Ordinal);
        Assert.Contains("\"placementProfiles\": []", json, StringComparison.Ordinal);
        Assert.Contains("\"hapticsEnabled\": true", json, StringComparison.Ordinal);
        Assert.Contains(
            "\"hapticFrequencyHertz\": 120",
            json,
            StringComparison.Ordinal);
        Assert.Contains("\"hapticAmplitude\": 0.65", json, StringComparison.Ordinal);
        Assert.Contains(
            "\"outputFolder\": \"knownfolder:Downloads\"",
            json,
            StringComparison.Ordinal);
        Assert.Contains("\"frameRate\": 30", json, StringComparison.Ordinal);
        Assert.Contains("\"encoder\": \"Auto\"", json, StringComparison.Ordinal);
        Assert.Contains("\"routing\": \"Mixed\"", json, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFiles(
            directory.Path,
            "*.tmp-*",
            SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task SchemaRejectionLeavesExistingDocumentUntouched()
    {
        using var directory = TemporaryDirectory.Create();
        var settingsPath = Path.Combine(directory.Path, "settings.json");
        byte[] originalDocument = "original-document"u8.ToArray();
        await File.WriteAllBytesAsync(settingsPath, originalDocument);
        var validator = new RejectingSettingsJsonSchemaValidator();
        var store = new JsonFileSettingsStore(
            settingsPath,
            new FixedWallClock(DateTimeOffset.UnixEpoch),
            validator);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            store.SaveAsync(
                VRRecorderSettings.CreateDefault(),
                CancellationToken.None));

        Assert.Equal("schema rejected", exception.Message);
        Assert.Equal(1, validator.ValidationCount);
        Assert.Equal(
            originalDocument,
            await File.ReadAllBytesAsync(settingsPath));
        Assert.Empty(Directory.EnumerateFiles(
            directory.Path,
            "*.tmp-*",
            SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task SchemaV1GlobalPlacementMigratesToSchemaV3Fallback()
    {
        using var directory = TemporaryDirectory.Create();
        var settingsPath = Path.Combine(directory.Path, "settings.json");
        await File.WriteAllTextAsync(
            settingsPath,
            """
            {
              "schemaVersion": 1,
              "recording": {
                "outputFolder": "knownfolder:Downloads",
                "selfTimerSeconds": 0,
                "autoStopSeconds": null,
                "resolutionChangePolicy": "SingleFileFit"
              },
              "video": {
                "frameRate": 30,
                "encoder": "Auto",
                "qualityPreset": "High",
                "codec": "H264"
              },
              "audio": {
                "routing": "Mixed",
                "desktopEndpointId": "default-render",
                "microphoneEndpointId": "default-capture",
                "desktopGainDb": -6,
                "microphoneGainDb": -6
              },
              "vr": {
                "hand": "Right",
                "placementMode": "WorldPin",
                "transform": {
                  "position": [1, 2, 3],
                  "rotationEuler": [4, 5, 6]
                }
              },
              "osc": {
                "autoDiscover": true,
                "fallbackHost": "127.0.0.1",
                "fallbackSendPort": 9000,
                "fallbackReceivePort": 9001
              }
            }
            """);
        var store = new JsonFileSettingsStore(settingsPath);

        var migrated = await store.LoadAsync(CancellationToken.None);

        Assert.Equal(3, migrated.SchemaVersion);
        Assert.Equal(VrHand.Right, migrated.Vr.Hand);
        Assert.Equal(OverlayPlacementMode.WorldPin, migrated.Vr.PlacementMode);
        Assert.Equal([1d, 2d, 3d], migrated.Vr.Transform.Position);
        Assert.Equal([4d, 5d, 6d], migrated.Vr.Transform.RotationEuler);
        Assert.Empty(migrated.Vr.PlacementProfiles);
        Assert.True(migrated.Vr.HapticsEnabled);
        Assert.Equal(120f, migrated.Vr.HapticFrequencyHertz);
        Assert.Equal(0.65f, migrated.Vr.HapticAmplitude);
        Assert.True(File.Exists(settingsPath));
        Assert.Empty(Directory.EnumerateFiles(
            directory.Path,
            "*.corrupt-*",
            SearchOption.TopDirectoryOnly));

        await store.SaveAsync(migrated, CancellationToken.None);
        var persisted = await File.ReadAllTextAsync(settingsPath);
        Assert.Contains("\"schemaVersion\": 3", persisted, StringComparison.Ordinal);
        Assert.Contains("\"placementProfiles\": []", persisted, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SchemaV2PlacementProfilesMigrateToSchemaV3HapticDefaults()
    {
        using var directory = TemporaryDirectory.Create();
        var settingsPath = Path.Combine(directory.Path, "settings.json");
        var schemaV2 = """
            {
              "schemaVersion": 2,
              "recording": {
                "outputFolder": "knownfolder:Downloads",
                "selfTimerSeconds": 0,
                "autoStopSeconds": null,
                "resolutionChangePolicy": "SingleFileFit"
              },
              "video": {
                "frameRate": 30,
                "encoder": "Auto",
                "qualityPreset": "High",
                "codec": "H264"
              },
              "audio": {
                "routing": "Mixed",
                "desktopEndpointId": "default-render",
                "microphoneEndpointId": "default-capture",
                "desktopGainDb": -6,
                "microphoneGainDb": -6
              },
              "vr": {
                "hand": "Left",
                "placementMode": "WristDock",
                "transform": {
                  "position": [0.03, 0.05, -0.08],
                  "rotationEuler": [25, 0, 10]
                },
                "placementProfiles": []
              },
              "osc": {
                "autoDiscover": true,
                "fallbackHost": "127.0.0.1",
                "fallbackSendPort": 9000,
                "fallbackReceivePort": 9001
              },
              "uiLocale": "Japanese"
            }
            """;
        await File.WriteAllTextAsync(settingsPath, schemaV2);
        var store = new JsonFileSettingsStore(settingsPath);

        var migrated = await store.LoadAsync(CancellationToken.None);

        Assert.Equal(3, migrated.SchemaVersion);
        Assert.Equal(UiLocale.Japanese, migrated.UiLocale);
        Assert.Empty(migrated.Vr.PlacementProfiles);
        Assert.True(migrated.Vr.HapticsEnabled);
        Assert.Equal(120f, migrated.Vr.HapticFrequencyHertz);
        Assert.Equal(0.65f, migrated.Vr.HapticAmplitude);
        Assert.Empty(Directory.EnumerateFiles(
            directory.Path,
            "*.corrupt-*",
            SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task MissingDocumentLoadsDesignDefaultsWithoutCreatingAFile()
    {
        using var directory = TemporaryDirectory.Create();
        var settingsPath = Path.Combine(directory.Path, "settings.json");
        var store = new JsonFileSettingsStore(settingsPath);

        var loaded = await store.LoadAsync(CancellationToken.None);

        Assert.Equivalent(
            VRRecorderSettings.CreateDefault(),
            loaded,
            strict: true);
        Assert.False(File.Exists(settingsPath));
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
                $"vr-recorder-settings-tests-{Guid.NewGuid():N}");
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

    private sealed class FixedWallClock : IWallClock
    {
        public FixedWallClock(DateTimeOffset localNow)
        {
            LocalNow = localNow;
        }

        public DateTimeOffset LocalNow { get; }
    }

    private sealed class RejectingSettingsJsonSchemaValidator
        : ISettingsJsonSchemaValidator
    {
        public int ValidationCount { get; private set; }

        public void Validate(ReadOnlyMemory<byte> documentBytes)
        {
            ValidationCount++;
            throw new InvalidDataException("schema rejected");
        }
    }
}
