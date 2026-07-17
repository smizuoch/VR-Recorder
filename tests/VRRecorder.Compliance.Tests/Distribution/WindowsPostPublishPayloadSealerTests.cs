using System.Security.Cryptography;
using VRRecorder.Compliance.Distribution;
using VRRecorder.Compliance.Runtime;
using VRRecorder.Compliance.Staging;
using VRRecorder.Compliance.Tests.Staging;

namespace VRRecorder.Compliance.Tests.Distribution;

public sealed class WindowsPostPublishPayloadSealerTests
{
    [Fact]
    public async Task ExactStagedRuntimeAndAuthenticatedLegalProduceIdentity()
    {
        using var fixture = Fixture.Create();
        AuthenticatedLegalBundleAnchor? observedAnchor = null;
        var verifier = new CallbackLegalVerifier((_, anchor) =>
        {
            observedAnchor = anchor;
            return new LegalBundleVerification.Verified(
                new LegalBundleIdentity(
                    anchor.BundleId,
                    anchor.ManifestSha256,
                    Fixture.ProductVersion));
        });

        var result = await new WindowsPostPublishPayloadSealer(verifier)
            .SealAsync(
                fixture.PublishRoot,
                fixture.ApprovedPropsPath,
                CancellationToken.None);

        Assert.True(result.IsSealed);
        Assert.Empty(result.Issues);
        var payload = Assert.IsType<SealedWindowsApplicationPayload>(
            result.Payload);
        Assert.Equal(fixture.PublishRoot, payload.RootDirectory);
        Assert.Equal("VRRecorder.App.exe", payload.EntryPoint);
        Assert.Equal("win-x64", payload.RuntimeIdentifier);
        Assert.Equal(Fixture.ProductVersion, payload.ProductVersion);
        Assert.Equal(Fixture.SourceRevision, payload.SourceRevision);
        Assert.Matches("^[0-9a-f]{64}$", payload.EntryPointSha256);
        Assert.Matches("^[0-9a-f]{64}$", payload.InventorySha256);
        Assert.Equal(Fixture.LegalBundleId, payload.LegalBundleId);
        Assert.Equal(Fixture.LegalManifestSha, payload.LegalManifestSha256);
        Assert.Equal(3, payload.Files.Count);
        Assert.NotNull(observedAnchor);
        Assert.Equal(Fixture.LegalBundleId, observedAnchor.BundleId);
        Assert.Equal(
            Fixture.LegalManifestSha,
            observedAnchor.ManifestSha256);
    }

    [Fact]
    public async Task ManualRuntimeCopyOrMissingRuntimeIsRejected()
    {
        using var changed = Fixture.Create();
        File.AppendAllText(
            Path.Combine(changed.PublishRoot, "native.dll"),
            "changed");
        AssertIssue(
            "published-runtime-file-mismatch",
            await changed.SealAsync());

        using var missing = Fixture.Create();
        File.Delete(Path.Combine(missing.PublishRoot, "native.dll"));
        AssertIssue(
            "published-runtime-file-missing",
            await missing.SealAsync());
    }

    [Fact]
    public async Task PropsMustRemainInItsExactImmutableDigestDirectory()
    {
        using var fixture = Fixture.Create();
        var wrongDirectory = Path.Combine(
            fixture.Root,
            "windows-runtime-" + new string('a', 64));
        Directory.CreateDirectory(wrongDirectory);
        var wrongProps = Path.Combine(
            wrongDirectory,
            "ApprovedWindowsRuntime.props");
        File.Copy(fixture.ApprovedPropsPath, wrongProps);

        AssertIssue(
            "approved-runtime-props-directory-mismatch",
            await new WindowsPostPublishPayloadSealer(
                    new CallbackLegalVerifier((_, anchor) =>
                        new LegalBundleVerification.Verified(
                            new LegalBundleIdentity(
                                anchor.BundleId,
                                anchor.ManifestSha256,
                                Fixture.ProductVersion))))
                .SealAsync(
                    fixture.PublishRoot,
                    wrongProps,
                    CancellationToken.None));
    }

