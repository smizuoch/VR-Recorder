using VRRecorder.Application.Diagnostics;

namespace VRRecorder.Application.Ports;

public interface IDiagnosticBundleExporter
{
    Task<DiagnosticBundleExport> ExportAsync(
        string destinationPath,
        CancellationToken cancellationToken);
}
