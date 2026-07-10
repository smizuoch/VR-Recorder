using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using VRRecorder.Application.Compliance;
using VRRecorder.Application.Desktop;
using VRRecorder.Application.Ports;
using VRRecorder.Compliance.Dependencies;
using VRRecorder.Compliance.Generation;
using VRRecorder.Compliance.Runtime;
using VRRecorder.Domain.Recording;

namespace VRRecorder.IntegrationTests.Compliance;

public sealed class DesktopLegalViewerIntegrationTests
{
    [Fact]
    [Trait("Scenario", "IT-029")]
    public async Task GeneratedInstallBundleRemainsOfflineWhenRecordingRuntimeFails()
    {
        using var directory = TemporaryDirectory.Create();
        var installDirectory = Path.Combine(directory.Path, "install");
        var networkProbe = new TcpListener(IPAddress.Loopback, 0);
        networkProbe.Start();
        try
        {
            var endpoint = (IPEndPoint)networkProbe.LocalEndpoint;
            var sourceInformation =
                $"http://127.0.0.1:{endpoint.Port}/must-remain-offline";
            var fixture = await WriteGeneratedBundleAsync(
                installDirectory,
                sourceInformation);
            var verifier = new AuthenticatedLegalBundleVerifier(
                new FixedAuthenticatedAnchorSource(fixture.Anchor));
            var shell = new CapturingLegalFolderShell();
            var controller = new DesktopLegalController(
                new AuthenticatedLegalCatalogReader(
                    installDirectory,
                    verifier),
                new AuthenticatedLegalBundleFolderOpener(
                    installDirectory,
                    installDirectory,
                    verifier,
                    shell));
            await using var recordingHost = new DesktopRecordingCommandHost(
                new FailingRecordingRuntimeFactory());

            var activation = await recordingHost.ActivateAsync(
                new RecorderStartupResult(RecorderState.Ready, []),
                CancellationToken.None);
            await controller.OpenAsync(CancellationToken.None);
            await controller.ShowDetailAsync("a", CancellationToken.None);
            await controller.ShowLicenseAsync(CancellationToken.None);
            await controller.OpenLicenseFolderAsync(CancellationToken.None);

            Assert.Equal(
                DesktopRecordingHostState.InitializationFailed,
                activation.State);
            Assert.Equal(DesktopLegalView.LicenseText, controller.State.View);
            Assert.Equal(fixture.Anchor.BundleId, controller.State.BundleId);
            Assert.Equal("0.1.0", controller.State.ProductVersion);
            Assert.Equal("a", controller.State.SelectedComponent?.Id);
            Assert.Equal(
                sourceInformation,
                controller.State.SelectedComponent?.SourceInformation);
            Assert.Equal(
                "a LICENSE\nline two\nline three\n",
                controller.State.FullLicenseText);
            Assert.Equal(
                [Path.GetFullPath(installDirectory)],
                shell.OpenedFolders);
            Assert.False(networkProbe.Pending());
        }
        finally
        {
            networkProbe.Stop();
        }
    }

    [Fact]
    [Trait("Scenario", "IT-031")]
    public async Task TamperAndOutsideInstallPathClearDesktopLegalContentWithoutShellLaunch()
    {
        using var directory = TemporaryDirectory.Create();
        var installDirectory = Path.Combine(directory.Path, "install");
        var outsideBundle = Path.Combine(directory.Path, "outside-bundle");
        Directory.CreateDirectory(installDirectory);
        var fixture = await WriteGeneratedBundleAsync(
            outsideBundle,
            "offline source");
        var verifier = new AuthenticatedLegalBundleVerifier(
            new FixedAuthenticatedAnchorSource(fixture.Anchor));
        var shell = new CapturingLegalFolderShell();
        var controller = new DesktopLegalController(
            new AuthenticatedLegalCatalogReader(outsideBundle, verifier),
            new AuthenticatedLegalBundleFolderOpener(
                installDirectory,
                outsideBundle,
                verifier,
                shell));
        await controller.OpenAsync(CancellationToken.None);
        await controller.ShowDetailAsync("a", CancellationToken.None);
        await controller.ShowLicenseAsync(CancellationToken.None);

        await controller.OpenLicenseFolderAsync(CancellationToken.None);

        Assert.Equal(DesktopLegalView.Unavailable, controller.State.View);
        Assert.Null(controller.State.FullLicenseText);
        Assert.Empty(controller.State.Components);
        Assert.Empty(shell.OpenedFolders);
        Assert.Contains(controller.State.Issues, issue =>
            issue.Code == "legal-folder-outside-install-bundle");

        var insideFixture = await WriteGeneratedBundleAsync(
            installDirectory,
            "offline source");
        var insideVerifier = new AuthenticatedLegalBundleVerifier(
            new FixedAuthenticatedAnchorSource(insideFixture.Anchor));
        var tamperController = new DesktopLegalController(
            new AuthenticatedLegalCatalogReader(
                installDirectory,
                insideVerifier),
            new AuthenticatedLegalBundleFolderOpener(
                installDirectory,
                installDirectory,
                insideVerifier,
                shell));
        await tamperController.OpenAsync(CancellationToken.None);
        await tamperController.ShowDetailAsync("a", CancellationToken.None);
        await tamperController.ShowLicenseAsync(CancellationToken.None);
        await File.WriteAllTextAsync(
            Path.Combine(installDirectory, "LICENSES", "a", "LICENSE.txt"),
            "tampered stale text");

        await tamperController.RefreshAsync(CancellationToken.None);

        Assert.Equal(DesktopLegalView.Unavailable, tamperController.State.View);
        Assert.Null(tamperController.State.FullLicenseText);
        Assert.Null(tamperController.State.BundleId);
        Assert.Empty(tamperController.State.Components);
    }

