using VRRecorder.Application.Ports;
using VRRecorder.Application.Setup;

namespace VRRecorder.Infrastructure.SteamVr;

public sealed class WindowsSteamVrInstallationProbe : IFirstRunSetupProbe
{
    private readonly ISteamVrRegistrationReader _registration;

    public WindowsSteamVrInstallationProbe()
        : this(new WindowsSteamVrRegistrationReader())
    {
    }

    public WindowsSteamVrInstallationProbe(
        ISteamVrRegistrationReader registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        _registration = registration;
    }

    public Task<bool> VerifyAsync(
        FirstRunSetupStep setupStep,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (setupStep != FirstRunSetupStep.SteamVrDetection)
        {
            return Task.FromResult(false);
        }

        var installed = _registration.ReadInstalledMarkers()
            .Any(marker => marker == 1);
        return Task.FromResult(installed);
    }
}
