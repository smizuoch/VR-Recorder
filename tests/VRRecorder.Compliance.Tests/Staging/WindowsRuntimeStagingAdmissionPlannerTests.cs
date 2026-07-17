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
    public async Task ExactFullProductionFixtureProducesImmutableIdentityPlan()
    {
        using var fixture = Fixture.Create();

        var result = await fixture.PlanAsync();

        Assert.True(result.IsAdmitted);
        Assert.Empty(result.Issues);
        var plan = Assert.IsType<AdmittedWindowsRuntimeStagingPlan>(result.Plan);
        Assert.Equal(fixture.Manifest.ManifestSha256, plan.ManifestSha256);
        Assert.Equal(fixture.Manifest.Profile, plan.Profile);
        Assert.Equal(
            fixture.Manifest.RuntimeIdentifier,
            plan.RuntimeIdentifier);
        Assert.Equal(
            fixture.Manifest.LegalBundle.BundleId,
            plan.LegalBundleId);
        Assert.Equal(
            fixture.Manifest.LegalBundle.ManifestSha256,
            plan.LegalManifestSha256);
        Assert.Equal(fixture.SourceRoot, plan.SourceRoot);
        Assert.Equal(fixture.RequiredEntries.Count, plan.Files.Count);
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
    public async Task FullProductionProfileRequiresItsEntireRuntimeClosure()
    {
        using var fixture = Fixture.Create();
        fixture.UseEntries(fixture.NativeEntry, fixture.EvidenceEntry);

        AssertIssue(
            "required-runtime-staging-artifact-missing",
            await fixture.PlanAsync());
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
    public async Task DeclaredLengthMustMatchTheScannedFile()
    {
        using var fixture = Fixture.Create();
        fixture.UseEntries(
            fixture.NativeEntry with
            {
                DeclaredLength = fixture.Binary.LongLength + 1,
            },
            fixture.EvidenceEntry);

        AssertIssue("staging-file-length-mismatch", await fixture.PlanAsync());
    }

    [Fact]
    public async Task DeploymentKindMustMatchScannedFileKind()
    {
        using var fixture = Fixture.Create();
        var disguised = new WindowsRuntimeStagingTestEntry(
            "assets/disguised.dll",
            "assets/disguised.dll",
            "application-asset",
            "vr-recorder",
            "asset");
        fixture.Write(disguised.Source, Encoding.UTF8.GetBytes("not-a-pe"));
        fixture.UseEntries(
            [.. fixture.RequiredEntries,
             disguised]);

        AssertIssue("staging-file-kind-mismatch", await fixture.PlanAsync());
    }

    [Fact]
    public async Task NativeLibraryMustBeAStructurallyValidAmd64PeImage()
    {
        using var fixture = Fixture.Create();
        var malformed = Encoding.ASCII.GetBytes(
            "prefix-VRRECORDER_FACTORY_SELECTION_V1:" +
            IntentSha +
            "-suffix");
        fixture.Write(fixture.NativeEntry.Source, malformed);
        fixture.WriteEvidence(
            fullProductionRequired: true,
            binary: malformed);
        fixture.UseEntries(
            fixture.RequiredEntries.ToArray());

        AssertIssue("invalid-windows-pe-image", await fixture.PlanAsync());
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
        var duplicate = new WindowsRuntimeStagingTestEntry(
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

        using var renamedEvidence = Fixture.Create();
        renamedEvidence.UseEntries(
            renamedEvidence.NativeEntry,
            renamedEvidence.EvidenceEntry with { Target = "renamed.json" });
        AssertIssue(
            "invalid-native-factory-staging-pair",
            await renamedEvidence.PlanAsync());
    }

    [Fact]
    public async Task FactoryEvidenceMustBindProductionFamiliesAndActualDllIdentity()
    {
        using var invalidFamily = Fixture.Create();
        invalidFamily.WriteEvidence(fullProductionRequired: false);
        invalidFamily.UseEntries(
            invalidFamily.RequiredEntries.ToArray());
        AssertIssue(
            "invalid-native-factory-selection-evidence",
            await invalidFamily.PlanAsync());

        using var wrongLength = Fixture.Create();
        wrongLength.WriteEvidence(
            fullProductionRequired: true,
            declaredBinaryLength: wrongLength.Binary.LongLength + 1);
        wrongLength.UseEntries(
            wrongLength.RequiredEntries.ToArray());
        AssertIssue(
            "native-factory-binary-identity-mismatch",
            await wrongLength.PlanAsync());
    }

    [Fact]
    public async Task ApprovedGraphIsAppliedToEveryThirdPartyNativeFile()
    {
        using var fixture = Fixture.Create();
        var thirdParty = new WindowsRuntimeStagingTestEntry(
            "runtime/third-party.dll",
            "third-party.dll",
            "ffmpeg-runtime",
            "not-approved",
            "native-library");
        fixture.Write(thirdParty.Source, Encoding.ASCII.GetBytes("third-party"));
        fixture.UseEntries(
            [.. fixture.RequiredEntries,
             thirdParty]);

        AssertIssue(
            "unapproved-native-artifact-owner",
            await fixture.PlanAsync());
    }

    [Fact]
    public async Task ApprovedGraphIsAppliedToEveryThirdPartyAsset()
    {
        using var fixture = Fixture.Create();
        var binding = new WindowsRuntimeStagingTestEntry(
            "openvr/rogue.json",
            "OpenVr/bindings/rogue.json",
            "openvr-binding",
            "not-approved",
            "asset");
        fixture.Write(binding.Source, "{}"u8.ToArray());
        fixture.UseEntries(
            [.. fixture.RequiredEntries,
             binding]);

        AssertIssue(
            "unapproved-runtime-staging-owner",
            await fixture.PlanAsync());
    }

    [Fact]
    public async Task BuildOnlyOwnerCannotStageRuntimeAsset()
    {
        using var fixture = Fixture.Create();
        fixture.UseApprovedComponents(
            Component("openvr", NoticeScope.BuildOnly));

        AssertIssue(
            "staged-component-scope-mismatch",
            await fixture.PlanAsync());
    }

    [Fact]
    public async Task RuntimeAssetOwnerCanStageItsExactRegisteredAsset()
    {
        using var fixture = Fixture.Create();
        fixture.UseApprovedComponents(
            Component("openvr", NoticeScope.RuntimeAsset));

        var result = await fixture.PlanAsync();

        Assert.True(result.IsAdmitted);
        Assert.Empty(result.Issues);
        Assert.NotNull(result.Plan);
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

    private static NormalizedComponent Component(
        string id,
        NoticeScope scope) => new(
        id,
        id,
        "1.0.0",
        new LicenseDecision("MIT", "MIT"),
        "Copyright Example",
        "runtime",
        "runtime",
        Modified: false,
        "https://example.invalid/source@commit",
        "MIT license",
        LegalFiles: [],
        scope,
        new LegalApproval(
            LegalApprovalStatus.Approved,
            "LEGAL-TEST",
            "requester",
            "reviewer"),
        Packages: []);

    private sealed class ThrowingInventoryReader : IStagingInventoryReader
    {
        public Task<StagingInventory> ReadAsync(
            string stagingDirectory,
            CancellationToken cancellationToken) =>
            throw new IOException("synthetic read failure");
    }

    private sealed class Fixture : IDisposable
    {
        private Fixture(string root)
        {
            Root = root;
            RepositoryRoot = Path.Combine(root, "repository");
            SourceRoot = Path.Combine(root, "source");
            Directory.CreateDirectory(RepositoryRoot);
            Directory.CreateDirectory(SourceRoot);
            var data = FullProductionStagingTestData.Create(
                SourceRoot,
                RepositoryRoot,
                IntentSha);
            Binary = data.NativeBinary;
            NativeEntry = data.NativeEntry;
            EvidenceEntry = data.EvidenceEntry;
            RequiredEntries = data.Entries;
            BaseComponents = data.ApprovedComponents;
            UseEntries(RequiredEntries.ToArray());
            ApprovedGraph = new ApprovedReleaseGraph(
                new NormalizedComponentGraph([], BaseComponents));
        }

        public string Root { get; }

        public string RepositoryRoot { get; }

        public string SourceRoot { get; }

        public byte[] Binary { get; }

        public WindowsRuntimeStagingTestEntry NativeEntry { get; }

        public WindowsRuntimeStagingTestEntry EvidenceEntry { get; }

        public IReadOnlyList<WindowsRuntimeStagingTestEntry> RequiredEntries
        {
            get;
        }

        private IReadOnlyList<NormalizedComponent> BaseComponents { get; }

        public WindowsRuntimeStagingManifest Manifest { get; private set; }
            = null!;

        public ApprovedReleaseGraph ApprovedGraph { get; private set; }

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

        public void UseEntries(params WindowsRuntimeStagingTestEntry[] entries)
        {
            var json = FullProductionStagingTestData.ManifestJson(
                SourceRoot,
                entries);
            Manifest = WindowsRuntimeStagingManifestReader.Read(
                Encoding.UTF8.GetBytes(json));
        }

        public void UseApprovedComponents(params NormalizedComponent[] components)
        {
            var overrides = components
                .ToDictionary(component => component.Id, StringComparer.Ordinal);
            var merged = BaseComponents
                .Where(component => !overrides.ContainsKey(component.Id))
                .Concat(components)
                .ToArray();
            ApprovedGraph = new ApprovedReleaseGraph(
                new NormalizedComponentGraph([], merged));
        }

        public void WriteEvidence(
            bool fullProductionRequired,
            long? declaredBinaryLength = null,
            byte[]? binary = null)
        {
            binary ??= Binary;
            var json = $$$"""
                {"schemaVersion":1,"evidenceKind":"linked-native-factory-selection","selectionIntentSha256":"{{{IntentSha}}}","fullProductionRequired":{{{fullProductionRequired.ToString().ToLowerInvariant()}}},"nativeBinary":{"file":"vrrecorder_native.dll","length":{{{declaredBinaryLength ?? binary.LongLength}}},"sha256":"{{{Sha256(binary)}}}"},"media":{"variant":"PRODUCTION","source":"production_media_backend.cpp"},"encoderProbe":{"variant":"PRODUCTION","source":"production_encoder_probe_backend.cpp"},"spout":{"variant":"PRODUCTION","source":"spout2_source_backend.cpp"},"steamVr":{"variant":"PRODUCTION","source":"openvr_steamvr_input_backend.cpp"}}
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

    }
}
