namespace VRRecorder.Application.Storage;

public sealed record RecordingMediaExpectation(
    int Width,
    int Height,
    int FramesPerSecond,
    int AudioSampleRate,
    int AudioChannels,
    TimeSpan? ExpectedDuration);
