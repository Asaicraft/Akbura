namespace Akbura.Language.Symbols;

internal enum SymbolKind
{
    None = 0,
    Namespace,
    Alias,
    Component,
    AkburaComponent,
    MarkupComponent,
    Property,
    Event,
    State,
    Parameter,
    CommandParameter,
    TailwindUtilityParameter,
    MarkupItem,
    InjectedService,
    Command,
    Function,
    UseHook,
    AkcssModule,
    AkcssClass,
    AkcssUtility,
    AkcssProperty,
    CSharpSymbol,
    Error,
}
