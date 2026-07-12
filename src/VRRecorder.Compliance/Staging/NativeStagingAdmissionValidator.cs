using System.Text.Json;
using VRRecorder.Compliance.Generation;
using VRRecorder.Compliance.Repository;

namespace VRRecorder.Compliance.Staging;

internal static class NativeStagingAdmissionValidator
{
    private const string FirstPartyComponentId = "vr-recorder";
    private const string WindowsX64Platform = "windows-x64";

    public static IReadOnlyList<ComplianceIssue> Validate(
        string? repositoryRoot,
        ApprovedReleaseGraph approvedGraph,
        IReadOnlyList<StagedPayloadFile> actualFiles,
        IReadOnlyList<RegisteredStagedArtifact> registeredArtifacts)
    {
        ArgumentNullException.ThrowIfNull(approvedGraph);
        ArgumentNullException.ThrowIfNull(actualFiles);
        ArgumentNullException.ThrowIfNull(registeredArtifacts);

        var approvedComponentIds = approvedGraph.Graph.Components
            .Select(component => component.Id)
            .ToHashSet(StringComparer.Ordinal);
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
        var thirdPartyCandidates = candidates.Where(candidate =>
                !string.Equals(
                    candidate.Registrations[0].ComponentId,
                    FirstPartyComponentId,
                    StringComparison.Ordinal))
            .ToArray();
        if (thirdPartyCandidates.Length == 0)
        {
            return ValidateKinds(candidates);
        }

        var issues = new List<ComplianceIssue>(ValidateKinds(candidates));
        var admittedCandidates = new List<(StagedPayloadFile File,
            RegisteredStagedArtifact Registration)>();
        foreach (var candidate in thirdPartyCandidates)
        {
            var registration = candidate.Registrations[0];
            if (!approvedComponentIds.Contains(registration.ComponentId))
            {
                issues.Add(new ComplianceIssue(
                    "unapproved-native-artifact-owner",
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

    private static ComplianceIssue[] Order(
        IEnumerable<ComplianceIssue> issues) =>
        issues.OrderBy(issue => issue.Code, StringComparer.Ordinal)
            .ThenBy(issue => issue.Subject, StringComparer.Ordinal)
            .ToArray();

    private sealed record NativeCandidate(
        StagedPayloadFile File,
        IReadOnlyList<RegisteredStagedArtifact> Registrations);
}
