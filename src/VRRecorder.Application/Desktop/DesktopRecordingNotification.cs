using VRRecorder.Application.Audio;
using VRRecorder.Application.Camera;
using VRRecorder.Application.Storage;

namespace VRRecorder.Application.Desktop;

public abstract record DesktopRecordingNotification(long Revision)
{
    public sealed record Saved(
        long Revision,
        FinalizedRecording Recording) :
        DesktopRecordingNotification(Revision);

    public sealed record CameraWarning(
        long Revision,
        CameraRestoreWarning Warning) :
        DesktopRecordingNotification(Revision);

    public sealed record AudioWarning(
        long Revision,
        AudioSessionWarning Warning) :
        DesktopRecordingNotification(Revision);

    public sealed record AudioRecovered(
        long Revision,
        AudioSessionStatus Recovery) :
        DesktopRecordingNotification(Revision);
}
