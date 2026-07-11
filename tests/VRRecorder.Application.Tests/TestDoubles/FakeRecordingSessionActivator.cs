using VRRecorder.Application.Ports;
using VRRecorder.Application.Recording;
using VRRecorder.Domain.Audio;

namespace VRRecorder.Application.Tests.TestDoubles;

internal sealed class FakeRecordingSessionActivator
    : IRecordingSessionActivator
{
    public List<RecordingHandle> Handles { get; } = [];

    public List<AudioRouting> AudioRoutings { get; } = [];

    public void Activate(
        RecordingHandle handle,
        CancellationToken sessionLifetimeToken,
        IRecordingSessionCompletionSink? completionSink = null) =>
        Handles.Add(handle);

    public void Activate(
        RecordingHandle handle,
        AudioRouting initialAudioRouting,
        CancellationToken sessionLifetimeToken,
        IRecordingSessionCompletionSink? completionSink = null)
    {
        Handles.Add(handle);
        AudioRoutings.Add(initialAudioRouting);
    }
}
