using VRRecorder.Application.Settings;

namespace VRRecorder.Application.Tests.Settings;

public sealed class WristOverlayPoseContractTests
{
    [Fact]
    public void IdentityRotationPreservesRightHandedOpenVrTranslationInMetres()
    {
        var matrix = WristOverlayPoseContract.ToOpenVrMatrix34(
            new OverlayTransform([1, 2, -3], [0, 0, 0]));

        Assert.Equal(WristOverlayTrackingOrigin.Standing,
            WristOverlayPoseContract.WorldPinTrackingOrigin);
        Assert.Equal(
            [
                1f, 0f, 0f, 1f,
                0f, 1f, 0f, 2f,
                0f, 0f, 1f, -3f,
            ],
            matrix.ToArray());
    }

    [Theory]
    [MemberData(nameof(AxisRotations))]
    public void EulerDegreesUsePitchYawRollAxes(
        double[] eulerDegrees,
        float[] expectedRotation)
    {
        var matrix = WristOverlayPoseContract.ToOpenVrMatrix34(
            new OverlayTransform([0, 0, 0], eulerDegrees));

        Assert.Equal(expectedRotation, matrix.RotationToArray(),
            FloatPrecisionComparer.Instance);
    }

    [Fact]
    public void CombinedEulerRotationUsesRzRyRxMultiplicationOrder()
    {
        var matrix = WristOverlayPoseContract.ToOpenVrMatrix34(
            new OverlayTransform([0, 0, 0], [90, 90, 0]));

        Assert.Equal(
            [
                0f, 1f, 0f,
                0f, 0f, -1f,
                -1f, 0f, 0f,
            ],
            matrix.RotationToArray(),
            FloatPrecisionComparer.Instance);
    }

    [Theory]
    [MemberData(nameof(RoundTripTransforms))]
    public void ConvertsValidatedOpenVrMatricesBackToStoredTransforms(
        OverlayTransform expected)
    {
        var matrix = WristOverlayPoseContract.ToOpenVrMatrix34(expected);

        var actual = WristOverlayPoseContract.FromOpenVrMatrix34(matrix);

        Assert.Equal(
            expected.Position,
            actual.Position,
            MatrixPrecisionComparer.Instance);
        Assert.Equal(
            expected.RotationEuler,
            actual.RotationEuler,
            new DegreePrecisionComparer());
    }

    [Fact]
    public void SmallAndLargeNudgesMoveOnlyLocalRightAndUpAxes()
    {
        var start = new OverlayTransform([1, 2, 3], [10, 20, 30]);

        var right = WristOverlayPoseContract.Nudge(
            start,
            WristOverlayNudgeDirection.Right,
            WristOverlayNudgeSize.Small);
        var down = WristOverlayPoseContract.Nudge(
            right,
            WristOverlayNudgeDirection.Down,
            WristOverlayNudgeSize.Large);

        Assert.Equal([1.005, 1.98, 3], down.Position,
            DoublePrecisionComparer.Instance);
        Assert.Equal([10d, 20d, 30d], down.RotationEuler);
        Assert.Equal([1d, 2d, 3d], start.Position);
    }

    [Theory]
    [InlineData(OverlayPlacementMode.WristDock, 0.119, OverlayPlacementMode.WristDock)]
    [InlineData(OverlayPlacementMode.WristDock, 0.120, OverlayPlacementMode.WorldPin)]
    [InlineData(OverlayPlacementMode.WorldPin, 0.081, OverlayPlacementMode.WorldPin)]
    [InlineData(OverlayPlacementMode.WorldPin, 0.080, OverlayPlacementMode.WristDock)]
    public void DragReleaseUsesDetachAndReattachHysteresis(
        OverlayPlacementMode current,
        double distanceMetres,
        OverlayPlacementMode expected)
    {
        Assert.Equal(
            expected,
            WristOverlayPoseContract.ResolveDragReleaseMode(
                current,
                distanceMetres));
    }

    [Fact]
    public void DragDeltaMovesInParentSpaceAndMeasuresFromDefaultDock()
    {
        var start = WristOverlayPoseContract
            .CreateDefaultWristDockTransform();

        var moved = WristOverlayPoseContract.ApplyDragDelta(
            start,
            new WristOverlayDragDelta(
                RightMeters: 0.12,
                UpMeters: -0.03));

        Assert.Equal(
            [0.15, 0.02, -0.08],
            moved.Position,
            DoublePrecisionComparer.Instance);
        Assert.Equal(start.RotationEuler, moved.RotationEuler);
        Assert.Equal(
            Math.Sqrt(0.12 * 0.12 + 0.03 * 0.03),
            WristOverlayPoseContract.DistanceFromDefaultDock(moved),
            precision: 9);
    }

