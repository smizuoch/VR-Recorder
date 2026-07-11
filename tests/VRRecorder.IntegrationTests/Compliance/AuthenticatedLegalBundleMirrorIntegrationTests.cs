using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using VRRecorder.Application.Desktop;
using VRRecorder.Application.Ports;
using VRRecorder.Application.Settings;
using VRRecorder.Compliance.Runtime;
using VRRecorder.Domain.Storage;
using VRRecorder.Infrastructure.Storage;

namespace VRRecorder.IntegrationTests.Compliance;

public sealed class AuthenticatedLegalBundleMirrorIntegrationTests
{
    [Fact]
    [Trait("Scenario", "IT-030")]
    public async Task SettingsOutputChangePublishesVerifiedBundleBeforeNewPathPersists()
    {
        using var directory = TemporaryDirectory.Create();
        var installRoot = Path.Combine(directory.Path, "install");
        var oldOutput = Path.Combine(directory.Path, "old-recordings");
        var newOutput = Path.Combine(directory.Path, "new-recordings");
        var anchor = await CreateAuthenticatedBundleAsync(
            installRoot,
            "https://example.invalid/spdx/vr-recorder-settings",
            "settings-output-change");
        var store = new JsonFileSettingsStore(
            Path.Combine(directory.Path, "settings.json"));
        var defaults = VRRecorderSettings.CreateDefault();
        await store.SaveAsync(
            defaults with
            {
                Recording = defaults.Recording with
                {
                    OutputFolder = oldOutput,
                },
            },
            CancellationToken.None);
        var controller = new DesktopRecordingSettingsController(
            store,
            new RecordingOutputPathResolver(
                new UnexpectedDefaultOutputPathProvider()),
            new AuthenticatedLegalBundleOutputMirror(
                installRoot,
                "3.1.0",
                new AuthenticatedLegalBundleVerifier(
                    new FixedAuthenticatedAnchorSource(anchor))));
        var draft = await controller.LoadAsync(CancellationToken.None);

        await controller.SaveAsync(
            draft with { OutputFolder = newOutput },
            CancellationToken.None);

        var persisted = await store.LoadAsync(CancellationToken.None);
        Assert.Equal(newOutput, persisted.Recording.OutputFolder);
        Assert.False(Directory.Exists(Path.Combine(
            oldOutput,
            "VR-Recorder-Legal")));
        var mirroredVersion = Path.Combine(
            newOutput,
            "VR-Recorder-Legal",
            "3.1.0");
        var verification = await new AuthenticatedLegalBundleVerifier(
                new FixedAuthenticatedAnchorSource(anchor))
            .VerifyAsync(mirroredVersion, CancellationToken.None);
        Assert.IsType<LegalBundleVerification.Verified>(verification);
        Assert.Equal(
            "3.1.0/\n"u8.ToArray(),
            await File.ReadAllBytesAsync(Path.Combine(
                newOutput,
                "VR-Recorder-Legal",
                "CURRENT.txt")));
        Assert.True(File.Exists(Path.Combine(
            newOutput,
            "VR-Recorder-Legal",
            "OPEN-NOTICES.html")));
    }

    [Fact]
    [Trait("Scenario", "IT-030")]
    public async Task InstallRootMirrorCopiesOnlyAuthenticatedLegalFiles()
    {
        using var directory = TemporaryDirectory.Create();
        var installRoot = Path.Combine(directory.Path, "install");
        var output = Path.Combine(directory.Path, "recordings");
        var anchor = await CreateAuthenticatedBundleAsync(
            installRoot,
            "https://example.invalid/spdx/vr-recorder-install",
            "install-root",
            new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                ["CUSTOM-LEGAL/authenticated-proof.dat"] =
                    "custom authenticated legal evidence\n"u8.ToArray(),
            });
        var authenticatedFiles = ReadTree(installRoot);
        await WriteFileAsync(
            installRoot,
            "VRRecorder.App.exe",
            "application executable");
        await WriteFileAsync(
            installRoot,
            "native/vrrecorder_native.dll",
            "native application payload");
        await WriteFileAsync(
            installRoot,
            "resources/application.json",
            "{\"not\":\"legal\"}\n");
        var mirror = new AuthenticatedLegalBundleOutputMirror(
            installRoot,
            "2.5.0",
            new AuthenticatedLegalBundleVerifier(
                new FixedAuthenticatedAnchorSource(anchor)));

