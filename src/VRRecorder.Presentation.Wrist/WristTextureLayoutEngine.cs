using VRRecorder.DesignSystem;

namespace VRRecorder.Presentation.Wrist;

public static class WristTextureLayoutEngine
{
    public const int PixelWidth = 1024;
    public const int PixelHeight = 512;
    public const int PixelsPerDp = 2;

    private const int SafeInset = 32;
    private static readonly WristPixelRect HeaderBounds = new(
        SafeInset,
        24,
        PixelWidth - SafeInset * 2,
        88);
    private static readonly WristPixelRect PrimaryBounds = new(
        256,
        136,
        512,
        224);

    public static WristTextureLayout Layout(
        WristUiSnapshot snapshot,
        WristLayoutOptions options)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(options);
        ValidateOptions(options);
        var actions = snapshot.Actions.ToArray();
        ValidateActions(actions);

        List<WristLayoutElement> elements =
        [
            new(
                "panel:background",
                WristElementKind.Background,
                new WristPixelRect(0, 0, PixelWidth, PixelHeight),
                ZIndex: 0,
                IsEnabled: false,
                SemanticId: null,
                Command: null,
                MinimumTargetDp: 0),
            new(
                $"state:{snapshot.StateCue.IconSemanticId}",
                WristElementKind.StateCue,
                HeaderBounds,
                ZIndex: 10,
                IsEnabled: false,
                SemanticId: snapshot.StateCue.IconSemanticId,
                Command: null,
                MinimumTargetDp: 0),
        ];
        List<WristHitTarget> hitTargets = [];
        if (actions.Length == 0)
        {
            return CreateLayout(options, elements, hitTargets);
        }
        if (snapshot.Page == WristPage.Positioning)
        {
            AddPositioningActions(
                actions,
                options.FlowDirection,
                elements,
                hitTargets);
            EnsureHitTargetsDoNotOverlap(hitTargets);
            return CreateLayout(options, elements, hitTargets);
        }

        var primary = SelectPrimary(actions);
        AddAction(
            primary,
            WristElementKind.PrimaryAction,
            MirrorIfRequired(PrimaryBounds, options.FlowDirection),
            zIndex: 30,
            elements,
            hitTargets);

