using System.Text.Json;
using VRRecorder.Infrastructure.SteamVr;

namespace VRRecorder.IntegrationTests.SteamVr;

public sealed class OpenVrApplicationManifestContractTests
{
    [Fact]
    public void ResolvesValidatedCurrentInstallIdentity()
    {
        using var install = TestInstall.Create(ValidManifest);

        var resolved = OpenVrApplicationManifest.ResolveAndValidate(
            install.Path);

        Assert.Equal("com.vrrecorder.desktop", resolved.AppKey);
        Assert.Equal(
            Path.Combine(install.Path, "OpenVr", "steamvr.vrmanifest"),
            resolved.ManifestPath);
        Assert.Equal(
            Path.Combine(install.Path, "VRRecorder.App.exe"),
            resolved.ExecutablePath);
        Assert.Equal(
            Path.Combine(install.Path, "OpenVr", "actions.json"),
            resolved.ActionManifestPath);
    }

    [Theory]
    [InlineData("app_key", "com.vrrecorder.old")]
    [InlineData("binary_path_windows", "../../stale/VRRecorder.App.exe")]
    [InlineData("action_manifest_path", "../../stale/actions.json")]
    public void RejectsIdentityOrPathOutsideTheCurrentInstallContract(
        string propertyName,
        string replacement)
    {
        var manifest = ValidManifest.Replace(
            $"\"{propertyName}\": \"{ExpectedValues[propertyName]}\"",
            $"\"{propertyName}\": \"{replacement}\"",
            StringComparison.Ordinal);
        using var install = TestInstall.Create(manifest);

        Assert.Throws<InvalidDataException>(() =>
            OpenVrApplicationManifest.ResolveAndValidate(install.Path));
    }

    [Fact]
    public void RejectsRegistrationWhenCurrentExecutableIsMissing()
    {
        using var install = TestInstall.Create(ValidManifest);
        File.Delete(Path.Combine(install.Path, "VRRecorder.App.exe"));

        var exception = Assert.Throws<FileNotFoundException>(() =>
            OpenVrApplicationManifest.ResolveAndValidate(install.Path));

        Assert.Equal(
            Path.Combine(install.Path, "VRRecorder.App.exe"),
            exception.FileName);
    }

    [Fact]
    public void RejectsRegistrationWhenCurrentActionManifestIsMissing()
    {
        using var install = TestInstall.Create(ValidManifest);
        File.Delete(Path.Combine(install.Path, "OpenVr", "actions.json"));

        var exception = Assert.Throws<FileNotFoundException>(() =>
            OpenVrApplicationManifest.ResolveAndValidate(install.Path));

        Assert.Equal(
            Path.Combine(install.Path, "OpenVr", "actions.json"),
            exception.FileName);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("[]")]
    public void RejectsMalformedOrNonObjectManifest(string manifest)
    {
        using var install = TestInstall.Create(manifest);

        Assert.Throws<InvalidDataException>(() =>
            OpenVrApplicationManifest.ResolveAndValidate(install.Path));
    }

    [Fact]
    public void RejectsUnknownRootProperty()
    {
        var manifest = ValidManifest.Replace(
            "\"source\": \"vrrecorder\"",
            "\"source\": \"vrrecorder\", \"unexpected\": true",
            StringComparison.Ordinal);
        using var install = TestInstall.Create(manifest);

        Assert.Throws<InvalidDataException>(() =>
            OpenVrApplicationManifest.ResolveAndValidate(install.Path));
    }

    [Fact]
    public void RejectsMultipleApplicationEntries()
    {
        var manifest = ValidManifest.Replace(
            "\"applications\": [",
            "\"applications\": [{},",
            StringComparison.Ordinal);
        using var install = TestInstall.Create(manifest);

        Assert.Throws<InvalidDataException>(() =>
            OpenVrApplicationManifest.ResolveAndValidate(install.Path));
    }

    [Fact]
    public void BuildOutputIncludesApplicationManifest()
    {
        Assert.True(File.Exists(Path.Combine(
            AppContext.BaseDirectory,
            "OpenVr",
            "steamvr.vrmanifest")));
    }

    [Fact]
    public void RepositoryManifestMatchesTheValidatedDistributionContract()
    {
        var repositoryRoot = FindRepositoryRoot();
        var openVrDirectory = Path.Combine(
            repositoryRoot,
            "src",
            "VRRecorder.Infrastructure.SteamVr",
            "OpenVr");
        var manifestPath = Path.Combine(
            openVrDirectory,
            "steamvr.vrmanifest");

        using var document = JsonDocument.Parse(
            File.ReadAllBytes(manifestPath));
        var application = Assert.Single(
            document.RootElement.GetProperty("applications").EnumerateArray());

        Assert.Equal(
            OpenVrApplicationManifest.StableAppKey,
            application.GetProperty("app_key").GetString());
        Assert.Equal(
            "../VRRecorder.App.exe",
            application.GetProperty("binary_path_windows").GetString());
        Assert.Equal(
            "actions.json",
            application.GetProperty("action_manifest_path").GetString());
        Assert.False(application.GetProperty("is_dashboard_overlay").GetBoolean());

        var strings = application.GetProperty("strings");
        Assert.Equal(
            ["en_us", "ja_jp"],
            strings.EnumerateObject()
                .Select(property => property.Name)
                .Order(StringComparer.Ordinal));
        Assert.All(strings.EnumerateObject(), localization =>
        {
            Assert.False(string.IsNullOrWhiteSpace(
                localization.Value.GetProperty("name").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(
                localization.Value.GetProperty("description").GetString()));
        });
    }

    private static readonly Dictionary<string, string> ExpectedValues =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["app_key"] = "com.vrrecorder.desktop",
            ["binary_path_windows"] = "../VRRecorder.App.exe",
            ["action_manifest_path"] = "actions.json",
        };

    private const string ValidManifest = """
        {
          "source": "vrrecorder",
          "applications": [
            {
              "app_key": "com.vrrecorder.desktop",
              "launch_type": "binary",
              "binary_path_windows": "../VRRecorder.App.exe",
              "action_manifest_path": "actions.json",
              "is_dashboard_overlay": false,
              "strings": {
                "en_us": {
                  "name": "VR Recorder",
                  "description": "Record VR sessions safely."
                },
                "ja_jp": {
                  "name": "VR Recorder",
                  "description": "VRセッションを安全に録画します。"
                }
              }
            }
          ]
        }
        """;

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "VR-Recorder.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }

    private sealed class TestInstall : IDisposable
    {
        private TestInstall(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TestInstall Create(string manifest)
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"vr-recorder-openvr-app-{Guid.NewGuid():N}");
            var openVrPath = System.IO.Path.Combine(path, "OpenVr");
            Directory.CreateDirectory(openVrPath);
            File.WriteAllText(
                System.IO.Path.Combine(openVrPath, "steamvr.vrmanifest"),
                manifest);
            File.WriteAllText(
                System.IO.Path.Combine(openVrPath, "actions.json"),
                "{}");
            File.WriteAllBytes(
                System.IO.Path.Combine(path, "VRRecorder.App.exe"),
                [0x4d, 0x5a]);
            return new TestInstall(path);
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
