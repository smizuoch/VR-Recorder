using System.Security.Cryptography;
using System.Text.Json;
using VRRecorder.Compliance.Repository;

namespace VRRecorder.Compliance.Tests.Repository;

public sealed class RepositoryComplianceTests
{
    [Fact]
    public void LockedNuGetPackagesHavePinnedCandidateLegalMetadata()
    {
        var repositoryRoot = FindRepositoryRoot();

        var issues = RepositoryComplianceVerifier.VerifyCandidateInputs(repositoryRoot);

        Assert.Empty(issues);
    }

    [Fact]
    public void RequiredReadmesHaveBilingualHeadingAndReleaseParity()
    {
        var repositoryRoot = FindRepositoryRoot();

        var issues = ReadmeBilingualParityValidator.VerifyRequiredReadmes(
            repositoryRoot);

        Assert.Empty(issues);
    }

    [Fact]
    public void LegalTemplateSnapshotMatchesHashesAndCompleteFileInventory()
    {
        var repositoryRoot = FindRepositoryRoot();

        var issues = LegalTemplateManifestValidator.Verify(repositoryRoot);

        Assert.Empty(issues);
    }

    [Fact]
    public void RepositoryComplianceWorkflowWatchesDependencyAdmissionInputs()
    {
        var repositoryRoot = FindRepositoryRoot();
        var workflowPath = Path.Combine(
            repositoryRoot,
            ".github",
            "workflows",
            "repository-compliance.yml");

        var workflowLines = File.ReadAllLines(workflowPath)
            .Select(line => line.Trim())
            .ToArray();

        Assert.Contains("- legal-template/**", workflowLines);
        Assert.Equal(2, workflowLines.Count(line =>
            line == "- third-party/**"));
        Assert.Equal(2, workflowLines.Count(line =>
            line == "- '**/packages.lock.json'"));
        Assert.Equal(2, workflowLines.Count(line =>
            line == "- CMakeLists.txt"));
        Assert.Equal(2, workflowLines.Count(line =>
            line == "- CMakePresets.json"));
        Assert.Equal(2, workflowLines.Count(line =>
            line == "- cmake/**"));
        Assert.Equal(2, workflowLines.Count(line =>
            line == "- src/VRRecorder.Native/**"));
        Assert.Equal(2, workflowLines.Count(line =>
            line == "- tests/cmake/**"));
        Assert.Equal(2, workflowLines.Count(line =>
            line == "- tests/VRRecorder.Native.Tests/**"));
    }

    [Fact]
    public void NativeRuntimeLoadCallSitesHaveExplicitIntegrityAdmissions()
    {
        var repositoryRoot = FindRepositoryRoot();

        var issues = RepositoryNativeRuntimeLoadVerifier.Verify(repositoryRoot);

        Assert.Empty(issues);
    }

    [Fact]
    public void NativeLinkCallSitesHaveExplicitOwnershipAdmissions()
    {
        var repositoryRoot = FindRepositoryRoot();

        var issues = RepositoryNativeLinkVerifier.Verify(repositoryRoot);

        Assert.Empty(issues);
    }

    [Fact]
    public void WindowsSystemNativeLinksRetainWindowsPlatformProvenance()
    {
        var repositoryRoot = FindRepositoryRoot();
        var manifestPath = Path.Combine(
            repositoryRoot,
            "third-party",
            "native-link-manifest.yml");
        using var manifest = JsonDocument.Parse(
            File.ReadAllBytes(manifestPath));

        var windowsSystemEntries = manifest.RootElement
            .GetProperty("entries")
            .EnumerateArray()
            .Where(entry =>
                entry.GetProperty("origin").GetString() == "WindowsSystem")
            .ToArray();
        Assert.Equal(2, windowsSystemEntries.Length);
        Assert.All(windowsSystemEntries, entry => Assert.Equal(
            "windows-x64",
            entry.GetProperty("platform").GetString()));
    }

    [Fact]
    public void CandidateVerificationRejectsAnUnregisteredRuntimeLoadCallSite()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"vr-recorder-runtime-load-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "third-party"));
            Directory.CreateDirectory(Path.Combine(root, "src", "Rogue"));
            File.WriteAllText(
                Path.Combine(root, "third-party", "registry.yml"),
                """
                {
                  "schemaVersion": 1,
                  "registryVersion": 1,
                  "components": []
                }
                """);
            File.WriteAllText(
                Path.Combine(
                    root,
                    "third-party",
                    "runtime-load-manifest.yml"),
                """
                {
                  "schemaVersion": 1,
                  "entries": []
                }
                """);
            File.WriteAllText(
                Path.Combine(root, "src", "Rogue", "Loader.cs"),
                "NativeLibrary.Load(fullPath);");

