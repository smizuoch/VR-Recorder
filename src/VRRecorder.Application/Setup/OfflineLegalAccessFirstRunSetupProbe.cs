using VRRecorder.Application.Compliance;
using VRRecorder.Application.Ports;

namespace VRRecorder.Application.Setup;

public sealed class OfflineLegalAccessFirstRunSetupProbe
    : IFirstRunSetupProbe
{
    private readonly ILegalCatalogReader _reader;

    public OfflineLegalAccessFirstRunSetupProbe(ILegalCatalogReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        _reader = reader;
    }

    public async Task<bool> VerifyAsync(
        FirstRunSetupStep setupStep,
        CancellationToken cancellationToken)
    {
        if (setupStep != FirstRunSetupStep.OfflineLegalAccess)
        {
            return false;
        }

        var read = await _reader.ReadAsync(cancellationToken)
            .ConfigureAwait(false);
        if (read is not LegalCatalogReadResult.Available available ||
            available.Catalog.Components.Count == 0)
        {
            return false;
        }

        foreach (var component in available.Catalog.Components)
        {
            if (component.LegalDocuments.Count == 0)
            {
                return false;
            }

            foreach (var reference in component.LegalDocuments)
            {
                var textRead = await _reader.ReadDocumentAsync(
                        component.Id,
                        reference,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (textRead is not LegalTextReadResult.Available textAvailable ||
                    !string.Equals(
                        textAvailable.Document.ComponentId,
                        component.Id,
                        StringComparison.Ordinal) ||
                    textAvailable.Document.Reference != reference ||
                    string.IsNullOrWhiteSpace(textAvailable.Document.Text))
                {
                    return false;
                }
            }
        }

        return true;
    }
}
