namespace Akbura.Language.Symbols;

internal static class CommandSymbolDocumentation
{
    public const string IsExecuting =
        "Observes whether the command is currently executing.";

    public const string CanExecute =
        "Observes whether the command is currently available for execution.";

    public const string Execute =
        "Executes the command asynchronously and returns its logical result.";

    public static string? GetMemberDocumentation(string memberName)
    {
        return memberName switch
        {
            "IsExecuting" => IsExecuting,
            "CanExecute" => CanExecute,
            "Execute" => Execute,
            _ => null,
        };
    }
}
