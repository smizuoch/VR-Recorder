using System.Windows;

namespace VRRecorder.App;

public partial class RecordingRightsWindow : Window
{
    public RecordingRightsWindow()
    {
        InitializeComponent();
    }

    private void OnAcknowledgementChanged(
        object sender,
        RoutedEventArgs e) =>
        AcceptRightsButton.IsEnabled =
            RightsAcknowledgementCheckBox.IsChecked == true;

    private void OnAccept(object sender, RoutedEventArgs e)
    {
        if (RightsAcknowledgementCheckBox.IsChecked != true)
        {
            return;
        }

        DialogResult = true;
    }

    private void OnDecline(object sender, RoutedEventArgs e) =>
        DialogResult = false;
}
