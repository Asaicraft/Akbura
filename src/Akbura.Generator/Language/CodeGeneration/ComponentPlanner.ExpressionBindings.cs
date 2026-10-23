using Akbura.Pools;
using Akbura.Language.Binder;
using Akbura.Language.Symbols;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Akbura.Language.CodeGeneration;

internal static partial class ComponentPlanner
{
    private ref partial struct Planner
    {
        private ExpressionBindingInitializationKind ClassifyExpressionBinding(in PendingPropertyWritePlan pending,
            out ImmutableDictionary<TextSpan, string> namedReceivers)
        {
            var names = ImmutableDictionary.CreateBuilder<TextSpan, string>();
            var stableNames = PooledHashSet<TextSpan>.GetInstance();
            try
            {
                var mapper = _semanticModel.CreateCSharpOperationSymbolMapper(pending.Operation.Syntax);
                var target = _elements[pending.ElementId];
                var plan = pending.Operation.ExpressionBindingPlan!;
                if (plan.Evaluation != null)
                {
                    var work = new Stack<ExpressionBindingEvaluation>();
                    work.Push(plan.Evaluation);
                    while (work.Count != 0)
                    {
                        var read = work.Pop();
                        Microsoft.CodeAnalysis.ISymbol? symbol = read.Operation switch
                        {
                            ILocalReferenceOperation local => local.Local,
                            IParameterReferenceOperation parameter => parameter.Parameter,
                            _ => null,
                        };
                        // Never map a same-named render local as an x.Name receiver.
                        if (symbol != null && symbol.DeclaringSyntaxReferences.Any(reference =>
                            reference.GetSyntax().GetAnnotations(CSharpProbeBinder.ProjectedSymbolAnnotationKind)
                                .Any(annotation => CSharpProbeSymbolOrigin.TryParse(annotation.Data, out var origin) &&
                                    origin.Kind == Akbura.Language.Symbols.SymbolKind.MarkupName)) &&
                            mapper(symbol) is IMarkupNameSymbol name &&
                            _syntaxElementIds.TryGetValue(name.ElementSyntax, out var elementId))
                        {
                            var source = _elements[elementId];
                            names[read.Operation.Syntax.Span] = source.Identifier;
                            // Cross-scope names may be replaced independently. Only
                            // self, or ordinary component-owned peers, are proven.
                            if (elementId == pending.ElementId || source.ScopeId == 0 && target.ScopeId == 0 &&
                                (source.Flags & ComponentElementFlags.IsLocal) == 0)
                                stableNames.Add(read.Operation.Syntax.Span);
                        }
                        foreach (var child in read.Children) work.Push(child);
                    }
                }
                namedReceivers = names.ToImmutable();
                if ((target.Flags & ComponentElementFlags.IsStyleSubtree) != 0 ||
                    target.ScopeId == 0 && (target.Flags & ComponentElementFlags.IsLocal) != 0)
                    return ExpressionBindingInitializationKind.RequiresRenderRefresh;
                return ExpressionBindingInitializationClassifier.Classify(plan, stableNames);
            }
            finally
            {
                stableNames.Free();
            }
        }
    }
}
