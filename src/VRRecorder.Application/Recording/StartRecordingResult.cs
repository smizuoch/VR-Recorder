using VRRecorder.Domain.Storage;

namespace VRRecorder.Application.Recording;

public abstract record StartRecordingResult
{
    private StartRecordingResult()
    {
    }

    public sealed record Started(
        RecordingHandle Handle,
        Task AutoStopCompletion,
        Task StorageMonitoringCompletion) : StartRecordingResult;

    public sealed record NoSignal : StartRecordingResult;

    public sealed record InsufficientStorage(StorageSpace AvailableSpace)
        : StartRecordingResult;
}
