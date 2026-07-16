namespace VRRecorder.Application.Settings;

public static class WristOverlayPoseContract
{
    public const double MaximumAbsolutePositionMeters = 100;
    public const double SmallNudgeMeters = 0.005;
    public const double LargeNudgeMeters = 0.020;
    public const double DockDetachDistanceMeters = 0.120;
    public const double DockReattachDistanceMeters = 0.080;
    public const double PositionReadbackToleranceMeters = 0.0005;
    public const double RotationReadbackToleranceDegrees = 0.1;

    public static WristOverlayTrackingOrigin WorldPinTrackingOrigin =>
        WristOverlayTrackingOrigin.Standing;

    public static OverlayTransform CreateDefaultWristDockTransform() =>
        new(
            Position: [0.03, 0.05, -0.08],
            RotationEuler: [25, 0, 10]);

    public static OpenVrMatrix34 ToOpenVrMatrix34(OverlayTransform transform)
    {
        ValidateRuntimeTransform(transform);
        var pitch = DegreesToRadians(transform.RotationEuler[0]);
        var yaw = DegreesToRadians(transform.RotationEuler[1]);
        var roll = DegreesToRadians(transform.RotationEuler[2]);
        var sinPitch = Math.Sin(pitch);
        var cosPitch = Math.Cos(pitch);
        var sinYaw = Math.Sin(yaw);
        var cosYaw = Math.Cos(yaw);
        var sinRoll = Math.Sin(roll);
        var cosRoll = Math.Cos(roll);

        return new OpenVrMatrix34(
            M00: ToFloat(cosRoll * cosYaw),
            M01: ToFloat(cosRoll * sinYaw * sinPitch - sinRoll * cosPitch),
            M02: ToFloat(cosRoll * sinYaw * cosPitch + sinRoll * sinPitch),
            M03: ToFloat(transform.Position[0]),
            M10: ToFloat(sinRoll * cosYaw),
            M11: ToFloat(sinRoll * sinYaw * sinPitch + cosRoll * cosPitch),
            M12: ToFloat(sinRoll * sinYaw * cosPitch - cosRoll * sinPitch),
            M13: ToFloat(transform.Position[1]),
            M20: ToFloat(-sinYaw),
            M21: ToFloat(cosYaw * sinPitch),
            M22: ToFloat(cosYaw * cosPitch),
            M23: ToFloat(transform.Position[2]));
    }

    public static OverlayTransform FromOpenVrMatrix34(OpenVrMatrix34 matrix)
    {
        ValidateRuntimeMatrix(matrix);
        var yaw = Math.Asin(Math.Clamp(-matrix.M20, -1, 1));
        var cosYaw = Math.Cos(yaw);
        double pitch;
        double roll;
        if (Math.Abs(cosYaw) > 0.000001)
        {
            pitch = Math.Atan2(matrix.M21, matrix.M22);
            roll = Math.Atan2(matrix.M10, matrix.M00);
        }
        else
        {
            pitch = Math.Atan2(-matrix.M12, matrix.M11);
            roll = 0;
        }

        var transform = new OverlayTransform(
            [matrix.M03, matrix.M13, matrix.M23],
            [
                RadiansToDegrees(pitch),
                RadiansToDegrees(yaw),
                RadiansToDegrees(roll),
            ]);
        ValidateRuntimeTransform(transform);
        return transform;
    }

    public static OverlayTransform Nudge(
        OverlayTransform transform,
        WristOverlayNudgeDirection direction,
        WristOverlayNudgeSize size)
    {
        ValidateRuntimeTransform(transform);
        EnsureDefined(direction);
        EnsureDefined(size);
        var distance = size == WristOverlayNudgeSize.Small
            ? SmallNudgeMeters
            : LargeNudgeMeters;
        var position = (double[])transform.Position.Clone();
        switch (direction)
        {
            case WristOverlayNudgeDirection.Up:
                position[1] += distance;
                break;
            case WristOverlayNudgeDirection.Down:
                position[1] -= distance;
                break;
            case WristOverlayNudgeDirection.Left:
                position[0] -= distance;
                break;
            case WristOverlayNudgeDirection.Right:
                position[0] += distance;
                break;
        }

        var nudged = new OverlayTransform(
            position,
            (double[])transform.RotationEuler.Clone());
        ValidateRuntimeTransform(nudged);
        return nudged;
    }

