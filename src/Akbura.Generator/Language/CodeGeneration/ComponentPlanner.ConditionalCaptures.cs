using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Akbura.Language.CodeGeneration;

internal static partial class ComponentPlanner
{
    private ref partial struct Planner
    {
        private void LowerConditionalCaptures()
        {
            if (_localRuntimeStorageCounts.Count == 0)
            {
                return;
            }

            var declarations = new Dictionary<CSharpExpressionSyntax, ImmutableArray<CSharpLocalSymbol>>();
            var captures = new Dictionary<(int Region, int Branch, string Key), PendingConditionalCapture>();
            foreach (var syntax in _component.DeclarationSyntax.DescendantNodes())
            {
                var scopeId = GetRenderCaptureScopeId(syntax);
                if (scopeId < 0)
                {
                    continue;
                }

                var references = syntax switch
                {
                    MarkupAttributeSyntax attribute => _semanticModel.GetCSharpSymbolReferences(attribute),
                    InlineExpressionSyntax expression => _semanticModel.GetCSharpSymbolReferences(expression),
                    CSharpExpressionSyntax expression => _semanticModel.GetCSharpSymbolReferences(expression),
                    _ => ImmutableArray<CSharpSymbolReference>.Empty,
                };
                foreach (var reference in references)
                {
                    if (reference.CSharpDefinition.Symbol is not ILocalSymbol referencedLocal ||
                        !TryGetConditionalCapture(syntax, referencedLocal.Name, scopeId, declarations,
                            out var regionId, out var branchId, out var local, out var key))
                    {
                        continue;
                    }

                    var entry = (regionId, branchId, key);
                    if (!captures.TryGetValue(entry, out var capture))
                    {
                        capture = new PendingConditionalCapture(local.Name, local.Local.Type, key);
                        captures.Add(entry, capture);
                    }

                    capture.ScopeIds.Add(scopeId);
                    capture.RecordEntryFlow(scopeId, reference);
                }
            }

            for (var regionId = 0; regionId < _conditionalRegions.Count; regionId++)
            {
                var region = _conditionalRegions[regionId];
                using var branches = ImmutableArrayBuilder<ComponentConditionalBranchPlan>.Rent();
                for (var branchId = 0; branchId < region.Branches.Length; branchId++)
                {
                    using var branchCaptures = ImmutableArrayBuilder<ComponentConditionalCapturePlan>.Rent();
                    foreach (var entry in captures)
                    {
                        if (entry.Key.Region != regionId || entry.Key.Branch != branchId)
                        {
                            continue;
                        }

                        var capture = entry.Value;
                        var scopeIds = new int[capture.ScopeIds.Count];
                        capture.ScopeIds.CopyTo(scopeIds);
                        Array.Sort(scopeIds);
                        using var nonNullScopeIds = ImmutableArrayBuilder<int>.Rent();
                        foreach (var scopeId in scopeIds)
                        {
                            if (capture.EntryFlows.TryGetValue(scopeId, out var entryFlow) && entryFlow.IsNonNull)
                            {
                                nonNullScopeIds.Add(scopeId);
                            }
                        }

                        branchCaptures.Add(new(capture.Name, capture.Type, capture.Key,
                            ImmutableArray.CreateRange(scopeIds), nonNullScopeIds.ToImmutable()));
                    }

                    var branch = region.Branches[branchId];
                    branches.Add(new(branch.ScopeId, branch.Syntax, branch.Condition, branch.Items,
                        branch.ShapeIdentity, branchCaptures.ToImmutable()));
                }

                _conditionalRegions[regionId] = new(region.Id, region.OwnerElementId, region.ParentScopeId,
                    region.ParentRegionId, region.ParentBranchId, region.Syntax, branches.ToImmutable(),
                    region.RuntimeStorageRootScopeId, region.ReservedCapacity);
            }
        }

        private bool TryGetConditionalCapture(AkburaSyntax syntax, string name, int readScopeId,
            Dictionary<CSharpExpressionSyntax, ImmutableArray<CSharpLocalSymbol>> declarations,
            out int regionId, out int branchId, out CSharpLocalSymbol local, out string key)
        {
            regionId = -1;
            branchId = -1;
            local = null!;
            key = string.Empty;
            for (var ancestor = syntax.Parent; ancestor != null; ancestor = ancestor.Parent)
            {
                if (ancestor is not MarkupBlockSyntax block ||
                    !TryGetConditionalBranch(block, out var chain, out var reachedBranch) ||
                    !_conditionalSyntaxIds.TryGetValue(chain, out var candidateRegion))
                {
                    continue;
                }

                for (var conditionIndex = Math.Min(reachedBranch, chain.ElseIfClauses.Count); conditionIndex >= 0;
                    conditionIndex--)
                {
                    var condition = conditionIndex == 0 ? chain.Condition : chain.ElseIfClauses[conditionIndex - 1].Condition;
                    if (!declarations.TryGetValue(condition, out var declaredLocals))
                    {
                        declaredLocals = _semanticModel.GetCSharpDeclaredLocals(condition);
                        declarations.Add(condition, declaredLocals);
                    }

                    foreach (var declaredLocal in declaredLocals)
                    {
                        if (!string.Equals(declaredLocal.Name, name, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        var region = _conditionalRegions[candidateRegion];
                        if (region.RuntimeStorageRootScopeId == readScopeId)
                        {
                            return false;
                        }

                        regionId = candidateRegion;
                        branchId = reachedBranch;
                        local = declaredLocal;
                        var owner = _elements[region.OwnerElementId];
                        key = "conditional/" + ComponentHotReloadIdentity.CreateOperationSyntaxIdentity(owner.Syntax.StartTag ?? (AkburaSyntax)owner.Syntax) +
                            "/" + ComponentHotReloadIdentity.CreateOperationSyntaxIdentity(condition) + "/" +
                            condition.Position.ToString(CultureInfo.InvariantCulture) + "/local/" + name;
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool TryGetConditionalBranch(MarkupBlockSyntax block,
            out MarkupIfStatementSyntax chain, out int branchId)
        {
            if (block.Parent is MarkupIfStatementSyntax statement)
            {
                chain = statement;
                branchId = 0;
                return true;
            }

            if (block.Parent is MarkupElseIfClauseSyntax clause && clause.Parent is MarkupIfStatementSyntax clauseChain)
            {
                chain = clauseChain;
                branchId = 1;
                foreach (var candidate in chain.ElseIfClauses)
                {
                    if (ReferenceEquals(candidate, clause))
                    {
                        return true;
                    }

                    branchId++;
                }
            }
            else if (block.Parent is MarkupElseClauseSyntax finalClause && finalClause.Parent is MarkupIfStatementSyntax finalChain)
            {
                chain = finalChain;
                branchId = chain.ElseIfClauses.Count + 1;
                return true;
            }

            chain = null!;
            branchId = -1;
            return false;
        }

        private sealed class PendingConditionalCapture(string name, ITypeSymbol type, string key)
        {
            public string Name { get; } = name;

            public ITypeSymbol Type { get; } = type;

            public string Key { get; } = key;

            public HashSet<int> ScopeIds { get; } = [];

            public Dictionary<int, ConditionalCaptureEntryFlow> EntryFlows { get; } = [];

            public void RecordEntryFlow(int scopeId, in CSharpSymbolReference reference)
            {
                if (reference.IsNameOfOperand || reference.NullableFlowState == NullableFlowState.None)
                {
                    return;
                }

                var deferred = false;
                for (var syntax = reference.Syntax.Parent; syntax != null; syntax = syntax.Parent)
                {
                    if (syntax is CSharp.AnonymousFunctionExpressionSyntax or CSharp.LocalFunctionStatementSyntax)
                    {
                        deferred = true;
                        break;
                    }
                }

                var position = reference.SourceSpan.Start;
                var nonNull = reference.NullableFlowState == NullableFlowState.NotNull;
                if (EntryFlows.TryGetValue(scopeId, out var previous))
                {
                    if (!previous.IsDeferred && deferred)
                    {
                        return;
                    }

                    if (previous.IsDeferred == deferred)
                    {
                        if (position > previous.Position)
                        {
                            return;
                        }

                        if (position == previous.Position)
                        {
                            nonNull &= previous.IsNonNull;
                        }
                    }
                }

                EntryFlows[scopeId] = new(position, deferred, nonNull);
            }
        }

        private readonly struct ConditionalCaptureEntryFlow(int position, bool isDeferred, bool isNonNull)
        {
            public int Position { get; } = position;

            public bool IsDeferred { get; } = isDeferred;

            public bool IsNonNull { get; } = isNonNull;
        }
    }
}
