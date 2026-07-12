using System.Globalization;
using VRRecorder.Domain.Encoding;

namespace VRRecorder.Application.Recording;

public sealed record RecordingEnvironmentSnapshot
{
    public RecordingEnvironmentSnapshot(
        string appVersion,
        string osBuild,
        RecordingProcessArchitecture architecture,
        string gpuModel,
        GpuVendor gpuVendor,
        string driverVersion)
    {
        EnsureNumericVersion(appVersion, 3, 4, nameof(appVersion));
        EnsureNumericVersion(osBuild, 3, 4, nameof(osBuild));
        EnsureNumericVersion(driverVersion, 4, 4, nameof(driverVersion));
        if (!Enum.IsDefined(architecture))
        {
            throw new ArgumentOutOfRangeException(nameof(architecture));
        }

        if (!Enum.IsDefined(gpuVendor))
        {
            throw new ArgumentOutOfRangeException(nameof(gpuVendor));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(gpuModel);
        if (gpuModel.Length > 64 || gpuModel.Any(character =>
                !char.IsAsciiLetterOrDigit(character) &&
                character is not '_' and not '&'))
        {
            throw new ArgumentException(
                "The GPU model must be a canonical hardware identifier.",
                nameof(gpuModel));
        }

        AppVersion = appVersion;
        OsBuild = osBuild;
        Architecture = architecture;
        GpuModel = gpuModel;
        GpuVendor = gpuVendor;
        DriverVersion = driverVersion;
    }

    public string AppVersion { get; }

    public string OsBuild { get; }

    public RecordingProcessArchitecture Architecture { get; }

    public string GpuModel { get; }

    public GpuVendor GpuVendor { get; }

    public string DriverVersion { get; }

    private static void EnsureNumericVersion(
        string value,
        int minimumComponents,
        int maximumComponents,
        string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        var components = value.Split('.');
        if (components.Length < minimumComponents ||
            components.Length > maximumComponents ||
            components.Any(component =>
                component.Length == 0 ||
                (component.Length > 1 && component[0] == '0') ||
                !uint.TryParse(
                    component,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out _)))
        {
            throw new ArgumentException(
                "The value must be a canonical numeric version.",
                parameterName);
        }
    }
}
