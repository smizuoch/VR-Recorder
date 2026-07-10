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

        var targetDirectory = Path.GetFullPath(outputDirectory);
        var trimmedTarget = Path.TrimEndingDirectorySeparator(targetDirectory);
        var parentDirectory = Path.GetDirectoryName(trimmedTarget) ??
                              throw new InvalidOperationException(
                                  "The legal artifact directory must have a parent.");
        var targetName = Path.GetFileName(trimmedTarget);
        if (string.IsNullOrWhiteSpace(targetName))
        {
            throw new InvalidOperationException(
                "The legal artifact directory must not be a filesystem root.");
        }

        Directory.CreateDirectory(parentDirectory);
        var transactionId = Guid.NewGuid().ToString("N");
        var stagingDirectory = Path.Combine(
            parentDirectory,
            $".{targetName}.staging-{transactionId}");
        var backupDirectory = Path.Combine(
            parentDirectory,
            $".{targetName}.backup-{transactionId}");

        try
        {
            await WriteStagingDirectoryAsync(
                    stagingDirectory,
                    artifactSet,
                    cancellationToken)
                .ConfigureAwait(false);
            var issues = await LegalArtifactDirectoryVerifier
                .VerifyAsync(
                    stagingDirectory,
                    artifactSet,
                    cancellationToken)
                .ConfigureAwait(false);
            if (issues.Count != 0)
            {
                throw new InvalidOperationException(
                    "The staged Legal Bundle failed self-verification.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            ReplaceDirectory(
                stagingDirectory,
                targetDirectory,
                backupDirectory);
        }
        finally
        {
            DeleteDirectoryIfPresent(stagingDirectory);
        }
    }

    private static async Task WriteStagingDirectoryAsync(
        string stagingDirectory,
        GeneratedLegalArtifactSet artifactSet,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(stagingDirectory);
        foreach (var artifact in artifactSet.Artifacts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = LegalArtifactPath.Resolve(
                stagingDirectory,
                artifact.RelativePath);
            var directory = Path.GetDirectoryName(path) ??
                            throw new InvalidOperationException(
                                "A legal artifact has no parent directory.");
            Directory.CreateDirectory(directory);
            await using var stream = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await stream
                .WriteAsync(artifact.Content, cancellationToken)
                .ConfigureAwait(false);
            await stream
                .FlushAsync(cancellationToken)
                .ConfigureAwait(false);
            stream.Flush(flushToDisk: true);
        }
    }

    private static void ReplaceDirectory(
        string stagingDirectory,
        string targetDirectory,
        string backupDirectory)
    {
        if (!Directory.Exists(targetDirectory))
        {
            Directory.Move(stagingDirectory, targetDirectory);
            return;
        }

        Directory.Move(targetDirectory, backupDirectory);
        try
        {
            Directory.Move(stagingDirectory, targetDirectory);
        }
        catch
        {
            if (!Directory.Exists(targetDirectory) &&
                Directory.Exists(backupDirectory))
            {
                Directory.Move(backupDirectory, targetDirectory);
            }

            throw;
        }

        DeleteDirectoryIfPresent(backupDirectory);
    }

    private static void DeleteDirectoryIfPresent(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
