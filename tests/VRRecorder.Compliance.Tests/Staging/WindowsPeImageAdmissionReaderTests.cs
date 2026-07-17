using System.Buffers.Binary;
using System.Text;
using VRRecorder.Compliance.Staging;

namespace VRRecorder.Compliance.Tests.Staging;

public sealed class WindowsPeImageAdmissionReaderTests
{
    [Fact]
    public void ReadsAmd64Pe32PlusExecutableAndCanonicalImports()
    {
        var image = WindowsPeImageTestData.Create(
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
            WindowsPeImageTestData.Create(
                isDll: true,
                subsystem: 3,
                imports: ["avcodec-62.dll"]));

        Assert.True(admitted.IsDll);
        Assert.Equal(WindowsPeSubsystem.Console, admitted.Subsystem);
        Assert.Equal(["avcodec-62.dll"], admitted.Imports);
    }

    [Fact]
    public void DelayImportsJoinTheCanonicalImportClosure()
    {
        var admitted = WindowsPeImageAdmissionReader.Read(
            "vrrecorder_native.dll",
            WindowsPeImageTestData.Create(
                isDll: true,
                subsystem: 2,
                imports: ["KERNEL32.dll"],
                delayImports: ["avcodec-62.dll"]));

        Assert.Equal(
            ["avcodec-62.dll", "KERNEL32.dll"],
            admitted.Imports);
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
        var image = WindowsPeImageTestData.Create(
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
        var image = WindowsPeImageTestData.Create(true, 2, [importName]);

        Assert.Throws<InvalidDataException>(() =>
            WindowsPeImageAdmissionReader.Read("native.dll", image));
    }

    [Fact]
    public void PeFileNameMustBeABasename()
    {
        var image = WindowsPeImageTestData.Create(
            false,
            2,
            ["KERNEL32.dll"]);

        Assert.Throws<InvalidDataException>(() =>
            WindowsPeImageAdmissionReader.Read("folder\\app.exe", image));
    }

    [Fact]
    public void TruncatedHeaderAndOutOfRangeImportRvaAreRejected()
    {
        Assert.Throws<InvalidDataException>(() =>
            WindowsPeImageAdmissionReader.Read("app.exe", new byte[63]));

        var image = WindowsPeImageTestData.Create(
            false,
            2,
            ["KERNEL32.dll"]);
        BinaryPrimitives.WriteUInt32LittleEndian(
            image.AsSpan(0x98 + 120, 4),
            0x7fff_ffff);
        Assert.Throws<InvalidDataException>(() =>
            WindowsPeImageAdmissionReader.Read("app.exe", image));
    }

    [Theory]
    [InlineData("bad-dos-signature")]
    [InlineData("pe-offset-too-small")]
    [InlineData("pe-offset-out-of-range")]
    [InlineData("bad-pe-signature")]
    [InlineData("bad-machine")]
    [InlineData("zero-sections")]
    [InlineData("too-many-sections")]
    [InlineData("not-executable")]
    [InlineData("optional-header-too-small")]
    [InlineData("optional-header-out-of-range")]
    [InlineData("bad-pe32-plus-magic")]
    [InlineData("section-table-out-of-range")]
    [InlineData("section-raw-offset-overflow")]
    [InlineData("section-raw-size-overflow")]
    [InlineData("section-raw-range-invalid")]
    [InlineData("section-virtual-extent-overflow")]
    [InlineData("import-rva-without-size")]
    [InlineData("import-size-without-rva")]
    [InlineData("import-size-too-small")]
    [InlineData("import-size-overflow")]
    [InlineData("import-name-rva-zero")]
    [InlineData("import-descriptor-not-terminated")]
    [InlineData("delay-import-without-rva-attribute")]
    public void MalformedPeHeaderBoundariesAreRejected(string mutation)
    {
        var image = WindowsPeImageTestData.Create(
            false,
            2,
            ["KERNEL32.dll"],
            delayImports: mutation == "delay-import-without-rva-attribute"
                ? ["USER32.dll"]
                : null);
        Mutate(image, mutation);

        Assert.Throws<InvalidDataException>(() =>
            WindowsPeImageAdmissionReader.Read("app.exe", image));
    }

    [Theory]
    [InlineData("app.bin")]
    [InlineData("app.exe ")]
    [InlineData("app.exe.")]
    public void PeFileNameMustBeCanonicalExeOrDll(string fileName)
    {
        var image = WindowsPeImageTestData.Create(
            false,
            2,
            ["KERNEL32.dll"]);

        Assert.Throws<InvalidDataException>(() =>
            WindowsPeImageAdmissionReader.Read(fileName, image));
    }

    [Fact]
    public void PeFileNameCannotExceedTheWindowsComponentLimit()
    {
        var image = WindowsPeImageTestData.Create(
            false,
            2,
            ["KERNEL32.dll"]);

        Assert.Throws<InvalidDataException>(() =>
            WindowsPeImageAdmissionReader.Read(
                new string('a', 257) + ".exe",
                image));
    }

    [Theory]
    [InlineData("")]
    [InlineData("bad name.dll")]
    [InlineData("bad!.dll")]
    public void EmptyWhitespaceOrPunctuationImportNamesAreRejected(
        string importName)
    {
        var image = WindowsPeImageTestData.Create(true, 2, [importName]);

        Assert.Throws<InvalidDataException>(() =>
            WindowsPeImageAdmissionReader.Read("native.dll", image));
    }

    [Fact]
    public void ImportNameCannotExceedThePeNameLimit()
    {
        var image = WindowsPeImageTestData.Create(
            true,
            2,
            [new string('a', 257) + ".dll"]);

        Assert.Throws<InvalidDataException>(() =>
            WindowsPeImageAdmissionReader.Read("native.dll", image));
    }

    [Fact]
    public void PeWithoutImportDirectoriesIsAccepted()
    {
        var image = WindowsPeImageTestData.Create(
            false,
            2,
            ["KERNEL32.dll"]);
        Write32(image, OptionalOffset + 108, 1);

        var admitted = WindowsPeImageAdmissionReader.Read("app.exe", image);

        Assert.Empty(admitted.Imports);
    }

    [Fact]
    public void ZeroLengthSectionWithoutImportsIsAccepted()
    {
        var image = WindowsPeImageTestData.Create(
            false,
            2,
            ["KERNEL32.dll"]);
        Write32(image, OptionalOffset + 108, 1);
        Write32(image, SectionOffset + 16, 0);

        var admitted = WindowsPeImageAdmissionReader.Read("app.exe", image);

        Assert.Empty(admitted.Imports);
    }

    private const int PeOffset = 0x80;
    private const int CoffOffset = PeOffset + 4;
    private const int OptionalOffset = CoffOffset + 20;
    private const int SectionOffset = OptionalOffset + 0xf0;
    private const int RawOffset = 0x200;

    private static void Mutate(byte[] image, string mutation)
    {
        switch (mutation)
        {
            case "bad-dos-signature":
                image[0] = 0;
                break;
            case "pe-offset-too-small":
                Write32(image, 0x3c, 1);
                break;
            case "pe-offset-out-of-range":
                Write32(image, 0x3c, int.MaxValue);
                break;
            case "bad-pe-signature":
                image[PeOffset] = 0;
                break;
            case "bad-machine":
                Write16(image, CoffOffset, 0x014c);
                break;
            case "zero-sections":
                Write16(image, CoffOffset + 2, 0);
                break;
            case "too-many-sections":
                Write16(image, CoffOffset + 2, 97);
                break;
            case "not-executable":
                Write16(image, CoffOffset + 18, 0);
                break;
            case "optional-header-too-small":
                Write16(image, CoffOffset + 16, 127);
                break;
            case "optional-header-out-of-range":
                Write16(image, CoffOffset + 16, ushort.MaxValue);
                break;
            case "bad-pe32-plus-magic":
                Write16(image, OptionalOffset, 0x010b);
                break;
            case "section-table-out-of-range":
                Write16(image, CoffOffset + 2, 96);
                break;
            case "section-raw-offset-overflow":
                Write32(image, SectionOffset + 20, uint.MaxValue);
                break;
            case "section-raw-size-overflow":
                Write32(image, SectionOffset + 16, uint.MaxValue);
                break;
            case "section-raw-range-invalid":
                Write32(image, SectionOffset + 20, 0x700);
                Write32(image, SectionOffset + 16, 0x200);
                break;
            case "section-virtual-extent-overflow":
                Write32(image, SectionOffset + 12, uint.MaxValue);
                Write32(image, SectionOffset + 8, 2);
                break;
            case "import-rva-without-size":
                Write32(image, OptionalOffset + 124, 0);
                break;
            case "import-size-without-rva":
                Write32(image, OptionalOffset + 120, 0);
                break;
            case "import-size-too-small":
                Write32(image, OptionalOffset + 124, 19);
                break;
            case "import-size-overflow":
                Write32(image, OptionalOffset + 124, uint.MaxValue);
                break;
            case "import-name-rva-zero":
                Write32(image, RawOffset + 0x100, 1);
                Write32(image, RawOffset + 0x100 + 12, 0);
                break;
            case "import-descriptor-not-terminated":
                Write32(image, OptionalOffset + 124, 20);
                break;
            case "delay-import-without-rva-attribute":
                Write32(image, RawOffset + 0x200, 0);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation));
        }
    }

    private static void Write16(byte[] bytes, int offset, ushort value) =>
        BinaryPrimitives.WriteUInt16LittleEndian(
            bytes.AsSpan(offset, sizeof(ushort)),
            value);

    private static void Write32(byte[] bytes, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(offset, sizeof(uint)),
            value);

}
