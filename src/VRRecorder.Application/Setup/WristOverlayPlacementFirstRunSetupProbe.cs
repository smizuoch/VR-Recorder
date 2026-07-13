using VRRecorder.Application.Ports;
using VRRecorder.Application.Settings;

namespace VRRecorder.Application.Setup;

public sealed class WristOverlayPlacementFirstRunSetupProbe
    : IFirstRunSetupProbe
{
    private readonly ISettingsStore _settings;
    private readonly IWristOverlayPlacementVerifier _verifier;

    public WristOverlayPlacementFirstRunSetupProbe(
        ISettingsStore settings,
        IWristOverlayPlacementVerifier verifier)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(verifier);
        _settings = settings;
        _verifier = verifier;
    }

    public async Task<bool> VerifyAsync(
        FirstRunSetupStep setupStep,
        CancellationToken cancellationToken)
    {
        if (setupStep != FirstRunSetupStep.WristOverlayPlacement)
        {
            return false;
        }

        var settings = await _settings.LoadAsync(cancellationToken)
            .ConfigureAwait(false);
        ArgumentNullException.ThrowIfNull(settings);
        var evidence = await _verifier
            .VerifyAsync(settings.Vr, cancellationToken)
            .ConfigureAwait(false);
        return evidence is not null &&
               evidence.IsVisible &&
               evidence.IsReadableConfirmed &&
               evidence.IsInteractionUnobstructedConfirmed &&
               evidence.PlacementMode == settings.Vr.PlacementMode &&
               SameTransform(evidence.AppliedTransform, settings.Vr.Transform);
    }

    private static bool SameTransform(
        OverlayTransform actual,
        OverlayTransform expected) =>
        actual is not null &&
        expected is not null &&
        actual.Position.AsSpan().SequenceEqual(expected.Position) &&
        actual.RotationEuler.AsSpan().SequenceEqual(expected.RotationEuler);
}
