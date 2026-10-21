using Akbura.Language.Binder;
using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Akbura.Language.CodeGeneration;

internal static partial class AkcssActivatorPlanner
{
    private ref partial struct Planner
    {
        private ImmutableDictionary<string, string> GetConditionStateAccessors()
        {
            if (_conditionStateAccessors != null) return _conditionStateAccessors;
            var names = ImmutableDictionary.CreateBuilder<string, string>();
            if (_semanticModel.GetSymbolInfo(_semanticModel.SyntaxTree.GetRootSyntax()).Symbol is IAkburaComponentSymbol component)
            {
                var members = ComponentMemberPlanner.Create(component, _semanticModel);
                try
                {
                    foreach (var state in members.States)
                        names.Add(state.Name, "__State_" + state.GeneratedName);
                }
                finally { members.ReturnToPool(); }
            }
            return _conditionStateAccessors = names.ToImmutable();
        }

        private ImmutableDictionary<TextSpan, string> GetConditionNamedReceivers(ITailwindUtilityAttributeOperation operation)
        {
            var names = ImmutableDictionary.CreateBuilder<TextSpan, string>();
            if (_expressionElementReceivers == null || operation.ConditionExpressionBinding?.Evaluation == null)
                return names.ToImmutable();

            var mapper = _semanticModel.CreateCSharpOperationSymbolMapper(operation.Syntax);
            var work = new Stack<ExpressionBindingEvaluation>();
            work.Push(operation.ConditionExpressionBinding.Evaluation);
            while (work.Count != 0)
            {
                var read = work.Pop();
                Microsoft.CodeAnalysis.ISymbol? symbol = read.Operation switch
                {
                    ILocalReferenceOperation local => local.Local,
                    IParameterReferenceOperation parameter => parameter.Parameter,
                    _ => null,
                };
                // Resolve the projected declaration, not an identifier with the
                // same spelling. This preserves local shadowing and template scopes.
                if (symbol != null && symbol.DeclaringSyntaxReferences.Any(reference =>
                    reference.GetSyntax().GetAnnotations(CSharpProbeBinder.ProjectedSymbolAnnotationKind)
                        .Any(annotation => CSharpProbeSymbolOrigin.TryParse(annotation.Data, out var origin) &&
                            origin.Kind == SymbolKind.MarkupName)) &&
                    mapper(symbol) is IMarkupNameSymbol name &&
                    _expressionElementReceivers.TryGetValue(name.ElementSyntax, out var receiver))
                {
                    names[read.Operation.Syntax.Span] = receiver;
                }
                foreach (var child in read.Children) work.Push(child);
            }
            return names.ToImmutable();
        }
    }
}
