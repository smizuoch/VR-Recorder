using System.Windows.Threading;
using VRRecorder.Application.Camera;
using VRRecorder.Application.Ports;

namespace VRRecorder.App;

internal sealed class WpfVrChatInstanceSelectionPrompt
    : IVrChatInstanceSelectionPrompt
{
    private readonly Dispatcher _dispatcher;

    public WpfVrChatInstanceSelectionPrompt()
        : this(System.Windows.Application.Current?.Dispatcher ??
               throw new InvalidOperationException(
                   "The WPF application dispatcher is unavailable."))
    {
    }

    internal WpfVrChatInstanceSelectionPrompt(Dispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        _dispatcher = dispatcher;
    }

    public async Task<string?> SelectAsync(
        IReadOnlyList<VrChatInstanceCandidate> candidates,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var snapshot = candidates.ToArray();
        cancellationToken.ThrowIfCancellationRequested();
        if (_dispatcher.CheckAccess())
        {
            return ShowDialog(snapshot, cancellationToken);
        }

        var operation = _dispatcher.InvokeAsync(
            () => ShowDialog(snapshot, cancellationToken),
            DispatcherPriority.Normal,
            cancellationToken);
        return await operation.Task.ConfigureAwait(false);
    }

    private static string? ShowDialog(
        IReadOnlyList<VrChatInstanceCandidate> candidates,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var window = new VrChatInstanceSelectionWindow(candidates);
        if (System.Windows.Application.Current?.MainWindow is
            { IsVisible: true } owner)
        {
            window.Owner = owner;
        }

        using var registration = cancellationToken.Register(() =>
            _ = window.Dispatcher.InvokeAsync(() =>
            {
                if (window.IsVisible)
                {
                    window.Close();
                }
            }));
        cancellationToken.ThrowIfCancellationRequested();
        var accepted = window.ShowDialog() == true;
        cancellationToken.ThrowIfCancellationRequested();
        return accepted ? window.SelectedServiceId : null;
    }
}
