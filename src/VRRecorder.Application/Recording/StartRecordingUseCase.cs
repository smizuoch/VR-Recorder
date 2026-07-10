using VRRecorder.Application.Ports;

namespace VRRecorder.Application.Recording;

public sealed class StartRecordingUseCase
{
    private static readonly TimeSpan SignalTimeout = TimeSpan.FromSeconds(1.5);
    private readonly IVideoSignalGateway _videoSignalGateway;
    private readonly ICountdownTimer _countdownTimer;
    private readonly IRecordingEngine _recordingEngine;
    private readonly AutoStopScheduler _autoStopScheduler;

    public StartRecordingUseCase(
        IVideoSignalGateway videoSignalGateway,
        ICountdownTimer countdownTimer,
        IRecordingEngine recordingEngine,
        AutoStopScheduler autoStopScheduler)
    {
        ArgumentNullException.ThrowIfNull(videoSignalGateway);
        ArgumentNullException.ThrowIfNull(countdownTimer);
        ArgumentNullException.ThrowIfNull(recordingEngine);
        ArgumentNullException.ThrowIfNull(autoStopScheduler);

        _videoSignalGateway = videoSignalGateway;
        _countdownTimer = countdownTimer;
        _recordingEngine = recordingEngine;
        _autoStopScheduler = autoStopScheduler;
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

        if (command.SelfTimer.IsEnabled)
        {
            await _countdownTimer
                .WaitAsync(command.SelfTimer, cancellationToken)
                .ConfigureAwait(false);
        }

        var handle = await _recordingEngine
            .StartAsync(new RecordingPlan(signal), cancellationToken)
            .ConfigureAwait(false);
        var autoStopCompletion = _autoStopScheduler.OnFirstPacketCommittedAsync(
            handle,
            command.AutoStop,
            cancellationToken);

        return new StartRecordingResult.Started(handle, autoStopCompletion);
    }
}
