using Akbura.Language.Operations;
using Akbura.Language.Syntax;
using System.Collections.Immutable;

namespace Akbura.Language.CodeGeneration;

internal readonly struct ComponentForeachRootPlan
{
    public ComponentForeachRootPlan(MarkupElementSyntax syntax, int scopeId, int elementId,
        IMarkupForeachKeyOperation? key = null)
    {
        Syntax = syntax;
        ScopeId = scopeId;
        ElementId = elementId;
        Key = key;
    }

    public MarkupElementSyntax Syntax { get; }
    public int ScopeId { get; }
    public int ElementId { get; }
    public IMarkupForeachKeyOperation? Key { get; }
}

/// <summary>
/// One template definition. Its node slots are instantiated separately for
/// each source occurrence, never as per-item fields on the component.
/// </summary>
internal readonly struct ComponentForeachPlan
{
    public ComponentForeachPlan(
        int id,
        int ownerElementId,
        IMarkupForeachOperation operation,
        string templateRevision,
        string keyContractIdentity,
        ImmutableArray<ComponentForeachRootPlan> roots)
    {
        Id = id;
        OwnerElementId = ownerElementId;
        Operation = operation;
        TemplateRevision = templateRevision;
        KeyContractIdentity = keyContractIdentity;
        Roots = roots;
    }

    public int Id { get; }

    public int OwnerElementId { get; }

    public IMarkupForeachOperation Operation { get; }

    public string TemplateRevision { get; }

    public string KeyContractIdentity { get; }

    public ImmutableArray<ComponentForeachRootPlan> Roots { get; }
}
