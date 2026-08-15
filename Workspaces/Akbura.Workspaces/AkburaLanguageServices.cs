namespace Akbura.Workspaces;

internal sealed class AkburaLanguageServices :  IAkburaLanguageServices
{
    public AkburaLanguageServices()
    {
        var referenceResolver = new AkcssReferenceResolver();

        Classification = new AkburaClassificationService(referenceResolver);

        Diagnostics = new AkburaDiagnosticService();

        Definition = new AkburaDefinitionService(referenceResolver);

        Completion = new AkburaCompletionService();

        QuickInfo = new AkburaQuickInfoService(referenceResolver);
    }

    public IAkburaClassificationService Classification { get; }

    public IAkburaDiagnosticService Diagnostics { get; }

    public IAkburaDefinitionService Definition { get; }

    public IAkburaCompletionService Completion { get; }

    public IAkburaQuickInfoService QuickInfo { get; }
}
