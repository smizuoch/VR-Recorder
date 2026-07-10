using System.Security.Cryptography;
using VRRecorder.Compliance.Generation;

namespace VRRecorder.Compliance.Staging;

internal static class MaterialSymbolsAssetProvenanceValidator
{
    private const string ComponentId = "material-symbols";
    private const string Repository =
        "https://github.com/google/material-design-icons";
    private const string LicenseExpression = "Apache-2.0";
    private const string RightsLedgerId = "material-symbols-ui-icons";
    private const string RightsPathGlob =
        "src/VRRecorder.DesignSystem/Assets/MaterialSymbols/**/*";
    private const string SelectedAssetManifest = "ui/material-symbols.yml";

    public static async Task<IReadOnlyList<ComplianceIssue>> ValidateAsync(
        NormalizedComponentGraph graph,
        MaterialSymbolsReleaseEvidence? evidence,
        IReadOnlyList<RegisteredStagedArtifact> registeredArtifacts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(registeredArtifacts);
        if (evidence is null)
        {
            return
            [
                Issue(
                    "material-symbols-release-evidence-missing",
                    ComponentId),
            ];
        }

        if (evidence.RightsLedgerEntry is null ||
            evidence.StagedAssets is null ||
            !MaterialSymbolsManifestReader.TryRead(
                graph.Components,
                out var manifest))
        {
            return
            [
                Issue(
                    "material-symbols-release-evidence-invalid",
                    ComponentId),
            ];
        }

        if (!RepositoryEvidenceRoot.TryResolve(
                evidence.RepositoryRoot,
                out var repositoryRoot))
        {
            return
            [
                Issue(
                    "material-symbols-release-evidence-invalid",
                    ComponentId),
            ];
        }

        var component = graph.Components.Single(component =>
            string.Equals(component.Id, ComponentId, StringComparison.Ordinal));
        var issues = new List<ComplianceIssue>();
        ValidateRightsLedger(
            component,
            manifest!,
            repositoryRoot,
            evidence.RightsLedgerEntry,
            issues);
        await ValidateRepositoryAssetsAsync(
                repositoryRoot,
                manifest!.SelectedIcons,
                issues,
                cancellationToken)
            .ConfigureAwait(false);
        ValidateStagingRegistrations(
            manifest.SelectedIcons,
            evidence.RightsLedgerEntry,
            evidence.StagedAssets,
            registeredArtifacts,
            issues);
        return Order(issues);
    }

    private static void ValidateRightsLedger(
        NormalizedComponent component,
        MaterialSymbolsManifestDocument manifest,
        string repositoryRoot,
        MaterialSymbolsRightsLedgerEntry entry,
        List<ComplianceIssue> issues)
    {
        if (!string.Equals(entry.Id, RightsLedgerId, StringComparison.Ordinal) ||
            !string.Equals(
                entry.PathGlob,
                RightsPathGlob,
                StringComparison.Ordinal) ||
            !string.Equals(
                entry.ComponentRef,
                ComponentId,
                StringComparison.Ordinal) ||
            !string.Equals(entry.Upstream, Repository, StringComparison.Ordinal) ||
            !string.Equals(
                entry.Upstream,
                manifest.Source.Repository,
                StringComparison.Ordinal) ||
            !string.Equals(
                entry.Commit,
                manifest.Source.Commit,
                StringComparison.Ordinal) ||
            !string.Equals(
                entry.Commit,
                component.Version,
                StringComparison.Ordinal) ||
            !string.Equals(
                entry.SelectedAssetManifest,
                SelectedAssetManifest,
                StringComparison.Ordinal) ||
            !string.Equals(
                entry.License,
                LicenseExpression,
                StringComparison.Ordinal) ||
            !string.Equals(
                entry.License,
                manifest.Source.LicenseExpression,
                StringComparison.Ordinal) ||
            entry.TrademarkUse ||
            entry.ProductLogoUse ||
            entry.RuntimeNetworkAllowed ||
            !entry.RedistributionApproved ||
            !IsSafeApprovalId(entry.ApprovalId) ||
            !string.Equals(
                entry.ApprovalId,
                component.Approval.TicketId,
                StringComparison.Ordinal) ||
            !IsSafeRelativePath(entry.Evidence) ||
            ContainsPlaceholder(entry.ApprovalId) ||
            ContainsPlaceholder(entry.Evidence) ||
            !IsRegularEvidenceFile(
                repositoryRoot,
                entry.SelectedAssetManifest) ||
            !IsRegularEvidenceFile(repositoryRoot, entry.Evidence))
        {
            issues.Add(Issue(
                "material-symbols-rights-ledger-mismatch",
                ComponentId));
        }
    }

