using VRRecorder.Application.Presentation;
using VRRecorder.Domain.Recording;

namespace VRRecorder.Application.Desktop;

public sealed class DesktopRecordingAudioUiController
{
    private readonly object _gate = new();
    private DesktopRecordingAudioUiSnapshot? _current;
    private bool _terminal;

    public DesktopRecordingAudioUiSnapshot? Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public DesktopRecordingAudioUiSnapshot? Apply(
        RecorderStatusSnapshot status)
    {
        ArgumentNullException.ThrowIfNull(status);
        lock (_gate)
        {
            if (_terminal ||
                (_current is not null && status.Revision <= _current.Revision))
            {
                return null;
            }

            var audio = status.AudioControlState;
            var isSessionState = status.State is
                RecorderState.Recording or
                RecorderState.SignalLost or
                RecorderState.Stopping;
            var isVisible = isSessionState && audio is not null;
            var isEnabled = isVisible && status.State is
                RecorderState.Recording or RecorderState.SignalLost;
            var microphoneSelected = audio?.MicrophoneIncluded == true;
            var microphoneState = microphoneSelected ? "On" : "Off";
            var snapshot = new DesktopRecordingAudioUiSnapshot(
                status.Revision,
                status.State,
                isVisible,
                isEnabled,
                microphoneSelected,
                audio?.MuteAll == true,
                $"Microphone_{microphoneState}_AccessibleName",
                $"Microphone_{microphoneState}_AccessibleName",
                $"Microphone_{microphoneState}_Tooltip",
                "Audio_MuteAll_Short",
                "Audio_MuteAll_AccessibleName",
                "Audio_MuteAll_Tooltip");
            _current = snapshot;
            _terminal = status.State is
                RecorderState.ComplianceFault or RecorderState.Faulted;
            return snapshot;
        }
    }
}
