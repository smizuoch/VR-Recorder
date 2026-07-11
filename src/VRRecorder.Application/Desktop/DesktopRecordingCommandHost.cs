using VRRecorder.Application.Audio;
using VRRecorder.Application.Compliance;
using VRRecorder.Application.Ports;
using VRRecorder.Application.Presentation;
using VRRecorder.Application.Recording;
using VRRecorder.Domain.Recording;

namespace VRRecorder.Application.Desktop;

public sealed class DesktopRecordingCommandHost
    : IAsyncDisposable,
      IComplianceFaultSink,
      IRecorderStatusSource,
      IActiveRecordingAudioCommands
{
    private const string UnexpectedInitializationFailureCode =
        "RECORDING_INITIALIZATION_FAILED";
    private readonly object _gate = new();
    private readonly object _statusGate = new();
    private readonly IDesktopRecordingRuntimeFactory _runtimeFactory;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly RecorderStatusHub _statuses = new(
        RecorderStatusSnapshot.Create(0, RecorderState.Booting));
    private Task<DesktopRecordingHostActivation>? _activationTask;
    private Task? _toggleTask;
    private Task<RecordingAudioControlState>? _audioCommandTask;
    private Task? _shutdownTask;
    private Task? _disposeTask;
    private IDesktopRecordingRuntime? _runtime;
    private IDisposable? _runtimeStatusSubscription;
    private DesktopRecordingHostState _state = DesktopRecordingHostState.Booting;
    private DesktopRecordingInitializationFailure? _failure;
    private RecordingStopReason? _shutdownReason;
    private long _statusRevision;
    private bool _terminalStatus;
    private bool _complianceFaulted;
    private bool _disposed;

    public DesktopRecordingCommandHost(
        IDesktopRecordingRuntimeFactory runtimeFactory)
    {
        ArgumentNullException.ThrowIfNull(runtimeFactory);
        _runtimeFactory = runtimeFactory;
    }

    public DesktopRecordingHostState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    public RecorderStatusSnapshot Current => _statuses.Current;

    public RecordingAudioControlState? CurrentAudioControlState =>
        Current.AudioControlState;

    public IDisposable Subscribe(Action<RecorderStatusSnapshot> subscriber) =>
        _statuses.Subscribe(subscriber);

    public Task<DesktopRecordingHostActivation> ActivateAsync(
        RecorderStartupResult startup,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(startup);
        cancellationToken.ThrowIfCancellationRequested();
        if (startup.State == RecorderState.ComplianceFault)
        {
            var complianceActivation = ActivateComplianceFaultAsync();
            return cancellationToken.CanBeCanceled
                ? complianceActivation.WaitAsync(cancellationToken)
                : complianceActivation;
        }

        if (startup.State != RecorderState.Ready)
        {
            throw new InvalidOperationException(
                $"Desktop recording cannot activate from recorder state {startup.State}.");
        }

        Task<DesktopRecordingHostActivation> activationTask;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            activationTask = _complianceFaulted
                ? ComplianceFaultActivation()
                : _activationTask ??= InitializeAsync();
        }

        return cancellationToken.CanBeCanceled
            ? activationTask.WaitAsync(cancellationToken)
            : activationTask;
    }

    public Task ToggleAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Task toggleTask;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_state != DesktopRecordingHostState.Ready || _runtime is null)
            {
                throw new DesktopRecordingUnavailableException(
                    _state,
                    _failure);
            }

            _toggleTask = _runtime.ToggleAsync(_lifetime.Token);
            toggleTask = _toggleTask;
        }

        return cancellationToken.CanBeCanceled
            ? toggleTask.WaitAsync(cancellationToken)
            : toggleTask;
    }

    public Task<RecordingAudioControlState> ExecuteAudioCommandAsync(
        RecordingAudioCommand command,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Task<RecordingAudioControlState> audioCommandTask;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_state != DesktopRecordingHostState.Ready || _runtime is null)
            {
                throw new DesktopRecordingUnavailableException(
                    _state,
                    _failure);
            }

            _audioCommandTask = ((IActiveRecordingAudioCommands)_runtime)
                .ExecuteAudioCommandAsync(command, _lifetime.Token);
            audioCommandTask = _audioCommandTask;
        }

        return cancellationToken.CanBeCanceled
            ? audioCommandTask.WaitAsync(cancellationToken)
            : audioCommandTask;
    }

    public ValueTask DisposeAsync()
    {
        bool cancelLifetime = false;
        Task disposeTask;
        lock (_gate)
        {
            if (_disposeTask is null)
            {
                _disposed = true;
                _state = DesktopRecordingHostState.Disposed;
                SelectFirstShutdownReason(
                    RecordingStopReason.ApplicationShutdown);
                cancelLifetime = true;
                _disposeTask = DisposeCoreAsync();
            }

            disposeTask = _disposeTask;
        }

        if (cancelLifetime)
        {
            CancelLifetime();
        }

        return new ValueTask(disposeTask);
    }

    public ValueTask EnterComplianceFaultAsync()
    {
        bool cancelLifetime = false;
        lock (_gate)
        {
            if (_disposed)
            {
                return _disposeTask is null
                    ? ValueTask.CompletedTask
                    : new ValueTask(_disposeTask);
            }

            if (!_complianceFaulted)
            {
                _complianceFaulted = true;
                _failure = null;
                _state = DesktopRecordingHostState.ComplianceFault;
                SelectFirstShutdownReason(
                    RecordingStopReason.ComplianceFault);
                cancelLifetime = true;
            }
        }

        if (cancelLifetime)
        {
            PublishStatus(RecorderState.ComplianceFault);
            CancelLifetime();
        }

        return new ValueTask(EnsureShutdownStarted());
    }

    private async Task<DesktopRecordingHostActivation>
        ActivateComplianceFaultAsync()
    {
        await EnterComplianceFaultAsync().ConfigureAwait(false);
        lock (_gate)
        {
            return _disposed
                ? new DesktopRecordingHostActivation(
                    DesktopRecordingHostState.Disposed,
                    Failure: null)
                : ComplianceFaultActivationResult();
        }
    }

    private async Task<DesktopRecordingHostActivation> InitializeAsync()
    {
        IDesktopRecordingRuntime runtime;
        try
        {
            runtime = await _runtimeFactory
                .InitializeAsync(_lifetime.Token)
                .ConfigureAwait(false);
            ArgumentNullException.ThrowIfNull(runtime);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            lock (_gate)
            {
                return TerminalActivationAfterShutdown();
            }
        }
        catch (Exception exception)
        {
            var code = exception is DesktopRecordingInitializationException failure
                ? failure.Code
                : UnexpectedInitializationFailureCode;
            DesktopRecordingHostActivation activation;
            lock (_gate)
            {
                if (_disposed)
                {
                    return new DesktopRecordingHostActivation(
                        DesktopRecordingHostState.Disposed,
                        Failure: null);
                }

                if (_complianceFaulted)
                {
                    return ComplianceFaultActivationResult();
                }

                _failure = new DesktopRecordingInitializationFailure(
                    code,
                    exception.Message);
                _state = DesktopRecordingHostState.InitializationFailed;
                activation = new DesktopRecordingHostActivation(
                    _state,
                    _failure);
            }

            PublishStatus(RecorderState.Faulted);
            return activation;
        }

        lock (_gate)
        {
            _runtime = runtime;
            if (_disposed)
            {
                return new DesktopRecordingHostActivation(
                    DesktopRecordingHostState.Disposed,
                    Failure: null);
            }

            if (_complianceFaulted)
            {
                return ComplianceFaultActivationResult();
            }

            _state = DesktopRecordingHostState.Ready;
            _runtimeStatusSubscription = runtime.Subscribe(status =>
                PublishStatus(status));
            return new DesktopRecordingHostActivation(
                _state,
                Failure: null);
        }
    }

    private async Task DisposeCoreAsync()
    {
        await Task.Yield();
        try
        {
            await EnsureShutdownStarted().ConfigureAwait(false);
        }
        finally
        {
            _statuses.Dispose();
            _lifetime.Dispose();
        }
    }

    private Task EnsureShutdownStarted()
    {
        lock (_gate)
        {
            var reason = _shutdownReason ?? throw new InvalidOperationException(
                "Desktop recording shutdown has no terminal reason.");
            return _shutdownTask ??= ShutdownCoreAsync(reason);
        }
    }

    private void SelectFirstShutdownReason(RecordingStopReason reason)
    {
        _shutdownReason ??= reason;
    }

    private async Task ShutdownCoreAsync(RecordingStopReason reason)
    {
        await Task.Yield();
        Task<DesktopRecordingHostActivation>? activationTask;
        lock (_gate)
        {
            activationTask = _activationTask;
        }

        await ObserveWithoutInterruptingShutdownAsync(activationTask)
            .ConfigureAwait(false);

        Task? toggleTask;
        Task? audioCommandTask;
        IDesktopRecordingRuntime? runtime;
        lock (_gate)
        {
            toggleTask = _toggleTask;
            audioCommandTask = _audioCommandTask;
            runtime = _runtime;
        }

        await ObserveWithoutInterruptingShutdownAsync(toggleTask)
            .ConfigureAwait(false);
        await ObserveWithoutInterruptingShutdownAsync(audioCommandTask)
            .ConfigureAwait(false);
        if (runtime is not null)
        {
            try
            {
                await runtime.ShutdownAsync(reason).ConfigureAwait(false);
            }
            finally
            {
                IDisposable? subscription;
                lock (_gate)
                {
                    subscription = _runtimeStatusSubscription;
                    _runtimeStatusSubscription = null;
                }

                subscription?.Dispose();
            }
        }
    }

    private void PublishStatus(RecorderState state) =>
        PublishStatus(RecorderStatusSnapshot.Create(0, state));

    private void PublishStatus(RecorderStatusSnapshot status)
    {
        lock (_statusGate)
        {
            if (_terminalStatus ||
                (_statuses.Current.State == status.State &&
                 _statuses.Current.AudioControlState ==
                 status.AudioControlState))
            {
                return;
            }

            _terminalStatus = status.State is
                RecorderState.Faulted or RecorderState.ComplianceFault;
            _statuses.TryPublish(RecorderStatusSnapshot.Create(
                checked(++_statusRevision),
                status.State,
                status.AudioControlState));
        }
    }

    private void CancelLifetime()
    {
        try
        {
            _lifetime.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // A concurrent DisposeAsync already completed the same shutdown.
        }
    }

    private static async Task ObserveWithoutInterruptingShutdownAsync(
        Task? operation)
    {
        if (operation is null)
        {
            return;
        }

        try
        {
            await operation.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Shutdown must still dispose the runtime after a canceled or failed command.
        }
    }

    private static Task<DesktopRecordingHostActivation>
        ComplianceFaultActivation() =>
        Task.FromResult(ComplianceFaultActivationResult());

    private static DesktopRecordingHostActivation
        ComplianceFaultActivationResult() =>
        new(
            DesktopRecordingHostState.ComplianceFault,
            Failure: null);

    private DesktopRecordingHostActivation TerminalActivationAfterShutdown() =>
        _disposed
            ? new DesktopRecordingHostActivation(
                DesktopRecordingHostState.Disposed,
                Failure: null)
            : ComplianceFaultActivationResult();
}
