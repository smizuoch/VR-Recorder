using System.Security.Cryptography;
using System.Text;
using VRRecorder.Compliance.Generation;
using VRRecorder.Compliance.Staging;

namespace VRRecorder.Compliance.Tests.Staging;

public sealed class WindowsRuntimeStagingAdmissionPlannerTests
{
    private const string IntentSha =
        "1234567890abcdef1234567890abcdef1234567890abcdef1234567890abcdef";

    [Fact]
    public async Task ExactFirstPartyFixtureProducesImmutableIdentityPlan()
    {
        using var fixture = Fixture.Create();

        var result = await fixture.PlanAsync();

        Assert.True(result.IsAdmitted);
        Assert.Empty(result.Issues);
        var plan = Assert.IsType<AdmittedWindowsRuntimeStagingPlan>(result.Plan);
        Assert.Equal(fixture.Manifest.ManifestSha256, plan.ManifestSha256);
        Assert.Equal(fixture.SourceRoot, plan.SourceRoot);
        Assert.Equal(2, plan.Files.Count);
        var native = Assert.Single(
            plan.Files,
            file => file.Role == WindowsRuntimeRole.FirstPartyNative);
        Assert.Equal("native/vrrecorder_native.dll", native.Source);
        Assert.Equal("vrrecorder_native.dll", native.Target);
        Assert.Equal("vr-recorder", native.ComponentId);
        Assert.Equal(
            WindowsRuntimeDeploymentKind.NativeLibrary,
            native.DeploymentKind);
        Assert.Equal(fixture.Binary.LongLength, native.Length);
        Assert.Equal(StagedArtifactKind.NativeLibrary, native.Kind);
        Assert.Equal(Sha256(fixture.Binary), native.Sha256);

        var mutableView = Assert.IsAssignableFrom<
            IList<AdmittedWindowsRuntimeStagingFile>>(plan.Files);
        Assert.Throws<NotSupportedException>(() => mutableView.Clear());
    }

    [Fact]
    public async Task MissingExtraAndHashMismatchAreRejectedByExactClosure()
    {
        using var missing = Fixture.Create();
        File.Delete(missing.Resolve(missing.EvidenceEntry.Source));
        AssertIssue(
            "registered-staging-file-missing",
            await missing.PlanAsync());

        using var extra = Fixture.Create();
        await File.WriteAllTextAsync(extra.Resolve("unexpected.txt"), "extra");
        AssertIssue("unregistered-staging-file", await extra.PlanAsync());

        using var changed = Fixture.Create();
        await File.AppendAllTextAsync(
            changed.Resolve(changed.EvidenceEntry.Source),
            "changed");
        AssertIssue("staging-file-hash-mismatch", await changed.PlanAsync());
    }

    [Fact]
    public async Task DeploymentKindMustMatchScannedFileKind()
    {
        using var fixture = Fixture.Create();
        var disguised = new TestEntry(
            "assets/disguised.dll",
            "assets/disguised.dll",
            "application-asset",
            "vr-recorder",
            "asset");
        fixture.Write(disguised.Source, Encoding.UTF8.GetBytes("not-a-pe"));
        fixture.UseEntries(
            fixture.NativeEntry,
            fixture.EvidenceEntry,
            disguised);

        AssertIssue("staging-file-kind-mismatch", await fixture.PlanAsync());
    }

    [Fact]
    public async Task SourceAndRepositoryRootsMustBeExistingCanonicalDirectories()
    {
        using var fixture = Fixture.Create();
        var missingSource = Path.Combine(fixture.Root, "missing-source");
        var missingRepository = Path.Combine(fixture.Root, "missing-repository");

        AssertIssue(
            "invalid-runtime-staging-source-root",
            await fixture.PlanAsync(sourceRoot: missingSource));
        AssertIssue(
            "invalid-runtime-staging-repository-root",
            await fixture.PlanAsync(repositoryRoot: missingRepository));
    }

    [Fact]
    public async Task LinkedSourceRootAndLinkedEntryAreRejectedWithoutFollowingThem()
    {
        using var fixture = Fixture.Create();
        var linkedRoot = Path.Combine(fixture.Root, "linked-source");
        if (!TryCreateDirectoryLink(linkedRoot, fixture.SourceRoot))
        {
            return;
        }

        AssertIssue(
            "invalid-runtime-staging-source-root",
            await fixture.PlanAsync(sourceRoot: linkedRoot));

        var linkedFile = fixture.Resolve("linked-evidence.json");
        if (!TryCreateFileLink(
                linkedFile,
                fixture.Resolve(fixture.EvidenceEntry.Source)))
        {
            return;
        }

        AssertIssue("staging-link-not-allowed", await fixture.PlanAsync());
    }

