using VRRecorder.Application.Compliance;

namespace VRRecorder.Application.Ports;

public interface ILegalCatalogReader
{
    Task<LegalCatalogReadResult> ReadAsync(
        CancellationToken cancellationToken);

    Task<LegalTextReadResult> ReadDocumentAsync(
        string componentId,
        LegalDocumentReference reference,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(componentId);
        ArgumentNullException.ThrowIfNull(reference);
        cancellationToken.ThrowIfCancellationRequested();
        if (reference.Kind == LegalDocumentKind.License)
        {
            return ReadLicenseTextAsync(componentId, cancellationToken);
        }

        return Task.FromResult<LegalTextReadResult>(
            new LegalTextReadResult.Rejected(
            [
                new LegalCatalogIssue(
                    "legal-catalog-generic-reader-unavailable",
                    componentId),
            ]));
    }

    Task<LegalTextReadResult> ReadLicenseTextAsync(
        string componentId,
        CancellationToken cancellationToken);
}
