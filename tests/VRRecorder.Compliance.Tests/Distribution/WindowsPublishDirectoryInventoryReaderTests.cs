using System.Security.Cryptography;
using VRRecorder.Compliance.Distribution;
using VRRecorder.Compliance.Staging;
using VRRecorder.Compliance.Tests.Staging;

namespace VRRecorder.Compliance.Tests.Distribution;

public sealed class WindowsPublishDirectoryInventoryReaderTests
{
    [Fact]
    public async Task ExactDirectoryProducesDeterministicCanonicalInventory()
    {
        using var fixture = Fixture.Create();
        var reader = new WindowsPublishDirectoryInventoryReader();

        var first = await reader.ReadAsync(
            fixture.Root,
            "VRRecorder.App.exe",
            CancellationToken.None);
        var second = await reader.ReadAsync(
            fixture.Root,
            "VRRecorder.App.exe",
            CancellationToken.None);

        Assert.True(first.IsAdmitted);
        Assert.Empty(first.Issues);
        var inventory = Assert.IsType<WindowsPublishDirectoryInventory>(
            first.Inventory);
        Assert.Equal(fixture.Root, inventory.RootDirectory);
        Assert.Equal("VRRecorder.App.exe", inventory.EntryPoint);
        Assert.Equal(3, inventory.Files.Count);
        Assert.Equal(
            ["VRRecorder.App.exe", "assets/settings.json", "native.dll"],
            inventory.Files.Select(file => file.RelativePath));
        Assert.Equal(
            Sha256(File.ReadAllBytes(fixture.EntryPointPath)),
            inventory.EntryPointSha256);
        Assert.Matches("^[0-9a-f]{64}$", inventory.InventorySha256);
        Assert.Equal(
            inventory.InventorySha256,
            second.Inventory?.InventorySha256);

        File.AppendAllText(fixture.AssetPath, "changed");
        var changed = await reader.ReadAsync(
            fixture.Root,
            "VRRecorder.App.exe",
            CancellationToken.None);
        Assert.NotEqual(
            inventory.InventorySha256,
            changed.Inventory?.InventorySha256);
    }

    [Fact]
    public async Task RootAndEntrypointMustBeCanonicalAndContained()
    {
        using var fixture = Fixture.Create();
        var reader = new WindowsPublishDirectoryInventoryReader();

        AssertIssue(
            "invalid-publish-directory-root",
            await reader.ReadAsync(
                ".",
                "VRRecorder.App.exe",
                CancellationToken.None));
        AssertIssue(
            "invalid-publish-entrypoint",
            await reader.ReadAsync(
                fixture.Root,
                "../VRRecorder.App.exe",
                CancellationToken.None));
        AssertIssue(
            "publish-entrypoint-missing",
            await reader.ReadAsync(
                fixture.Root,
                "missing.exe",
                CancellationToken.None));
    }

    [Fact]
    public async Task EntrypointMustBeAnActualAmd64PeExecutable()
    {
        using var fixture = Fixture.Create();
        File.WriteAllText(fixture.EntryPointPath, "not a PE image");

        AssertIssue(
            "invalid-publish-entrypoint-pe",
            await new WindowsPublishDirectoryInventoryReader().ReadAsync(
                fixture.Root,
                "VRRecorder.App.exe",
                CancellationToken.None));
    }

    [Fact]
    public async Task LinkedFileAndWindowsEquivalentPathsAreRejected()
    {
        using var fixture = Fixture.Create();
        var link = Path.Combine(fixture.Root, "linked.json");
        try
        {
            File.CreateSymbolicLink(link, fixture.AssetPath);
            AssertIssue(
                "staging-link-not-allowed",
                await new WindowsPublishDirectoryInventoryReader().ReadAsync(
                    fixture.Root,
                    "VRRecorder.App.exe",
                    CancellationToken.None));
        }
        catch (Exception exception) when (exception is
            IOException or UnauthorizedAccessException or
            PlatformNotSupportedException)
        {
        }

        if (!OperatingSystem.IsWindows())
        {
            File.WriteAllText(Path.Combine(fixture.Root, "Case.txt"), "one");
            File.WriteAllText(Path.Combine(fixture.Root, "case.txt"), "two");
            AssertIssue(
                "duplicate-publish-inventory-path",
                await new WindowsPublishDirectoryInventoryReader().ReadAsync(
                    fixture.Root,
                    "VRRecorder.App.exe",
                    CancellationToken.None));
        }
    }

    private static void AssertIssue(
        string code,
        WindowsPublishDirectoryAdmission result)
    {
        Assert.False(result.IsAdmitted);
        Assert.Null(result.Inventory);
        Assert.Contains(result.Issues, issue => issue.Code == code);
    }

    private static string Sha256(byte[] bytes) => Convert
        .ToHexString(SHA256.HashData(bytes))
        .ToLowerInvariant();

    private sealed class Fixture : IDisposable
    {
        private Fixture(string root)
        {
            Root = root;
            EntryPointPath = Path.Combine(root, "VRRecorder.App.exe");
            AssetPath = Path.Combine(root, "assets", "settings.json");
            Directory.CreateDirectory(Path.GetDirectoryName(AssetPath)!);
            File.WriteAllBytes(
                EntryPointPath,
                WindowsPeImageTestData.Create(
                    isDll: false,
                    subsystem: 2,
                    imports: ["KERNEL32.dll"]));
            File.WriteAllBytes(
                Path.Combine(root, "native.dll"),
                WindowsPeImageTestData.Create(
                    isDll: true,
                    subsystem: 2,
                    imports: ["KERNEL32.dll"]));
            File.WriteAllText(AssetPath, "{}");
        }

        public string Root { get; }

        public string EntryPointPath { get; }

        public string AssetPath { get; }

        public static Fixture Create()
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                "vrrecorder-publish-inventory-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return new Fixture(Path.GetFullPath(root));
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