            var issues = RepositoryComplianceVerifier.VerifyCandidateInputs(root);

            Assert.Contains(issues, issue =>
                issue.Code == "unregistered-runtime-load" &&
                issue.Subject == "src/Rogue/Loader.cs");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void CandidateVerificationRejectsAnUnregisteredCMakeLinkInput()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"vr-recorder-native-link-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "third-party"));
            Directory.CreateDirectory(Path.Combine(root, "src", "Rogue"));
            File.WriteAllText(
                Path.Combine(root, "third-party", "registry.yml"),
                """
                {
                  "schemaVersion": 1,
                  "registryVersion": 1,
                  "components": []
                }
                """);
            foreach (var manifestName in new[]
                     {
                         "runtime-load-manifest.yml",
                         "native-link-manifest.yml",
                     })
            {
                File.WriteAllText(
                    Path.Combine(root, "third-party", manifestName),
                    """
                    {
                      "schemaVersion": 1,
                      "entries": []
                    }
                    """);
            }

            File.WriteAllText(
                Path.Combine(root, "src", "Rogue", "CMakeLists.txt"),
                "target_link_libraries(vrrecorder_native PRIVATE Spout.lib)");

            var issues = RepositoryComplianceVerifier.VerifyCandidateInputs(root);

            Assert.Contains(issues, issue =>
                issue.Code == "unregistered-native-link" &&
                issue.Subject == "vrrecorder_native:Spout.lib");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void ThirdPartyRuntimeLoadRequiresNativeArtifactMetadata()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"vr-recorder-native-artifact-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "third-party"));
            Directory.CreateDirectory(Path.Combine(root, "src", "Rogue"));
            File.WriteAllText(
                Path.Combine(root, "third-party", "registry.yml"),
                """
                {
                  "schemaVersion": 1,
                  "registryVersion": 1,
                  "components": [
                    {
                      "id": "openvr"
                    }
                  ]
                }
                """);
            File.WriteAllText(
                Path.Combine(
                    root,
                    "third-party",
                    "runtime-load-manifest.yml"),
                """
                {
                  "schemaVersion": 1,
                  "entries": [
                    {
                      "consumer": "Rogue",
                      "fileName": "openvr_api.dll",
                      "mechanism": "NativeLibrary",
                      "platform": "windows-x64",
                      "origin": "ThirdParty",
                      "integrity": "RegistrySha256",
                      "componentId": "openvr",
                      "sourcePaths": ["src/Rogue/Loader.cs"]
                    }
                  ]
                }
                """);
            File.WriteAllText(
                Path.Combine(root, "src", "Rogue", "Loader.cs"),
                "NativeLibrary.Load(fullPath);");

            var issues = RepositoryNativeRuntimeLoadVerifier.Verify(root);

            Assert.Contains(issues, issue =>
                issue.Code == "missing-native-artifact-registration" &&
                issue.Subject == "openvr:openvr_api.dll");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void ThirdPartyImportedTargetRequiresItsRuntimeArtifactMapping()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"vr-recorder-native-target-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "third-party"));
            Directory.CreateDirectory(Path.Combine(root, "src", "Native"));
            Directory.CreateDirectory(
                Path.Combine(root, "third-party", "source-archives"));
            Directory.CreateDirectory(
                Path.Combine(root, "third-party", "build-recipes"));
            var sourceArchivePath = Path.Combine(
                root,
                "third-party",
                "source-archives",
                "ffmpeg-source.tar.xz");
            File.WriteAllText(sourceArchivePath, "pinned ffmpeg source");
            var sourceArchiveSha256 = Convert
                .ToHexString(SHA256.HashData(File.ReadAllBytes(sourceArchivePath)))
                .ToLowerInvariant();
            File.WriteAllText(
                Path.Combine(
                    root,
                    "third-party",
                    "build-recipes",
                    "ffmpeg-windows-x64.md"),
                "pinned build recipe");
            File.WriteAllText(
                Path.Combine(root, "third-party", "registry.yml"),
                $$"""
                {
                  "schemaVersion": 1,
                  "registryVersion": 1,
                  "components": [
                    {
                      "id": "ffmpeg",
                      "nativeArtifacts": [
                        {
                          "platform": "windows-x64",
                          "fileName": "avcodec-62.dll",
                          "binarySha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                          "sourceArchivePath": "third-party/source-archives/ffmpeg-source.tar.xz",
                          "sourceArchiveSha256": "{{sourceArchiveSha256}}",
                          "buildRecipePath": "third-party/build-recipes/ffmpeg-windows-x64.md"
                        }
                      ]
                    }
                  ]
                }
                """);
            var nativeLinkManifestPath = Path.Combine(
                root,
                "third-party",
                "native-link-manifest.yml");
            var nativeLinkManifest = """
                {
                  "schemaVersion": 1,
                  "entries": [
                    {
                      "consumerTarget": "vrrecorder_native",
                      "inputIdentity": "FFmpeg::avcodec",
                      "inputKind": "ToolchainTarget",
                      "platform": "windows-x64",
                      "origin": "ThirdParty",
                      "componentId": "ffmpeg",
                      "artifactFileName": "avcodec-62.dll",
                      "sourcePath": "src/Native/CMakeLists.txt"
                    }
                  ]
                }
                """;
            File.WriteAllText(nativeLinkManifestPath, nativeLinkManifest);
            File.WriteAllText(
                Path.Combine(root, "src", "Native", "CMakeLists.txt"),
                """
                if(WIN32)
                    target_link_libraries(
                        vrrecorder_native
                        PRIVATE
                            FFmpeg::avcodec)
                endif()
                """);

