using System.Buffers.Binary;
using System.Text;

namespace VRRecorder.Compliance.Staging;

internal enum WindowsPeSubsystem
{
    Gui = 2,
    Console = 3,
}

internal sealed record WindowsPeImageAdmission(
    bool IsDll,
    bool HasEntryPoint,
    WindowsPeSubsystem Subsystem,
    IReadOnlyList<string> Imports);

internal static class WindowsPeImageAdmissionReader
{
    private const ushort Amd64Machine = 0x8664;
    private const ushort Pe32PlusMagic = 0x020b;
    private const ushort ExecutableImage = 0x0002;
    private const ushort DllImage = 0x2000;
    private const int CoffHeaderSize = 20;
    private const int SectionHeaderSize = 40;
    private const int ImportDescriptorSize = 20;
    private const int DelayImportDescriptorSize = 32;
    private const int MaximumSectionCount = 96;
    private const int MaximumImportCount = 4096;
    private const int MaximumImportNameBytes = 260;

    public static WindowsPeImageAdmission Read(
        string fileName,
        byte[] bytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(bytes);
        try
        {
            return ReadCore(fileName, bytes);
        }
        catch (Exception exception) when (
            exception is not InvalidDataException &&
            exception is ArgumentException or
                OverflowException or
                IndexOutOfRangeException)
        {
            throw Invalid(exception);
        }
    }

