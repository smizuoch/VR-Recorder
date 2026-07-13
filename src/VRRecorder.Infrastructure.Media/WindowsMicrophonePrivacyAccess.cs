using VRRecorder.Application.Ports;

namespace VRRecorder.Infrastructure.Media;

public sealed class WindowsMicrophonePrivacyAccess
    : IMicrophonePrivacyAccess
{
    private readonly IMicrophonePrivacyRegistrationReader _registration;

    public WindowsMicrophonePrivacyAccess()
        : this(new WindowsMicrophonePrivacyRegistrationReader())
    {
    }

    public WindowsMicrophonePrivacyAccess(
        IMicrophonePrivacyRegistrationReader registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        _registration = registration;
    }

    public Task<bool> IsAllowedAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var allowed = string.Equals(
            _registration.ReadConsentValue(),
            "Allow",
            StringComparison.OrdinalIgnoreCase);
        return Task.FromResult(allowed);
    }
}
