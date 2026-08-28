using System;
using System.Collections.Generic;
using System.Text;

namespace Akbura;
internal static class ProcessorCountHelper
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "MicrosoftCodeAnalysisCorrectness", 
        "RS1035:Do not use APIs banned for analyzers", 
        Justification = 
        "Used by the Akbura source generator for internal resource sizing; " +
        "not executed as a Roslyn analyzer, so RS1035 restrictions do not apply.\r\n")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("CodeQuality", "IDE0079:Remove unnecessary suppression", Justification = "False positive")]
    public static int GetProcessorCount()
    {
        var processorCount = Environment.ProcessorCount;
        return processorCount > 0 ? processorCount : 1;
    }
}
