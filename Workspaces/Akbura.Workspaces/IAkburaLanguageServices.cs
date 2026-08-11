namespace Akbura.Workspaces;

public interface IAkburaLanguageServices
{
    IAkburaClassificationService Classification { get; }

    IAkburaDiagnosticService Diagnostics { get; }

    IAkburaDefinitionService Definition { get; }

    IAkburaCompletionService Completion { get; }
}
