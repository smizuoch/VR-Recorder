using VRRecorder.Application.Compliance;
using VRRecorder.Application.Presentation;
using VRRecorder.DesignSystem;
using VRRecorder.Domain.Recording;
using VRRecorder.Presentation.Wrist;

namespace VRRecorder.Presentation.Tests.Wrist;

public sealed class WristLegalProjectorTests
{
    private static readonly string[] ExpectedDetailLabels =
    [
        "Name",
        "Version",
        "License",
        "Usage",
        "Linkage",
        "Modified",
        "Source information",
    ];
    private static readonly string[] ExpectedDetailValues =
    [
        "Component a",
        "a.0.0",
        "MIT",
        "runtime-feature",
        "managed-library",
        "Yes",
        "offline source a@commit",
    ];

    [Fact]
    public void ListProjectsDeterministicAccessibleComponentsAndVersion()
    {
        var projector = new WristLegalProjector(EnglishUiLocalizer.Instance);
        var state = State(
            WristLegalView.ComponentList,
            components: [Component("b"), Component("a")]);

        var snapshot = projector.Project(state, ReadyStatus());

        Assert.Equal("About & Legal", snapshot.Title.Value);
        Assert.Equal("Product version 0.1.0", snapshot.VersionLabel.Value);
        Assert.Equal(["a", "b"], snapshot.Components.Select(item => item.Id));
        var first = snapshot.Components[0];
        Assert.Equal("Component a", first.DisplayName);
        Assert.Equal("a.0.0", first.Version);
        Assert.Equal("MIT", first.LicenseExpression);
        Assert.Equal(
            "Open legal details for Component a, version a.0.0, license MIT",
            first.AccessibleName.Value);
        Assert.Empty(snapshot.FixedRecordingActions);
    }

    [Fact]
    public void DetailProjectsVersionLicenseSourceAndModificationInformation()
    {
        var projector = new WristLegalProjector(EnglishUiLocalizer.Instance);
        var component = Component("a") with { Modified = true };
        var state = State(
            WristLegalView.ComponentDetail,
            components: [component],
            selected: component);

        var snapshot = projector.Project(state, ReadyStatus());

        Assert.Equal(
            ExpectedDetailLabels,
            snapshot.DetailFields.Select(field => field.Label.Value));
        Assert.Equal(
            ExpectedDetailValues,
            snapshot.DetailFields.Select(field => field.Value));
        var openLicense = Assert.Single(snapshot.NavigationActions, action =>
            action.Action == WristLegalAction.OpenLicense);
        Assert.Equal("Read full license text", openLicense.AccessibleName.Value);
        Assert.True(openLicense.MinimumTargetDp >= 56);
    }

    [Fact]
    public void LicenseProjectsScrollablePageAndLocalizedNavigation()
    {
        var projector = new WristLegalProjector(EnglishUiLocalizer.Instance);
        var component = Component("a");
        var state = State(
            WristLegalView.LicenseText,
            components: [component],
            selected: component,
            fullText: "line 1\nline 2\nline 3\nline 4\n",
            firstVisibleLine: 2,
            linesPerPage: 2);

        var snapshot = projector.Project(state, ReadyStatus());

        Assert.NotNull(snapshot.LicensePage);
        Assert.Equal("line 3\nline 4", snapshot.LicensePage.Text);
        Assert.Equal(2, snapshot.LicensePage.PageNumber);
        Assert.Equal(2, snapshot.LicensePage.PageCount);
        Assert.Equal("Page 2 of 2", snapshot.LicensePage.AccessiblePageLabel.Value);
        Assert.Contains(snapshot.NavigationActions, action =>
            action.Action == WristLegalAction.PreviousPage &&
            action.AccessibleName.Value == "Previous license page");
        Assert.Contains(snapshot.NavigationActions, action =>
            action.Action == WristLegalAction.NextPage &&
            action.AccessibleName.Value == "Next license page");
    }

    [Theory]
    [InlineData(WristLegalView.ComponentList)]
    [InlineData(WristLegalView.ComponentDetail)]
    [InlineData(WristLegalView.LicenseText)]
    [InlineData(WristLegalView.Unavailable)]
    public void RecordingUsesCanonicalOneOperationStopOnEveryLegalView(
        WristLegalView view)
    {
        var projector = new WristLegalProjector(EnglishUiLocalizer.Instance);
        var component = Component("a");
        var state = State(
            view,
            components: [component],
            selected: view is WristLegalView.ComponentDetail or
                WristLegalView.LicenseText
                ? component
                : null,
            fullText: view == WristLegalView.LicenseText
                ? "license text\n"
                : null);
        var recording = new RecorderStatusSnapshot(
            Revision: 9,
            State: RecorderState.Recording,
            AvailableActions: RecorderAvailableActions.Stop);

        var snapshot = projector.Project(state, recording);

        var stop = Assert.Single(snapshot.FixedRecordingActions);
        Assert.Equal("recording.stop", stop.SemanticId);
        Assert.Equal(UiCommandId.ToggleRecording, stop.Command);
        Assert.True(stop.IsEnabled);
        Assert.Equal("STOP", stop.VisibleLabel.Value);
        Assert.Equal("Stop recording", stop.AccessibleName.Value);
        Assert.True(stop.MinimumTargetDp >= 64);
    }

    private static WristLegalState State(
        WristLegalView view,
        IReadOnlyList<LegalCatalogComponent>? components = null,
        LegalCatalogComponent? selected = null,
        string? fullText = null,
        int firstVisibleLine = 0,
        int linesPerPage = 2) =>
        new(
            Revision: 1,
            View: view,
            ProductVersion: "0.1.0",
            Components: components ?? [],
            SelectedComponent: selected,
            FullLicenseText: fullText,
            FirstVisibleLine: firstVisibleLine,
            LinesPerPage: linesPerPage,
            Issues: []);

    private static LegalCatalogComponent Component(string id) =>
        new(
            id,
            $"Component {id}",
            $"{id}.0.0",
            "MIT",
            "runtime-feature",
            "managed-library",
            false,
            $"offline source {id}@commit",
            $"LICENSES/{id}/LICENSE.txt");

    private static RecorderStatusSnapshot ReadyStatus() =>
        new(
            Revision: 1,
            State: RecorderState.Ready,
            AvailableActions: RecorderAvailableActions.Start);
}
