using System.Runtime.Versioning;
using System.Security;
using Microsoft.Win32;

namespace VRRecorder.Infrastructure.Media;

public sealed class WindowsMicrophonePrivacyRegistrationReader
    : IMicrophonePrivacyRegistrationReader
{
    private const string ConsentPath =
        @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone";

    public string? ReadConsentValue()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        return ReadWindowsConsentValue();
    }

    [SupportedOSPlatform("windows")]
    private static string? ReadWindowsConsentValue()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                ConsentPath,
                writable: false);
            return key?.GetValue(
                "Value",
                defaultValue: null,
                RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or SecurityException or
                IOException)
        {
            return null;
        }
    }
}
