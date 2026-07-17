namespace VRRecorder.Compliance.Distribution;

internal sealed record HardwareValidationEvidence(
    ValidatedPayloadIdentity Payload,
    string PayloadIdentityDocumentSha256,
    string ValidationReportSha256,
    IReadOnlyList<Guid> RunIds);
