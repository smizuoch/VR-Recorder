using VRRecorder.Compliance.Distribution;
using VRRecorder.Compliance.Tests.Staging;

namespace VRRecorder.Compliance.Tests.Distribution;

public sealed class WindowsValidatedPayloadDirectoryVerifierTests
{
    [Fact]
    public async Task ExactPayloadDirectoryMatchesTheSealedIdentity()
    {
        using var fixture = await Fixture.CreateAsync();

        var result = await WindowsValidatedPayloadDirectoryVerifier
            .VerifyAsync(
                fixture.PayloadRoot,
                fixture.Identity,
                CancellationToken.None);

        Assert.True(result.IsVerified);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public async Task ChangedOrAdditionalPayloadBytesAreRejected()
    {
        using var changed = await Fixture.CreateAsync();
        await File.AppendAllTextAsync(changed.AssetPath, "changed");
        AssertIssue(
            "validated-payload-inventory-mismatch",
            await WindowsValidatedPayloadDirectoryVerifier.VerifyAsync(
                changed.PayloadRoot,
                changed.Identity,
                CancellationToken.None));

        using var additional = await Fixture.CreateAsync();
        await File.WriteAllTextAsync(
            Path.Combine(additional.PayloadRoot, "ambient.txt"),
            "ambient");
        AssertIssue(
            "validated-payload-inventory-mismatch",
            await WindowsValidatedPayloadDirectoryVerifier.VerifyAsync(
                additional.PayloadRoot,
                additional.Identity,
                CancellationToken.None));
    }

    [Fact]
    public async Task DifferentEntrypointBytesAreRejectedExplicitly()
    {
        using var fixture = await Fixture.CreateAsync();
        File.WriteAllBytes(
            fixture.EntryPointPath,
            WindowsPeImageTestData.Create(
                isDll: false,
                subsystem: 2,
                imports: ["different.dll"]));

        var result = await WindowsValidatedPayloadDirectoryVerifier
            .VerifyAsync(
                fixture.PayloadRoot,
                fixture.Identity,
                CancellationToken.None);

        AssertIssue("validated-payload-executable-mismatch", result);
    }

    private static void AssertIssue(
        string code,
        WindowsValidatedPayloadDirectoryVerification result)
    {
        Assert.False(result.IsVerified);
        Assert.Contains(result.Issues, issue => issue.Code == code);
    }

    private sealed class Fixture : IDisposable
    {
        private Fixture(
            string payloadRoot,
            string entryPointPath,
            string assetPath,
            WindowsApplicationPayloadIdentityDocument identity)
        {
            PayloadRoot = payloadRoot;
            EntryPointPath = entryPointPath;
            AssetPath = assetPath;
            Identity = identity;
        }

        public string PayloadRoot { get; }

        public string EntryPointPath { get; }

        public string AssetPath { get; }

        public WindowsApplicationPayloadIdentityDocument Identity { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var root = Path.GetFullPath(Path.Combine(
                Path.GetTempPath(),
                "vrrecorder-validated-payload-verifier-tests",
                Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(root);
            var entryPoint = Path.Combine(root, "VRRecorder.App.exe");
            File.WriteAllBytes(
                entryPoint,
                WindowsPeImageTestData.Create(
                    isDll: false,
                    subsystem: 2,
                    imports: []));
            var asset = Path.Combine(root, "settings.json");
            await File.WriteAllTextAsync(asset, "{}\n");

            var admission = await new WindowsPublishDirectoryInventoryReader()
                .ReadAsync(root, "VRRecorder.App.exe", CancellationToken.None);
            var inventory = Assert.IsType<WindowsPublishDirectoryInventory>(
                admission.Inventory);
            var identity = new WindowsApplicationPayloadIdentityDocument(
                SchemaVersion: 1,
                new ValidatedPayloadIdentity(
                    "0.1.0",
                    "0123456789abcdef0123456789abcdef01234567",
                    "win-x64",
                    inventory.EntryPointSha256,
                    inventory.InventorySha256,
                    "urn:vr-recorder:legal:0.1.0:test",
                    new string('f', 64)),
                inventory.EntryPoint,
                inventory.Files,
                new string('a', 64));
            return new Fixture(root, entryPoint, asset, identity);
        }

        public void Dispose()
        {
            if (Directory.Exists(PayloadRoot))
            {
                Directory.Delete(PayloadRoot, recursive: true);
            }
        }
    }
}