    private static async Task ValidateRepositoryAssetsAsync(
        string repositoryRoot,
        MaterialSymbolsManifestIcon[] icons,
        List<ComplianceIssue> issues,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(repositoryRoot) ||
            !Directory.Exists(repositoryRoot))
        {
            issues.Add(Issue(
                "material-symbols-release-evidence-invalid",
                ComponentId));
            return;
        }

        foreach (var icon in icons)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ValidateRepositoryAssetAsync(
                    repositoryRoot,
                    icon.SourcePath,
                    icon.SourceSha256,
                    "source",
                    icon.SemanticId,
                    issues,
                    cancellationToken)
                .ConfigureAwait(false);
            await ValidateRepositoryAssetAsync(
                    repositoryRoot,
                    icon.OutputPath,
                    icon.OutputSha256,
                    "output",
                    icon.SemanticId,
                    issues,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static async Task ValidateRepositoryAssetAsync(
        string repositoryRoot,
        string relativePath,
        string expectedSha256,
        string assetKind,
        string semanticId,
        List<ComplianceIssue> issues,
        CancellationToken cancellationToken)
    {
        var subject = $"{ComponentId}:{semanticId}:{relativePath}";
        if (!TryResolveRegularFile(
                repositoryRoot,
                relativePath,
                out var fullPath,
                out var linked))
        {
            issues.Add(Issue(
                linked
                    ? "material-symbols-asset-link-not-allowed"
                    : $"material-symbols-{assetKind}-asset-missing",
                subject));
            return;
        }

        try
        {
            await using var stream = new FileStream(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var actualSha256 = Convert
                .ToHexString(await SHA256.HashDataAsync(
                    stream,
                    cancellationToken))
                .ToLowerInvariant();
            if (!string.Equals(
                    actualSha256,
                    expectedSha256,
                    StringComparison.Ordinal))
            {
                issues.Add(Issue(
                    $"material-symbols-{assetKind}-hash-mismatch",
                    subject));
            }
        }
        catch (IOException)
        {
            issues.Add(Issue(
                $"material-symbols-{assetKind}-asset-unreadable",
                subject));
        }
        catch (UnauthorizedAccessException)
        {
            issues.Add(Issue(
                $"material-symbols-{assetKind}-asset-unreadable",
                subject));
        }
    }

    private static void ValidateStagingRegistrations(
        MaterialSymbolsManifestIcon[] icons,
        MaterialSymbolsRightsLedgerEntry rightsLedger,
        IReadOnlyList<MaterialSymbolsStagedAssetRegistration> mappings,
        IReadOnlyList<RegisteredStagedArtifact> registeredArtifacts,
        List<ComplianceIssue> issues)
    {
        var expectedOutputs = icons
            .Select(icon => icon.OutputPath)
            .ToHashSet(StringComparer.Ordinal);
        var materialRegistrations = registeredArtifacts
            .Where(artifact => string.Equals(
                artifact.ComponentId,
                ComponentId,
                StringComparison.Ordinal))
            .ToArray();
        var mappingPathsAreUnique = mappings
            .GroupBy(
                mapping => mapping.OutputPath,
                StringComparer.OrdinalIgnoreCase)
            .All(group => group.Count() == 1) &&
            mappings
                .GroupBy(
                    mapping => mapping.StagingRelativePath,
                    StringComparer.OrdinalIgnoreCase)
                .All(group => group.Count() == 1);

        var mismatch = !mappingPathsAreUnique ||
                       mappings.Count != icons.Length ||
                       materialRegistrations.Length != icons.Length;
        foreach (var icon in icons)
        {
            var iconMappings = mappings.Where(candidate =>
                    string.Equals(
                        candidate.OutputPath,
                        icon.OutputPath,
                        StringComparison.Ordinal))
                .Take(2)
                .ToArray();
            if (iconMappings.Length != 1)
            {
                mismatch = true;
                continue;
            }

            var mapping = iconMappings[0];
            if (!string.Equals(
                    mapping.RightsLedgerEntryId,
                    rightsLedger.Id,
                    StringComparison.Ordinal) ||
                !IsSafeRelativePath(mapping.StagingRelativePath))
            {
                mismatch = true;
                continue;
            }

            var registrations = registeredArtifacts.Where(artifact =>
                    string.Equals(
                        artifact.RelativePath,
                        mapping.StagingRelativePath,
                        StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (registrations.Length != 1)
            {
                mismatch = true;
                continue;
            }

            var registration = registrations[0];
            if (!string.Equals(
                    registration.ComponentId,
                    ComponentId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    registration.RelativePath,
                    mapping.StagingRelativePath,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    registration.Sha256,
                    icon.OutputSha256,
                    StringComparison.Ordinal) ||
                registration.Kind != StagedArtifactKind.Asset)
            {
                mismatch = true;
            }
        }

        if (mappings.Any(mapping =>
                !expectedOutputs.Contains(mapping.OutputPath)))
        {
            mismatch = true;
        }

        if (mismatch)
        {
            issues.Add(Issue(
                "material-symbols-staging-registration-mismatch",
                ComponentId));
        }
    }

    private static bool IsRegularEvidenceFile(
        string repositoryRoot,
        string relativePath) =>
        IsSafeRelativePath(relativePath) &&
        TryResolveRegularFile(
            repositoryRoot,
            relativePath,
            out _,
            out _);

    private static bool TryResolveRegularFile(
        string repositoryRoot,
        string relativePath,
        out string fullPath,
        out bool linked)
    {
        fullPath = string.Empty;
        linked = false;
        if (!IsSafeRelativePath(relativePath))
        {
            return false;
        }

        string root;
        try
        {
            root = Path.GetFullPath(repositoryRoot);
            fullPath = LegalArtifactPath.Resolve(root, relativePath);
        }
        catch (ArgumentException)
        {
            return false;
        }

        if (!Directory.Exists(root))
        {
            return false;
        }

        var current = root;
        try
        {
            if (HasReparsePoint(current))
            {
                linked = true;
                return false;
            }

            foreach (var segment in relativePath.Split('/'))
            {
                current = Path.Combine(current, segment);
                if (!File.Exists(current) && !Directory.Exists(current))
                {
                    return false;
                }

                if (HasReparsePoint(current))
                {
                    linked = true;
                    return false;
                }
            }
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }

        return File.Exists(fullPath) && !Directory.Exists(fullPath);
    }

    private static bool HasReparsePoint(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static bool IsSafeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            path.StartsWith('/') ||
            path.Contains('\\') ||
            path.Contains(':'))
        {
            return false;
        }

        return path.Split('/').All(segment =>
            segment.Length > 0 && segment is not "." and not "..");
    }

    private static bool ContainsPlaceholder(string value) =>
        value.Contains('<', StringComparison.Ordinal) ||
        value.Contains('>', StringComparison.Ordinal);

    private static bool IsSafeApprovalId(string value) =>
        value is { Length: > 0 and <= 128 } &&
        IsAsciiLetterOrDigit(value[0]) &&
        value.Skip(1).All(character =>
            IsAsciiLetterOrDigit(character) ||
            character is '-' or '_' or '.');

    private static bool IsAsciiLetterOrDigit(char value) =>
        value is >= 'a' and <= 'z' or
            >= 'A' and <= 'Z' or
            >= '0' and <= '9';

    private static ComplianceIssue Issue(string code, string subject) =>
        new(code, subject);

    private static ComplianceIssue[] Order(
        IEnumerable<ComplianceIssue> issues) =>
        issues
            .OrderBy(issue => issue.Code, StringComparer.Ordinal)
            .ThenBy(issue => issue.Subject, StringComparer.Ordinal)
            .ToArray();
}
