namespace Akbura.Workspaces;

internal sealed class AkburaLanguageServices :
    IAkburaLanguageServices
{
    public AkburaLanguageServices()
    {
        Classification = new AkburaClassificationService();
    }

    public IAkburaClassificationService Classification { get; }
}
