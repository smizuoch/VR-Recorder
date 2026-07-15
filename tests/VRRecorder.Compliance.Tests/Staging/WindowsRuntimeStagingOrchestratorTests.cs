using System.Security.Cryptography;
using System.Text;
using VRRecorder.Compliance.Generation;
using VRRecorder.Compliance.Staging;

namespace VRRecorder.Compliance.Tests.Staging;

public sealed class WindowsRuntimeStagingOrchestratorTests
{
    private const string FactoryIntentSha =
        "1234567890abcdef1234567890abcdef1234567890abcdef1234567890abcdef";

    [Fact]
    public async Task StrictManifestFailureDoesNotCallAdmissionOrTouchOutput()
    {
        using var fixture = Fixture.Create();
        var marker = fixture.CreateExistingOutputMarker();
        await File.WriteAllTextAsync(
            fixture.ManifestPath,
            "{\"schemaVersion\":1,\"entries\":[],\"unknown\":true}");
        var planner = new CallbackPlanner((_, _, _, _, _) =>
            throw new InvalidOperationException("planner must not run"));
        var publisher = new CallbackPublisher((_, _, _) =>
            throw new InvalidOperationException("publisher must not run"));
        var orchestrator = new WindowsRuntimeStagingOrchestrator(
            WindowsRuntimeStagingPlatformGate.AllowForPortableTests,
            planner,
            publisher);

        var result = await orchestrator.StageAsync(
            fixture.Request,
            CancellationToken.None);

        Assert.False(result.IsStaged);
        Assert.Null(result.Publication);
        AssertIssue("invalid-windows-runtime-staging-manifest", result);
        Assert.Equal("existing", await File.ReadAllTextAsync(marker));
        Assert.Single(Directory.EnumerateFileSystemEntries(fixture.OutputRoot));
    }

    [Fact]
    public async Task RejectedAdmissionDoesNotCallPublisherOrTouchOutput()
    {
        using var fixture = Fixture.Create();
        var marker = fixture.CreateExistingOutputMarker();
        var planner = new CallbackPlanner((_, _, _, _, _) => Task.FromResult(
            new WindowsRuntimeStagingAdmissionResult(
                null,
                [new ComplianceIssue("synthetic-admission-failure", "fixture")])));
        var publisher = new CallbackPublisher((_, _, _) =>
            throw new InvalidOperationException("publisher must not run"));
        var orchestrator = new WindowsRuntimeStagingOrchestrator(
            WindowsRuntimeStagingPlatformGate.AllowForPortableTests,
            planner,
            publisher);

        var result = await orchestrator.StageAsync(
            fixture.Request,
            CancellationToken.None);

        Assert.False(result.IsStaged);
        AssertIssue("synthetic-admission-failure", result);
        Assert.Equal("existing", await File.ReadAllTextAsync(marker));
        Assert.Single(Directory.EnumerateFileSystemEntries(fixture.OutputRoot));
    }

    [Fact]
    public async Task AdmittedPlanIsPublishedAndReturnsExactPropsPath()
    {
        using var fixture = Fixture.Create();
        var planner = new CallbackPlanner((manifest, source, _, _, _) =>
            Task.FromResult(new WindowsRuntimeStagingAdmissionResult(
                fixture.CreateAdmittedPlan(manifest, source),
                [])));
        var orchestrator = new WindowsRuntimeStagingOrchestrator(
            WindowsRuntimeStagingPlatformGate.AllowForPortableTests,
            planner,
            new ImmutableWindowsRuntimeStagingPublisher());

        var result = await orchestrator.StageAsync(
            fixture.Request,
            CancellationToken.None);

        Assert.Empty(result.Issues);
        Assert.True(result.IsStaged);
        var publication = Assert.IsType<WindowsRuntimeStagingPublication>(
            result.Publication);
        Assert.True(File.Exists(publication.ApprovedPropsPath));
        Assert.Equal(
            "payload",
            await File.ReadAllTextAsync(Path.Combine(
                publication.PayloadDirectory,
                "asset.txt")));
    }

    [Fact]
    public async Task ExactFirstPartyManifestRunsThroughRealAdmissionAndPublish()
    {
        using var fixture = Fixture.Create();
        fixture.UseExactFirstPartyRuntime();
        var orchestrator = new WindowsRuntimeStagingOrchestrator(
            WindowsRuntimeStagingPlatformGate.AllowForPortableTests,
            new WindowsRuntimeStagingAdmissionPlanner(),
            new ImmutableWindowsRuntimeStagingPublisher());

        var result = await orchestrator.StageAsync(
            fixture.Request,
            CancellationToken.None);

        Assert.Empty(result.Issues);
        Assert.True(result.IsStaged);
        var publication = Assert.IsType<WindowsRuntimeStagingPublication>(
            result.Publication);
        Assert.True(File.Exists(Path.Combine(
            publication.PayloadDirectory,
            "vrrecorder_native.dll")));
        Assert.True(File.Exists(Path.Combine(
            publication.PayloadDirectory,
            "native-factory-selection.json")));
    }

