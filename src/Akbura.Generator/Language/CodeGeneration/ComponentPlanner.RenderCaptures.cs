using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Akbura.Language.CodeGeneration;

internal static partial class ComponentPlanner
{
    private ref partial struct Planner
    {
        private Dictionary<string, HashSet<int>>? CreateRenderCaptureReferences()
        {
            if (_localRuntimeStorageCounts.Count == 0)
            {
                return null;
            }

            var references = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
            foreach (var syntax in _component.DeclarationSyntax.DescendantNodes())
            {
                var scopeId = GetRenderCaptureScopeId(syntax);
                if (scopeId < 0)
                {
                    continue;
                }

                var symbols = syntax switch
                {
                    MarkupAttributeSyntax attribute => _semanticModel.GetCSharpSymbolReferences(attribute),
                    InlineExpressionSyntax expression => _semanticModel.GetCSharpSymbolReferences(expression),
                    CSharpExpressionSyntax expression => _semanticModel.GetCSharpSymbolReferences(expression),
                    _ => ImmutableArray<CSharpSymbolReference>.Empty,
                };
                foreach (var symbol in symbols)
                {
                    if (symbol.CSharpDefinition.Symbol is not ILocalSymbol local)
                    {
                        continue;
                    }

                    if (!references.TryGetValue(local.Name, out var scopes))
                    {
                        scopes = [];
                        references.Add(local.Name, scopes);
                    }

                    scopes.Add(scopeId);
                }
            }

            return references;
        }

        private int GetRenderCaptureScopeId(AkburaSyntax syntax)
        {
            for (var ancestor = syntax.Parent; ancestor != null; ancestor = ancestor.Parent)
            {
                if (ancestor is MarkupIfStatementSyntax conditional &&
                    _conditionalSyntaxIds.TryGetValue(conditional, out var regionId))
                {
                    var rootScopeId = _conditionalRegions[regionId].RuntimeStorageRootScopeId;
                    if (rootScopeId > 0 && _localRuntimeStorageCounts.ContainsKey(rootScopeId))
                    {
                        return rootScopeId;
                    }
                }

                if (ancestor is not MarkupElementSyntax element ||
                    !_syntaxElementIds.TryGetValue(element, out var elementId))
                {
                    continue;
                }

                for (var scopeId = _elements[elementId].ScopeId; scopeId > 0;
                    scopeId = _pendingScopes[scopeId].ParentScopeId)
                {
                    if (_localRuntimeStorageCounts.ContainsKey(scopeId))
                    {
                        return scopeId;
                    }
                }

                return -1;
            }

            return -1;
        }

        private ImmutableArray<ComponentRenderCapturePlan> CreateRenderCaptures(
            ImmutableArray<CSharpLocalSymbol> declaredLocals, Dictionary<string, HashSet<int>>? references)
        {
            if (references == null || references.Count == 0)
            {
                return [];
            }

            using var captures = ImmutableArrayBuilder<ComponentRenderCapturePlan>.Rent();
            foreach (var local in declaredLocals)
            {
                if (!references.TryGetValue(local.Name, out var scopes))
                {
                    continue;
                }

                var scopeIds = new int[scopes.Count];
                scopes.CopyTo(scopeIds);
                Array.Sort(scopeIds);
                captures.Add(new ComponentRenderCapturePlan(local.Name, local.Local.Type,
                    ImmutableArray.CreateRange(scopeIds)));
            }

            return captures.ToImmutable();
        }
    }
}
