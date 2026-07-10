using VRRecorder.Domain.Storage;

namespace VRRecorder.Application.Ports;

public interface IDefaultOutputPathProvider
{
    OutputPath GetDefault();
}
