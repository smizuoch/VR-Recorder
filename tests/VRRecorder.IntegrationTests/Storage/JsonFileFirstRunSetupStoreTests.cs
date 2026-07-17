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
        Assert.Equivalent(
            progress,
            await store.LoadAsync(CancellationToken.None),
            strict: true);
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

    [Theory]
    [InlineData("[]")]
    [InlineData("""
        {
          "schemaVersion": 1,
          "setupVersion": 1,
          "setupVersion": 1,
          "completedSteps": []
        }
        """)]
    [InlineData("""
        {
          "schemaVersion": 2,
          "setupVersion": 1,
          "completedSteps": []
        }
        """)]
    [InlineData("""
        {
          "schemaVersion": 1,
          "setupVersion": 1,
          "completedSteps": {}
        }
        """)]
    [InlineData("""
        {
          "schemaVersion": 1,
          "setupVersion": 1,
          "completedSteps": [null]
        }
        """)]
    [InlineData("""
        {
          "schemaVersion": 1,
          "setupVersion": 1,
          "completedSteps": ["privateUnknownStep"]
        }
        """)]
    [InlineData("""
        {
          "schemaVersion": 1,
          "setupVersion": 1,
          "completedSteps": [
            "steamVrDetection",
            "vrChatOscDetection",
            "cameraOscEndpoint",
            "microphonePrivacyAndDevice",
            "encoderSelfTest",
            "steamVrActionBinding",
            "wristOverlayPlacement",
            "testRecordingPlayback",
            "legalBundleVerification",
            "offlineLegalAccess",
            "localizationAccessibility",
            "designAssetConformance",
            "designAssetConformance"
          ]
        }
        """)]
    public async Task EveryInvalidDocumentShapeIsBackedUp(string content)
    {
        using var directory = TemporaryDirectory.Create();
        var path = Path.Combine(directory.Path, "first-run-setup.json");
        await File.WriteAllTextAsync(path, content);
        using var store = new JsonFileFirstRunSetupStore(path);

        Assert.Null(await store.LoadAsync(CancellationToken.None));

        Assert.False(File.Exists(path));
        Assert.Single(Directory.GetFiles(
            directory.Path,
            "first-run-setup.corrupt-*.json"));
    }

    [Fact]
    public async Task SaveRejectsSkippedOrExcessCompletedSteps()
    {
        using var directory = TemporaryDirectory.Create();
        var path = Path.Combine(directory.Path, "first-run-setup.json");
        using var store = new JsonFileFirstRunSetupStore(path);
        var skipped = new FirstRunSetupProgress(
            FirstRunSetupController.CurrentSetupVersion,
            [FirstRunSetupStep.VrChatOscDetection]);
        var excess = new FirstRunSetupProgress(
            FirstRunSetupController.CurrentSetupVersion,
            Enumerable.Repeat(
                FirstRunSetupStep.SteamVrDetection,
                FirstRunSetupController.RequiredSteps.Count + 1));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            store.SaveAsync(skipped, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            store.SaveAsync(excess, CancellationToken.None));
        Assert.Empty(Directory.EnumerateFileSystemEntries(directory.Path));
    }

    [Fact]
    public async Task RejectsRelativePathAndUseAfterDisposal()
    {
        Assert.Throws<ArgumentException>(() =>
            new JsonFileFirstRunSetupStore("first-run-setup.json"));
        using var directory = TemporaryDirectory.Create();
        var path = Path.Combine(directory.Path, "first-run-setup.json");
        var store = new JsonFileFirstRunSetupStore(path);
        var progress = new FirstRunSetupProgress(
            FirstRunSetupController.CurrentSetupVersion,
            []);

        store.Dispose();
        store.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            store.LoadAsync(CancellationToken.None));
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            store.SaveAsync(progress, CancellationToken.None));
    }

    [Fact]
    public async Task LinkedDocumentAndParentDirectoryAreRejected()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var directory = TemporaryDirectory.Create();
        var targetFile = Path.Combine(directory.Path, "target.json");
        var linkedFile = Path.Combine(directory.Path, "first-run-setup.json");
        await File.WriteAllTextAsync(targetFile, "outside evidence");
        File.CreateSymbolicLink(linkedFile, targetFile);
        using (var store = new JsonFileFirstRunSetupStore(linkedFile))
        {
            await Assert.ThrowsAsync<IOException>(() =>
                store.LoadAsync(CancellationToken.None));
        }

        var targetDirectory = Path.Combine(directory.Path, "target-directory");
        var linkedDirectory = Path.Combine(directory.Path, "linked-directory");
        Directory.CreateDirectory(targetDirectory);
        Directory.CreateSymbolicLink(linkedDirectory, targetDirectory);
        using var linkedParentStore = new JsonFileFirstRunSetupStore(
            Path.Combine(linkedDirectory, "first-run-setup.json"));
        var progress = new FirstRunSetupProgress(
            FirstRunSetupController.CurrentSetupVersion,
            []);

        await Assert.ThrowsAsync<IOException>(() =>
            linkedParentStore.SaveAsync(progress, CancellationToken.None));
        Assert.Equal("outside evidence", await File.ReadAllTextAsync(targetFile));
        Assert.Empty(Directory.EnumerateFileSystemEntries(targetDirectory));
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
