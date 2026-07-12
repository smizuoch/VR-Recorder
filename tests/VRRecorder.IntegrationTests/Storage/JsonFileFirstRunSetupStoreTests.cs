using VRRecorder.Application.Setup;
using VRRecorder.Infrastructure.Storage;

namespace VRRecorder.IntegrationTests.Storage;

public sealed class JsonFileFirstRunSetupStoreTests
{
    [Fact]
    public async Task MissingDocumentIsIncompleteAndSaveRoundTripsAtomically()
    {
        using var directory = TemporaryDirectory.Create();
        var path = Path.Combine(directory.Path, "first-run-setup.json");
        using var store = new JsonFileFirstRunSetupStore(path);

        Assert.Null(await store.LoadAsync(CancellationToken.None));

        var progress = new FirstRunSetupProgress(
            FirstRunSetupController.CurrentSetupVersion,
            [
                FirstRunSetupStep.SteamVrDetection,
                FirstRunSetupStep.VrChatOscDetection,
            ]);
        await store.SaveAsync(progress, CancellationToken.None);

        Assert.Equal(
            """
            {
              "schemaVersion": 1,
              "setupVersion": 1,
              "completedSteps": [
                "steamVrDetection",
                "vrChatOscDetection"
              ]
            }
            """ + "\n",
            await File.ReadAllTextAsync(path));
        Assert.Equal(
            progress,
            await store.LoadAsync(CancellationToken.None));
        Assert.DoesNotContain(
            Directory.GetFiles(directory.Path),
            file => Path.GetFileName(file).Contains(
                ".tmp-",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task UnexpectedPropertyIsBackedUpAndCannotClaimCompletion()
    {
        using var directory = TemporaryDirectory.Create();
        var path = Path.Combine(directory.Path, "first-run-setup.json");
        await File.WriteAllTextAsync(
            path,
            """
            {
              "schemaVersion": 1,
              "setupVersion": 1,
              "completedSteps": ["steamVrDetection"],
              "complete": true
            }
            """);
        using var store = new JsonFileFirstRunSetupStore(path);

        var loaded = await store.LoadAsync(CancellationToken.None);

        Assert.Null(loaded);
        Assert.False(File.Exists(path));
        var backup = Assert.Single(Directory.GetFiles(
            directory.Path,
            "first-run-setup.corrupt-*.json"));
        Assert.Contains("complete", await File.ReadAllTextAsync(backup));
    }

    [Fact]
    public async Task InvalidStepOrderIsBackedUpAndCannotSkipSetup()
    {
        using var directory = TemporaryDirectory.Create();
        var path = Path.Combine(directory.Path, "first-run-setup.json");
        await File.WriteAllTextAsync(
            path,
            """
            {
              "schemaVersion": 1,
              "setupVersion": 1,
              "completedSteps": ["encoderSelfTest"]
            }
            """);
        using var store = new JsonFileFirstRunSetupStore(path);

        Assert.Null(await store.LoadAsync(CancellationToken.None));
        Assert.Single(Directory.GetFiles(
            directory.Path,
            "first-run-setup.corrupt-*.json"));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path) => Path = path;

        public string Path { get; }

        public static TemporaryDirectory Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"vr-recorder-setup-tests-{Guid.NewGuid():N}");
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
