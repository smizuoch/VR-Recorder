using VRRecorder.Compliance.Generation;

namespace VRRecorder.Compliance.Staging;

internal sealed class WindowsRuntimeStagingOrchestrator
{
    private const int MaximumManifestBytes = 1024 * 1024;
    private readonly IWindowsRuntimeStagingPlatformGate _platformGate;
    private readonly IWindowsRuntimeStagingAdmissionPlanner _planner;
    private readonly IWindowsRuntimeStagingPublisher _publisher;

    public WindowsRuntimeStagingOrchestrator()
        : this(
            WindowsRuntimeStagingPlatformGate.Default,
            new WindowsRuntimeStagingAdmissionPlanner(),
            new ImmutableWindowsRuntimeStagingPublisher())
    {
    }

    internal WindowsRuntimeStagingOrchestrator(
        IWindowsRuntimeStagingPlatformGate platformGate,
        IWindowsRuntimeStagingAdmissionPlanner planner,
        IWindowsRuntimeStagingPublisher publisher)
    {
        ArgumentNullException.ThrowIfNull(platformGate);
        ArgumentNullException.ThrowIfNull(planner);
        ArgumentNullException.ThrowIfNull(publisher);
        _platformGate = platformGate;
        _planner = planner;
        _publisher = publisher;
    }

    public async Task<WindowsRuntimeStagingResult> StageAsync(
        WindowsRuntimeStagingRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var platformIssue = _platformGate.Validate();
        if (platformIssue is not null)
        {
            return Reject(platformIssue);
        }

        WindowsRuntimeStagingManifest manifest;
        try
        {
            manifest = await ReadManifestAsync(
                    request.ManifestPath,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is InvalidDataException or
                IOException or
                UnauthorizedAccessException or
                InvalidOperationException or
                ArgumentException)
        {
            return Reject(new ComplianceIssue(
                "invalid-windows-runtime-staging-manifest",
                request.ManifestPath));
        }

        var admission = await _planner
            .PlanAsync(
                manifest,
                request.SourceRoot,
                request.ApprovedGraph,
                request.RepositoryRoot,
                cancellationToken)
            .ConfigureAwait(false);
        if (!admission.IsAdmitted || admission.Plan is null)
        {
            return Reject(admission.Issues);
        }

        try
        {
            var publication = await _publisher
                .PublishAsync(
                    admission.Plan,
                    request.OutputParent,
                    cancellationToken)
                .ConfigureAwait(false);
            return new WindowsRuntimeStagingResult(publication, []);
        }
        catch (Exception exception) when (
            exception is InvalidDataException or
                IOException or
                UnauthorizedAccessException or
                InvalidOperationException or
                ArgumentException or
                PlatformNotSupportedException)
        {
            return Reject(new ComplianceIssue(
                "windows-runtime-staging-publication-failed",
                request.OutputParent));
        }
    }

    private static async Task<WindowsRuntimeStagingManifest> ReadManifestAsync(
        string manifestPath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);
        if (!Path.IsPathFullyQualified(manifestPath))
        {
            throw new InvalidDataException(
                "The Windows runtime staging manifest path must be absolute.");
        }

        var path = Path.GetFullPath(manifestPath);
        if (!string.Equals(path, manifestPath, PathComparison))
        {
            throw new InvalidDataException(
                "The Windows runtime staging manifest path must be canonical.");
        }

        EnsureRegularUnlinkedFile(path);
        WindowsRuntimeFileSemanticsVerifier.Instance.VerifyRegularFile(path);
        var file = new FileInfo(path);
        if (file.Length is <= 0 or > MaximumManifestBytes)
        {
            throw new InvalidDataException(
                "The Windows runtime staging manifest size is invalid.");
        }

        var expectedLength = checked((int)file.Length);
        var content = new byte[expectedLength];
        await using (var input = new FileStream(
                         path,
                         FileMode.Open,
                         FileAccess.Read,
                         FileShare.Read,
                         bufferSize: 81920,
                         FileOptions.Asynchronous |
                         FileOptions.SequentialScan))
        {
            await input
                .ReadExactlyAsync(content, cancellationToken)
                .ConfigureAwait(false);
            if (input.ReadByte() != -1)
            {
                throw new InvalidDataException(
                    "The Windows runtime staging manifest changed while reading.");
            }
        }

        EnsureRegularUnlinkedFile(path);
        WindowsRuntimeFileSemanticsVerifier.Instance.VerifyRegularFile(path);
        if (new FileInfo(path).Length != expectedLength)
        {
            throw new InvalidDataException(
                "The Windows runtime staging manifest changed while reading.");
        }

        return WindowsRuntimeStagingManifestReader.Read(content);
    }

    private static void EnsureRegularUnlinkedFile(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists || IsLink(file))
        {
            throw new InvalidDataException(
                "The Windows runtime staging manifest must be a regular file.");
        }

        for (var directory = file.Directory;
             directory is not null;
             directory = directory.Parent)
        {
            if (!directory.Exists || IsLink(directory))
            {
                throw new InvalidDataException(
                    "The Windows runtime staging manifest cannot traverse a link.");
            }
        }
    }

    private static bool IsLink(FileSystemInfo entry)
    {
        entry.Refresh();
        return entry.LinkTarget is not null ||
               entry.Attributes.HasFlag(FileAttributes.ReparsePoint);
    }

    private static WindowsRuntimeStagingResult Reject(ComplianceIssue issue) =>
        Reject([issue]);

    private static WindowsRuntimeStagingResult Reject(
        IEnumerable<ComplianceIssue> issues) => new(
        null,
        issues.OrderBy(issue => issue.Code, StringComparer.Ordinal)
            .ThenBy(issue => issue.Subject, StringComparer.Ordinal)
            .ToArray());

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
}

internal sealed record WindowsRuntimeStagingRequest(
    string ManifestPath,
    string SourceRoot,
    string OutputParent,
    string RepositoryRoot,
    ApprovedReleaseGraph ApprovedGraph);

internal sealed record WindowsRuntimeStagingResult(
    WindowsRuntimeStagingPublication? Publication,
    IReadOnlyList<ComplianceIssue> Issues)
{
    public bool IsStaged => Publication is not null && Issues.Count == 0;
}

internal interface IWindowsRuntimeStagingPlatformGate
{
    ComplianceIssue? Validate();
}

internal static class WindowsRuntimeStagingPlatformGate
{
    public static IWindowsRuntimeStagingPlatformGate Default { get; } =
        new RequireWindows();

    public static IWindowsRuntimeStagingPlatformGate AllowForPortableTests
    { get; } = new Allow();

    private sealed class RequireWindows : IWindowsRuntimeStagingPlatformGate
    {
        public ComplianceIssue? Validate() => OperatingSystem.IsWindows()
            ? null
            : new ComplianceIssue(
                "windows-runtime-staging-requires-windows",
                "windows-x64");
    }

    private sealed class Allow : IWindowsRuntimeStagingPlatformGate
    {
        public ComplianceIssue? Validate() => null;
    }
}
