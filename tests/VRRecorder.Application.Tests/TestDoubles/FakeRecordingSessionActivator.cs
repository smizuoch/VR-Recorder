using VRRecorder.Application.Ports;
using VRRecorder.Application.Recording;

namespace VRRecorder.Application.Tests.TestDoubles;

internal sealed class FakeRecordingSessionActivator
    : IRecordingSessionActivator
{
    public List<RecordingHandle> Handles { get; } = [];

    public void Activate(
        RecordingHandle handle,
        CancellationToken sessionLifetimeToken,
        IRecordingSessionCompletionSink? completionSink = null) =>
        Handles.Add(handle);
}
