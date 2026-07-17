using System.Security.Cryptography;
using System.Text;
using VRRecorder.Compliance.Generation;

namespace VRRecorder.Compliance.Tests.Staging;

internal sealed record WindowsRuntimeStagingTestEntry(
    string Source,
    string Target,
    string Role,
    string ComponentId,
    string DeploymentKind,
    long? DeclaredLength = null);

internal sealed record FullProductionStagingTestData(
    IReadOnlyList<WindowsRuntimeStagingTestEntry> Entries,
    WindowsRuntimeStagingTestEntry NativeEntry,
    WindowsRuntimeStagingTestEntry EvidenceEntry,
    byte[] NativeBinary,
    IReadOnlyList<NormalizedComponent> ApprovedComponents)
{
    private const string LegalManifestSha =
        "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";

    public static FullProductionStagingTestData Create(
        string sourceRoot,
        string repositoryRoot,
        string factoryIntentSha)
    {
        var native = Entry(
            "native/vrrecorder_native.dll",
            "vrrecorder_native.dll",
            "first-party-native",
            "vr-recorder",
            "native-library");
        var evidence = Entry(
            "evidence/native-factory-selection.json",
            "native-factory-selection.json",
            "factory-selection-evidence",
            "vr-recorder",
            "evidence");
        var nativeBinary = WindowsPeImageTestData.Create(
            isDll: true,
            subsystem: 2,
            imports: ["KERNEL32.dll"],
            payload: Encoding.ASCII.GetBytes(
                "prefix-VRRECORDER_FACTORY_SELECTION_V1:" +
                factoryIntentSha +
                "-suffix"));
        Write(sourceRoot, native.Source, nativeBinary);
        var evidenceBytes = Encoding.UTF8.GetBytes($$$"""
            {"schemaVersion":1,"evidenceKind":"linked-native-factory-selection","selectionIntentSha256":"{{{factoryIntentSha}}}","fullProductionRequired":true,"nativeBinary":{"file":"vrrecorder_native.dll","length":{{{nativeBinary.LongLength}}},"sha256":"{{{Sha256(nativeBinary)}}}"},"media":{"variant":"PRODUCTION","source":"production_media_backend.cpp"},"encoderProbe":{"variant":"PRODUCTION","source":"production_encoder_probe_backend.cpp"},"spout":{"variant":"PRODUCTION","source":"spout2_source_backend.cpp"},"steamVr":{"variant":"PRODUCTION","source":"openvr_steamvr_input_backend.cpp"}}
            """);
        Write(sourceRoot, evidence.Source, evidenceBytes);

        var entries = new List<WindowsRuntimeStagingTestEntry>
        {
            native,
            Entry("runtime/avcodec-62.dll", "avcodec-62.dll",
                "ffmpeg-runtime", "ffmpeg", "native-library"),
            Entry("runtime/avformat-62.dll", "avformat-62.dll",
                "ffmpeg-runtime", "ffmpeg", "native-library"),
            Entry("runtime/avutil-60.dll", "avutil-60.dll",
                "ffmpeg-runtime", "ffmpeg", "native-library"),
            Entry("runtime/swresample-6.dll", "swresample-6.dll",
                "ffmpeg-runtime", "ffmpeg", "native-library"),
            Entry("tools/ffprobe.exe", "ffprobe.exe",
                "diagnostic-tool", "ffmpeg", "executable"),
            Entry("runtime/openvr_api.dll", "openvr_api.dll",
                "openvr-runtime", "openvr", "native-library"),
            Entry("openvr/steamvr.vrmanifest", "OpenVr/steamvr.vrmanifest",
                "openvr-manifest", "openvr", "asset"),
            Entry("openvr/actions.json", "OpenVr/actions.json",
                "openvr-manifest", "openvr", "asset"),
            Entry("openvr/bindings/knuckles.json",
                "OpenVr/bindings/knuckles.json",
                "openvr-binding", "openvr", "asset"),
            Entry("openvr/bindings/oculus_touch.json",
                "OpenVr/bindings/oculus_touch.json",
                "openvr-binding", "openvr", "asset"),
            Entry("openvr/bindings/vive_controller.json",
                "OpenVr/bindings/vive_controller.json",
                "openvr-binding", "openvr", "asset"),
            evidence,
        };

        foreach (var entry in entries.Where(entry =>
                     entry.DeploymentKind is "native-library" or "executable" &&
                     entry != native))
        {
            Write(
                sourceRoot,
                entry.Source,
                WindowsPeImageTestData.Create(
                    isDll: entry.DeploymentKind == "native-library",
                    subsystem: entry.DeploymentKind == "executable"
                        ? (ushort)3
                        : (ushort)2,
                    imports: ["KERNEL32.dll"]));
        }

        foreach (var entry in entries.Where(entry =>
                     entry.DeploymentKind == "asset"))
        {
            Write(sourceRoot, entry.Source, "{}"u8.ToArray());
        }

        WriteApprovedRegistry(repositoryRoot, sourceRoot, entries);
        return new FullProductionStagingTestData(
            entries,
            native,
            evidence,
            nativeBinary,
            [Component("ffmpeg"), Component("openvr")]);
    }

    public static string ManifestJson(
        string sourceRoot,
        IEnumerable<WindowsRuntimeStagingTestEntry> entries) => $$"""
        {"schemaVersion":2,"profile":"full-production-hardware-validation-v1","runtimeIdentifier":"win-x64","legalBundle":{"bundleId":"https://example.invalid/spdx/vr-recorder-test","manifestSha256":"{{LegalManifestSha}}"},"entries":[{{string.Join(',', entries.Select(entry => EntryJson(sourceRoot, entry)))}}]}
        """;

    private static string EntryJson(
        string sourceRoot,
        WindowsRuntimeStagingTestEntry entry)
    {
        var bytes = File.ReadAllBytes(Resolve(sourceRoot, entry.Source));
        return $$"""
            {"source":"{{entry.Source}}","target":"{{entry.Target}}","role":"{{entry.Role}}","componentId":"{{entry.ComponentId}}","platform":"windows-x64","deploymentKind":"{{entry.DeploymentKind}}","sha256":"{{Sha256(bytes)}}","length":{{entry.DeclaredLength ?? bytes.LongLength}}}
            """;
    }

    private static void WriteApprovedRegistry(
        string repositoryRoot,
        string sourceRoot,
        IReadOnlyList<WindowsRuntimeStagingTestEntry> entries)
    {
        var evidenceRoot = Path.Combine(repositoryRoot, "third-party", "test");
        Directory.CreateDirectory(evidenceRoot);
        var archive = Path.Combine(evidenceRoot, "source.tar");
        var recipe = Path.Combine(evidenceRoot, "recipe.md");
        File.WriteAllText(archive, "approved source");
        File.WriteAllText(recipe, "approved recipe");
        var archiveSha = Sha256(File.ReadAllBytes(archive));
        var artifacts = entries
            .Where(entry => entry.ComponentId is "ffmpeg" or "openvr" &&
                            entry.DeploymentKind is
                                "native-library" or "executable")
            .GroupBy(entry => entry.ComponentId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => string.Join(',', group.Select(entry => $$"""
                    {"platform":"windows-x64","fileName":"{{Path.GetFileName(entry.Target)}}","binarySha256":"{{Sha256(File.ReadAllBytes(Resolve(sourceRoot, entry.Source)))}}","sourceArchivePath":"third-party/test/source.tar","sourceArchiveSha256":"{{archiveSha}}","buildRecipePath":"third-party/test/recipe.md"}
                    """)),
                StringComparer.Ordinal);
        var registry = $$"""
            {"schemaVersion":1,"registryVersion":1,"components":[{{ComponentJson("ffmpeg", artifacts["ffmpeg"])}},{{ComponentJson("openvr", artifacts["openvr"])}}]}
            """;
        var registryDirectory = Path.Combine(repositoryRoot, "third-party");
        Directory.CreateDirectory(registryDirectory);
        File.WriteAllText(
            Path.Combine(registryDirectory, "registry.yml"),
            registry);
    }

    private static string ComponentJson(string id, string artifacts) => $$"""
        {"id":"{{id}}","version":"1.0.0","repository":{"url":"https://example.invalid/source","commit":"commit"},"approval":{"status":"approved","id":"LEGAL-TEST","reviewer":"reviewer"},"nativeArtifacts":[{{artifacts}}]}
        """;

    private static NormalizedComponent Component(string id) => new(
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
        NoticeScope.RuntimeBundled,
        new LegalApproval(
            LegalApprovalStatus.Approved,
            "LEGAL-TEST",
            "requester",
            "reviewer"),
        Packages: []);

    private static WindowsRuntimeStagingTestEntry Entry(
        string source,
        string target,
        string role,
        string componentId,
        string deploymentKind) => new(
        source,
        target,
        role,
        componentId,
        deploymentKind);

    private static void Write(
        string sourceRoot,
        string relativePath,
        byte[] bytes)
    {
        var path = Resolve(sourceRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }

    private static string Resolve(string root, string relativePath) =>
        Path.GetFullPath(Path.Combine(
            root,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string Sha256(byte[] bytes) => Convert
        .ToHexString(SHA256.HashData(bytes))
        .ToLowerInvariant();
}
