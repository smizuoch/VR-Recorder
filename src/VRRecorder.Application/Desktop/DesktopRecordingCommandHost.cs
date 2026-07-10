using VRRecorder.Application.Compliance;
using VRRecorder.Domain.Recording;

namespace VRRecorder.Application.Desktop;

public sealed class DesktopRecordingCommandHost : IAsyncDisposable
{
    private const string UnexpectedInitializationFailureCode =
        "RECORDING_INITIALIZATION_FAILED";
    private readonly object _gate = new();
    private readonly IDesktopRecordingRuntimeFactory _runtimeFactory;
    private readonly CancellationTokenSource _lifetime = new();
    private Task<DesktopRecordingHostActivation>? _activationTask;
    private Task? _toggleTask;
    private IDesktopRecordingRuntime? _runtime;
    private DesktopRecordingHostState _state = DesktopRecordingHostState.Booting;
    private DesktopRecordingInitializationFailure? _failure;
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
        Task<DesktopRecordingHostActivation> activationTask;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _activationTask ??= startup.State switch
            {
                RecorderState.Ready => InitializeAsync(),
                RecorderState.ComplianceFault => CompleteComplianceFault(),
                _ => throw new InvalidOperationException(
                    $"Desktop recording cannot activate from recorder state {startup.State}."),
            };
            activationTask = _activationTask;
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
        IDesktopRecordingRuntime? runtime;
        Task<DesktopRecordingHostActivation>? activationTask;
        Task? toggleTask;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _state = DesktopRecordingHostState.Disposed;
            runtime = _runtime;
            activationTask = _activationTask;
            toggleTask = _toggleTask;
        }

        _lifetime.Cancel();
        if (activationTask is not null)
        {
            try
            {
                await activationTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (
                _lifetime.IsCancellationRequested)
            {
            }
        }

        if (toggleTask is not null)
        {
            try
            {
                await toggleTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
            {
            }
        }

        if (runtime is not null)
        {
            await runtime.DisposeAsync().ConfigureAwait(false);
        }

        _lifetime.Dispose();
    }

    private Task<DesktopRecordingHostActivation> CompleteComplianceFault()
    {
        _state = DesktopRecordingHostState.ComplianceFault;
        return Task.FromResult(new DesktopRecordingHostActivation(
            _state,
            Failure: null));
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
            throw;
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

                _failure = new DesktopRecordingInitializationFailure(
                    code,
                    exception.Message);
                _state = DesktopRecordingHostState.InitializationFailed;
                return new DesktopRecordingHostActivation(_state, _failure);
            }
        }

        lock (_gate)
        {
            if (!_disposed)
            {
                _runtime = runtime;
                _state = DesktopRecordingHostState.Ready;
                return new DesktopRecordingHostActivation(
                    _state,
                    Failure: null);
            }
        }

        await runtime.DisposeAsync().ConfigureAwait(false);
        return new DesktopRecordingHostActivation(
            DesktopRecordingHostState.Disposed,
            Failure: null);
    }
}
