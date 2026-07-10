using VRRecorder.Compliance.Generation;
using VRRecorder.Compliance.Runtime;
using VRRecorder.Compliance.Staging;

namespace VRRecorder.Compliance.Packaging;

public sealed class LegalReleasePackageOrchestrator
{
    private const string LegalBundleRoot = "VR-Recorder-Legal";
    private const string ManifestRelativePath = "LEGAL-MANIFEST.sha256";
    private readonly FileSystemStagingInventoryReader _inventoryReader;
    private readonly ReleasePackageGenerator _packageGenerator;

    public LegalReleasePackageOrchestrator()
    {
        _inventoryReader = new FileSystemStagingInventoryReader();
        _packageGenerator = new ReleasePackageGenerator(
            _inventoryReader,
            new DeterministicZipReleasePackageWriter());
    }

    public async Task<LegalReleasePackageResult> GenerateAsync(
        ReleaseLegalPackageRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.ComponentGraph);
        ArgumentNullException.ThrowIfNull(request.GenerationContext);
        ArgumentNullException.ThrowIfNull(request.ApprovedPayloadArtifacts);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.StagingDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.PackagePath);

        var legalBundleRelativePath = ResolveLegalBundleRelativePath(
            request.GenerationContext.ProductVersion);
        var eligibility = ReleaseEligibilityGate.EvaluateProductRelease(
            request.ComponentGraph);
        if (!eligibility.IsApproved)
        {
            return Reject(eligibility.Issues);
        }

        var preflightIssues = await ValidatePayloadStagingAsync(
                request,
                cancellationToken)
            .ConfigureAwait(false);
        if (preflightIssues.Count != 0)
        {
            return Reject(preflightIssues);
        }

        var artifactSet = LegalArtifactSetGenerator.Generate(
            request.GenerationContext,
            eligibility.ApprovedGraph!);
        var legalBundleDirectory = LegalArtifactPath.Resolve(
            request.StagingDirectory,
            legalBundleRelativePath);
        await LegalArtifactDirectoryWriter
            .WriteAsync(
                legalBundleDirectory,
                artifactSet,
                cancellationToken)
            .ConfigureAwait(false);

        var registeredArtifacts = request.ApprovedPayloadArtifacts
            .Concat(CreateLegalRegistrations(
                legalBundleRelativePath,
                artifactSet))
            .ToArray();
        var packageResult = await _packageGenerator
            .GenerateAsync(
                new ReleasePackageRequest(
                    request.StagingDirectory,
                    request.PackagePath,
                    registeredArtifacts),
                cancellationToken)
            .ConfigureAwait(false);
        if (!packageResult.Succeeded)
        {
            return Reject(packageResult.Issues);
        }

        var manifest = artifactSet.Artifacts.Single(artifact =>
            string.Equals(
                artifact.RelativePath,
                ManifestRelativePath,
                StringComparison.Ordinal));
        return new LegalReleasePackageResult(
            true,
            [],
            new AuthenticatedLegalBundleAnchor(
                request.GenerationContext.DocumentNamespace,
                manifest.Sha256),
            legalBundleRelativePath);
    }

    private async Task<IReadOnlyList<ComplianceIssue>>
        ValidatePayloadStagingAsync(
            ReleaseLegalPackageRequest request,
            CancellationToken cancellationToken)
    {
        var inventory = await _inventoryReader
            .ReadAsync(request.StagingDirectory, cancellationToken)
            .ConfigureAwait(false);
        return inventory.ScanIssues
            .Concat(StagingInventoryValidator.Validate(
                inventory.Files,
                request.ApprovedPayloadArtifacts))
            .OrderBy(issue => issue.Code, StringComparer.Ordinal)
            .ThenBy(issue => issue.Subject, StringComparer.Ordinal)
            .ToArray();
    }

    private static IEnumerable<RegisteredStagedArtifact>
        CreateLegalRegistrations(
            string legalBundleRelativePath,
            GeneratedLegalArtifactSet artifactSet) =>
        artifactSet.Artifacts.Select(artifact =>
            new RegisteredStagedArtifact(
                "vr-recorder-legal-bundle",
                $"{legalBundleRelativePath}/{artifact.RelativePath}",
                artifact.Sha256,
                StagedArtifactKind.Asset));

    private static string ResolveLegalBundleRelativePath(
        string productVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productVersion);
        if (productVersion is "." or ".." ||
            productVersion.EndsWith(' ') ||
            productVersion.EndsWith('.') ||
            productVersion.Any(character =>
                character < ' ' ||
                character is '<' or '>' or ':' or '"' or '/' or '\\' or
                    '|' or '?' or '*'))
        {
            throw new ArgumentException(
                "The product version is not safe for a release package path.",
                nameof(productVersion));
        }

        return $"{LegalBundleRoot}/{productVersion}";
    }

    private static LegalReleasePackageResult Reject(
        IReadOnlyList<ComplianceIssue> issues) =>
        new(false, issues, null, null);
}
