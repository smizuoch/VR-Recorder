namespace VRRecorder.Infrastructure.Osc;

public sealed class CameraWriteConfirmationException : TimeoutException
{
    public CameraWriteConfirmationException(int attempts)
        : base($"VRChat did not confirm the OSC camera write after {attempts} attempts.")
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(attempts);
        Attempts = attempts;
    }

    public int Attempts { get; }
}
