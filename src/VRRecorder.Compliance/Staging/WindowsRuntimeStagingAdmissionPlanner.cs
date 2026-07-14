using VRRecorder.Compliance.Generation;

namespace VRRecorder.Compliance.Staging;

internal sealed class WindowsRuntimeStagingAdmissionPlanner
{
    private const string FirstPartyComponentId = "vr-recorder";
    private const string NativeBinaryFileName = "vrrecorder_native.dll";
    private const string FactoryPairSubject =
        FirstPartyComponentId + ":" + NativeBinaryFileName;
    private readonly IStagingInventoryReader _inventoryReader;

    public WindowsRuntimeStagingAdmissionPlanner()
        : this(new FileSystemStagingInventoryReader())
    {
    }

    internal WindowsRuntimeStagingAdmissionPlanner(
        IStagingInventoryReader inventoryReader)
    {
        ArgumentNullException.ThrowIfNull(inventoryReader);
        _inventoryReader = inventoryReader;
    }

    public async Task<WindowsRuntimeStagingAdmissionResult> PlanAsync(
        WindowsRuntimeStagingManifest manifest,
        string sourceRoot,
        ApprovedReleaseGraph approvedGraph,
        string repositoryRoot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(approvedGraph);
        cancellationToken.ThrowIfCancellationRequested();

        var rootIssues = new List<ComplianceIssue>();
        if (!RepositoryEvidenceRoot.TryResolve(
                sourceRoot,
                out var canonicalSourceRoot))
        {
            rootIssues.Add(new ComplianceIssue(
                "invalid-runtime-staging-source-root",
                sourceRoot));
        }

        if (!RepositoryEvidenceRoot.TryResolve(
                repositoryRoot,
                out var canonicalRepositoryRoot))
        {
            rootIssues.Add(new ComplianceIssue(
                "invalid-runtime-staging-repository-root",
                repositoryRoot));
        }

        if (rootIssues.Count != 0)
        {
            return Reject(rootIssues);
        }

        StagingInventory inventory;
        try
        {
            inventory = await _inventoryReader
                .ReadAsync(canonicalSourceRoot, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                InvalidOperationException or
                ArgumentException)
        {
            return Reject(new ComplianceIssue(
                "runtime-staging-source-read-failed",
                canonicalSourceRoot));
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!TryCreateRegistrations(
                manifest,
                out var registeredArtifacts,
                out var manifestModelIssues))
        {
            return Reject([
                .. inventory.ScanIssues,
                .. manifestModelIssues,
            ]);
        }

        var issues = new List<ComplianceIssue>(inventory.ScanIssues);
        issues.AddRange(StagingInventoryValidator.Validate(
            inventory.Files,
            registeredArtifacts));
        issues.AddRange(ValidateKinds(
            inventory.Files,
            registeredArtifacts));
        issues.AddRange(NativeStagingAdmissionValidator.Validate(
            canonicalRepositoryRoot,
            approvedGraph,
            inventory.Files,
            registeredArtifacts));

        var pair = FindExactFactoryPair(manifest, issues);
        if (issues.Count != 0 || pair is null)
        {
            return Reject(issues);
        }

        byte[] nativeBinary;
        byte[] factoryEvidence;
        try
        {
            nativeBinary = await File.ReadAllBytesAsync(
                    WindowsRuntimeRelativePath.Resolve(
                        canonicalSourceRoot,
                        pair.Native.Source),
                    cancellationToken)
                .ConfigureAwait(false);
            factoryEvidence = await File.ReadAllBytesAsync(
                    WindowsRuntimeRelativePath.Resolve(
                        canonicalSourceRoot,
                        pair.Evidence.Source),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                InvalidOperationException or
                ArgumentException)
        {
            return Reject(new ComplianceIssue(
                "runtime-staging-source-read-failed",
                canonicalSourceRoot));
        }

        issues.AddRange(NativeFactorySelectionEvidenceValidator.Validate(
            factoryEvidence,
            pair.Evidence.Sha256,
            NativeBinaryFileName,
            nativeBinary));
        if (issues.Count != 0)
        {
            return Reject(issues);
        }

        var admittedFiles = manifest.Entries
            .OrderBy(entry => entry.Target, StringComparer.Ordinal)
            .Select(entry =>
            {
                var actual = inventory.Files.Single(file => string.Equals(
                    file.RelativePath,
                    entry.Source,
                    StringComparison.OrdinalIgnoreCase));
                return new AdmittedWindowsRuntimeStagingFile(
                    entry.Source,
                    entry.Target,
                    entry.Role,
                    entry.ComponentId,
                    entry.DeploymentKind,
                    entry.Sha256,
                    actual.Length,
                    actual.Kind);
            })
            .ToArray();
        return new WindowsRuntimeStagingAdmissionResult(
            new AdmittedWindowsRuntimeStagingPlan(
                manifest.ManifestSha256,
                canonicalSourceRoot,
                admittedFiles),
            []);
    }

    private static bool TryCreateRegistrations(
        WindowsRuntimeStagingManifest manifest,
        out RegisteredStagedArtifact[] registeredArtifacts,
        out ComplianceIssue[] issues)
    {
        var registrations = new List<RegisteredStagedArtifact>();
        var failures = new List<ComplianceIssue>();
        if (manifest.SchemaVersion != 1 ||
            manifest.Entries is null ||
            manifest.Entries.Count == 0)
        {
            failures.Add(new ComplianceIssue(
                "invalid-runtime-staging-manifest-model",
                manifest.ManifestSha256));
        }
        else
        {
            foreach (var entry in manifest.Entries)
            {
                if (entry is null ||
                    !TryMapKind(entry.DeploymentKind, out var kind))
                {
                    failures.Add(new ComplianceIssue(
                        "invalid-runtime-staging-manifest-model",
                        entry?.Target ?? manifest.ManifestSha256));
                    continue;
                }

                registrations.Add(new RegisteredStagedArtifact(
                    entry.ComponentId,
                    entry.Source,
                    entry.Sha256,
                    kind));
            }
        }

        registeredArtifacts = registrations.ToArray();
        issues = Order(failures);
        return failures.Count == 0;
    }

    private static ComplianceIssue[] ValidateKinds(
        IReadOnlyList<StagedPayloadFile> actualFiles,
        IReadOnlyList<RegisteredStagedArtifact> registeredArtifacts)
    {
        var issues = new List<ComplianceIssue>();
        foreach (var file in actualFiles)
        {
            var registrations = registeredArtifacts.Where(artifact =>
                    string.Equals(
                        artifact.RelativePath,
                        file.RelativePath,
                        StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (registrations.Length == 1 &&
                registrations[0].Kind != file.Kind)
            {
                issues.Add(new ComplianceIssue(
                    "staging-file-kind-mismatch",
                    file.RelativePath));
            }
        }

        return Order(issues);
    }

    private static FactoryPair? FindExactFactoryPair(
        WindowsRuntimeStagingManifest manifest,
        List<ComplianceIssue> issues)
    {
        var nativeEntries = manifest.Entries
            .Where(entry => entry.Role == WindowsRuntimeRole.FirstPartyNative)
            .ToArray();
        var evidenceEntries = manifest.Entries
            .Where(entry =>
                entry.Role == WindowsRuntimeRole.FactorySelectionEvidence)
            .ToArray();
        if (nativeEntries.Length != 1 ||
            evidenceEntries.Length != 1)
        {
            issues.Add(new ComplianceIssue(
                "invalid-native-factory-staging-pair",
                FactoryPairSubject));
            return null;
        }

        var native = nativeEntries[0];
        var evidence = evidenceEntries[0];
        if (!string.Equals(
                native.ComponentId,
                FirstPartyComponentId,
                StringComparison.Ordinal) ||
            native.DeploymentKind !=
                WindowsRuntimeDeploymentKind.NativeLibrary ||
            !HasExactFileName(native.Source, NativeBinaryFileName) ||
            !HasExactFileName(native.Target, NativeBinaryFileName) ||
            !string.Equals(
                evidence.ComponentId,
                FirstPartyComponentId,
                StringComparison.Ordinal) ||
            evidence.DeploymentKind !=
                WindowsRuntimeDeploymentKind.Evidence)
        {
            issues.Add(new ComplianceIssue(
                "invalid-native-factory-staging-pair",
                FactoryPairSubject));
            return null;
        }

        return new FactoryPair(native, evidence);
    }

    private static bool HasExactFileName(
        string relativePath,
        string expectedFileName) =>
        string.Equals(
            Path.GetFileName(relativePath),
            expectedFileName,
            StringComparison.Ordinal);

    private static bool TryMapKind(
        WindowsRuntimeDeploymentKind deploymentKind,
        out StagedArtifactKind kind)
    {
        switch (deploymentKind)
        {
            case WindowsRuntimeDeploymentKind.NativeLibrary:
                kind = StagedArtifactKind.NativeLibrary;
                return true;
            case WindowsRuntimeDeploymentKind.Executable:
                kind = StagedArtifactKind.Executable;
                return true;
            case WindowsRuntimeDeploymentKind.Asset:
            case WindowsRuntimeDeploymentKind.Evidence:
                kind = StagedArtifactKind.Asset;
                return true;
            default:
                kind = default;
                return false;
        }
    }

    private static WindowsRuntimeStagingAdmissionResult Reject(
        ComplianceIssue issue) => Reject([issue]);

    private static WindowsRuntimeStagingAdmissionResult Reject(
        IEnumerable<ComplianceIssue> issues) =>
        new(
            null,
            Array.AsReadOnly(Order(issues)));

    private static ComplianceIssue[] Order(
        IEnumerable<ComplianceIssue> issues) =>
        issues.OrderBy(issue => issue.Code, StringComparer.Ordinal)
            .ThenBy(issue => issue.Subject, StringComparer.Ordinal)
            .ToArray();

    private sealed record FactoryPair(
        WindowsRuntimeStagingEntry Native,
        WindowsRuntimeStagingEntry Evidence);
}
