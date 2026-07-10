using System.Security.Cryptography;

namespace VRRecorder.Compliance.Generation;

public static class LegalArtifactDirectoryVerifier
{
    public static async Task<IReadOnlyList<ComplianceIssue>> VerifyAsync(
        string outputDirectory,
        GeneratedLegalArtifactSet expected,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(expected.Artifacts);

        var issues = new List<ComplianceIssue>();
        var pathComparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var expectedPaths = expected.Artifacts
            .Select(artifact => artifact.RelativePath)
            .ToHashSet(pathComparer);
        foreach (var artifact in expected.Artifacts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = LegalArtifactPath.Resolve(
                outputDirectory,
                artifact.RelativePath);
            if (!File.Exists(path))
            {
                issues.Add(new ComplianceIssue(
                    "generated-artifact-missing",
                    artifact.RelativePath));
                continue;
            }

            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var hash = await SHA256
                .HashDataAsync(stream, cancellationToken)
                .ConfigureAwait(false);
            var actualHash = Convert
                .ToHexString(hash)
                .ToLowerInvariant();
            if (!string.Equals(
                    artifact.Sha256,
                    actualHash,
                    StringComparison.Ordinal))
            {
                issues.Add(new ComplianceIssue(
                    "generated-artifact-diff",
                    artifact.RelativePath));
            }
        }

        var fullOutputDirectory = Path.GetFullPath(outputDirectory);
        if (Directory.Exists(fullOutputDirectory))
        {
            foreach (var path in Directory.EnumerateFiles(
                         fullOutputDirectory,
                         "*",
                         SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relativePath = Path
                    .GetRelativePath(fullOutputDirectory, path)
                    .Replace(Path.DirectorySeparatorChar, '/')
                    .Replace(Path.AltDirectorySeparatorChar, '/');
                if (!expectedPaths.Contains(relativePath))
                {
                    issues.Add(new ComplianceIssue(
                        "generated-artifact-unexpected",
                        relativePath));
                }
            }
        }

        return issues
            .OrderBy(issue => issue.Code, StringComparer.Ordinal)
            .ThenBy(issue => issue.Subject, StringComparer.Ordinal)
            .ToArray();
    }
}
