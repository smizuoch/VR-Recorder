using VRRecorder.Application.Audio;
using VRRecorder.Application.Presentation;
using VRRecorder.DesignSystem;
using VRRecorder.Domain.Audio;
using VRRecorder.Domain.Recording;
using VRRecorder.Presentation.Wrist;

namespace VRRecorder.Presentation.Tests.Wrist;

public sealed class WristTextureLayoutEngineTests
{
    [Fact]
    public void RecordingLayoutIsDeterministicAndHitTestsEveryEnabledAction()
    {
        var snapshot = new WristUiProjector(EnglishUiLocalizer.Instance)
            .Project(
                RecorderStatusSnapshot.Create(
                    42,
                    RecorderState.Recording,
                    RecordingAudioControlState.FromRouting(
                        AudioRouting.Mixed)));
        var first = WristTextureLayoutEngine.Layout(
            snapshot,
            WristLayoutOptions.Default);
        var second = WristTextureLayoutEngine.Layout(
            snapshot,
            WristLayoutOptions.Default);

        Assert.Equal(1024, first.PixelWidth);
        Assert.Equal(512, first.PixelHeight);
        Assert.Equal(2, first.PixelsPerDp);
        Assert.Equal(first.Elements, second.Elements);
        Assert.Equal(first.HitTargets, second.HitTargets);
        Assert.Equal(4, first.HitTargets.Count);
        Assert.Equal(
            first.Elements.Count,
            first.Elements.Select(element => element.ElementId)
                .Distinct(StringComparer.Ordinal)
                .Count());

        foreach (var target in first.HitTargets)
        {
            Assert.True(target.Bounds.Width >= target.MinimumTargetDp * 2);
            Assert.True(target.Bounds.Height >= target.MinimumTargetDp * 2);
            Assert.True(target.Bounds.Left >= 0);
            Assert.True(target.Bounds.Top >= 0);
            Assert.True(target.Bounds.Right <= first.PixelWidth);
            Assert.True(target.Bounds.Bottom <= first.PixelHeight);
            Assert.Equal(
                target,
                first.HitTest(
                    target.Bounds.Left + target.Bounds.Width / 2,
                    target.Bounds.Top + target.Bounds.Height / 2));
        }

        for (var left = 0; left < first.HitTargets.Count; left++)
        {
            for (var right = left + 1;
                 right < first.HitTargets.Count;
                 right++)
            {
                Assert.False(first.HitTargets[left].Bounds.Intersects(
                    first.HitTargets[right].Bounds));
            }
        }

        Assert.Null(first.HitTest(-1, 0));
        Assert.Null(first.HitTest(1024, 511));
        var stop = Assert.Single(first.HitTargets, target =>
            target.SemanticId == "recording.stop");
        Assert.Equal(WristElementKind.PrimaryAction, stop.Kind);
        Assert.Equal(UiCommandId.ToggleRecording, stop.Command);
        Assert.True(stop.MinimumTargetDp >= 64);
    }

    [Fact]
    public void RightToLeftMirrorsSecondaryTargetsWithoutMirroringIdentity()
    {
        var snapshot = new WristUiProjector(EnglishUiLocalizer.Instance)
            .Project(
                RecorderStatusSnapshot.Create(
                    43,
                    RecorderState.Recording,
                    RecordingAudioControlState.FromRouting(
                        AudioRouting.Mixed)));
        var leftToRight = WristTextureLayoutEngine.Layout(
            snapshot,
            WristLayoutOptions.Default);
        var rightToLeft = WristTextureLayoutEngine.Layout(
            snapshot,
            WristLayoutOptions.Default with
            {
                FlowDirection = WristFlowDirection.RightToLeft,
            });

        foreach (var semanticId in new[]
                 {
                     "audio.microphone.on",
                     "audio.muteAll",
                 })
        {
            var ltr = Assert.Single(leftToRight.HitTargets, target =>
                target.SemanticId == semanticId);
            var rtl = Assert.Single(rightToLeft.HitTargets, target =>
                target.SemanticId == semanticId);
            Assert.Equal(ltr.ElementId, rtl.ElementId);
            Assert.Equal(
                leftToRight.PixelWidth - ltr.Bounds.Right,
                rtl.Bounds.Left);
            Assert.Equal(ltr.Bounds.Width, rtl.Bounds.Width);
        }
    }