    [Fact]
    public async Task PublicationFailureIsClosedAndOwnedTemporaryOutputIsRemoved()
    {
        using var fixture = Fixture.Create();
        var marker = fixture.CreateExistingOutputMarker();
        var planner = new CallbackPlanner((manifest, source, _, _, _) =>
            Task.FromResult(new WindowsRuntimeStagingAdmissionResult(
                fixture.CreateAdmittedPlan(manifest, source),
                [])));
        var publisher = new CallbackPublisher((_, _, _) =>
            throw new IOException("synthetic publication failure"));
        var orchestrator = new WindowsRuntimeStagingOrchestrator(
            WindowsRuntimeStagingPlatformGate.AllowForPortableTests,
            planner,
            publisher);

        var result = await orchestrator.StageAsync(
            fixture.Request,
            CancellationToken.None);

        Assert.False(result.IsStaged);
        AssertIssue("windows-runtime-staging-publication-failed", result);
        Assert.Equal("existing", await File.ReadAllTextAsync(marker));
        Assert.Single(Directory.EnumerateFileSystemEntries(fixture.OutputRoot));
    }

    [Fact]
    public async Task DefaultEntryPointFailsClosedOutsideWindows()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = Fixture.Create();

        var result = await new WindowsRuntimeStagingOrchestrator().StageAsync(
            fixture.Request,
            CancellationToken.None);

