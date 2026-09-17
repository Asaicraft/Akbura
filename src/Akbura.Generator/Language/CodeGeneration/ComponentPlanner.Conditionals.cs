using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Immutable;
using System.Linq;

namespace Akbura.Language.CodeGeneration;

internal static partial class ComponentPlanner
{
    private ref partial struct Planner
    {
        private void BuildConditional(int ownerId, MarkupIfStatementSyntax syntax,
            IMarkupContentOperation? destination, TraversalContext context,
            MarkupContentModel? contentModelOverride = null)
        {
            if (destination == null || _semanticModel.GetOperation(syntax) is not IMarkupIfOperation operation || operation.HasErrors)
            {
                return;
            }

            var id = _pendingConditionalRegions.Count;
            var parentScope = context.GetEffectiveScope().ScopeId;
            var parent = _conditionalScopes.TryGetValue(parentScope, out var entry) ? entry : (-1, -1);
            _pendingConditionalRegions.Add(default);
            _conditionalSyntaxIds.Add(syntax, id);
            using var scopes = ImmutableArrayBuilder<int>.Rent();
            foreach (var branch in operation.Branches)
            {
                var scopeId = AddPendingScope(parentScope, ownerId, ComponentElementScopeKind.ConditionalBranch);
                _conditionalScopes.Add(scopeId, (id, scopes.Count));
                scopes.Add(scopeId);
                var branchContext = new TraversalContext(context.Template, context.Deferred,
                    new ScopeReference(scopeId, ownerId, ComponentElementScopeKind.ConditionalBranch, isDeferred: false));
                BuildConditionalChildren(ownerId, branch.Content, destination, branchContext, contentModelOverride);
            }

            _pendingConditionalRegions[id] = new PendingConditionalRegion(ownerId, parentScope,
                parent.Item1, parent.Item2, operation, contentModelOverride ?? destination.ContentModel, scopes.ToImmutable());
        }

        private void BuildConditionalChildren(int ownerId, ImmutableArray<MarkupChildContent> content,
            IMarkupContentOperation destination, TraversalContext context,
            MarkupContentModel? contentModelOverride = null)
        {
            foreach (var child in content)
            {
                if (child.Syntax is MarkupForeachStatementSyntax foreachSyntax)
                {
                    BuildForeach(ownerId, foreachSyntax, context);
                }
                else if (child.Syntax is MarkupIfStatementSyntax nested)
                {
                    BuildConditional(ownerId, nested, destination, context, contentModelOverride);
                }
                else if (child.Syntax is MarkupElementContentSyntax element)
                {
                    TryBuildElement(element.Element, ownerId, context, isRoot: false, out _);
                }
            }
        }

        private ComponentContentTargetReference LowerConditionalContent(in PendingContentPlan pending)
        {
            var operation = pending.Operation;
            var model = pending.ContentModel;
            var isCollection = model.Kind is MarkupContentKind.Collection or MarkupContentKind.Dictionary or MarkupContentKind.AddMethods;
            var collection = isCollection ? CreateCollectionWritePlan(operation) : default;
            var property = pending.DestinationOverride;
            if (!property.IsValid && !isCollection && operation.Property != null)
            {
                property = PropertyWritePlan.Create(operation.Property, _elements[pending.OwnerElementId].Type);
            }
            if (!collection.IsValid && !property.IsValid)
            {
                return default;
            }

            var items = LowerConditionalSequence(operation.Content, model);
            var id = _conditionalContents.Count;
            _conditionalContents.Add(new ComponentConditionalContentPlan(id, pending.OwnerElementId,
                property, collection, items, operation.Syntax, model.DictionaryShape,
                SequenceContainsStyles(operation.Content)));
            return new(ComponentContentTargetKind.Conditional, id);
        }

        private bool SequenceContainsStyles(ImmutableArray<MarkupChildContent> content)
        {
            var styleType = _compilation.GetTypeByMetadataName("Avalonia.Styling.StyleBase");
            foreach (var child in content)
            {
                if (child.ComponentSymbol?.ComponentType is { } type && IsImplicitConversion(type, styleType))
                {
                    return true;
                }

                if (child.ConditionalOperation is { } conditional)
                {
                    foreach (var branch in conditional.Branches)
                    {
                        if (SequenceContainsStyles(branch.Content))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private ComponentPlanRange LowerConditionalSequence(ImmutableArray<MarkupChildContent> content, MarkupContentModel model)
        {
            using var items = ImmutableArrayBuilder<ComponentContentItemPlan>.Rent();
            foreach (var child in content)
            {
                var value = default(ComponentContentValueReference);
                if (child.Kind == MarkupChildKind.Foreach && _foreachSyntaxIds.TryGetValue(child.Syntax, out var foreachId))
                {
                    value = new(ComponentContentValueKind.Foreach, foreachId);
                }
                else if (child.Kind == MarkupChildKind.Conditional && _conditionalSyntaxIds.TryGetValue(child.Syntax, out var region))
                {
                    LowerConditionalRegion(region);
                    value = new(ComponentContentValueKind.Conditional, region);
                }
                else if (child.Syntax is MarkupElementContentSyntax element && _syntaxElementIds.TryGetValue(element.Element, out var elementId))
                {
                    value = new(ComponentContentValueKind.Element, elementId);
                }
                else if (child.Kind == MarkupChildKind.Text)
                {
                    value = AddTextContentValue(child, model);
                }
                else if (child.Kind == MarkupChildKind.Expression)
                {
                    value = AddExpressionContentValue(child, model);
                }

                if (!value.IsValid)
                {
                    continue;
                }

                var key = default(ComponentContentValueReference);
                if (model.IsDictionary && child.ComponentSymbol?.AttributeOperations
                    .OfType<IMarkupDictionaryKeyOperation>().FirstOrDefault() is { HasErrors: false } keyOperation)
                {
                    var keyId = _csharpValues.Count;
                    _csharpValues.Add(new ComponentCSharpValuePlan(keyOperation.ValueOperation, null,
                        keyOperation.LiteralValue, keyOperation.KeyType.Symbol as ITypeSymbol));
                    key = new(keyOperation.ValueSyntax is MarkupLiteralAttributeValueSyntax
                        ? ComponentContentValueKind.Constant : ComponentContentValueKind.CSharpExpression, keyId);
                }

                items.Add(new(value, child.Syntax, child.InsertionMethod, key));
            }

            var start = _contentItems.Count;
            _contentItems.AddRange(items.WrittenSpan);
            return new(start, items.Count);
        }

        private void LowerConditionalRegion(int id)
        {
            while (_conditionalRegions.Count <= id)
            {
                _conditionalRegions.Add(default);
            }

            if (!_conditionalRegions[id].Branches.IsDefault)
            {
                return;
            }

            var pending = _pendingConditionalRegions[id];
            using var branches = ImmutableArrayBuilder<ComponentConditionalBranchPlan>.Rent();
            for (var i = 0; i < pending.Operation.Branches.Length; i++)
            {
                var branch = pending.Operation.Branches[i];
                ComponentPlanRange items;
                if (!branch.ValueOperation.IsDefault || branch.LiteralValue != null)
                {
                    var valueId = _csharpValues.Count;
                    _csharpValues.Add(new ComponentCSharpValuePlan(branch.ValueOperation, null,
                        branch.LiteralValue, pending.ContentModel.AllowedChildType.Symbol as ITypeSymbol));
                    items = new(_contentItems.Count, 1);
                    _contentItems.Add(new(new(branch.LiteralValue != null ? ComponentContentValueKind.Constant
                        : ComponentContentValueKind.CSharpExpression, valueId), branch.Syntax));
                }
                else
                {
                    items = LowerConditionalSequence(branch.Content, pending.ContentModel);
                }

                var shape = string.Join(";", branch.Content.Select(child => child.ComponentSymbol?.ComponentType?.ToDisplayString()
                    ?? child.Kind.ToString()));
                branches.Add(new(pending.ScopeIds[i], branch.Syntax, branch.ConditionSyntax, items,
                    shape.Length == 0 ? "$empty" : shape));
            }

            _conditionalRegions[id] = new(id, pending.OwnerId, pending.ParentScopeId,
                pending.ParentRegionId, pending.ParentBranchId, pending.Operation.Syntax, branches.ToImmutable(),
                _elements[pending.OwnerId].RuntimeStorageRootScopeId, pending.Operation.Cardinality.Maximum);
        }

        private readonly struct PendingConditionalRegion(int ownerId, int parentScopeId,
            int parentRegionId, int parentBranchId, IMarkupIfOperation operation,
            MarkupContentModel contentModel, ImmutableArray<int> scopeIds)
        {
            public int OwnerId { get; } = ownerId;
            public int ParentScopeId { get; } = parentScopeId;
            public int ParentRegionId { get; } = parentRegionId;
            public int ParentBranchId { get; } = parentBranchId;
            public IMarkupIfOperation Operation { get; } = operation;
            public MarkupContentModel ContentModel { get; } = contentModel;
            public ImmutableArray<int> ScopeIds { get; } = scopeIds;
        }
    }
}
