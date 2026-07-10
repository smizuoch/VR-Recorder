using VRRecorder.Domain.Audio;

namespace VRRecorder.Application.Audio;

public sealed record AudioEndpointRediscoveryRequest(
    AudioInput Input,
    AudioEndpointRole Role,
    TimeSpan Budget);
