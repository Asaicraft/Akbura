using Akbura.Language.BoundTree;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Immutable;
using System.Linq;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;
using SyntaxFactory = Akbura.Language.Syntax.SyntaxFactory;

namespace Akbura.Language.Binder;

internal sealed partial class MarkupBinder
{
    private BoundMarkupForeachStatement BindMarkupForeachStatement(MarkupForeachStatementSyntax syntax)
    {
        using var diagnostics = ImmutableArrayBuilder<AkburaSemanticDiagnostic>.Rent();
        using var children = ImmutableArrayBuilder<BoundNode>.Rent();
        var owner = AkburaSemanticModel.GetContainingMarkupElement(syntax);
        var ownerSymbol = owner == null ? null : SemanticModel.GetSymbolInfo(owner).Symbol;
        var model = ownerSymbol switch
        {
            IMarkupComponentSymbol component => component.ContentModel,
            Symbols.IPropertySymbol property => SemanticModel.CreateMarkupPropertyElementContentModel(property),
            _ => default,
        };
        var ownerType = (ownerSymbol as IMarkupComponentSymbol)?.ComponentType;
        SemanticModel.AddMarkupForeachDestinationDiagnostics(syntax, model, ownerType, diagnostics);
        var header = syntax.Header.GetRawCSharpForeach();
        var source = default(CSharpOperationDefinition);
        var iterationType = default(CSharpSymbolDefinition);
        if (header == null || header.AwaitKeyword.RawKind != 0 || header.Type is CSharp.RefTypeSyntax)
        {
            diagnostics.Add(new(syntax.Header, ErrorCodes.AKBURA_SEMANTIC_UnsupportedForeachHeader, []));
        }
        else
        {
            var binding = SemanticModel.BindingSession.GetCSharpProbeBinder(syntax, BinderUsage.Markup)
                .BindMarkupForeachHeader(syntax);
            source = binding.Source.OperationDefinition;
            var itemType = binding.ItemType;
            if (header.Type is CSharp.IdentifierNameSyntax { Identifier.ValueText: "var" } &&
                TryGetMarkupForeachSourceElementType(binding.Source.TypeSymbol, out var sourceElementType))
            {
                itemType = sourceElementType;
            }
            iterationType = itemType == null ? default : new(itemType);
            SemanticModel.AddMarkupExpressionDiagnostics(syntax.Header, syntax.Header.ToFullString(),
                binding.Header, diagnostics);
            if (binding.ItemType is INamedTypeSymbol { IsRefLikeType: true })
            {
                diagnostics.Add(new(syntax.Header, ErrorCodes.AKBURA_SEMANTIC_UnsupportedForeachHeader, []));
            }
            if (header.Identifier.ValueText == "index")
            {
                diagnostics.Add(new(syntax.Header, ErrorCodes.AKBURA_SEMANTIC_ForeachIndexRedeclaration, []));
            }
        }
        var key = default(CSharpOperationDefinition);
        AkburaSyntax? keySyntax = null;
        if (syntax.KeyClause is { } keyClause)
        {
            var expression = CSharpProbeBuilder.ParseMarkupCondition(keyClause.Expression);
            var binding = SemanticModel.BindingSession.GetCSharpProbeBinder(keyClause.Expression, BinderUsage.Markup)
                .BindExpression(keyClause.Expression, expression, isBindingPath: false);
            key = GetLoopExpressionOperation(binding);
            keySyntax = keyClause.Expression;
            AddMarkupForeachKeyDiagnostics(keyClause.Expression, syntax, key, iterationKey: true, diagnostics);
            if (binding is BoundCSharpExpression bound)
            {
                SemanticModel.AddMarkupExpressionDiagnostics(keyClause.Expression, expression.ToFullString(),
                    bound.BindingResult, diagnostics);
            }
        }
        else if (GetImplicitMarkupForeachKey(syntax) is { } implicitKey)
        {
            var binding = BindMarkupForeachKey(implicitKey);
            key = binding.Binding.OperationDefinition;
            keySyntax = implicitKey;
            diagnostics.AddRange(binding.Diagnostics);
        }
        var body = owner == null ? ImmutableArray<BoundMarkupForeachBodyItem>.Empty :
            BindMarkupForeachBody(syntax.Body, owner, model, ownerType, diagnostics, children);
        var result = new BoundMarkupForeachStatement(syntax, this, source, iterationType, model.AllowedChildType,
            key, keySyntax, body,
            children.ToImmutable(), diagnostics.ToImmutable());
        SemanticModel.SetSemanticDiagnostics(syntax, result.Diagnostics);
        SemanticModel.SetCachedBoundNode(syntax, result);
        return result;
    }

