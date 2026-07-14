using System.Security.Cryptography;
using System.Text;
using VRRecorder.Compliance.Staging;

namespace VRRecorder.Compliance.Tests.Staging;

public sealed class ImmutableWindowsRuntimeStagingPublisherTests
{
    [Fact]
    public async Task PublishesPayloadAndPropsInOneDigestNamedDirectory()
    {
        using var directory = TemporaryDirectory.Create();
        var source = Path.Combine(directory.Path, "source");
        var output = Path.Combine(directory.Path, "output");
        var plan = await CreatePlanAsync(
            source,
            ("native/source.bin", "runtime/source.bin", "source-content"),
            ("assets/settings.json", "settings.json", "{}"));

        var result = await new ImmutableWindowsRuntimeStagingPublisher()
            .PublishAsync(plan, output, CancellationToken.None);

        Assert.False(result.ReusedExistingPublication);
        Assert.Matches(
            "^windows-runtime-[0-9a-f]{64}$",
            Path.GetFileName(result.PublishedDirectory));
        Assert.Equal(
            "source-content",
            await File.ReadAllTextAsync(Path.Combine(
                result.PayloadDirectory,
                "runtime",
                "source.bin")));
        Assert.Equal(
            "{}",
            await File.ReadAllTextAsync(Path.Combine(
                result.PayloadDirectory,
                "settings.json")));
        Assert.True(File.Exists(result.ApprovedPropsPath));
        Assert.Equal(
            result.PublishedDirectory,
            Path.GetDirectoryName(result.ApprovedPropsPath));
        Assert.DoesNotContain(
            Directory.EnumerateFileSystemEntries(output),
            path => Path.GetFileName(path).Contains(
                ".staging-",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task RepeatedPublicationIsIdempotentAndDoesNotRewriteIt()
    {
        using var directory = TemporaryDirectory.Create();
        var source = Path.Combine(directory.Path, "source");
        var output = Path.Combine(directory.Path, "output");
        var plan = await CreatePlanAsync(
            source,
            ("source.bin", "source.bin", "content"));
        var publisher = new ImmutableWindowsRuntimeStagingPublisher();
        var first = await publisher.PublishAsync(
            plan,
            output,
            CancellationToken.None);
        var propsTimestamp = File.GetLastWriteTimeUtc(first.ApprovedPropsPath);

        var second = await publisher.PublishAsync(
            plan,
            output,
            CancellationToken.None);

        Assert.True(second.ReusedExistingPublication);
        Assert.Equal(first.PublishedDirectory, second.PublishedDirectory);
        Assert.Equal(
            propsTimestamp,
            File.GetLastWriteTimeUtc(second.ApprovedPropsPath));
        Assert.Single(Directory.EnumerateDirectories(output));
    }

    [Fact]
    public async Task AChangedPlanPublishesBesideTheOldImmutableDirectory()
    {
        using var directory = TemporaryDirectory.Create();
        var source = Path.Combine(directory.Path, "source");
        var output = Path.Combine(directory.Path, "output");
        var publisher = new ImmutableWindowsRuntimeStagingPublisher();
        var firstPlan = await CreatePlanAsync(
            source,
            ("source.bin", "source.bin", "first"));
        var first = await publisher.PublishAsync(
            firstPlan,
            output,
            CancellationToken.None);
        var firstPayload = Path.Combine(first.PayloadDirectory, "source.bin");

        var secondPlan = await CreatePlanAsync(
            source,
            ("source.bin", "source.bin", "second"));
        var second = await publisher.PublishAsync(
            secondPlan,
            output,
            CancellationToken.None);

        Assert.NotEqual(first.PublishedDirectory, second.PublishedDirectory);
        Assert.Equal("first", await File.ReadAllTextAsync(firstPayload));
        Assert.Equal(
            "second",
            await File.ReadAllTextAsync(Path.Combine(
                second.PayloadDirectory,
                "source.bin")));
        Assert.Equal(2, Directory.EnumerateDirectories(output).Count());
    }

    [Fact]
    public async Task CopyFailureRemovesOnlyTheOwnedTemporaryDirectory()
    {
        using var directory = TemporaryDirectory.Create();
        var source = Path.Combine(directory.Path, "source");
        var output = Path.Combine(directory.Path, "output");
        var baseline = await PublishBaselineAsync(source, output);
        var plan = await CreatePlanAsync(
            source,
            ("source.bin", "source.bin", new string('x', 100_000)));
        var fault = new CallbackFaultInjector((checkpoint, _) =>
        {
            if (checkpoint == WindowsRuntimeStagingCheckpoint.AfterCopyChunk)
            {
                throw new IOException("injected copy failure");
            }
        });
        var publisher = new ImmutableWindowsRuntimeStagingPublisher(fault);

        await Assert.ThrowsAsync<IOException>(() => publisher.PublishAsync(
            plan,
            output,
            CancellationToken.None));

        await AssertBaselineIsOnlyPublicationAsync(output, baseline);
    }

    [Fact]
    public async Task PostCopyTamperingIsRejectedBeforeCommit()
    {
        using var directory = TemporaryDirectory.Create();
        var source = Path.Combine(directory.Path, "source");
        var output = Path.Combine(directory.Path, "output");
        var baseline = await PublishBaselineAsync(source, output);
        var plan = await CreatePlanAsync(
            source,
            ("source.bin", "source.bin", "next"));
        var fault = new CallbackFaultInjector((checkpoint, context) =>
        {
            if (checkpoint ==
                WindowsRuntimeStagingCheckpoint.BeforePayloadVerification)
            {
                File.WriteAllText(
                    Path.Combine(context.PayloadDirectory, "source.bin"),
                    "tampered");
            }
        });
        var publisher = new ImmutableWindowsRuntimeStagingPublisher(fault);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            publisher.PublishAsync(
                plan,
                output,
                CancellationToken.None));

        await AssertBaselineIsOnlyPublicationAsync(output, baseline);
    }

    [Fact]
    public async Task UnexpectedPostCopyFileIsRejectedBeforeCommit()
    {
        using var directory = TemporaryDirectory.Create();
        var source = Path.Combine(directory.Path, "source");
        var output = Path.Combine(directory.Path, "output");
        var plan = await CreatePlanAsync(
            source,
            ("source.bin", "source.bin", "content"));
        var fault = new CallbackFaultInjector((checkpoint, context) =>
        {
            if (checkpoint ==
                WindowsRuntimeStagingCheckpoint.BeforePayloadVerification)
            {
                File.WriteAllText(
                    Path.Combine(context.PayloadDirectory, "extra.bin"),
                    "unexpected");
            }
        });
        var publisher = new ImmutableWindowsRuntimeStagingPublisher(fault);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            publisher.PublishAsync(
                plan,
                output,
                CancellationToken.None));

        Assert.Empty(Directory.EnumerateFileSystemEntries(output));
    }

    [Fact]
    public async Task CancellationBeforeCommitRemovesTemporaryDirectory()
    {
        using var directory = TemporaryDirectory.Create();
        var source = Path.Combine(directory.Path, "source");
        var output = Path.Combine(directory.Path, "output");
        var plan = await CreatePlanAsync(
            source,
            ("source.bin", "source.bin", "content"));
        using var cancellation = new CancellationTokenSource();
        var fault = new CallbackFaultInjector((checkpoint, _) =>
        {
            if (checkpoint == WindowsRuntimeStagingCheckpoint.BeforeCommit)
            {
                cancellation.Cancel();
            }
        });
        var publisher = new ImmutableWindowsRuntimeStagingPublisher(fault);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            publisher.PublishAsync(plan, output, cancellation.Token));

        Assert.Empty(Directory.EnumerateFileSystemEntries(output));
    }

