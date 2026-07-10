using VRRecorder.Application.Encoding;
using VRRecorder.Application.Ports;
using VRRecorder.Domain.Encoding;

namespace VRRecorder.Application.Tests.TestDoubles;

internal sealed class ScriptedEncoderProbe : IEncoderProbe
{
    private readonly Dictionary<EncoderKind, EncoderProbeResult> _results;

    public ScriptedEncoderProbe(
        params (EncoderKind Encoder, EncoderProbeResult Result)[] results)
    {
        _results = results.ToDictionary(pair => pair.Encoder, pair => pair.Result);
    }

    public List<EncoderKind> ProbedEncoders { get; } = [];

    public Task<EncoderProbeResult> ProbeAsync(
        EncoderProbeRequest request,
        CancellationToken cancellationToken)
    {
        ProbedEncoders.Add(request.Encoder);
        return Task.FromResult(_results[request.Encoder]);
    }
}
