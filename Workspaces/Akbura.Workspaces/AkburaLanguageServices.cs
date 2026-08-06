namespace Akbura.Workspaces;

internal sealed class AkburaLanguageServices :  IAkburaLanguageServices
{
    public AkburaLanguageServices()
    {
        Classification = new AkburaClassificationService();

        Definition = new AkburaDefinitionService();
    }

    public IAkburaClassificationService Classification { get; }

    public IAkburaDefinitionService Definition { get; }
}
