using System.Runtime.InteropServices;
using VRRecorder.Application.Recording;
using VRRecorder.Application.Storage;
using VRRecorder.Domain.Storage;
using VRRecorder.Domain.Video;
using VRRecorder.Infrastructure.Media;

namespace VRRecorder.IntegrationTests.Media;

public sealed class PInvokeNativeRecordingBackendTests
{
    [Fact]
    public async Task ThrowingManagedFaultCallbackStillFaultsPendingStop()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var directory = TemporaryDirectory.Create();
        var pending = new PendingRecording(
            Path.Combine(directory.Path, "take.recording.mp4"),
            Path.Combine(directory.Path, "take.mp4"));
        var plan = new RecordingPlan(
            new StableVideoSignal(320, 180),
            pending,
            new RecordingSessionTimestamp(new DateTimeOffset(
                2026,
                7,
                10,
                12,
                34,
                56,
                TimeSpan.Zero)),
            new FrameRate(30));
        using var backend = new PInvokeNativeRecordingBackend(FixturePath());
        using var controls = new NativeFixtureControls(FixturePath());
        var session = await backend.OpenAsync(
            plan,
            new NativeRecordingCallbacks(
                () => { },
                _ => throw new InvalidOperationException(
                    "The managed fault callback failed.")),
            CancellationToken.None);
        var stopping = session.StopAsync(CancellationToken.None);

        controls.Fail(
            status: 6,
            message: "encoder failed while stopping");

        var completed = await Task.WhenAny(
            stopping,
            Task.Delay(TimeSpan.FromMilliseconds(250)));
        if (!ReferenceEquals(stopping, completed))
        {
            await session.AbortAsync(CancellationToken.None);
        }

        Assert.Same(stopping, completed);
        var exception = await Assert.ThrowsAsync<NativeRecordingException>(
            () => stopping);
        Assert.Equal(6, exception.Fault.Status);
        Assert.Equal("encoder failed while stopping", exception.Fault.Message);
    }

    [Fact]
    public async Task StopCompletesOnlyAfterNativeStoppedEventWithPacketCounts()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var directory = TemporaryDirectory.Create();
        var pending = new PendingRecording(
            Path.Combine(directory.Path, "take.recording.mp4"),
            Path.Combine(directory.Path, "take.mp4"));
        var plan = new RecordingPlan(
            new StableVideoSignal(320, 180),
            pending,
            new RecordingSessionTimestamp(new DateTimeOffset(
                2026,
                7,
                10,
                12,
                34,
                56,
                TimeSpan.Zero)),
            new FrameRate(30));
        var firstPacket = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var unexpectedFault = new TaskCompletionSource<NativeRecordingFault>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var backend = new PInvokeNativeRecordingBackend(FixturePath());
        using var controls = new NativeFixtureControls(FixturePath());
        var session = await backend.OpenAsync(
            plan,
            new NativeRecordingCallbacks(
                () => firstPacket.TrySetResult(),
                fault => unexpectedFault.TrySetResult(fault)),
            CancellationToken.None);

        Assert.False(firstPacket.Task.IsCompleted);
        controls.CommitMuxedVideoPacket();
        await firstPacket.Task;

        var stopping = session.StopAsync(CancellationToken.None);
        Assert.False(stopping.IsCompleted);
        controls.CompleteTrailerFlushClose(
            videoPacketCount: 90,
            audioPacketCount: 142);
        var stopped = await stopping;

        Assert.Equal(pending, stopped.Recording);
        Assert.Equal(90, stopped.VideoPacketCount);
        Assert.Equal(142, stopped.AudioPacketCount);
        Assert.False(unexpectedFault.Task.IsCompleted);
    }

    private static string FixturePath()
    {
        var root = FindRepositoryRoot();
        return Path.Combine(
            root,
            "tests",
            "VRRecorder.Native.Tests",
            "build",
            "libvrrecorder_native_test.so");
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

        throw new DirectoryNotFoundException(
            "The VR-Recorder repository root was not found.");
    }

    private sealed class NativeFixtureControls : IDisposable
    {
        private readonly nint _library;
        private readonly CommitDelegate _commit;
        private readonly CompleteDelegate _complete;
        private readonly FailDelegate _fail;

        public NativeFixtureControls(string path)
        {
            _library = NativeLibrary.Load(path);
            _commit = Marshal.GetDelegateForFunctionPointer<CommitDelegate>(
                NativeLibrary.GetExport(
                    _library,
                    "vrrec_test_commit_muxed_video_packet"));
            _complete = Marshal.GetDelegateForFunctionPointer<CompleteDelegate>(
                NativeLibrary.GetExport(
                    _library,
                    "vrrec_test_complete_trailer_flush_close"));
            _fail = Marshal.GetDelegateForFunctionPointer<FailDelegate>(
                NativeLibrary.GetExport(
                    _library,
                    "vrrec_test_fail"));
        }

        public void CommitMuxedVideoPacket() => _commit();

        public void CompleteTrailerFlushClose(
            ulong videoPacketCount,
            ulong audioPacketCount) =>
            _complete(videoPacketCount, audioPacketCount);

        public void Fail(int status, string message) => _fail(status, message);

        public void Dispose() => NativeLibrary.Free(_library);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void CommitDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void CompleteDelegate(
            ulong videoPacketCount,
            ulong audioPacketCount);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void FailDelegate(
            int status,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string message);
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
                $"vr-recorder-pinvoke-tests-{Guid.NewGuid():N}");
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
