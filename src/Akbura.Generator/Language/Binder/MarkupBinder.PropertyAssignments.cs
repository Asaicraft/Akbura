using Akbura.Language.BoundTree;
using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Immutable;
using AkburaPropertySymbol = Akbura.Language.Symbols.IPropertySymbol;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;
using CSharpSyntaxFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using CSharpSyntaxKind = Microsoft.CodeAnalysis.CSharp.SyntaxKind;

namespace Akbura.Language.Binder;

internal sealed partial class MarkupBinder
{
    private void AddAssignmentContractDiagnostics(AkburaSyntax syntax, string name,
        in MarkupPropertyAssignmentContract contract, bool requiresLiteralConversion,
        ImmutableArrayBuilder<AkburaSemanticDiagnostic> diagnostics)
    {
        if (!contract.Metadata.InvalidDependencies.IsDefaultOrEmpty)
        {
            foreach (var dependency in contract.Metadata.InvalidDependencies)
            {
                diagnostics.Add(new AkburaSemanticDiagnostic(syntax,
                    ErrorCodes.AKBURA_SEMANTIC_MarkupPropertyDependencyInvalid, [name, dependency]));
            }
        }

        if (requiresLiteralConversion && contract.ContextualTypeAmbiguous)
        {
            diagnostics.Add(new AkburaSemanticDiagnostic(syntax,
                ErrorCodes.AKBURA_SEMANTIC_MarkupPropertyContextualTypeAmbiguous, [name]));
        }
        else if (requiresLiteralConversion && contract.ContextualTypeUnknown)
        {
            diagnostics.Add(new AkburaSemanticDiagnostic(syntax,
                ErrorCodes.AKBURA_SEMANTIC_MarkupPropertyContextualTypeUnknown, [name]));
        }
    }

    private void AddPropertyReferenceDiagnostic(AkburaSyntax syntax, string text,
        MarkupElementSyntax element, ImmutableArrayBuilder<AkburaSemanticDiagnostic> diagnostics)
    {
        var context = SemanticModel.GetMarkupStyleTargetContext(element);
        var code = text.IndexOf('.') >= 0
            ? ErrorCodes.AKBURA_SEMANTIC_MarkupPropertyReferenceNotFound
            : context.IsUnknown ? ErrorCodes.AKBURA_SEMANTIC_MarkupPropertyReferenceTargetUnknown
            : context.IsAmbiguous ? ErrorCodes.AKBURA_SEMANTIC_MarkupPropertyReferenceTargetAmbiguous
            : ErrorCodes.AKBURA_SEMANTIC_MarkupPropertyReferenceNotFound;
        diagnostics.Add(new AkburaSemanticDiagnostic(syntax, code, [text]));
    }

    private void AddAssignmentValueDiagnostics(AkburaSyntax syntax, string name,
        in MarkupPropertyAssignmentContract contract, ITypeSymbol? sourceType,
        ImmutableArrayBuilder<AkburaSemanticDiagnostic> diagnostics)
    {
        if (sourceType == null || sourceType.SpecialType == SpecialType.System_Object ||
            contract.ContextualValueType is not { } targetType ||
            SymbolEqualityComparer.Default.Equals(targetType, contract.DeclaredType))
        {
            return;
        }

        var compilation = SemanticModel.Compilation.CSharpCompilation;
        if (contract.AssignBinding && compilation.GetTypeByMetadataName("Avalonia.Data.BindingBase") is { } bindingBase &&
            compilation.ClassifyConversion(sourceType, bindingBase).IsImplicit)
        {
            return;
        }

        if (!compilation.ClassifyConversion(sourceType, targetType).IsImplicit)
        {
            diagnostics.Add(new AkburaSemanticDiagnostic(syntax,
                ErrorCodes.AKBURA_SEMANTIC_MarkupAttributeValueCannotConvert,
                [name, sourceType.ToDisplayString(), targetType.ToDisplayString()]));
        }
    }

