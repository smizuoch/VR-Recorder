using System.Text.Json;
using VRRecorder.Application.Settings;
using VRRecorder.Infrastructure.Storage;

namespace VRRecorder.IntegrationTests.Storage;

public sealed class SettingsJsonSchemaTests
{
    [Fact]
    public async Task PackagedV3SchemaValidatesPersistedDesignDefaults()
    {
        using var directory = TemporaryDirectory.Create();
        var settingsPath = Path.Combine(directory.Path, "settings.json");
        var store = new JsonFileSettingsStore(settingsPath);
        var defaults = VRRecorderSettings.CreateDefault();
        var settingsWithProfile = defaults with
        {
            Vr = defaults.Vr with
            {
                HapticsEnabled = false,
                HapticFrequencyHertz = 90,
                HapticAmplitude = 0.4f,
                PlacementProfiles =
                [
                    new VrOverlayPlacementProfile(
                        new VrDeviceProfile(
                            "lighthouse",
                            "index-hmd",
                            "/input/index_controller_profile.json"),
                        VrHand.Left,
                        OverlayPlacementMode.WristDock,
                        new OverlayTransform(
                            [0.03, 0.05, -0.08],
                            [25, 0, 10])),
                ],
            },
        };
        await store.SaveAsync(settingsWithProfile, CancellationToken.None);
        var schemaPath = Path.Combine(
            AppContext.BaseDirectory,
            "Schemas",
            "vr-recorder-settings-v3.schema.json");
        var legacyV1SchemaPath = Path.Combine(
            AppContext.BaseDirectory,
            "Schemas",
            "vr-recorder-settings-v1.schema.json");
        var legacyV2SchemaPath = Path.Combine(
            AppContext.BaseDirectory,
            "Schemas",
            "vr-recorder-settings-v2.schema.json");

        Assert.True(
            File.Exists(schemaPath),
            $"The packaged settings schema was not found at {schemaPath}.");
        Assert.True(
            File.Exists(legacyV1SchemaPath),
            $"The legacy settings schema was not found at {legacyV1SchemaPath}.");
        Assert.True(
            File.Exists(legacyV2SchemaPath),
            $"The legacy settings schema was not found at {legacyV2SchemaPath}.");
        Assert.Contains(
            "VRRecorder.Settings.v1.schema.json",
            typeof(JsonFileSettingsStore).Assembly.GetManifestResourceNames());
        Assert.Contains(
            "VRRecorder.Settings.v2.schema.json",
            typeof(JsonFileSettingsStore).Assembly.GetManifestResourceNames());
        Assert.Contains(
            "VRRecorder.Settings.v3.schema.json",
            typeof(JsonFileSettingsStore).Assembly.GetManifestResourceNames());
        using var schema = JsonDocument.Parse(
            await File.ReadAllBytesAsync(schemaPath));
        using var settings = JsonDocument.Parse(
            await File.ReadAllBytesAsync(settingsPath));
        var persistedVr = settings.RootElement.GetProperty("vr");
        Assert.False(persistedVr.GetProperty("hapticsEnabled").GetBoolean());
        Assert.Equal(
            90,
            persistedVr.GetProperty("hapticFrequencyHertz").GetSingle());
        Assert.Equal(
            0.4f,
            persistedVr.GetProperty("hapticAmplitude").GetSingle());

        Assert.Equal(
            "https://json-schema.org/draft/2020-12/schema",
            schema.RootElement.GetProperty("$schema").GetString());
        Assert.Equal(
            3,
            schema.RootElement
                .GetProperty("properties")
                .GetProperty("schemaVersion")
                .GetProperty("const")
                .GetInt32());
        SettingsJsonSchemaValidator.Default.Validate(
            await File.ReadAllBytesAsync(settingsPath));
    }

    [Theory]
    [InlineData("127.0.0.999")]
    [InlineData("::2")]
    public async Task ConformingValidatorRejectsNonLoopbackOrMalformedAddresses(
        string invalidAddress)
    {
        using var directory = TemporaryDirectory.Create();
        var settingsPath = Path.Combine(directory.Path, "settings.json");
        var store = new JsonFileSettingsStore(settingsPath);
        await store.SaveAsync(
            VRRecorderSettings.CreateDefault(),
            CancellationToken.None);
        var validDocument = await File.ReadAllTextAsync(settingsPath);
        var invalidDocument = validDocument.Replace(
            "127.0.0.1",
            invalidAddress,
            StringComparison.Ordinal);

        var exception = Assert.Throws<InvalidDataException>(() =>
            SettingsJsonSchemaValidator.Default.Validate(
                System.Text.Encoding.UTF8.GetBytes(invalidDocument)));

        Assert.Contains("schema v3", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConformingValidatorRejectsUnknownProperties()
    {
        using var directory = TemporaryDirectory.Create();
        var settingsPath = Path.Combine(directory.Path, "settings.json");
        var store = new JsonFileSettingsStore(settingsPath);
        await store.SaveAsync(
            VRRecorderSettings.CreateDefault(),
            CancellationToken.None);
        var validDocument = await File.ReadAllTextAsync(settingsPath);
        var invalidDocument = validDocument.Replace(
            "{\r\n  \"schemaVersion\"",
            "{\r\n  \"unexpected\": true,\r\n  \"schemaVersion\"",
            StringComparison.Ordinal);
        if (string.Equals(
                validDocument,
                invalidDocument,
                StringComparison.Ordinal))
        {
            invalidDocument = validDocument.Replace(
                "{\n  \"schemaVersion\"",
                "{\n  \"unexpected\": true,\n  \"schemaVersion\"",
                StringComparison.Ordinal);
        }

        var exception = Assert.Throws<InvalidDataException>(() =>
            SettingsJsonSchemaValidator.Default.Validate(
                System.Text.Encoding.UTF8.GetBytes(invalidDocument)));

        Assert.Contains("schema v3", exception.Message, StringComparison.Ordinal);
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
                $"vr-recorder-schema-tests-{Guid.NewGuid():N}");
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
