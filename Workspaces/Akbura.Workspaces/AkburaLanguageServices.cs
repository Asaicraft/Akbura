namespace Akbura.Workspaces;

internal sealed class AkburaLanguageServices :  IAkburaLanguageServices
{
    public AkburaLanguageServices()
    {
        Classification = new AkburaClassificationService();

        Diagnostics = new AkburaDiagnosticService();

        Definition = new AkburaDefinitionService();

        Completion = new AkburaCompletionService();
    }

    public IAkburaClassificationService Classification { get; }

    public IAkburaDiagnosticService Diagnostics { get; }

    public IAkburaDefinitionService Definition { get; }

    public IAkburaCompletionService Completion { get; }
}
