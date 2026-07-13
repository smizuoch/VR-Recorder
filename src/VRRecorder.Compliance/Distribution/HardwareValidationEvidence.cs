namespace VRRecorder.Compliance.Distribution;

internal sealed record HardwareValidationEvidence(
    ValidatedPayloadIdentity Payload,
    string ValidationReportSha256,
    bool Passed);
