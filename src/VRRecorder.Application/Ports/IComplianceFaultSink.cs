namespace VRRecorder.Application.Ports;

public interface IComplianceFaultSink
{
    ValueTask EnterComplianceFaultAsync();
}
