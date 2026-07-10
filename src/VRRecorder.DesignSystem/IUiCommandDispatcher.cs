namespace VRRecorder.DesignSystem;

public interface IUiCommandDispatcher
{
    Task DispatchAsync(
        UiCommandId command,
        UiActivationKind activationKind,
        CancellationToken cancellationToken);
}
