using VRRecorder.Application.Encoding;
using VRRecorder.Application.Ports;

namespace VRRecorder.Infrastructure.Media;

public sealed class TransientEncoderProbe : IEncoderProbe
{
    private readonly Func<IEncoderProbe> _create;

    public TransientEncoderProbe(string libraryPath)
        : this(CreateFactory(libraryPath))
    {
    }

    public TransientEncoderProbe(Func<IEncoderProbe> create)
    {
        ArgumentNullException.ThrowIfNull(create);
        _create = create;
    }

    public async Task<EncoderProbeResult> ProbeAsync(
        EncoderProbeRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var probe = _create() ?? throw new InvalidOperationException(
            "The transient encoder probe factory returned null.");
        try
        {
            return await probe.ProbeAsync(request, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            if (probe is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            }
            else if (probe is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    private static Func<IEncoderProbe> CreateFactory(string libraryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryPath);
        var fullPath = Path.GetFullPath(libraryPath);
        return () => new PInvokeEncoderProbe(fullPath);
    }
}
