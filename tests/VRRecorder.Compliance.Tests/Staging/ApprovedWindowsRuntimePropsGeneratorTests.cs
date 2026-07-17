using System.Text;
using System.Xml.Linq;
using VRRecorder.Compliance.Staging;

namespace VRRecorder.Compliance.Tests.Staging;

public sealed class ApprovedWindowsRuntimePropsGeneratorTests
{
    private const string ShaA =
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string ShaB =
        "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string LegalBundleId =
        "https://example.invalid/spdx/vr-recorder-test";

    [Fact]
    public void GeneratesExplicitApprovedItemsInDeterministicTargetOrder()
    {
        var manifest = Manifest(
            ShaA.ToUpperInvariant(),
            Entry(
                source: "inputs/z-source.dll",
                target: "native/z.dll",
                sha256: ShaB),
            Entry(
                source: "inputs/a-source.exe",
                target: "tools/a.exe",
                role: WindowsRuntimeRole.DiagnosticTool,
                deploymentKind: WindowsRuntimeDeploymentKind.Executable,
                sha256: ShaA));

        var bytes = ApprovedWindowsRuntimePropsGenerator.Generate(
            manifest,
            ShaB.ToUpperInvariant());

        Assert.False(bytes.AsSpan().StartsWith(
            new byte[] { 0xef, 0xbb, 0xbf }));
        var text = new UTF8Encoding(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true).GetString(bytes);
        Assert.DoesNotContain('\r', text);
        Assert.EndsWith("\n", text, StringComparison.Ordinal);

        var project = XDocument.Parse(text).Root;
        Assert.NotNull(project);
        Assert.Equal("Project", project.Name.LocalName);
        Assert.Equal(
            "true",
            Property(project, "VRRecorderApprovedWindowsRuntimeImported"));
        Assert.Equal(
            ShaA,
            Property(
                project,
                "VRRecorderApprovedWindowsRuntimeManifestSha256"));
        Assert.Equal(
            ShaB,
            Property(
                project,
                "VRRecorderApprovedWindowsRuntimeInventorySha256"));
        Assert.Equal(
            "full-production-hardware-validation-v1",
            Property(project, "VRRecorderApprovedWindowsRuntimeProfile"));
        Assert.Equal(
            "win-x64",
            Property(
                project,
                "VRRecorderApprovedWindowsRuntimeIdentifier"));
        Assert.Equal(
            LegalBundleId,
            Property(project, "VRRecorderApprovedLegalBundleId"));
        Assert.Equal(
            ShaB,
            Property(project, "VRRecorderApprovedLegalManifestSha256"));
        Assert.Equal(
            LegalBundleId,
            Property(project, "LegalBundleId"));
        Assert.Equal(
            ShaB,
            Property(project, "LegalManifestSha256"));

        var contents = project.Descendants("Content").ToArray();
        Assert.Equal(2, contents.Length);
        Assert.Equal(
            "$(MSBuildThisFileDirectory)payload/native/z.dll",
            contents[0].Attribute("Include")?.Value);
        Assert.Equal(
            "$(MSBuildThisFileDirectory)payload/tools/a.exe",
            contents[1].Attribute("Include")?.Value);
        Assert.Equal("native/z.dll", contents[0].Element("Link")?.Value);
        Assert.Equal(
            "native/z.dll",
            contents[0].Element("TargetPath")?.Value);
        Assert.All(
            contents,
            content =>
            {
                Assert.Equal(
                    "IfDifferent",
                    content.Element("CopyToOutputDirectory")?.Value);
                Assert.Equal(
                    "IfDifferent",
                    content.Element("CopyToPublishDirectory")?.Value);
            });
        Assert.DoesNotContain("inputs/", text, StringComparison.Ordinal);
        Assert.DoesNotContain("**", text, StringComparison.Ordinal);
    }

    [Fact]
    public void EntryInputOrderDoesNotChangeGeneratedBytes()
    {
        var first = Entry(
            source: "inputs/z.dll",
            target: "z.dll",
            sha256: ShaB);
        var second = Entry(
            source: "inputs/a.dll",
            target: "a.dll",
            sha256: ShaA);

        var forward = ApprovedWindowsRuntimePropsGenerator.Generate(
            Manifest(first, second),
            ShaB);
        var reverse = ApprovedWindowsRuntimePropsGenerator.Generate(
            Manifest(second, first),
            ShaB);

        Assert.Equal(forward, reverse);
    }

