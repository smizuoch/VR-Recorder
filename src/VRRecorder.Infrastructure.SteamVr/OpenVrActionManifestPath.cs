namespace VRRecorder.Infrastructure.SteamVr;

public static class OpenVrActionManifestPath
{
    public static string Resolve(string installRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installRoot);
        if (!Path.IsPathFullyQualified(installRoot))
        {
            throw new ArgumentException(
                "The application install root must be absolute.",
                nameof(installRoot));
        }

        var manifestPath = Path.GetFullPath(Path.Combine(
            Path.GetFullPath(installRoot),
            "OpenVr",
            "actions.json"));
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException(
                "The packaged OpenVR action manifest was not found.",
                manifestPath);
        }

        return manifestPath;
    }
}
