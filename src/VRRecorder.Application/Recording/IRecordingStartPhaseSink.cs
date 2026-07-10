namespace VRRecorder.Application.Recording;

internal interface IRecordingStartPhaseSink
{
    void CountdownStarted();

    void StartPreparationCompleted();
}
