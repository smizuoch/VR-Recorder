using VRRecorder.Application.Recording;
using VRRecorder.Application.Presentation;
using VRRecorder.Domain.Recording;

namespace VRRecorder.Application.Ports;

public interface IRecordingLifecycleController
    : IDisposable,
      IRecorderStatusSource
{
    RecorderState State { get; }

    Task<RecordingLifecycleStartResult> StartAsync(
        string? selectedServiceId,
        StartRecordingCommand command,
        CancellationToken cancellationToken);
}
