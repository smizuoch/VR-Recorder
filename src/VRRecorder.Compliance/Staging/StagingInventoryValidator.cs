namespace VRRecorder.Compliance.Staging;

public static class StagingInventoryValidator
{
    public static IReadOnlyList<ComplianceIssue> Validate(
        IEnumerable<StagedPayloadFile> actualFiles,
        IEnumerable<RegisteredStagedArtifact> registeredArtifacts)
    {
        ArgumentNullException.ThrowIfNull(actualFiles);
        ArgumentNullException.ThrowIfNull(registeredArtifacts);

        var actual = actualFiles.ToArray();
        var registered = registeredArtifacts.ToArray();
        var issues = new List<ComplianceIssue>();
        var duplicatePaths = actual
            .GroupBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .Concat(registered
                .GroupBy(
                    item => item.RelativePath,
                    StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ThenBy(path => path, StringComparer.Ordinal)
            .ToArray();
        var duplicatePathSet = duplicatePaths.ToHashSet(
            StringComparer.OrdinalIgnoreCase);
        issues.AddRange(duplicatePaths.Select(path => new ComplianceIssue(
            "duplicate-staging-path",
            path)));

        foreach (var file in actual.Where(file =>
                     !duplicatePathSet.Contains(file.RelativePath)))
        {
            var matches = registered
                .Where(item => string.Equals(
                    item.RelativePath,
                    file.RelativePath,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (matches.Length == 0)
            {
                issues.Add(new ComplianceIssue(
                    "unregistered-staging-file",
                    file.RelativePath));
            }
            else if (matches.Length > 1)
            {
                issues.Add(new ComplianceIssue(
                    "ambiguous-staging-owner",
                    file.RelativePath));
            }
            else if (!string.Equals(
                         matches[0].Sha256,
                         file.Sha256,
                         StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new ComplianceIssue(
                    "staging-file-hash-mismatch",
                    file.RelativePath));
            }
        }

        foreach (var artifact in registered.Where(artifact =>
                     !duplicatePathSet.Contains(artifact.RelativePath) &&
                     !actual.Any(file => string.Equals(
                         file.RelativePath,
                         artifact.RelativePath,
                         StringComparison.OrdinalIgnoreCase))))
        {
            issues.Add(new ComplianceIssue(
                "registered-staging-file-missing",
                artifact.RelativePath));
        }

        return issues
            .OrderBy(item => item.Code, StringComparer.Ordinal)
            .ThenBy(item => item.Subject, StringComparer.Ordinal)
            .ToArray();
    }
}
