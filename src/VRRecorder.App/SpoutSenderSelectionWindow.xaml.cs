using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using VRRecorder.Application.Recording;

namespace VRRecorder.App;

public partial class SpoutSenderSelectionWindow : Window
{
    internal SpoutSenderSelectionWindow(
        IReadOnlyList<StableVideoSignal> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (candidates.Count == 0 ||
            candidates.Any(candidate => candidate is null))
        {
            throw new ArgumentException(
                "At least one valid Spout sender is required.",
                nameof(candidates));
        }

        InitializeComponent();
        var itemFormat = FindResource(
            "Spout_Selection_Item_Format") as string ??
            throw new InvalidOperationException(
                "The Spout sender selection item format resource is missing.");
        SpoutSenderList.ItemsSource = candidates
            .Select(candidate => new CandidateOption(
                candidate.SenderId,
                string.Format(
                    CultureInfo.CurrentCulture,
                    itemFormat,
                    candidate.SenderId,
                    candidate.Width,
                    candidate.Height,
                    candidate.GpuIdentity)))
            .ToArray();
    }

    public string? SelectedSenderId { get; private set; }

    private void OnSelectionChanged(
        object sender,
        SelectionChangedEventArgs e) =>
        AcceptSelectionButton.IsEnabled =
            SpoutSenderList.SelectedItem is CandidateOption;

    private void OnAccept(object sender, RoutedEventArgs e)
    {
        if (SpoutSenderList.SelectedItem is not CandidateOption selected)
        {
            return;
        }

        SelectedSenderId = selected.SenderId;
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) =>
        DialogResult = false;

    private sealed record CandidateOption(string SenderId, string Label);
}
