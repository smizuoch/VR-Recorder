namespace VRRecorder.Application.Diagnostics;

public sealed record DiagnosticBundleExport
{
    public DiagnosticBundleExport(string bundlePath, int eventCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bundlePath);
        if (!Path.IsPathFullyQualified(bundlePath))
        {
            throw new ArgumentException(
                "The diagnostic bundle path must be absolute.",
                nameof(bundlePath));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(eventCount);
        BundlePath = Path.GetFullPath(bundlePath);
        EventCount = eventCount;
    }

    public string BundlePath { get; }

    public int EventCount { get; }
}
