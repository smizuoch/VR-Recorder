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
    RegistryPackage[] Packages,
    RegistryFfmpegLegalCandidate? FfmpegLegalCandidate = null,
    RegistrySpout2LegalCandidate? Spout2LegalCandidate = null,
    RegistryNativeArtifact[]? NativeArtifacts = null);

internal sealed record RegistryRepository(string Url, string? Commit);

internal sealed record RegistryApproval(string Status, string? Id, string? Reviewer);

internal sealed record RegistryPackage(
    string Id,
    string Version,
    string ContentHashSha512,
    string ArchiveSha256,
    [property: JsonConverter(typeof(JsonStringEnumConverter<NuGetDependencyKind>))]
    NuGetDependencyKind Kind);

internal sealed record RegistryNativeArtifact(
    string Platform,
    string FileName,
    string BinarySha256,
    string SourceArchivePath,
    string SourceArchiveSha256,
    string BuildRecipePath);

internal sealed record RegistryFfmpegLegalCandidate(
    int SchemaVersion,
    string SourceArchiveFileName,
    long SourceArchiveLength,
    string SourceArchiveSha256,
    string SourceDownloadUrl,
    string SourcePatchPath,
    long SourcePatchLength,
    string SourcePatchSha256,
    string SourcePatchUpstreamCommit,
    string SourcePatchUpstreamUrl,
    string BuildRecipePath,
    long BuildRecipeLength,
    string BuildRecipeSha256,
    string SourceOfferPath,
    long SourceOfferLength,
    string SourceOfferSha256,
    RegistryCandidateArtifact[] Artifacts);

internal sealed record RegistryCandidateArtifact(
    string FileName,
    long Length,
    string Sha256);

internal sealed record RegistrySpout2LegalCandidate(
    int SchemaVersion,
    string SourceArchiveFileName,
    long SourceArchiveLength,
    string SourceArchiveSha256,
    string SourceDownloadUrl,
    string BinaryArchiveFileName,
    long BinaryArchiveLength,
    string BinaryArchiveSha256,
    string BinaryDownloadUrl,
    string Deployment,
    string RuntimeLibrary,
    string BuildRecipePath,
    long BuildRecipeLength,
    string BuildRecipeSha256,
    string SourceOfferPath,
    long SourceOfferLength,
    string SourceOfferSha256,
    RegistryCandidateArtifact[] Artifacts);

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
