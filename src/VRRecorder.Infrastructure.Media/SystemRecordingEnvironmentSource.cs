using System.Reflection;
using System.Runtime.InteropServices;
using VRRecorder.Application.Recording;

namespace VRRecorder.Infrastructure.Media;

public sealed class SystemRecordingEnvironmentSource
    : IRecordingEnvironmentSource
{
    private readonly string _appVersion;
    private readonly string _osBuild;
    private readonly RecordingProcessArchitecture _architecture;

    public SystemRecordingEnvironmentSource(
        string appVersion,
        string osBuild,
        RecordingProcessArchitecture architecture)
    {
        _appVersion = appVersion;
        _osBuild = osBuild;
        _architecture = architecture;
    }

    public static SystemRecordingEnvironmentSource ForCurrentProcess()
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version ??
                      Assembly.GetExecutingAssembly().GetName().Version ??
                      new Version(0, 0, 0, 0);
        var architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => RecordingProcessArchitecture.X64,
            Architecture.Arm64 => RecordingProcessArchitecture.Arm64,
            var unsupported => throw new PlatformNotSupportedException(
                $"Unsupported process architecture: {unsupported}"),
        };
        return new SystemRecordingEnvironmentSource(
            version.ToString(Math.Max(3, version.Revision >= 0 ? 4 : 3)),
            Environment.OSVersion.Version.ToString(
                Environment.OSVersion.Version.Revision >= 0 ? 4 : 3),
            architecture);
    }

    public RecordingEnvironmentSnapshot Capture(StableVideoSignal signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        var parts = signal.GpuIdentity.Split('|');
        var hardware = parts[0].ToLowerInvariant();
        const string prefix = "pci\\";
        if (hardware.StartsWith(prefix, StringComparison.Ordinal))
        {
            hardware = hardware[prefix.Length..];
        }

        var hardwareParts = hardware.Split('&');
        if (hardwareParts.Length < 2 ||
            !IsHardwarePart(hardwareParts[0], "ven_") ||
            !IsHardwarePart(hardwareParts[1], "dev_"))
        {
            throw new InvalidDataException(
                "The GPU identity has no canonical PCI vendor/device ID.");
        }

        var driverPart = parts.FirstOrDefault(part =>
            part.StartsWith("driver-", StringComparison.OrdinalIgnoreCase));
        if (driverPart is null)
        {
            throw new InvalidDataException(
                "The GPU identity has no driver version.");
        }

        return new RecordingEnvironmentSnapshot(
            _appVersion,
            _osBuild,
            _architecture,
            $"{hardwareParts[0]}&{hardwareParts[1]}",
            signal.GpuVendor,
            driverPart["driver-".Length..]);
    }

    private static bool IsHardwarePart(string value, string prefix) =>
        value.Length == prefix.Length + 4 &&
        value.StartsWith(prefix, StringComparison.Ordinal) &&
        value[prefix.Length..].All(character =>
            char.IsAsciiHexDigit(character));
}