            var issues = RepositoryNativeLinkVerifier.Verify(root);

            Assert.Empty(issues);

            File.WriteAllText(
                nativeLinkManifestPath,
                nativeLinkManifest.Replace(
                    "      \"artifactFileName\": \"avcodec-62.dll\",\n",
                    string.Empty,
                    StringComparison.Ordinal));

            issues = RepositoryNativeLinkVerifier.Verify(root);

            Assert.Contains(issues, issue =>
                issue.Code == "invalid-native-link-manifest" &&
                issue.Subject == "third-party/native-link-manifest.yml");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void ComponentCatalogV3TemplateIsStrictCycleFreeAndDocumented()
    {
        var repositoryRoot = FindRepositoryRoot();
        var schemaPath = Path.Combine(
            repositoryRoot,
            "legal-template",
            "schemas",
            "third-party-components-v3.schema.json");
        var examplePath = Path.Combine(
            repositoryRoot,
            "legal-template",
            "THIRD-PARTY-COMPONENTS.v3.example.json");
        var decisionPath = Path.Combine(
            repositoryRoot,
            "docs",
            "adr",
            "0002-legal-catalog-v3.md");

        Assert.True(File.Exists(schemaPath), schemaPath);
        Assert.True(File.Exists(examplePath), examplePath);
        Assert.True(File.Exists(decisionPath), decisionPath);
        using var schema = JsonDocument.Parse(File.ReadAllBytes(schemaPath));
        using var example = JsonDocument.Parse(File.ReadAllBytes(examplePath));

        Assert.Equal(
            "urn:vr-recorder:third-party-components:3",
            schema.RootElement.GetProperty("$id").GetString());
        Assert.Equal(
            3,
            schema.RootElement
                .GetProperty("properties")
                .GetProperty("schemaVersion")
                .GetProperty("const")
                .GetInt32());
        Assert.False(schema.RootElement
            .GetProperty("properties")
            .TryGetProperty("manifestSha256", out _));
        Assert.DoesNotContain(
            schema.RootElement.GetProperty("required").EnumerateArray(),
            item => item.GetString() == "manifestSha256");

        var root = example.RootElement;
        Assert.Equal(3, root.GetProperty("schemaVersion").GetInt32());
        Assert.False(root.TryGetProperty("manifestSha256", out _));
        Assert.Equal(
            "LEGAL-MANIFEST.sha256",
            root.GetProperty("integrityManifest")
                .GetProperty("path")
                .GetString());
        Assert.Equal(
            "SHA-256",
            root.GetProperty("integrityManifest")
                .GetProperty("algorithm")
                .GetString());
        var component = Assert.Single(root.GetProperty("components")
            .EnumerateArray());
        Assert.False(component.TryGetProperty("licenseText", out _));
        Assert.False(string.IsNullOrWhiteSpace(
            component.GetProperty("copyrightNotice").GetString()));
        var legalDocuments = component.GetProperty("legalDocuments")
            .EnumerateArray()
            .ToArray();
        Assert.Contains(legalDocuments, document =>
            document.GetProperty("kind").GetString() == "license");
        var decision = File.ReadAllText(decisionPath);
        Assert.Contains(
            "out-of-band",
            decision,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "schema v2",
            decision,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "fail closed",
            decision,
            StringComparison.OrdinalIgnoreCase);
    }

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

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
