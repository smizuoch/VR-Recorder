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
    [InlineData("[]")]
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

    [Fact]
    public async Task UnknownCameraStateRoundTripsAsExplicitNulls()
    {
        using var directory = TemporaryDirectory.Create();
        var path = Path.Combine(directory.Path, "camera-lease.json");
        using var store = new FileSystemCameraLeaseStore(path);
        var lease = new CameraLease(
            "session-a",
            "service-a",
            1234,
            new DateTimeOffset(2026, 7, 10, 0, 0, 0, TimeSpan.Zero),
            ObservedCameraValue.Unknown<CameraMode>(),
            ObservedCameraValue.Unknown<bool>(),
            changedModeByRecorder: false,
            changedStreamingByRecorder: false);

        await store.SaveAsync(lease, CancellationToken.None);
        var loaded = Assert.IsType<CameraLease>(
            await store.LoadAsync(CancellationToken.None));

        Assert.False(loaded.PreviousMode.IsKnown);
        Assert.False(loaded.PreviousStreaming.IsKnown);
        var content = await File.ReadAllTextAsync(path);
        Assert.Contains("\"previousMode\": null", content);
        Assert.Contains("\"previousStreaming\": null", content);
    }

    [Fact]
    public async Task MatchingDeleteAndRepeatedDeleteAreIdempotent()
    {
        using var directory = TemporaryDirectory.Create();
        var path = Path.Combine(directory.Path, "camera-lease.json");
        using var store = new FileSystemCameraLeaseStore(path);
        var lease = Lease("session-a", "service-a", processId: 1234);
        await store.SaveAsync(lease, CancellationToken.None);

        await store.DeleteAsync(lease, CancellationToken.None);
        await store.DeleteAsync(lease, CancellationToken.None);

        Assert.False(File.Exists(path));
    }

    [Theory]
    [InlineData(InvalidLeaseDocument.SchemaVersionString)]
    [InlineData(InvalidLeaseDocument.SchemaVersionFraction)]
    [InlineData(InvalidLeaseDocument.UnknownModeWithValue)]
    [InlineData(InvalidLeaseDocument.UnknownStreamingWithValue)]
    [InlineData(InvalidLeaseDocument.InvalidCreationTime)]
    [InlineData(InvalidLeaseDocument.NonUtcCreationTime)]
    [InlineData(InvalidLeaseDocument.NumericDefinedMode)]
    [InlineData(InvalidLeaseDocument.BlankSessionId)]
    [InlineData(InvalidLeaseDocument.NonStringSessionId)]
    [InlineData(InvalidLeaseDocument.NonBooleanChangedMode)]
    [InlineData(InvalidLeaseDocument.NestedArrayDuplicate)]
    [InlineData(InvalidLeaseDocument.NestedArrayWithoutDuplicate)]
    public async Task InvalidFieldShapesFailClosed(
        InvalidLeaseDocument invalidDocument)
    {
        using var directory = TemporaryDirectory.Create();
        var path = Path.Combine(directory.Path, "camera-lease.json");
        using var store = new FileSystemCameraLeaseStore(path);
        var usesUnknownState = invalidDocument is
            InvalidLeaseDocument.UnknownModeWithValue or
            InvalidLeaseDocument.UnknownStreamingWithValue;
        var lease = usesUnknownState
            ? new CameraLease(
                "session-a",
                "service-a",
                1234,
                new DateTimeOffset(2026, 7, 10, 0, 0, 0, TimeSpan.Zero),
                ObservedCameraValue.Unknown<CameraMode>(),
                ObservedCameraValue.Unknown<bool>(),
                changedModeByRecorder: false,
                changedStreamingByRecorder: false)
            : Lease("session-a", "service-a", processId: 1234);
        await store.SaveAsync(lease, CancellationToken.None);
        var valid = await File.ReadAllTextAsync(path);
        var invalid = invalidDocument switch
        {
            InvalidLeaseDocument.SchemaVersionString => valid.Replace(
                "\"schemaVersion\": 1",
                "\"schemaVersion\": \"1\"",
                StringComparison.Ordinal),
            InvalidLeaseDocument.SchemaVersionFraction => valid.Replace(
                "\"schemaVersion\": 1",
                "\"schemaVersion\": 1.5",
                StringComparison.Ordinal),
            InvalidLeaseDocument.UnknownModeWithValue => valid.Replace(
                "\"previousMode\": null",
                "\"previousMode\": \"Photo\"",
                StringComparison.Ordinal),
            InvalidLeaseDocument.UnknownStreamingWithValue => valid.Replace(
                "\"previousStreaming\": null",
                "\"previousStreaming\": true",
                StringComparison.Ordinal),
            InvalidLeaseDocument.InvalidCreationTime => valid.Replace(
                "2026-07-10T00:00:00.0000000\\u002B00:00",
                "invalid",
                StringComparison.Ordinal),
            InvalidLeaseDocument.NonUtcCreationTime => valid.Replace(
                "2026-07-10T00:00:00.0000000\\u002B00:00",
                "2026-07-10T09:00:00.0000000\\u002B09:00",
                StringComparison.Ordinal),
            InvalidLeaseDocument.NumericDefinedMode => valid.Replace(
                "\"previousMode\": \"Photo\"",
                "\"previousMode\": \"1\"",
                StringComparison.Ordinal),
            InvalidLeaseDocument.BlankSessionId => valid.Replace(
                "\"sessionId\": \"session-a\"",
                "\"sessionId\": \" \"",
                StringComparison.Ordinal),
            InvalidLeaseDocument.NonStringSessionId => valid.Replace(
                "\"sessionId\": \"session-a\"",
                "\"sessionId\": 1",
                StringComparison.Ordinal),
            InvalidLeaseDocument.NonBooleanChangedMode => valid.Replace(
                "\"changedModeByRecorder\": true",
                "\"changedModeByRecorder\": null",
                StringComparison.Ordinal),
            InvalidLeaseDocument.NestedArrayDuplicate => valid.Replace(
                "\"previousMode\": \"Photo\"",
                "\"previousMode\": [{\"item\":1,\"item\":2}]",
                StringComparison.Ordinal),
            InvalidLeaseDocument.NestedArrayWithoutDuplicate => valid.Replace(
                "\"previousMode\": \"Photo\"",
                "\"previousMode\": [{\"item\":1}]",
                StringComparison.Ordinal),
            _ => throw new InvalidOperationException(
                "The invalid lease document case is not supported."),
        };
        await File.WriteAllTextAsync(path, invalid);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            store.LoadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task InvalidUtf8FailsClosed()
    {
        using var directory = TemporaryDirectory.Create();
        var path = Path.Combine(directory.Path, "camera-lease.json");
        await File.WriteAllBytesAsync(path, [0xff]);
        using var store = new FileSystemCameraLeaseStore(path);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            store.LoadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task LinkedParentDirectoryIsRejected()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var directory = TemporaryDirectory.Create();
        var target = Path.Combine(directory.Path, "target");
        var link = Path.Combine(directory.Path, "linked-parent");
        Directory.CreateDirectory(target);
        Directory.CreateSymbolicLink(link, target);
        using var store = new FileSystemCameraLeaseStore(
            Path.Combine(link, "camera-lease.json"));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            store.SaveAsync(
                Lease("session-a", "service-a", processId: 1234),
                CancellationToken.None));
        Assert.Empty(Directory.EnumerateFileSystemEntries(target));
    }

    [Fact]
    public async Task RejectsRelativePathInvalidLeaseAndDisposedUse()
    {
        Assert.Throws<ArgumentException>(() =>
            new FileSystemCameraLeaseStore("camera-lease.json"));

        using var directory = TemporaryDirectory.Create();
        var path = Path.Combine(directory.Path, "camera-lease.json");
        var store = new FileSystemCameraLeaseStore(path);
        var nonPersistent = new CameraLease(
            ObservedCameraValue.Unknown<bool>(),
            changedStreamingByRecorder: false);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.SaveAsync(nonPersistent, CancellationToken.None));
        var undefinedMode = new CameraLease(
            "session-a",
            "service-a",
            1234,
            new DateTimeOffset(2026, 7, 10, 0, 0, 0, TimeSpan.Zero),
            ObservedCameraValue.Known((CameraMode)(-1)),
            ObservedCameraValue.Known(false),
            changedModeByRecorder: true,
            changedStreamingByRecorder: true);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.SaveAsync(undefinedMode, CancellationToken.None));

        store.Dispose();
        store.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            store.LoadAsync(CancellationToken.None));
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            store.SaveAsync(
                Lease("session-a", "service-a", processId: 1234),
                CancellationToken.None));
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            store.DeleteAsync(
                Lease("session-a", "service-a", processId: 1234),
                CancellationToken.None));
    }

    public enum InvalidLeaseDocument
    {
        SchemaVersionString,
        SchemaVersionFraction,
        UnknownModeWithValue,
        UnknownStreamingWithValue,
        InvalidCreationTime,
        NonUtcCreationTime,
        NumericDefinedMode,
        BlankSessionId,
        NonStringSessionId,
        NonBooleanChangedMode,
        NestedArrayDuplicate,
        NestedArrayWithoutDuplicate,
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
