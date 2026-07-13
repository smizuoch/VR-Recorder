namespace VRRecorder.Compliance.Distribution;

internal sealed record ValidatedPayloadIdentity(
    string ProductVersion,
    string SourceRevision,
    string RuntimeIdentifier,
    string ApplicationExecutableSha256,
    string PayloadInventorySha256,
    string LegalBundleId,
    string LegalManifestSha256);
