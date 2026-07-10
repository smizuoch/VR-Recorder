using VRRecorder.Application.Ports;

namespace VRRecorder.Application.Camera;

public abstract record VrChatCameraConnectionResolution
{
    private VrChatCameraConnectionResolution()
    {
    }

    public sealed record NotFound : VrChatCameraConnectionResolution;

    public sealed record SelectionRequired(
        IReadOnlyList<VrChatInstanceCandidate> Candidates)
        : VrChatCameraConnectionResolution;

    public sealed record Connected(
        VrChatInstanceCandidate Candidate,
        IVrChatCameraGateway Gateway)
        : VrChatCameraConnectionResolution;
}
