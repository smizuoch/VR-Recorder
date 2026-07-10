namespace VRRecorder.Presentation.Wrist;

internal static class WristLegalTextLines
{
    public static IReadOnlyList<string> Split(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var lines = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
        return lines.Length > 1 && lines[^1].Length == 0
            ? lines[..^1]
            : lines;
    }
}
