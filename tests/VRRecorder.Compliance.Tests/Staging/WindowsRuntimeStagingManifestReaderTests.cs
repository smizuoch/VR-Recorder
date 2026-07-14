using System.Text;
using VRRecorder.Compliance.Staging;

namespace VRRecorder.Compliance.Tests.Staging;

public sealed class WindowsRuntimeStagingManifestReaderTests
{
    private const string ShaA =
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string ShaB =
        "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    [Fact]
    public void ExactManifestIsParsedIntoDeterministicTargetOrder()
    {
        var manifest = Read(Manifest(
            Entry(
                source: "runtime/z.dll",
                target: "native/z.dll",
                role: "ffmpeg-runtime",
                componentId: "ffmpeg",
                deploymentKind: "native-library",
                sha256: ShaB),
            Entry(
                source: "runtime/a.exe",
                target: "tools/a.exe",
                role: "diagnostic-tool",
                componentId: "ffmpeg",
                deploymentKind: "executable",
                sha256: ShaA)));

        Assert.Equal(1, manifest.SchemaVersion);
        Assert.Equal(2, manifest.Entries.Count);
        Assert.Equal("native/z.dll", manifest.Entries[0].Target);
        Assert.Equal("tools/a.exe", manifest.Entries[1].Target);
        Assert.Equal(WindowsRuntimeRole.DiagnosticTool, manifest.Entries[1].Role);
        Assert.Equal(
            WindowsRuntimeDeploymentKind.Executable,
            manifest.Entries[1].DeploymentKind);
    }

    [Theory]
    [InlineData(
        "{\"schemaVersion\":1,\"entries\":[],\"unexpected\":true}")]
    [InlineData(
        "{\"schemaVersion\":1,\"schemaVersion\":1,\"entries\":[]}")]
    [InlineData("{\"schemaVersion\":2,\"entries\":[]}")]
    [InlineData("{\"schemaVersion\":1,\"entries\":[]}")]
    [InlineData("{\"schemaVersion\":1,\"entries\":null}")]
    [InlineData("{\"schemaVersion\":\"1\",\"entries\":[]}")]
    [InlineData("[]")]
    public void InvalidRootShapeIsRejected(string json)
    {
        AssertInvalid(json);
    }

    [Theory]
    [InlineData("../escape.dll")]
    [InlineData("runtime/../escape.dll")]
    [InlineData("runtime/./file.dll")]
    [InlineData("runtime//file.dll")]
    [InlineData("/rooted/file.dll")]
    [InlineData("C:/rooted/file.dll")]
    [InlineData("C:\\rooted\\file.dll")]
    [InlineData("\\\\server\\share\\file.dll")]
    [InlineData("runtime/file.dll:payload")]
    [InlineData("runtime/file.dll.")]
    [InlineData("runtime/file.dll ")]
    [InlineData("runtime/<file>.dll")]
    [InlineData("runtime/file?.dll")]
    [InlineData("runtime/$(Injected).dll")]
    [InlineData("runtime/%(Metadata).dll")]
    [InlineData("runtime/@(Items).dll")]
    [InlineData("runtime/file;other.dll")]
    [InlineData("runtime/CON")]
    [InlineData("runtime/con.txt")]
    [InlineData("runtime/AUX.json")]
    [InlineData("runtime/NUL.dll")]
    [InlineData("runtime/COM1.bin")]
    [InlineData("runtime/LPT9")]
    [InlineData("runtime/COM¹.txt")]
    [InlineData("runtime/LPT³.txt")]
    [InlineData("runtime/CONIN$.txt")]
    [InlineData("runtime/CONOUT$")]
    [InlineData("runtime/COM0.dll")]
    [InlineData("runtime/LPT0.dll")]
    [InlineData("runtime/file\u0001.dll")]
    public void WindowsUnsafeSourcePathIsRejected(string path)
    {
        AssertInvalid(Manifest(Entry(source: path)));
    }

    [Theory]
    [InlineData("../escape.dll")]
    [InlineData("native/../escape.dll")]
    [InlineData("native//file.dll")]
    [InlineData("/rooted/file.dll")]
    [InlineData("D:/rooted/file.dll")]
    [InlineData("native/file.dll:stream")]
    [InlineData("native/PRN.txt")]
    [InlineData("native/COM9.dll")]
    [InlineData("native/file*.dll")]
    [InlineData("native/$(Injected).dll")]
    [InlineData("native/%(Metadata).dll")]
    [InlineData("native/@(Items).dll")]
    [InlineData("native/file;other.dll")]
    public void WindowsUnsafeTargetPathIsRejected(string path)
    {
        AssertInvalid(Manifest(Entry(target: path)));
    }

