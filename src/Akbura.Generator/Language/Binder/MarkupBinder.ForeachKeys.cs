using Akbura.Language.BoundTree;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;
using CSharpSyntaxFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Akbura.Language.Binder;

internal sealed partial class MarkupBinder
{
    private BoundMarkupForeachKey BindMarkupForeachKey(MarkupAttachedPropertyAttributeSyntax attribute)
    {
        using var diagnostics = ImmutableArrayBuilder<AkburaSemanticDiagnostic>.Rent();
        var loop = CSharpProbeBuilder.GetContainingMarkupForeach(attribute);
        var binding = CSharpBindingResult.Empty;
        var isIterationKey = loop != null && loop.KeyClause == null &&
            ReferenceEquals(GetImplicitMarkupForeachKey(loop), attribute);
        CSharp.ExpressionSyntax? expression = AkburaSemanticModel.GetMarkupAttributeValue(attribute) switch
        {
            MarkupDynamicAttributeValueSyntax dynamicValue =>
                AkburaSemanticModel.ParseInlineExpression(dynamicValue.Expression),
            MarkupLiteralAttributeValueSyntax literal => CSharpSyntaxFactory.LiteralExpression(
                Microsoft.CodeAnalysis.CSharp.SyntaxKind.StringLiteralExpression,
                CSharpSyntaxFactory.Literal(AkburaSemanticModel.GetMarkupLiteralAttributeValueText(literal))),
            _ => null,
        };
        if (loop == null || expression == null)
        {
            diagnostics.Add(new(attribute, ErrorCodes.AKBURA_SEMANTIC_ForeachKeyInvalid,
                ["x.id requires a value inside a markup foreach iteration"]));
        }
        else
        {
            binding = SemanticModel.BindMarkupAttributeExpression(attribute, expression, targetType: null);
            SemanticModel.AddMarkupExpressionDiagnostics(attribute, expression.ToFullString(), binding, diagnostics);
            AddMarkupForeachKeyDiagnostics(attribute, loop, binding.OperationDefinition, isIterationKey, diagnostics);
        }
        var seen = false;
        foreach (var candidate in AkburaSemanticModel.GetContainingMarkupElement(attribute)!.StartTag!.Attributes)
        {
            if (!AkburaSemanticModel.IsMarkupForeachKeyDirective(candidate))
            {
                continue;
            }
            if (ReferenceEquals(candidate, attribute))
            {
                if (seen)
                {
                    diagnostics.Add(new(attribute, ErrorCodes.AKBURA_SEMANTIC_ForeachKeyInvalid,
                        ["an element may declare x.id only once"]));
                }
                break;
            }
            seen = true;
        }
        var result = new BoundMarkupForeachKey(attribute, this,
            SemanticModel.GetContainingMarkupComponentSymbol(attribute), binding, isIterationKey, diagnostics.ToImmutable());
        SemanticModel.SetSemanticDiagnostics(attribute, result.Diagnostics);
        SemanticModel.SetCachedBoundNode(attribute, result);
        return result;
    }

    private static MarkupAttachedPropertyAttributeSyntax? GetImplicitMarkupForeachKey(MarkupForeachStatementSyntax loop)
    {
        var content = loop.Body.Content.Where(syntax => syntax is not MarkupTextLiteralSyntax text ||
            !string.IsNullOrWhiteSpace(text.ToFullString())).ToArray();
        return content.Length == 1 && content[0] is MarkupElementContentSyntax root
            ? root.Element.StartTag?.Attributes.OfType<MarkupAttachedPropertyAttributeSyntax>()
                .FirstOrDefault(AkburaSemanticModel.IsMarkupForeachKeyDirective)
            : null;
    }

    private static void AddMarkupForeachKeyDiagnostics(AkburaSyntax syntax, MarkupForeachStatementSyntax loop,
        CSharpOperationDefinition definition, bool iterationKey,
        ImmutableArrayBuilder<AkburaSemanticDiagnostic> diagnostics)
    {
        if (definition.IsDefault)
        {
            return;
        }
        var type = definition.Type;
        var reason = type is { TypeKind: TypeKind.Delegate or TypeKind.Pointer or TypeKind.FunctionPointer } ||
            type?.SpecialType == SpecialType.System_Void || type is INamedTypeSymbol { IsRefLikeType: true }
                ? "the key type cannot be captured as stable identity" :
            definition.ConstantValue is { HasValue: true, Value: null }
                ? "keys must be non-null" : null;
        if (reason == null && iterationKey && definition.Operation != null &&
            !IsItemOnlyMarkupForeachKey(definition.Operation, loop))
        {
            reason = "iteration keys must be side-effect-free metadata of the current item, not index or ambient state";
        }
        if (reason != null)
        {
            diagnostics.Add(new(syntax, ErrorCodes.AKBURA_SEMANTIC_ForeachKeyInvalid, [reason]));
        }
    }

    private static bool IsItemOnlyMarkupForeachKey(Microsoft.CodeAnalysis.IOperation operation,
        MarkupForeachStatementSyntax loop)
    {
        if (operation.ConstantValue.HasValue)
        {
            return true;
        }
        if (operation is IAssignmentOperation or IIncrementOrDecrementOperation or IInvocationOperation or
            IObjectCreationOperation or IAnonymousFunctionOperation or IAwaitOperation or
            IParameterReferenceOperation or IInstanceReferenceOperation)
        {
            return false;
        }
        if (operation is ILocalReferenceOperation local)
        {
            var name = loop.Header.GetRawCSharpForeach()?.Identifier.ValueText;
            return local.Local.Name == name && local.Local.DeclaringSyntaxReferences.Any(reference =>
                reference.GetSyntax() is CSharp.ForEachStatementSyntax);
        }
        if (operation is IPropertyReferenceOperation { Property.IsStatic: true } ||
            operation is IFieldReferenceOperation { Field.IsStatic: true })
        {
            return false;
        }
        return operation.ChildOperations.All(child => IsItemOnlyMarkupForeachKey(child, loop));
    }
}
