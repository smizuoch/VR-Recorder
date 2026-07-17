using VRRecorder.Application.Camera;

namespace VRRecorder.Application.Tests.Camera;

public sealed class VrChatInstanceCandidateTests
{
    [Fact]
    public void PreservesValidatedDiscoveryIdentity()
    {
        var endpoint = new Uri("http://127.0.0.1:9010/");
        var candidate = new VrChatInstanceCandidate(
            "service-id",
            "VRChat",
            endpoint,
            "127.0.0.1",
            9000);

        Assert.Equal("service-id", candidate.ServiceId);
        Assert.Equal("VRChat", candidate.DisplayName);
        Assert.Same(endpoint, candidate.OscQueryEndpoint);
        Assert.Equal("127.0.0.1", candidate.OscHost);
        Assert.Equal(9000, candidate.OscPort);
    }

    [Fact]
    public void RejectsMissingRelativeAndOutOfRangeDiscoveryValues()
    {
        Assert.Throws<ArgumentException>(() => Create(serviceId: " "));
        Assert.Throws<ArgumentException>(() => Create(displayName: " "));
        Assert.Throws<ArgumentNullException>(() =>
            new VrChatInstanceCandidate(
                "service-id",
                "VRChat",
                null!,
                "127.0.0.1",
                9000));
        Assert.Throws<ArgumentException>(() => Create(oscHost: " "));
        Assert.Throws<ArgumentException>(() =>
            Create(endpoint: new Uri("relative", UriKind.Relative)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(oscPort: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(oscPort: 65_536));
    }

    private static VrChatInstanceCandidate Create(
        string serviceId = "service-id",
        string displayName = "VRChat",
        Uri? endpoint = null,
        string oscHost = "127.0.0.1",
        int oscPort = 9000) =>
        new(
            serviceId,
            displayName,
            endpoint ?? new Uri("http://127.0.0.1:9010/"),
            oscHost,
            oscPort);
}
