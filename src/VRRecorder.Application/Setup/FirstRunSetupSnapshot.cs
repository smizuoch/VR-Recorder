namespace VRRecorder.Application.Setup;

public sealed record FirstRunSetupSnapshot(
    int SetupVersion,
    FirstRunSetupStep? CurrentStep,
    int CompletedStepCount)
{
    public bool IsComplete => CurrentStep is null;
}
