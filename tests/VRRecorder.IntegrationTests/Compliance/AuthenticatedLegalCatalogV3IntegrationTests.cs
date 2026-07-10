using System.Security.Cryptography;
using System.Text;
using VRRecorder.Application.Compliance;
using VRRecorder.Application.Desktop;
using VRRecorder.Application.Ports;
using VRRecorder.Application.Presentation;
using VRRecorder.Application.Recording;
using VRRecorder.Compliance.Dependencies;
using VRRecorder.Compliance.Generation;
using VRRecorder.Compliance.Runtime;
using VRRecorder.Domain.Recording;
using VRRecorder.Presentation.Wrist;

namespace VRRecorder.IntegrationTests.Compliance;

public sealed class AuthenticatedLegalCatalogV3IntegrationTests
{
    [Fact]
    public async Task GeneratedV3BundleReadsEveryTypedDocumentAndFailsClosedOnTamper()
    {
        using var directory = TemporaryDirectory.Create();
        var context = new SpdxGenerationContext(
            ProductName: "VR-Recorder",
            ProductVersion: "0.1.0",
            DocumentNamespace:
                "https://example.invalid/spdx/legal-catalog-v3-integration",
            CreatedAtUtc: new DateTimeOffset(
                2026,
                7,
                10,
                0,
                0,
                0,
                TimeSpan.Zero),
            Creator: "Organization: VR-Recorder Project");
        var texts = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["LICENSES/example/LICENSE.txt"] = "license text\n",
            ["NOTICES/example/NOTICE.txt"] = "notice text\n",
            ["COPYRIGHTS/example.txt"] = "copyright document\n",
            ["RIGHTS/example-attribution.txt"] = "attribution text\n",
            ["MATERIAL-SYMBOLS-MANIFEST.json"] =
                MaterialSymbolsManifestTestFixture.Create(
                    "LICENSES/example/LICENSE.txt",
                    "RIGHTS/example-attribution.txt",
                    "present"),
        };
        var graph = new NormalizedComponentGraph(
            [new NuGetPackage(
                "Package.Example",
                "1.0.0",
                NuGetDependencyKind.Direct)],
            [Component(texts)]);
        var eligibility = ReleaseEligibilityGate.Evaluate(graph);
        Assert.True(eligibility.IsApproved);
        var artifacts = LegalArtifactSetGenerator.Generate(
            context,
            eligibility.ApprovedGraph!);
        await LegalArtifactDirectoryWriter.WriteAsync(
            directory.Path,
            artifacts,
            CancellationToken.None);
        var manifest = artifacts.Artifacts.Single(artifact =>
            artifact.RelativePath == "LEGAL-MANIFEST.sha256");
        var reader = new AuthenticatedLegalCatalogReader(
            directory.Path,
            new AuthenticatedLegalBundleVerifier(
                new FixedAuthenticatedAnchorSource(
                    new AuthenticatedLegalBundleAnchor(
                        context.DocumentNamespace,
                        manifest.Sha256))));

        var catalog = Assert.IsType<LegalCatalogReadResult.Available>(
            await reader.ReadAsync(CancellationToken.None)).Catalog;
        Assert.Equal(manifest.Sha256, catalog.ManifestSha256);
        var component = Assert.Single(catalog.Components);
        Assert.Equal("Copyright Example", component.CopyrightNotice);
        Assert.Equal(
        [
            LegalDocumentKind.License,
            LegalDocumentKind.Notice,
            LegalDocumentKind.Copyright,
            LegalDocumentKind.Attribution,
            LegalDocumentKind.AssetManifest,
        ],
            component.LegalDocuments.Select(reference => reference.Kind));

        foreach (var reference in component.LegalDocuments)
        {
            var result = await reader.ReadDocumentAsync(
                component.Id,
                reference,
                CancellationToken.None);
            var document = Assert.IsType<LegalTextReadResult.Available>(
                result).Document;
            Assert.Equal(reference, document.Reference);
            Assert.Equal(texts[reference.RelativePath], document.Text);
        }

