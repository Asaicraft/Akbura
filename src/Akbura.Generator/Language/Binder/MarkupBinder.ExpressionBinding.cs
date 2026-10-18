using Akbura.Language.BoundTree;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Immutable;
using System.Linq;
using PropertySymbol = Akbura.Language.Symbols.IPropertySymbol;

namespace Akbura.Language.Binder;

internal sealed partial class MarkupBinder
{
    private BoundMarkupPropertySetter BindMarkupExpressionBinding(MarkupAttributeSyntax attribute,
        MarkupExpressionBindingSyntax syntax)
    {
        var property = SemanticModel.GetSymbolInfo(attribute).Symbol as PropertySymbol;
        var element = AkburaSemanticModel.GetContainingMarkupElement(attribute);
        var contract = SemanticModel.GetMarkupPropertyAssignmentContract(property, element);
        // AssignBinding describes how the holder accepts a runtime binding,
        // not the C# result type. Contextual Value/Setter contracts still apply.
        var target = contract.ContextualValueType ?? contract.DeclaredType;
        var bindingKind = AkburaSemanticModel.GetMarkupAttributeBindingKind(attribute);
        using var diagnostics = ImmutableArrayBuilder<AkburaSemanticDiagnostic>.Rent();
        diagnostics.AddRange(SemanticModel.GetCachedSemanticDiagnostics(attribute));
        AddAssignmentContractDiagnostics(attribute, property?.Name ?? attribute.ToString(), contract,
            requiresLiteralConversion: true, diagnostics);
        var reason = GetExpressionBindingUnsupportedTarget(attribute, property, bindingKind, target);
        if (contract.ContextualTypeAmbiguous || contract.ContextualTypeUnknown)
            reason ??= "the contextual destination value type cannot be determined";
        if (reason != null)
            diagnostics.Add(new AkburaSemanticDiagnostic(syntax,
                ErrorCodes.AKBURA_SEMANTIC_ExpressionBindingUnsupportedTarget, [reason]));
        if (property != null)
            AkburaSemanticModel.AddDuplicateMarkupPropertySetterDiagnostics(attribute, property, bindingKind, diagnostics);

        var raw = syntax.Expression.GetRawCSharpExpression();
        var binding = raw == null ? CSharpBindingResult.Empty :
            SemanticModel.BindMarkupAttributeExpression(syntax.Expression, raw, target);
        var root = binding.OperationDefinition.Operation;
        // Probe diagnostics use wrapper positions. Project only their relative
        // expression spans; never expose a synthetic C# file to the user.
        foreach (var diagnostic in binding.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error))
        {
            var relativeStart = root == null ? 0 : diagnostic.Location.SourceSpan.Start - root.Syntax.FullSpan.Start;
            var start = Math.Max(0, Math.Min(syntax.Expression.FullWidth, relativeStart));
            var length = Math.Min(diagnostic.Location.SourceSpan.Length, syntax.Expression.FullWidth - start);
            diagnostics.Add(new AkburaSemanticDiagnostic(syntax.Expression,
                ErrorCodes.AKBURA_SEMANTIC_MarkupExpressionError, [raw?.ToFullString(), diagnostic.GetMessage()],
                span: new TextSpan(syntax.Expression.Position + start, length)));
        }
        if (raw == null || raw.ContainsDiagnostics && !binding.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
            diagnostics.Add(new AkburaSemanticDiagnostic(syntax.Expression,
                ErrorCodes.AKBURA_SEMANTIC_MarkupExpressionError, [raw?.ToFullString(), "Invalid C# expression."]));
        if (reason == null && target != null && binding.Conversion.SourceType is { } source &&
            !binding.Conversion.IsImplicit)
            diagnostics.Add(new AkburaSemanticDiagnostic(syntax.Expression,
                ErrorCodes.AKBURA_SEMANTIC_MarkupAttributeValueCannotConvert,
                [property!.Name, source.ToDisplayString(), target.ToDisplayString()]));

        var resultDiagnostics = diagnostics.ToImmutable();
        ExpressionBindingPlan? plan = null;
        if (reason == null)
        {
            plan = root == null
                ? new ExpressionBindingPlan(binding.OperationDefinition, null,
                    target, binding.Conversion, null, ImmutableArray<ExpressionBindingDependency>.Empty, resultDiagnostics, true)
                : new ExpressionBindingDependencyAnalyzer(syntax, root,
                    SemanticModel.CreateCSharpOperationSymbolMapper(attribute)).Analyze(binding, target, resultDiagnostics);
            resultDiagnostics = plan.Diagnostics;
        }
        SemanticModel.SetSemanticDiagnostics(attribute, resultDiagnostics);
        return new BoundMarkupPropertySetter(attribute, this,
            SemanticModel.GetContainingMarkupComponentSymbol(attribute), property,
            ImmutableArray<IAkcssSymbol>.Empty,
            binding.TypeSymbol == null ? default : new CSharpSymbolDefinition(binding.TypeSymbol),
            binding.OperationDefinition, binding.Conversion, bindingKind,
            MarkupAttributeValueKind.ExpressionBinding, syntax, null, null, false,
            resultDiagnostics, reason != null || root == null ||
                resultDiagnostics.Any(d => d.Severity == AkburaDiagnosticSeverity.Error), plan);
    }

    private bool IsExpressionBindingCollectionType(ITypeSymbol? type)
    {
        var enumerable = SemanticModel.Compilation.CSharpCompilation.GetTypeByMetadataName("System.Collections.IEnumerable");
        return type?.SpecialType != SpecialType.System_String && type != null && enumerable != null &&
            SemanticModel.Compilation.CSharpCompilation.ClassifyConversion(type, enumerable).IsImplicit;
    }

    private string? GetExpressionBindingUnsupportedTarget(MarkupAttributeSyntax attribute,
        PropertySymbol? property, MarkupAttributeBindingKind bindingKind, ITypeSymbol? target)
    {
        if (bindingKind != MarkupAttributeBindingKind.None) return "bind: / out: prefixes conflict with x.bind";
        if (property == null) return "events, directives and unresolved properties are not property-value targets";
        if (property.Command != null || target != null && SemanticModel.IsSystemWindowsInputICommand(target))
            return "command handler targets are not supported";
        if (AkburaSemanticModel.IsMarkupDataTypeDirective(attribute) ||
            AkburaSemanticModel.IsMarkupItemNameDirective(attribute) ||
            AkburaSemanticModel.IsMarkupNameDirective(attribute) ||
            AkburaSemanticModel.IsMarkupForeachKeyDirective(attribute) ||
            AkburaSemanticModel.IsMarkupDictionaryKeyDirective(attribute) ||
            AkburaSemanticModel.IsMarkupWhitespaceDirective(attribute)) return "metadata directives are not supported";
        if (!property.CanWrite) return "the property is read-only";
        if (property.ClrPropertyDefinition.Symbol is Microsoft.CodeAnalysis.IPropertySymbol { IsStatic: true } or
            Microsoft.CodeAnalysis.IPropertySymbol { SetMethod.IsInitOnly: true })
            return "static and init-only properties are not reactive instance targets";
        for (var type = target as INamedTypeSymbol; type != null; type = type.BaseType)
            if (type.ToDisplayString() == "Avalonia.Data.BindingBase")
                return "BindingBase is an assignment channel, not a destination value type";
        if (property.Parameter is { BindingKind: not ParamBindingKind.Default }) return "directional component parameters are not supported";
        if (IsExpressionBindingCollectionType(target)) return "collection assignment contracts are not supported";
        return target == null || target.TypeKind == TypeKind.Error ? "the destination value type is unknown" : null;
    }
}
