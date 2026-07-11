namespace VRRecorder.Infrastructure.Storage;

public interface IDiagnosticLogWriter
{
    Task WriteAsync(
        DiagnosticLogEntry entry,
        CancellationToken cancellationToken);
}
