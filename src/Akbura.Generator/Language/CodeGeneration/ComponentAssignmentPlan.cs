using System.Collections.Immutable;

namespace Akbura.Language.CodeGeneration;

internal enum ComponentAssignmentKind : byte
{
    Property,
    FirstUpdateAction,
    Content,
}

internal readonly struct ComponentAssignmentReference(
    ComponentAssignmentKind kind, int index, ComponentContentTargetReference content = default)
{
    public ComponentAssignmentKind Kind { get; } = kind;
    public int Index { get; } = index;
    public ComponentContentTargetReference Content { get; } = content;
}

internal enum ComponentAssignmentPhase : byte
{
    Initial,
    LocalInitial,
    Update,
    HotReload,
}
