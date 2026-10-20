using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

namespace Akbura.Language.Binder;

internal sealed partial class MarkupBinder
{
    private ExpressionBindingPlan BindTailwindExpressionCondition(MarkupExpressionBindingSyntax syntax)
    {
        var target = SemanticModel.Compilation.CSharpCompilation.GetSpecialType(SpecialType.System_Boolean);
        var raw = syntax.Expression.GetRawCSharpExpression();
        var binding = raw == null ? CSharpBindingResult.Empty :
            SemanticModel.BindMarkupAttributeExpression(syntax.Expression, raw, target);
        using var diagnostics = ImmutableArrayBuilder<AkburaSemanticDiagnostic>.Rent();
        AddExpressionBindingExpressionDiagnostics(syntax, raw, binding, diagnostics);
        if (binding.Conversion.SourceType is { } source && !binding.Conversion.IsImplicit)
        {
            diagnostics.Add(new AkburaSemanticDiagnostic(syntax.Expression,
                ErrorCodes.AKBURA_SEMANTIC_MarkupAttributeValueCannotConvert,
                ["utility condition", source.ToDisplayString(), target.ToDisplayString()]));
        }

        // This is the same operation-based analyzer used for property bindings:
        // conditional subscriptions, unsupported forms and warnings stay shared.
        var root = binding.OperationDefinition.Operation;
        return root == null
            ? new ExpressionBindingPlan(binding.OperationDefinition, null, target,
                binding.Conversion, null, ImmutableArray<ExpressionBindingDependency>.Empty,
                diagnostics.ToImmutable(), hasUnknownDependencies: true)
            : new ExpressionBindingDependencyAnalyzer(syntax, root,
                SemanticModel.CreateCSharpOperationSymbolMapper(syntax)).Analyze(binding, target, diagnostics.ToImmutable());
    }
}
