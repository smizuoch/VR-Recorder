using VRRecorder.Application.Haptics;

namespace VRRecorder.Application.Ports;

public interface IWristHapticOutput
{
    Task PlayAsync(
        WristHapticPattern pattern,
        CancellationToken cancellationToken);
}
