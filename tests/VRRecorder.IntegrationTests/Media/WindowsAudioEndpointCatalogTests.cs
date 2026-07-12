using VRRecorder.Domain.Audio;
using VRRecorder.Infrastructure.Media;

namespace VRRecorder.IntegrationTests.Media;

public sealed class WindowsAudioEndpointCatalogTests
{
    [Fact]
    public async Task ReturnsRoleSpecificActiveEndpointsDeterministically()
    {
        var api = new StubWindowsAudioEndpointApi
        {
            Desktop =
            [
                new WindowsAudioEndpoint("render-b", "Speakers B"),
                new WindowsAudioEndpoint("render-a", "Speakers A"),
            ],
            Microphone =
            [new WindowsAudioEndpoint("capture-a", "Studio microphone")],
        };
        var catalog = new WindowsAudioEndpointCatalog(api);

        var desktop = await catalog.GetActiveAsync(
            AudioInput.Desktop,
            CancellationToken.None);
        var microphone = await catalog.GetActiveAsync(
            AudioInput.Microphone,
            CancellationToken.None);

        Assert.Equal(["render-a", "render-b"], desktop.Select(x => x.Id));
        Assert.Equal(["capture-a"], microphone.Select(x => x.Id));
        Assert.Equal(
            [AudioInput.Desktop, AudioInput.Microphone],
            api.Requests);
    }

    private sealed class StubWindowsAudioEndpointApi
        : IWindowsAudioEndpointApi
    {
        public IReadOnlyList<WindowsAudioEndpoint> Desktop { get; init; } = [];

        public IReadOnlyList<WindowsAudioEndpoint> Microphone { get; init; } = [];

        public List<AudioInput> Requests { get; } = [];

        public IReadOnlyList<WindowsAudioEndpoint> EnumerateActive(
            AudioInput input)
        {
            Requests.Add(input);
            return input == AudioInput.Desktop ? Desktop : Microphone;
        }
    }
}
