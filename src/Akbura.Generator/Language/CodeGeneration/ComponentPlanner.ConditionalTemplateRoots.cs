using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using System;
using System.Diagnostics;
using System.Linq;

namespace Akbura.Language.CodeGeneration;

internal static partial class ComponentPlanner
{
    private ref partial struct Planner
    {
        private int CreateConditionalTemplateBoundaryRoot(in ContentBoundary boundary)
        {
            Debug.Assert(boundary.IsValid);
            var type = _compilation.GetTypeByMetadataName("Akbura.Markup.AkburaConditionalTemplateInstance")
                ?? throw new InvalidOperationException("Conditional template roots require a matching Akbura runtime.");
            IMarkupComponentSymbol? symbol = null;
            for (AkburaSyntax? syntax = boundary.Syntax; syntax != null; syntax = syntax.Parent)
            {
                if (syntax is MarkupElementSyntax element &&
                    _semanticModel.GetSymbolInfo(element).Symbol is IMarkupComponentSymbol component)
                {
                    symbol = component;
                    break;
                }
            }

            Debug.Assert(symbol != null);
            if (symbol == null)
            {
                throw new InvalidOperationException("A conditional template must have a markup owner.");
            }

            if (!_localRuntimeStorageCounts.ContainsKey(boundary.ScopeId))
            {
                _localRuntimeStorageCounts.Add(boundary.ScopeId, 0);
            }

            var runtimeId = GetNextRuntimeStorageId(boundary.ScopeId);
            Debug.Assert(runtimeId == 0);
            var id = _elements.Count;
            var kind = boundary.IsDeferred
                ? ComponentElementScopeKind.DeferredContent
                : ComponentElementScopeKind.DataTemplate;
            var flags = ComponentElementFlags.IsLocal | ComponentElementFlags.RequiresLocalMarkupContext |
                ComponentElementFlags.UsesRuntimeStorage | ComponentElementFlags.IsConditionalTemplateRoot;
            flags |= boundary.IsDeferred ? ComponentElementFlags.IsDeferred : ComponentElementFlags.IsTemplateElement;
            _elements.Add(new PendingElementPlan(id, boundary.Syntax, symbol, type,
                CreateRuntimeStorageExpression(type, runtimeId, isForeachLocal: false),
                boundary.OwnerElementId, boundary.ScopeId,
                kind, flags, children: default, pendingFirstUpdateActions: default, propertyElements: default,
                runtimeStorageId: runtimeId, runtimeStorageRootScopeId: boundary.ScopeId));
            return id;
        }

        private PropertyWritePlan CreateConditionalTemplateRootDestination(int rootId)
        {
            var property = _elements[rootId].Type.GetMembers("Root")
                .OfType<Microsoft.CodeAnalysis.IPropertySymbol>().Single();
            return PropertyWritePlan.Create(property);
        }
    }
}
