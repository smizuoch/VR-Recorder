using VRRecorder.Application.Ports;

namespace VRRecorder.Application.Storage;

public sealed class StaleRecordingRecoveryUseCase
{
    private readonly IStaleRecordingCatalog _catalog;
    private readonly IRecordingRecoveryStore _recoveryStore;

    public StaleRecordingRecoveryUseCase(
        IStaleRecordingCatalog catalog,
        IRecordingRecoveryStore recoveryStore)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(recoveryStore);
        _catalog = catalog;
        _recoveryStore = recoveryStore;
    }

    public async Task<IReadOnlyList<QuarantinedRecording>> ExecuteAsync(
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        var staleRecordings = await _catalog
            .FindAsync(outputDirectory, cancellationToken)
            .ConfigureAwait(false);
        var quarantined = new List<QuarantinedRecording>(
            staleRecordings.Count);
        foreach (var recording in staleRecordings)
        {
            quarantined.Add(await _recoveryStore
                .QuarantineAsync(recording, cancellationToken)
                .ConfigureAwait(false));
        }

        return quarantined;
    }
}
