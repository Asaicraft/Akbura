#if STATS
using Akbura.Language.CodeGeneration;
using Akbura.Language.Syntax;

namespace Akbura.Language.Binder;

internal sealed partial class CSharpProbeBinder
{
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
