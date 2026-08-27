namespace Akbura.Workspaces.References;

/// <summary>
/// Stable language-neutral identity kind used by reference and rename services.
/// </summary>
public enum AkburaSymbolKind
{
    None = 0,
    Namespace,
    Component,
    Property,
    Event,
    State,
    Parameter,
    CommandParameter,
    UtilityParameter,
    MarkupItem,
    MarkupName,
    InjectedService,
    Command,
    Function,
    Hook,
    AkcssModule,
    AkcssClass,
    AkcssUtility,
    CSharpSymbol,
}
