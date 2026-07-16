using VRRecorder.Application.Presentation;
using VRRecorder.Application.Storage;

namespace VRRecorder.Application.Ports;

public interface ITestRecordingPlaybackRuntime
{
    RecorderStatusSnapshot Current { get; }

    Task StartAsync(CancellationToken cancellationToken);

    Task StopAsync(CancellationToken cancellationToken);

    IDisposable SubscribeSaved(Action<FinalizedRecording> subscriber);
}
