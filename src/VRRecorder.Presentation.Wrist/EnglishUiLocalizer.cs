using VRRecorder.DesignSystem;

namespace VRRecorder.Presentation.Wrist;

public sealed class EnglishUiLocalizer : IUiLocalizer
{
    private static readonly Dictionary<string, string> Values =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["recording.start.short"] = "REC",
            ["recording.start.accessible"] = "Start recording",
            ["recording.stop.short"] = "STOP",
            ["recording.stop.accessible"] = "Stop recording",
            ["camera.retry.short"] = "RETRY",
            ["camera.retry.accessible"] = "Retry camera connection",
            ["state.booting.label"] = "Starting",
            ["state.compliance-fault.label"] = "Compliance check failed",
            ["state.ready.label"] = "Ready",
            ["state.arming.label"] = "Connecting camera",
            ["state.countdown.label"] = "Countdown",
            ["state.starting.label"] = "Starting recording",
            ["state.recording.label"] = "Recording",
            ["state.signal-lost.label"] = "Camera signal lost",
            ["state.stopping.label"] = "Saving recording",
            ["state.no-signal.label"] = "No camera signal",
            ["state.faulted.label"] = "Recorder error",
        };

    private EnglishUiLocalizer()
    {
    }

    public static EnglishUiLocalizer Instance { get; } = new();

    public LocalizedText Resolve(string resourceKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceKey);
        return Values.TryGetValue(resourceKey, out var value)
            ? new LocalizedText(resourceKey, value)
            : throw new KeyNotFoundException(
                $"No English UI resource exists for {resourceKey}.");
    }
}
