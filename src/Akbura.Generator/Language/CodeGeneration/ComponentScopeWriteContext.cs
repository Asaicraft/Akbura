using System;
using CSharpSyntaxFacts = Microsoft.CodeAnalysis.CSharp.SyntaxFacts;
using CSharpSyntaxKind = Microsoft.CodeAnalysis.CSharp.SyntaxKind;

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
        ReadOnlySpan<ComponentElementPlan> elements,
        ReadOnlySpan<BindingElementReference> elementReferences)
    {
        IntermediateRootExpression = intermediateRootExpression;
        BaseUriExpression = baseUriExpression;
        FallbackServiceProviderExpression = fallbackServiceProviderExpression;
        NameScopeExpression = nameScopeExpression;
        ScopeId = scopeId;
        Elements = elements;
        ElementReferences = elementReferences;
    }

    public string IntermediateRootExpression { get; }

    public string BaseUriExpression { get; }

    public string? FallbackServiceProviderExpression { get; }

    public string? NameScopeExpression { get; }

    public int ScopeId { get; }

    public ReadOnlySpan<ComponentElementPlan> Elements { get; }

    public ReadOnlySpan<BindingElementReference> ElementReferences { get; }

    public MarkupExtensionWriteContext ForElement(int elementId)
    {
        if ((uint)elementId >= (uint)Elements.Length ||
            Elements[elementId].ScopeId != ScopeId)
        {
            throw new ArgumentOutOfRangeException(nameof(elementId));
        }

        var targetExpression = EscapeIdentifier(Elements[elementId].Identifier);
        return new MarkupExtensionWriteContext(
            targetExpression,
            targetProperty: default,
            IntermediateRootExpression,
            BaseUriExpression,
            new MarkupParentStackPlan(Elements, elementId, ScopeId),
            FallbackServiceProviderExpression,
            NameScopeExpression,
            ScopeId,
            ElementReferences);
    }

    private static string EscapeIdentifier(string identifier)
    {
        return CSharpSyntaxFacts.GetKeywordKind(identifier) != CSharpSyntaxKind.None ||
            CSharpSyntaxFacts.GetContextualKeywordKind(identifier) != CSharpSyntaxKind.None
                ? "@" + identifier
                : identifier;
    }
}
