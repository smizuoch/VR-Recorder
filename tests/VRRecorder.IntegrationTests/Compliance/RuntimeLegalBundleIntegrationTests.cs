using System.Security.Cryptography;
using System.Text;
using VRRecorder.Application.Compliance;
using VRRecorder.Application.Presentation;
using VRRecorder.Compliance.Runtime;
using VRRecorder.DesignSystem;
using VRRecorder.Domain.Recording;
using VRRecorder.Presentation.Wrist;

namespace VRRecorder.IntegrationTests.Compliance;

public sealed class RuntimeLegalBundleIntegrationTests
{
    private const string BundleId =
        "https://example.invalid/spdx/vr-recorder-integration";

    [Fact]
    [Trait("Scenario", "IT-031")]
    public async Task TamperedBundleLocksRecorderAndSuppressesWristRecAction()
    {
        using var directory = TemporaryDirectory.Create();
        var catalogPath = Path.Combine(
            directory.Path,
            "THIRD-PARTY-COMPONENTS.json");
        var catalog = Encoding.UTF8.GetBytes($$"""
            {
              "schemaVersion": 2,
              "bundleId": "{{BundleId}}",
              "integrityManifest": {
                "path": "LEGAL-MANIFEST.sha256",
                "algorithm": "SHA-256"
              }
            }
            """);
        var manifest = Encoding.UTF8.GetBytes(
            $"{Hash(catalog)}  THIRD-PARTY-COMPONENTS.json\n");
        await File.WriteAllBytesAsync(catalogPath, catalog);
        await File.WriteAllBytesAsync(
            Path.Combine(directory.Path, "LEGAL-MANIFEST.sha256"),
            manifest);
        await File.AppendAllTextAsync(catalogPath, "\n");
        var gateway = new RuntimeLegalBundleVerificationGateway(
            directory.Path,
            new AuthenticatedLegalBundleVerifier(
                new FixedAuthenticatedAnchorSource(
                    new AuthenticatedLegalBundleAnchor(
                        BundleId,
                        Hash(manifest)))));

        var startup = await new RecorderStartupUseCase(gateway)
            .ExecuteAsync(CancellationToken.None);
        var wrist = new WristUiProjector(EnglishUiLocalizer.Instance)
            .Project(new RecorderStatusSnapshot(
                Revision: 1,
                State: startup.State,
                AvailableActions: RecorderAvailableActions.Start));

        Assert.Equal(RecorderState.ComplianceFault, startup.State);
        Assert.Equal(
            "LEGAL_BUNDLE_HASH_MISMATCH",
            Assert.Single(startup.Issues).Code);
        Assert.Equal(UiColorRole.Error, wrist.StateCue.ColorRole);
        Assert.DoesNotContain(wrist.Actions, action =>
            action.Command == UiCommandId.ToggleRecording);
    }

    private static string Hash(ReadOnlySpan<byte> content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    private sealed class FixedAuthenticatedAnchorSource(
        AuthenticatedLegalBundleAnchor anchor)
        : IAuthenticatedLegalBundleAnchorSource
    {
        public ValueTask<AuthenticatedLegalBundleAnchor> GetAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(anchor);
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TemporaryDirectory Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"vr-recorder-compliance-integration-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return new TemporaryDirectory(path);
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
