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
