using VRRecorder.Domain.Camera;
using VRRecorder.Infrastructure.Storage;

namespace VRRecorder.IntegrationTests.Storage;

public sealed class FileSystemCameraLeaseStoreTests
{
    [Fact]
    public async Task RichLeaseRoundTripsAndSameSaveIsIdempotent()
    {
        using var directory = TemporaryDirectory.Create();
        var path = Path.Combine(directory.Path, "camera-lease.json");
        using var store = new FileSystemCameraLeaseStore(path);
        var lease = Lease("session-a", "service-a", processId: 1234);

        await store.SaveAsync(lease, CancellationToken.None);
        var firstBytes = await File.ReadAllBytesAsync(path);
        await store.SaveAsync(lease, CancellationToken.None);

        var loaded = Assert.IsType<CameraLease>(
            await store.LoadAsync(CancellationToken.None));
        Assert.Equal("session-a", loaded.SessionId);
        Assert.Equal("service-a", loaded.VrChatServiceId);
        Assert.Equal(1234, loaded.ProcessId);
        Assert.Equal(
            new DateTimeOffset(2026, 7, 10, 0, 0, 0, TimeSpan.Zero),
            loaded.CreatedAtUtc);
        Assert.True(loaded.PreviousMode.IsKnown);
        Assert.Equal(CameraMode.Photo, loaded.PreviousMode.Value);
        Assert.True(loaded.PreviousStreaming.IsKnown);
        Assert.False(loaded.PreviousStreaming.Value);
        Assert.True(loaded.ChangedModeByRecorder);
        Assert.True(loaded.ChangedStreamingByRecorder);
        Assert.Equal(firstBytes, await File.ReadAllBytesAsync(path));
        Assert.Empty(Directory.EnumerateFiles(
            directory.Path,
            ".camera-lease.json.*.tmp"));
    }

    [Fact]
    public async Task DifferentLeaseCannotOverwriteOrDeletePersistedOwner()
    {
        using var directory = TemporaryDirectory.Create();
        var path = Path.Combine(directory.Path, "camera-lease.json");
        using var store = new FileSystemCameraLeaseStore(path);
        var persisted = Lease("session-a", "service-a", processId: 1234);
        var different = Lease("session-b", "service-a", processId: 5678);
        await store.SaveAsync(persisted, CancellationToken.None);
        var evidence = await File.ReadAllBytesAsync(path);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.SaveAsync(different, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.DeleteAsync(different, CancellationToken.None));

        Assert.Equal(evidence, await File.ReadAllBytesAsync(path));
        Assert.Equal(
            "session-a",
            (await store.LoadAsync(CancellationToken.None))?.SessionId);
    }

    [Theory]
    [InlineData("{\"schemaVersion\":1,\"schemaVersion\":1}")]
    [InlineData("{\"schemaVersion\":1,\"unexpected\":true}")]
    [InlineData("not-json")]
    public async Task InvalidDocumentFailsClosedWithoutDestroyingEvidence(
        string content)
    {
        using var directory = TemporaryDirectory.Create();
        var path = Path.Combine(directory.Path, "camera-lease.json");
        await File.WriteAllTextAsync(path, content);
        using var store = new FileSystemCameraLeaseStore(path);
        var evidence = await File.ReadAllBytesAsync(path);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            store.LoadAsync(CancellationToken.None));

        Assert.Equal(evidence, await File.ReadAllBytesAsync(path));
    }

    [Theory]
    [InlineData("999")]
    [InlineData("UndefinedMode")]
    public async Task UndefinedOrNumericModeNameIsRejectedWithoutChangingEvidence(
        string modeName)
    {
        using var directory = TemporaryDirectory.Create();
        var path = Path.Combine(directory.Path, "camera-lease.json");
        using var store = new FileSystemCameraLeaseStore(path);
        await store.SaveAsync(
            Lease("session-a", "service-a", processId: 1234),
            CancellationToken.None);
        var valid = await File.ReadAllTextAsync(path);
        var invalid = valid.Replace(
            "\"previousMode\": \"Photo\"",
            $"\"previousMode\": \"{modeName}\"",
            StringComparison.Ordinal);
        await File.WriteAllTextAsync(path, invalid);
        var evidence = await File.ReadAllBytesAsync(path);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            store.LoadAsync(CancellationToken.None));

        Assert.Equal(evidence, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task LinkedLeaseFileIsRejectedWithoutChangingTarget()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var directory = TemporaryDirectory.Create();
        var outside = Path.Combine(directory.Path, "outside.json");
        var link = Path.Combine(directory.Path, "camera-lease.json");
        await File.WriteAllTextAsync(outside, "outside evidence");
        File.CreateSymbolicLink(link, outside);
        using var store = new FileSystemCameraLeaseStore(link);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            store.SaveAsync(
                Lease("session-a", "service-a", processId: 1234),
                CancellationToken.None));

        Assert.Equal("outside evidence", await File.ReadAllTextAsync(outside));
    }

    [Fact]
    public async Task DanglingLeaseLinkIsRejectedWithoutReplacingEvidence()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var directory = TemporaryDirectory.Create();
        var missingTarget = Path.Combine(directory.Path, "missing.json");
        var link = Path.Combine(directory.Path, "camera-lease.json");
        File.CreateSymbolicLink(link, missingTarget);
        using var store = new FileSystemCameraLeaseStore(link);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            store.SaveAsync(
                Lease("session-a", "service-a", processId: 1234),
                CancellationToken.None));

        Assert.False(File.Exists(missingTarget));
        Assert.Equal(missingTarget, new FileInfo(link).LinkTarget);
    }

    private static CameraLease Lease(
        string sessionId,
        string serviceId,
        int processId) =>
        new(
            sessionId,
            serviceId,
            processId,
            new DateTimeOffset(2026, 7, 10, 0, 0, 0, TimeSpan.Zero),
            ObservedCameraValue.Known(CameraMode.Photo),
            ObservedCameraValue.Known(false),
            changedModeByRecorder: true,
            changedStreamingByRecorder: true);

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
                $"vr-recorder-camera-lease-{Guid.NewGuid():N}");
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
