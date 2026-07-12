using System.Text.Json;
using VRRecorder.Infrastructure.Storage;

namespace VRRecorder.IntegrationTests.Storage;

public sealed class JsonFileVideoSenderSelectionStoreTests
{
    [Fact]
    public async Task PersistsServiceScopedSelectionsWithAtomicReplacement()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"vr-recorder-spout-selection-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "spout-senders.json");
        try
        {
            using (var store = new JsonFileVideoSenderSelectionStore(path))
            {
                await store.SaveAsync(
                    "service-b",
                    "sender-b1",
                    CancellationToken.None);
                await store.SaveAsync(
                    "service-a",
                    "sender-a1",
                    CancellationToken.None);
                await store.SaveAsync(
                    "service-a",
                    "sender-a2",
                    CancellationToken.None);
            }

            using var reloaded = new JsonFileVideoSenderSelectionStore(path);
            Assert.Equal(
                "sender-a2",
                await reloaded.LoadAsync("service-a", CancellationToken.None));
            Assert.Equal(
                "sender-b1",
                await reloaded.LoadAsync("service-b", CancellationToken.None));
            Assert.Null(await reloaded.LoadAsync(
                "service-missing",
                CancellationToken.None));
            Assert.Empty(Directory.EnumerateFiles(directory, "*.tmp"));
            using var document = JsonDocument.Parse(
                await File.ReadAllBytesAsync(path));
            Assert.Equal(
                1,
                document.RootElement.GetProperty("schemaVersion").GetInt32());
            Assert.Equal(
                ["service-a", "service-b"],
                document.RootElement
                    .GetProperty("selections")
                    .EnumerateObject()
                    .Select(property => property.Name));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
