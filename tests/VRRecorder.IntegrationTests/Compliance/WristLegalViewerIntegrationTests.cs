using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using VRRecorder.Application.Compliance;
using VRRecorder.Application.Ports;
using VRRecorder.Application.Presentation;
using VRRecorder.Compliance.Dependencies;
using VRRecorder.Compliance.Generation;
using VRRecorder.Compliance.Runtime;
using VRRecorder.DesignSystem;
using VRRecorder.Domain.Recording;
using VRRecorder.Presentation.Wrist;

namespace VRRecorder.IntegrationTests.Compliance;

public sealed class WristLegalViewerIntegrationTests
{
    [Fact]
    [Trait("Scenario", "IT-029")]
    public async Task GeneratedBundleIsBrowsableOfflineAndTamperClearsVisibleText()
    {
        using var directory = TemporaryDirectory.Create();
        var networkProbe = new TcpListener(IPAddress.Loopback, 0);
        networkProbe.Start();
        try
        {
            var endpoint = (IPEndPoint)networkProbe.LocalEndpoint;
            var sourceInformation =
                $"http://127.0.0.1:{endpoint.Port}/must-remain-offline";
            var fixture = await WriteGeneratedBundleAsync(
                directory.Path,
                sourceInformation);
            var reader = new AuthenticatedLegalCatalogReader(
                directory.Path,
                new AuthenticatedLegalBundleVerifier(
                    new FixedAuthenticatedAnchorSource(fixture.Anchor)));
            var sink = new CapturingComplianceFaultSink();
            var controller = new WristLegalController(
                reader,
                sink,
                linesPerPage: 2);
            var projector = new WristLegalProjector(
                EnglishUiLocalizer.Instance);
            var recording = new RecorderStatusSnapshot(
                Revision: 42,
                State: RecorderState.Recording,
                AvailableActions: RecorderAvailableActions.Stop);

            await controller.OpenAsync(CancellationToken.None);

            var list = projector.Project(controller.State, recording);
            Assert.Equal(WristLegalView.ComponentList, list.View);
            Assert.Equal(["a", "b"], list.Components.Select(item => item.Id));
            AssertCanonicalStop(list);

            await controller.ShowDetailAsync("a", CancellationToken.None);

            var detail = projector.Project(controller.State, recording);
            Assert.Equal(WristLegalView.ComponentDetail, detail.View);
            Assert.Contains(detail.DetailFields, field =>
                field.Label.Value == "Version" && field.Value == "1.0.0");
            Assert.Contains(detail.DetailFields, field =>
                field.Label.Value == "License" && field.Value == "MIT");
            Assert.Contains(detail.DetailFields, field =>
                field.Label.Value == "Modified" && field.Value == "No");
            Assert.Contains(detail.DetailFields, field =>
                field.Label.Value == "Source information" &&
                field.Value == sourceInformation);
            AssertCanonicalStop(detail);

            await controller.ShowLicenseAsync(CancellationToken.None);

            var license = projector.Project(controller.State, recording);
            Assert.Equal(WristLegalView.LicenseText, license.View);
            Assert.Equal("a LICENSE\nline two", license.LicensePage?.Text);
            Assert.Equal(1, license.LicensePage?.PageNumber);
            Assert.Equal(2, license.LicensePage?.PageCount);
            var stop = AssertCanonicalStop(license);
            var commands = new CapturingUiCommandDispatcher();

            await new WristInputAdapter(commands)
                .ActivateAsync(stop, CancellationToken.None);

            var command = Assert.Single(commands.Commands);
            Assert.Equal(UiCommandId.ToggleRecording, command.Command);
            Assert.Equal(UiActivationKind.WristRay, command.ActivationKind);
            Assert.False(networkProbe.Pending());

            await File.WriteAllTextAsync(
                Path.Combine(
                    directory.Path,
                    "LICENSES",
                    "a",
                    "LICENSE.txt"),
                "tampered stale text");

            await controller.RefreshAsync(CancellationToken.None);

            Assert.Equal(WristLegalView.Unavailable, controller.State.View);
            Assert.Null(controller.State.FullLicenseText);
            Assert.Null(controller.State.SelectedComponent);
            Assert.Empty(controller.State.Components);
            var rejected = projector.Project(controller.State, recording);
            Assert.Null(rejected.LicensePage);
            Assert.DoesNotContain(
                "a LICENSE",
                rejected.StatusMessage?.Value ?? string.Empty,
                StringComparison.Ordinal);
            AssertCanonicalStop(rejected);
            Assert.Equal(1, sink.CallCount);
            Assert.False(networkProbe.Pending());
        }
        finally
        {
            networkProbe.Stop();
        }
    }

    private static UiActionSnapshot AssertCanonicalStop(
        WristLegalUiSnapshot snapshot)
    {
        var stop = Assert.Single(snapshot.FixedRecordingActions);
        Assert.Equal("recording.stop", stop.SemanticId);
        Assert.Equal(UiCommandId.ToggleRecording, stop.Command);
        Assert.True(stop.IsEnabled);
        return stop;
    }

    private static async Task<BundleFixture> WriteGeneratedBundleAsync(
        string directory,
        string sourceInformation)
    {
        var context = new SpdxGenerationContext(
            ProductName: "VR-Recorder",
            ProductVersion: "0.1.0",
            DocumentNamespace:
                "https://example.invalid/spdx/wrist-legal-it-029",
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
                Component(
                    "b",
                    "Package.B",
                    "2.0.0",
                    modified: true,
                    "bundled source b@commit"),
                Component(
                    "a",
                    "Package.A",
                    "1.0.0",
                    modified: false,
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

    private sealed class CapturingComplianceFaultSink : IComplianceFaultSink
    {
        public int CallCount { get; private set; }

        public ValueTask EnterComplianceFaultAsync()
        {
            CallCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CapturingUiCommandDispatcher : IUiCommandDispatcher
    {
        public List<(UiCommandId Command, UiActivationKind ActivationKind)>
            Commands
        { get; } = [];

        public Task DispatchAsync(
            UiCommandId command,
            UiActivationKind activationKind,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Commands.Add((command, activationKind));
            return Task.CompletedTask;
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
                $"vr-recorder-wrist-legal-it-029-{Guid.NewGuid():N}");
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
