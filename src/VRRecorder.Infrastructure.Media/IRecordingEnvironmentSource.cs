using VRRecorder.Application.Recording;

namespace VRRecorder.Infrastructure.Media;

public interface IRecordingEnvironmentSource
{
    RecordingEnvironmentSnapshot Capture(StableVideoSignal signal);
}
