using Akbura.Language.Operations;
using Akbura.Language.Syntax;
using System.Collections.Immutable;
using System.Linq;
using Akbura.Language.Symbols;

namespace Akbura.Language.CodeGeneration;

internal static partial class ComponentPlanner
{
    private ref partial struct Planner
    {
        private void BuildForeach(int ownerId, MarkupForeachStatementSyntax syntax, in TraversalContext inherited)
        {
            if (_foreachSyntaxIds.ContainsKey(syntax) ||
                _semanticModel.GetOperation(syntax) is not IMarkupForeachOperation operation || operation.HasErrors)
            {
                return;
            }

            var id = _foreachRegions.Count;
            _foreachSyntaxIds.Add(syntax, id);
            _foreachRegions.Add(default);
            var roots = ImmutableArray.CreateBuilder<ComponentForeachRootPlan>();
            BuildForeachRoots(ownerId, syntax.Body, inherited, roots);
            _foreachRegions[id] = new ComponentForeachPlan(id, ownerId, operation, roots.ToImmutable());
        }

        private void BuildForeachRoots(int ownerId, AkburaSyntax syntax, in TraversalContext inherited,
            ImmutableArray<ComponentForeachRootPlan>.Builder roots)
        {
            foreach (var childNode in syntax.ChildNodesAndTokens())
            {
                if (childNode.AsNode() is not { } child) continue;
                if (child is MarkupElementSyntax element)
                {
                    var parent = inherited.GetEffectiveScope();
                    var scopeId = AddPendingScope(parent.ScopeId, ownerId, ComponentElementScopeKind.ForeachIteration);
                    var context = new TraversalContext(
                        new ScopeReference(scopeId, ownerId, ComponentElementScopeKind.ForeachIteration, isDeferred: false), default);
                    if (TryBuildElement(element, ownerId, context, isRoot: false, out var elementId))
                    {
                        var key = (_semanticModel.GetSymbolInfo(element).Symbol as IMarkupComponentSymbol)?
                            .AttributeOperations.OfType<IMarkupForeachKeyOperation>().FirstOrDefault();
                        roots.Add(new ComponentForeachRootPlan(element, scopeId, elementId, key));
                    }
                }
                else if (child is MarkupForeachStatementSyntax nested)
                {
                    BuildForeach(ownerId, nested, inherited);
                }
                else
                {
                    BuildForeachRoots(ownerId, child, inherited, roots);
                }
            }
        }
    }
}