    private ImmutableArray<BoundMarkupForeachBodyItem> BindMarkupForeachBody(MarkupCodeBlockSyntax block,
        MarkupElementSyntax owner, MarkupContentModel model, INamedTypeSymbol? ownerType,
        ImmutableArrayBuilder<AkburaSemanticDiagnostic> diagnostics, ImmutableArrayBuilder<BoundNode> children)
    {
        using var body = ImmutableArrayBuilder<BoundMarkupForeachBodyItem>.Rent();
        foreach (var syntax in block.Content)
        {
            if (syntax is MarkupCodeIfStatementSyntax conditional)
            {
                var binding = SemanticModel.BindingSession.GetCSharpProbeBinder(conditional.Condition, BinderUsage.Markup)
                    .BindMarkupCondition(conditional.Condition);
                SemanticModel.AddMarkupExpressionDiagnostics(conditional.Condition,
                    conditional.Condition.ToFullString(), binding, diagnostics);
                AddLoopIndexDeclarations(conditional.Condition,
                    CSharpProbeBuilder.ParseMarkupCondition(conditional.Condition), diagnostics);
                var branch = BindMarkupForeachBody(conditional.Body, owner, model, ownerType, diagnostics, children);
                var alternative = conditional.ElseBody == null ? ImmutableArray<BoundMarkupForeachBodyItem>.Empty :
                    BindMarkupForeachBody(conditional.ElseBody, owner, model, ownerType, diagnostics, children);
                body.Add(new(conditional, binding.OperationDefinition, body: branch, elseBody: alternative));
                continue;
            }
            if (syntax is MarkupCodeStatementSyntax code)
            {
                var statement = code.GetRawCSharpStatement();
                if (statement is not (CSharp.LocalDeclarationStatementSyntax or CSharp.BreakStatementSyntax or
                    CSharp.ContinueStatementSyntax))
                {
                    diagnostics.Add(new(code, ErrorCodes.AKBURA_SEMANTIC_UnsupportedForeachStatement,
                        [statement.Kind().ToString()]));
                }
                AddLoopIndexDeclarations(code, statement, diagnostics);
                var bound = SemanticModel.BindingSession.GetCSharpProbeBinder(code, BinderUsage.Markup)
                    .BindMarkupLoopStatement(code);
                children.Add(bound);
                SemanticModel.SetCachedBoundNode(code, bound);
                var binding = bound switch
                {
                    BoundLocalDeclarationStatement local => local.BindingResult,
                    BoundCSharpStatement ordinary => ordinary.BindingResult,
                    _ => CSharpBindingResult.Empty,
                };
                SemanticModel.AddMarkupExpressionDiagnostics(code, code.ToFullString(), binding, diagnostics);
                body.Add(new(code, binding.OperationDefinition));
                continue;
            }
            if (syntax is MarkupForeachStatementSyntax nested)
            {
                var bound = (BoundMarkupForeachStatement)SemanticModel.BindingSession.BindSemanticSyntax(nested);
                children.Add(bound);
                diagnostics.AddRange(bound.Diagnostics);
                body.Add(new(nested, foreachStatement: bound));
                continue;
            }
            var content = SemanticModel.CreateMarkupChildren(owner, SyntaxFactory.SingletonList(syntax), model,
                out var contentDiagnostics, ownerType);
            diagnostics.AddRange(contentDiagnostics);
            if (!content.IsDefaultOrEmpty)
            {
                body.Add(new(syntax, content: content));
                children.Add(SemanticModel.BindingSession.BindSemanticSyntax(syntax));
            }
        }
        return body.ToImmutable();
    }

    private static CSharpOperationDefinition GetLoopExpressionOperation(BoundExpression expression) =>
        expression switch
        {
            BoundCSharpExpression value => value.BindingResult.OperationDefinition,
            BoundLiteralExpression value => value.BindingResult.OperationDefinition,
            BoundBinaryExpression value => value.BindingResult.OperationDefinition,
            BoundCallExpression value => value.BindingResult.OperationDefinition,
            BoundConversionExpression conversion => GetLoopExpressionOperation(conversion.Operand),
            _ => default,
        };

    private static bool TryGetMarkupForeachSourceElementType(ITypeSymbol? sourceType,
        out ITypeSymbol? elementType)
    {
        if (sourceType is IArrayTypeSymbol array)
        {
            elementType = array.ElementType;
            return true;
        }
        if (sourceType is INamedTypeSymbol named && TryGetEnumerableElementType(named, out elementType))
        {
            return true;
        }
        if (sourceType != null)
        {
            foreach (var contract in sourceType.AllInterfaces)
            {
                if (TryGetEnumerableElementType(contract, out elementType))
                {
                    return true;
                }
            }
        }
        elementType = null;
        return false;
    }

    private static bool TryGetEnumerableElementType(INamedTypeSymbol type, out ITypeSymbol? elementType)
    {
        if (type.Name == "IEnumerable" && type.Arity == 1 &&
            type.ContainingNamespace.ToDisplayString() == "System.Collections.Generic")
        {
            elementType = type.TypeArguments[0];
            return true;
        }
        elementType = null;
        return false;
    }

    private static void AddLoopIndexDeclarations(AkburaSyntax syntax, SyntaxNode code,
        ImmutableArrayBuilder<AkburaSemanticDiagnostic> diagnostics)
    {
        if (code.DescendantNodesAndSelf().OfType<CSharp.VariableDeclaratorSyntax>()
                .Any(variable => variable.Identifier.ValueText == "index") ||
            code.DescendantNodesAndSelf().OfType<CSharp.SingleVariableDesignationSyntax>()
                .Any(variable => variable.Identifier.ValueText == "index") ||
            code.DescendantNodesAndSelf().OfType<CSharp.ParameterSyntax>()
                .Any(variable => variable.Identifier.ValueText == "index"))
        {
            diagnostics.Add(new(syntax, ErrorCodes.AKBURA_SEMANTIC_ForeachIndexRedeclaration, []));
        }
    }
}