    public static OverlayPlacementMode ResolveDragReleaseMode(
        OverlayPlacementMode currentMode,
        double distanceFromDockMeters)
    {
        EnsureDefined(currentMode);
        if (!double.IsFinite(distanceFromDockMeters) ||
            distanceFromDockMeters < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(distanceFromDockMeters),
                distanceFromDockMeters,
                "The dock distance must be a finite non-negative metre value.");
        }

        return currentMode switch
        {
            OverlayPlacementMode.WristDock
                when distanceFromDockMeters >= DockDetachDistanceMeters =>
                OverlayPlacementMode.WorldPin,
            OverlayPlacementMode.WorldPin
                when distanceFromDockMeters <= DockReattachDistanceMeters =>
                OverlayPlacementMode.WristDock,
            _ => currentMode,
        };
    }

    public static OverlayTransform ApplyDragDelta(
        OverlayTransform transform,
        WristOverlayDragDelta delta)
    {
        ValidateRuntimeTransform(transform);
        if (!double.IsFinite(delta.RightMeters) ||
            !double.IsFinite(delta.UpMeters))
        {
            throw new ArgumentOutOfRangeException(
                nameof(delta),
                delta,
                "The drag delta must contain finite metre values.");
        }

        var moved = new OverlayTransform(
            [
                transform.Position[0] + delta.RightMeters,
                transform.Position[1] + delta.UpMeters,
                transform.Position[2],
            ],
            (double[])transform.RotationEuler.Clone());
        ValidateRuntimeTransform(moved);
        return moved;
    }

    public static double DistanceFromDefaultDock(
        OverlayTransform dockSpaceTransform)
    {
        ValidateRuntimeTransform(dockSpaceTransform);
        var origin = CreateDefaultWristDockTransform().Position;
        var deltaX = dockSpaceTransform.Position[0] - origin[0];
        var deltaY = dockSpaceTransform.Position[1] - origin[1];
        var deltaZ = dockSpaceTransform.Position[2] - origin[2];
        return Math.Sqrt(
            deltaX * deltaX +
            deltaY * deltaY +
            deltaZ * deltaZ);
    }

    public static bool MatchesReadback(
        OverlayTransform actual,
        OverlayTransform expected)
    {
        if (!TryConvert(actual, out var actualMatrix) ||
            !TryConvert(expected, out var expectedMatrix))
        {
            return false;
        }

        return MatchesMatrices(actualMatrix, expectedMatrix);
    }

    public static bool MatchesReadback(
        OpenVrMatrix34 actual,
        OverlayTransform expected)
    {
        if (!TryConvert(expected, out var expectedMatrix) ||
            actual.ToArray().Any(value => !float.IsFinite(value)))
        {
            return false;
        }

        return MatchesMatrices(actual, expectedMatrix);
    }

    private static bool MatchesMatrices(
        OpenVrMatrix34 actualMatrix,
        OpenVrMatrix34 expectedMatrix)
    {
        var x = actualMatrix.M03 - expectedMatrix.M03;
        var y = actualMatrix.M13 - expectedMatrix.M13;
        var z = actualMatrix.M23 - expectedMatrix.M23;
        var positionDistance = Math.Sqrt(x * x + y * y + z * z);
        if (positionDistance > PositionReadbackToleranceMeters)
        {
            return false;
        }

        var trace =
            actualMatrix.M00 * expectedMatrix.M00 +
            actualMatrix.M01 * expectedMatrix.M01 +
            actualMatrix.M02 * expectedMatrix.M02 +
            actualMatrix.M10 * expectedMatrix.M10 +
            actualMatrix.M11 * expectedMatrix.M11 +
            actualMatrix.M12 * expectedMatrix.M12 +
            actualMatrix.M20 * expectedMatrix.M20 +
            actualMatrix.M21 * expectedMatrix.M21 +
            actualMatrix.M22 * expectedMatrix.M22;
        var cosine = Math.Clamp((trace - 1) / 2, -1, 1);
        var rotationDegrees = Math.Acos(cosine) * 180 / Math.PI;
        return rotationDegrees <= RotationReadbackToleranceDegrees;
    }

    public static void ValidateStoredTransform(OverlayTransform transform)
    {
        ArgumentNullException.ThrowIfNull(transform);
        EnsureVector(transform.Position, "position");
        EnsureVector(transform.RotationEuler, "rotationEuler");
    }

    private static void ValidateRuntimeTransform(OverlayTransform transform)
    {
        ValidateStoredTransform(transform);
        if (transform.Position.Any(value =>
                Math.Abs(value) > MaximumAbsolutePositionMeters))
        {
            throw new InvalidDataException(
                "Overlay positions must be between -100 and 100 metres.");
        }
    }

    private static void ValidateRuntimeMatrix(OpenVrMatrix34 matrix)
    {
        var values = matrix.ToArray();
        if (values.Any(value => !float.IsFinite(value)))
        {
            throw new InvalidDataException(
                "The OpenVR overlay matrix must contain finite values.");
        }
        if (Math.Abs(matrix.M03) > MaximumAbsolutePositionMeters ||
            Math.Abs(matrix.M13) > MaximumAbsolutePositionMeters ||
            Math.Abs(matrix.M23) > MaximumAbsolutePositionMeters)
        {
            throw new InvalidDataException(
                "Overlay positions must be between -100 and 100 metres.");
        }

        const double tolerance = 0.002;
        var row0 = new double[] { matrix.M00, matrix.M01, matrix.M02 };
        var row1 = new double[] { matrix.M10, matrix.M11, matrix.M12 };
        var row2 = new double[] { matrix.M20, matrix.M21, matrix.M22 };
        if (Math.Abs(Dot(row0, row0) - 1) > tolerance ||
            Math.Abs(Dot(row1, row1) - 1) > tolerance ||
            Math.Abs(Dot(row2, row2) - 1) > tolerance ||
            Math.Abs(Dot(row0, row1)) > tolerance ||
            Math.Abs(Dot(row0, row2)) > tolerance ||
            Math.Abs(Dot(row1, row2)) > tolerance)
        {
            throw new InvalidDataException(
                "The OpenVR overlay rotation must be orthonormal.");
        }
        var determinant =
            matrix.M00 * (matrix.M11 * matrix.M22 -
                          matrix.M12 * matrix.M21) -
            matrix.M01 * (matrix.M10 * matrix.M22 -
                          matrix.M12 * matrix.M20) +
            matrix.M02 * (matrix.M10 * matrix.M21 -
                          matrix.M11 * matrix.M20);
        if (Math.Abs(determinant - 1) > tolerance)
        {
            throw new InvalidDataException(
                "The OpenVR overlay rotation must preserve handedness.");
        }
    }

    private static double Dot(double[] left, double[] right) =>
        left[0] * right[0] +
        left[1] * right[1] +
        left[2] * right[2];

    private static bool TryConvert(
        OverlayTransform transform,
        out OpenVrMatrix34 matrix)
    {
        try
        {
            matrix = ToOpenVrMatrix34(transform);
            return true;
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidDataException)
        {
            matrix = default;
            return false;
        }
    }

    private static void EnsureVector(double[] vector, string name)
    {
        ArgumentNullException.ThrowIfNull(vector);
        if (vector.Length != 3 || vector.Any(value => !double.IsFinite(value)))
        {
            throw new InvalidDataException(
                $"The overlay {name} must contain three finite values.");
        }
    }

    private static void EnsureDefined<TEnum>(TEnum value)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                $"Unknown {typeof(TEnum).Name} value.");
        }
    }

    private static double DegreesToRadians(double degrees) =>
        degrees * Math.PI / 180;

    private static double RadiansToDegrees(double radians) =>
        radians * 180 / Math.PI;

    private static float ToFloat(double value) => (float)value;
}

public readonly record struct OpenVrMatrix34(
    float M00,
    float M01,
    float M02,
    float M03,
    float M10,
    float M11,
    float M12,
    float M13,
    float M20,
    float M21,
    float M22,
    float M23)
{
    public float[] ToArray() =>
    [
        M00, M01, M02, M03,
        M10, M11, M12, M13,
        M20, M21, M22, M23,
    ];

    public float[] RotationToArray() =>
    [
        M00, M01, M02,
        M10, M11, M12,
        M20, M21, M22,
    ];
}

public enum WristOverlayTrackingOrigin
{
    Standing,
}

public enum WristOverlayNudgeDirection
{
    Up,
    Down,
    Left,
    Right,
}

public enum WristOverlayNudgeSize
{
    Small,
    Large,
}

public readonly record struct WristOverlayDragDelta(
    double RightMeters,
    double UpMeters);
