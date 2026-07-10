using VRRecorder.Application.Ports;
using VRRecorder.Domain.Storage;

namespace VRRecorder.Application.Desktop;

public sealed class RecordingOutputPathResolver
{
    private const string DownloadsKnownFolderToken = "knownfolder:Downloads";
    private const string KnownFolderTokenPrefix = "knownfolder:";
    private readonly IDefaultOutputPathProvider _defaultOutputPaths;

    public RecordingOutputPathResolver(
        IDefaultOutputPathProvider defaultOutputPaths)
    {
        ArgumentNullException.ThrowIfNull(defaultOutputPaths);
        _defaultOutputPaths = defaultOutputPaths;
    }

    public OutputPath Resolve(string configuredPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configuredPath);
        if (string.Equals(
                configuredPath,
                DownloadsKnownFolderToken,
                StringComparison.Ordinal))
        {
            return _defaultOutputPaths.GetDefault() ??
                   throw new InvalidDataException(
                       "The Downloads known folder could not be resolved.");
        }

        if (configuredPath.StartsWith(
                KnownFolderTokenPrefix,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The configured output known-folder token is not supported.");
        }

        if (configuredPath.Any(char.IsControl))
        {
            throw new InvalidDataException(
                "The configured output path contains control characters.");
        }

        try
        {
            return new OutputPath(configuredPath);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException(
                "The configured output path must be a safe absolute path.",
                exception);
        }
    }
}
