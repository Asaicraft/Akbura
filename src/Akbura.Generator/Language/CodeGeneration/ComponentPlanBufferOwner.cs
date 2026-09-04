using Akbura.Pools;
using System.Diagnostics;

namespace Akbura.Language.CodeGeneration;

/// <summary>
/// Temporarily owns pooled component-plan buffers while the plan is assembled.
/// Ownership is transferred to <see cref="ComponentPlan"/> by
/// <see cref="MoveToPlan"/>.
/// </summary>
internal ref struct ComponentPlanBufferOwner
{
    private bool _ownsBuffers;

    public ComponentPlanBufferOwner(AkcssComponentActivatorPlan akcss)
    {
        this = default;

        Akcss = akcss;
        _ownsBuffers = true;
    }

    public PooledImmutableList<ComponentElementPlan> Elements;

    public PooledImmutableList<int> RootElementIds;

    public PooledImmutableList<int> ChildElementIds;

    public PooledImmutableList<ComponentScopePlan> Scopes;

    public PooledImmutableList<int> ScopeElementIds;

    public PooledImmutableList<int> ScopeRootElementIds;

    public PooledImmutableList<ComponentPropertyWritePlan> PropertyWrites;

    public PooledImmutableList<ComponentCSharpValuePlan> CSharpValues;

    public PooledImmutableList<MarkupExtensionResultPlan> MarkupExtensions;

    public PooledImmutableList<BindingWritePlan> Bindings;

    public PooledImmutableList<ComponentPropertySubscriptionPlan> PropertySubscriptions;

    public PooledImmutableList<ComponentNameAssignmentPlan> NameAssignments;

    public PooledImmutableList<ComponentRoutedEventPlan> RoutedEvents;

    public PooledImmutableList<ComponentCommandBindingPlan> CommandBindings;

    public PooledImmutableList<ComponentFirstUpdateActionPlan> FirstUpdateActions;

    public PooledImmutableList<ComponentPropertyElementPlan> PropertyElements;

    public PooledImmutableList<ComponentPropertyContentPlan> PropertyContents;

    public PooledImmutableList<ComponentCollectionContentPlan> CollectionContents;

    public PooledImmutableList<ComponentContentItemPlan> ContentItems;

    public PooledImmutableList<ComponentDeferredContentPlan> DeferredContents;

    public PooledImmutableList<ComponentTemplatePlan> Templates;

    public PooledImmutableList<BindingElementReference> ElementReferences;

    public PooledImmutableList<ComponentRenderStatementPlan> RenderStatements;

    public AkcssComponentActivatorPlan Akcss;

    public ComponentPlan MoveToPlan(ComponentLifecyclePlan lifecycle)
    {
        Debug.Assert(_ownsBuffers);

        var plan = new ComponentPlan(
            Elements,
            RootElementIds,
            ChildElementIds,
            Scopes,
            ScopeElementIds,
            ScopeRootElementIds,
            PropertyWrites,
            CSharpValues,
            MarkupExtensions,
            Bindings,
            PropertySubscriptions,
            NameAssignments,
            RoutedEvents,
            CommandBindings,
            FirstUpdateActions,
            PropertyElements,
            PropertyContents,
            CollectionContents,
            ContentItems,
            DeferredContents,
            Templates,
            ElementReferences,
            lifecycle,
            RenderStatements,
            Akcss);

        this = default;

        return plan;
    }

    public void Dispose()
    {
        if (!_ownsBuffers)
        {
            return;
        }

        var plan = MoveToPlan(default);

        plan.ReturnToPool();
    }
}
