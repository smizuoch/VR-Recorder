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

}
