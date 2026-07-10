using VRRecorder.Application.Encoding;
using VRRecorder.Application.Ports;
using VRRecorder.Domain.Encoding;

namespace VRRecorder.Application.Tests.TestDoubles;

internal sealed class ScriptedEncoderProbe : IEncoderProbe
{
    private readonly IReadOnlyDictionary<EncoderKind, EncoderProbeResult> _results;

    public ScriptedEncoderProbe(
        params (EncoderKind Encoder, EncoderProbeResult Result)[] results)
    {
        _results = results.ToDictionary(pair => pair.Encoder, pair => pair.Result);
    }

    public List<EncoderKind> ProbedEncoders { get; } = [];

    public Task<EncoderProbeResult> ProbeAsync(
        EncoderKind encoder,
        CancellationToken cancellationToken)
    {
        ProbedEncoders.Add(encoder);
        return Task.FromResult(_results[encoder]);
    }
}