    [Fact]
    public void XmlSensitiveUnicodeTargetRoundTripsWithoutChangingMsbuildToken()
    {
        var target = "native/音声&capture's.dll";

        var bytes = ApprovedWindowsRuntimePropsGenerator.Generate(
            Manifest(Entry(target: target)),
            ShaB);
        var text = Encoding.UTF8.GetString(bytes);
        var content = XDocument.Parse(text).Descendants("Content").Single();

        Assert.Contains("音声&amp;capture's.dll", text, StringComparison.Ordinal);
        Assert.Equal(
            $"$(MSBuildThisFileDirectory)payload/{target}",
            content.Attribute("Include")?.Value);
        Assert.Equal(target, content.Element("Link")?.Value);
        Assert.Equal(target, content.Element("TargetPath")?.Value);
        Assert.Contains(
            "$(MSBuildThisFileDirectory)",
            text,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("native/a;@(Injected).dll")]
    [InlineData("native/$(Injected).dll")]
    [InlineData("native/%(Metadata).dll")]
    [InlineData("native/*.dll")]
    public void DirectModelCannotInjectMsbuildItemSyntax(string target)
    {
        Assert.Throws<InvalidDataException>(() =>
            ApprovedWindowsRuntimePropsGenerator.Generate(
                Manifest(Entry(target: target)),
                ShaB));
    }

    [Fact]
    public void WindowsEquivalentDuplicateTargetIsRejectedDefensively()
    {
        Assert.Throws<InvalidDataException>(() =>
            ApprovedWindowsRuntimePropsGenerator.Generate(
                Manifest(
                    Entry(source: "inputs/a.dll", target: "Native/A.dll"),
                    Entry(
                        source: "inputs/b.dll",
                        target: "native/a.DLL",
                        sha256: ShaB)),
                ShaB));
    }

    [Fact]
    public void WindowsEquivalentDuplicateSourceIsRejectedDefensively()
    {
        Assert.Throws<InvalidDataException>(() =>
            ApprovedWindowsRuntimePropsGenerator.Generate(
                Manifest(
                    Entry(source: "inputs/a.dll", target: "native/a.dll"),
                    Entry(
                        source: "INPUTS/A.DLL",
                        target: "native/b.dll",
                        sha256: ShaB)),
                ShaB));
    }

    [Theory]
    [InlineData(
        "inputs/a.dll",
        "inputs/b.dll",
        "native",
        "NATIVE/a.dll")]
    [InlineData(
        "inputs",
        "INPUTS/a.dll",
        "native/a.dll",
        "native/b.dll")]
    public void FileParentConflictsAreRejectedDefensively(
        string firstSource,
        string secondSource,
        string firstTarget,
        string secondTarget)
    {
        Assert.Throws<InvalidDataException>(() =>
            ApprovedWindowsRuntimePropsGenerator.Generate(
                Manifest(
                    Entry(source: firstSource, target: firstTarget),
                    Entry(
                        source: secondSource,
                        target: secondTarget,
                        sha256: ShaB)),
                ShaB));
    }

    [Fact]
    public void XmlInvalidTargetIsRejectedBeforeSerialization()
    {
        Assert.Throws<InvalidDataException>(() =>
            ApprovedWindowsRuntimePropsGenerator.Generate(
                Manifest(Entry(target: "native/\ud800.dll")),
                ShaB));
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData(
        "gggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggg")]
    public void InvalidInventoryDigestIsRejected(string inventorySha256)
    {
        Assert.Throws<InvalidDataException>(() =>
            ApprovedWindowsRuntimePropsGenerator.Generate(
                Manifest(Entry()),
                inventorySha256));
    }

    [Fact]
    public void InvalidManifestDigestAndEmptyManifestAreRejected()
    {
        Assert.Throws<InvalidDataException>(() =>
            ApprovedWindowsRuntimePropsGenerator.Generate(
                Manifest(ShaA[..^1], Entry()),
                ShaB));
        Assert.Throws<InvalidDataException>(() =>
            ApprovedWindowsRuntimePropsGenerator.Generate(
                Manifest(),
                ShaB));
    }

    private static string Property(XElement project, string name) =>
        project.Descendants(name).Single().Value;

    private static WindowsRuntimeStagingManifest Manifest(
        params WindowsRuntimeStagingEntry[] entries) =>
        Manifest(ShaA, entries);

    private static WindowsRuntimeStagingManifest Manifest(
        string manifestSha256,
        params WindowsRuntimeStagingEntry[] entries) =>
        new(
            SchemaVersion: 2,
            ManifestSha256: manifestSha256,
            Profile: "full-production-hardware-validation-v1",
            RuntimeIdentifier: "win-x64",
            LegalBundle: new WindowsRuntimeLegalBundleAnchor(
                LegalBundleId,
                ShaB),
            Entries: entries);

    private static WindowsRuntimeStagingEntry Entry(
        string source = "inputs/avcodec-62.dll",
        string target = "avcodec-62.dll",
        WindowsRuntimeRole role = WindowsRuntimeRole.FfmpegRuntime,
        WindowsRuntimeDeploymentKind deploymentKind =
            WindowsRuntimeDeploymentKind.NativeLibrary,
        string sha256 = ShaA) =>
        new(
            Source: source,
            Target: target,
            Role: role,
            ComponentId: "ffmpeg",
            Platform: "windows-x64",
            DeploymentKind: deploymentKind,
            Sha256: sha256,
            Length: 17);
}
