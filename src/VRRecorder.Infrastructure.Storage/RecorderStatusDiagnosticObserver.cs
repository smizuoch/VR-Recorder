using System.Globalization;
using VRRecorder.Application.Ports;
using VRRecorder.Application.Presentation;
using VRRecorder.Domain.Recording;

namespace VRRecorder.Infrastructure.Storage;

public sealed class RecorderStatusDiagnosticObserver : IDisposable
{
    private readonly object _gate = new();
    private readonly RotatingJsonLinesDiagnosticLog _log;
    private readonly IWallClock _clock;
    private readonly IDisposable _subscription;
    private Task _writeTail = Task.CompletedTask;
    private bool _accepting = true;
    private bool _disposed;

    public RecorderStatusDiagnosticObserver(
        IRecorderStatusSource statuses,
        RotatingJsonLinesDiagnosticLog log,
        IWallClock clock)
    {
        ArgumentNullException.ThrowIfNull(statuses);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(clock);
        _log = log;
        _clock = clock;
        _subscription = statuses.Subscribe(Observe);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _accepting = false;
            _disposed = true;
        }

        _subscription.Dispose();
        Task writeTail;
        lock (_gate)
        {
            writeTail = _writeTail;
        }

        writeTail.GetAwaiter().GetResult();
        GC.SuppressFinalize(this);
    }

    private void Observe(RecorderStatusSnapshot status)
    {
        var entry = new DiagnosticLogEntry(
            _clock.LocalNow.ToUniversalTime(),
            Level(status.State),
            "recording.state_transition",
            new Dictionary<string, string>
            {
                ["revision"] = status.Revision.ToString(
                    CultureInfo.InvariantCulture),
                ["state"] = StateName(status.State),
            });
        lock (_gate)
        {
            if (!_accepting)
            {
                return;
            }

            _writeTail = WriteAfterAsync(_writeTail, entry);
        }
    }

    private async Task WriteAfterAsync(
        Task preceding,
        DiagnosticLogEntry entry)
    {
        try
        {
            await preceding.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Each diagnostic failure is already contained below.
        }

        try
        {
            await _log
                .WriteAsync(entry, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            System.Diagnostics.Trace.TraceWarning(
                "Recorder state diagnostic logging failed: {0}",
                exception.GetType().Name);
        }
    }

    private static DiagnosticLogLevel Level(RecorderState state) => state switch
    {
        RecorderState.SignalLost or RecorderState.NoSignal =>
            DiagnosticLogLevel.Warning,
        RecorderState.Faulted or RecorderState.ComplianceFault =>
            DiagnosticLogLevel.Error,
        _ => DiagnosticLogLevel.Information,
    };

    private static string StateName(RecorderState state) => state switch
    {
        RecorderState.Booting => "booting",
        RecorderState.Ready => "ready",
        RecorderState.Arming => "arming",
        RecorderState.Countdown => "countdown",
        RecorderState.Starting => "starting",
        RecorderState.Recording => "recording",
        RecorderState.SignalLost => "signal_lost",
        RecorderState.Stopping => "stopping",
        RecorderState.NoSignal => "no_signal",
        RecorderState.Faulted => "faulted",
        RecorderState.ComplianceFault => "compliance_fault",
        _ => throw new ArgumentOutOfRangeException(
            nameof(state),
            state,
            "The recorder state is not supported."),
    };
}
