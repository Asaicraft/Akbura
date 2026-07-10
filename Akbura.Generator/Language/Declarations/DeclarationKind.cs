// This file is ported and adapted from the Roslyn (dotnet/roslyn)

#nullable disable

namespace Akbura.Language;

internal enum DeclarationKind : byte
{
    None = 0,
    Namespace,
    Using,
    Component,
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
