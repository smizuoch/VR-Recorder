using VRRecorder.DesignSystem;

namespace VRRecorder.Infrastructure.SteamVr;

public interface IUiCommandDispatcher
{
    Task DispatchAsync(
        UiCommandId command,
        UiActivationKind activationKind,
        CancellationToken cancellationToken);
}
