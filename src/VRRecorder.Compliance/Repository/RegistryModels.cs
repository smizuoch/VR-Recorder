using System.Text.Json.Serialization;
using VRRecorder.Compliance.Dependencies;

namespace VRRecorder.Compliance.Repository;

internal sealed record RegistryDocument(
    int SchemaVersion,
    int RegistryVersion,
    RegistryComponent[] Components);

internal sealed record RegistryComponent(
    string Id,
    string DisplayName,
    string Version,
    string Purl,
    string Scope,
    string LicenseDeclared,
    string LicenseConcluded,
    string CopyrightNotice,
    string LicenseFilePath,
    string LicenseFileSha256,
    RegistryRepository Repository,
    bool Modified,
    RegistryApproval Approval,
    RegistryPackage[] Packages);

internal sealed record RegistryRepository(string Url, string? Commit);

internal sealed record RegistryApproval(string Status, string? Id, string? Reviewer);

internal sealed record RegistryPackage(
    string Id,
    string Version,
    string ContentHashSha512,
    string ArchiveSha256,
    [property: JsonConverter(typeof(JsonStringEnumConverter<NuGetDependencyKind>))]
    NuGetDependencyKind Kind);

internal sealed record RegistryPackageRegistration(
    RegistryComponent Component,
    RegistryPackage Package);

internal sealed record LockedNuGetPackage(
    string Id,
    string Version,
    string ContentHashSha512,
    NuGetDependencyKind Kind)
{
    public string Identity => $"{Id}@{Version}";
}