    private bool TryBindAssignmentContent(MarkupElementSyntax syntax,
        MarkupElementSyntax owner, IMarkupComponentSymbol? component,
        AkburaPropertySymbol property, MarkupContentModel contentModel,
        MarkupWhitespaceMode whitespace, ImmutableArray<MarkupChildContent> content,
        out BoundMarkupContentSetter setter)
    {
        setter = null!;
        if (contentModel.IsCollection || contentModel.IsDictionary ||
            AkburaSemanticModel.HasElementContent(syntax) ||
            !AkburaSemanticModel.TryCreateMarkupContentValueExpression(syntax, whitespace,
                out var expression, out var literalValue, out var synthesized,
                out var hasText, out var diagnosticSyntax))
        {
            return false;
        }

        var contract = SemanticModel.GetMarkupPropertyAssignmentContract(property, owner);
        var hasPropertyReferenceDependency = false;
        if (!contract.Dependencies.IsDefaultOrEmpty)
        {
            foreach (var dependency in contract.Dependencies)
            {
                if (dependency is Microsoft.CodeAnalysis.IPropertySymbol dependencyProperty &&
                    SemanticModel.IsAvaloniaPropertyType(dependencyProperty.Type))
                {
                    hasPropertyReferenceDependency = true;
                    break;
                }
            }
        }

        var contextualAssignment = hasPropertyReferenceDependency ||
            contract.DeclaredType != null && SemanticModel.IsAvaloniaPropertyType(contract.DeclaredType);
        // Ordinary implicit content retains its established binding path. Scalar
        // property elements use that same expression/probe path rather than a placeholder value.
        if (!contextualAssignment && contract.Metadata.InvalidDependencies.IsDefaultOrEmpty && syntax == owner)
        {
            return false;
        }

        using var diagnostics = ImmutableArrayBuilder<AkburaSemanticDiagnostic>.Rent();
        AddAssignmentContractDiagnostics(syntax, property.Name, contract, literalValue != null, diagnostics);
        if (literalValue != null && contract.ContextualValueType is { } target)
        {
            object? converted;
            if (SemanticModel.IsAvaloniaPropertyType(target))
            {
                var reference = SemanticModel.ResolveMarkupAvaloniaPropertyReference(literalValue.Trim(), owner);
                converted = reference == null ? null : new CSharpSymbolDefinition(reference.Field);
                if (reference == null)
                {
                    AddPropertyReferenceDiagnostic(diagnosticSyntax, literalValue, owner, diagnostics);
                }
            }
            else
            {
                var status = MarkupLiteralValueConverter.Convert(literalValue, target,
                    SemanticModel.Compilation.CSharpCompilation, out converted);
                if (status is MarkupLiteralConversionStatus.Invalid or MarkupLiteralConversionStatus.Unsupported &&
                    target.SpecialType != SpecialType.System_Object)
                {
                    diagnostics.Add(new AkburaSemanticDiagnostic(diagnosticSyntax,
                        ErrorCodes.AKBURA_SEMANTIC_MarkupAttributeValueCannotConvert,
                        [property.Name, "string", target.ToDisplayString()]));
                }
            }

            if (converted != null && converted is not string)
            {
                expression = CreateAssignmentLiteralExpression(converted, target);
                literalValue = null;
                synthesized = false;
            }
        }

        var targetType = contextualAssignment
            ? contract.AssignBinding ? contract.DeclaredType : contract.ContextualValueType
            : GetExpectedValueType(property);
        var binding = SemanticModel.BindMarkupAttributeExpression(diagnosticSyntax, expression, targetType);
        SemanticModel.AddMarkupExpressionDiagnostics(diagnosticSyntax, expression.ToFullString(), binding, diagnostics);
        AddAssignmentValueDiagnostics(diagnosticSyntax, property.Name, contract,
            binding.Conversion.SourceType ?? binding.TypeSymbol, diagnostics);
        var resultDiagnostics = diagnostics.ToImmutable();
        if (content.IsDefault)
        {
            content = SemanticModel.CreateMarkupChildren(syntax, contentModel, out _);
        }
        SemanticModel.SetSemanticDiagnostics(syntax, resultDiagnostics);
        setter = new BoundMarkupContentSetter(syntax, this, component, property, contentModel, content,
            binding.TypeSymbol == null ? default : new CSharpSymbolDefinition(binding.TypeSymbol),
            binding.OperationDefinition, binding.Conversion, whitespace, literalValue, synthesized,
            resultDiagnostics, resultDiagnostics.Length != 0);
        return true;
    }