        var secondary = actions
            .Where(action => !ReferenceEquals(action, primary))
            .ToArray();
        AddSecondaryActions(
            secondary,
            options.FlowDirection,
            elements,
            hitTargets);
        EnsureHitTargetsDoNotOverlap(hitTargets);
        return CreateLayout(options, elements, hitTargets);
    }

    private static WristTextureLayout CreateLayout(
        WristLayoutOptions options,
        IEnumerable<WristLayoutElement> elements,
        IEnumerable<WristHitTarget> hitTargets) =>
        new(
            PixelWidth,
            PixelHeight,
            PixelsPerDp,
            options,
            elements,
            hitTargets);

    private static UiActionSnapshot SelectPrimary(
        UiActionSnapshot[] actions) =>
        actions.FirstOrDefault(action =>
            action.SemanticId == "recording.stop") ??
        actions.FirstOrDefault(action =>
            action.ComponentRole == UiComponentRole.LargeFilledIconButton) ??
        actions[0];

    private static void AddSecondaryActions(
        UiActionSnapshot[] actions,
        WristFlowDirection direction,
        ICollection<WristLayoutElement> elements,
        ICollection<WristHitTarget> hitTargets)
    {
        if (actions.Length == 0)
        {
            return;
        }

        const int gap = 32;
        const int minimumWidth = 176;
        const int top = 384;
        const int height = 112;
        var widths = actions
            .Select(action => Math.Max(
                minimumWidth,
                checked(action.MinimumTargetDp * PixelsPerDp)))
            .ToArray();
        var totalWidth = checked(widths.Sum() + gap * (actions.Length - 1));
        if (totalWidth > PixelWidth - SafeInset * 2)
        {
            throw new InvalidOperationException(
                "The wrist action targets do not fit within the safe area.");
        }

        var left = (PixelWidth - totalWidth) / 2;
        for (var index = 0; index < actions.Length; index++)
        {
            var bounds = new WristPixelRect(
                left,
                top,
                widths[index],
                height);
            AddAction(
                actions[index],
                WristElementKind.SecondaryAction,
                MirrorIfRequired(bounds, direction),
                zIndex: 20,
                elements,
                hitTargets);
            left = checked(left + widths[index] + gap);
        }
    }

    private static void AddPositioningActions(
        UiActionSnapshot[] actions,
        WristFlowDirection direction,
        ICollection<WristLayoutElement> elements,
        ICollection<WristHitTarget> hitTargets)
    {
        const int top = 128;
        const int bottom = PixelHeight - 16;
        const int gap = 16;
        const int maximumColumns = 5;
        const int maximumRows = 2;
        var columns = actions.Length <= 6
            ? 3
            : actions.Length <= 8
                ? 4
                : maximumColumns;
        var rows = checked((actions.Length + columns - 1) / columns);
        if (rows > maximumRows)
        {
            throw new InvalidOperationException(
                "The positioning controls exceed the supported grid.");
        }

        var availableWidth = checked(
            PixelWidth - SafeInset * 2 - gap * (columns - 1));
        var availableHeight = checked(
            bottom - top - gap * (rows - 1));
        var cellWidth = availableWidth / columns;
        var cellHeight = availableHeight / rows;
        for (var index = 0; index < actions.Length; index++)
        {
            var row = index / columns;
            var column = index % columns;
            var left = checked(SafeInset + column * (cellWidth + gap));
            var cellRight = column == columns - 1
                ? PixelWidth - SafeInset
                : checked(left + cellWidth);
            var cellBottom = row == rows - 1
                ? bottom
                : checked(top + (row + 1) * cellHeight + row * gap);
            var bounds = new WristPixelRect(
                left,
                checked(top + row * (cellHeight + gap)),
                checked(cellRight - left),
                checked(cellBottom -
                        (top + row * (cellHeight + gap))));
            var action = actions[index];
            if (bounds.Width < action.MinimumTargetDp * PixelsPerDp ||
                bounds.Height < action.MinimumTargetDp * PixelsPerDp)
            {
                throw new InvalidOperationException(
                    "A positioning control does not meet its minimum target size.");
            }
            AddAction(
                action,
                WristElementKind.SecondaryAction,
                MirrorIfRequired(bounds, direction),
                zIndex: 20,
                elements,
                hitTargets);
        }
    }

    private static void AddAction(
        UiActionSnapshot action,
        WristElementKind kind,
        WristPixelRect bounds,
        int zIndex,
        ICollection<WristLayoutElement> elements,
        ICollection<WristHitTarget> hitTargets)
    {
        var elementId = $"action:{action.SemanticId}";
        elements.Add(new WristLayoutElement(
            elementId,
            kind,
            bounds,
            zIndex,
            action.IsEnabled,
            action.SemanticId,
            action.Command,
            action.MinimumTargetDp));
        if (action.IsEnabled)
        {
            hitTargets.Add(new WristHitTarget(
                elementId,
                action.SemanticId,
                action.Command,
                kind,
                bounds,
                action.MinimumTargetDp,
                zIndex));
        }
    }

    private static WristPixelRect MirrorIfRequired(
        WristPixelRect bounds,
        WristFlowDirection direction) =>
        direction == WristFlowDirection.RightToLeft
            ? new WristPixelRect(
                PixelWidth - bounds.Right,
                bounds.Top,
                bounds.Width,
                bounds.Height)
            : bounds;

    private static void ValidateOptions(WristLayoutOptions options)
    {
        if (!Enum.IsDefined(options.FlowDirection))
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.FlowDirection,
                "The wrist flow direction is not defined.");
        }

        if (!double.IsFinite(options.TextScale) ||
            options.TextScale < 1.0 ||
            options.TextScale > 2.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.TextScale,
                "The wrist text scale must be between 100% and 200%.");
        }
    }

    private static void ValidateActions(
        UiActionSnapshot[] actions)
    {
        var semanticIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var action in actions)
        {
            if (string.IsNullOrWhiteSpace(action.SemanticId) ||
                !semanticIds.Add(action.SemanticId))
            {
                throw new InvalidOperationException(
                    "Wrist action semantic IDs must be non-blank and unique.");
            }

            if (action.MinimumTargetDp < 48)
            {
                throw new InvalidOperationException(
                    "Wrist action targets must be at least 48 dp.");
            }
        }
    }

    private static void EnsureHitTargetsDoNotOverlap(
        List<WristHitTarget> hitTargets)
    {
        for (var left = 0; left < hitTargets.Count; left++)
        {
            for (var right = left + 1;
                 right < hitTargets.Count;
                 right++)
            {
                if (hitTargets[left].Bounds.Intersects(
                        hitTargets[right].Bounds))
                {
                    throw new InvalidOperationException(
                        "Wrist action targets must not overlap.");
                }
            }
        }
    }
}
