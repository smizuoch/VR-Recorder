using VRRecorder.Application.Settings;

namespace VRRecorder.Application.Recording;

public sealed class UnsupportedResolutionChangePolicyException
    : NotSupportedException
{
    public UnsupportedResolutionChangePolicyException(
        ResolutionChangePolicy policy)
        : base(
            $"Resolution change policy {policy} is not implemented by the " +
            "recording video layout session.")
    {
        Policy = policy;
    }

    public ResolutionChangePolicy Policy { get; }
}