        var desktopSink = new CapturingComplianceFaultSink();
        var desktop = new DesktopLegalController(
            reader,
            new NeverOpenedFolderOpener(),
            desktopSink);
        await desktop.OpenAsync(CancellationToken.None);
        await desktop.ShowDetailAsync(component.Id, CancellationToken.None);
        foreach (var reference in component.LegalDocuments)
        {
            await desktop.ShowDocumentAsync(reference, CancellationToken.None);
            Assert.Equal(reference, desktop.State.SelectedDocument);
            Assert.Equal(
                texts[reference.RelativePath],
                desktop.State.FullDocumentText);
        }

        var runtime = new TrackingRecordingRuntime();
        await using var recordingHost = new DesktopRecordingCommandHost(
            new ReadyRecordingRuntimeFactory(runtime));
        await recordingHost.ActivateAsync(
            new RecorderStartupResult(RecorderState.Ready, []),
            CancellationToken.None);
        var wrist = new WristLegalController(
            reader,
            recordingHost,
            linesPerPage: 2);
        await wrist.OpenAsync(CancellationToken.None);
        await wrist.ShowDetailAsync(component.Id, CancellationToken.None);
        var wristProjector = new WristLegalProjector(
            EnglishUiLocalizer.Instance);
        var wristDetail = wristProjector.Project(
            wrist.State,
            new RecorderStatusSnapshot(
                Revision: 1,
                State: RecorderState.Recording,
                AvailableActions: RecorderAvailableActions.Stop));
        Assert.Equal(
            component.LegalDocuments,
            wristDetail.Documents.Select(document => document.Reference));
        var notice = component.LegalDocuments.Single(reference =>
            reference.Kind == LegalDocumentKind.Notice);
        await wrist.ShowDocumentAsync(notice, CancellationToken.None);
        var wristProjection = wristProjector.Project(
            wrist.State,
            new RecorderStatusSnapshot(
                Revision: 1,
                State: RecorderState.Recording,
                AvailableActions: RecorderAvailableActions.Stop));
        Assert.Equal(manifest.Sha256, wrist.State.ManifestSha256);
        Assert.Equal(
            texts[notice.RelativePath].TrimEnd('\n'),
            wristProjection.DocumentPage?.Text);

        await File.AppendAllTextAsync(
            Path.Combine(
                directory.Path,
                "NOTICES",
                "example",
                "NOTICE.txt"),
            "tamper");

        await wrist.RefreshAsync(CancellationToken.None);
        await desktop.RefreshAsync(CancellationToken.None);

        Assert.Equal(WristLegalView.Unavailable, wrist.State.View);
        Assert.Null(wrist.State.BundleId);
        Assert.Null(wrist.State.ManifestSha256);
        Assert.Null(wrist.State.SelectedComponent);
        Assert.Null(wrist.State.SelectedDocument);
        Assert.Null(wrist.State.FullDocumentText);
        Assert.Empty(wrist.State.Components);
        Assert.Equal(DesktopLegalView.Unavailable, desktop.State.View);
        Assert.Null(desktop.State.BundleId);
        Assert.Null(desktop.State.ManifestSha256);
        Assert.Null(desktop.State.SelectedComponent);
        Assert.Null(desktop.State.SelectedDocument);
        Assert.Null(desktop.State.FullDocumentText);
        Assert.Empty(desktop.State.Components);
        Assert.Equal(1, desktopSink.CallCount);
        Assert.Equal(DesktopRecordingHostState.ComplianceFault, recordingHost.State);
        Assert.Equal(1, runtime.DisposeCallCount);
        Assert.Equal(
            [RecordingStopReason.ComplianceFault],
            runtime.ShutdownReasons);
        var permanentlyUnavailable = await Assert.ThrowsAsync<
            DesktopRecordingUnavailableException>(() =>
            recordingHost.ToggleAsync(CancellationToken.None));
        Assert.Equal(
            DesktopRecordingHostState.ComplianceFault,
            permanentlyUnavailable.State);
        var faultProjection = new WristLegalProjector(
            EnglishUiLocalizer.Instance).Project(
                wrist.State,
                new RecorderStatusSnapshot(
                    Revision: 2,
                    State: RecorderState.Recording,
                    AvailableActions: RecorderAvailableActions.Stop));
        Assert.Null(faultProjection.DocumentPage);
        Assert.Equal(
            "recording.stop",
            Assert.Single(faultProjection.FixedRecordingActions).SemanticId);

