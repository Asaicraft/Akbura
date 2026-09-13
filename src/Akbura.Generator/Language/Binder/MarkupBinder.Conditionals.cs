using Akbura.Language.BoundTree;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

namespace Akbura.Language.Binder;

internal sealed partial class MarkupBinder
{
    private BoundMarkupIfStatement BindMarkupIfStatement(MarkupIfStatementSyntax syntax)
    {
        var owner = AkburaSemanticModel.GetContainingMarkupElement(syntax)!;
        var ownerSymbol = SemanticModel.GetSymbolInfo(owner).Symbol;
        var model = ownerSymbol switch
        {
            IMarkupComponentSymbol component => component.ContentModel,
            Symbols.IPropertySymbol property => SemanticModel.CreateMarkupPropertyElementContentModel(property),
            _ => default,
        };
        var ownerType = (ownerSymbol as IMarkupComponentSymbol)?.ComponentType;
        var templateRoot = SemanticModel.GetConditionalTemplateRootInfo(owner);
        if (templateRoot.IsImplicitControlRoot)
        {
            model = templateRoot.ContentModel;
        }
        using var branches = ImmutableArrayBuilder<BoundMarkupConditionalBranch>.Rent();
        using var diagnostics = ImmutableArrayBuilder<AkburaSemanticDiagnostic>.Rent();
        branches.Add(BindMarkupConditionalBranch(syntax, syntax.Condition, syntax.Body, owner, model, ownerType));
        foreach (var clause in syntax.ElseIfClauses)
        {
            branches.Add(BindMarkupConditionalBranch(clause, clause.Condition, clause.Body, owner, model, ownerType));
        }

        if (syntax.ElseClause is { } finalClause)
        {
            branches.Add(BindMarkupConditionalBranch(finalClause, null, finalClause.Body, owner, model, ownerType));
        }

        foreach (var branch in branches.WrittenSpan)
        {
            diagnostics.AddRange(branch.Diagnostics);
        }

        SemanticModel.AddMarkupConditionalDestinationDiagnostics(syntax, model, ownerType, diagnostics);
        SemanticModel.AddMarkupConditionalTemplateRootDiagnostics(syntax, model, branches.WrittenSpan, diagnostics);
        SemanticModel.AddMarkupConditionalTemplateCaptureDiagnostics(syntax, diagnostics);

        var bound = new BoundMarkupIfStatement(syntax, this, branches.ToImmutable(), diagnostics.ToImmutable());
        SemanticModel.SetSemanticDiagnostics(syntax, bound.Diagnostics);
        SemanticModel.SetCachedBoundNode(syntax, bound);
        return bound;
    }

    private BoundMarkupConditionalBranch BindMarkupConditionalBranch(AkburaSyntax syntax,
        CSharpExpressionSyntax? conditionSyntax, MarkupBlockSyntax block, MarkupElementSyntax owner,
        MarkupContentModel model, INamedTypeSymbol? ownerType)
    {
        using var diagnostics = ImmutableArrayBuilder<AkburaSemanticDiagnostic>.Rent();
        var condition = default(CSharpOperationDefinition);
        if (conditionSyntax != null)
        {
            var binding = SemanticModel.BindingSession.GetCSharpProbeBinder(conditionSyntax, BinderUsage.Markup)
                .BindMarkupCondition(conditionSyntax);
            condition = binding.OperationDefinition;
            SemanticModel.AddMarkupExpressionDiagnostics(conditionSyntax,
                conditionSyntax.ToFullString(), binding, diagnostics);
        }

        var content = SemanticModel.CreateMarkupChildren(owner, block.Content, model,
            out var contentDiagnostics, ownerType);
        diagnostics.AddRange(contentDiagnostics);
        var valueOperation = default(CSharpOperationDefinition);
        string? literalValue = null;
        var isSynthesizedString = false;
        if (model.Kind == MarkupContentKind.Property &&
            !AkburaSemanticModel.HasMarkupElementOrConditionalContent(block.Content) &&
            AkburaSemanticModel.TryCreateMarkupContentValueExpression(block.Content,
                SemanticModel.BindingSession.MarkupWhitespace.GetEffectiveMode(owner),
                out var valueExpression, out literalValue, out isSynthesizedString,
                out var hasText, out var valueSyntax))
        {
            var binding = SemanticModel.BindMarkupAttributeExpression(valueSyntax, valueExpression,
                AkburaSemanticModel.GetMarkupContentTargetType(model));
            valueOperation = binding.OperationDefinition;
            SemanticModel.AddMarkupExpressionDiagnostics(valueSyntax, valueExpression.ToFullString(), binding, diagnostics);
            SemanticModel.AddMarkupContentValueDiagnostics(valueSyntax, model, binding, hasText, diagnostics);
        }

        using var children = ImmutableArrayBuilder<BoundNode>.Rent();
        foreach (var child in block.Content)
        {
            children.Add(SemanticModel.BindingSession.BindSemanticSyntax(child));
        }

        return new(syntax, this, conditionSyntax, condition, content, children.ToImmutable(), diagnostics.ToImmutable(),
            valueOperation, literalValue, isSynthesizedString);
    }
}
