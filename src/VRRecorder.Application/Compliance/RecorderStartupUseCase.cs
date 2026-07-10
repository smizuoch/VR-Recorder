using VRRecorder.Application.Ports;
using VRRecorder.Domain.Recording;

namespace VRRecorder.Application.Compliance;

public sealed class RecorderStartupUseCase
{
    private readonly ILegalBundleVerifier _legalBundleVerifier;

    public RecorderStartupUseCase(ILegalBundleVerifier legalBundleVerifier)
    {
        ArgumentNullException.ThrowIfNull(legalBundleVerifier);
        _legalBundleVerifier = legalBundleVerifier;
    }

    public async Task<RecorderStartupResult> ExecuteAsync(
        CancellationToken cancellationToken)
    {
        var verification = await _legalBundleVerifier
            .VerifyAsync(cancellationToken)
            .ConfigureAwait(false);
        ArgumentNullException.ThrowIfNull(verification);
        var trigger = verification is LegalBundleVerification.Verified
            ? RecorderTrigger.LegalVerificationSucceeded
            : RecorderTrigger.LegalVerificationFailed;
        var state = RecorderStateMachine.Transition(
            RecorderState.Booting,
            trigger);
        var issues = verification is LegalBundleVerification.Rejected rejected
            ? rejected.Issues
            : [];
        ArgumentNullException.ThrowIfNull(issues);
        return new RecorderStartupResult(state, issues);
    }
}
