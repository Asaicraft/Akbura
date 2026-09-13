using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;
using System;

namespace Akbura.Language.CodeGeneration;

internal static partial class ComponentPlanner
{
    private ref partial struct Planner
    {
        private bool TryBuildConditionalTemplateBoundary(in ContentBoundary boundary, in TraversalContext inherited,
            out ComponentContentValueReference value, out int rootId)
        {
            value = default;
            rootId = -1;
            if (!boundary.IsValid || boundary.Operation.HasErrors)
            {
                return false;
            }

            var info = _semanticModel.GetConditionalTemplateRootInfo(boundary.Syntax);
            if (!info.IsSupported || !info.IsImplicitControlRoot)
            {
                return false;
            }

            rootId = CreateConditionalTemplateBoundaryRoot(boundary);
            var destination = CreateConditionalTemplateRootDestination(rootId);
            var contentModel = new MarkupContentModel(info.ContentModel.ContentProperty,
                new CSharpSymbolDefinition(destination.ClrProperty!.Type), isCollection: false, allowsText: false,
                contentParameter: info.ContentModel.ContentParameter);
            var context = new TraversalContext(
                boundary.IsTemplate ? boundary.CreateTemplateScope() : inherited.Template,
                boundary.IsDeferred ? boundary.CreateDeferredScope() : inherited.Deferred,
                inherited.Conditional);
            using var children = ImmutableArrayBuilder<int>.Rent();
            foreach (var child in boundary.Operation.Content)
            {
                if (child.Syntax is MarkupIfStatementSyntax conditional)
                {
                    BuildConditional(rootId, conditional, boundary.Operation, context, contentModel);
                }
                else if (child.Syntax is MarkupElementContentSyntax element &&
                    TryBuildElement(element.Element, rootId, context, isRoot: false, out var childId))
                {
                    children.Add(childId);
                }
            }

            _pendingContents.Add(new PendingContentPlan(rootId, boundary.Operation, AddElementIds(children.WrittenSpan),
                propertyElementId: -1, boundaryValue: default,
                destinationOverride: destination, contentModelOverride: contentModel));
            Span<int> roots = stackalloc int[1];
            roots[0] = rootId;
            value = boundary.IsDeferred ? CompleteBoundary(boundary, roots, default) : CompleteBoundary(boundary, default, roots);
            return value.IsValid;
        }
    }
}
