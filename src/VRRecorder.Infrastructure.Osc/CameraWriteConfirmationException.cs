namespace VRRecorder.Infrastructure.Osc;

public sealed class CameraWriteConfirmationException : TimeoutException
{
    public CameraWriteConfirmationException()
        : base("VRChat did not confirm the OSC camera write after two attempts.")
    {
    }
}