    [Fact]
    public async Task NativeFactoryDllAndEvidenceMustBeAnExactFirstPartyPair()
    {
        using var missingNative = Fixture.Create();
        File.Delete(missingNative.Resolve(missingNative.NativeEntry.Source));
        missingNative.UseEntries(missingNative.EvidenceEntry);
        AssertIssue(
            "invalid-native-factory-staging-pair",
            await missingNative.PlanAsync());

        using var duplicateNative = Fixture.Create();
        var duplicate = new TestEntry(
            "duplicate/vrrecorder_native.dll",
            "duplicate/vrrecorder_native.dll",
            "first-party-native",
            "vr-recorder",
            "native-library");
        duplicateNative.Write(duplicate.Source, duplicateNative.Binary);
        duplicateNative.UseEntries(
            duplicateNative.NativeEntry,
            duplicateNative.EvidenceEntry,
            duplicate);
        AssertIssue(
            "invalid-native-factory-staging-pair",
            await duplicateNative.PlanAsync());

        using var wrongOwner = Fixture.Create();
        wrongOwner.UseEntries(
            wrongOwner.NativeEntry,
            wrongOwner.EvidenceEntry with { ComponentId = "other" });
        AssertIssue(
            "invalid-native-factory-staging-pair",
            await wrongOwner.PlanAsync());
    }

    [Fact]
    public async Task FactoryEvidenceMustBindProductionFamiliesAndActualDllIdentity()
    {
        using var invalidFamily = Fixture.Create();
        invalidFamily.WriteEvidence(fullProductionRequired: false);
        invalidFamily.UseEntries(
            invalidFamily.NativeEntry,
            invalidFamily.EvidenceEntry);
        AssertIssue(
            "invalid-native-factory-selection-evidence",
            await invalidFamily.PlanAsync());

        using var wrongLength = Fixture.Create();
        wrongLength.WriteEvidence(
            fullProductionRequired: true,
            declaredBinaryLength: wrongLength.Binary.LongLength + 1);
        wrongLength.UseEntries(
            wrongLength.NativeEntry,
            wrongLength.EvidenceEntry);
        AssertIssue(
            "native-factory-binary-identity-mismatch",
            await wrongLength.PlanAsync());
    }

    [Fact]
    public async Task ApprovedGraphIsAppliedToEveryThirdPartyNativeFile()
    {
        using var fixture = Fixture.Create();
        var thirdParty = new TestEntry(
            "runtime/third-party.dll",
            "third-party.dll",
            "ffmpeg-runtime",
            "not-approved",
            "native-library");
        fixture.Write(thirdParty.Source, Encoding.ASCII.GetBytes("third-party"));
        fixture.UseEntries(
            fixture.NativeEntry,
            fixture.EvidenceEntry,
            thirdParty);

        AssertIssue(
            "unapproved-native-artifact-owner",
            await fixture.PlanAsync());
    }

    [Fact]
    public async Task InventoryReadFailureIsAClosedComplianceFailure()
    {
        using var fixture = Fixture.Create();
        var planner = new WindowsRuntimeStagingAdmissionPlanner(
            new ThrowingInventoryReader());

        var result = await planner.PlanAsync(
            fixture.Manifest,
            fixture.SourceRoot,
            fixture.ApprovedGraph,
            fixture.RepositoryRoot,
            CancellationToken.None);

        AssertIssue("runtime-staging-source-read-failed", result);
    }