    private static CSharp.ExpressionSyntax CreateAssignmentLiteralExpression(object value, ITypeSymbol targetType)
    {
        if (value is MarkupLiteralValue literal)
        {
            var text = CSharpSyntaxFactory.LiteralExpression(CSharpSyntaxKind.StringLiteralExpression,
                CSharpSyntaxFactory.Literal(literal.Text)).ToString();
            var type = literal.TargetType.Symbol?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var expression = literal.ConverterKind switch
            {
                MarkupLiteralConverterKind.ParseMethod when literal.Converter.Symbol is IMethodSymbol method =>
                    method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "." + method.Name + "(" + text + ")",
                MarkupLiteralConverterKind.StringConstructor => "new " + type + "(" + text + ")",
                MarkupLiteralConverterKind.TypeConverter => "(" + type + ")new " +
                    literal.Converter.Symbol?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
                    "().ConvertFromInvariantString(" + text + ")!",
                _ => throw new InvalidOperationException("A bound literal must have a supported converter."),
            };
            return CSharpSyntaxFactory.ParseExpression(expression);
        }

        if (value is CSharpSymbolDefinition { Symbol: { ContainingType: { } containingType } member })
        {
            var name = Microsoft.CodeAnalysis.CSharp.SyntaxFacts.GetKeywordKind(member.Name) != CSharpSyntaxKind.None
                ? "@" + member.Name : member.Name;
            return CSharpSyntaxFactory.ParseExpression(containingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
                "." + name);
        }

        if (value is string textValue)
        {
            return CSharpSyntaxFactory.LiteralExpression(CSharpSyntaxKind.StringLiteralExpression, CSharpSyntaxFactory.Literal(textValue));
        }

        if (value is bool boolean)
        {
            return CSharpSyntaxFactory.LiteralExpression(boolean ? CSharpSyntaxKind.TrueLiteralExpression : CSharpSyntaxKind.FalseLiteralExpression);
        }

        if (value is char character)
        {
            return CSharpSyntaxFactory.LiteralExpression(CSharpSyntaxKind.CharacterLiteralExpression, CSharpSyntaxFactory.Literal(character));
        }

        if (value is float single && (float.IsNaN(single) || float.IsInfinity(single)))
        {
            var field = float.IsNaN(single) ? "NaN" : single > 0 ? "PositiveInfinity" : "NegativeInfinity";
            return CSharpSyntaxFactory.ParseExpression("global::System.Single." + field);
        }

        if (value is double numberValue && (double.IsNaN(numberValue) || double.IsInfinity(numberValue)))
        {
            var field = double.IsNaN(numberValue) ? "NaN" : numberValue > 0 ? "PositiveInfinity" : "NegativeInfinity";
            return CSharpSyntaxFactory.ParseExpression("global::System.Double." + field);
        }

        var token = value switch
        {
            byte number => CSharpSyntaxFactory.Literal((int)number),
            sbyte number => CSharpSyntaxFactory.Literal((int)number),
            short number => CSharpSyntaxFactory.Literal((int)number),
            ushort number => CSharpSyntaxFactory.Literal((int)number),
            int number => CSharpSyntaxFactory.Literal(number),
            uint number => CSharpSyntaxFactory.Literal(number),
            long number => CSharpSyntaxFactory.Literal(number),
            ulong number => CSharpSyntaxFactory.Literal(number),
            float number => CSharpSyntaxFactory.Literal(number),
            double number => CSharpSyntaxFactory.Literal(number),
            decimal number => CSharpSyntaxFactory.Literal(number),
            _ => throw new InvalidOperationException("A bound literal must have a supported constant."),
        };
        var numeric = CSharpSyntaxFactory.LiteralExpression(CSharpSyntaxKind.NumericLiteralExpression, token);
        return targetType.SpecialType is SpecialType.System_Byte or SpecialType.System_SByte or
            SpecialType.System_Int16 or SpecialType.System_UInt16
            ? CSharpSyntaxFactory.CastExpression(CSharpSyntaxFactory.ParseTypeName(targetType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)), numeric)
            : numeric;
    }
}
