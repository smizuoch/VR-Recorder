using VRRecorder.Application.Ports;

namespace VRRecorder.Application.Recording;

public sealed class StartRecordingUseCase
{
    private static readonly TimeSpan SignalTimeout = TimeSpan.FromSeconds(1.5);
    private readonly IVideoSignalGateway _videoSignalGateway;
    private readonly IRecordingEngine _recordingEngine;

    public StartRecordingUseCase(
        IVideoSignalGateway videoSignalGateway,
        IRecordingEngine recordingEngine)
    {
        ArgumentNullException.ThrowIfNull(videoSignalGateway);
        ArgumentNullException.ThrowIfNull(recordingEngine);

        _videoSignalGateway = videoSignalGateway;
        _recordingEngine = recordingEngine;
    }

    public async Task<StartRecordingResult> ExecuteAsync(
        StartRecordingCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        StableVideoSignal signal;
        try
        {
            signal = await _videoSignalGateway
                .WaitForStableSignalAsync(SignalTimeout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return new StartRecordingResult.NoSignal();
        }

        var handle = await _recordingEngine
            .StartAsync(new RecordingPlan(signal), cancellationToken)
            .ConfigureAwait(false);

        return new StartRecordingResult.Started(handle);
    }
}