        var tampered = await reader.ReadDocumentAsync(
            component.Id,
            component.LegalDocuments.Single(reference =>
                reference.Kind == LegalDocumentKind.Notice),
            CancellationToken.None);

        var rejected = Assert.IsType<LegalTextReadResult.Rejected>(tampered);
        Assert.Contains(rejected.Issues, issue =>
            issue.Code == "legal-bundle-payload-hash-mismatch" &&
            issue.Subject == "NOTICES/example/NOTICE.txt");
    }

    private static NormalizedComponent Component(
        Dictionary<string, string> texts) =>
        new(
            Id: "material-symbols",
            DisplayName:
                "Material Symbols (Material Design icons by Google)",
            Version: MaterialSymbolsManifestTestFixture.Commit,
            License: new LicenseDecision("Apache-2.0", "Apache-2.0"),
            CopyrightNotice: "Copyright Example",
            Usage: "runtime",
            Linkage: "managed-library",
            Modified: true,
            SourceInformation:
                "https://github.com/google/material-design-icons@" +
                MaterialSymbolsManifestTestFixture.Commit,
            LicenseText: texts["LICENSES/example/LICENSE.txt"],
            LegalFiles:
            [
                LegalFile(
                    LegalFileKind.License,
                    "LICENSES/example/LICENSE.txt",
                    texts),
                LegalFile(
                    LegalFileKind.Notice,
                    "NOTICES/example/NOTICE.txt",
                    texts),
                LegalFile(
                    LegalFileKind.Copyright,
                    "COPYRIGHTS/example.txt",
                    texts),
                LegalFile(
                    LegalFileKind.Attribution,
                    "RIGHTS/example-attribution.txt",
                    texts),
                LegalFile(
                    LegalFileKind.AssetManifest,
                    "MATERIAL-SYMBOLS-MANIFEST.json",
                    texts),
            ],
            Scope: NoticeScope.RuntimeBundled,
            Approval: new LegalApproval(
                LegalApprovalStatus.Approved,
                TicketId: "LEGAL-EXAMPLE",
                RequestedBy: "developer",
                Reviewer: "independent-legal-reviewer"),
            Packages: [new NoticePackage("Package.Example", "1.0.0")]);

    private static VerifiedLegalFile LegalFile(
        LegalFileKind kind,
        string path,
        Dictionary<string, string> texts)
    {
        var text = texts[path];
        return new VerifiedLegalFile(kind, path, Hash(text), text);
    }

    private static string Hash(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))
            .ToLowerInvariant();

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

    private sealed class NeverOpenedFolderOpener : ILegalBundleFolderOpener
    {
        public Task<LegalFolderOpenResult> OpenAsync(
            string expectedBundleId,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                $"Folder {expectedBundleId} must not be opened by this test.");
    }

    private sealed class ReadyRecordingRuntimeFactory(
        IDesktopRecordingRuntime runtime)
        : IDesktopRecordingRuntimeFactory
    {
        public Task<IDesktopRecordingRuntime> InitializeAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(runtime);
        }
    }

    private sealed class TrackingRecordingRuntime : IDesktopRecordingRuntime
    {
        private readonly RecorderStatusHub _statuses = new(
            RecorderStatusSnapshot.Create(0, RecorderState.Ready));

        public int DisposeCallCount { get; private set; }

        public List<RecordingStopReason> ShutdownReasons { get; } = [];

        public RecorderStatusSnapshot Current => _statuses.Current;

        public IDisposable Subscribe(
            Action<RecorderStatusSnapshot> subscriber) =>
            _statuses.Subscribe(subscriber);

        public Task ToggleAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task ShutdownAsync(RecordingStopReason reason)
        {
            ShutdownReasons.Add(reason);
            return DisposeAsync().AsTask();
        }

        public ValueTask DisposeAsync()
        {
            DisposeCallCount++;
            _statuses.Dispose();
            return ValueTask.CompletedTask;
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
                $"vr-recorder-legal-v3-integration-{Guid.NewGuid():N}");
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