    [Fact]
    public void WindowsEquivalentSourcePathsAreRejected()
    {
        AssertInvalid(Manifest(
            Entry(source: "runtime/A.dll", target: "native/a.dll"),
            Entry(
                source: "runtime/a.DLL",
                target: "native/b.dll",
                sha256: ShaB)));
    }

    [Fact]
    public void WindowsEquivalentTargetPathsAreRejected()
    {
        AssertInvalid(Manifest(
            Entry(source: "runtime/a.dll", target: "native/A.dll"),
            Entry(
                source: "runtime/b.dll",
                target: "native/a.DLL",
                sha256: ShaB)));
    }

    [Fact]
    public void SourceFileCannotAlsoBeAnotherEntryParent()
    {
        AssertInvalid(Manifest(
            Entry(source: "runtime/native", target: "one.dll"),
            Entry(
                source: "runtime/NATIVE/file.dll",
                target: "two.dll",
                sha256: ShaB)));
    }

    [Fact]
    public void TargetFileCannotAlsoBeAnotherEntryParent()
    {
        AssertInvalid(Manifest(
            Entry(source: "runtime/one.dll", target: "native"),
            Entry(
                source: "runtime/two.dll",
                target: "NATIVE/file.dll",
                sha256: ShaB)));
    }

    [Theory]
    [InlineData("WINDOWS-X64", "native-library", "ffmpeg-runtime", "ffmpeg")]
    [InlineData("windows-arm64", "native-library", "ffmpeg-runtime", "ffmpeg")]
    [InlineData("windows-x64", "library", "ffmpeg-runtime", "ffmpeg")]
    [InlineData("windows-x64", "native-library", "unknown", "ffmpeg")]
    [InlineData("windows-x64", "executable", "ffmpeg-runtime", "ffmpeg")]
    [InlineData("windows-x64", "native-library", "diagnostic-tool", "ffmpeg")]
    [InlineData("windows-x64", "native-library", "ffmpeg-runtime", "FFmpeg")]
    [InlineData("windows-x64", "native-library", "ffmpeg-runtime", "bad/id")]
    public void InvalidEntryIdentityIsRejected(
        string platform,
        string deploymentKind,
        string role,
        string componentId)
    {
        AssertInvalid(Manifest(Entry(
            platform: platform,
            deploymentKind: deploymentKind,
            role: role,
            componentId: componentId)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("aa")]
    [InlineData(
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData(
        "gggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggg")]
    public void Sha256MustBeExactLowercaseHex(string sha256)
    {
        AssertInvalid(Manifest(Entry(sha256: sha256)));
    }

    [Fact]
    public void UnknownOrDuplicateEntryMemberIsRejected()
    {
        var entry = Entry();
        AssertInvalid(Manifest(entry[..^1] + ",\"unexpected\":true}"));
        AssertInvalid(Manifest(entry[..^1] + ",\"source\":\"other.dll\"}"));
    }

    private static WindowsRuntimeStagingManifest Read(string json) =>
        WindowsRuntimeStagingManifestReader.Read(
            Encoding.UTF8.GetBytes(json));

    private static void AssertInvalid(string json) =>
        Assert.Throws<InvalidDataException>(() => Read(json));

    private static string Manifest(params string[] entries) =>
        $$"""
        {"schemaVersion":1,"entries":[{{string.Join(',', entries)}}]}
        """;

    private static string Entry(
        string source = "runtime/avcodec-62.dll",
        string target = "avcodec-62.dll",
        string role = "ffmpeg-runtime",
        string componentId = "ffmpeg",
        string platform = "windows-x64",
        string deploymentKind = "native-library",
        string sha256 = ShaA) =>
        $$"""
        {"source":"{{source}}","target":"{{target}}","role":"{{role}}","componentId":"{{componentId}}","platform":"{{platform}}","deploymentKind":"{{deploymentKind}}","sha256":"{{sha256}}"}
        """;
}