    [Fact]
    public void ReadbackComparisonAllowsOnlySubMillimetreAndSubDegreeRounding()
    {
        var expected = new OverlayTransform(
            [0.03, 0.05, -0.08],
            [25, 0, 10]);
        var rounded = new OverlayTransform(
            [0.0302, 0.0498, -0.0801],
            [25.04, -0.02, 10.03]);
        var moved = new OverlayTransform(
            [0.031, 0.05, -0.08],
            [25, 0, 10]);
        var rotated = new OverlayTransform(
            [0.03, 0.05, -0.08],
            [25.2, 0, 10]);
        var roundedMatrix = WristOverlayPoseContract.ToOpenVrMatrix34(rounded);
        var movedMatrix = WristOverlayPoseContract.ToOpenVrMatrix34(moved);

        Assert.True(WristOverlayPoseContract.MatchesReadback(
            rounded,
            expected));
        Assert.True(WristOverlayPoseContract.MatchesReadback(
            roundedMatrix,
            expected));
        Assert.False(WristOverlayPoseContract.MatchesReadback(
            moved,
            expected));
        Assert.False(WristOverlayPoseContract.MatchesReadback(
            movedMatrix,
            expected));
        Assert.False(WristOverlayPoseContract.MatchesReadback(
            rotated,
            expected));
    }

    [Fact]
    public void StoredLegacyFiniteValueIsPreservedButUnsafeRuntimePoseFailsClosed()
    {
        var legacy = new OverlayTransform([101, 0, 0], [385, 360, 370]);

        WristOverlayPoseContract.ValidateStoredTransform(legacy);

        Assert.Throws<InvalidDataException>(() =>
            WristOverlayPoseContract.ToOpenVrMatrix34(legacy));
        Assert.False(WristOverlayPoseContract.MatchesReadback(
            legacy,
            WristOverlayPoseContract.CreateDefaultWristDockTransform()));
    }

