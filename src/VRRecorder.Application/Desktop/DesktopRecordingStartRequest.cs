using VRRecorder.Application.Recording;

namespace VRRecorder.Application.Desktop;

public sealed record DesktopRecordingStartRequest
{
    public DesktopRecordingStartRequest(
        string? selectedServiceId,
        StartRecordingCommand command)
    {
        if (selectedServiceId is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(selectedServiceId);
        }

        ArgumentNullException.ThrowIfNull(command);
        SelectedServiceId = selectedServiceId;
        Command = command;
    }

    public string? SelectedServiceId { get; }

    public StartRecordingCommand Command { get; }
}
