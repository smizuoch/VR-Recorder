using VRRecorder.Compliance.Staging;

namespace VRRecorder.Compliance.Tests.Staging;

public sealed class WindowsRuntimeFileSemanticsVerifierTests
{
    [Fact]
    public void RegularFileIsAcceptedOnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var file = TemporaryFile.Create();

        WindowsRuntimeFileSemanticsVerifier.Instance.VerifyRegularFile(
            file.Path);
    }

    [Fact]
    public void NamedDataStreamIsRejectedOnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var file = TemporaryFile.Create();
        File.WriteAllText(file.Path + ":unapproved", "hidden");

        Assert.Throws<InvalidDataException>(() =>
            WindowsRuntimeFileSemanticsVerifier.Instance.VerifyRegularFile(
                file.Path));
    }

    private sealed class TemporaryFile : IDisposable
    {
        private TemporaryFile(string path) => Path = path;

        public string Path { get; }

        public static TemporaryFile Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"vr-recorder-stream-test-{Guid.NewGuid():N}.bin");
            File.WriteAllText(path, "payload");
            return new TemporaryFile(path);
        }

        public void Dispose()
        {
            if (File.Exists(Path))
            {
                File.Delete(Path);
            }
        }
    }
}
