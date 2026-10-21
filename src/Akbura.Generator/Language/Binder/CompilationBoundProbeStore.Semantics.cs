using Akbura.Language.CodeGeneration;
using Akbura.Language.Syntax;
using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Akbura.Language.Binder;

internal sealed partial class CompilationBoundProbeStore
{
    internal sealed partial class DocumentSession
    {
        internal bool CanCaptureBinding => ReuseBindings && Entries.Count < MaxEntriesPerDocument &&
            Entries.Values.Count(static entry => entry.Binding != null) < MaxSemanticEntriesPerDocument;

        internal bool TryGetBinding(AkburaSyntax scope, CSharp.ExpressionSyntax expression, ITypeSymbol? target,
            CSharp.CompilationUnitSyntax projection, string text, out ReusableProbeBinding result)
        {
            result = null!;
            CancellationToken.ThrowIfCancellationRequested();
#if STATS
            GenerationStatistics.Increment(GenerationStatisticCounter.CrossRevisionBindingCandidate);
            using var lookup = GenerationStatistics.Measure(GenerationStatisticStage.CrossRevisionBindingLookup);
#endif
            if (!ReuseBindings || projection.ContainsDiagnostics || projection.ContainsSkippedText || projection.ContainsDirectives ||
                !TryKey(scope, expression, target, out var key) || _previous == null || !_previous.Entries.TryGetValue(key, out var entry) ||
                entry.Binding is not { } binding)
            {
#if STATS
                GenerationStatistics.Increment(GenerationStatisticCounter.CrossRevisionBindingMiss);
#endif
                return false;
            }
#if STATS
            using var validation = GenerationStatistics.Measure(GenerationStatisticStage.CrossRevisionBindingValidation);
#endif
            var query = CSharpProbeBuilder.GetReturnProbeExpression(projection);
            if (query == null || query.Span != binding.QuerySpan || !binding.Dependencies.IsComplete ||
                binding.Dependencies.Schema != DetachedCSharpDependencySet.CurrentSchema ||
                !ReferenceEquals(entry.Projection, projection) && (binding.ProjectionText != text ||
                    !Microsoft.CodeAnalysis.CSharp.SyntaxFactory.AreEquivalent(entry.Projection, projection)))
            {
#if STATS
                GenerationStatistics.Increment(GenerationStatisticCounter.CrossRevisionBindingContextRejected);
                GenerationStatistics.Increment(GenerationStatisticCounter.CrossRevisionBindingMiss);
#endif
                return false;
            }
            CancellationToken.ThrowIfCancellationRequested();
            Entries[key] = entry;
            result = binding;
            return true;
        }

        internal void RecordBinding(AkburaSyntax scope, CSharp.ExpressionSyntax expression, ITypeSymbol? target,
            CSharp.CompilationUnitSyntax projection, string text, CSharpBindingResult result, DetachedCSharpDependencySet? dependencies)
        {
            CancellationToken.ThrowIfCancellationRequested();
            if (!ReuseBindings || dependencies is not { IsComplete: true, Schema: DetachedCSharpDependencySet.CurrentSchema } ||
                !ReusableProbeBinding.CanRetain(result) || text.Length > ReusableProbeBinding.MaxProjectionCharacters ||
                projection.ContainsDiagnostics || projection.ContainsSkippedText || projection.ContainsDirectives ||
                Entries.Values.Count(static entry => entry.Binding != null) >= MaxSemanticEntriesPerDocument ||
                Entries.Count >= MaxEntriesPerDocument || !TryKey(scope, expression, target, out var key) ||
                CSharpProbeBuilder.GetReturnProbeExpression(projection) is not { } query)
            {
#if STATS
                GenerationStatistics.Increment(GenerationStatisticCounter.CompilationBoundSemanticEntriesRejected);
#endif
                return;
            }
            Entries[key] = new Entry(projection, text.Length, new WeakReference<Akbura.Language.Syntax.Green.GreenNode>(scope.Green),
                new ReusableProbeBinding(result, dependencies, query.Span, text));
        }
    }
}
