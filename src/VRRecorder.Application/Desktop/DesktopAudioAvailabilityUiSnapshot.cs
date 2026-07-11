using VRRecorder.Domain.Audio;

namespace VRRecorder.Application.Desktop;

public sealed record DesktopAudioAvailabilityUiSnapshot(
    long Revision,
    AudioInputAvailability UnavailableInputs,
    string? DisplayResourceKey,
    string? AnnouncementResourceKey,
    DesktopAnnouncementUrgency AnnouncementUrgency)
{
    public bool IsVisible => DisplayResourceKey is not null;
}
