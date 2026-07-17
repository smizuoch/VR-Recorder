using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace VRRecorder.Compliance.Staging;

internal sealed class ImmutableWindowsRuntimeStagingPublisher
    : IWindowsRuntimeStagingPublisher
{
    private const int BufferSize = 81920;
    private const int MaximumEntryCount = 4096;
    private const string PropsFileName = "ApprovedWindowsRuntime.props";
    private const string PublicationPrefix = "windows-runtime-";
    private const string TemporaryPublicationPrefix = ".wrs-";
    private readonly IWindowsRuntimeStagingFaultInjector _faultInjector;
    private readonly IWindowsRuntimeDirectoryCommitter _committer;
    private readonly IWindowsRuntimeFileSemanticsVerifier _fileSemantics;
    private readonly FileSystemStagingInventoryReader _inventoryReader = new();

    public ImmutableWindowsRuntimeStagingPublisher()
        : this(
            WindowsRuntimeStagingFaultInjector.None,
            DirectoryMoveWindowsRuntimeCommitter.Instance,
            WindowsRuntimeFileSemanticsVerifier.Instance)
    {
    }

    internal ImmutableWindowsRuntimeStagingPublisher(
        IWindowsRuntimeStagingFaultInjector faultInjector)
        : this(
            faultInjector,
            DirectoryMoveWindowsRuntimeCommitter.Instance,
            WindowsRuntimeFileSemanticsVerifier.Instance)
    {
    }

    internal ImmutableWindowsRuntimeStagingPublisher(
        IWindowsRuntimeStagingFaultInjector faultInjector,
        IWindowsRuntimeDirectoryCommitter committer)
        : this(
            faultInjector,
            committer,
            WindowsRuntimeFileSemanticsVerifier.Instance)
    {
    }

    internal ImmutableWindowsRuntimeStagingPublisher(
        IWindowsRuntimeStagingFaultInjector faultInjector,
        IWindowsRuntimeDirectoryCommitter committer,
        IWindowsRuntimeFileSemanticsVerifier fileSemantics)
    {
        ArgumentNullException.ThrowIfNull(faultInjector);
        ArgumentNullException.ThrowIfNull(committer);
        ArgumentNullException.ThrowIfNull(fileSemantics);
        _faultInjector = faultInjector;
        _committer = committer;
        _fileSemantics = fileSemantics;
    }

    public async Task<WindowsRuntimeStagingPublication> PublishAsync(
        AdmittedWindowsRuntimeStagingPlan plan,
        string outputParent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputParent);
        cancellationToken.ThrowIfCancellationRequested();

        var files = ValidateAndOrderPlan(plan);
        var sourceRoot = Path.GetFullPath(plan.SourceRoot);
        if (!Path.IsPathFullyQualified(plan.SourceRoot) ||
            !string.Equals(
                sourceRoot,
                plan.SourceRoot,
                PathComparison) ||
            !Directory.Exists(sourceRoot))
        {
            throw new InvalidDataException(
                "The admitted Windows runtime source root is invalid.");
        }

        EnsureDirectoryAndAncestorsAreNotLinks(sourceRoot);
        if (!Path.IsPathFullyQualified(outputParent))
        {
            throw new ArgumentException(
                "The Windows runtime output parent must be absolute.",
                nameof(outputParent));
        }

        var outputRoot = Path.GetFullPath(outputParent);
        if (PathsOverlap(sourceRoot, outputRoot))
        {
            throw new InvalidDataException(
                "The Windows runtime source and output must not overlap.");
        }

        EnsureExistingAncestorsAreNotLinks(outputRoot);
        RefuseFileOrLink(outputRoot);
        Directory.CreateDirectory(outputRoot);
        EnsureDirectoryAndAncestorsAreNotLinks(outputRoot);

        var inventorySha256 = ComputeInventorySha256(plan, files);
        var publicationDirectory = Path.Combine(
            outputRoot,
            PublicationPrefix + inventorySha256);
        var manifest = RecreateManifest(plan, files);
        var props = ApprovedWindowsRuntimePropsGenerator.Generate(
            manifest,
            inventorySha256);
        if (EntryExistsOrIsLink(publicationDirectory))
        {
            await VerifyPublicationAsync(
                    publicationDirectory,
                    files,
                    props,
                    cancellationToken)
                .ConfigureAwait(false);
            return Publication(
                publicationDirectory,
                inventorySha256,
                reusedExistingPublication: true);
        }

        var stagingDirectory = Path.Combine(
            outputRoot,
            TemporaryPublicationPrefix + Guid.NewGuid().ToString("N"));
        RefuseFileOrLink(stagingDirectory);
        Directory.CreateDirectory(stagingDirectory);
        var payloadDirectory = Path.Combine(stagingDirectory, "payload");
        Directory.CreateDirectory(payloadDirectory);
        try
        {
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await CopyVerifiedFileAsync(
                        plan.SourceRoot,
                        payloadDirectory,
                        stagingDirectory,
                        file,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            Checkpoint(
                WindowsRuntimeStagingCheckpoint.BeforePayloadVerification,
                stagingDirectory,
                payloadDirectory,
                targetRelativePath: null,
                chunkNumber: 0);
            await VerifyPayloadAsync(
                    payloadDirectory,
                    files,
                    cancellationToken)
                .ConfigureAwait(false);

            Checkpoint(
                WindowsRuntimeStagingCheckpoint.BeforePropsWrite,
                stagingDirectory,
                payloadDirectory,
                targetRelativePath: null,
                chunkNumber: 0);
            await WriteVerifiedFileAsync(
                    Path.Combine(stagingDirectory, PropsFileName),
                    props,
                    cancellationToken)
                .ConfigureAwait(false);
            await VerifyPublicationAsync(
                    stagingDirectory,
                    files,
                    props,
                    cancellationToken)
                .ConfigureAwait(false);

            Checkpoint(
                WindowsRuntimeStagingCheckpoint.BeforeCommit,
                stagingDirectory,
                payloadDirectory,
                targetRelativePath: null,
                chunkNumber: 0);
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                _committer.Commit(stagingDirectory, publicationDirectory);
            }
            catch (IOException) when (EntryExistsOrIsLink(publicationDirectory))
            {
                await VerifyPublicationAsync(
                        publicationDirectory,
                        files,
                        props,
                        cancellationToken)
                    .ConfigureAwait(false);
                return Publication(
                    publicationDirectory,
                    inventorySha256,
                    reusedExistingPublication: true);
            }

            // Directory.Move is the commit point. Do not observe cancellation or
            // run fallible post-commit work after this point.
            return Publication(
                publicationDirectory,
                inventorySha256,
                reusedExistingPublication: false);
        }
        finally
        {
            if (Directory.Exists(stagingDirectory))
            {
                Directory.Delete(stagingDirectory, recursive: true);
            }
        }
    }

    private async Task CopyVerifiedFileAsync(
        string sourceRoot,
        string payloadDirectory,
        string stagingDirectory,
        AdmittedWindowsRuntimeStagingFile file,
        CancellationToken cancellationToken)
    {
        var sourcePath = WindowsRuntimeRelativePath.Resolve(
            sourceRoot,
            file.Source);
        var destinationPath = WindowsRuntimeRelativePath.Resolve(
            payloadDirectory,
            file.Target);
        EnsureRegularUnlinkedFile(sourcePath, sourceRoot);
        _fileSemantics.VerifyRegularFile(sourcePath);
        var sourceInfo = new FileInfo(sourcePath);
        if (sourceInfo.Length != file.Length)
        {
            throw FileMismatch(file.Target);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[BufferSize];
        long length = 0;
        var chunkNumber = 0;
        await using (var input = new FileStream(
                         sourcePath,
                         FileMode.Open,
                         FileAccess.Read,
                         FileShare.Read,
                         BufferSize,
                         FileOptions.Asynchronous |
                         FileOptions.SequentialScan))
        await using (var output = new FileStream(
                         destinationPath,
                         FileMode.CreateNew,
                         FileAccess.Write,
                         FileShare.None,
                         BufferSize,
                         FileOptions.Asynchronous |
                         FileOptions.SequentialScan |
                         FileOptions.WriteThrough))
        {
            while (true)
            {
                var read = await input
                    .ReadAsync(buffer, cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                await output
                    .WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                    .ConfigureAwait(false);
                hash.AppendData(buffer, 0, read);
                length += read;
                chunkNumber++;
                Checkpoint(
                    WindowsRuntimeStagingCheckpoint.AfterCopyChunk,
                    stagingDirectory,
                    payloadDirectory,
                    file.Target,
                    chunkNumber);
            }

            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            output.Flush(flushToDisk: true);
            EnsureRegularUnlinkedFile(sourcePath, sourceRoot);
        }

        EnsureRegularUnlinkedFile(sourcePath, sourceRoot);
        _fileSemantics.VerifyRegularFile(sourcePath);
        _fileSemantics.VerifyRegularFile(destinationPath);
        var actualHash = hash.GetHashAndReset();
        if (length != file.Length ||
            !CryptographicOperations.FixedTimeEquals(
                actualHash,
                Convert.FromHexString(file.Sha256)))
        {
            throw FileMismatch(file.Target);
        }

        Checkpoint(
            WindowsRuntimeStagingCheckpoint.AfterFileCopied,
            stagingDirectory,
            payloadDirectory,
            file.Target,
            chunkNumber);
    }

    private async Task VerifyPublicationAsync(
        string publicationDirectory,
        IReadOnlyList<AdmittedWindowsRuntimeStagingFile> files,
        byte[] expectedProps,
        CancellationToken cancellationToken)
    {
        var directory = new DirectoryInfo(publicationDirectory);
        if (!directory.Exists)
        {
            throw PublicationMismatch(publicationDirectory);
        }

        EnsureDirectoryAndAncestorsAreNotLinks(publicationDirectory);
        var entries = directory
            .EnumerateFileSystemInfos()
            .OrderBy(entry => entry.Name, StringComparer.Ordinal)
            .ToArray();
        if (entries.Length != 2 ||
            entries.Any(IsLink) ||
            entries.SingleOrDefault(entry => string.Equals(
                entry.Name,
                "payload",
                StringComparison.Ordinal)) is not DirectoryInfo ||
            entries.SingleOrDefault(entry => string.Equals(
                entry.Name,
                PropsFileName,
                StringComparison.Ordinal)) is not FileInfo propsFile)
        {
            throw PublicationMismatch(publicationDirectory);
        }

        var actualProps = await File.ReadAllBytesAsync(
                propsFile.FullName,
                cancellationToken)
            .ConfigureAwait(false);
        if (!actualProps.AsSpan().SequenceEqual(expectedProps))
        {
            throw PublicationMismatch(publicationDirectory);
        }

        _fileSemantics.VerifyRegularFile(propsFile.FullName);

        await VerifyPayloadAsync(
                Path.Combine(publicationDirectory, "payload"),
                files,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task VerifyPayloadAsync(
        string payloadDirectory,
        IReadOnlyList<AdmittedWindowsRuntimeStagingFile> files,
        CancellationToken cancellationToken)
    {
        var inventory = await _inventoryReader
            .ReadAsync(payloadDirectory, cancellationToken)
            .ConfigureAwait(false);
        var registrations = files.Select(file => new RegisteredStagedArtifact(
                file.ComponentId,
                file.Target,
                file.Sha256,
                file.Kind))
            .ToArray();
        var issues = inventory.ScanIssues
            .Concat(StagingInventoryValidator.Validate(
                inventory.Files,
                registrations))
            .ToList();
        foreach (var expected in files)
        {
            var actual = inventory.Files.SingleOrDefault(file => string.Equals(
                file.RelativePath,
                expected.Target,
                StringComparison.OrdinalIgnoreCase));
            if (actual is null ||
                !string.Equals(
                    actual.RelativePath,
                    expected.Target,
                    StringComparison.Ordinal) ||
                actual.Length != expected.Length ||
                actual.Kind != expected.Kind)
            {
                issues.Add(new ComplianceIssue(
                    "windows-runtime-staged-file-mismatch",
                    expected.Target));
            }
            else
            {
                _fileSemantics.VerifyRegularFile(
                    WindowsRuntimeRelativePath.Resolve(
                        payloadDirectory,
                        expected.Target));
            }
        }

        if (issues.Count != 0)
        {
            throw new InvalidDataException(
                "The staged Windows runtime payload failed verification: " +
                string.Join(
                    ", ",
                    issues.Select(issue => $"{issue.Code}:{issue.Subject}")));
        }
    }

    private async Task WriteVerifiedFileAsync(
        string path,
        byte[] content,
        CancellationToken cancellationToken)
    {
        await using (var output = new FileStream(
                         path,
                         FileMode.CreateNew,
                         FileAccess.Write,
                         FileShare.None,
                         BufferSize,
                         FileOptions.Asynchronous |
                         FileOptions.SequentialScan |
                         FileOptions.WriteThrough))
        {
            await output
                .WriteAsync(content, cancellationToken)
                .ConfigureAwait(false);
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            output.Flush(flushToDisk: true);
        }

        var actual = await File.ReadAllBytesAsync(path, cancellationToken)
            .ConfigureAwait(false);
        if (!actual.AsSpan().SequenceEqual(content))
        {
            throw new InvalidDataException(
                "The generated Windows runtime props changed while staging.");
        }

        _fileSemantics.VerifyRegularFile(path);
    }

    private void Checkpoint(
        WindowsRuntimeStagingCheckpoint checkpoint,
        string stagingDirectory,
        string payloadDirectory,
        string? targetRelativePath,
        int chunkNumber) => _faultInjector.OnCheckpoint(
        checkpoint,
        new WindowsRuntimeStagingFaultContext(
            stagingDirectory,
            payloadDirectory,
            targetRelativePath,
            chunkNumber));

    private static AdmittedWindowsRuntimeStagingFile[] ValidateAndOrderPlan(
        AdmittedWindowsRuntimeStagingPlan plan)
    {
        if (!IsCanonicalSha256(plan.ManifestSha256) ||
            plan.Files.Count is <= 0 or > MaximumEntryCount)
        {
            throw new InvalidDataException(
                "The admitted Windows runtime staging plan is invalid.");
        }

        var sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in plan.Files)
        {
            ArgumentNullException.ThrowIfNull(file);
            try
            {
                WindowsRuntimeRelativePath.RequireCanonical(
                    file.Source,
                    nameof(file.Source));
                WindowsRuntimeRelativePath.RequireCanonical(
                    file.Target,
                    nameof(file.Target));
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException(
                    "The admitted Windows runtime staging plan is invalid.",
                    exception);
            }

            if (!sources.Add(file.Source) ||
                !targets.Add(file.Target) ||
                string.IsNullOrWhiteSpace(file.ComponentId) ||
                !IsCanonicalSha256(file.Sha256) ||
                file.Length < 0 ||
                !Enum.IsDefined(file.Role) ||
                !Enum.IsDefined(file.DeploymentKind) ||
                !Enum.IsDefined(file.Kind) ||
                !KindsMatch(file.DeploymentKind, file.Kind))
            {
                throw new InvalidDataException(
                    "The admitted Windows runtime staging plan is invalid.");
            }
        }

        return plan.Files
            .OrderBy(file => file.Target, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool KindsMatch(
        WindowsRuntimeDeploymentKind deploymentKind,
        StagedArtifactKind kind) => deploymentKind switch
        {
            WindowsRuntimeDeploymentKind.NativeLibrary =>
                kind == StagedArtifactKind.NativeLibrary,
            WindowsRuntimeDeploymentKind.Executable =>
                kind == StagedArtifactKind.Executable,
            WindowsRuntimeDeploymentKind.Asset or
                WindowsRuntimeDeploymentKind.Evidence =>
                kind == StagedArtifactKind.Asset,
            _ => false,
        };

    private static WindowsRuntimeStagingManifest RecreateManifest(
        AdmittedWindowsRuntimeStagingPlan plan,
        IReadOnlyList<AdmittedWindowsRuntimeStagingFile> files) => new(
        SchemaVersion: 2,
        plan.ManifestSha256,
        plan.Profile,
        plan.RuntimeIdentifier,
        new WindowsRuntimeLegalBundleAnchor(
            plan.LegalBundleId,
            plan.LegalManifestSha256),
        files.Select(file => new WindowsRuntimeStagingEntry(
                file.Source,
                file.Target,
                file.Role,
                file.ComponentId,
                "windows-x64",
                file.DeploymentKind,
                file.Sha256,
                file.Length))
            .ToArray());

    private static string ComputeInventorySha256(
        AdmittedWindowsRuntimeStagingPlan plan,
        AdmittedWindowsRuntimeStagingFile[] files)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendHashField(hash, "VRRECORDER_WINDOWS_RUNTIME_INVENTORY_V1");
        AppendHashField(hash, plan.ManifestSha256);
        Span<byte> count = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(count, files.Length);
        hash.AppendData(count);
        Span<byte> length = stackalloc byte[sizeof(long)];
        foreach (var file in files)
        {
            AppendHashField(hash, file.Source);
            AppendHashField(hash, file.Target);
            AppendHashField(hash, file.Role.ToString());
            AppendHashField(hash, file.ComponentId);
            AppendHashField(hash, file.DeploymentKind.ToString());
            AppendHashField(hash, file.Sha256);
            BinaryPrimitives.WriteInt64BigEndian(length, file.Length);
            hash.AppendData(length);
            AppendHashField(hash, file.Kind.ToString());
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static void AppendHashField(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }

    private static WindowsRuntimeStagingPublication Publication(
        string publicationDirectory,
        string inventorySha256,
        bool reusedExistingPublication) => new(
        publicationDirectory,
        Path.Combine(publicationDirectory, "payload"),
        Path.Combine(publicationDirectory, PropsFileName),
        inventorySha256,
        reusedExistingPublication);

    private static bool PathsOverlap(string first, string second) =>
        string.Equals(first, second, PathComparison) ||
        IsWithinRoot(first, second) ||
        IsWithinRoot(second, first);

    private static bool IsWithinRoot(string path, string root)
    {
        var prefix = Path.TrimEndingDirectorySeparator(root) +
                     Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, PathComparison);
    }

    private static void EnsureRegularUnlinkedFile(string path, string root)
    {
        var file = new FileInfo(path);
        if (!file.Exists || IsLink(file))
        {
            throw new InvalidDataException(
                $"The admitted Windows runtime source is missing or linked: {path}");
        }

        var rootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        for (var directory = file.Directory;
             directory is not null;
             directory = directory.Parent)
        {
            if (!directory.Exists || IsLink(directory))
            {
                throw new InvalidDataException(
                    $"The admitted Windows runtime source has a linked ancestor: {path}");
            }

            if (string.Equals(directory.FullName, rootPath, PathComparison))
            {
                return;
            }
        }

        throw new InvalidDataException(
            $"The admitted Windows runtime source escaped its root: {path}");
    }

    private static void EnsureDirectoryAndAncestorsAreNotLinks(string path)
    {
        for (var directory = new DirectoryInfo(path);
             directory is not null;
             directory = directory.Parent)
        {
            if (!directory.Exists || IsLink(directory))
            {
                throw new InvalidDataException(
                    $"Windows runtime directories must exist and be unlinked: {directory.FullName}");
            }
        }
    }

    private static void EnsureExistingAncestorsAreNotLinks(string path)
    {
        for (var directory = new DirectoryInfo(path);
             directory is not null;
             directory = directory.Parent)
        {
            if (!directory.Exists)
            {
                if (directory.LinkTarget is not null)
                {
                    throw new InvalidDataException(
                        $"Windows runtime output cannot traverse a link: {directory.FullName}");
                }

                continue;
            }

            if (IsLink(directory))
            {
                throw new InvalidDataException(
                    $"Windows runtime output cannot traverse a link: {directory.FullName}");
            }
        }
    }

    private static bool EntryExistsOrIsLink(string path)
    {
        var file = new FileInfo(path);
        var directory = new DirectoryInfo(path);
        return file.Exists || directory.Exists ||
               file.LinkTarget is not null || directory.LinkTarget is not null;
    }

    private static void RefuseFileOrLink(string path)
    {
        var file = new FileInfo(path);
        var directory = new DirectoryInfo(path);
        if (file.Exists || file.LinkTarget is not null ||
            directory.LinkTarget is not null)
        {
            throw new InvalidDataException(
                $"Windows runtime output collides with a file or link: {path}");
        }
    }

    private static bool IsLink(FileSystemInfo entry)
    {
        entry.Refresh();
        return entry.LinkTarget is not null ||
               entry.Attributes.HasFlag(FileAttributes.ReparsePoint);
    }

    private static bool IsCanonicalSha256(string value) =>
        value is not null &&
        value.Length == 64 &&
        value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static InvalidDataException FileMismatch(string target) => new(
        $"The admitted Windows runtime source changed while copying: {target}");

    private static InvalidDataException PublicationMismatch(string path) => new(
        $"The existing Windows runtime publication does not match its digest: {path}");

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
}

internal sealed record WindowsRuntimeStagingPublication(
    string PublishedDirectory,
    string PayloadDirectory,
    string ApprovedPropsPath,
    string InventorySha256,
    bool ReusedExistingPublication);

internal enum WindowsRuntimeStagingCheckpoint
{
    AfterCopyChunk,
    AfterFileCopied,
    BeforePayloadVerification,
    BeforePropsWrite,
    BeforeCommit,
}

internal sealed record WindowsRuntimeStagingFaultContext(
    string StagingDirectory,
    string PayloadDirectory,
    string? TargetRelativePath,
    int ChunkNumber);

internal interface IWindowsRuntimeStagingFaultInjector
{
    void OnCheckpoint(
        WindowsRuntimeStagingCheckpoint checkpoint,
        WindowsRuntimeStagingFaultContext context);
}

internal static class WindowsRuntimeStagingFaultInjector
{
    public static IWindowsRuntimeStagingFaultInjector None { get; } =
        new NoFaults();

    private sealed class NoFaults : IWindowsRuntimeStagingFaultInjector
    {
        public void OnCheckpoint(
            WindowsRuntimeStagingCheckpoint checkpoint,
            WindowsRuntimeStagingFaultContext context)
        {
        }
    }
}

internal interface IWindowsRuntimeDirectoryCommitter
{
    void Commit(string stagingDirectory, string publishedDirectory);
}

internal sealed class DirectoryMoveWindowsRuntimeCommitter
    : IWindowsRuntimeDirectoryCommitter
{
    public static DirectoryMoveWindowsRuntimeCommitter Instance { get; } =
        new();

    private DirectoryMoveWindowsRuntimeCommitter()
    {
    }

    public void Commit(string stagingDirectory, string publishedDirectory) =>
        Directory.Move(stagingDirectory, publishedDirectory);
}
