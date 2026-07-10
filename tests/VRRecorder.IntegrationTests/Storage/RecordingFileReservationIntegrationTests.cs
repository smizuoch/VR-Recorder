using VRRecorder.Domain.Storage;
using VRRecorder.Domain.Video;
using VRRecorder.Infrastructure.Storage;

namespace VRRecorder.IntegrationTests.Storage;

public sealed class RecordingFileReservationIntegrationTests
{
    [Fact]
    public async Task ConcurrentReservationsCreateDistinctPairedTemporaryNames()
    {
        using var directory = TemporaryDirectory.Create();
        var outputPath = new OutputPath(directory.Path);
        var descriptor = new RecordingFileDescriptor(
            new RecordingSessionTimestamp(new DateTimeOffset(
                2026,
                7,
                10,
                12,
                34,
                56,
                TimeSpan.Zero)),
            Width: 1920,
            Height: 1080,
            FrameRate: new FrameRate(30),
            SegmentNumber: 1);
        var firstNames = RecordingFileNamePolicy.Create(
            descriptor,
            collisionOrdinal: 1);
        await File.WriteAllTextAsync(
            Path.Combine(directory.Path, firstNames.FinalFileName),
            "existing");
        var reservation = new FileSystemRecordingFileReservation();

        var pendingRecordings = await Task.WhenAll(
            Enumerable.Range(0, 4).Select(_ => Task.Run(
                () => reservation.ReserveAsync(
                    outputPath,
                    descriptor,
                    CancellationToken.None))));

        var expectedFinalPaths = Enumerable.Range(2, 4)
            .Select(ordinal => Path.Combine(
                directory.Path,
                RecordingFileNamePolicy.Create(descriptor, ordinal)
                    .FinalFileName))
            .Order(StringComparer.Ordinal)
            .ToArray();
        var expectedTemporaryPaths = Enumerable.Range(2, 4)
            .Select(ordinal => Path.Combine(
                directory.Path,
                RecordingFileNamePolicy.Create(descriptor, ordinal)
                    .TemporaryFileName))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            expectedFinalPaths,
            pendingRecordings
                .Select(recording => recording.FinalPath)
                .Order(StringComparer.Ordinal));
        Assert.Equal(
            expectedTemporaryPaths,
            pendingRecordings
                .Select(recording => recording.TemporaryPath)
                .Order(StringComparer.Ordinal));
        Assert.All(
            pendingRecordings,
            recording =>
            {
                Assert.True(File.Exists(recording.TemporaryPath));
                Assert.False(File.Exists(recording.FinalPath));
                Assert.Equal(
                    Path.GetDirectoryName(recording.FinalPath),
                    Path.GetDirectoryName(recording.TemporaryPath));
            });
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
                $"vr-recorder-reservation-tests-{Guid.NewGuid():N}");
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
