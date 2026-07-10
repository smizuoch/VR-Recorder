namespace VRRecorder.Compliance.Generation;

internal static class LegalArtifactPath
{
    public static string Resolve(string rootDirectory, string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        if (Path.IsPathRooted(relativePath) || relativePath.Contains('\\'))
        {
            throw new ArgumentException(
                "Legal artifact paths must be forward-slash relative paths.",
                nameof(relativePath));
        }

        var segments = relativePath.Split('/');
        if (segments.Any(segment =>
                string.IsNullOrWhiteSpace(segment) ||
                segment is "." or ".." ||
                segment.Contains(':') ||
                segment.EndsWith(' ') ||
                segment.EndsWith('.')))
        {
            throw new ArgumentException(
                "The legal artifact path is not package-safe.",
                nameof(relativePath));
        }

        var root = Path.GetFullPath(rootDirectory);
        var fullPath = Path.GetFullPath(Path.Combine(
            root,
            Path.Combine(segments)));
        var rootWithSeparator = Path.TrimEndingDirectorySeparator(root) +
                                Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!fullPath.StartsWith(rootWithSeparator, comparison))
        {
            throw new ArgumentException(
                "The legal artifact path escapes its output directory.",
                nameof(relativePath));
        }

        return fullPath;
    }
}