    [Fact]
    public async Task CancellationDuringCopyRemovesTemporaryDirectory()
    {
        using var directory = TemporaryDirectory.Create();
        var source = Path.Combine(directory.Path, "source");
        var output = Path.Combine(directory.Path, "output");
        var plan = await CreatePlanAsync(
            source,
            ("source.bin", "source.bin", new string('x', 100_000)));
        using var cancellation = new CancellationTokenSource();
        var fault = new CallbackFaultInjector((checkpoint, _) =>
        {
            if (checkpoint == WindowsRuntimeStagingCheckpoint.AfterCopyChunk)
            {
                cancellation.Cancel();
            }
        });
        var publisher = new ImmutableWindowsRuntimeStagingPublisher(fault);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            publisher.PublishAsync(plan, output, cancellation.Token));

        Assert.Empty(Directory.EnumerateFileSystemEntries(output));
    }

    [Fact]
    public async Task CommitFailureLeavesExistingPublicationUntouched()
    {
        using var directory = TemporaryDirectory.Create();
        var source = Path.Combine(directory.Path, "source");
        var output = Path.Combine(directory.Path, "output");
        var baseline = await PublishBaselineAsync(source, output);
        var plan = await CreatePlanAsync(
            source,
            ("source.bin", "source.bin", "next"));
        var publisher = new ImmutableWindowsRuntimeStagingPublisher(
            WindowsRuntimeStagingFaultInjector.None,
            new CallbackCommitter((_, _) =>
                throw new IOException("injected commit failure")));

        await Assert.ThrowsAsync<IOException>(() => publisher.PublishAsync(
            plan,
            output,
            CancellationToken.None));

        await AssertBaselineIsOnlyPublicationAsync(output, baseline);
    }

    [Fact]
    public async Task CancellationWonAfterCommitDoesNotUndoPublishedSuccess()
    {
        using var directory = TemporaryDirectory.Create();
        var source = Path.Combine(directory.Path, "source");
        var output = Path.Combine(directory.Path, "output");
        var plan = await CreatePlanAsync(
            source,
            ("source.bin", "source.bin", "content"));
        using var cancellation = new CancellationTokenSource();
        var committer = new CallbackCommitter((staging, published) =>
        {
            Directory.Move(staging, published);
            cancellation.Cancel();
        });
        var publisher = new ImmutableWindowsRuntimeStagingPublisher(
            WindowsRuntimeStagingFaultInjector.None,
            committer);

        var result = await publisher.PublishAsync(
            plan,
            output,
            cancellation.Token);

        Assert.True(cancellation.IsCancellationRequested);
        Assert.True(Directory.Exists(result.PublishedDirectory));
        Assert.False(result.ReusedExistingPublication);
    }

    [Fact]
    public async Task ExistingDigestDirectoryWithDifferentBytesIsRejected()
    {
        using var directory = TemporaryDirectory.Create();
        var source = Path.Combine(directory.Path, "source");
        var output = Path.Combine(directory.Path, "output");
        var plan = await CreatePlanAsync(
            source,
            ("source.bin", "source.bin", "content"));
        var publisher = new ImmutableWindowsRuntimeStagingPublisher();
        var first = await publisher.PublishAsync(
            plan,
            output,
            CancellationToken.None);
        Directory.Delete(first.PublishedDirectory, recursive: true);
        Directory.CreateDirectory(first.PublishedDirectory);
        var impostor = Path.Combine(first.PublishedDirectory, "impostor.txt");
        await File.WriteAllTextAsync(impostor, "do-not-replace");

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            publisher.PublishAsync(
                plan,
                output,
                CancellationToken.None));

        Assert.Equal("do-not-replace", await File.ReadAllTextAsync(impostor));
        Assert.Single(Directory.EnumerateFileSystemEntries(output));
    }

    [Fact]
    public async Task ExistingDigestPayloadTamperingIsRejectedWithoutRepair()
    {
        using var directory = TemporaryDirectory.Create();
        var source = Path.Combine(directory.Path, "source");
        var output = Path.Combine(directory.Path, "output");
        var plan = await CreatePlanAsync(
            source,
            ("source.bin", "source.bin", "content"));
        var publisher = new ImmutableWindowsRuntimeStagingPublisher();
        var first = await publisher.PublishAsync(
            plan,
            output,
            CancellationToken.None);
        var payload = Path.Combine(first.PayloadDirectory, "source.bin");
        await File.WriteAllTextAsync(payload, "tampered");

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            publisher.PublishAsync(
                plan,
                output,
                CancellationToken.None));

        Assert.Equal("tampered", await File.ReadAllTextAsync(payload));
        Assert.Single(Directory.EnumerateDirectories(output));
    }

    [Fact]
    public async Task LinkedOutputParentIsRejectedWithoutWritingThroughIt()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var directory = TemporaryDirectory.Create();
        var source = Path.Combine(directory.Path, "source");
        var actualOutput = Path.Combine(directory.Path, "actual-output");
        var linkedOutput = Path.Combine(directory.Path, "linked-output");
        Directory.CreateDirectory(actualOutput);
        Directory.CreateSymbolicLink(linkedOutput, actualOutput);
        var plan = await CreatePlanAsync(
            source,
            ("source.bin", "source.bin", "content"));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new ImmutableWindowsRuntimeStagingPublisher().PublishAsync(
                plan,
                linkedOutput,
                CancellationToken.None));

        Assert.Empty(Directory.EnumerateFileSystemEntries(actualOutput));
    }

    [Fact]
    public async Task SourceHashMismatchNeverPublishes()
    {
        using var directory = TemporaryDirectory.Create();
        var source = Path.Combine(directory.Path, "source");
        var output = Path.Combine(directory.Path, "output");
        var plan = await CreatePlanAsync(
            source,
            ("source.bin", "source.bin", "content"));
        var exactFile = plan.Files[0];
        var badFile = new AdmittedWindowsRuntimeStagingFile(
            exactFile.Source,
            exactFile.Target,
            exactFile.Role,
            exactFile.ComponentId,
            exactFile.DeploymentKind,
            new string('0', 64),
            exactFile.Length,
            exactFile.Kind);
        var badPlan = new AdmittedWindowsRuntimeStagingPlan(
            plan.ManifestSha256,
            plan.SourceRoot,
            [badFile]);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new ImmutableWindowsRuntimeStagingPublisher().PublishAsync(
                badPlan,
                output,
                CancellationToken.None));

        Assert.Empty(Directory.EnumerateFileSystemEntries(output));
    }

    [Fact]
    public async Task FileSemanticsFailureNeverPublishes()
    {
        using var directory = TemporaryDirectory.Create();
        var source = Path.Combine(directory.Path, "source");
        var output = Path.Combine(directory.Path, "output");
        var plan = await CreatePlanAsync(
            source,
            ("source.bin", "source.bin", "content"));
        var verifier = new CallbackFileSemanticsVerifier(path =>
        {
            if (path.StartsWith(source, StringComparison.Ordinal))
            {
                throw new InvalidDataException("synthetic named stream");
            }
        });
        var publisher = new ImmutableWindowsRuntimeStagingPublisher(
            WindowsRuntimeStagingFaultInjector.None,
            new CallbackCommitter(Directory.Move),
            verifier);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            publisher.PublishAsync(plan, output, CancellationToken.None));

        Assert.Empty(Directory.EnumerateFileSystemEntries(output));
    }

    [Fact]
    public async Task ConcurrentPublicationOfSameDigestHasOneVerifiedWinner()
    {
        using var directory = TemporaryDirectory.Create();
        var source = Path.Combine(directory.Path, "source");
        var output = Path.Combine(directory.Path, "output");
        var plan = await CreatePlanAsync(
            source,
            ("source.bin", "source.bin", new string('x', 100_000)));
        var firstPublisher = new ImmutableWindowsRuntimeStagingPublisher();
        var secondPublisher = new ImmutableWindowsRuntimeStagingPublisher();

        var results = await Task.WhenAll(
            firstPublisher.PublishAsync(plan, output, CancellationToken.None),
            secondPublisher.PublishAsync(plan, output, CancellationToken.None));

        Assert.Equal(
            results[0].PublishedDirectory,
            results[1].PublishedDirectory);
        Assert.Single(Directory.EnumerateDirectories(output));
        Assert.Single(results, result => !result.ReusedExistingPublication);
        Assert.Single(results, result => result.ReusedExistingPublication);
    }

    private static async Task<WindowsRuntimeStagingPublication>
        PublishBaselineAsync(string source, string output)
    {
        var baselinePlan = await CreatePlanAsync(
            source,
            ("source.bin", "source.bin", "baseline"));
        return await new ImmutableWindowsRuntimeStagingPublisher().PublishAsync(
            baselinePlan,
            output,
            CancellationToken.None);
    }

    private static async Task AssertBaselineIsOnlyPublicationAsync(
        string output,
        WindowsRuntimeStagingPublication baseline)
    {
        Assert.Single(Directory.EnumerateFileSystemEntries(output));
        Assert.True(Directory.Exists(baseline.PublishedDirectory));
        Assert.Equal(
            "baseline",
            await File.ReadAllTextAsync(Path.Combine(
                baseline.PayloadDirectory,
                "source.bin")));
    }

    private static async Task<AdmittedWindowsRuntimeStagingPlan>
        CreatePlanAsync(
            string sourceRoot,
            params (string Source, string Target, string Content)[] entries)
    {
        if (Directory.Exists(sourceRoot))
        {
            Directory.Delete(sourceRoot, recursive: true);
        }

        Directory.CreateDirectory(sourceRoot);
        var files = new List<AdmittedWindowsRuntimeStagingFile>();
        foreach (var entry in entries)
        {
            var path = WindowsRuntimeRelativePath.Resolve(
                sourceRoot,
                entry.Source);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var bytes = Encoding.UTF8.GetBytes(entry.Content);
            await File.WriteAllBytesAsync(path, bytes);
            files.Add(new AdmittedWindowsRuntimeStagingFile(
                entry.Source,
                entry.Target,
                WindowsRuntimeRole.ApplicationAsset,
                "vr-recorder",
                WindowsRuntimeDeploymentKind.Asset,
                Sha256(bytes),
                bytes.LongLength,
                StagedArtifactKind.Asset));
        }

        return new AdmittedWindowsRuntimeStagingPlan(
            new string('1', 64),
            Path.GetFullPath(sourceRoot),
            files);
    }

    private static string Sha256(byte[] content) => Convert
        .ToHexString(SHA256.HashData(content))
        .ToLowerInvariant();

    private sealed class CallbackFaultInjector(
        Action<WindowsRuntimeStagingCheckpoint,
            WindowsRuntimeStagingFaultContext> callback)
        : IWindowsRuntimeStagingFaultInjector
    {
        public void OnCheckpoint(
            WindowsRuntimeStagingCheckpoint checkpoint,
            WindowsRuntimeStagingFaultContext context) =>
            callback(checkpoint, context);
    }

    private sealed class CallbackCommitter(Action<string, string> callback)
        : IWindowsRuntimeDirectoryCommitter
    {
        public void Commit(
            string stagingDirectory,
            string publishedDirectory) =>
            callback(stagingDirectory, publishedDirectory);
    }

    private sealed class CallbackFileSemanticsVerifier(
        Action<string> callback)
        : IWindowsRuntimeFileSemanticsVerifier
    {
        public void VerifyRegularFile(string path) => callback(path);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path) => Path = path;

        public string Path { get; }

        public static TemporaryDirectory Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"vr-recorder-runtime-staging-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return new TemporaryDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
