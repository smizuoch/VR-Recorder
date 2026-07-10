using System.Runtime.ExceptionServices;
using VRRecorder.Application.Camera;
using VRRecorder.Application.Ports;
using VRRecorder.Domain.Camera;
using VRRecorder.Domain.Recording;
using VRRecorder.Domain.Timing;
using VRRecorder.Domain.Video;

namespace VRRecorder.Application.Recording;

public sealed class RecordingLifecycleController : IDisposable
{
    private readonly object _stateGate = new();
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly VrChatCameraConnectionUseCase _cameraConnections;
    private readonly ICameraLeaseStore _cameraLeases;
    private readonly StartRecordingUseCase _startRecording;
    private readonly IStopRequestSink _stopRequests;
    private readonly ICameraRestoreWarningSink _cameraRestoreWarnings;
    private readonly ICameraLeaseIdentitySource? _cameraLeaseIdentities;
    private VideoSignalSupervisor? _videoSignal;
    private RecorderState _state = RecorderState.Ready;

    public RecordingLifecycleController(
        VrChatCameraConnectionUseCase cameraConnections,
        ICameraLeaseStore cameraLeases,
        StartRecordingUseCase startRecording,
        IStopRequestSink stopRequests,
        ICameraRestoreWarningSink cameraRestoreWarnings,
        ICameraLeaseIdentitySource? cameraLeaseIdentities = null)
    {
        ArgumentNullException.ThrowIfNull(cameraConnections);
        ArgumentNullException.ThrowIfNull(cameraLeases);
        ArgumentNullException.ThrowIfNull(startRecording);
        ArgumentNullException.ThrowIfNull(stopRequests);
        ArgumentNullException.ThrowIfNull(cameraRestoreWarnings);
        _cameraConnections = cameraConnections;
        _cameraLeases = cameraLeases;
        _startRecording = startRecording;
        _stopRequests = stopRequests;
        _cameraRestoreWarnings = cameraRestoreWarnings;
        _cameraLeaseIdentities = cameraLeaseIdentities;
    }

    public RecorderState State
    {
        get
        {
            lock (_stateGate)
            {
                return _state;
            }
        }
    }

