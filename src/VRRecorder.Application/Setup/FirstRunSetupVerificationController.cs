using VRRecorder.Application.Ports;

namespace VRRecorder.Application.Setup;

public sealed class FirstRunSetupVerificationController
{
    private readonly FirstRunSetupController _setup;
    private readonly IFirstRunSetupProbe _probe;

    public FirstRunSetupVerificationController(
        FirstRunSetupController setup,
        IFirstRunSetupProbe probe)
    {
        ArgumentNullException.ThrowIfNull(setup);
        ArgumentNullException.ThrowIfNull(probe);
        _setup = setup;
        _probe = probe;
    }

    public async Task<FirstRunSetupVerificationResult> VerifyCurrentAsync(
        CancellationToken cancellationToken)
    {
        var current = await _setup.LoadAsync(cancellationToken)
            .ConfigureAwait(false);
        if (current.IsComplete)
        {
            return new FirstRunSetupVerificationResult(
                Succeeded: false,
                VerifiedStep: null,
                Setup: current);
        }

        var step = current.CurrentStep ?? throw new InvalidOperationException(
            "Incomplete first-run setup has no current step.");
        var succeeded = await _probe.VerifyAsync(step, cancellationToken)
            .ConfigureAwait(false);
        if (!succeeded)
        {
            return new FirstRunSetupVerificationResult(
                Succeeded: false,
                VerifiedStep: step,
                Setup: current);
        }

        var advanced = await _setup.CompleteAsync(step, cancellationToken)
            .ConfigureAwait(false);
        return new FirstRunSetupVerificationResult(
            Succeeded: true,
            VerifiedStep: step,
            Setup: advanced);
    }
}
