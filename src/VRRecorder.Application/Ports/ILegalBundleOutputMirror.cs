using VRRecorder.Domain.Storage;

namespace VRRecorder.Application.Ports;

public interface ILegalBundleOutputMirror
{
    Task MirrorAsync(
        OutputPath outputPath,
        CancellationToken cancellationToken);
}
