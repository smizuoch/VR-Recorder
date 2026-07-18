using VRRecorder.Application.Presentation;
using VRRecorder.Application.Settings;

namespace VRRecorder.Presentation.Wrist;

public interface IWristTelemetrySource
{
    WristTelemetrySnapshot? Capture(
        RecorderStatusSnapshot status,
        OverlayPlacementMode placementMode,
        IUiLocalizer localizer);
}

public sealed class NullWristTelemetrySource : IWristTelemetrySource
{
    public static NullWristTelemetrySource Instance { get; } = new();

    private NullWristTelemetrySource()
    {
    }

    public WristTelemetrySnapshot? Capture(
        RecorderStatusSnapshot status,
        OverlayPlacementMode placementMode,
        IUiLocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(localizer);
        return null;
    }
}
