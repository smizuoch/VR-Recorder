using VRRecorder.Application.Encoding;
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
    private readonly EncoderSelector _encoderSelector;
    private readonly IRecordingEngine _recordingEngine;
    private readonly IRecordingSessionActivator _sessionActivator;
    private readonly IRecordingStorageMonitor _storageMonitor;
    private readonly AutoStopScheduler _autoStopScheduler;

    public StartRecordingUseCase(
        IVideoSignalGateway videoSignalGateway,
        ICountdownTimer countdownTimer,
        IRecordingFileReservation fileReservation,
        IWallClock wallClock,
        IStorageSpaceProbe storageSpaceProbe,
        EncoderSelector encoderSelector,
        IRecordingEngine recordingEngine,
        IRecordingSessionActivator sessionActivator,
        IRecordingStorageMonitor storageMonitor,
        AutoStopScheduler autoStopScheduler)
    {
        ArgumentNullException.ThrowIfNull(videoSignalGateway);
        ArgumentNullException.ThrowIfNull(countdownTimer);
        ArgumentNullException.ThrowIfNull(fileReservation);
        ArgumentNullException.ThrowIfNull(wallClock);
        ArgumentNullException.ThrowIfNull(storageSpaceProbe);
        ArgumentNullException.ThrowIfNull(encoderSelector);
        ArgumentNullException.ThrowIfNull(recordingEngine);
        ArgumentNullException.ThrowIfNull(sessionActivator);
        ArgumentNullException.ThrowIfNull(storageMonitor);
        ArgumentNullException.ThrowIfNull(autoStopScheduler);

        _videoSignalGateway = videoSignalGateway;
        _countdownTimer = countdownTimer;
        _fileReservation = fileReservation;
        _wallClock = wallClock;
        _storageSpaceProbe = storageSpaceProbe;
        _encoderSelector = encoderSelector;
        _recordingEngine = recordingEngine;
        _sessionActivator = sessionActivator;
        _storageMonitor = storageMonitor;
        _autoStopScheduler = autoStopScheduler;
    }

    public async Task<StartRecordingResult> ExecuteAsync(
        StartRecordingCommand command,
        CancellationToken cancellationToken,
        IRecordingSessionCompletionSink? completionSink = null)
    {
        await CaptureVideoSignalBaselineAsync(cancellationToken)
            .ConfigureAwait(false);
        return await ExecuteAsync(
                command,
                completionSink,
                phaseSink: null,
                cancellationToken)
            .ConfigureAwait(false);
    }

    internal Task<StartRecordingResult> ExecuteAsync(
        string vrChatServiceId,
        StartRecordingCommand command,
        IRecordingSessionCompletionSink? completionSink,
        IRecordingStartPhaseSink? phaseSink,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vrChatServiceId);
        return ExecuteCoreAsync(
            vrChatServiceId,
            command,
            completionSink,
            phaseSink,
            cancellationToken);
    }

    internal Task CaptureVideoSignalBaselineAsync(
        CancellationToken cancellationToken) =>
        _videoSignalGateway.CaptureBaselineAsync(cancellationToken);

    internal async Task<StartRecordingResult> ExecuteAsync(
        StartRecordingCommand command,
        IRecordingSessionCompletionSink? completionSink,
        IRecordingStartPhaseSink? phaseSink,
        CancellationToken cancellationToken) =>
        await ExecuteCoreAsync(
                vrChatServiceId: null,
                command,
                completionSink,
                phaseSink,
                cancellationToken)
            .ConfigureAwait(false);

    private async Task<StartRecordingResult> ExecuteCoreAsync(
        string? vrChatServiceId,
        StartRecordingCommand command,
        IRecordingSessionCompletionSink? completionSink,
        IRecordingStartPhaseSink? phaseSink,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        StableVideoSignal signal;
        try
        {
            signal = await WaitForStableSignalAsync(
                    vrChatServiceId,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return new StartRecordingResult.NoSignal();
        }

        var videoLayout = RecordingVideoLayoutSession.Start(
            signal,
            command.ResolutionChangePolicy);

        if (command.SelfTimer.IsEnabled)
        {
            phaseSink?.CountdownStarted();
            await _countdownTimer
                .WaitAsync(command.SelfTimer, cancellationToken)
                .ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        phaseSink?.StartPreparationCompleted();

        var availableSpace = await _storageSpaceProbe
            .MeasureAsync(command.OutputPath, cancellationToken)
            .ConfigureAwait(false);
        if (!StorageCapacityPolicy.CanStart(availableSpace))
        {
            return new StartRecordingResult.InsufficientStorage(availableSpace);
        }

        var encoder = signal.HasDiscoveredSourceIdentity
            ? await _encoderSelector
                .SelectAsync(
                    command.EncoderPreference,
                    signal,
                    videoLayout.OutputCanvas.Width,
                    videoLayout.OutputCanvas.Height,
                    command.FrameRate,
                    cancellationToken)
                .ConfigureAwait(false)
            : await _encoderSelector
                .SelectAsync(
                    command.EncoderPreference,
                    command.GpuVendor,
                    signal,
                    videoLayout.OutputCanvas.Width,
                    videoLayout.OutputCanvas.Height,
                    command.FrameRate,
                    cancellationToken)
                .ConfigureAwait(false);

        var startedAt = new RecordingSessionTimestamp(_wallClock.LocalNow);
        var descriptor = new RecordingFileDescriptor(
            startedAt,
            videoLayout.OutputCanvas.Width,
            videoLayout.OutputCanvas.Height,
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
            command.FrameRate,
            encoder,
            videoLayout)
        {
            EncoderPreference = command.EncoderPreference,
            Media = (command.Media ?? RecordingMediaConfiguration.CreateDefault())
                .WithVideoSource(signal),
        };
        var handle = await _recordingEngine
            .StartAsync(plan, cancellationToken)
            .ConfigureAwait(false);
        _sessionActivator.Activate(
            handle,
            plan.Media.AudioRouting,
            cancellationToken,
            completionSink);
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

    private Task<StableVideoSignal> WaitForStableSignalAsync(
        string? vrChatServiceId,
        CancellationToken cancellationToken) =>
        vrChatServiceId is null
            ? _videoSignalGateway.WaitForStableSignalAsync(
                SignalTimeout,
                cancellationToken)
            : _videoSignalGateway.WaitForStableSignalAsync(
                vrChatServiceId,
                SignalTimeout,
                cancellationToken);
}