        await mirror.MirrorAsync(
            new OutputPath(output),
            CancellationToken.None);

        var mirroredVersion = Path.Combine(
            output,
            "VR-Recorder-Legal",
            "2.5.0");
        AssertTreeEqual(authenticatedFiles, ReadTree(mirroredVersion));
        Assert.False(File.Exists(Path.Combine(
            mirroredVersion,
            "VRRecorder.App.exe")));
        Assert.False(File.Exists(Path.Combine(
            mirroredVersion,
            "native",
            "vrrecorder_native.dll")));
        Assert.True(File.Exists(Path.Combine(
            mirroredVersion,
            "CUSTOM-LEGAL",
            "authenticated-proof.dat")));
        var verification = await new AuthenticatedLegalBundleVerifier(
                new FixedAuthenticatedAnchorSource(anchor))
            .VerifyAsync(mirroredVersion, CancellationToken.None);
        Assert.IsType<LegalBundleVerification.Verified>(verification);
        Assert.Equal(
            "2.5.0/\n"u8.ToArray(),
            await File.ReadAllBytesAsync(Path.Combine(
                output,
                "VR-Recorder-Legal",
                "CURRENT.txt")));
        Assert.True(File.Exists(Path.Combine(
            output,
            "VR-Recorder-Legal",
            "OPEN-NOTICES.html")));
    }

    [Fact]
    public async Task InstallRootMirrorRejectsUnmanifestedLegalPayload()
    {
        using var directory = TemporaryDirectory.Create();
        var installRoot = Path.Combine(directory.Path, "install");
        var output = Path.Combine(directory.Path, "recordings");
        var anchor = await CreateAuthenticatedBundleAsync(
            installRoot,
            "https://example.invalid/spdx/vr-recorder-install",
            "unregistered-legal");
        await WriteFileAsync(
            installRoot,
            "LICENSES/rogue/LICENSE.txt",
            "unregistered legal payload");
        var mirror = new AuthenticatedLegalBundleOutputMirror(
            installRoot,
            "2.5.0",
            new AuthenticatedLegalBundleVerifier(
                new FixedAuthenticatedAnchorSource(anchor)));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            mirror.MirrorAsync(
                new OutputPath(output),
                CancellationToken.None));

        Assert.False(Directory.Exists(Path.Combine(
            output,
            "VR-Recorder-Legal")));
    }

    [Fact]
    public async Task InstallRootMirrorDoesNotFollowApplicationSymlinkDirectory()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var directory = TemporaryDirectory.Create();
        var installRoot = Path.Combine(directory.Path, "install");
        var output = Path.Combine(directory.Path, "recordings");
        var outsidePlugins = Path.Combine(directory.Path, "outside-plugins");
        var anchor = await CreateAuthenticatedBundleAsync(
            installRoot,
            "https://example.invalid/spdx/vr-recorder-install",
            "ignored-application-link");
        var authenticatedFiles = ReadTree(installRoot);
        await WriteFileAsync(
            outsidePlugins,
            "nested/third-party-plugin.dll",
            "application plugin outside the install root");
        Directory.CreateSymbolicLink(
            Path.Combine(installRoot, "plugins"),
            outsidePlugins);
        var mirror = new AuthenticatedLegalBundleOutputMirror(
            installRoot,
            "2.5.0",
            new AuthenticatedLegalBundleVerifier(
                new FixedAuthenticatedAnchorSource(anchor)));

        await mirror.MirrorAsync(
            new OutputPath(output),
            CancellationToken.None);

        AssertTreeEqual(
            authenticatedFiles,
            ReadTree(Path.Combine(
                output,
                "VR-Recorder-Legal",
                "2.5.0")));
        Assert.False(Directory.Exists(Path.Combine(
            output,
            "VR-Recorder-Legal",
            "2.5.0",
            "plugins")));
    }

    [Fact]
    public async Task DefaultStrictMirrorStillRejectsInstallRootPayload()
    {
        using var directory = TemporaryDirectory.Create();
        var installRoot = Path.Combine(directory.Path, "install");
        var output = Path.Combine(directory.Path, "recordings");
        var anchor = await CreateAuthenticatedBundleAsync(
            installRoot,
            "https://example.invalid/spdx/vr-recorder-install",
            "strict-source");
        await WriteFileAsync(
            installRoot,
            "VRRecorder.App.exe",
            "application executable");

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            CreateMirror(anchor).MirrorAsync(
                installRoot,
                output,
                "2.5.0",
                CancellationToken.None));

        Assert.False(Directory.Exists(Path.Combine(
            output,
            "VR-Recorder-Legal")));
    }

    [Fact]
    [Trait("Scenario", "IT-030")]
    public async Task NewCurrentMirrorPreservesOlderBundleAndIsDeterministicOffline()
    {
        using var directory = TemporaryDirectory.Create();
        var firstSource = Path.Combine(directory.Path, "source-1");
        var secondSource = Path.Combine(directory.Path, "source-2");
        var firstOutput = Path.Combine(directory.Path, "output-1");
        var secondOutput = Path.Combine(directory.Path, "output-2");
        var firstAnchor = await CreateAuthenticatedBundleAsync(
            firstSource,
            "https://example.invalid/spdx/vr-recorder-1",
            "first");
        var secondAnchor = await CreateAuthenticatedBundleAsync(
            secondSource,
            "https://example.invalid/spdx/vr-recorder-2",
            "second");

        await CreateMirror(firstAnchor).MirrorAsync(
            firstSource,
            firstOutput,
            "1.0.0",
            CancellationToken.None);
        var firstVersionPath = Path.Combine(
            firstOutput,
            "VR-Recorder-Legal",
            "1.0.0");
        AssertTreesEqual(firstSource, firstVersionPath);
        var preservedFirstVersion = ReadTree(firstVersionPath);

        await CreateMirror(secondAnchor).MirrorAsync(
            secondSource,
            firstOutput,
            "2.0.0",
            CancellationToken.None);
        await CreateMirror(secondAnchor).MirrorAsync(
            secondSource,
            secondOutput,
            "2.0.0",
            CancellationToken.None);

        AssertTreeEqual(preservedFirstVersion, ReadTree(firstVersionPath));
        var firstLegalRoot = Path.Combine(firstOutput, "VR-Recorder-Legal");
        var secondLegalRoot = Path.Combine(secondOutput, "VR-Recorder-Legal");
        AssertTreesEqual(
            secondSource,
            Path.Combine(firstLegalRoot, "2.0.0"));
        AssertTreesEqual(
            Path.Combine(firstLegalRoot, "2.0.0"),
            Path.Combine(secondLegalRoot, "2.0.0"));
        var currentBytes = "2.0.0/\n"u8.ToArray();
        Assert.Equal(
            currentBytes,
            await File.ReadAllBytesAsync(Path.Combine(
                firstLegalRoot,
                "CURRENT.txt")));
        Assert.Equal(
            currentBytes,
            await File.ReadAllBytesAsync(Path.Combine(
                secondLegalRoot,
                "CURRENT.txt")));
        var firstIndex = await File.ReadAllBytesAsync(Path.Combine(
            firstLegalRoot,
            "OPEN-NOTICES.html"));
        var secondIndex = await File.ReadAllBytesAsync(Path.Combine(
            secondLegalRoot,
            "OPEN-NOTICES.html"));
        Assert.Equal(firstIndex, secondIndex);
        AssertOfflineRelativeIndex(firstIndex, "2.0.0");
        Assert.Empty(Directory.EnumerateFileSystemEntries(
            firstLegalRoot,
            ".*.staging-*",
            SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task UnverifiedReplacementLeavesPriorCurrentFilesUnchanged()
    {
        using var directory = TemporaryDirectory.Create();
        var firstSource = Path.Combine(directory.Path, "source-1");
        var secondSource = Path.Combine(directory.Path, "source-2");
        var output = Path.Combine(directory.Path, "output");
        var firstAnchor = await CreateAuthenticatedBundleAsync(
            firstSource,
            "https://example.invalid/spdx/vr-recorder-1",
            "first");
        var secondAnchor = await CreateAuthenticatedBundleAsync(
            secondSource,
            "https://example.invalid/spdx/vr-recorder-2",
            "second");
        await CreateMirror(firstAnchor).MirrorAsync(
            firstSource,
            output,
            "1.0.0",
            CancellationToken.None);
        var legalRoot = Path.Combine(output, "VR-Recorder-Legal");
        var currentBefore = await File.ReadAllBytesAsync(Path.Combine(
            legalRoot,
            "CURRENT.txt"));
        var indexBefore = await File.ReadAllBytesAsync(Path.Combine(
            legalRoot,
            "OPEN-NOTICES.html"));
        await File.AppendAllTextAsync(
            Path.Combine(secondSource, "THIRD-PARTY-NOTICES.txt"),
            "tampered");

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            CreateMirror(secondAnchor).MirrorAsync(
                secondSource,
                output,
                "2.0.0",
                CancellationToken.None));

        Assert.Equal(
            currentBefore,
            await File.ReadAllBytesAsync(Path.Combine(
                legalRoot,
                "CURRENT.txt")));
        Assert.Equal(
            indexBefore,
            await File.ReadAllBytesAsync(Path.Combine(
                legalRoot,
                "OPEN-NOTICES.html")));
        Assert.False(Directory.Exists(Path.Combine(legalRoot, "2.0.0")));
        Assert.True(Directory.Exists(Path.Combine(legalRoot, "1.0.0")));
    }

    [Theory]
    [InlineData("../2.0.0")]
    [InlineData("2.0/0")]
    [InlineData("C:escape")]
    [InlineData(".")]
    public async Task UnsafeProductVersionIsRejectedWithoutOutput(
        string productVersion)
    {
        using var directory = TemporaryDirectory.Create();
        var source = Path.Combine(directory.Path, "source");
        var output = Path.Combine(directory.Path, "output");
        var anchor = await CreateAuthenticatedBundleAsync(
            source,
            "https://example.invalid/spdx/vr-recorder",
            "unsafe-version");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            CreateMirror(anchor).MirrorAsync(
                source,
                output,
                productVersion,
                CancellationToken.None));

        Assert.False(Directory.Exists(Path.Combine(
            output,
            "VR-Recorder-Legal")));
    }

    [Fact]
    public async Task LinkedSourceRootIsRejectedWithoutOutput()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var directory = TemporaryDirectory.Create();
        var source = Path.Combine(directory.Path, "source");
        var linkedSource = Path.Combine(directory.Path, "source-link");
        var output = Path.Combine(directory.Path, "output");
        var anchor = await CreateAuthenticatedBundleAsync(
            source,
            "https://example.invalid/spdx/vr-recorder",
            "linked-source");
        Directory.CreateSymbolicLink(linkedSource, source);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            CreateMirror(anchor).MirrorAsync(
                linkedSource,
                output,
                "1.0.0",
                CancellationToken.None));

        Assert.False(Directory.Exists(Path.Combine(
            output,
            "VR-Recorder-Legal")));
    }

    [Fact]
    public async Task LinkedControlFileLeavesPriorCurrentAndTargetUntouched()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var directory = TemporaryDirectory.Create();
        var firstSource = Path.Combine(directory.Path, "source-1");
        var secondSource = Path.Combine(directory.Path, "source-2");
        var output = Path.Combine(directory.Path, "output");
        var firstAnchor = await CreateAuthenticatedBundleAsync(
            firstSource,
            "https://example.invalid/spdx/vr-recorder-1",
            "first");
        var secondAnchor = await CreateAuthenticatedBundleAsync(
            secondSource,
            "https://example.invalid/spdx/vr-recorder-2",
            "second");
        await CreateMirror(firstAnchor).MirrorAsync(
            firstSource,
            output,
            "1.0.0",
            CancellationToken.None);
        var legalRoot = Path.Combine(output, "VR-Recorder-Legal");
        var currentPath = Path.Combine(legalRoot, "CURRENT.txt");
        var currentBefore = await File.ReadAllBytesAsync(currentPath);
        var outsideTarget = Path.Combine(directory.Path, "outside.html");
        byte[] outsideBytes = "outside\n"u8.ToArray();
        await File.WriteAllBytesAsync(outsideTarget, outsideBytes);
        var indexPath = Path.Combine(legalRoot, "OPEN-NOTICES.html");
        File.Delete(indexPath);
        File.CreateSymbolicLink(indexPath, outsideTarget);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            CreateMirror(secondAnchor).MirrorAsync(
                secondSource,
                output,
                "2.0.0",
                CancellationToken.None));

        Assert.Equal(currentBefore, await File.ReadAllBytesAsync(currentPath));
        Assert.Equal(outsideBytes, await File.ReadAllBytesAsync(outsideTarget));
        Assert.False(Directory.Exists(Path.Combine(legalRoot, "2.0.0")));
    }

    private static AuthenticatedLegalBundleMirror CreateMirror(
        AuthenticatedLegalBundleAnchor anchor) =>
        new(new AuthenticatedLegalBundleVerifier(
            new FixedAuthenticatedAnchorSource(anchor)));

    private static async Task<AuthenticatedLegalBundleAnchor>
        CreateAuthenticatedBundleAsync(
            string directory,
            string bundleId,
            string marker,
            IReadOnlyDictionary<string, byte[]>? additionalFiles = null)
    {
        var catalog = Encoding.UTF8.GetBytes($$"""
            {
              "schemaVersion": 3,
              "bundleId": "{{bundleId}}",
              "productVersion": "0.1.0",
              "generatedAtUtc": "2026-07-10T00:00:00Z",
              "integrityManifest": {
                "path": "LEGAL-MANIFEST.sha256",
                "algorithm": "SHA-256"
              },
              "components": []
            }
            """);
        var files = new SortedDictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["LICENSES/example/LICENSE.txt"] = Encoding.UTF8.GetBytes(
                $"license {marker}\n"),
            ["SBOM/manifest.spdx.json"] = Encoding.UTF8.GetBytes(
                $"{{\"marker\":\"{marker}\"}}\n"),
            ["SOURCE-OFFERS/FFmpeg-SOURCE-INFO.txt"] = Encoding.UTF8.GetBytes(
                $"source {marker}\n"),
            ["THIRD-PARTY-COMPONENTS.json"] = catalog,
            ["THIRD-PARTY-NOTICES.html"] = Encoding.UTF8.GetBytes(
                $"<!doctype html><html><body>{marker}</body></html>\n"),
            ["THIRD-PARTY-NOTICES.txt"] = Encoding.UTF8.GetBytes(
                $"notice {marker}\n"),
        };
        if (additionalFiles is not null)
        {
            foreach (var (relativePath, content) in additionalFiles)
            {
                files.Add(relativePath, content);
            }
        }
        Directory.CreateDirectory(directory);
        foreach (var (relativePath, content) in files)
        {
            var path = Path.Combine(
                directory,
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, content);
        }

        var manifest = Encoding.UTF8.GetBytes(string.Concat(files.Select(file =>
            $"{Hash(file.Value)}  {file.Key}\n")));
        await File.WriteAllBytesAsync(
            Path.Combine(directory, "LEGAL-MANIFEST.sha256"),
            manifest);
        return new AuthenticatedLegalBundleAnchor(bundleId, Hash(manifest));
    }

    private static async Task WriteFileAsync(
        string root,
        string relativePath,
        string content)
    {
        var path = Path.Combine(
            root,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content);
    }

    private static void AssertOfflineRelativeIndex(
        byte[] indexBytes,
        string productVersion)
    {
        var html = new UTF8Encoding(false, true).GetString(indexBytes);
        Assert.False(html.Contains("http://", StringComparison.OrdinalIgnoreCase));
        Assert.False(html.Contains("https://", StringComparison.OrdinalIgnoreCase));
        Assert.False(html.Contains("<script", StringComparison.OrdinalIgnoreCase));
        Assert.False(html.Contains("http-equiv=\"refresh\"", StringComparison.OrdinalIgnoreCase));
        Assert.False(html.Contains(" src=", StringComparison.OrdinalIgnoreCase));
        var document = XDocument.Parse(html);
        var links = document
            .Descendants("a")
            .Select(element => element.Attribute("href")?.Value)
            .Where(value => value is not null)
            .ToArray();
        Assert.Contains(
            $"{productVersion}/THIRD-PARTY-NOTICES.html",
            links);
        Assert.Contains(
            $"{productVersion}/THIRD-PARTY-NOTICES.txt",
            links);
        Assert.Contains(
            $"{productVersion}/THIRD-PARTY-COMPONENTS.json",
            links);
        Assert.Contains(
            $"{productVersion}/SBOM/manifest.spdx.json",
            links);
        Assert.Contains(
            $"{productVersion}/SOURCE-OFFERS/FFmpeg-SOURCE-INFO.txt",
            links);
        Assert.All(links, link =>
        {
            Assert.False(Uri.TryCreate(link, UriKind.Absolute, out _));
            Assert.DoesNotContain("../", link, StringComparison.Ordinal);
            Assert.StartsWith($"{productVersion}/", link, StringComparison.Ordinal);
        });
    }

    private static void AssertTreesEqual(string expected, string actual) =>
        AssertTreeEqual(ReadTree(expected), ReadTree(actual));

    private static void AssertTreeEqual(
        SortedDictionary<string, byte[]> expected,
        SortedDictionary<string, byte[]> actual)
    {
        Assert.Equal(expected.Keys, actual.Keys);
        foreach (var (path, content) in expected)
        {
            Assert.Equal(content, actual[path]);
        }
    }

    private static SortedDictionary<string, byte[]> ReadTree(string root)
    {
        var files = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(
                     root,
                     "*",
                     SearchOption.AllDirectories))
        {
            var relativePath = Path
                .GetRelativePath(root, path)
                .Replace(Path.DirectorySeparatorChar, '/');
            files.Add(relativePath, File.ReadAllBytes(path));
        }

        return files;
    }

    private static string Hash(ReadOnlySpan<byte> content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    private sealed class FixedAuthenticatedAnchorSource(
        AuthenticatedLegalBundleAnchor anchor)
        : IAuthenticatedLegalBundleAnchorSource
    {
        public ValueTask<AuthenticatedLegalBundleAnchor> GetAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(anchor);
        }
    }

    private sealed class UnexpectedDefaultOutputPathProvider
        : IDefaultOutputPathProvider
    {
        public OutputPath GetDefault() => throw new InvalidOperationException(
            "Absolute settings paths must not resolve the Downloads token.");
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TemporaryDirectory Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"vr-recorder-legal-mirror-tests-{Guid.NewGuid():N}");
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
