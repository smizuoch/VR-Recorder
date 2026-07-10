using VRRecorder.Domain.Encoding;

namespace VRRecorder.Application.Encoding;

public sealed class EncoderUnavailableException : InvalidOperationException
{
    public EncoderUnavailableException(EncoderPreference preference)
        : base($"No usable encoder was found for preference {preference}.")
    {
        Preference = preference;
    }

    public EncoderPreference Preference { get; }
}
