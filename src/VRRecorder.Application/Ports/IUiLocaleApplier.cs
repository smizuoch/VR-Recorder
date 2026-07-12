using VRRecorder.Application.Settings;

namespace VRRecorder.Application.Ports;

public interface IUiLocaleApplier
{
    void Apply(UiLocale locale);
}
