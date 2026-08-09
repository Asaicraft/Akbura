namespace Akbura.Workspaces;

public interface IAkburaLanguageServices
{
    IAkburaClassificationService Classification { get; }

    IAkburaDefinitionService Definition { get; }
}
