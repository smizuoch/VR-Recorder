namespace VRRecorder.Application.Camera;

public abstract record VrChatTargetResolution
{
    private VrChatTargetResolution()
    {
    }

    public sealed record NotFound : VrChatTargetResolution;

    public sealed record Selected(VrChatInstanceCandidate Candidate)
        : VrChatTargetResolution;

    public sealed record SelectionRequired(
        IReadOnlyList<VrChatInstanceCandidate> Candidates)
        : VrChatTargetResolution;
}
