namespace VRRecorder.Domain.Audio;

public static class AudioMixer
{
    public static float[] Mix(
        float[] desktop,
        float[] microphone,
        AudioGains gains)
    {
        ArgumentNullException.ThrowIfNull(desktop);
        ArgumentNullException.ThrowIfNull(microphone);

        if (desktop.Length != microphone.Length)
        {
            throw new ArgumentException(
                "Desktop and microphone buffers must have the same length.",
                nameof(microphone));
        }

        var output = new float[desktop.Length];
        for (var index = 0; index < output.Length; index++)
        {
            output[index] = (float)(
                (desktop[index] * gains.Desktop) +
                (microphone[index] * gains.Microphone));
        }

        return output;
    }
}
