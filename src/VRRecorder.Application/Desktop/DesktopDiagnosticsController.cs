using VRRecorder.Application.Ports;

namespace VRRecorder.Application.Desktop;

public sealed class DesktopDiagnosticsController
{
    private readonly IDiagnosticBundleExporter _exporter;
    private readonly object _gate = new();
    private DesktopDiagnosticsState _state = new(
        0,
        DesktopDiagnosticsStatus.Idle,
        LastExport: null);
    private bool _operationActive;

    public DesktopDiagnosticsController(IDiagnosticBundleExporter exporter)
    {
        ArgumentNullException.ThrowIfNull(exporter);
        _exporter = exporter;
    }

    public DesktopDiagnosticsState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    public async Task ExportAsync(
        string destinationPath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        if (!Path.IsPathFullyQualified(destinationPath))
        {
            throw new ArgumentException(
                "The diagnostic bundle destination must be absolute.",
                nameof(destinationPath));
        }

        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_operationActive)
            {
                throw new InvalidOperationException(
                    "A diagnostic bundle export is already running.");
            }

            _operationActive = true;
            _state = new DesktopDiagnosticsState(
                checked(_state.Revision + 1),
                DesktopDiagnosticsStatus.Exporting,
                LastExport: null);
        }

        try
        {
            var result = await _exporter
                .ExportAsync(
                    Path.GetFullPath(destinationPath),
                    cancellationToken)
                .ConfigureAwait(false);
            ArgumentNullException.ThrowIfNull(result);
            SetCompletedState(
                DesktopDiagnosticsStatus.Exported,
                result);
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested)
        {
            SetCompletedState(
                DesktopDiagnosticsStatus.Idle,
                lastExport: null);
            throw;
        }
        catch
        {
            SetCompletedState(
                DesktopDiagnosticsStatus.Failed,
                lastExport: null);
            throw;
        }
    }

    private void SetCompletedState(
        DesktopDiagnosticsStatus status,
        Diagnostics.DiagnosticBundleExport? lastExport)
    {
        lock (_gate)
        {
            _operationActive = false;
            _state = new DesktopDiagnosticsState(
                checked(_state.Revision + 1),
                status,
                lastExport);
        }
    }
}