    [Fact]
    public void PositioningGridFitsEveryControlInsideTheSafeArea()
    {
        var snapshot = new WristUiProjector(EnglishUiLocalizer.Instance)
            .Project(
                new RecorderStatusSnapshot(
                    Revision: 44,
                    RecorderState.Ready,
                    RecorderAvailableActions.Start),
                WristPage.Positioning);

        var layout = WristTextureLayoutEngine.Layout(
            snapshot,
            WristLayoutOptions.Default);

        Assert.Equal(8, layout.HitTargets.Count);
        Assert.All(layout.HitTargets, target =>
        {
            Assert.Equal(WristElementKind.SecondaryAction, target.Kind);
            Assert.True(target.Bounds.Width >= target.MinimumTargetDp * 2);
            Assert.True(target.Bounds.Height >= target.MinimumTargetDp * 2);
            Assert.InRange(target.Bounds.Left, 32, 992);
            Assert.InRange(target.Bounds.Top, 128, 496);
            Assert.True(target.Bounds.Right <= 992);
            Assert.True(target.Bounds.Bottom <= 496);
        });
        for (var left = 0; left < layout.HitTargets.Count; left++)
        {
            for (var right = left + 1;
                 right < layout.HitTargets.Count;
                 right++)
            {
                Assert.False(layout.HitTargets[left].Bounds.Intersects(
                    layout.HitTargets[right].Bounds));
            }
        }
    }

    [Fact]
    public void RecordingPositioningGridKeepsStopAndAllControlsReachable()
    {
        var snapshot = new WristUiProjector(EnglishUiLocalizer.Instance)
            .Project(
                new RecorderStatusSnapshot(
                    Revision: 45,
                    RecorderState.Recording,
                    RecorderAvailableActions.Stop),
                WristPage.Positioning);

        var layout = WristTextureLayoutEngine.Layout(
            snapshot,
            WristLayoutOptions.Default);

        Assert.Equal(9, layout.HitTargets.Count);
        Assert.Equal(
            UiCommandId.ToggleRecording,
            layout.HitTargets[0].Command);
        Assert.Contains(
            layout.HitTargets,
            target => target.Command == UiCommandId.DockOverlayToWrist);
        Assert.Contains(
            layout.HitTargets,
            target => target.Command == UiCommandId.PinOverlayInWorld);
        Assert.All(layout.HitTargets, target =>
        {
            Assert.True(target.Bounds.Right <= 992);
            Assert.True(target.Bounds.Bottom <= 496);
        });
    }

    [Fact]
    public void DisabledActionIsLaidOutButNeverReturnedByHitTest()
    {
        var disabled = new UiActionSnapshot(
            SemanticId: "recording.start",
            Command: UiCommandId.ToggleRecording,
            IconSemanticId: "recording.start",
            ComponentRole: UiComponentRole.LargeFilledIconButton,
            ColorRole: UiColorRole.Recording,
            IsEnabled: false,
            VisibleLabel: new LocalizedText("recording.start.short", "REC"),
            AccessibleName: new LocalizedText(
                "recording.start.accessible",
                "Start recording"),
            Tooltip: new LocalizedText(
                "recording.start.tooltip",
                "Start recording"),
            MinimumTargetDp: 56);
        var snapshot = new WristUiSnapshot(
            1,
            RecorderState.NoSignal,
            new UiStateCue(
                UiColorRole.Error,
                "camera.no-signal",
                new LocalizedText("state.no-signal.label", "NO SIGNAL")),
            WristPage.Main,
            [disabled]);

        var layout = WristTextureLayoutEngine.Layout(
            snapshot,
            WristLayoutOptions.Default);

        Assert.Empty(layout.HitTargets);
        var actionElement = Assert.Single(layout.Elements, element =>
            element.ElementId == "action:recording.start");
        Assert.False(actionElement.IsEnabled);
        Assert.Null(layout.HitTest(
            actionElement.Bounds.Left + actionElement.Bounds.Width / 2,
            actionElement.Bounds.Top + actionElement.Bounds.Height / 2));
    }
}
