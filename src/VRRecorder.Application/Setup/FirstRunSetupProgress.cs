namespace VRRecorder.Application.Setup;

public sealed record FirstRunSetupProgress
{
    public FirstRunSetupProgress(
        int setupVersion,
        IEnumerable<FirstRunSetupStep> completedSteps)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(setupVersion);

        ArgumentNullException.ThrowIfNull(completedSteps);
        SetupVersion = setupVersion;
        CompletedSteps = Array.AsReadOnly(completedSteps.ToArray());
    }

    public int SetupVersion { get; }

    public IReadOnlyList<FirstRunSetupStep> CompletedSteps { get; }
}
