using System.Collections.ObjectModel;
using VRRecorder.Application.Ports;

namespace VRRecorder.Application.Setup;

public sealed class FirstRunSetupController
{
    private static readonly ReadOnlyCollection<FirstRunSetupStep> Steps =
        Array.AsReadOnly(
        [
            FirstRunSetupStep.SteamVrDetection,
            FirstRunSetupStep.VrChatOscDetection,
            FirstRunSetupStep.CameraOscEndpoint,
            FirstRunSetupStep.MicrophonePrivacyAndDevice,
            FirstRunSetupStep.EncoderSelfTest,
            FirstRunSetupStep.SteamVrActionBinding,
            FirstRunSetupStep.WristOverlayPlacement,
            FirstRunSetupStep.TestRecordingPlayback,
            FirstRunSetupStep.LegalBundleVerification,
            FirstRunSetupStep.OfflineLegalAccess,
            FirstRunSetupStep.LocalizationAccessibility,
            FirstRunSetupStep.DesignAssetConformance,
        ]);

    private readonly IFirstRunSetupStore _store;

    public FirstRunSetupController(IFirstRunSetupStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    public const int CurrentSetupVersion = 1;

    public static IReadOnlyList<FirstRunSetupStep> RequiredSteps => Steps;

    public async Task<FirstRunSetupSnapshot> LoadAsync(
        CancellationToken cancellationToken)
    {
        var progress = await LoadCurrentProgressAsync(cancellationToken)
            .ConfigureAwait(false);
        return Project(progress);
    }

    public async Task<FirstRunSetupSnapshot> CompleteAsync(
        FirstRunSetupStep step,
        CancellationToken cancellationToken)
    {
        var progress = await LoadCurrentProgressAsync(cancellationToken)
            .ConfigureAwait(false);
        var currentIndex = progress.CompletedSteps.Count;
        if (currentIndex >= Steps.Count || Steps[currentIndex] != step)
        {
            throw new InvalidOperationException(
                "Only the current first-run setup step can be completed.");
        }

        var completed = progress.CompletedSteps.Append(step).ToArray();
        var updated = new FirstRunSetupProgress(
            CurrentSetupVersion,
            completed);
        await _store.SaveAsync(updated, cancellationToken)
            .ConfigureAwait(false);
        return Project(updated);
    }

    private async Task<FirstRunSetupProgress> LoadCurrentProgressAsync(
        CancellationToken cancellationToken)
    {
        var loaded = await _store.LoadAsync(cancellationToken)
            .ConfigureAwait(false);
        if (loaded is null || loaded.SetupVersion != CurrentSetupVersion)
        {
            return new FirstRunSetupProgress(
                CurrentSetupVersion,
                []);
        }

        ValidateOrderedPrefix(loaded.CompletedSteps);
        return loaded;
    }

    private static void ValidateOrderedPrefix(
        IReadOnlyList<FirstRunSetupStep> completed)
    {
        if (completed.Count > Steps.Count)
        {
            throw new InvalidDataException(
                "First-run setup progress contains too many completed steps.");
        }

        for (var index = 0; index < completed.Count; index++)
        {
            if (completed[index] != Steps[index])
            {
                throw new InvalidDataException(
                    "First-run setup progress is not an ordered prefix.");
            }
        }
    }

    private static FirstRunSetupSnapshot Project(
        FirstRunSetupProgress progress)
    {
        var completedCount = progress.CompletedSteps.Count;
        return new FirstRunSetupSnapshot(
            CurrentSetupVersion,
            completedCount < Steps.Count ? Steps[completedCount] : null,
            completedCount);
    }
}
