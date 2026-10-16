using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
#if STATS
using Akbura.Language.CodeGeneration;
#endif

namespace Akbura.Language.Binder;

/// <summary>
/// Raw Roslyn diagnostics for covering spans of one exact semantic model. Filtering
/// and Akbura conversion suppression are applied separately for every target node.
/// No model, symbol or diagnostic is transferred to another compilation or tree.
/// </summary>
internal sealed class CSharpProbeDiagnostics(SemanticModel model)
{
    private readonly object _gate = new();
    private readonly List<(TextSpan Span, ImmutableArray<Diagnostic> Diagnostics)> _regions = new();

    internal ImmutableArray<Diagnostic> GetDiagnostics(TextSpan span)
    {
        lock (_gate)
        {
            foreach (var region in _regions)
            {
                if (!region.Span.Contains(span))
                {
                    continue;
                }

#if STATS
                GenerationStatistics.Increment(GenerationStatisticCounter.ProbeDiagnosticsReused);
#endif
                return region.Span == span ? region.Diagnostics : region.Diagnostics
                    .Where(diagnostic => Intersects(diagnostic.Location, model.SyntaxTree, span) ||
                        diagnostic.AdditionalLocations.Any(location => Intersects(location, model.SyntaxTree, span)))
                    .ToImmutableArray();
            }

#if STATS
            GenerationStatistics.Increment(GenerationStatisticCounter.ProbeDiagnosticQuery);
#endif
            var diagnostics = model.GetDiagnostics(span);
            _regions.Add((span, diagnostics));
            return diagnostics;
        }
    }

    private static bool Intersects(Microsoft.CodeAnalysis.Location location, SyntaxTree tree, TextSpan span) =>
        ReferenceEquals(location.SourceTree, tree) && span.IntersectsWith(location.SourceSpan);
}
