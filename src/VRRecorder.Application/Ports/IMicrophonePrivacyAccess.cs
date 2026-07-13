namespace VRRecorder.Application.Ports;

public interface IMicrophonePrivacyAccess
{
    Task<bool> IsAllowedAsync(CancellationToken cancellationToken);
}
