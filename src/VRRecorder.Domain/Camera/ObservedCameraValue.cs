namespace VRRecorder.Domain.Camera;

public readonly record struct ObservedCameraValue<T>
    where T : struct
{
    internal ObservedCameraValue(bool isKnown, T value)
    {
        IsKnown = isKnown;
        Value = value;
    }

    public bool IsKnown { get; }

    public T Value { get; }

}

public static class ObservedCameraValue
{
    public static ObservedCameraValue<T> Known<T>(T value)
        where T : struct =>
        new(true, value);

    public static ObservedCameraValue<T> Unknown<T>()
        where T : struct =>
        new(false, default);
}