    private static WindowsPeImageAdmission ReadCore(
        string fileName,
        byte[] bytes)
    {
        if (!IsCanonicalFileName(fileName) || bytes.Length < 64 ||
            ReadUInt16(bytes, 0) != 0x5a4d)
        {
            throw Invalid();
        }

        var peOffset = ReadInt32(bytes, 0x3c);
        if (peOffset < 64 ||
            !HasRange(bytes, peOffset, 4 + CoffHeaderSize) ||
            ReadUInt32(bytes, peOffset) != 0x0000_4550)
        {
            throw Invalid();
        }

        var coffOffset = checked(peOffset + 4);
        if (ReadUInt16(bytes, coffOffset) != Amd64Machine)
        {
            throw Invalid();
        }

        var sectionCount = ReadUInt16(bytes, coffOffset + 2);
        var optionalSize = ReadUInt16(bytes, coffOffset + 16);
        var characteristics = ReadUInt16(bytes, coffOffset + 18);
        if (sectionCount is 0 or > MaximumSectionCount ||
            (characteristics & ExecutableImage) == 0 ||
            optionalSize < 128)
        {
            throw Invalid();
        }

        var optionalOffset = checked(coffOffset + CoffHeaderSize);
        if (!HasRange(bytes, optionalOffset, optionalSize) ||
            ReadUInt16(bytes, optionalOffset) != Pe32PlusMagic)
        {
            throw Invalid();
        }

        var entryPointRva = ReadUInt32(bytes, optionalOffset + 16);
        var subsystemValue = ReadUInt16(bytes, optionalOffset + 68);
        if (!Enum.IsDefined(typeof(WindowsPeSubsystem), (int)subsystemValue))
        {
            throw Invalid();
        }

        var isDll = (characteristics & DllImage) != 0;
        var extension = Path.GetExtension(fileName);
        if ((string.Equals(extension, ".exe", StringComparison.OrdinalIgnoreCase) &&
             (isDll || entryPointRva == 0)) ||
            (string.Equals(extension, ".dll", StringComparison.OrdinalIgnoreCase) &&
             !isDll) ||
            (!string.Equals(extension, ".exe", StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(extension, ".dll", StringComparison.OrdinalIgnoreCase)))
        {
            throw Invalid();
        }

        var sectionTableOffset = checked(optionalOffset + optionalSize);
        var sectionTableLength = checked(sectionCount * SectionHeaderSize);
        if (!HasRange(bytes, sectionTableOffset, sectionTableLength))
        {
            throw Invalid();
        }

        var sections = new Section[sectionCount];
        for (var index = 0; index < sectionCount; index++)
        {
            var offset = checked(sectionTableOffset + index * SectionHeaderSize);
            var section = new Section(
                ReadUInt32(bytes, offset + 8),
                ReadUInt32(bytes, offset + 12),
                ReadUInt32(bytes, offset + 16),
                ReadUInt32(bytes, offset + 20));
            if (section.RawSize > 0 &&
                (section.RawOffset > int.MaxValue ||
                 section.RawSize > int.MaxValue ||
                 !HasRange(
                     bytes,
                     checked((int)section.RawOffset),
                     checked((int)section.RawSize))))
            {
                throw Invalid();
            }

            _ = checked(section.VirtualAddress +
                Math.Max(section.VirtualSize, section.RawSize));
            sections[index] = section;
        }

        var directoryCount = ReadUInt32(bytes, optionalOffset + 108);
        var imports = new List<string>();
        if (directoryCount > 1)
        {
            ReadImports(
                bytes,
                sections,
                ReadUInt32(bytes, optionalOffset + 120),
                ReadUInt32(bytes, optionalOffset + 124),
                ImportDescriptorSize,
                nameFieldOffset: 12,
                requireRvaAttribute: false,
                imports);
        }

        if (directoryCount > 13 && optionalSize >= 224)
        {
            ReadImports(
                bytes,
                sections,
                ReadUInt32(bytes, optionalOffset + 216),
                ReadUInt32(bytes, optionalOffset + 220),
                DelayImportDescriptorSize,
                nameFieldOffset: 4,
                requireRvaAttribute: true,
                imports);
        }

        var canonicalImports = imports
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ThenBy(value => value, StringComparer.Ordinal)
            .ToArray();
        if (canonicalImports.Length > MaximumImportCount)
        {
            throw Invalid();
        }

        return new WindowsPeImageAdmission(
            isDll,
            entryPointRva != 0,
            (WindowsPeSubsystem)subsystemValue,
            canonicalImports);
    }

    private static void ReadImports(
        byte[] bytes,
        IReadOnlyList<Section> sections,
        uint directoryRva,
        uint directorySize,
        int descriptorSize,
        int nameFieldOffset,
        bool requireRvaAttribute,
        List<string> imports)
    {
        if (directoryRva == 0 && directorySize == 0)
        {
            return;
        }
        if (directoryRva == 0 || directorySize < descriptorSize ||
            directorySize > int.MaxValue)
        {
            throw Invalid();
        }

        var directoryOffset = RvaToOffset(
            bytes,
            sections,
            directoryRva,
            checked((int)directorySize));
        var maximumDescriptors = checked((int)directorySize / descriptorSize);
        var terminated = false;
        for (var index = 0; index < maximumDescriptors; index++)
        {
            var descriptorOffset = checked(
                directoryOffset + index * descriptorSize);
            if (IsZero(bytes, descriptorOffset, descriptorSize))
            {
                terminated = true;
                break;
            }

            if (requireRvaAttribute &&
                (ReadUInt32(bytes, descriptorOffset) & 1U) == 0)
            {
                throw Invalid();
            }

            var nameRva = ReadUInt32(
                bytes,
                descriptorOffset + nameFieldOffset);
            var nameOffset = RvaToOffset(bytes, sections, nameRva, 1);
            imports.Add(ReadImportName(bytes, nameOffset));
            if (imports.Count > MaximumImportCount)
            {
                throw Invalid();
            }
        }

        if (!terminated)
        {
            throw Invalid();
        }
    }

    private static string ReadImportName(byte[] bytes, int offset)
    {
        var length = 0;
        while (length <= MaximumImportNameBytes &&
               offset + length < bytes.Length &&
               bytes[offset + length] != 0)
        {
            var value = bytes[offset + length];
            if (value is < 0x21 or > 0x7e)
            {
                throw Invalid();
            }
            length++;
        }

        if (length is 0 or > MaximumImportNameBytes ||
            offset + length >= bytes.Length)
        {
            throw Invalid();
        }

        var name = Encoding.ASCII.GetString(bytes, offset, length);
        if (!name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
            name[0] == '.' || name.Contains("..", StringComparison.Ordinal) ||
            name.Any(character =>
                character is not (>= 'a' and <= 'z') and
                    not (>= 'A' and <= 'Z') and
                    not (>= '0' and <= '9') and
                    not '.' and not '_' and not '-'))
        {
            throw Invalid();
        }

        return name;
    }

    private static int RvaToOffset(
        byte[] bytes,
        IReadOnlyList<Section> sections,
        uint rva,
        int requiredLength)
    {
        if (rva == 0 || requiredLength <= 0)
        {
            throw Invalid();
        }

        foreach (var section in sections)
        {
            var extent = Math.Max(section.VirtualSize, section.RawSize);
            if (extent == 0 || rva < section.VirtualAddress)
            {
                continue;
            }

            var delta = rva - section.VirtualAddress;
            if (delta >= extent || delta >= section.RawSize ||
                requiredLength > section.RawSize - delta)
            {
                continue;
            }

            var offset = checked(section.RawOffset + delta);
            if (offset > int.MaxValue ||
                !HasRange(bytes, (int)offset, requiredLength))
            {
                throw Invalid();
            }
            return (int)offset;
        }

        throw Invalid();
    }

    private static bool IsCanonicalFileName(string value) =>
        value.Length <= 260 &&
        string.Equals(Path.GetFileName(value), value, StringComparison.Ordinal) &&
        value.IndexOfAny(['/', '\\']) < 0 &&
        !value.Contains(':') &&
        !value.EndsWith(' ') &&
        !value.EndsWith('.');

    private static bool IsZero(byte[] bytes, int offset, int length)
    {
        if (!HasRange(bytes, offset, length))
        {
            throw Invalid();
        }
        return bytes.AsSpan(offset, length).IndexOfAnyExcept((byte)0) < 0;
    }

    private static ushort ReadUInt16(byte[] bytes, int offset)
    {
        RequireRange(bytes, offset, sizeof(ushort));
        return BinaryPrimitives.ReadUInt16LittleEndian(
            bytes.AsSpan(offset, sizeof(ushort)));
    }

    private static uint ReadUInt32(byte[] bytes, int offset)
    {
        RequireRange(bytes, offset, sizeof(uint));
        return BinaryPrimitives.ReadUInt32LittleEndian(
            bytes.AsSpan(offset, sizeof(uint)));
    }

    private static int ReadInt32(byte[] bytes, int offset)
    {
        RequireRange(bytes, offset, sizeof(int));
        return BinaryPrimitives.ReadInt32LittleEndian(
            bytes.AsSpan(offset, sizeof(int)));
    }

    private static void RequireRange(byte[] bytes, int offset, int length)
    {
        if (!HasRange(bytes, offset, length))
        {
            throw Invalid();
        }
    }

    private static bool HasRange(byte[] bytes, int offset, int length) =>
        offset >= 0 && length >= 0 && offset <= bytes.Length - length;

    private static InvalidDataException Invalid(Exception? inner = null) =>
        new("The Windows PE image is not an admitted AMD64 PE32+ file.", inner);

    private readonly record struct Section(
        uint VirtualSize,
        uint VirtualAddress,
        uint RawSize,
        uint RawOffset);
}
