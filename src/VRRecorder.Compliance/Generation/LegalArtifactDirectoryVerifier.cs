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

        return issues
            .OrderBy(issue => issue.Code, StringComparer.Ordinal)
            .ThenBy(issue => issue.Subject, StringComparer.Ordinal)
            .ToArray();
    }
}
