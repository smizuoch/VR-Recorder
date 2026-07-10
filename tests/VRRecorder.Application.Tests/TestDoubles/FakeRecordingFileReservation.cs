using VRRecorder.Application.Ports;
using VRRecorder.Application.Storage;
using VRRecorder.Domain.Storage;

namespace VRRecorder.Application.Tests.TestDoubles;

internal sealed class FakeRecordingFileReservation
    : IRecordingFileReservation
{
    private readonly TaskCompletionSource _requested = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<PendingRecording> _completion = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    public OutputPath? RequestedOutputPath { get; private set; }

    public RecordingFileDescriptor? RequestedDescriptor { get; private set; }

    public int CallCount { get; private set; }

    public Task<PendingRecording> ReserveAsync(
        OutputPath outputPath,
        RecordingFileDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        CallCount++;
        RequestedOutputPath = outputPath;
        RequestedDescriptor = descriptor;
        _requested.TrySetResult();
        return _completion.Task.WaitAsync(cancellationToken);
    }

    public void Complete(PendingRecording recording) =>
        _completion.TrySetResult(recording);

    public Task WaitUntilRequestedAsync() => _requested.Task;
}