    [Fact]
    public async Task LegalRejectionPreventsPayloadIdentity()
    {
        using var fixture = Fixture.Create();
        var result = await fixture.SealAsync(
            new LegalBundleVerification.Rejected(
                [new ComplianceIssue("legal-bundle-missing", "root")]));

        AssertIssue("legal-bundle-missing", result);
    }

    [Fact]
    public async Task UnexpectedStagedRuntimeIsRejected()
    {
        using var fixture = Fixture.Create();
        File.WriteAllBytes(
            Path.Combine(fixture.StagingPayload, "ambient.dll"),
            WindowsPeImageTestData.Create(
                isDll: true,
                subsystem: 2,
                imports: ["KERNEL32.dll"]));

        AssertIssue(
            "approved-runtime-payload-inventory-mismatch",
            await fixture.SealAsync());
    }

    [Fact]
    public async Task StagedRuntimeMutationIsRejectedEvenWhenPublishMatches()
    {
        using var fixture = Fixture.Create();
        var changedBytes = WindowsPeImageTestData.Create(
            isDll: true,
            subsystem: 2,
            imports: ["USER32.dll"]);
        File.WriteAllBytes(
            Path.Combine(fixture.StagingPayload, "native.dll"),
            changedBytes);
        File.WriteAllBytes(
            Path.Combine(fixture.PublishRoot, "native.dll"),
            changedBytes);

        AssertIssue(
            "approved-runtime-file-mismatch",
            await fixture.SealAsync());
    }

    [Fact]
    public async Task VerifiedLegalIdentityMustMatchPropsAnchor()
    {
        using var fixture = Fixture.Create();
        var result = await fixture.SealAsync(
            new LegalBundleVerification.Verified(
                new LegalBundleIdentity(
                    "https://example.invalid/spdx/substituted",
                    Fixture.LegalManifestSha,
                    Fixture.ProductVersion)));

        AssertIssue("legal-bundle-identity-mismatch", result);
    }

    [Fact]
    public async Task LegalProductVersionMustMatchManagedBuildIdentity()
    {
        using var fixture = Fixture.Create();
        var result = await fixture.SealAsync(
            new LegalBundleVerification.Verified(
                new LegalBundleIdentity(
                    Fixture.LegalBundleId,
                    Fixture.LegalManifestSha,
                    "0.1.1")));

        AssertIssue("application-product-version-legal-mismatch", result);
    }

    [Fact]
    public async Task MissingManagedApplicationAssemblyPreventsIdentity()
    {
        using var fixture = Fixture.Create();
        File.Delete(Path.Combine(fixture.PublishRoot, "VRRecorder.App.dll"));

        AssertIssue(
            "application-build-identity-assembly-missing",
            await fixture.SealAsync());
    }

    [Fact]
    public async Task ModifiedApprovedPropsAreRejected()
    {
        using var fixture = Fixture.Create();
        var text = File.ReadAllText(fixture.ApprovedPropsPath);
        File.WriteAllText(
            fixture.ApprovedPropsPath,
            text.Replace(
                "full-production-hardware-validation-v1",
                "substituted-profile",
                StringComparison.Ordinal));

        AssertIssue(
            "invalid-approved-runtime-props",
            await fixture.SealAsync());
    }

    private static void AssertIssue(
        string code,
        WindowsPostPublishPayloadSealResult result)
    {
        Assert.False(result.IsSealed);
        Assert.Null(result.Payload);
        Assert.Contains(result.Issues, issue => issue.Code == code);
    }

