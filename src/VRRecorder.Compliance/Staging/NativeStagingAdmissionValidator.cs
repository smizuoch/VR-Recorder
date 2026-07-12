using System.Text.Json;
using VRRecorder.Compliance.Generation;
using VRRecorder.Compliance.Repository;

namespace VRRecorder.Compliance.Staging;

internal static class NativeStagingAdmissionValidator
{
    private const string FirstPartyComponentId = "vr-recorder";
    private const string WindowsX64Platform = "windows-x64";
    private static readonly HashSet<string> FirstPartyExecutables =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "VR-Recorder.exe",
            "VRRecorder.App.exe",
        };
    private static readonly HashSet<string> FirstPartyLibraries =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "VRRecorder.App.dll",
            "VRRecorder.Application.dll",
            "VRRecorder.Compliance.dll",
            "VRRecorder.DesignSystem.dll",
            "VRRecorder.Domain.dll",
            "VRRecorder.Infrastructure.Media.dll",
            "VRRecorder.Infrastructure.Osc.dll",
            "VRRecorder.Infrastructure.SteamVr.dll",
            "VRRecorder.Infrastructure.Storage.dll",
            "VRRecorder.Presentation.Wrist.dll",
            "vrrecorder_native.dll",
        };

    public static IReadOnlyList<ComplianceIssue> Validate(
        string? repositoryRoot,
        ApprovedReleaseGraph approvedGraph,
        IReadOnlyList<StagedPayloadFile> actualFiles,
        IReadOnlyList<RegisteredStagedArtifact> registeredArtifacts)
    {
        ArgumentNullException.ThrowIfNull(approvedGraph);
        ArgumentNullException.ThrowIfNull(actualFiles);
        ArgumentNullException.ThrowIfNull(registeredArtifacts);

        var candidates = actualFiles
            .Select(file => new NativeCandidate(
                file,
                registeredArtifacts.Where(artifact =>
                    string.Equals(
                        artifact.RelativePath,
                        file.RelativePath,
                        StringComparison.OrdinalIgnoreCase)).ToArray()))
            .Where(candidate =>
                IsNative(candidate.File.Kind) ||
                candidate.Registrations.Any(registration =>
                    IsNative(registration.Kind)))
            .Where(candidate => candidate.Registrations.Count == 1)
            .ToArray();
        var issues = new List<ComplianceIssue>(ValidateKinds(candidates));
        var thirdPartyCandidates = new List<NativeCandidate>();
        foreach (var candidate in candidates)
        {
            var registration = candidate.Registrations[0];
            if (!string.Equals(
                    registration.ComponentId,
                    FirstPartyComponentId,
                    StringComparison.Ordinal))
            {
                thirdPartyCandidates.Add(candidate);
                continue;
            }

            if (!IsApprovedFirstPartyArtifact(candidate.File))
            {
                issues.Add(new ComplianceIssue(
                    "unregistered-first-party-native-artifact",
                    $"{FirstPartyComponentId}:{candidate.File.RelativePath}"));
            }
        }

        if (thirdPartyCandidates.Count == 0)
        {
            return Order(issues);
        }

        var admittedCandidates = new List<(StagedPayloadFile File,
            RegisteredStagedArtifact Registration)>();
        foreach (var candidate in thirdPartyCandidates)
        {
            var registration = candidate.Registrations[0];
            var approvedComponents = approvedGraph.Graph.Components.Where(
                    component => string.Equals(
                        component.Id,
                        registration.ComponentId,
                        StringComparison.Ordinal))
                .ToArray();
            if (approvedComponents.Length == 0)
            {
                issues.Add(new ComplianceIssue(
                    "unapproved-native-artifact-owner",
                    $"{registration.ComponentId}:{candidate.File.RelativePath}"));
                continue;
            }

            if (approvedComponents.Length != 1)
            {
                issues.Add(new ComplianceIssue(
                    "ambiguous-native-artifact-owner",
                    $"{registration.ComponentId}:{candidate.File.RelativePath}"));
                continue;
            }

            if (approvedComponents[0].Scope is
                NoticeScope.TestOnly or NoticeScope.BuildOnly)
            {
                issues.Add(new ComplianceIssue(
                    "staged-component-scope-mismatch",
                    $"{registration.ComponentId}:{candidate.File.RelativePath}"));
                continue;
            }

            admittedCandidates.Add((candidate.File, registration));
        }

        if (admittedCandidates.Count == 0)
        {
            return Order(issues);
        }

        if (string.IsNullOrWhiteSpace(repositoryRoot))
        {
            issues.Add(new ComplianceIssue(
                "missing-native-artifact-evidence",
                WindowsX64Platform));
            return Order(issues);
        }

        NativeArtifactRegistry registry;
        try
        {
            registry = NativeArtifactRegistryReader.Read(repositoryRoot);
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                JsonException or
                InvalidDataException or
                InvalidOperationException or
                KeyNotFoundException or
                ArgumentException)
        {
            issues.Add(new ComplianceIssue(
                "invalid-native-artifact-registry",
                repositoryRoot));
            return Order(issues);
        }

        if (registry.SchemaVersion != 1 || registry.RegistryVersion < 1)
        {
            issues.Add(new ComplianceIssue(
                "invalid-native-artifact-registry",
                repositoryRoot));
            return Order(issues);
        }

        foreach (var componentId in admittedCandidates
                     .Select(candidate => candidate.Registration.ComponentId)
                     .Distinct(StringComparer.Ordinal))
        {
            var approvedComponents = approvedGraph.Graph.Components.Where(
                    component => string.Equals(
                    component.Id,
                    componentId,
                    StringComparison.Ordinal))
                .ToArray();
            if (approvedComponents.Length != 1)
            {
                issues.Add(new ComplianceIssue(
                    "ambiguous-native-artifact-owner",
                    componentId));
                continue;
            }

            var registeredComponents = registry.Components.Where(
                    component => string.Equals(
                    component.Id,
                    componentId,
                    StringComparison.Ordinal))
                .ToArray();
            if (registeredComponents.Length != 1)
            {
                issues.Add(new ComplianceIssue(
                    "missing-native-component-identity",
                    componentId));
                continue;
            }

            var approvedComponent = approvedComponents[0];
            var registeredComponent = registeredComponents[0];
            if (!string.Equals(
                    registeredComponent.ApprovalStatus,
                    "approved",
                    StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(registeredComponent.ApprovalId) ||
                string.IsNullOrWhiteSpace(
                    registeredComponent.ApprovalReviewer))
            {
                issues.Add(new ComplianceIssue(
                    "unapproved-native-artifact-owner",
                    componentId));
                continue;
            }

            if (string.IsNullOrWhiteSpace(registeredComponent.Version) ||
                string.IsNullOrWhiteSpace(registeredComponent.RepositoryUrl) ||
                string.IsNullOrWhiteSpace(registeredComponent.RepositoryCommit))
            {
                issues.Add(new ComplianceIssue(
                    "missing-native-component-identity",
                    componentId));
                continue;
            }

            if (!string.Equals(
                    approvedComponent.Version,
                    registeredComponent.Version,
                    StringComparison.Ordinal))
            {
                issues.Add(new ComplianceIssue(
                    "native-component-version-mismatch",
                    componentId));
            }

            if (!string.Equals(
                    approvedComponent.SourceInformation,
                    $"{registeredComponent.RepositoryUrl}@" +
                    registeredComponent.RepositoryCommit,
                    StringComparison.Ordinal))
            {
                issues.Add(new ComplianceIssue(
                    "native-component-source-mismatch",
                    componentId));
            }
        }

        foreach (var (file, registration) in admittedCandidates)
        {
            var fileName = Path.GetFileName(file.RelativePath);
            var registryIssue = NativeArtifactRegistryReader
                .ValidateDependency(
                    repositoryRoot,
                    registry,
                    registration.ComponentId,
                    fileName,
                    WindowsX64Platform);
            if (registryIssue is not null)
            {
                issues.Add(registryIssue);
                continue;
            }

            var artifact = registry.Artifacts.Single(candidate =>
                string.Equals(
                    candidate.ComponentId,
                    registration.ComponentId,
                    StringComparison.Ordinal) &&
                string.Equals(
                    candidate.FileName,
                    fileName,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    candidate.Platform,
                    WindowsX64Platform,
                    StringComparison.Ordinal));
            if (!string.Equals(
                    artifact.BinarySha256,
                    file.Sha256,
                    StringComparison.Ordinal))
            {
                issues.Add(new ComplianceIssue(
                    "native-artifact-binary-hash-mismatch",
                    $"{registration.ComponentId}:{fileName}"));
            }
        }

        return Order(issues);
    }

    private static List<ComplianceIssue> ValidateKinds(
        IEnumerable<NativeCandidate> candidates)
    {
        var issues = new List<ComplianceIssue>();
        foreach (var candidate in candidates)
        {
            var file = candidate.File;
            var registration = candidate.Registrations[0];
            if (file.Kind != registration.Kind)
            {
                issues.Add(new ComplianceIssue(
                    "native-artifact-kind-mismatch",
                    file.RelativePath));
            }
        }

        return issues;
    }

    private static bool IsNative(StagedArtifactKind kind) =>
        kind is StagedArtifactKind.NativeLibrary or StagedArtifactKind.Executable;

    private static bool IsApprovedFirstPartyArtifact(
        StagedPayloadFile file)
    {
        var fileName = Path.GetFileName(file.RelativePath);
        return file.Kind switch
        {
            StagedArtifactKind.Executable =>
                FirstPartyExecutables.Contains(fileName),
            StagedArtifactKind.NativeLibrary =>
                FirstPartyLibraries.Contains(fileName),
            _ => false,
        };
    }

    private static ComplianceIssue[] Order(
        IEnumerable<ComplianceIssue> issues) =>
        issues.OrderBy(issue => issue.Code, StringComparer.Ordinal)
            .ThenBy(issue => issue.Subject, StringComparer.Ordinal)
            .ToArray();

    private sealed record NativeCandidate(
        StagedPayloadFile File,
        IReadOnlyList<RegisteredStagedArtifact> Registrations);
}
