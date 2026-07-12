namespace VRRecorder.Application.Setup;

public sealed record FirstRunSetupVerificationResult(
    bool Succeeded,
    FirstRunSetupStep? VerifiedStep,
    FirstRunSetupSnapshot Setup);
