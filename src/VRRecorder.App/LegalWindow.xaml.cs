using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using VRRecorder.Application.Compliance;
using VRRecorder.Application.Desktop;
using VRRecorder.DesignSystem;

namespace VRRecorder.App;

public partial class LegalWindow : Window
{
    private readonly DesktopLegalController _controller;
    private bool _applyingState;
    private bool _loaded;

    internal LegalWindow(DesktopLegalController controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        _controller = controller;
        InitializeComponent();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        await RunLegalOperationAsync(() =>
            _controller.OpenAsync(CancellationToken.None));
    }

    private async void OnComponentSelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_applyingState ||
            LegalComponentList.SelectedItem is not LegalListItem selected)
        {
            return;
        }

        await RunLegalOperationAsync(() =>
            _controller.ShowDetailAsync(
                selected.Component.Id,
                CancellationToken.None));
    }

    private async void OnDocumentSelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_applyingState ||
            LegalDocumentList.SelectedItem is not LegalDocumentListItem selected)
        {
            return;
        }

        await RunLegalOperationAsync(() =>
            _controller.ShowDocumentAsync(
                selected.Reference,
                CancellationToken.None));
    }

    private async void OnOpenLicenseFolderClick(
        object sender,
        RoutedEventArgs e)
    {
        await RunLegalOperationAsync(() =>
            _controller.OpenLicenseFolderAsync(CancellationToken.None));
    }

    private async void OnRefreshLegalClick(
        object sender,
        RoutedEventArgs e)
    {
        await RunLegalOperationAsync(() =>
            _controller.RefreshAsync(CancellationToken.None));
    }

    private void OnCloseLegalClick(object sender, RoutedEventArgs e) => Close();

    private async Task RunLegalOperationAsync(Func<Task> operation)
    {
        try
        {
            await operation();
        }
        catch (Exception)
        {
            // Application ports fail closed; an event-handler exception must not
            // escape the WPF synchronization context.
        }

        ApplyState();
    }

    private void ApplyState()
    {
        var state = _controller.State;
        var available = state.View != DesktopLegalView.Unavailable;
        _applyingState = true;
        try
        {
            ApplyAccessibleText(ProductVersionText, CreateIdentityText(
                "Legal_ProductVersion_Format",
                available ? state.ProductVersion : null));
            ApplyAccessibleText(BundleIdentityText, CreateIdentityText(
                "Legal_BundleIdentity_Format",
                available ? state.BundleId : null));
            ApplyAccessibleText(ManifestSha256Text, CreateIdentityText(
                "Legal_ManifestSha256_Format",
                available ? state.ManifestSha256 : null));
            var components = available
                ? state.Components
                : Array.Empty<LegalCatalogComponent>();
            var items = components
                .OrderBy(item => item.Id, StringComparer.Ordinal)
                .Select(item => new LegalListItem(
                    item,
                    $"{item.DisplayName} — {item.Version} — {item.LicenseExpression}"))
                .ToArray();
            LegalComponentList.ItemsSource = items;
            var selectedComponent = available
                ? state.SelectedComponent
                : null;
            LegalComponentList.SelectedItem = selectedComponent is null
                ? null
                : items.SingleOrDefault(item => string.Equals(
                    item.Component.Id,
                    selectedComponent.Id,
                    StringComparison.Ordinal));
            ApplyAccessibleText(ComponentDetailText,
                AccessibleText.FromVisibleText(
                    selectedComponent is null
                        ? string.Empty
                        : FormatDetail(selectedComponent)));

            var documentItems = selectedComponent is null
                ? Array.Empty<LegalDocumentListItem>()
                : selectedComponent.LegalDocuments
                    .OrderBy(reference => reference.Kind)
                    .ThenBy(
                        reference => reference.RelativePath,
                        StringComparer.Ordinal)
                    .Select(reference => new LegalDocumentListItem(
                        reference,
                        FormatDocumentLabel(reference)))
                    .ToArray();
            LegalDocumentList.ItemsSource = documentItems;
            var selectedDocument = available ? state.SelectedDocument : null;
            LegalDocumentList.SelectedItem = selectedDocument is null
                ? null
                : documentItems.SingleOrDefault(item =>
                    item.Reference == selectedDocument);
            ApplyDocumentHeading(selectedDocument);
            var fullDocumentText = available ? state.FullDocumentText : null;
            FullDocumentText.Text = fullDocumentText ?? string.Empty;

            LegalComponentList.IsEnabled = available;
            LegalDocumentList.IsEnabled =
                available && selectedComponent is not null;
            OpenLicenseFolderButton.IsEnabled = available;
            LegalUnavailableText.Visibility = available
                ? Visibility.Collapsed
                : Visibility.Visible;
            LegalUnavailableText.SetResourceReference(
                TextBlock.TextProperty,
                "Legal_State_ComplianceFault");
            LegalUnavailableText.SetResourceReference(
                AutomationProperties.NameProperty,
                "Legal_State_ComplianceFault");
        }
        finally
        {
            _applyingState = false;
        }
    }

    private static void ApplyAccessibleText(
        TextBlock target,
        AccessibleText semantic)
    {
        target.Text = semantic.VisibleText;
        AutomationProperties.SetName(target, semantic.AutomationName);
    }

    private AccessibleText CreateIdentityText(
        string formatResourceKey,
        string? value)
    {
        return string.IsNullOrEmpty(value)
            ? AccessibleText.FromVisibleText(string.Empty)
            : AccessibleText.FromLocalizedFormat(
                CultureInfo.CurrentCulture,
                Resolve(formatResourceKey),
                value);
    }

    private void ApplyDocumentHeading(LegalDocumentReference? reference)
    {
        var kind = reference is null
            ? null
            : Resolve(DocumentKindResourceKey(reference.Kind));
        var heading = kind is null
            ? AccessibleText.FromVisibleText(
                Resolve("Legal_DocumentText_Heading"))
            : AccessibleText.FromLocalizedFormat(
                CultureInfo.CurrentCulture,
                Resolve("Legal_DocumentText_HeadingFormat"),
                kind);
        ApplyAccessibleText(LegalDocumentHeadingText, heading);
        var documentName = kind is null
            ? Resolve("Legal_DocumentText_AccessibleName")
            : Format("Legal_DocumentText_AccessibleNameFormat", kind);
        AutomationProperties.SetName(FullDocumentText, documentName);
    }

    private string FormatDetail(LegalCatalogComponent component) =>
        string.Join(
            Environment.NewLine,
            Format("Legal_Detail_Name_Format", component.DisplayName),
            Format("Legal_Detail_Version_Format", component.Version),
            Format("Legal_Detail_License_Format", component.LicenseExpression),
            Format(
                "Legal_Detail_Copyright_Format",
                component.CopyrightNotice),
            Format("Legal_Detail_Usage_Format", component.Usage),
            Format("Legal_Detail_Linkage_Format", component.Linkage),
            Format(
                "Legal_Detail_Modified_Format",
                Resolve(component.Modified
                    ? "Legal_Modified_Yes"
                    : "Legal_Modified_No")),
            Format("Legal_Detail_Source_Format", component.SourceInformation));

    private string FormatDocumentLabel(LegalDocumentReference reference) =>
        $"{Resolve(DocumentKindResourceKey(reference.Kind))} — " +
        reference.RelativePath;

    private static string DocumentKindResourceKey(LegalDocumentKind kind) =>
        kind switch
        {
            LegalDocumentKind.License => "Legal_DocumentKind_License",
            LegalDocumentKind.Notice => "Legal_DocumentKind_Notice",
            LegalDocumentKind.Copyright => "Legal_DocumentKind_Copyright",
            LegalDocumentKind.Attribution => "Legal_DocumentKind_Attribution",
            LegalDocumentKind.AssetManifest =>
                "Legal_DocumentKind_AssetManifest",
            _ => throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "Unsupported legal document kind."),
        };

    private string Format(string resourceKey, object value) =>
        string.Format(
            CultureInfo.CurrentCulture,
            Resolve(resourceKey),
            value);

    private string Resolve(string resourceKey) =>
        TryFindResource(resourceKey) as string ??
        throw new InvalidOperationException(
            $"The localized desktop resource is missing: {resourceKey}");

    private sealed record LegalListItem(
        LegalCatalogComponent Component,
        string Label);

    private sealed record LegalDocumentListItem(
        LegalDocumentReference Reference,
        string Label);
}
