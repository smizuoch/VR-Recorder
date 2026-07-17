using VRRecorder.Compliance.Distribution;

namespace VRRecorder.Compliance.Tests.Distribution;

public sealed class ManagedApplicationBuildIdentityReaderTests
{
    [Fact]
    public void ReadsExactIdentityWithoutLoadingTheAssembly()
    {
        var result = ManagedApplicationBuildIdentityReader.Read(
            typeof(ManagedApplicationBuildIdentityReaderTests)
                .Assembly
                .Location);

        Assert.True(result.IsAdmitted);
        Assert.Empty(result.Issues);
        var identity = Assert.IsType<ManagedApplicationBuildIdentity>(
            result.Identity);
        Assert.Equal("0.1.0", identity.ProductVersion);
        Assert.Equal(
            "0123456789abcdef0123456789abcdef01234567",
            identity.SourceRevision);
        Assert.Equal("win-x64", identity.RuntimeIdentifier);
    }

    [Fact]
    public void MissingBuildIdentityMetadataIsRejected()
    {
        var result = ManagedApplicationBuildIdentityReader.Read(
            typeof(WindowsPostPublishPayloadSealer).Assembly.Location);

        Assert.False(result.IsAdmitted);
        Assert.Null(result.Identity);
        Assert.Contains(
            result.Issues,
            issue => issue.Code ==
                "application-build-identity-metadata-invalid");
    }

    [Fact]
    public void NonManagedPayloadIsRejected()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "vrrecorder-build-identity-tests",
            Guid.NewGuid().ToString("N"),
            "VRRecorder.App.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            File.WriteAllText(path, "not a managed PE");

            var result = ManagedApplicationBuildIdentityReader.Read(path);

            Assert.False(result.IsAdmitted);
            Assert.Null(result.Identity);
            Assert.Contains(
                result.Issues,
                issue => issue.Code ==
                    "application-build-identity-assembly-invalid");
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }
}
