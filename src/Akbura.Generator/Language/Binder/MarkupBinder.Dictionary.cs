using Akbura.Language.BoundTree;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Immutable;

namespace Akbura.Language.Binder;

internal sealed partial class MarkupBinder
{
    private BoundMarkupDictionaryKey BindMarkupDictionaryKey(MarkupAttachedPropertyAttributeSyntax attribute)
    {
        var element = AkburaSemanticModel.GetContainingMarkupElement(attribute)!;
        var component = SemanticModel.GetContainingMarkupComponentSymbol(attribute);
        SemanticModel.TryGetMarkupDictionaryContext(element, out var contentModel);
        var shape = contentModel.DictionaryShape;
        using var diagnostics = ImmutableArrayBuilder<AkburaSemanticDiagnostic>.Rent();
        if (!shape.IsDictionary)
        {
            diagnostics.Add(new AkburaSemanticDiagnostic(attribute,
                ErrorCodes.AKBURA_SEMANTIC_MarkupDictionaryKeyOutsideDictionary, []));
        }

        var seen = false;
        foreach (var candidate in element.StartTag!.Attributes)
        {
            if (!AkburaSemanticModel.IsMarkupDictionaryKeyDirective(candidate))
            {
                continue;
            }

            if (ReferenceEquals(candidate, attribute))
            {
                if (seen)
                {
                    diagnostics.Add(new AkburaSemanticDiagnostic(attribute,
                        ErrorCodes.AKBURA_SEMANTIC_MarkupDictionaryKeyDuplicateDirective, []));
                }

                break;
            }

            seen = true;
        }

        var binding = CSharpBindingResult.Empty;
        string? literal = null;
        Microsoft.CodeAnalysis.CSharp.Syntax.ExpressionSyntax? expression = null;
        switch (AkburaSemanticModel.GetMarkupAttributeValue(attribute))
        {
            case MarkupLiteralAttributeValueSyntax value:
                literal = AkburaSemanticModel.GetMarkupLiteralAttributeValueText(value);
                expression = Microsoft.CodeAnalysis.CSharp.SyntaxFactory.LiteralExpression(
                    Microsoft.CodeAnalysis.CSharp.SyntaxKind.StringLiteralExpression,
                    Microsoft.CodeAnalysis.CSharp.SyntaxFactory.Literal(literal));
                break;
            case MarkupDynamicAttributeValueSyntax value:
                expression = AkburaSemanticModel.ParseInlineExpression(value.Expression);
                break;
        }

        if (expression != null)
        {
            binding = SemanticModel.BindMarkupAttributeExpression(attribute, expression, shape.KeyType);
            SemanticModel.AddMarkupExpressionDiagnostics(attribute, expression.ToFullString(), binding, diagnostics);
            var sourceType = literal != null
                ? SemanticModel.Compilation.CSharpCompilation.GetSpecialType(SpecialType.System_String)
                : binding.Conversion.SourceType ?? binding.OperationDefinition.Type ?? binding.TypeSymbol;
            if (sourceType?.TypeKind != TypeKind.Error && shape.KeyType != null &&
                !binding.Conversion.IsImplicit)
            {
                diagnostics.Add(new AkburaSemanticDiagnostic(attribute,
                    ErrorCodes.AKBURA_SEMANTIC_MarkupDictionaryKeyTypeMismatch,
                    [sourceType?.ToDisplayString() ?? expression.ToFullString(), shape.KeyType.ToDisplayString()]));
            }
        }
        else
        {
            diagnostics.Add(new AkburaSemanticDiagnostic(attribute,
                ErrorCodes.AKBURA_SEMANTIC_MarkupDictionaryKeyValueInvalid, []));
        }

        var result = diagnostics.ToImmutable();
        SemanticModel.SetSemanticDiagnostics(attribute, result);
        return new BoundMarkupDictionaryKey(attribute, this, component, shape, binding, literal, result);
    }
}
