using System.Buffers;

namespace VRRecorder.Compliance.Staging;

internal static class WindowsRuntimeRelativePath
{
    private const int MaximumPathLength = 1024;
    private static readonly SearchValues<char> InvalidNameCharacters =
        SearchValues.Create(
            ['<', '>', ':', '"', '\\', '|', '?', '*', '%', '$', '@',
                '(', ')', ';']);

    public static string RequireCanonical(string path, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            path.Length > MaximumPathLength ||
            path[0] == '/' ||
            path.Contains('\\') ||
            IsDriveQualified(path))
        {
            throw Invalid(propertyName);
        }

        var segments = path.Split('/');
        if (segments.Any(segment => !IsSafeSegment(segment)))
        {
            throw Invalid(propertyName);
        }

        return path;
    }

    public static string Resolve(string root, string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        RequireCanonical(relativePath, nameof(relativePath));

        var fullRoot = Path.GetFullPath(root);
        var fullPath = Path.GetFullPath(Path.Combine(
            fullRoot,
            Path.Combine(relativePath.Split('/'))));
        var rootPrefix = Path.TrimEndingDirectorySeparator(fullRoot) +
                         Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!fullPath.StartsWith(rootPrefix, comparison))
        {
            throw Invalid(nameof(relativePath));
        }

        return fullPath;
    }

    private static bool IsDriveQualified(string path) =>
        path.Length >= 2 &&
        path[1] == ':' &&
        path[0] is >= 'A' and <= 'Z' or >= 'a' and <= 'z';

    private static bool IsSafeSegment(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment) ||
            segment is "." or ".." ||
            segment.EndsWith(' ') ||
            segment.EndsWith('.') ||
            segment.AsSpan().IndexOfAny(InvalidNameCharacters) >= 0 ||
            segment.Any(character => character == '\0' || character < ' '))
        {
            return false;
        }

        var baseName = segment.Split('.', 2)[0];
        return !IsReservedDeviceName(baseName);
    }

    private static bool IsReservedDeviceName(string baseName)
    {
        if (baseName.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
            baseName.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
            baseName.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
            baseName.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
            baseName.Equals("CONIN$", StringComparison.OrdinalIgnoreCase) ||
            baseName.Equals("CONOUT$", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (baseName.Length != 4 ||
            !(baseName.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
              baseName.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return baseName[3] is >= '0' and <= '9' or '\u00b9' or '\u00b2' or
            '\u00b3';
    }

    private static ArgumentException Invalid(string propertyName) =>
        new(
            "Windows runtime paths must be canonical package-relative paths.",
            propertyName);
}
