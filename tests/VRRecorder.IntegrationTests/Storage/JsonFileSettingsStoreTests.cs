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
    public async Task DefaultsRoundTripAsDeterministicAtomicSchemaV1Json()
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
        Assert.Contains("\"schemaVersion\": 1", json, StringComparison.Ordinal);
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
}
