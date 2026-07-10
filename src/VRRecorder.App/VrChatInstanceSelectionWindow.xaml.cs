using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using VRRecorder.Application.Camera;

namespace VRRecorder.App;

public partial class VrChatInstanceSelectionWindow : Window
{
    internal VrChatInstanceSelectionWindow(
        IReadOnlyList<VrChatInstanceCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (candidates.Count == 0 ||
            candidates.Any(candidate => candidate is null))
        {
            throw new ArgumentException(
                "At least one valid VRChat instance is required.",
                nameof(candidates));
        }

        InitializeComponent();
        var itemFormat = FindResource(
            "VrChat_Selection_Item_Format") as string ??
            throw new InvalidOperationException(
                "The VRChat selection item format resource is missing.");
        VrChatInstanceList.ItemsSource = candidates
            .Select(candidate => new CandidateOption(
                candidate.ServiceId,
                string.Format(
                    CultureInfo.CurrentCulture,
                    itemFormat,
                    candidate.DisplayName,
                    candidate.ServiceId)))
            .ToArray();
    }

    public string? SelectedServiceId { get; private set; }

    private void OnSelectionChanged(
        object sender,
        SelectionChangedEventArgs e) =>
        AcceptSelectionButton.IsEnabled =
            VrChatInstanceList.SelectedItem is CandidateOption;

    private void OnAccept(object sender, RoutedEventArgs e)
    {
        if (VrChatInstanceList.SelectedItem is not CandidateOption selected)
        {
            return;
        }

        SelectedServiceId = selected.ServiceId;
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) =>
        DialogResult = false;

    private sealed record CandidateOption(string ServiceId, string Label);
}
