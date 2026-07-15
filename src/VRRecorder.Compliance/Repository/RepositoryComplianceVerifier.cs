using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using VRRecorder.Compliance.Dependencies;
using VRRecorder.Compliance.Legal;

namespace VRRecorder.Compliance.Repository;

public static class RepositoryComplianceVerifier
{
    private static readonly JsonSerializerOptions RegistryJsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static IReadOnlyList<ComplianceIssue> VerifyCandidateInputs(
        string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        var root = Path.GetFullPath(repositoryRoot);
        var issues = new List<ComplianceIssue>();
        var registry = ReadRegistry(root, issues);
        if (registry is null)
        {
            return issues;
        }

        if (registry.SchemaVersion != 1 || registry.RegistryVersion < 1)
        {
            issues.Add(new ComplianceIssue(
                "unsupported-registry-version",
                "third-party/registry.yml"));
            return issues;
        }

        var lockedPackages = ReadLockedPackages(root, issues);
        var registeredPackages = registry.Components
            .SelectMany(component => component.Packages.Select(package =>
                new RegistryPackageRegistration(component, package)))
            .ToArray();

        issues.AddRange(NuGetInventoryValidator.Validate(
            lockedPackages.Select(package => new NuGetPackage(
                package.Id,
                package.Version,
                package.Kind)),
            registeredPackages.Select(item => new RegisteredNuGetPackage(
                item.Package.Id,
                item.Package.Version,
                item.Component.LicenseConcluded))));

        VerifyPackageHashes(lockedPackages, registeredPackages, issues);
        VerifyLegalFiles(root, registry.Components, issues);
        issues.AddRange(FfmpegLegalCandidateVerifier.Verify(
            root,
            registry.Components));
        issues.AddRange(Spout2LegalCandidateVerifier.Verify(
            root,
            registry.Components));
        issues.AddRange(OpenVrLegalCandidateVerifier.Verify(
            root,
            registry.Components));
        issues.AddRange(RepositoryNativeRuntimeLoadVerifier.Verify(root));
        issues.AddRange(RepositoryNativeLinkVerifier.Verify(root));

        return issues;
    }

    private static RegistryDocument? ReadRegistry(
        string root,
        List<ComplianceIssue> issues)
    {
        var path = Path.Combine(root, "third-party", "registry.yml");
        if (!File.Exists(path))
        {
            issues.Add(new ComplianceIssue(
                "missing-component-registry",
                "third-party/registry.yml"));
            return null;
        }

        try
        {
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize<RegistryDocument>(
                       stream,
                       RegistryJsonOptions)
                   ?? throw new JsonException("The registry document is null.");
        }
        catch (JsonException)
        {
            issues.Add(new ComplianceIssue(
                "invalid-component-registry",
                "third-party/registry.yml"));
            return null;
        }
    }

    private static LockedNuGetPackage[] ReadLockedPackages(
        string root,
        List<ComplianceIssue> issues)
    {
        var packages = new Dictionary<string, LockedNuGetPackage>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var path in Directory.EnumerateFiles(
                     root,
                     "packages.lock.json",
                     SearchOption.AllDirectories))
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            var dependencies = document.RootElement.GetProperty("dependencies");
            foreach (var targetFramework in dependencies.EnumerateObject())
            {
                foreach (var dependency in targetFramework.Value.EnumerateObject())
                {
                    var value = dependency.Value;
                    var type = value.GetProperty("type").GetString();
                    if (string.Equals(type, "Project", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var version = value.GetProperty("resolved").GetString();
                    var contentHash = value.GetProperty("contentHash").GetString();
                    if (version is null || contentHash is null)
                    {
                        issues.Add(new ComplianceIssue(
                            "invalid-packages-lock",
                            Path.GetRelativePath(root, path)));
                        continue;
                    }

                    var kind = string.Equals(type, "Direct", StringComparison.Ordinal)
                        ? NuGetDependencyKind.Direct
                        : NuGetDependencyKind.Transitive;
                    var package = new LockedNuGetPackage(
                        dependency.Name,
                        version,
                        contentHash,
                        kind);

                    if (packages.TryGetValue(package.Identity, out var existing))
                    {
                        if (!string.Equals(
                                existing.ContentHashSha512,
                                package.ContentHashSha512,
                                StringComparison.Ordinal))
                        {
                            issues.Add(new ComplianceIssue(
                                "lock-content-hash-conflict",
                                package.Identity));
                        }
                        else if (kind == NuGetDependencyKind.Direct &&
                                 existing.Kind == NuGetDependencyKind.Transitive)
                        {
                            packages[package.Identity] = package;
                        }
                    }
                    else
                    {
                        packages.Add(package.Identity, package);
                    }
                }
            }
        }

        return packages.Values.ToArray();
    }

    private static void VerifyPackageHashes(
        IEnumerable<LockedNuGetPackage> lockedPackages,
        IEnumerable<RegistryPackageRegistration> registeredPackages,
        List<ComplianceIssue> issues)
    {
        var registeredByIdentity = new Dictionary<string, RegistryPackage>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var item in registeredPackages)
        {
            var package = item.Package;
            var identity = $"{package.Id}@{package.Version}";
            if (!registeredByIdentity.TryAdd(identity, package))
            {
                issues.Add(new ComplianceIssue(
                    "duplicate-component-registration",
                    identity));
            }
        }

        foreach (var package in lockedPackages)
        {
            if (!registeredByIdentity.TryGetValue(package.Identity, out var registered))
            {
                continue;
            }

            if (!string.Equals(
                    package.ContentHashSha512,
                    registered.ContentHashSha512,
                    StringComparison.Ordinal))
            {
                issues.Add(new ComplianceIssue(
                    "nuget-content-hash-mismatch",
                    package.Identity));
            }

            if (!IsLowerHexSha256(registered.ArchiveSha256))
            {
                issues.Add(new ComplianceIssue(
                    "invalid-package-archive-hash",
                    package.Identity));
            }
        }
    }

    private static void VerifyLegalFiles(
        string root,
        IEnumerable<RegistryComponent> components,
        List<ComplianceIssue> issues)
    {
        var componentArray = components.ToArray();
        var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var component in componentArray)
        {
            if (!TryResolveRepositoryPath(root, component.LicenseFilePath, out var path) ||
                !File.Exists(path))
            {
                continue;
            }

            using var stream = File.OpenRead(path);
            hashes[component.LicenseFilePath] = Convert
                .ToHexString(SHA256.HashData(stream))
                .ToLowerInvariant();
        }

        issues.AddRange(LegalMetadataValidator.Validate(
            componentArray.Select(component => new ThirdPartyComponent(
                component.Id,
                component.LicenseConcluded,
                component.CopyrightNotice,
                [new LegalFileReference(
                    component.LicenseFilePath,
                    component.LicenseFileSha256)])),
            hashes));
    }

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
        var rootWithSeparator = Path.TrimEndingDirectorySeparator(root) +
                                Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return fullPath.StartsWith(rootWithSeparator, comparison);
    }

    private static bool IsLowerHexSha256(string value) =>
        value.Length == 64 && value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');
}
