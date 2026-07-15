namespace VRRecorder.Application.Encoding;

[Flags]
public enum EncoderProbeValidation : uint
{
    None = 0,
    NonemptyPacket = 0x0001,
    ParseableAccessUnit = 0x0002,
    Sps = 0x0004,
    Pps = 0x0008,
    Idr = 0x0010,
    DisplayDimensions = 0x0020,
    Profile = 0x0040,
    FrameRate = 0x0080,
    ZeroBFrames = 0x0100,
    Decoded = 0x0200,
    SameAdapter = 0x0400,
    CompleteSoftwarePacket = NonemptyPacket |
        ParseableAccessUnit |
        Sps |
        Pps |
        Idr |
        DisplayDimensions |
        Profile |
        FrameRate |
        ZeroBFrames |
        Decoded,
    CompleteHardwarePacket = CompleteSoftwarePacket | SameAdapter,
}
