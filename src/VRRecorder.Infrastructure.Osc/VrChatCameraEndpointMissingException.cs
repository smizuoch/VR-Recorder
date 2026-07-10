namespace VRRecorder.Infrastructure.Osc;

public sealed class VrChatCameraEndpointMissingException : Exception
{
    public VrChatCameraEndpointMissingException(
        string serviceId,
        string endpointPath,
        Exception innerException)
        : base(
            $"VRChat service {serviceId} does not expose required camera endpoint {endpointPath}.",
            innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointPath);
        ServiceId = serviceId;
        EndpointPath = endpointPath;
    }

    public string ServiceId { get; }

    public string EndpointPath { get; }
}
