namespace VRRecorder.Application.Setup;

public sealed record FirstRunSetupUiSnapshot(
    bool RequiresSetup,
    FirstRunSetupStep? CurrentStep,
    string TitleResourceKey,
    string BodyResourceKey,
    int StepNumber,
    int TotalSteps,
    double ProgressPercent);
