using VRRecorder.Application.Presentation;
using VRRecorder.Domain.Recording;

namespace VRRecorder.Application.Desktop;

public sealed class DesktopRecordingUiController
{
    private readonly object _gate = new();
    private DesktopRecordingUiSnapshot? _current;
    private bool _terminal;

    public DesktopRecordingUiSnapshot? Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public DesktopRecordingUiSnapshot? Apply(RecorderStatusSnapshot status)
    {
        ArgumentNullException.ThrowIfNull(status);
        lock (_gate)
        {
            if (_terminal ||
                (_current is not null && status.Revision <= _current.Revision))
            {
                return null;
            }

            var action = PrimaryAction(status.State);
            var snapshot = new DesktopRecordingUiSnapshot(
                status.Revision,
                status.State,
                $"Recording_State_{status.State}",
                $"Status_{status.State}_AccessibleDescription",
                action.Label,
                action.AccessibleName,
                action.Help,
                IsEnabled(status, action.RequiredAction));
            _current = snapshot;
            _terminal = status.State is
                RecorderState.ComplianceFault or RecorderState.Faulted;
            return snapshot;
        }
    }

    private static PrimaryActionDescriptor PrimaryAction(
        RecorderState state) =>
        state switch
        {
            RecorderState.Arming or RecorderState.Countdown => new(
                "Recording_Action_Cancel_Short",
                "Recording_Action_Cancel_AccessibleName",
                "Recording_Action_Cancel_Tooltip",
                RecorderAvailableActions.Cancel),
            RecorderState.Recording or
                RecorderState.SignalLost or
                RecorderState.Stopping => new(
                    "Recording_Stop_Short",
                    "Recording_Stop_AccessibleName",
                    "Recording_Stop_Tooltip",
                    RecorderAvailableActions.Stop),
            RecorderState.NoSignal => new(
                "Recording_Action_Retry_Short",
                "Recording_Action_Retry_AccessibleName",
                "Recording_Action_Retry_Tooltip",
                RecorderAvailableActions.Retry),
            _ => new(
                "Recording_Start_Short",
                "Recording_Start_AccessibleName",
                "Recording_Start_Tooltip",
                RecorderAvailableActions.Start),
        };

    private static bool IsEnabled(
        RecorderStatusSnapshot status,
        RecorderAvailableActions requiredAction) =>
        (status.AvailableActions & requiredAction) == requiredAction;

    private sealed record PrimaryActionDescriptor(
        string Label,
        string AccessibleName,
        string Help,
        RecorderAvailableActions RequiredAction);
}
