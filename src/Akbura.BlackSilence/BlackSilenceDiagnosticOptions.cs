using Akbura.Diagnostics;
using Microsoft.CodeAnalysis.Diagnostics;
using System;

namespace Akbura.BlackSilence;

internal enum BlackSilenceDiagnosticMode
{
    Off,
    Shadow,
    Publish,
}

internal readonly record struct BlackSilenceDiagnosticOptions(
    BlackSilenceDiagnosticMode Mode,
    AkburaDiagnosticPublisher Publisher,
    bool IsDesignTimeBuild,
    bool WorkspaceDiagnosticsActive)
{
    public bool ComputeDiagnostics => Mode != BlackSilenceDiagnosticMode.Off;

    public bool PublishDiagnostics => Mode == BlackSilenceDiagnosticMode.Publish &&
        AkburaDiagnosticPublicationPolicy.ShouldPublishGenerator(Publisher, IsDesignTimeBuild, WorkspaceDiagnosticsActive);

    public static BlackSilenceDiagnosticOptions Create(AnalyzerConfigOptions options)
    {
        options.TryGetValue("build_property.AkburaBlackSilenceDiagnostics", out var mode);
        options.TryGetValue("build_property.AkburaDiagnosticPublisher", out var publisher);
        options.TryGetValue("build_property.DesignTimeBuild", out var designTime);
        options.TryGetValue("build_property.AkburaWorkspaceDiagnosticsActive", out var workspaceActive);

        return new BlackSilenceDiagnosticOptions(
            Parse(mode, BlackSilenceDiagnosticMode.Shadow),
            Parse(publisher, AkburaDiagnosticPublisher.Auto),
            bool.TryParse(designTime, out var isDesignTime) && isDesignTime,
            bool.TryParse(workspaceActive, out var isWorkspaceActive) && isWorkspaceActive);
    }

    private static T Parse<T>(string? value, T fallback) where T : struct, Enum
    {
        return Enum.TryParse<T>(value, ignoreCase: true, out var result) && Enum.IsDefined(typeof(T), result)
            ? result : fallback;
    }
}