    public async Task<RecordingLifecycleStartResult> StartAsync(
        string? selectedServiceId,
        StartRecordingCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        await _operationGate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            var startState = State;
            if (startState is not (
                    RecorderState.Ready or RecorderState.NoSignal))
            {
                throw new InvalidOperationException(
                    $"Recording cannot start while the lifecycle is {startState}.");
            }

            var connection = await _cameraConnections
                .ResolveAsync(selectedServiceId, cancellationToken)
                .ConfigureAwait(false);
            if (connection is not VrChatCameraConnectionResolution.Connected connected)
            {
                return new RecordingLifecycleStartResult(
                    State,
                    connection,
                    Recording: null);
            }

            CameraSnapshot cameraSnapshot;
            try
            {
                cameraSnapshot = await connected.Gateway
                    .ReadSnapshotAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (cameraSnapshot is null ||
                    (cameraSnapshot.Mode.IsKnown &&
                     !Enum.IsDefined(cameraSnapshot.Mode.Value)))
                {
                    throw new InvalidDataException(
                        "The selected VRChat camera returned an invalid snapshot.");
                }
            }
            catch (OperationCanceledException) when (
                cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                return new RecordingLifecycleStartResult(
                    State,
                    connection,
                    Recording: null,
                    new CameraSnapshotStartFailure(
                        CameraSnapshotStartFailureKind.ReadFailed,
                        connected.Candidate.ServiceId,
                        exception));
            }

            SetState(RecorderStateMachine.Transition(
                startState,
                RecorderTrigger.StartRequested));
            var camera = _cameraLeaseIdentities is null
                ? new CameraSessionController(
                    connected.Gateway,
                    _cameraLeases)
                : new CameraSessionController(
                    connected.Gateway,
                    _cameraLeases,
                    _cameraLeaseIdentities);
            CameraLease lease;
            try
            {
                lease = _cameraLeaseIdentities is null
                    ? await camera
                        .AcquireAsync(cameraSnapshot, cancellationToken)
                        .ConfigureAwait(false)
                    : await camera
                        .AcquireAsync(
                            cameraSnapshot,
                            connected.Candidate.ServiceId,
                            cancellationToken)
                        .ConfigureAwait(false);
            }
            catch (CameraAcquisitionRollbackException exception)
            {
                var warningReason =
                    exception.AcquisitionFailure is OperationCanceledException &&
                    cancellationToken.IsCancellationRequested
                        ? CameraRestoreWarningReason.StartCanceled
                        : CameraRestoreWarningReason.StartFailed;
                await PublishCameraRestoreWarningBestEffortAsync(
                        new CameraRestoreWarning(
                            warningReason,
                            exception.RestorationFailure))
                    .ConfigureAwait(false);
                SetState(RecorderState.Ready);
                ExceptionDispatchInfo.Capture(exception.AcquisitionFailure)
                    .Throw();
                throw new InvalidOperationException(
                    "Unreachable after rethrowing the camera acquisition failure.");
            }
            catch
            {
                SetState(RecorderState.Ready);
                throw;
            }

            var completionSink = new CameraSessionCompletionSink(
                this,
                camera,
                lease);
            try
            {
                var recording = await _startRecording
                    .ExecuteAsync(
                        command,
                        cancellationToken,
                        completionSink)
                    .ConfigureAwait(false);
                var state = recording switch
                {
                    StartRecordingResult.Started => RecorderStateMachine.Transition(
                        RecorderState.Starting,
                        RecorderTrigger.FirstPacketCommitted),
                    StartRecordingResult.NoSignal => RecorderStateMachine.Transition(
                        RecorderState.Arming,
                        RecorderTrigger.SignalTimeout),
                    StartRecordingResult.InsufficientStorage => RecorderState.Ready,
                    _ => throw new InvalidOperationException(
                        $"Unknown recording start result {recording.GetType().Name}."),
                };
                if (recording is StartRecordingResult.Started started)
                {
                    if (!completionSink.TryCommitStarted(() =>
                        {
                            lock (_stateGate)
                            {
                                _videoSignal = new VideoSignalSupervisor(
                                    started.Handle,
                                    _stopRequests);
                                _state = state;
                            }
                        }))
                    {
                        state = State;
                    }
                }
                else
                {
                    var warningReason = recording switch
                    {
                        StartRecordingResult.NoSignal =>
                            CameraRestoreWarningReason.NoSignal,
                        StartRecordingResult.InsufficientStorage =>
                            CameraRestoreWarningReason.InsufficientStorage,
                        _ => throw new InvalidOperationException(
                            $"Unknown non-start result {recording.GetType().Name}."),
                    };
                    await RestoreCameraBestEffortAsync(
                            camera,
                            lease,
                            warningReason)
                        .ConfigureAwait(false);
                    SetState(state);
                }

                return new RecordingLifecycleStartResult(
                    state,
                    connection,
                    recording);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    await RestoreCameraBestEffortAsync(
                            camera,
                            lease,
                            CameraRestoreWarningReason.StartCanceled)
                        .ConfigureAwait(false);
                }
                finally
                {
                    SetState(RecorderState.Ready);
                }

                throw;
            }
            catch
            {
                try
                {
                    await RestoreCameraBestEffortAsync(
                            camera,
                            lease,
                            CameraRestoreWarningReason.StartFailed)
                        .ConfigureAwait(false);
                }
                finally
                {
                    SetState(RecorderState.Ready);
                }

                throw;
            }
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task ObserveFreshVideoFrameAsync(
        VideoFrameObservation frame,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(frame);
        await _operationGate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            var supervisor = ActiveVideoSignalSupervisor();
            supervisor.ObserveFreshFrame(frame);
            if (State == RecorderState.SignalLost)
            {
                SetState(RecorderStateMachine.Transition(
                    RecorderState.SignalLost,
                    RecorderTrigger.SignalRecovered));
            }
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<VideoSignalStatus> EvaluateVideoSignalAsync(
        MonotonicTimestamp now,
        CancellationToken cancellationToken)
    {
        await _operationGate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            var status = await ActiveVideoSignalSupervisor()
                .EvaluateAsync(now, cancellationToken)
                .ConfigureAwait(false);
            if (status == VideoSignalStatus.SignalLost &&
                State == RecorderState.Recording)
            {
                SetState(RecorderStateMachine.Transition(
                    RecorderState.Recording,
                    RecorderTrigger.FreshFrameTimeout));
            }

            return status;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public void Dispose() => _operationGate.Dispose();

    private void SetState(RecorderState state)
    {
        lock (_stateGate)
        {
            _state = state;
        }
    }

    private VideoSignalSupervisor ActiveVideoSignalSupervisor()
    {
        lock (_stateGate)
        {
            return _videoSignal ?? throw new InvalidOperationException(
                "Video signal monitoring requires an active recording.");
        }
    }

    private void CompleteRecordingSession(RecorderState finalState)
    {
        lock (_stateGate)
        {
            _videoSignal = null;
            _state = finalState;
        }
    }

    private async Task RestoreCameraBestEffortAsync(
        CameraSessionController camera,
        CameraLease lease,
        CameraRestoreWarningReason warningReason)
    {
        try
        {
            await camera
                .RestoreAsync(lease, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await PublishCameraRestoreWarningBestEffortAsync(
                    new CameraRestoreWarning(warningReason, exception))
                .ConfigureAwait(false);
        }
    }

    private async Task PublishCameraRestoreWarningBestEffortAsync(
        CameraRestoreWarning warning)
    {
        try
        {
            await _cameraRestoreWarnings
                .PublishAsync(warning, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Warning delivery is secondary to camera evidence, recording
            // finalization, and lifecycle convergence.
        }
    }

    private sealed class CameraSessionCompletionSink(
        RecordingLifecycleController owner,
        CameraSessionController camera,
        CameraLease lease) : IRecordingSessionCompletionSink
    {
        private readonly object _gate = new();
        private bool _completed;
        private int _completionClaimed;

        public bool TryCommitStarted(Action commit)
        {
            ArgumentNullException.ThrowIfNull(commit);
            lock (_gate)
            {
                if (_completed)
                {
                    return false;
                }

                commit();
                return true;
            }
        }

        public async Task CompleteAsync(
            RecordingSessionCompletion completion,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(completion);
            if (Interlocked.CompareExchange(
                    ref _completionClaimed,
                    1,
                    0) != 0)
            {
                return;
            }

            await owner
                .RestoreCameraBestEffortAsync(
                    camera,
                    lease,
                    CameraRestoreWarningReason.RecordingCompleted)
                .ConfigureAwait(false);
            lock (_gate)
            {
                owner.CompleteRecordingSession(completion.FinalState);
                _completed = true;
            }
        }
    }
}