        Assert.False(result.IsStaged);
        AssertIssue("windows-runtime-staging-requires-windows", result);
        Assert.False(Directory.Exists(fixture.OutputRoot));
    }

    [Fact]
    public async Task LinkedManifestIsRejectedWithoutFollowingIt()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var fixture = Fixture.Create();
        var linkedManifest = Path.Combine(fixture.Root, "linked-manifest.json");
        File.CreateSymbolicLink(linkedManifest, fixture.ManifestPath);
        var request = fixture.Request with { ManifestPath = linkedManifest };
        var orchestrator = new WindowsRuntimeStagingOrchestrator(
            WindowsRuntimeStagingPlatformGate.AllowForPortableTests,
            new CallbackPlanner((_, _, _, _, _) =>
                throw new InvalidOperationException("planner must not run")),
            new CallbackPublisher((_, _, _) =>
                throw new InvalidOperationException("publisher must not run")));

        var result = await orchestrator.StageAsync(
            request,
            CancellationToken.None);

        Assert.False(result.IsStaged);
        AssertIssue("invalid-windows-runtime-staging-manifest", result);
        Assert.False(Directory.Exists(fixture.OutputRoot));
    }

    [Fact]
    public async Task CancellationIsPropagatedWithoutCreatingOutput()
    {
        using var fixture = Fixture.Create();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new WindowsRuntimeStagingOrchestrator(
                    WindowsRuntimeStagingPlatformGate.AllowForPortableTests,
                    new CallbackPlanner((_, _, _, _, _) =>
                        throw new InvalidOperationException()),
                    new CallbackPublisher((_, _, _) =>
                        throw new InvalidOperationException()))
                .StageAsync(fixture.Request, cancellation.Token));

        Assert.False(Directory.Exists(fixture.OutputRoot));
    }

    private static void AssertIssue(
        string code,
        WindowsRuntimeStagingResult result) =>
        Assert.Contains(result.Issues, issue => issue.Code == code);

    private sealed class CallbackPlanner(
        Func<WindowsRuntimeStagingManifest,
            string,
            ApprovedReleaseGraph,
            string,
            CancellationToken,
            Task<WindowsRuntimeStagingAdmissionResult>> callback)
        : IWindowsRuntimeStagingAdmissionPlanner
    {
        public Task<WindowsRuntimeStagingAdmissionResult> PlanAsync(
            WindowsRuntimeStagingManifest manifest,
            string sourceRoot,
            ApprovedReleaseGraph approvedGraph,
            string repositoryRoot,
            CancellationToken cancellationToken) => callback(
            manifest,
            sourceRoot,
            approvedGraph,
            repositoryRoot,
            cancellationToken);
    }

    private sealed class CallbackPublisher(
        Func<AdmittedWindowsRuntimeStagingPlan,
            string,
            CancellationToken,
            Task<WindowsRuntimeStagingPublication>> callback)
        : IWindowsRuntimeStagingPublisher
    {
        public Task<WindowsRuntimeStagingPublication> PublishAsync(
            AdmittedWindowsRuntimeStagingPlan plan,
            string outputParent,
            CancellationToken cancellationToken) => callback(
            plan,
            outputParent,
            cancellationToken);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly byte[] _asset = "payload"u8.ToArray();

        private Fixture(string root)
        {
            Root = root;
            SourceRoot = Path.Combine(root, "source");
            RepositoryRoot = Path.Combine(root, "repository");
            OutputRoot = Path.Combine(root, "output");
            ManifestPath = Path.Combine(root, "runtime-manifest.json");
            Directory.CreateDirectory(SourceRoot);
            Directory.CreateDirectory(RepositoryRoot);
            File.WriteAllBytes(Path.Combine(SourceRoot, "asset.txt"), _asset);
            var manifest = $$"""
                {"schemaVersion":1,"entries":[{"source":"asset.txt","target":"asset.txt","role":"application-asset","componentId":"vr-recorder","platform":"windows-x64","deploymentKind":"asset","sha256":"{{Sha256(_asset)}}"}]}
                """;
            File.WriteAllBytes(
                ManifestPath,
                Encoding.UTF8.GetBytes(manifest));
            Request = new WindowsRuntimeStagingRequest(
                ManifestPath,
                SourceRoot,
                OutputRoot,
                RepositoryRoot,
                new ApprovedReleaseGraph(new NormalizedComponentGraph([], [])));
        }

        public string Root { get; }

        public string SourceRoot { get; }

        public string RepositoryRoot { get; }

        public string OutputRoot { get; }

        public string ManifestPath { get; }

        public WindowsRuntimeStagingRequest Request { get; }

        public static Fixture Create()
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                $"vr-recorder-staging-orchestrator-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            return new Fixture(root);
        }

        public AdmittedWindowsRuntimeStagingPlan CreateAdmittedPlan(
            WindowsRuntimeStagingManifest manifest,
            string sourceRoot) => new(
            manifest.ManifestSha256,
            Path.GetFullPath(sourceRoot),
            [new AdmittedWindowsRuntimeStagingFile(
                "asset.txt",
                "asset.txt",
                WindowsRuntimeRole.ApplicationAsset,
                "vr-recorder",
                WindowsRuntimeDeploymentKind.Asset,
                Sha256(_asset),
                _asset.LongLength,
                StagedArtifactKind.Asset)]);

        public void UseExactFirstPartyRuntime()
        {
            File.Delete(Path.Combine(SourceRoot, "asset.txt"));
            var native = Encoding.ASCII.GetBytes(
                "prefix-VRRECORDER_FACTORY_SELECTION_V1:" +
                FactoryIntentSha +
                "-suffix");
            var nativePath = Path.Combine(
                SourceRoot,
                "native",
                "vrrecorder_native.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(nativePath)!);
            File.WriteAllBytes(nativePath, native);
            var evidence = $$$"""
                {"schemaVersion":1,"evidenceKind":"linked-native-factory-selection","selectionIntentSha256":"{{{FactoryIntentSha}}}","fullProductionRequired":true,"nativeBinary":{"file":"vrrecorder_native.dll","length":{{{native.LongLength}}},"sha256":"{{{Sha256(native)}}}"},"media":{"variant":"PRODUCTION","source":"production_media_backend.cpp"},"encoderProbe":{"variant":"PRODUCTION","source":"production_encoder_probe_backend.cpp"},"spout":{"variant":"PRODUCTION","source":"spout2_source_backend.cpp"},"steamVr":{"variant":"PRODUCTION","source":"openvr_steamvr_input_backend.cpp"}}
                """;
            var evidenceBytes = Encoding.UTF8.GetBytes(evidence);
            var evidencePath = Path.Combine(
                SourceRoot,
                "evidence",
                "native-factory-selection.json");
            Directory.CreateDirectory(Path.GetDirectoryName(evidencePath)!);
            File.WriteAllBytes(evidencePath, evidenceBytes);
            var manifest = $$"""
                {"schemaVersion":1,"entries":[{"source":"native/vrrecorder_native.dll","target":"vrrecorder_native.dll","role":"first-party-native","componentId":"vr-recorder","platform":"windows-x64","deploymentKind":"native-library","sha256":"{{Sha256(native)}}"},{"source":"evidence/native-factory-selection.json","target":"native-factory-selection.json","role":"factory-selection-evidence","componentId":"vr-recorder","platform":"windows-x64","deploymentKind":"evidence","sha256":"{{Sha256(evidenceBytes)}}"}]}
                """;
            File.WriteAllBytes(
                ManifestPath,
                Encoding.UTF8.GetBytes(manifest));
        }

        public string CreateExistingOutputMarker()
        {
            Directory.CreateDirectory(OutputRoot);
            var marker = Path.Combine(OutputRoot, "existing.txt");
            File.WriteAllText(marker, "existing");
            return marker;
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }

    private static string Sha256(byte[] bytes) => Convert
        .ToHexString(SHA256.HashData(bytes))
        .ToLowerInvariant();
}
