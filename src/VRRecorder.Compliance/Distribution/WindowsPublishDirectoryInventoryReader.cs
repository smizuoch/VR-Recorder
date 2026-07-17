using System.Collections.ObjectModel;
using System.Globalization;
using VRRecorder.Compliance.Staging;

namespace VRRecorder.Compliance.Distribution;

public sealed class WindowsPublishDirectoryInventory
{
    internal WindowsPublishDirectoryInventory(
        string rootDirectory,
        string entryPoint,
        string entryPointSha256,
        string inventorySha256,
        IReadOnlyList<StagedPayloadFile> files)
    {
        RootDirectory = rootDirectory;
        EntryPoint = entryPoint;
        EntryPointSha256 = entryPointSha256;
        InventorySha256 = inventorySha256;
        Files = new ReadOnlyCollection<StagedPayloadFile>(files.ToArray());
    }

    public string RootDirectory { get; }

    public string EntryPoint { get; }

    public string EntryPointSha256 { get; }

    public string InventorySha256 { get; }

    public IReadOnlyList<StagedPayloadFile> Files { get; }
}

public sealed record WindowsPublishDirectoryAdmission(
    WindowsPublishDirectoryInventory? Inventory,
    IReadOnlyList<ComplianceIssue> Issues)
{
    public bool IsAdmitted => Inventory is not null && Issues.Count == 0;
}

public sealed class WindowsPublishDirectoryInventoryReader
{
    private const int MaximumFileCount = 32_768;
    private readonly IStagingInventoryReader _inventoryReader;
    private readonly IWindowsRuntimeFileSemanticsVerifier _fileSemantics;

    public WindowsPublishDirectoryInventoryReader()
        : this(
            new FileSystemStagingInventoryReader(),
            WindowsRuntimeFileSemanticsVerifier.Instance)
    {
    }

    internal WindowsPublishDirectoryInventoryReader(
        IStagingInventoryReader inventoryReader,
        IWindowsRuntimeFileSemanticsVerifier fileSemantics)
    {
        ArgumentNullException.ThrowIfNull(inventoryReader);
        ArgumentNullException.ThrowIfNull(fileSemantics);
        _inventoryReader = inventoryReader;
        _fileSemantics = fileSemantics;
    }

    public async Task<WindowsPublishDirectoryAdmission> ReadAsync(
        string rootDirectory,
        string entryPoint,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(entryPoint);
        cancellationToken.ThrowIfCancellationRequested();
        if (!RepositoryEvidenceRoot.TryResolve(
                rootDirectory,
                out var root))
        {
            return Reject("invalid-publish-directory-root", rootDirectory);
        }

        string canonicalEntryPoint;
        try
        {
            canonicalEntryPoint = WindowsRuntimeRelativePath
                .RequireCanonical(entryPoint, nameof(entryPoint));
        }
        catch (ArgumentException)
        {
            return Reject("invalid-publish-entrypoint", entryPoint);
        }

        if (!string.Equals(
                Path.GetExtension(canonicalEntryPoint),
                ".exe",
                StringComparison.OrdinalIgnoreCase))
        {
            return Reject("invalid-publish-entrypoint", entryPoint);
        }

        StagingInventory scanned;
        try
        {
            scanned = await _inventoryReader
                .ReadAsync(root, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is
            IOException or UnauthorizedAccessException or
            InvalidOperationException or ArgumentException)
        {
            return Reject("publish-directory-read-failed", root);
        }

        var issues = new List<ComplianceIssue>(scanned.ScanIssues);
        if (scanned.Files.Count is 0 or > MaximumFileCount)
        {
            issues.Add(new ComplianceIssue(
                "invalid-publish-inventory-file-count",
                scanned.Files.Count.ToString(CultureInfo.InvariantCulture)));
        }

        var duplicatePaths = scanned.Files
            .GroupBy(file =>
                file.RelativePath,
                StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        issues.AddRange(duplicatePaths.Select(path => new ComplianceIssue(
            "duplicate-publish-inventory-path",
            path)));

        foreach (var file in scanned.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                _ = WindowsRuntimeRelativePath.RequireCanonical(
                    file.RelativePath,
                    nameof(file.RelativePath));
                _fileSemantics.VerifyRegularFile(
                    WindowsRuntimeRelativePath.Resolve(
                        root,
                        file.RelativePath));
            }
            catch (Exception exception) when (exception is
                ArgumentException or InvalidDataException or IOException or
                UnauthorizedAccessException)
            {
                issues.Add(new ComplianceIssue(
                    "invalid-publish-file-semantics",
                    file.RelativePath));
            }
        }

        var entryMatches = scanned.Files.Where(file => string.Equals(
                file.RelativePath,
                canonicalEntryPoint,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (entryMatches.Length == 0)
        {
            issues.Add(new ComplianceIssue(
                "publish-entrypoint-missing",
                canonicalEntryPoint));
        }
        else if (entryMatches.Length != 1 ||
                 !string.Equals(
                     entryMatches[0].RelativePath,
                     canonicalEntryPoint,
                     StringComparison.Ordinal) ||
                 entryMatches[0].Kind != StagedArtifactKind.Executable)
        {
            issues.Add(new ComplianceIssue(
                "publish-entrypoint-inventory-mismatch",
                canonicalEntryPoint));
        }
        else
        {
            try
            {
                var bytes = await File.ReadAllBytesAsync(
                        WindowsRuntimeRelativePath.Resolve(
                            root,
                            canonicalEntryPoint),
                        cancellationToken)
                    .ConfigureAwait(false);
                var image = WindowsPeImageAdmissionReader.Read(
                    Path.GetFileName(canonicalEntryPoint),
                    bytes);
                if (image.IsDll || !image.HasEntryPoint ||
                    image.Subsystem != WindowsPeSubsystem.Gui)
                {
                    throw new InvalidDataException();
                }
            }
            catch (Exception exception) when (exception is
                InvalidDataException or IOException or
                UnauthorizedAccessException or ArgumentException)
            {
                issues.Add(new ComplianceIssue(
                    "invalid-publish-entrypoint-pe",
                    canonicalEntryPoint));
            }
        }

        if (issues.Count != 0 || entryMatches.Length != 1)
        {
            return Reject(issues);
        }

        var files = scanned.Files
            .OrderBy(file => file.RelativePath, StringComparer.Ordinal)
            .ToArray();
        return new WindowsPublishDirectoryAdmission(
            new WindowsPublishDirectoryInventory(
                root,
                canonicalEntryPoint,
                entryMatches[0].Sha256,
                WindowsPublishInventoryDigest.Compute(files),
                files),
            []);
    }

    private static WindowsPublishDirectoryAdmission Reject(
        string code,
        string subject) => Reject([new ComplianceIssue(code, subject)]);

    private static WindowsPublishDirectoryAdmission Reject(
        IEnumerable<ComplianceIssue> issues) => new(
        null,
        issues.OrderBy(issue => issue.Code, StringComparer.Ordinal)
            .ThenBy(issue => issue.Subject, StringComparer.Ordinal)
            .ToArray());
}
