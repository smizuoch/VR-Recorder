using VRRecorder.Application.Ports;

namespace VRRecorder.Application.Camera;

public sealed class VrChatCameraConnectionUseCase
{
    private readonly VrChatTargetResolver _targets;
    private readonly IVrChatCameraGatewayFactory _gateways;

    public VrChatCameraConnectionUseCase(
        VrChatTargetResolver targets,
        IVrChatCameraGatewayFactory gateways)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(gateways);
        _targets = targets;
        _gateways = gateways;
    }

    public async Task<VrChatCameraConnectionResolution> ResolveAsync(
        string? selectedServiceId,
        CancellationToken cancellationToken)
    {
        var target = await _targets
            .ResolveAsync(selectedServiceId, cancellationToken)
            .ConfigureAwait(false);
        return target switch
        {
            VrChatTargetResolution.NotFound =>
                new VrChatCameraConnectionResolution.NotFound(),
            VrChatTargetResolution.SelectionRequired selection =>
                new VrChatCameraConnectionResolution.SelectionRequired(
                    selection.Candidates),
            VrChatTargetResolution.Selected selected =>
                new VrChatCameraConnectionResolution.Connected(
                    selected.Candidate,
                    _gateways.Create(selected.Candidate)),
            _ => throw new InvalidOperationException(
                $"Unknown VRChat target resolution {target.GetType().Name}."),
        };
    }
}
