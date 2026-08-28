namespace Akbura.Workspaces.Projects;

public enum AkburaProjectLoadDiagnosticSeverity
{
    Information,
    Warning,
    Error,
}

public readonly record struct AkburaProjectLoadDiagnostic(
    AkburaProjectLoadDiagnosticSeverity Severity,
    string Message,
    string? ProjectPath = null);