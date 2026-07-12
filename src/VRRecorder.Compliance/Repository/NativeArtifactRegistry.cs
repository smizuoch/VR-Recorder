using System.Security.Cryptography;
using System.Text.Json;
using VRRecorder.Compliance.Dependencies;

namespace VRRecorder.Compliance.Repository;

internal sealed record NativeArtifactRegistry(
    int SchemaVersion,
    int RegistryVersion,
    IReadOnlySet<string> ComponentIds,
    IReadOnlyList<RegisteredNativeComponent> Components,
    IReadOnlyList<RegisteredNativeArtifact> Artifacts);

internal sealed record RegisteredNativeComponent(
    string Id,
    string? Version,
    string? RepositoryUrl,
    string? RepositoryCommit,
    string? ApprovalStatus,
    string? ApprovalId,
    string? ApprovalReviewer);

internal sealed record RegisteredNativeArtifact(
    string ComponentId,
    string Platform,
    string FileName,
    string BinarySha256,
    string SourceArchivePath,
    string SourceArchiveSha256,
    string BuildRecipePath);

internal static class NativeArtifactRegistryReader
{
    public static NativeArtifactRegistry Read(string root)
    {
        var path = Path.Combine(root, "third-party", "registry.yml");
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var schemaVersion = OptionalInt32(
            document.RootElement,
            "schemaVersion");
        var registryVersion = OptionalInt32(
            document.RootElement,
            "registryVersion");
        var componentIds = new HashSet<string>(StringComparer.Ordinal);
        var components = new List<RegisteredNativeComponent>();
        var artifacts = new List<RegisteredNativeArtifact>();
        foreach (var component in document.RootElement
                     .GetProperty("components")
                     .EnumerateArray())
        {
            var componentId = component.GetProperty("id").GetString() ??
                              throw new InvalidDataException(
                                  "A registry component ID is null.");
            if (!componentIds.Add(componentId))
            {
                throw new InvalidDataException(
                    "A registry component ID is duplicated.");
            }

            string? repositoryUrl = null;
            string? repositoryCommit = null;
            if (component.TryGetProperty("repository", out var repository) &&
                repository.ValueKind == JsonValueKind.Object)
            {
                repositoryUrl = OptionalString(repository, "url");
                repositoryCommit = OptionalString(repository, "commit");
            }

            string? approvalStatus = null;
            string? approvalId = null;
            string? approvalReviewer = null;
            if (component.TryGetProperty("approval", out var approval) &&
                approval.ValueKind == JsonValueKind.Object)
            {
                approvalStatus = OptionalString(approval, "status");
                approvalId = OptionalString(approval, "id");
                approvalReviewer = OptionalString(approval, "reviewer");
            }

            components.Add(new RegisteredNativeComponent(
                componentId,
                OptionalString(component, "version"),
                repositoryUrl,
                repositoryCommit,
                approvalStatus,
                approvalId,
                approvalReviewer));

            if (!component.TryGetProperty("nativeArtifacts", out var nativeArtifacts))
            {
                continue;
            }

            foreach (var artifact in nativeArtifacts.EnumerateArray())
            {
                artifacts.Add(new RegisteredNativeArtifact(
                    componentId,
                    RequiredString(artifact, "platform"),
                    RequiredString(artifact, "fileName"),
                    RequiredString(artifact, "binarySha256"),
                    RequiredString(artifact, "sourceArchivePath"),
                    RequiredString(artifact, "sourceArchiveSha256"),
                    RequiredString(artifact, "buildRecipePath")));
            }
        }

        return new NativeArtifactRegistry(
            schemaVersion,
            registryVersion,
            componentIds,
            components,
            artifacts);
    }

    public static ComplianceIssue? ValidateDependency(
        string root,
        NativeArtifactRegistry registry,
        string componentId,
        string fileName,
        string platform)
    {
        var subject = $"{componentId}:{fileName}";
        var matches = registry.Artifacts.Where(artifact =>
                string.Equals(
                    artifact.ComponentId,
                    componentId,
                    StringComparison.Ordinal) &&
                string.Equals(
                    artifact.FileName,
                    fileName,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    artifact.Platform,
                    platform,
                    StringComparison.Ordinal))
            .ToArray();
        if (matches.Length == 0)
        {
            return new ComplianceIssue(
                "missing-native-artifact-registration",
                subject);
        }

        if (matches.Length != 1)
        {
            return new ComplianceIssue(
                "ambiguous-native-artifact-registration",
                subject);
        }

        return IsValid(root, matches[0])
            ? null
            : new ComplianceIssue(
                "invalid-native-artifact-registration",
                subject);
    }

    private static bool IsValid(string root, RegisteredNativeArtifact artifact)
    {
        if (!IsLowerHexSha256(artifact.BinarySha256) ||
            !IsLowerHexSha256(artifact.SourceArchiveSha256) ||
            !TryResolveRepositoryPath(
                root,
                artifact.SourceArchivePath,
                out var sourceArchivePath) ||
            !File.Exists(sourceArchivePath) ||
            !TryResolveRepositoryPath(
                root,
                artifact.BuildRecipePath,
                out var buildRecipePath) ||
            !File.Exists(buildRecipePath))
        {
            return false;
        }

        using var source = File.OpenRead(sourceArchivePath);
        var actualHash = Convert
            .ToHexString(SHA256.HashData(source))
            .ToLowerInvariant();
        return string.Equals(
            actualHash,
            artifact.SourceArchiveSha256,
            StringComparison.Ordinal);
    }

    private static string RequiredString(JsonElement element, string propertyName)
    {
        var value = element.GetProperty(propertyName).GetString();
        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidDataException(
                $"Native artifact property {propertyName} is missing.");
    }

    private static string? OptionalString(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var value = property.GetString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static int OptionalInt32(
        JsonElement element,
        string propertyName) =>
        element.TryGetProperty(propertyName, out var property) &&
        property.TryGetInt32(out var value)
            ? value
            : 0;

    private static bool TryResolveRepositoryPath(
        string root,
        string relativePath,
        out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(relativePath) ||
            Path.IsPathRooted(relativePath))
        {
            return false;
        }

        fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
        var prefix = Path.TrimEndingDirectorySeparator(root) +
                     Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return fullPath.StartsWith(prefix, comparison);
    }

    private static bool IsLowerHexSha256(string value) =>
        value.Length == 64 && value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');
}
