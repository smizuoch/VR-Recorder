namespace VRRecorder.Compliance.Distribution;

internal sealed record DistributionPromotionRequest(
    DistributionTarget Target,
    string ArtifactPath,
    ValidatedPayloadIdentity Payload,
    HardwareValidationEvidence? HardwareValidation,
    MicrosoftStoreIdentity? StoreIdentity);
