using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using VRRecorder.Domain.Audio;
using VRRecorder.Infrastructure.Media;

namespace VRRecorder.IntegrationTests.Media;

[SupportedOSPlatform("windows")]
public sealed class ShellWindowsAudioEndpointApiTests
{
    [Fact]
    public void NonWindowsPlatformFailsBeforeComActivation()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var api = new ShellWindowsAudioEndpointApi();

        Assert.Throws<PlatformNotSupportedException>(() =>
            api.EnumerateActive(AudioInput.Desktop));
    }

    [Fact]
    public void MapsEveryAudioInputToCoreAudioDataFlow()
    {
        Assert.Equal(
            0,
            ShellWindowsAudioEndpointApi.DataFlow(AudioInput.Desktop));
        Assert.Equal(
            1,
            ShellWindowsAudioEndpointApi.DataFlow(AudioInput.Microphone));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ShellWindowsAudioEndpointApi.DataFlow((AudioInput)(-1)));
    }

    [Fact]
    public void DecodesFriendlyNamePropVariant()
    {
        var pointer = Marshal.StringToCoTaskMemUni("Microphone (USB Audio)");
        try
        {
            var endpoint = ShellWindowsAudioEndpointApi.CreateEndpoint(
                "endpoint-id",
                valueType: 31,
                pointer);

            Assert.Equal("endpoint-id", endpoint.Id);
            Assert.Equal("Microphone (USB Audio)", endpoint.DisplayName);
        }
        finally
        {
            Marshal.FreeCoTaskMem(pointer);
        }
    }

    [Theory]
    [InlineData((ushort)0, false)]
    [InlineData((ushort)31, true)]
    public void RejectsMissingFriendlyNamePropVariant(
        ushort valueType,
        bool nullPointer)
    {
        var pointer = nullPointer
            ? 0
            : Marshal.StringToCoTaskMemUni("wrong variant type");
        try
        {
            Assert.Throws<InvalidDataException>(() =>
                ShellWindowsAudioEndpointApi.CreateEndpoint(
                    "endpoint-id",
                    valueType,
                    pointer));
        }
        finally
        {
            if (pointer != 0)
            {
                Marshal.FreeCoTaskMem(pointer);
            }
        }
    }

    [Fact]
    public void ReleaseIgnoresNullAndManagedObjects()
    {
        ShellWindowsAudioEndpointApi.Release(null);
        ShellWindowsAudioEndpointApi.Release(new object());
    }
}
