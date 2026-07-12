using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using VRRecorder.Domain.Audio;

namespace VRRecorder.Infrastructure.Media;

[SupportedOSPlatform("windows")]
public sealed class ShellWindowsAudioEndpointApi : IWindowsAudioEndpointApi
{
    private const uint DeviceStateActive = 1;
    private const uint StorageRead = 0;
    private static readonly Guid EnumeratorClassId = new(
        "BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly PropertyKey FriendlyNameKey = new(
        new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"),
        14);

    public IReadOnlyList<WindowsAudioEndpoint> EnumerateActive(AudioInput input)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Windows Core Audio endpoint enumeration requires Windows.");
        }

        if (!Enum.IsDefined(input))
        {
            throw new ArgumentOutOfRangeException(nameof(input));
        }

        var type = Type.GetTypeFromCLSID(EnumeratorClassId, throwOnError: true)!;
        var enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(type)!;
        IMMDeviceCollection? collection = null;
        try
        {
            Marshal.ThrowExceptionForHR(enumerator.EnumAudioEndpoints(
                input == AudioInput.Desktop ? 0 : 1,
                DeviceStateActive,
                out collection));
            Marshal.ThrowExceptionForHR(collection.GetCount(out var count));
            var endpoints = new List<WindowsAudioEndpoint>(checked((int)count));
            for (uint index = 0; index < count; index++)
            {
                Marshal.ThrowExceptionForHR(collection.Item(index, out var device));
                try
                {
                    endpoints.Add(Read(device));
                }
                finally
                {
                    Release(device);
                }
            }

            return endpoints;
        }
        finally
        {
            Release(collection);
            Release(enumerator);
        }
    }

    private static WindowsAudioEndpoint Read(IMMDevice device)
    {
        Marshal.ThrowExceptionForHR(device.GetId(out var id));
        Marshal.ThrowExceptionForHR(device.OpenPropertyStore(
            StorageRead,
            out var properties));
        try
        {
            var key = FriendlyNameKey;
            Marshal.ThrowExceptionForHR(properties.GetValue(ref key, out var value));
            try
            {
                if (value.Type != 31 || value.Pointer == 0)
                {
                    throw new InvalidDataException(
                        "The Windows audio endpoint has no friendly name.");
                }

                return new WindowsAudioEndpoint(
                    id,
                    Marshal.PtrToStringUni(value.Pointer) ?? string.Empty);
            }
            finally
            {
                _ = PropVariantClear(ref value);
            }
        }
        finally
        {
            Release(properties);
        }
    }

    private static void Release(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            _ = Marshal.FinalReleaseComObject(value);
        }
    }

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant value);

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey(Guid formatId, uint propertyId)
    {
        public Guid FormatId = formatId;
        public uint PropertyId = propertyId;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct PropVariant
    {
        [FieldOffset(0)]
        public ushort Type;

        [FieldOffset(8)]
        public nint Pointer;
    }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig]
        int EnumAudioEndpoints(
            int dataFlow,
            uint stateMask,
            out IMMDeviceCollection devices);
    }

    [ComImport]
    [Guid("0BD7A1BE-7A1A-44DB-8397-C0A7B843D8A0")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        [PreserveSig]
        int GetCount(out uint count);

        [PreserveSig]
        int Item(uint index, out IMMDevice device);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig]
        int Activate(ref Guid id, uint context, nint parameters, out nint value);

        [PreserveSig]
        int OpenPropertyStore(uint access, out IPropertyStore properties);

        [PreserveSig]
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    }

    [ComImport]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig]
        int GetCount(out uint count);

        [PreserveSig]
        int GetAt(uint index, out PropertyKey key);

        [PreserveSig]
        int GetValue(ref PropertyKey key, out PropVariant value);
    }
}
