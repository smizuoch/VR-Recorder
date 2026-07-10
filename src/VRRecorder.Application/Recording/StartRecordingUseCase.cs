using VRRecorder.Application.Ports;
using VRRecorder.Domain.Storage;

namespace VRRecorder.Application.Recording;

public sealed class StartRecordingUseCase
{
    private static readonly TimeSpan SignalTimeout = TimeSpan.FromSeconds(1.5);
    private readonly IVideoSignalGateway _videoSignalGateway;
    private readonly ICountdownTimer _countdownTimer;
    private readonly IRecordingFileReservation _fileReservation;
    private readonly IWallClock _wallClock;
    private readonly IStorageSpaceProbe _storageSpaceProbe;
    private readonly IRecordingEngine _recordingEngine;
    private readonly IRecordingStorageMonitor _storageMonitor;
    private readonly AutoStopScheduler _autoStopScheduler;

    public StartRecordingUseCase(
        IVideoSignalGateway videoSignalGateway,
        ICountdownTimer countdownTimer,
        IRecordingFileReservation fileReservation,
        IWallClock wallClock,
        IStorageSpaceProbe storageSpaceProbe,
        IRecordingEngine recordingEngine,
        IRecordingStorageMonitor storageMonitor,
        AutoStopScheduler autoStopScheduler)
    {
        ArgumentNullException.ThrowIfNull(videoSignalGateway);
        ArgumentNullException.ThrowIfNull(countdownTimer);
        ArgumentNullException.ThrowIfNull(fileReservation);
        ArgumentNullException.ThrowIfNull(wallClock);
        ArgumentNullException.ThrowIfNull(storageSpaceProbe);
        ArgumentNullException.ThrowIfNull(recordingEngine);
        ArgumentNullException.ThrowIfNull(storageMonitor);
        ArgumentNullException.ThrowIfNull(autoStopScheduler);

        _videoSignalGateway = videoSignalGateway;
        _countdownTimer = countdownTimer;
        _fileReservation = fileReservation;
        _wallClock = wallClock;
        _storageSpaceProbe = storageSpaceProbe;
        _recordingEngine = recordingEngine;
        _storageMonitor = storageMonitor;
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

        var availableSpace = await _storageSpaceProbe
            .MeasureAsync(command.OutputPath, cancellationToken)
            .ConfigureAwait(false);
        if (!StorageCapacityPolicy.CanStart(availableSpace))
        {
            return new StartRecordingResult.InsufficientStorage(availableSpace);
        }

        var startedAt = new RecordingSessionTimestamp(_wallClock.LocalNow);
        var descriptor = new RecordingFileDescriptor(
            startedAt,
            signal.Width,
            signal.Height,
            command.FrameRate,
            SegmentNumber: 1);
        var output = await _fileReservation
            .ReserveAsync(
                command.OutputPath,
                descriptor,
                cancellationToken)
            .ConfigureAwait(false);
        var plan = new RecordingPlan(
            signal,
            output,
            startedAt,
            command.FrameRate);
        var handle = await _recordingEngine
            .StartAsync(plan, cancellationToken)
            .ConfigureAwait(false);
        var autoStopCompletion = _autoStopScheduler.OnFirstPacketCommittedAsync(
            handle,
            command.AutoStop,
            cancellationToken);
        var storageMonitoringCompletion = _storageMonitor.RunAsync(
            handle,
            command.OutputPath,
            cancellationToken);

        return new StartRecordingResult.Started(
            handle,
            autoStopCompletion,
            storageMonitoringCompletion);
    }
}
