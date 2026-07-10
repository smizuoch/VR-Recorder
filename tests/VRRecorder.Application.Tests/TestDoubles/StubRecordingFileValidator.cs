using VRRecorder.Application.Ports;
using VRRecorder.Application.Storage;

namespace VRRecorder.Application.Tests.TestDoubles;

internal sealed class StubRecordingFileValidator : IRecordingFileValidator
{
    private readonly RecordingFileValidation _result;

    public StubRecordingFileValidator(RecordingFileValidation result)
    {
        _result = result;
    }

    public Task<RecordingFileValidation> ValidateAsync(
        FinalizedRecording recording,
        CancellationToken cancellationToken) =>
        Task.FromResult(_result);
}
