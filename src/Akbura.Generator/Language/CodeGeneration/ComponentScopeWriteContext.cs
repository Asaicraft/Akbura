using System;
using System.Diagnostics;

namespace Akbura.Language.CodeGeneration;

/// <summary>
/// Carries the generated expressions and scope-local references shared while a
/// component generation scope is initialized.
/// </summary>
internal readonly ref struct ComponentScopeWriteContext
{
    public ComponentScopeWriteContext(
        string intermediateRootExpression,
        string baseUriExpression,
        string? fallbackServiceProviderExpression,
        string? nameScopeExpression,
        int scopeId,
        MarkupParentStackTraversalKind parentStackTraversalKind,
        ReadOnlySpan<ComponentElementPlan> elements,
        ReadOnlySpan<BindingElementReference> elementReferences)
    {
        IntermediateRootExpression = intermediateRootExpression;
        BaseUriExpression = baseUriExpression;
        FallbackServiceProviderExpression = fallbackServiceProviderExpression;
        NameScopeExpression = nameScopeExpression;
        ScopeId = scopeId;
        ParentStackTraversalKind = parentStackTraversalKind;
        Elements = elements;
        ElementReferences = elementReferences;
    }

    public string IntermediateRootExpression { get; }

    public string BaseUriExpression { get; }

    public string? FallbackServiceProviderExpression { get; }

    public string? NameScopeExpression { get; }

    public int ScopeId { get; }

    public MarkupParentStackTraversalKind ParentStackTraversalKind { get; }

    public ReadOnlySpan<ComponentElementPlan> Elements { get; }

    public ReadOnlySpan<BindingElementReference> ElementReferences { get; }

    public MarkupExtensionWriteContext ForElement(int elementId)
    {
        Debug.Assert((uint)elementId < (uint)Elements.Length);
        Debug.Assert(Elements[elementId].ScopeId == ScopeId);

        ref readonly var element = ref Elements[elementId];
        var targetExpression = element.Identifier;
        return new MarkupExtensionWriteContext(
            targetExpression,
            targetProperty: default,
            IntermediateRootExpression,
            BaseUriExpression,
            new MarkupParentStackPlan(
                Elements,
                elementId,
                ScopeId,
                ParentStackTraversalKind),
            FallbackServiceProviderExpression,
            NameScopeExpression,
            ScopeId,
            ElementReferences);
    }

}
