namespace Akbura.Workspaces.AutomaticPairing;

public interface IAkburaTypingService
{
    AkburaTypingResult GetResult(
        AkburaSyntacticDocument document,
        AkburaTypingCommand command,
        CancellationToken cancellationToken = default);
}
