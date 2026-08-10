namespace Akbura.Workspaces;

internal sealed class AkburaLanguageServices :  IAkburaLanguageServices
{
    public AkburaLanguageServices()
    {
        Classification = new AkburaClassificationService();

        Definition = new AkburaDefinitionService();

        Completion = new AkburaCompletionService();
    }

    public IAkburaClassificationService Classification { get; }

    public IAkburaDefinitionService Definition { get; }

    public IAkburaCompletionService Completion { get; }
}