    [Fact]
    public async Task CancellationIsPropagatedAndNeverBecomesAnAdmission()
    {
        using var fixture = Fixture.Create();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.PlanAsync(cancellationToken: cancellation.Token));
    }

    private static void AssertIssue(
        string code,
        WindowsRuntimeStagingAdmissionResult result)
    {
        Assert.False(result.IsAdmitted);
        Assert.Null(result.Plan);
        Assert.Contains(result.Issues, issue => issue.Code == code);
        Assert.Equal(
            result.Issues
                .OrderBy(issue => issue.Code, StringComparer.Ordinal)
                .ThenBy(issue => issue.Subject, StringComparer.Ordinal),
            result.Issues);
    }

    private static bool TryCreateDirectoryLink(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                PlatformNotSupportedException)
        {
            return false;
        }
    }

    private static bool TryCreateFileLink(string link, string target)
    {
        try
        {
            File.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                PlatformNotSupportedException)
        {
            return false;
        }
    }

    private static string Sha256(byte[] bytes) => Convert
        .ToHexString(SHA256.HashData(bytes))
        .ToLowerInvariant();

    private sealed class ThrowingInventoryReader : IStagingInventoryReader
    {
        public Task<StagingInventory> ReadAsync(
            string stagingDirectory,
            CancellationToken cancellationToken) =>
            throw new IOException("synthetic read failure");
    }

    private sealed record TestEntry(
        string Source,
        string Target,
        string Role,
        string ComponentId,
        string DeploymentKind);

    private sealed class Fixture : IDisposable
    {
        private Fixture(string root)
        {
            Root = root;
            RepositoryRoot = Path.Combine(root, "repository");
            SourceRoot = Path.Combine(root, "source");
            Directory.CreateDirectory(RepositoryRoot);
            Directory.CreateDirectory(SourceRoot);
            Binary = Encoding.ASCII.GetBytes(
                "prefix-VRRECORDER_FACTORY_SELECTION_V1:" +
                IntentSha +
                "-suffix");
            NativeEntry = new TestEntry(
                "native/vrrecorder_native.dll",
                "vrrecorder_native.dll",
                "first-party-native",
                "vr-recorder",
                "native-library");
            EvidenceEntry = new TestEntry(
                "evidence/native-factory-selection.json",
                "native-factory-selection.json",
                "factory-selection-evidence",
                "vr-recorder",
                "evidence");
            Write(NativeEntry.Source, Binary);
            WriteEvidence(fullProductionRequired: true);
            UseEntries(NativeEntry, EvidenceEntry);
            ApprovedGraph = new ApprovedReleaseGraph(
                new NormalizedComponentGraph([], []));
        }

        public string Root { get; }

        public string RepositoryRoot { get; }

        public string SourceRoot { get; }

        public byte[] Binary { get; }

        public TestEntry NativeEntry { get; }

        public TestEntry EvidenceEntry { get; }

        public WindowsRuntimeStagingManifest Manifest { get; private set; }
            = null!;

        public ApprovedReleaseGraph ApprovedGraph { get; }

        public static Fixture Create()
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                "vrrecorder-staging-admission-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return new Fixture(root);
        }

        public Task<WindowsRuntimeStagingAdmissionResult> PlanAsync(
            string? sourceRoot = null,
            string? repositoryRoot = null,
            CancellationToken cancellationToken = default) =>
            new WindowsRuntimeStagingAdmissionPlanner().PlanAsync(
                Manifest,
                sourceRoot ?? SourceRoot,
                ApprovedGraph,
                repositoryRoot ?? RepositoryRoot,
                cancellationToken);

        public void UseEntries(params TestEntry[] entries)
        {
            var json = $$"""
                {"schemaVersion":1,"entries":[{{string.Join(',', entries.Select(EntryJson))}}]}
                """;
            Manifest = WindowsRuntimeStagingManifestReader.Read(
                Encoding.UTF8.GetBytes(json));
        }

        public void WriteEvidence(
            bool fullProductionRequired,
            long? declaredBinaryLength = null)
        {
            var json = $$$"""
                {"schemaVersion":1,"evidenceKind":"linked-native-factory-selection","selectionIntentSha256":"{{{IntentSha}}}","fullProductionRequired":{{{fullProductionRequired.ToString().ToLowerInvariant()}}},"nativeBinary":{"file":"vrrecorder_native.dll","length":{{{declaredBinaryLength ?? Binary.LongLength}}},"sha256":"{{{Sha256(Binary)}}}"},"media":{"variant":"PRODUCTION","source":"production_media_backend.cpp"},"encoderProbe":{"variant":"PRODUCTION","source":"production_encoder_probe_backend.cpp"},"spout":{"variant":"PRODUCTION","source":"spout2_source_backend.cpp"},"steamVr":{"variant":"PRODUCTION","source":"openvr_steamvr_input_backend.cpp"}}
                """;
            Write(EvidenceEntry.Source, Encoding.UTF8.GetBytes(json));
        }

        public string Resolve(string relativePath) =>
            WindowsRuntimeRelativePath.Resolve(SourceRoot, relativePath);

        public void Write(string relativePath, byte[] bytes)
        {
            var path = Resolve(relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }

        private string EntryJson(TestEntry entry)
        {
            var sha256 = Sha256(File.ReadAllBytes(Resolve(entry.Source)));
            return $$"""
                {"source":"{{entry.Source}}","target":"{{entry.Target}}","role":"{{entry.Role}}","componentId":"{{entry.ComponentId}}","platform":"windows-x64","deploymentKind":"{{entry.DeploymentKind}}","sha256":"{{sha256}}"}
                """;
        }
    }
}
