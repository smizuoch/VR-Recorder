namespace VRRecorder.Application.Recording;

public interface IRecordingStartPhaseSink
{
    void CountdownStarted();

    void StartPreparationCompleted();
}
