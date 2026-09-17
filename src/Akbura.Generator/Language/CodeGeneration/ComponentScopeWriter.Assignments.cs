using System;

namespace Akbura.Language.CodeGeneration;

internal readonly ref partial struct ComponentScopeWriter
{
    private void WriteApplyReplacedStyles(in ComponentPlan plan, in ComponentScopePlan scope)
    {
        foreach (var content in plan.CollectionContents)
        {
            ref readonly var owner = ref plan.Elements.ItemRef(content.OwnerElementId);
            if (content.ReplacesStyles && owner.ScopeId == scope.Id && !owner.IsStyleSubtree)
            {
                _writer.Write("global::Akbura.HotReload.AkburaRenderStyleHelper.ApplyStyling(");
                _writer.Write(owner.Identifier).WriteLine(");");
            }
        }
    }

    private void WriteElementAssignments(
        in ComponentPlan plan, int elementId, in ComponentScopeWriteContext scopeContext,
        bool[] initialized, ComponentAssignmentPhase phase)
    {
        if (initialized[elementId])
        {
            return;
        }

        initialized[elementId] = true;
        ref readonly var element = ref plan.Elements.ItemRef(elementId);
        var context = scopeContext.ForElement(elementId);
        var actualPhase = element.IsStyleSubtree ? ComponentAssignmentPhase.LocalInitial : phase;
        if (phase == ComponentAssignmentPhase.Initial && element.UsesRuntimeStorage)
        {
            WriteStructuralOrderedAssignments(plan, element, context, conditionalOneTime: false,
                skipFrameContents: false, initialized, scopeContext);
        }
        else
        {
            WriteOrderedAssignments(plan, element, context, actualPhase, initialized, scopeContext, phase);
        }

        if (phase != ComponentAssignmentPhase.Update || element.IsStyleSubtree)
        {
            WriteSetStyles(plan, elementId, element.Identifier, context);
        }

        if (element.IsStyleSubtree)
        {
            new ElementWriter(_writer, _sourceMap).WriteEndInit(element);
        }

        if (phase == ComponentAssignmentPhase.Update)
        {
            WriteRefresh(element, element.Identifier);
        }
    }

    private void WriteContentChildren(
        in ComponentPlan plan, in ComponentContentTargetReference target,
        in ComponentScopeWriteContext scopeContext, bool[] initialized, ComponentAssignmentPhase phase)
    {
        if (target.Kind == ComponentContentTargetKind.Property)
        {
            var value = plan.PropertyContents.ItemRef(target.Index).FirstUpdateValue;
            if (value.Kind == ComponentContentValueKind.Element)
            {
                WriteElementAssignments(plan, value.Index, scopeContext, initialized, phase);
            }
        }
        else if (target.Kind == ComponentContentTargetKind.Collection)
        {
            var items = plan.CollectionContents.ItemRef(target.Index).Items;
            for (var i = 0; i < items.Length; i++)
            {
                var value = plan.ContentItems.ItemRef(items.Start + i).Value;
                if (value.Kind == ComponentContentValueKind.Element)
                {
                    WriteElementAssignments(plan, value.Index, scopeContext, initialized, phase);
                }
            }
        }
        else if (target.Kind == ComponentContentTargetKind.Conditional)
        {
            var items = plan.ConditionalContents.ItemRef(target.Index).Items;
            for (var i = 0; i < items.Length; i++)
            {
                var value = plan.ContentItems.ItemRef(items.Start + i).Value;
                if (value.Kind == ComponentContentValueKind.Element)
                {
                    WriteElementAssignments(plan, value.Index, scopeContext, initialized, phase);
                }
            }
        }
    }

    private void WriteStructuralOrderedAssignments(
        in ComponentPlan plan,
        in ComponentElementPlan element,
        in MarkupExtensionWriteContext context,
        bool conditionalOneTime,
        bool skipFrameContents,
        bool[]? initialized = null,
        ComponentScopeWriteContext initializationContext = default)
    {
        var propertyContext = context.WithTarget(element.Identifier, context.TargetProperty,
            element.ScopeId, plan.ElementReferences.AsSpan());
        var properties = new ComponentPropertyWriter(_writer, in _bindingEnvironment, _sourceMap);
        var actions = new ComponentFirstUpdateActionWriter(_writer, _sourceMap);
        var subscriptions = new PropertySubscriptionWriter(_writer, _sourceMap, writeInlineHandlers: true);
        foreach (var assignment in element.Assignments)
        {
            if (assignment.Kind == ComponentAssignmentKind.Property)
            {
                ref readonly var property = ref plan.PropertyWrites.ItemRef(assignment.Index);
                if (!property.WritesDuringFirstUpdate)
                {
                    continue;
                }

                if (property.ValueKind == ComponentPropertyValueKind.Constant)
                {
                    WriteStructuralPropertyOperation(plan, element, property, element.Identifier, propertyContext);
                }
                else if (ComponentPropertyWriter.CanWriteStructuralBindingValue(property))
                {
                    properties.WriteStructuralBindingValue(plan, property, element.RuntimeStorageId,
                        element.Identifier, propertyContext);
                }
                else
                {
                    WriteConditionalInitialStart(element, conditionalOneTime);
                    properties.Write(plan, property, element.Identifier, propertyContext);
                    WriteConditionalInitialEnd(conditionalOneTime);
                }

                continue;
            }

            if (assignment.Kind == ComponentAssignmentKind.Content)
            {
                var target = assignment.Content;
                if (target.Kind == ComponentContentTargetKind.Conditional)
                {
                    if (!skipFrameContents)
                    {
                        if (initialized != null)
                        {
                            WriteContentChildren(plan, target, initializationContext, initialized, ComponentAssignmentPhase.Initial);
                        }

                        WriteConditionalContent(plan, plan.ConditionalContents.ItemRef(target.Index), context);
                    }

                    continue;
                }

                if (initialized != null)
                {
                    WriteContentChildren(plan, target, initializationContext, initialized, ComponentAssignmentPhase.Initial);
                }

                if (target.Kind == ComponentContentTargetKind.Collection)
                {
                    ref readonly var collection = ref plan.CollectionContents.ItemRef(target.Index);
                    if (skipFrameContents && (collection.DictionaryShape.IsDictionary || collection.ReplacesStyles))
                    {
                        continue;
                    }

                    if (ComponentContentWriter.CanWriteStructuralCollection(plan, collection))
                    {
                        WriteContentTarget(plan, target, isFirstUpdate: true, context);
                    }
                    else
                    {
                        WriteConditionalInitialStart(element, conditionalOneTime);
                        WriteContentTarget(plan, target, isFirstUpdate: true, context);
                        WriteConditionalInitialEnd(conditionalOneTime);
                    }
                }
                else if (target.Kind == ComponentContentTargetKind.Property)
                {
                    ref readonly var content = ref plan.PropertyContents.ItemRef(target.Index);
                    var hasOwnedFactory =
                        content.FirstUpdateValue.Kind is ComponentContentValueKind.Template or ComponentContentValueKind.DeferredContent &&
                        content.Destination.Kind is PropertyWriteKind.ClrProperty or PropertyWriteKind.AvaloniaProperty or
                            PropertyWriteKind.ComponentParameter or PropertyWriteKind.DirectMember;
                    if (content.FirstUpdateValue.Kind == ComponentContentValueKind.Element ||
                        ComponentContentWriter.CanWriteStructuralConstantValue(plan, content) || hasOwnedFactory)
                    {
                        // Source initialization must visit an owned factory slot even
                        // when its receiver's own declaration stayed unchanged. Normal
                        // frames do not enter this structural initialization path.
                        WriteContentTarget(plan, target, isFirstUpdate: true, context);
                    }
                    else
                    {
                        WriteConditionalInitialStart(element, conditionalOneTime);
                        WriteContentTarget(plan, target, isFirstUpdate: true, context);
                        WriteConditionalInitialEnd(conditionalOneTime);
                    }
                }

                continue;
            }

            ref readonly var action = ref plan.FirstUpdateActions.ItemRef(assignment.Index);
            switch (action.Kind)
            {
                case ComponentFirstUpdateActionKind.NameAssignment:
                    WriteConditionalInitialStart(element, conditionalOneTime);
                    actions.WriteNameAssignment(plan.NameAssignments.ItemRef(action.Index), element.Identifier, null);
                    WriteConditionalInitialEnd(conditionalOneTime);
                    break;
                case ComponentFirstUpdateActionKind.PropertySubscription:
                    if (element.ConditionalRegionId >= 0 || element.RuntimeStorageRootScopeId > 0)
                    {
                        break;
                    }

                    subscriptions.WriteStructuralRegistration(element, plan.PropertySubscriptions.ItemRef(action.Index));
                    break;
                case ComponentFirstUpdateActionKind.RoutedEvent:
                    if (element.ConditionalRegionId >= 0 || element.RuntimeStorageRootScopeId > 0)
                    {
                        break;
                    }

                    actions.WriteStructuralRoutedEvent(plan.RoutedEvents.ItemRef(action.Index),
                        element.RuntimeStorageId, element.Identifier);
                    break;
                case ComponentFirstUpdateActionKind.CommandBinding:
                    if (element.ConditionalRegionId >= 0 || element.RuntimeStorageRootScopeId > 0)
                    {
                        break;
                    }

                    ref readonly var command = ref plan.CommandBindings.ItemRef(action.Index);
                    if (ComponentFirstUpdateActionWriter.CanWriteStructuralCommandBinding(command))
                    {
                        actions.WriteStructuralCommandBinding(command, element.RuntimeStorageId, element.Identifier);
                    }
                    else
                    {
                        WriteConditionalInitialStart(element, conditionalOneTime);
                        actions.WriteCommandBinding(command, element.Identifier);
                        WriteConditionalInitialEnd(conditionalOneTime);
                    }

                    break;
            }
        }
    }

    private void WriteConditionalInitialStart(in ComponentElementPlan element, bool conditional)
    {
        if (!conditional)
        {
            return;
        }

        _writer.Write("if (").Write(ComponentStructuralHotReloadWriter.RenderStateFieldName);
        _writer.Write(".IsNew(").WriteIntegerLiteral(element.RuntimeStorageId).Write(") || ");
        _writer.Write(ComponentStructuralHotReloadWriter.RenderStateFieldName);
        _writer.Write(".ShouldApplyInitialValues(").WriteIntegerLiteral(element.RuntimeStorageId).WriteLine("))");
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;
    }

    private void WriteConditionalInitialEnd(bool conditional)
    {
        if (conditional)
        {
            _writer.CurrentIndent -= _writer.TabSize;
            _writer.WriteLine("}");
        }
    }

    private bool WriteOrderedAssignments(
        in ComponentPlan plan,
        in ComponentElementPlan element,
        in MarkupExtensionWriteContext context,
        ComponentAssignmentPhase phase,
        bool[]? initialized = null,
        ComponentScopeWriteContext initializationContext = default,
        ComponentAssignmentPhase childPhase = ComponentAssignmentPhase.Initial)
    {
        var propertyContext = context.WithTarget(element.Identifier, context.TargetProperty,
            element.ScopeId, plan.ElementReferences.AsSpan());
        var propertyWriter = new ComponentPropertyWriter(_writer, in _bindingEnvironment, _sourceMap);
        var actionWriter = new ComponentFirstUpdateActionWriter(_writer, _sourceMap);
        var subscriptions = new PropertySubscriptionWriter(_writer, _sourceMap,
            writeInlineHandlers: _generationMode.UsesStructuralRuntime());
        var wroteAny = false;
        foreach (var assignment in element.Assignments)
        {
            switch (assignment.Kind)
            {
                case ComponentAssignmentKind.Property:
                {
                    ref readonly var property = ref plan.PropertyWrites.ItemRef(assignment.Index);
                    var write = phase switch
                    {
                        ComponentAssignmentPhase.Initial => property.WritesDuringFirstUpdate,
                        ComponentAssignmentPhase.LocalInitial => true,
                        ComponentAssignmentPhase.Update => property.WritesDuringUpdate,
                        ComponentAssignmentPhase.HotReload => property.Phase == ComponentPropertyWritePhase.FirstUpdate &&
                            property.ValueKind == ComponentPropertyValueKind.Constant,
                        _ => false,
                    };
                    if (write)
                    {
                        propertyWriter.Write(plan, property, element.Identifier, propertyContext);
                        wroteAny = true;
                    }

                    break;
                }
                case ComponentAssignmentKind.Content:
                    if (assignment.Content.Kind == ComponentContentTargetKind.Conditional)
                    {
                        if (phase != ComponentAssignmentPhase.HotReload)
                        {
                            if (initialized != null)
                            {
                                WriteContentChildren(plan, assignment.Content, initializationContext, initialized, childPhase);
                            }

                            WriteConditionalContent(plan, plan.ConditionalContents.ItemRef(assignment.Content.Index), context);
                            wroteAny = true;
                        }

                        break;
                    }

                    if (initialized != null)
                    {
                        WriteContentChildren(plan, assignment.Content, initializationContext, initialized, childPhase);
                    }

                    if (phase is ComponentAssignmentPhase.Initial or ComponentAssignmentPhase.LocalInitial)
                    {
                        wroteAny |= WriteContentTarget(plan, assignment.Content, isFirstUpdate: true, context);
                    }

                    if (phase is ComponentAssignmentPhase.Update or ComponentAssignmentPhase.LocalInitial)
                    {
                        if (phase == ComponentAssignmentPhase.LocalInitial &&
                            assignment.Content.Kind == ComponentContentTargetKind.Property)
                        {
                            ref readonly var content = ref plan.PropertyContents.ItemRef(assignment.Content.Index);
                            if (content.FirstUpdateValue.Kind == content.UpdateValue.Kind &&
                                content.FirstUpdateValue.Index == content.UpdateValue.Index)
                            {
                                break;
                            }
                        }

                        // Owned snapshots are committed once, including during local initialization.
                        if (phase != ComponentAssignmentPhase.LocalInitial ||
                            assignment.Content.Kind != ComponentContentTargetKind.Collection ||
                            !(plan.CollectionContents.ItemRef(assignment.Content.Index).DictionaryShape.IsDictionary ||
                                plan.CollectionContents.ItemRef(assignment.Content.Index).ReplacesStyles))
                        {
                            wroteAny |= WriteContentTarget(plan, assignment.Content, isFirstUpdate: false, context);
                        }
                    }

                    break;
                case ComponentAssignmentKind.FirstUpdateAction:
                    if (phase is not (ComponentAssignmentPhase.Initial or ComponentAssignmentPhase.LocalInitial))
                    {
                        break;
                    }

                    ref readonly var action = ref plan.FirstUpdateActions.ItemRef(assignment.Index);
                    switch (action.Kind)
                    {
                        case ComponentFirstUpdateActionKind.NameAssignment:
                            actionWriter.WriteNameAssignment(plan.NameAssignments.ItemRef(action.Index),
                                element.Identifier, element.IsLocal ? context.NameScopeExpression : null);
                            break;
                        case ComponentFirstUpdateActionKind.PropertySubscription:
                            subscriptions.WriteRegistration(element, plan.PropertySubscriptions.ItemRef(action.Index));
                            break;
                        case ComponentFirstUpdateActionKind.RoutedEvent:
                            actionWriter.WriteRoutedEvent(plan.RoutedEvents.ItemRef(action.Index), element.Identifier);
                            break;
                        case ComponentFirstUpdateActionKind.CommandBinding:
                            actionWriter.WriteCommandBinding(plan.CommandBindings.ItemRef(action.Index), element.Identifier);
                            break;
                        default:
                            throw new InvalidOperationException("An invalid ordered first-update action reached generation.");
                    }

                    wroteAny = true;
                    break;
            }
        }

        return wroteAny;
    }
}
