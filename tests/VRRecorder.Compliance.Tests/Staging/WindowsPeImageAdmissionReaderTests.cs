using System.Buffers.Binary;
using System.Text;
using VRRecorder.Compliance.Staging;

namespace VRRecorder.Compliance.Tests.Staging;

public sealed class WindowsPeImageAdmissionReaderTests
{
    [Fact]
    public void ReadsAmd64Pe32PlusExecutableAndCanonicalImports()
    {
        var image = PeFixture.Create(
            isDll: false,
            subsystem: 2,
            imports: ["KERNEL32.dll", "vrrecorder_native.dll"]);

        var admitted = WindowsPeImageAdmissionReader.Read(
            "VRRecorder.App.exe",
            image);

        Assert.False(admitted.IsDll);
        Assert.True(admitted.HasEntryPoint);
        Assert.Equal(WindowsPeSubsystem.Gui, admitted.Subsystem);
        Assert.Equal(
            ["KERNEL32.dll", "vrrecorder_native.dll"],
            admitted.Imports);
    }

    [Fact]
    public void ReadsAmd64Pe32PlusDllWithoutGuessingFromExtension()
    {
        var admitted = WindowsPeImageAdmissionReader.Read(
            "vrrecorder_native.dll",
            PeFixture.Create(
                isDll: true,
                subsystem: 3,
                imports: ["avcodec-62.dll"]));

        Assert.True(admitted.IsDll);
        Assert.Equal(WindowsPeSubsystem.Console, admitted.Subsystem);
        Assert.Equal(["avcodec-62.dll"], admitted.Imports);
    }

    [Theory]
    [InlineData("VRRecorder.App.dll", false, 2, true)]
    [InlineData("vrrecorder_native.exe", true, 2, true)]
    [InlineData("VRRecorder.App.exe", false, 1, true)]
    [InlineData("VRRecorder.App.exe", false, 2, false)]
    public void ExtensionSubsystemAndEntrypointMismatchAreRejected(
        string fileName,
        bool isDll,
        ushort subsystem,
        bool hasEntryPoint)
    {
        var image = PeFixture.Create(
            isDll,
            subsystem,
            ["KERNEL32.dll"],
            hasEntryPoint);

        Assert.Throws<InvalidDataException>(() =>
            WindowsPeImageAdmissionReader.Read(fileName, image));
    }

    [Theory]
    [InlineData("../evil.dll")]
    [InlineData("folder/evil.dll")]
    [InlineData("folder\\evil.dll")]
    [InlineData("evil.dll:stream")]
    [InlineData("evil")]
    public void UnsafeOrNonDllImportNamesAreRejected(string importName)
    {
        var image = PeFixture.Create(true, 2, [importName]);

        Assert.Throws<InvalidDataException>(() =>
            WindowsPeImageAdmissionReader.Read("native.dll", image));
    }

    [Fact]
    public void PeFileNameMustBeABasename()
    {
        var image = PeFixture.Create(false, 2, ["KERNEL32.dll"]);

        Assert.Throws<InvalidDataException>(() =>
            WindowsPeImageAdmissionReader.Read("folder\\app.exe", image));
    }

    [Fact]
    public void TruncatedHeaderAndOutOfRangeImportRvaAreRejected()
    {
        Assert.Throws<InvalidDataException>(() =>
            WindowsPeImageAdmissionReader.Read("app.exe", new byte[63]));

        var image = PeFixture.Create(false, 2, ["KERNEL32.dll"]);
        BinaryPrimitives.WriteUInt32LittleEndian(
            image.AsSpan(0x98 + 120, 4),
            0x7fff_ffff);
        Assert.Throws<InvalidDataException>(() =>
            WindowsPeImageAdmissionReader.Read("app.exe", image));
    }

    private static class PeFixture
    {
        private const int PeOffset = 0x80;
        private const int CoffOffset = PeOffset + 4;
        private const int OptionalOffset = CoffOffset + 20;
        private const int SectionOffset = OptionalOffset + 0xf0;
        private const int RawOffset = 0x200;
        private const uint SectionRva = 0x1000;

        public static byte[] Create(
            bool isDll,
            ushort subsystem,
            IReadOnlyList<string> imports,
            bool hasEntryPoint = true)
        {
            var bytes = new byte[0x800];
            bytes[0] = (byte)'M';
            bytes[1] = (byte)'Z';
            Write32(bytes, 0x3c, PeOffset);
            bytes[PeOffset] = (byte)'P';
            bytes[PeOffset + 1] = (byte)'E';
            Write16(bytes, CoffOffset, 0x8664);
            Write16(bytes, CoffOffset + 2, 1);
            Write16(bytes, CoffOffset + 16, 0xf0);
            Write16(
                bytes,
                CoffOffset + 18,
                (ushort)(0x0022 | (isDll ? 0x2000 : 0)));

            Write16(bytes, OptionalOffset, 0x020b);
            Write32(
                bytes,
                OptionalOffset + 16,
                hasEntryPoint ? (int)SectionRva : 0);
            Write32(bytes, OptionalOffset + 32, 0x1000);
            Write32(bytes, OptionalOffset + 36, 0x200);
            Write32(bytes, OptionalOffset + 56, 0x2000);
            Write32(bytes, OptionalOffset + 60, RawOffset);
            Write16(bytes, OptionalOffset + 68, subsystem);
            Write32(bytes, OptionalOffset + 108, 16);

            var descriptorRva = SectionRva + 0x100;
            Write32(bytes, OptionalOffset + 120, (int)descriptorRva);
            Write32(bytes, OptionalOffset + 124, (imports.Count + 1) * 20);

            Encoding.ASCII.GetBytes(".rdata\0\0").CopyTo(bytes, SectionOffset);
            Write32(bytes, SectionOffset + 8, 0x600);
            Write32(bytes, SectionOffset + 12, (int)SectionRva);
            Write32(bytes, SectionOffset + 16, 0x600);
            Write32(bytes, SectionOffset + 20, RawOffset);
            Write32(bytes, SectionOffset + 36, 0x4000_0040);

            var descriptorOffset = RawOffset + 0x100;
            var stringOffset = RawOffset + 0x300;
            foreach (var import in imports)
            {
                var nameRva = SectionRva + (uint)(stringOffset - RawOffset);
                Write32(bytes, descriptorOffset + 12, (int)nameRva);
                var name = Encoding.ASCII.GetBytes(import);
                name.CopyTo(bytes, stringOffset);
                bytes[stringOffset + name.Length] = 0;
                descriptorOffset += 20;
                stringOffset += name.Length + 1;
            }

            return bytes;
        }

        private static void Write16(byte[] bytes, int offset, ushort value) =>
            BinaryPrimitives.WriteUInt16LittleEndian(
                bytes.AsSpan(offset, 2),
                value);

        private static void Write32(byte[] bytes, int offset, int value) =>
            BinaryPrimitives.WriteInt32LittleEndian(
                bytes.AsSpan(offset, 4),
                value);
    }
}
