using FamilyQuant.Report;
using System;

namespace FamilyQuant.Ai;

public static class AiInterpretationInputBuilder
{
    public static AiInterpretationInput BuildProject(FamilyClusterFile project, QuantManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(manifest);
        ReportInterpretationCapture captured = ReportInterpretationInput.CaptureFromProject(project, manifest, AppVersionInfo.Current());
        return new(captured.Text, captured.Cells);
    }
}
