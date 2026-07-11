using VRRecorder.Application.Diagnostics;

namespace VRRecorder.Application.Desktop;

public sealed record DesktopDiagnosticsState(
    long Revision,
    DesktopDiagnosticsStatus Status,
    DiagnosticBundleExport? LastExport);
