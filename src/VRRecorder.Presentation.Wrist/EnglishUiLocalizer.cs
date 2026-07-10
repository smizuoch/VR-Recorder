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
