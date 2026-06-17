namespace Akbura.Language.Symbols;

internal interface IUserHookSymbol : ISymbol
{
    string InvocationName { get; }

    CSharpSymbolDefinition HookType { get; }

    CSharpSymbolDefinition UseHookMethod { get; }

    CSharpSymbolDefinition ReturnType { get; }
}
