using VRRecorder.Application.Ports;
using VRRecorder.Application.Storage;
using VRRecorder.Infrastructure.Storage;

namespace VRRecorder.IntegrationTests.Storage;

public sealed class RecordingFileFinalizationIntegrationTests
{
    [Fact]
    [Trait("Scenario", "IT-022")]
    public async Task ExistingFinalIsPreservedAndNumberedFinalIsPublished()
    {
        using var directory = TemporaryDirectory.Create();
        var temporaryPath = Path.Combine(directory.Path, "take.recording.mp4");
        var finalPath = Path.Combine(directory.Path, "take.mp4");
        var numberedPath = Path.Combine(directory.Path, "take_002.mp4");
        byte[] existingContent = [0x01, 0x02, 0x03];
        byte[] newContent = [0x04, 0x05, 0x06];
        await File.WriteAllBytesAsync(finalPath, existingContent);
        await File.WriteAllBytesAsync(temporaryPath, newContent);
        var savedSink = new ReopeningSavedSink(newContent);
        var useCase = new RecordingFileFinalizationUseCase(
            new SameDirectoryAtomicRecordingFileFinalizer(),
            new ReopeningValidator(newContent),
            new UnexpectedRecoveryStore(),
            savedSink);

        var result = await useCase.ExecuteAsync(
            new PendingRecording(temporaryPath, finalPath),
            CancellationToken.None);

        var saved = Assert.IsType<RecordingFinalizationResult.Saved>(result);
        Assert.Equal(Path.GetFullPath(numberedPath), saved.Recording.FinalPath);
        Assert.Equal(existingContent, await File.ReadAllBytesAsync(finalPath));
        Assert.Equal(newContent, await File.ReadAllBytesAsync(numberedPath));
        Assert.False(File.Exists(temporaryPath));
        Assert.Equal(1, savedSink.CallCount);
    }

    [Fact]
    [Trait("Scenario", "IT-022")]
    public async Task ValidPendingFileIsMovedAndReopenedBeforeSaved()
    {
        using var directory = TemporaryDirectory.Create();
        var temporaryPath = Path.Combine(directory.Path, "take.recording.mp4");
        var finalPath = Path.Combine(directory.Path, "take.mp4");
        byte[] expectedContent = [0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70];
        await File.WriteAllBytesAsync(temporaryPath, expectedContent);
        var savedSink = new ReopeningSavedSink(expectedContent);
        var useCase = new RecordingFileFinalizationUseCase(
            new SameDirectoryAtomicRecordingFileFinalizer(),
            new ReopeningValidator(expectedContent),
            new UnexpectedRecoveryStore(),
            savedSink);

        var result = await useCase.ExecuteAsync(
            new PendingRecording(temporaryPath, finalPath),
            CancellationToken.None);

        var saved = Assert.IsType<RecordingFinalizationResult.Saved>(result);
        Assert.Equal(Path.GetFullPath(finalPath), saved.Recording.FinalPath);
        Assert.False(File.Exists(temporaryPath));
        Assert.True(File.Exists(finalPath));
        Assert.Equal(1, savedSink.CallCount);
    }

    private sealed class ReopeningValidator : IRecordingFileValidator
    {
        private readonly byte[] _expectedContent;

        public ReopeningValidator(byte[] expectedContent)
        {
            _expectedContent = expectedContent;
        }

        public async Task<RecordingFileValidation> ValidateAsync(
            FinalizedRecording recording,
            CancellationToken cancellationToken)
        {
            var actual = await File.ReadAllBytesAsync(
                recording.FinalPath,
                cancellationToken);
            return actual.SequenceEqual(_expectedContent)
                ? RecordingFileValidation.Valid
                : RecordingFileValidation.Invalid;
        }
    }

    private sealed class ReopeningSavedSink : ISavedRecordingSink
    {
        private readonly byte[] _expectedContent;

        public ReopeningSavedSink(byte[] expectedContent)
        {
            _expectedContent = expectedContent;
        }

        public int CallCount { get; private set; }

        public async Task PublishAsync(
            FinalizedRecording recording,
            CancellationToken cancellationToken)
        {
            var actual = await File.ReadAllBytesAsync(
                recording.FinalPath,
                cancellationToken);
            Assert.Equal(_expectedContent, actual);
            CallCount++;
        }
    }

    private sealed class UnexpectedRecoveryStore : IRecordingRecoveryStore
    {
        public Task QuarantineAsync(
            FinalizedRecording recording,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Recovery was not expected.");
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
                $"vr-recorder-storage-tests-{Guid.NewGuid():N}");
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
