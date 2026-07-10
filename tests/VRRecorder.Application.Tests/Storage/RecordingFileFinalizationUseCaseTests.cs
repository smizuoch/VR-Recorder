using VRRecorder.Application.Storage;
using VRRecorder.Application.Tests.TestDoubles;

namespace VRRecorder.Application.Tests.Storage;

public sealed class RecordingFileFinalizationUseCaseTests
{
    [Fact]
    public async Task SavedIsNotPublishedBeforeFinalRenameCompletes()
    {
        var finalizer = new ControllableRecordingFileFinalizer();
        var savedRecordings = new FakeSavedRecordingSink();
        var useCase = new RecordingFileFinalizationUseCase(
            finalizer,
            savedRecordings);
        var pending = new PendingRecording(
            "recording.recording.mp4",
            "recording.mp4");

        var execution = useCase.ExecuteAsync(pending, CancellationToken.None);
        await finalizer.WaitUntilRequestedAsync();

        Assert.Empty(savedRecordings.Recordings);

        var finalized = new FinalizedRecording("recording.mp4");
        finalizer.Complete(finalized);
        var result = await execution;

        Assert.Equal(finalized, result);
        Assert.Equal(finalized, Assert.Single(savedRecordings.Recordings));
    }
}
