namespace Akbura.Language.Declarations;

internal enum AkburaDeclarationKind
{
    None = 0,
    Compilation,
    Component,
    Namespace,
    Using,
    State,
    Parameter,
    InjectedService,
    Command,
    UseEffect,
    UserHook,
    MarkupRoot,
    MarkupElement,
    AkcssModule,
    AkcssUsing,
    AkcssStyle,
    AkcssUtility,
}