    [Fact]
    public void EveryNudgeDirectionMovesByTheSelectedContractDistance()
    {
        var start = new OverlayTransform([0, 0, 0], [1, 2, 3]);

        Assert.Equal(
            [0, WristOverlayPoseContract.SmallNudgeMeters, 0],
            WristOverlayPoseContract.Nudge(
                start,
                WristOverlayNudgeDirection.Up,
                WristOverlayNudgeSize.Small).Position);
        Assert.Equal(
            [0, -WristOverlayPoseContract.LargeNudgeMeters, 0],
            WristOverlayPoseContract.Nudge(
                start,
                WristOverlayNudgeDirection.Down,
                WristOverlayNudgeSize.Large).Position);
        Assert.Equal(
            [-WristOverlayPoseContract.SmallNudgeMeters, 0, 0],
            WristOverlayPoseContract.Nudge(
                start,
                WristOverlayNudgeDirection.Left,
                WristOverlayNudgeSize.Small).Position);
        Assert.Equal(
            [WristOverlayPoseContract.LargeNudgeMeters, 0, 0],
            WristOverlayPoseContract.Nudge(
                start,
                WristOverlayNudgeDirection.Right,
                WristOverlayNudgeSize.Large).Position);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            WristOverlayPoseContract.Nudge(
                start,
                (WristOverlayNudgeDirection)int.MaxValue,
                WristOverlayNudgeSize.Small));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            WristOverlayPoseContract.Nudge(
                start,
                WristOverlayNudgeDirection.Up,
                (WristOverlayNudgeSize)int.MaxValue));
    }

    [Fact]
    public void DragOperationsRejectInvalidModesDeltasAndRuntimeRange()
    {
        var start = WristOverlayPoseContract.CreateDefaultWristDockTransform();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            WristOverlayPoseContract.ResolveDragReleaseMode(
                (OverlayPlacementMode)int.MaxValue,
                0));
        foreach (var distance in new[]
                 {
                     double.NaN,
                     double.PositiveInfinity,
                     -0.001,
                 })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                WristOverlayPoseContract.ResolveDragReleaseMode(
                    OverlayPlacementMode.WristDock,
                    distance));
        }

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            WristOverlayPoseContract.ApplyDragDelta(
                start,
                new WristOverlayDragDelta(double.NaN, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            WristOverlayPoseContract.ApplyDragDelta(
                start,
                new WristOverlayDragDelta(0, double.NegativeInfinity)));
        Assert.Throws<InvalidDataException>(() =>
            WristOverlayPoseContract.ApplyDragDelta(
                new OverlayTransform([100, 0, 0], [0, 0, 0]),
                new WristOverlayDragDelta(0.001, 0)));
    }

    [Fact]
    public void StoredAndRuntimeTransformsRejectMalformedVectors()
    {
        Assert.Throws<ArgumentNullException>(() =>
            WristOverlayPoseContract.ValidateStoredTransform(null!));
        Assert.Throws<ArgumentNullException>(() =>
            WristOverlayPoseContract.ValidateStoredTransform(
                new OverlayTransform(null!, [0, 0, 0])));
        Assert.Throws<InvalidDataException>(() =>
            WristOverlayPoseContract.ValidateStoredTransform(
                new OverlayTransform([0, 0], [0, 0, 0])));
        Assert.Throws<InvalidDataException>(() =>
            WristOverlayPoseContract.ValidateStoredTransform(
                new OverlayTransform([0, 0, 0], [0, double.NaN, 0])));

        foreach (var position in new[]
                 {
                     new[] { 100.001, 0d, 0d },
                     new[] { 0d, -100.001, 0d },
                     new[] { 0d, 0d, 100.001 },
                 })
        {
            Assert.Throws<InvalidDataException>(() =>
                WristOverlayPoseContract.ToOpenVrMatrix34(
                    new OverlayTransform(position, [0, 0, 0])));
        }
    }

    [Fact]
    public void OpenVrMatrixValidationRejectsNonFiniteRangeAndInvalidRotation()
    {
        var identity = WristOverlayPoseContract.ToOpenVrMatrix34(
            new OverlayTransform([0, 0, 0], [0, 0, 0]));
        var gimbalLocked = WristOverlayPoseContract.FromOpenVrMatrix34(
            WristOverlayPoseContract.ToOpenVrMatrix34(
                new OverlayTransform([0, 0, 0], [20, 90, 0])));
        Assert.Equal(90, gimbalLocked.RotationEuler[1], precision: 4);

        foreach (var matrix in new[]
                 {
                     identity with { M00 = float.NaN },
                     identity with { M03 = 100.001f },
                     identity with { M13 = -100.001f },
                     identity with { M23 = 100.001f },
                     identity with { M00 = 2 },
                     identity with { M10 = 1 },
                     identity with { M22 = -1 },
                 })
        {
            Assert.Throws<InvalidDataException>(() =>
                WristOverlayPoseContract.FromOpenVrMatrix34(matrix));
        }

        Assert.False(WristOverlayPoseContract.MatchesReadback(
            identity with { M00 = float.NaN },
            WristOverlayPoseContract.CreateDefaultWristDockTransform()));
        Assert.False(WristOverlayPoseContract.MatchesReadback(
            identity,
            new OverlayTransform([double.NaN, 0, 0], [0, 0, 0])));
    }

    public static TheoryData<double[], float[]> AxisRotations =>
        new()
        {
            {
                [90, 0, 0],
                [
                    1, 0, 0,
                    0, 0, -1,
                    0, 1, 0,
                ]
            },
            {
                [0, 90, 0],
                [
                    0, 0, 1,
                    0, 1, 0,
                    -1, 0, 0,
                ]
            },
            {
                [0, 0, 90],
                [
                    0, -1, 0,
                    1, 0, 0,
                    0, 0, 1,
                ]
            },
        };

    public static TheoryData<OverlayTransform> RoundTripTransforms =>
        new()
        {
            new OverlayTransform([0.03, 0.05, -0.08], [25, 0, 10]),
            new OverlayTransform([1.25, 1.5, -2], [-30, 45, 70]),
            new OverlayTransform([-2, 3, 4], [90, 0, -90]),
        };

    private sealed class FloatPrecisionComparer : IEqualityComparer<float>
    {
        public static FloatPrecisionComparer Instance { get; } = new();

        public bool Equals(float x, float y) => Math.Abs(x - y) <= 0.000001F;

        public int GetHashCode(float value) => value.GetHashCode();
    }

    private sealed class DoublePrecisionComparer : IEqualityComparer<double>
    {
        public static DoublePrecisionComparer Instance { get; } = new();

        public bool Equals(double x, double y) => Math.Abs(x - y) <= 0.000000001;

        public int GetHashCode(double value) => value.GetHashCode();
    }

    private sealed class DegreePrecisionComparer : IEqualityComparer<double>
    {
        public bool Equals(double x, double y) =>
            Math.Abs(Normalize(x - y)) <= 0.0001;

        public int GetHashCode(double value) => value.GetHashCode();

        private static double Normalize(double value)
        {
            var normalized = value % 360;
            if (normalized > 180)
            {
                normalized -= 360;
            }
            else if (normalized < -180)
            {
                normalized += 360;
            }
            return normalized;
        }
    }

    private sealed class MatrixPrecisionComparer : IEqualityComparer<double>
    {
        public static MatrixPrecisionComparer Instance { get; } = new();

        public bool Equals(double x, double y) =>
            Math.Abs(x - y) <= 0.000001;

        public int GetHashCode(double value) => value.GetHashCode();
    }
}
