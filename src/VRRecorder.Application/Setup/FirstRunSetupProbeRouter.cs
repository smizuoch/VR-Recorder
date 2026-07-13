using VRRecorder.Application.Ports;

namespace VRRecorder.Application.Setup;

public sealed class FirstRunSetupProbeRouter : IFirstRunSetupProbe
{
    private readonly Dictionary<FirstRunSetupStep, IFirstRunSetupProbe>
        _probes;

    public FirstRunSetupProbeRouter(
        IReadOnlyDictionary<FirstRunSetupStep, IFirstRunSetupProbe> probes)
    {
        ArgumentNullException.ThrowIfNull(probes);
        if (probes.Values.Any(probe => probe is null))
        {
            throw new ArgumentException(
                "A setup probe route cannot be null.",
                nameof(probes));
        }

        _probes = new Dictionary<FirstRunSetupStep, IFirstRunSetupProbe>(probes);
    }

    public Task<bool> VerifyAsync(
        FirstRunSetupStep setupStep,
        CancellationToken cancellationToken) =>
        _probes.TryGetValue(setupStep, out var probe)
            ? probe.VerifyAsync(setupStep, cancellationToken)
            : Task.FromResult(false);
}
