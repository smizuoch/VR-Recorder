using VRRecorder.Application.Recording;
using VRRecorder.Domain.Audio;

namespace VRRecorder.Infrastructure.Media;

public interface INativeRecordingSession
{
    string Id { get; }

    Task UpdateVideoLayoutAsync(
        RecordingVideoLayout layout,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException(
            "This native recording session does not support runtime video layout updates.");

    Task UpdateAudioRoutingAsync(
        AudioRouting routing,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException(
            "This native recording session does not support runtime audio routing updates.");

    Task<NativeRecordingSessionStatistics> GetStatisticsAsync(
        CancellationToken cancellationToken) =>
        throw new NotSupportedException(
            "This native recording session does not expose runtime statistics.");

    Task AbortAsync(CancellationToken cancellationToken);

    Task<RecordingStopResult> StopAsync(CancellationToken cancellationToken);
}
