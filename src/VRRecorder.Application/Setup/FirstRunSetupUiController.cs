namespace VRRecorder.Application.Setup;

public sealed class FirstRunSetupUiController
{
    private readonly FirstRunSetupController _setup;

    public FirstRunSetupUiController(FirstRunSetupController setup)
    {
        ArgumentNullException.ThrowIfNull(setup);
        _setup = setup;
    }

    public async Task<FirstRunSetupUiSnapshot> LoadAsync(
        CancellationToken cancellationToken)
    {
        var progress = await _setup.LoadAsync(cancellationToken)
            .ConfigureAwait(false);
        var total = FirstRunSetupController.RequiredSteps.Count;
        if (progress.IsComplete)
        {
            return new FirstRunSetupUiSnapshot(
                RequiresSetup: false,
                CurrentStep: null,
                TitleResourceKey: string.Empty,
                BodyResourceKey: string.Empty,
                StepNumber: total,
                TotalSteps: total,
                ProgressPercent: 100d);
        }

        var current = progress.CurrentStep ??
                      throw new InvalidOperationException(
                          "Incomplete setup has no current step.");
        var resourceStem = ResourceStem(current);
        return new FirstRunSetupUiSnapshot(
            RequiresSetup: true,
            CurrentStep: current,
            TitleResourceKey: $"Setup_Step_{resourceStem}_Title",
            BodyResourceKey: $"Setup_Step_{resourceStem}_Body",
            StepNumber: progress.CompletedStepCount + 1,
            TotalSteps: total,
            ProgressPercent: progress.CompletedStepCount * 100d / total);
    }

    private static string ResourceStem(FirstRunSetupStep step) => step switch
    {
        FirstRunSetupStep.SteamVrDetection => "SteamVrDetection",
        FirstRunSetupStep.VrChatOscDetection => "VrChatOscDetection",
        FirstRunSetupStep.CameraOscEndpoint => "CameraOscEndpoint",
        FirstRunSetupStep.MicrophonePrivacyAndDevice =>
            "MicrophonePrivacyAndDevice",
        FirstRunSetupStep.EncoderSelfTest => "EncoderSelfTest",
        FirstRunSetupStep.SteamVrActionBinding => "SteamVrActionBinding",
        FirstRunSetupStep.WristOverlayPlacement => "WristOverlayPlacement",
        FirstRunSetupStep.TestRecordingPlayback => "TestRecordingPlayback",
        FirstRunSetupStep.LegalBundleVerification => "LegalBundleVerification",
        FirstRunSetupStep.OfflineLegalAccess => "OfflineLegalAccess",
        FirstRunSetupStep.LocalizationAccessibility =>
            "LocalizationAccessibility",
        FirstRunSetupStep.DesignAssetConformance => "DesignAssetConformance",
        _ => throw new ArgumentOutOfRangeException(nameof(step), step, null),
    };
}
