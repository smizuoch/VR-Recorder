namespace VRRecorder.Application.Camera;

public sealed record VrChatInstanceCandidate
{
    public VrChatInstanceCandidate(
        string serviceId,
        string displayName,
        Uri oscQueryEndpoint,
        string oscHost,
        int oscPort)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(oscQueryEndpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(oscHost);
        if (!oscQueryEndpoint.IsAbsoluteUri)
        {
            throw new ArgumentException(
                "The OSCQuery endpoint must be absolute.",
                nameof(oscQueryEndpoint));
        }

        if (oscPort is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(
                nameof(oscPort),
                oscPort,
                "The OSC port must be between 1 and 65535.");
        }

        ServiceId = serviceId;
        DisplayName = displayName;
        OscQueryEndpoint = oscQueryEndpoint;
        OscHost = oscHost;
        OscPort = oscPort;
    }

    public string ServiceId { get; }

    public string DisplayName { get; }

    public Uri OscQueryEndpoint { get; }

    public string OscHost { get; }

    public int OscPort { get; }
}
