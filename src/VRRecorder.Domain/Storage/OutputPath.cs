namespace VRRecorder.Domain.Storage;

public sealed record OutputPath
{
    public OutputPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException(
                "The output directory must be an absolute path.",
                nameof(path));
        }

        FullPath = Path.GetFullPath(path);
    }

    public string FullPath { get; }
}