    private static async Task<BundleFixture> WriteGeneratedBundleAsync(
        string directory,
        string sourceInformation)
    {
        var context = new SpdxGenerationContext(
            ProductName: "VR-Recorder",
            ProductVersion: "0.1.0",
            DocumentNamespace:
                $"https://example.invalid/spdx/desktop-legal-{Guid.NewGuid():N}",
            CreatedAtUtc: new DateTimeOffset(
                2026,
                7,
                10,
                0,
                0,
                0,
                TimeSpan.Zero),
            Creator: "Organization: VR-Recorder Project");
        var graph = new NormalizedComponentGraph(
            Dependencies:
            [
                new NuGetPackage(
                    "Package.B",
                    "2.0.0",
                    NuGetDependencyKind.Transitive),
                new NuGetPackage(
                    "Package.A",
                    "1.0.0",
                    NuGetDependencyKind.Direct),
            ],
            Components:
            [
                Component("b", "Package.B", "2.0.0", true, "source b"),
                Component(
                    "a",
                    "Package.A",
                    "1.0.0",
                    false,
                    sourceInformation),
            ]);
        var eligibility = ReleaseEligibilityGate.Evaluate(graph);
        Assert.True(eligibility.IsApproved);
        var artifacts = LegalArtifactSetGenerator.Generate(
            context,
            eligibility.ApprovedGraph!);
        await LegalArtifactDirectoryWriter.WriteAsync(
            directory,
            artifacts,
            CancellationToken.None);
        var manifest = artifacts.Artifacts.Single(artifact =>
            artifact.RelativePath == "LEGAL-MANIFEST.sha256");
        return new BundleFixture(new AuthenticatedLegalBundleAnchor(
            context.DocumentNamespace,
            manifest.Sha256));
    }

    private static NormalizedComponent Component(
        string id,
        string package,
        string version,
        bool modified,
        string sourceInformation)
    {
        var text = $"{id} LICENSE\nline two\nline three\n";
        return new NormalizedComponent(
            Id: id,
            DisplayName: $"Component {id}",
            Version: version,
            License: new LicenseDecision("MIT", "MIT"),
            CopyrightNotice: $"Copyright Component {id}",
            Usage: "runtime-feature",
            Linkage: "managed-library",
            Modified: modified,
            SourceInformation: sourceInformation,
            LicenseText: text,
            LegalFiles:
            [
                new VerifiedLegalFile(
                    LegalFileKind.License,
                    $"LICENSES/{id}/LICENSE.txt",
                    Hash(Encoding.UTF8.GetBytes(text)),
                    text),
            ],
            Scope: NoticeScope.RuntimeBundled,
            Approval: new LegalApproval(
                LegalApprovalStatus.Approved,
                TicketId: $"LEGAL-{id}",
                RequestedBy: "developer",
                Reviewer: "legal-reviewer"),
            Packages: [new NoticePackage(package, version)]);
    }

    private static string Hash(ReadOnlySpan<byte> content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    private sealed record BundleFixture(
        AuthenticatedLegalBundleAnchor Anchor);

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

    private sealed class CapturingLegalFolderShell : ILegalFolderShell
    {
        public List<string> OpenedFolders { get; } = [];

        public Task OpenFolderAsync(
            string folderPath,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OpenedFolders.Add(folderPath);
            return Task.CompletedTask;
        }
    }

    private sealed class FailingRecordingRuntimeFactory
        : IDesktopRecordingRuntimeFactory
    {
        public Task<IDesktopRecordingRuntime> InitializeAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromException<IDesktopRecordingRuntime>(
                new DesktopRecordingInitializationException(
                    "NATIVE_MEDIA_UNAVAILABLE",
                    "The native recording runtime is unavailable."));
        }
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
                $"vr-recorder-desktop-legal-{Guid.NewGuid():N}");
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
