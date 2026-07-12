using System.Runtime.Versioning;
using System.Security;
using Microsoft.Win32;

namespace VRRecorder.Infrastructure.SteamVr;

public sealed class WindowsSteamVrRegistrationReader
    : ISteamVrRegistrationReader
{
    private const string SteamVrRegistrationPath =
        @"Software\Valve\Steam\Apps\250820";

    public IReadOnlyList<int?> ReadInstalledMarkers()
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }

        return
        [
            ReadMarker(RegistryHive.CurrentUser, RegistryView.Default),
            ReadMarker(RegistryHive.LocalMachine, RegistryView.Registry32),
            ReadMarker(RegistryHive.LocalMachine, RegistryView.Registry64),
        ];
    }

    [SupportedOSPlatform("windows")]
    private static int? ReadMarker(
        RegistryHive hive,
        RegistryView view)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var appKey = baseKey.OpenSubKey(
                SteamVrRegistrationPath,
                writable: false);
            return appKey?.GetValue(
                "Installed",
                defaultValue: null,
                RegistryValueOptions.DoNotExpandEnvironmentNames) as int?;
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or SecurityException or
                IOException)
        {
            return null;
        }
    }
}
