namespace VRRecorder.Compliance.Generation;

public static class LegalArtifactDirectoryWriter
{
    public static async Task WriteAsync(
        string outputDirectory,
        GeneratedLegalArtifactSet artifactSet,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentNullException.ThrowIfNull(artifactSet);
        ArgumentNullException.ThrowIfNull(artifactSet.Artifacts);

        Directory.CreateDirectory(Path.GetFullPath(outputDirectory));
        foreach (var artifact in artifactSet.Artifacts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = LegalArtifactPath.Resolve(
                outputDirectory,
                artifact.RelativePath);
            var directory = Path.GetDirectoryName(path) ??
                            throw new InvalidOperationException(
                                "A legal artifact has no parent directory.");
            Directory.CreateDirectory(directory);
            await using var stream = new FileStream(
                path,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await stream
                .WriteAsync(artifact.Content, cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
