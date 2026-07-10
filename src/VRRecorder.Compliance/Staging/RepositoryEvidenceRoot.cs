namespace VRRecorder.Compliance.Staging;

internal static class RepositoryEvidenceRoot
{
    public static bool TryResolve(
        string repositoryRoot,
        out string canonicalRoot)
    {
        canonicalRoot = string.Empty;
        if (string.IsNullOrWhiteSpace(repositoryRoot) ||
            !Path.IsPathFullyQualified(repositoryRoot))
        {
            return false;
        }

        try
        {
            canonicalRoot = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(repositoryRoot));
            var suppliedRoot = Path.TrimEndingDirectorySeparator(
                repositoryRoot);
            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            if (!string.Equals(
                    canonicalRoot,
                    suppliedRoot,
                    comparison) ||
                !Directory.Exists(canonicalRoot))
            {
                return false;
            }

            return !HasLinkedAncestor(canonicalRoot);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool HasLinkedAncestor(string canonicalRoot)
    {
        for (var directory = new DirectoryInfo(canonicalRoot);
             directory is not null;
             directory = directory.Parent)
        {
            if (directory.LinkTarget is not null ||
                directory.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                return true;
            }
        }

        return false;
    }
}
