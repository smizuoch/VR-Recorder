namespace VRRecorder.Application.Setup;

public enum FirstRunSetupStep
{
    SteamVrDetection = 0,
    VrChatOscDetection = 1,
    CameraOscEndpoint = 2,
    MicrophonePrivacyAndDevice = 3,
    EncoderSelfTest = 4,
    SteamVrActionBinding = 5,
    WristOverlayPlacement = 6,
    TestRecordingPlayback = 7,
    LegalBundleVerification = 8,
    OfflineLegalAccess = 9,
    LocalizationAccessibility = 10,
    DesignAssetConformance = 11,
}
