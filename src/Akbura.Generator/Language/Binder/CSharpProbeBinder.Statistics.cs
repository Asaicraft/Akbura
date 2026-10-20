#if STATS
using Akbura.Language.CodeGeneration;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Akbura.Language.Binder;

internal sealed partial class CSharpProbeBinder
{
    private void RecordExpressionProbe(AkburaSyntax syntax, CSharp.ExpressionSyntax expression, ITypeSymbol? targetType,
        CSharp.CompilationUnitSyntax projection, string category = "Scoped")
    {
        if (expression is CSharp.LiteralExpressionSyntax)
        {
            GenerationStatistics.Increment(GenerationStatisticCounter.ProbeLiteralRequest);
        }
        if (!GenerationStatistics.IsInvalidationTrackingEnabled)
        {
            return;
        }
        using var hash = SHA256.Create();
        var digest = System.BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(projection.ToFullString()))).Replace("-", "");
        GenerationStatistics.RecordInvalidation(new GenerationInvalidationEvent("ExpressionProbe", SemanticModel.SyntaxTree.FilePath,
            "Computed", [], ImmutableDictionary<string, string>.Empty
                .Add("Start", syntax.Span.Start.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Add("Length", syntax.Span.Length.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Add("Expression", expression.ToFullString())
                .Add("Target", targetType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? "")
                .Add("Category", category)
                .Add("ProjectionSha256", digest)));
    }

    private enum ProbeCategory
    {
        Type,
        MarkupExpression,
        BindingPath,
        Statement,
        ComponentMethod,
        Lambda,
        Hook,
    }

    private GenerationOperationMeasurement MeasureProbe(ProbeCategory category, AkburaSyntax? syntax = null)
    {
        GenerationStatistics.Increment(GenerationStatisticCounter.ProbeRequest);
        return GenerationStatistics.OperationTrackingEnabled
            ? GenerationStatistics.MeasureOperation(
                GenerationStatisticOperation.CSharpProbe, SemanticModel, SemanticModel.SyntaxTree,
                SemanticModel.SyntaxTree.FilePath, syntax?.Span.Start ?? 0, syntax?.Span.Length ?? 0,
                category.ToString(), (int)category)
            : GenerationOperationMeasurement.Empty;
    }
}
#endif
