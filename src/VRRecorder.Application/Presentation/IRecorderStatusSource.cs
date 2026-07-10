namespace VRRecorder.Application.Presentation;

public interface IRecorderStatusSource
{
    RecorderStatusSnapshot Current { get; }

    IDisposable Subscribe(Action<RecorderStatusSnapshot> subscriber);
}
