using VRRecorder.Application.Audio;
using VRRecorder.Application.Presentation;
using VRRecorder.Domain.Audio;
using VRRecorder.Domain.Recording;

namespace VRRecorder.Application.Desktop;

public sealed class DesktopAudioAvailabilityUiController
{
    private readonly object _gate = new();
    private DesktopAudioAvailabilityUiSnapshot? _current;
    private AudioInputAvailability _unavailableInputs;
    private long _lastNotificationRevision;
    private long _lastStatusRevision = -1;
    private long _nextRevision;
    private bool _acceptAudioEvents;
    private bool _terminal;

    public DesktopAudioAvailabilityUiSnapshot? Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public DesktopAudioAvailabilityUiSnapshot? Apply(
        RecorderStatusSnapshot status)
    {
        ArgumentNullException.ThrowIfNull(status);
        lock (_gate)
        {
            if (_terminal || status.Revision <= _lastStatusRevision)
            {
                return null;
            }

            _lastStatusRevision = status.Revision;
            _acceptAudioEvents = status.State is
                RecorderState.Starting or
                RecorderState.Recording or
                RecorderState.SignalLost;
            _terminal = status.State is
                RecorderState.ComplianceFault or RecorderState.Faulted;

            return status.State switch
            {
                RecorderState.Starting or
                    RecorderState.Recording or
                    RecorderState.SignalLost or
                    RecorderState.Stopping => null,
                RecorderState.Booting or
                    RecorderState.ComplianceFault or
                    RecorderState.Ready or
                    RecorderState.Arming or
                    RecorderState.Countdown or
                    RecorderState.NoSignal or
                    RecorderState.Faulted => Clear(),
                _ => throw new ArgumentOutOfRangeException(
                    nameof(status),
                    status.State,
                    "The recorder state is unsupported."),
            };
        }
    }

    public DesktopAudioAvailabilityUiSnapshot? Apply(
        DesktopRecordingNotification.AudioWarning notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        lock (_gate)
        {
            if (!Accept(notification.Revision))
            {
                return null;
            }

            _ = notification.Warning.Kind switch
            {
                AudioSessionWarningKind.InputUnavailable => true,
                AudioSessionWarningKind.EndpointRediscoveryFailed => true,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(notification),
                    notification.Warning.Kind,
                    "The audio warning kind is unsupported."),
            };
            var input = ToAvailability(notification.Warning.Input);
            if ((_unavailableInputs & input) == input)
            {
                return null;
            }

            _unavailableInputs |= input;
            return CreateSnapshot(
                DisplayResourceKey(_unavailableInputs),
                UnavailableResourceKey(notification.Warning.Input),
                DesktopAnnouncementUrgency.Assertive);
        }
    }

    public DesktopAudioAvailabilityUiSnapshot? Apply(
        DesktopRecordingNotification.AudioRecovered notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        lock (_gate)
        {
            if (!Accept(notification.Revision) ||
                notification.Recovery.Kind !=
                    AudioSessionStatusKind.InputRecovered)
            {
                return null;
            }

            var input = ToAvailability(notification.Recovery.Input);
            if ((_unavailableInputs & input) == 0)
            {
                return null;
            }

            _unavailableInputs &= ~input;
            var recovered = RecoveredResourceKey(notification.Recovery.Input);
            return CreateSnapshot(
                _unavailableInputs == AudioInputAvailability.None
                    ? recovered
                    : DisplayResourceKey(_unavailableInputs),
                recovered,
                DesktopAnnouncementUrgency.Polite);
        }
    }

    private bool Accept(long notificationRevision)
    {
        if (notificationRevision <= _lastNotificationRevision)
        {
            return false;
        }

        _lastNotificationRevision = notificationRevision;
        return !_terminal && _acceptAudioEvents;
    }

    private DesktopAudioAvailabilityUiSnapshot? Clear()
    {
        _unavailableInputs = AudioInputAvailability.None;
        if (_current is null || !_current.IsVisible)
        {
            return null;
        }

        return CreateSnapshot(
            displayResourceKey: null,
            announcementResourceKey: null,
            DesktopAnnouncementUrgency.None);
    }

    private DesktopAudioAvailabilityUiSnapshot CreateSnapshot(
        string? displayResourceKey,
        string? announcementResourceKey,
        DesktopAnnouncementUrgency urgency)
    {
        _current = new DesktopAudioAvailabilityUiSnapshot(
            checked(++_nextRevision),
            _unavailableInputs,
            displayResourceKey,
            announcementResourceKey,
            urgency);
        return _current;
    }

    private static AudioInputAvailability ToAvailability(
        AudioInput input) => input switch
        {
            AudioInput.Desktop => AudioInputAvailability.Desktop,
            AudioInput.Microphone => AudioInputAvailability.Microphone,
            _ => throw new ArgumentOutOfRangeException(
                nameof(input),
                input,
                "The audio input is unsupported."),
        };

    private static string DisplayResourceKey(
        AudioInputAvailability unavailableInputs) =>
        unavailableInputs switch
        {
            AudioInputAvailability.Desktop =>
                "Recording_Notification_Audio_DesktopUnavailable",
            AudioInputAvailability.Microphone =>
                "Recording_Notification_Audio_MicrophoneUnavailable",
            AudioInputAvailability.All =>
                "Recording_Notification_Audio_BothUnavailable",
            _ => throw new ArgumentOutOfRangeException(
                nameof(unavailableInputs),
                unavailableInputs,
                "The unavailable audio inputs are unsupported."),
        };

    private static string UnavailableResourceKey(AudioInput input) =>
        input switch
        {
            AudioInput.Desktop =>
                "Recording_Notification_Audio_DesktopUnavailable",
            AudioInput.Microphone =>
                "Recording_Notification_Audio_MicrophoneUnavailable",
            _ => throw new ArgumentOutOfRangeException(
                nameof(input),
                input,
                "The unavailable audio input is unsupported."),
        };

    private static string RecoveredResourceKey(AudioInput input) =>
        input switch
        {
            AudioInput.Desktop =>
                "Recording_Notification_Audio_DesktopRecovered",
            AudioInput.Microphone =>
                "Recording_Notification_Audio_MicrophoneRecovered",
            _ => throw new ArgumentOutOfRangeException(
                nameof(input),
                input,
                "The recovered audio input is unsupported."),
        };
}