    private sealed class CallbackLegalVerifier(
        Func<string, AuthenticatedLegalBundleAnchor, LegalBundleVerification>
            callback)
        : IWindowsPublishLegalBundleVerifier
    {
        public Task<LegalBundleVerification> VerifyAsync(
            string publishRoot,
            AuthenticatedLegalBundleAnchor anchor,
            CancellationToken cancellationToken) =>
            Task.FromResult(callback(publishRoot, anchor));
    }

    private sealed class Fixture : IDisposable
    {
        public const string ProductVersion = "0.1.0";
        public const string SourceRevision =
            "0123456789abcdef0123456789abcdef01234567";
        public const string LegalBundleId =
            "https://example.invalid/spdx/vr-recorder-test";
        public const string LegalManifestSha =
            "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";
        private const string RuntimeInventorySha =
            "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

        private Fixture(string root)
        {
            Root = root;
            PublishRoot = Path.Combine(root, "publish");
            Directory.CreateDirectory(PublishRoot);
            var runtimeBytes = WindowsPeImageTestData.Create(
                isDll: true,
                subsystem: 2,
                imports: ["KERNEL32.dll"]);
            File.WriteAllBytes(
                Path.Combine(PublishRoot, "VRRecorder.App.exe"),
                WindowsPeImageTestData.Create(
                    isDll: false,
                    subsystem: 2,
                    imports: ["KERNEL32.dll", "native.dll"]));
            File.WriteAllBytes(
                Path.Combine(PublishRoot, "native.dll"),
                runtimeBytes);
            File.Copy(
                typeof(WindowsPostPublishPayloadSealerTests)
                    .Assembly
                    .Location,
                Path.Combine(PublishRoot, "VRRecorder.App.dll"));

            var stagingRoot = Path.Combine(
                root,
                "windows-runtime-" + RuntimeInventorySha);
            var stagingPayload = Path.Combine(stagingRoot, "payload");
            Directory.CreateDirectory(stagingPayload);
            StagingPayload = stagingPayload;
            File.WriteAllBytes(
                Path.Combine(stagingPayload, "native.dll"),
                runtimeBytes);
            var manifest = new WindowsRuntimeStagingManifest(
                2,
                new string('a', 64),
                "full-production-hardware-validation-v1",
                "win-x64",
                new WindowsRuntimeLegalBundleAnchor(
                    LegalBundleId,
                    LegalManifestSha),
                [new WindowsRuntimeStagingEntry(
                    "input/native.dll",
                    "native.dll",
                    WindowsRuntimeRole.FirstPartyNative,
                    "vr-recorder",
                    "windows-x64",
                    WindowsRuntimeDeploymentKind.NativeLibrary,
                    Sha256(runtimeBytes),
                    runtimeBytes.LongLength)]);
            ApprovedPropsPath = Path.Combine(
                stagingRoot,
                "ApprovedWindowsRuntime.props");
            File.WriteAllBytes(
                ApprovedPropsPath,
                ApprovedWindowsRuntimePropsGenerator.Generate(
                    manifest,
                    RuntimeInventorySha));
        }

        public string Root { get; }

        public string PublishRoot { get; }

        public string ApprovedPropsPath { get; }

        public string StagingPayload { get; }

        public static Fixture Create()
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                "vrrecorder-post-publish-sealer-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return new Fixture(Path.GetFullPath(root));
        }

        public Task<WindowsPostPublishPayloadSealResult> SealAsync(
            LegalBundleVerification? legalVerification = null)
        {
            var verification = legalVerification ??
                new LegalBundleVerification.Verified(
                    new LegalBundleIdentity(
                        LegalBundleId,
                        LegalManifestSha,
                        ProductVersion));
            return new WindowsPostPublishPayloadSealer(
                    new CallbackLegalVerifier((_, _) => verification))
                .SealAsync(
                    PublishRoot,
                    ApprovedPropsPath,
                    CancellationToken.None);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }

        private static string Sha256(byte[] bytes) => Convert
            .ToHexString(SHA256.HashData(bytes))
            .ToLowerInvariant();
    }
}
