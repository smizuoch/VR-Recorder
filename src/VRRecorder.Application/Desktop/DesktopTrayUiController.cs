using VRRecorder.Application.Presentation;
using VRRecorder.Domain.Recording;

namespace VRRecorder.Application.Desktop;

public sealed class DesktopTrayUiController
{
    private readonly object _gate = new();
    private readonly DesktopRecordingUiController _recording = new();
    private DesktopTrayUiSnapshot? _current;

    public DesktopTrayUiSnapshot? Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public DesktopTrayUiSnapshot? Apply(RecorderStatusSnapshot status)
    {
        ArgumentNullException.ThrowIfNull(status);
        lock (_gate)
        {
            var recording = _recording.Apply(status);
            if (recording is null)
            {
                return null;
            }

            var state = ProjectState(recording.State);
            _current = new DesktopTrayUiSnapshot(
                recording.Revision,
                state,
                $"Tray_State_{state}",
                recording.ActionLabelResourceKey,
                recording.IsActionEnabled);
            return _current;
        }
    }

    private static DesktopTrayState ProjectState(RecorderState state) =>
        state switch
        {
            RecorderState.Ready => DesktopTrayState.Ready,
            RecorderState.Recording => DesktopTrayState.Recording,
            RecorderState.Faulted or RecorderState.ComplianceFault =>
                DesktopTrayState.Fault,
            _ => DesktopTrayState.Warning,
        };
}
