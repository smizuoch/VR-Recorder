using VRRecorder.Infrastructure.Media;

namespace VRRecorder.IntegrationTests.Audio;

public sealed class WindowsMicrophonePrivacyAccessTests
{
    [Theory]
    [InlineData("Allow", true)]
    [InlineData("allow", true)]
    [InlineData("Deny", false)]
    [InlineData("Prompt", false)]
    [InlineData(null, false)]
    public async Task OnlyExplicitAllowRegistrationGrantsPrivacyAccess(
        string? registration,
        bool expected)
    {
        var reader = new StubPrivacyRegistrationReader(registration);
        var access = new WindowsMicrophonePrivacyAccess(reader);

        var allowed = await access.IsAllowedAsync(CancellationToken.None);

        Assert.Equal(expected, allowed);
        Assert.Equal(1, reader.ReadCount);
    }

    private sealed class StubPrivacyRegistrationReader(string? value)
        : IMicrophonePrivacyRegistrationReader
    {
        public int ReadCount { get; private set; }

        public string? ReadConsentValue()
        {
            ReadCount++;
            return value;
        }
    }
}
