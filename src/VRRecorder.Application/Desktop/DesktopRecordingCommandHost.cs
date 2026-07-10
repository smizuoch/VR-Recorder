using VRRecorder.Application.Compliance;
using VRRecorder.Application.Ports;
using VRRecorder.Domain.Recording;

namespace VRRecorder.Application.Desktop;

public sealed class DesktopRecordingCommandHost
    : IAsyncDisposable,
      IComplianceFaultSink
{
    private const string UnexpectedInitializationFailureCode =
        "RECORDING_INITIALIZATION_FAILED";
    private readonly object _gate = new();
    private readonly IDesktopRecordingRuntimeFactory _runtimeFactory;
    private readonly CancellationTokenSource _lifetime = new();
    private Task<DesktopRecordingHostActivation>? _activationTask;
    private Task? _toggleTask;
    private Task? _shutdownTask;
    private Task? _disposeTask;
    private IDesktopRecordingRuntime? _runtime;
    private DesktopRecordingHostState _state = DesktopRecordingHostState.Booting;
    private DesktopRecordingInitializationFailure? _failure;
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

            if (_toggleTask is null || _toggleTask.IsCompleted)
            {
                _toggleTask = _runtime.ToggleAsync(_lifetime.Token);
            }

            toggleTask = _toggleTask;
        }

        return cancellationToken.CanBeCanceled
            ? toggleTask.WaitAsync(cancellationToken)
            : toggleTask;
    }

    public async ValueTask DisposeAsync()
    {
        Task disposeTask;
        lock (_gate)
        {
            _disposeTask ??= DisposeCoreAsync();
            disposeTask = _disposeTask;
        }

        await disposeTask.ConfigureAwait(false);
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
                cancelLifetime = true;
            }
        }

        if (cancelLifetime)
        {
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
                return new DesktopRecordingHostActivation(_state, _failure);
            }
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
            return new DesktopRecordingHostActivation(
                _state,
                Failure: null);
        }
    }

    private async Task DisposeCoreAsync()
    {
        await Task.Yield();
        lock (_gate)
        {
            if (!_disposed)
            {
                _disposed = true;
                _state = DesktopRecordingHostState.Disposed;
            }
        }

        CancelLifetime();
        await EnsureShutdownStarted().ConfigureAwait(false);
        _lifetime.Dispose();
    }

    private Task EnsureShutdownStarted()
    {
        lock (_gate)
        {
            return _shutdownTask ??= ShutdownCoreAsync();
        }
    }

    private async Task ShutdownCoreAsync()
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
        IDesktopRecordingRuntime? runtime;
        lock (_gate)
        {
            toggleTask = _toggleTask;
            runtime = _runtime;
        }

        await ObserveWithoutInterruptingShutdownAsync(toggleTask)
            .ConfigureAwait(false);
        if (runtime is not null)
        {
            await runtime.DisposeAsync().ConfigureAwait(false);
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
