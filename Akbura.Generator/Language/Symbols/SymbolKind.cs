namespace Akbura.Language.Symbols;

internal enum SymbolKind
{
    None = 0,
    Namespace,
    Alias,
    Component,
    MarkupComponent,
    State,
    Parameter,
    InjectedService,
    Command,
    Function,
    UserHook,
    AkcssClass,
    AkcssUtility,
    AkcssProperty,
    CSharpSymbol,
    Error,
}
