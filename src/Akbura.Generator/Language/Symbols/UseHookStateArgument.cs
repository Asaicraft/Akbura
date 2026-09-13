namespace Akbura.Language.Symbols;

internal readonly struct UseHookStateArgument
{
    public UseHookStateArgument(int argumentIndex, IStateSymbol state)
    {
        ArgumentIndex = argumentIndex;
        State = state;
    }

    public int ArgumentIndex { get; }

    public IStateSymbol State { get; }
}
