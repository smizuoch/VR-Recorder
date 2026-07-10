using VRRecorder.Application.Compliance;

namespace VRRecorder.Application.Ports;

public interface ILegalCatalogReader
{
    Task<LegalCatalogReadResult> ReadAsync(
        CancellationToken cancellationToken);

    Task<LegalTextReadResult> ReadLicenseTextAsync(
        string componentId,
        CancellationToken cancellationToken);
}
