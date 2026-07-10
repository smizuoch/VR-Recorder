namespace VRRecorder.Compliance.Generation;

public sealed record VerifiedLegalFile(
    LegalFileKind Kind,
    string RelativePath,
    string Sha256,
    string Utf8Content);
