using VRRecorder.Application.Setup;
using VRRecorder.Infrastructure.SteamVr;

namespace VRRecorder.IntegrationTests.SteamVr;

public sealed class WindowsSteamVrInstallationProbeTests
{
    [Fact]
    public async Task InstalledMarkerVerifiesSteamVrDetection()
    {
        var registration = new StubSteamVrRegistrationReader([null, 0, 1]);
        var probe = new WindowsSteamVrInstallationProbe(registration);

        var verified = await probe.VerifyAsync(
            FirstRunSetupStep.SteamVrDetection,
            CancellationToken.None);

        Assert.True(verified);
        Assert.Equal(1, registration.ReadCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(2)]
    public async Task MissingOrInvalidInstalledMarkerDoesNotVerify(
        int? marker)
    {
        var probe = new WindowsSteamVrInstallationProbe(
            new StubSteamVrRegistrationReader([marker]));

        Assert.False(await probe.VerifyAsync(
            FirstRunSetupStep.SteamVrDetection,
            CancellationToken.None));
    }

    [Fact]
    public async Task ProbeNeverClaimsAnUnimplementedSetupStep()
    {
        var registration = new StubSteamVrRegistrationReader([1]);
        var probe = new WindowsSteamVrInstallationProbe(registration);

        var verified = await probe.VerifyAsync(
            FirstRunSetupStep.VrChatOscDetection,
            CancellationToken.None);

        Assert.False(verified);
        Assert.Equal(0, registration.ReadCount);
    }

    private sealed class StubSteamVrRegistrationReader(
        IReadOnlyList<int?> installedMarkers)
        : ISteamVrRegistrationReader
    {
        public int ReadCount { get; private set; }

        public IReadOnlyList<int?> ReadInstalledMarkers()
        {
            ReadCount++;
            return installedMarkers;
        }
    }
}
