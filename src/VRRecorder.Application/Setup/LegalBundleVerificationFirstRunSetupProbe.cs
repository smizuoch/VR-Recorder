using VRRecorder.Application.Desktop;
using VRRecorder.Application.Ports;

namespace VRRecorder.Application.Setup;

public sealed class LegalBundleVerificationFirstRunSetupProbe
    : IFirstRunSetupProbe
{
    private readonly ISettingsStore _settings;
    private readonly RecordingOutputPathResolver _outputPaths;
    private readonly ILegalBundleOutputMirror _mirror;

    public LegalBundleVerificationFirstRunSetupProbe(
        ISettingsStore settings,
        RecordingOutputPathResolver outputPaths,
        ILegalBundleOutputMirror mirror)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(outputPaths);
        ArgumentNullException.ThrowIfNull(mirror);
        _settings = settings;
        _outputPaths = outputPaths;
        _mirror = mirror;
    }

    public async Task<bool> VerifyAsync(
        FirstRunSetupStep setupStep,
        CancellationToken cancellationToken)
    {
        if (setupStep != FirstRunSetupStep.LegalBundleVerification)
        {
            return false;
        }

        var settings = await _settings.LoadAsync(cancellationToken)
            .ConfigureAwait(false);
        ArgumentNullException.ThrowIfNull(settings);
        var outputPath = _outputPaths.Resolve(
            settings.Recording.OutputFolder);
        await _mirror.MirrorAsync(outputPath, cancellationToken)
            .ConfigureAwait(false);
        return true;
    }
}
