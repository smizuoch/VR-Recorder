namespace VRRecorder.Infrastructure.SteamVr;

public sealed record SteamVrDigitalActionState(
    bool IsActive,
    bool State,
    bool Changed);
