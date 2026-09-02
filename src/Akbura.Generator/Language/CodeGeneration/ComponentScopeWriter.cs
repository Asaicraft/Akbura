using System.Diagnostics;

namespace Akbura.Language.CodeGeneration;

/// <summary>
/// Writes the initialization pipeline for one exact component generation
/// scope. Nested scopes are represented by deferred or template values and are
/// never traversed here.
/// </summary>
internal readonly ref struct ComponentScopeWriter
{
    private readonly CodeWriter _writer;
    private readonly BindingWriterEnvironment _bindingEnvironment;
    private readonly ComponentGenerationSourceMap _sourceMap;
    private readonly string _ownerTypeName;

    public ComponentScopeWriter(
        CodeWriter writer,
        in BindingWriterEnvironment bindingEnvironment,
        ComponentGenerationSourceMap sourceMap,
        string ownerTypeName)
    {
        Debug.Assert(writer != null);
        Debug.Assert(sourceMap != null);
        Debug.Assert(!string.IsNullOrEmpty(ownerTypeName));

        _writer = writer!;
        _bindingEnvironment = bindingEnvironment;
        _sourceMap = sourceMap!;
        _ownerTypeName = ownerTypeName;
    }

    public void WriteComponentInitialState(
        in ComponentPlan plan,
        in ComponentScopePlan scope,
        in ComponentScopeWriteContext context)
    {
        Debug.Assert(scope.Kind == ComponentElementScopeKind.Component);
        Debug.Assert(scope.Id == context.ScopeId);
        Debug.Assert((uint)scope.Id < (uint)plan.Scopes.Length);

        var indent = _writer.CurrentIndent;

        try
        {
            WriteElementCreation(plan, scope);
            WriteBeginInit(plan, scope);
            WriteInitialRenderStatements(plan);

            for (var i = 0; i < scope.Elements.Length; i++)
            {
                var elementId = GetScopeElementId(plan, scope, i);
                ref readonly var element = ref plan.Elements.ItemRef(elementId);
                var targetExpression = element.Identifier;
                var elementContext = context.ForElement(elementId);

                WriteFirstUpdateActions(plan, elementId, elementContext);
                WriteElementContent(plan, element, isFirstUpdate: true, elementContext);
                WritePropertyElements(plan, element, isFirstUpdate: true, elementContext);
                WriteSetStyles(plan, elementId, targetExpression, elementContext);
            }

            WriteEndInit(plan, scope);
        }
        finally
        {
            _writer.CurrentIndent = indent;
        }
    }

    private void WriteInitialRenderStatements(in ComponentPlan plan)
    {
        if (plan.RenderStatements.IsDefaultOrEmpty)
        {
            return;
        }

        var writer = new ComponentRenderStatementWriter(_writer, _sourceMap);

        for (var i = 0; i < plan.RenderStatements.Length; i++)
        {
            ref readonly var statement = ref plan.RenderStatements.ItemRef(i);
            if (statement.WritesDuringFirstUpdate)
            {
                writer.Write(statement);
            }
        }
    }

    public void WriteLocalInitialState(
        in ComponentPlan plan,
        in ComponentScopePlan scope,
        in ComponentScopeWriteContext context)
    {
        Debug.Assert(scope.Kind != ComponentElementScopeKind.Component);
        Debug.Assert(scope.Id == context.ScopeId);
        Debug.Assert((uint)scope.Id < (uint)plan.Scopes.Length);

        var indent = _writer.CurrentIndent;

        try
        {
            WriteElementCreation(plan, scope);
            WriteBeginInit(plan, scope);

            for (var i = 0; i < scope.Elements.Length; i++)
            {
                var elementId = GetScopeElementId(plan, scope, i);
                ref readonly var element = ref plan.Elements.ItemRef(elementId);
                var targetExpression = element.Identifier;
                var elementContext = context.ForElement(elementId);

                WriteFirstUpdateActions(plan, elementId, elementContext);
                WriteInitialDynamicProperties(plan, elementId, elementContext);
                WriteElementContent(plan, element, isFirstUpdate: true, elementContext);
                WriteElementContent(plan, element, isFirstUpdate: false, elementContext);
                WritePropertyElements(plan, element, isFirstUpdate: true, elementContext);
                WritePropertyElements(plan, element, isFirstUpdate: false, elementContext);
                WriteSetStyles(plan, elementId, targetExpression, elementContext);
            }

            WriteEndInit(plan, scope);
        }
        finally
        {
            _writer.CurrentIndent = indent;
        }
    }

    public void WriteUpdateState(
        in ComponentPlan plan,
        in ComponentScopePlan scope,
        in ComponentScopeWriteContext context)
    {
        Debug.Assert(scope.Kind == ComponentElementScopeKind.Component);
        Debug.Assert(scope.Id == context.ScopeId);
        Debug.Assert((uint)scope.Id < (uint)plan.Scopes.Length);

        var indent = _writer.CurrentIndent;

        try
        {
            for (var i = 0; i < scope.Elements.Length; i++)
            {
                var elementId = GetScopeElementId(plan, scope, i);
                ref readonly var element = ref plan.Elements.ItemRef(elementId);
                var targetExpression = element.Identifier;
                var elementContext = context.ForElement(elementId);

                WriteRuntimeUpdateProperties(plan, elementId, elementContext);
                WriteElementContent(plan, element, isFirstUpdate: false, elementContext);
                WritePropertyElements(plan, element, isFirstUpdate: false, elementContext);
                WriteRefresh(element, targetExpression);
            }
        }
        finally
        {
            _writer.CurrentIndent = indent;
        }
    }

    public bool WriteFirstUpdateActions(
        in ComponentPlan plan,
        int elementId,
        in MarkupExtensionWriteContext context)
    {
        ref readonly var element = ref GetElement(plan, elementId);
        if (element.FirstUpdateActions.IsEmpty)
        {
            return false;
        }

        var targetExpression = element.Identifier;
        var propertyContext = context.WithTarget(
            targetExpression,
            context.TargetProperty,
            element.ScopeId,
            plan.ElementReferences.AsSpan());
        var propertyWriter = new ComponentPropertyWriter(
            _writer,
            in _bindingEnvironment,
            _sourceMap);
        var subscriptionWriter = new PropertySubscriptionWriter(_writer, _sourceMap);
        var actionWriter = new ComponentFirstUpdateActionWriter(_writer, _sourceMap);

        for (var i = 0; i < element.FirstUpdateActions.Length; i++)
        {
            ref readonly var action = ref plan.FirstUpdateActions.ItemRef(
                element.FirstUpdateActions.Start + i);

            switch (action.Kind)
            {
                case ComponentFirstUpdateActionKind.NameAssignment:
                {
                    Debug.Assert((uint)action.Index < (uint)plan.NameAssignments.Length);
                    ref readonly var name = ref plan.NameAssignments.ItemRef(action.Index);
                    actionWriter.WriteNameAssignment(
                        name,
                        targetExpression,
                        element.IsLocal
                            ? context.NameScopeExpression
                            : null);
                    break;
                }
                case ComponentFirstUpdateActionKind.PropertyWrite:
                {
                    Debug.Assert((uint)action.Index < (uint)plan.PropertyWrites.Length);
                    ref readonly var property = ref plan.PropertyWrites.ItemRef(action.Index);
                    propertyWriter.Write(plan, property, targetExpression, propertyContext);
                    break;
                }
                case ComponentFirstUpdateActionKind.PropertySubscription:
                {
                    Debug.Assert((uint)action.Index < (uint)plan.PropertySubscriptions.Length);
                    ref readonly var subscription = ref plan.PropertySubscriptions.ItemRef(action.Index);
                    subscriptionWriter.WriteRegistration(element, subscription);
                    break;
                }
                case ComponentFirstUpdateActionKind.RoutedEvent:
                {
                    Debug.Assert((uint)action.Index < (uint)plan.RoutedEvents.Length);
                    ref readonly var routedEvent = ref plan.RoutedEvents.ItemRef(action.Index);
                    actionWriter.WriteRoutedEvent(routedEvent, targetExpression);
                    break;
                }
                case ComponentFirstUpdateActionKind.CommandBinding:
                {
                    Debug.Assert((uint)action.Index < (uint)plan.CommandBindings.Length);
                    ref readonly var command = ref plan.CommandBindings.ItemRef(action.Index);
                    actionWriter.WriteCommandBinding(command, targetExpression);
                    break;
                }
                default:
                    Debug.Fail("An invalid first-update action reached code generation.");
                    break;
            }
        }

        return true;
    }

    public bool WriteUpdateProperties(
        in ComponentPlan plan,
        int elementId,
        in MarkupExtensionWriteContext context)
    {
        return WriteRuntimeUpdateProperties(plan, elementId, context);
    }

    private bool WriteInitialDynamicProperties(
        in ComponentPlan plan,
        int elementId,
        in MarkupExtensionWriteContext context)
    {
        return WriteProperties(
            plan,
            elementId,
            context,
            ComponentPropertyWriteFilter.UpdateOnly);
    }

    private bool WriteRuntimeUpdateProperties(
        in ComponentPlan plan,
        int elementId,
        in MarkupExtensionWriteContext context)
    {
        return WriteProperties(
            plan,
            elementId,
            context,
            ComponentPropertyWriteFilter.RuntimeUpdate);
    }

    private bool WriteProperties(
        in ComponentPlan plan,
        int elementId,
        in MarkupExtensionWriteContext context,
        ComponentPropertyWriteFilter filter)
    {
        ref readonly var element = ref GetElement(plan, elementId);
        if (element.PropertyWrites.IsEmpty)
        {
            return false;
        }

        var targetExpression = element.Identifier;
        var propertyContext = context.WithTarget(
            targetExpression,
            context.TargetProperty,
            element.ScopeId,
            plan.ElementReferences.AsSpan());
        var writer = new ComponentPropertyWriter(
            _writer,
            in _bindingEnvironment,
            _sourceMap);
        var wroteAny = false;

        for (var i = 0; i < element.PropertyWrites.Length; i++)
        {
            ref readonly var property = ref plan.PropertyWrites.ItemRef(
                element.PropertyWrites.Start + i);
            var shouldWrite = filter == ComponentPropertyWriteFilter.UpdateOnly
                ? property.Phase == ComponentPropertyWritePhase.Update
                : property.WritesDuringUpdate;
            if (!shouldWrite)
            {
                continue;
            }

            writer.Write(plan, property, targetExpression, propertyContext);
            wroteAny = true;
        }

        return wroteAny;
    }

    public bool WriteFirstUpdateContent(
        in ComponentPlan plan,
        int elementId,
        in MarkupExtensionWriteContext context)
    {
        ref readonly var element = ref GetElement(plan, elementId);
        return WriteElementContent(plan, element, isFirstUpdate: true, context);
    }

    public bool WriteUpdateContent(
        in ComponentPlan plan,
        int elementId,
        in MarkupExtensionWriteContext context)
    {
        ref readonly var element = ref GetElement(plan, elementId);
        var wroteAny = WriteElementContent(
            plan,
            element,
            isFirstUpdate: false,
            context);

        wroteAny |= WritePropertyElements(
            plan,
            element,
            isFirstUpdate: false,
            context);
        return wroteAny;
    }

    public bool WritePropertyElements(
        in ComponentPlan plan,
        int elementId,
        in MarkupExtensionWriteContext context)
    {
        ref readonly var element = ref GetElement(plan, elementId);
        return WritePropertyElements(plan, element, isFirstUpdate: true, context);
    }

    public bool WriteSetStyles(
        in ComponentPlan plan,
        int elementId,
        string targetExpression,
        in MarkupExtensionWriteContext context)
    {
        ref readonly var element = ref GetElement(plan, elementId);
        if (element.Akcss.Activators.IsEmpty)
        {
            return false;
        }

        var writer = new AkcssActivatorWriter(
            _writer,
            in _bindingEnvironment,
            _ownerTypeName,
            _sourceMap);
        writer.WriteSetStyles(
            plan.Akcss,
            element.Akcss.Activators,
            targetExpression,
            context);
        return true;
    }

    private bool WriteRefresh(
        in ComponentElementPlan element,
        string targetExpression)
    {
        if (element.Akcss.Activators.IsEmpty)
        {
            return false;
        }

        var writer = new AkcssActivatorWriter(
            _writer,
            in _bindingEnvironment,
            _ownerTypeName,
            _sourceMap);
        writer.WriteRefresh(
            element.Akcss.Activators,
            targetExpression);
        return true;
    }

    private void WriteElementCreation(
        in ComponentPlan plan,
        in ComponentScopePlan scope)
    {
        var writer = new ElementWriter(_writer, _sourceMap);

        for (var i = 0; i < scope.Elements.Length; i++)
        {
            var elementId = GetScopeElementId(plan, scope, i);
            writer.WriteCreation(plan.Elements.ItemRef(elementId));
        }
    }

    private void WriteBeginInit(
        in ComponentPlan plan,
        in ComponentScopePlan scope)
    {
        var writer = new ElementWriter(_writer, _sourceMap);

        for (var i = 0; i < scope.Elements.Length; i++)
        {
            var elementId = GetScopeElementId(plan, scope, i);
            writer.WriteBeginInit(plan.Elements.ItemRef(elementId));
        }
    }

    private void WriteEndInit(
        in ComponentPlan plan,
        in ComponentScopePlan scope)
    {
        var writer = new ElementWriter(_writer, _sourceMap);

        for (var i = scope.Elements.Length - 1; i >= 0; i--)
        {
            var elementId = GetScopeElementId(plan, scope, i);
            writer.WriteEndInit(plan.Elements.ItemRef(elementId));
        }
    }

    private bool WriteElementContent(
        in ComponentPlan plan,
        in ComponentElementPlan element,
        bool isFirstUpdate,
        in MarkupExtensionWriteContext context)
    {
        return element.Content.IsValid &&
            WriteContentTarget(plan, element.Content, isFirstUpdate, context);
    }

    private bool WritePropertyElements(
        in ComponentPlan plan,
        in ComponentElementPlan element,
        bool isFirstUpdate,
        in MarkupExtensionWriteContext context)
    {
        if (element.PropertyElements.IsEmpty)
        {
            return false;
        }

        var wroteAny = false;

        for (var i = 0; i < element.PropertyElements.Length; i++)
        {
            ref readonly var propertyElement = ref plan.PropertyElements.ItemRef(
                element.PropertyElements.Start + i);
            wroteAny |= WriteContentTarget(
                plan,
                propertyElement.Content,
                isFirstUpdate,
                context);
        }

        return wroteAny;
    }

    private bool WriteContentTarget(
        in ComponentPlan plan,
        in ComponentContentTargetReference target,
        bool isFirstUpdate,
        in MarkupExtensionWriteContext context)
    {
        switch (target.Kind)
        {
            case ComponentContentTargetKind.Property:
                Debug.Assert((uint)target.Index < (uint)plan.PropertyContents.Length);
                return WritePropertyContent(plan, target.Index, isFirstUpdate, context);

            case ComponentContentTargetKind.Collection:
                if (!isFirstUpdate)
                {
                    return false;
                }

                Debug.Assert((uint)target.Index < (uint)plan.CollectionContents.Length);
                var eagerWriter = new ComponentContentWriter(_writer, _sourceMap);
                return eagerWriter.WriteCollection(
                    plan,
                    plan.CollectionContents.ItemRef(target.Index));

            default:
                return false;
        }
    }

    private bool WritePropertyContent(
        in ComponentPlan plan,
        int contentIndex,
        bool isFirstUpdate,
        in MarkupExtensionWriteContext context)
    {
        ref readonly var content = ref plan.PropertyContents.ItemRef(contentIndex);
        var value = isFirstUpdate
            ? content.FirstUpdateValue
            : content.UpdateValue;

        switch (value.Kind)
        {
            case ComponentContentValueKind.Element:
            case ComponentContentValueKind.Constant:
            case ComponentContentValueKind.CSharpExpression:
            {
                var writer = new ComponentContentWriter(_writer, _sourceMap);
                return writer.WriteProperty(plan, content, isFirstUpdate);
            }

            case ComponentContentValueKind.DeferredContent:
            {
                Debug.Assert(isFirstUpdate);
                Debug.Assert((uint)value.Index < (uint)plan.DeferredContents.Length);

                if (!isFirstUpdate)
                {
                    return false;
                }

                var writer = new DeferredContentWriter(
                    _writer,
                    in _bindingEnvironment,
                    _sourceMap,
                    _ownerTypeName);
                return writer.WriteValue(
                    plan,
                    content,
                    plan.DeferredContents.ItemRef(value.Index),
                    context);
            }

            case ComponentContentValueKind.Template:
            {
                Debug.Assert(isFirstUpdate);
                Debug.Assert((uint)value.Index < (uint)plan.Templates.Length);

                if (!isFirstUpdate || (uint)value.Index >= (uint)plan.Templates.Length)
                {
                    return false;
                }

                var writer = new TemplateWriter(
                    _writer,
                    in _bindingEnvironment,
                    _sourceMap,
                    _ownerTypeName);
                return writer.WriteValue(
                    plan,
                    content,
                    plan.Templates.ItemRef(value.Index),
                    context);
            }

            default:
                return false;
        }
    }

    private static int GetScopeElementId(
        in ComponentPlan plan,
        in ComponentScopePlan scope,
        int index)
    {
        Debug.Assert((uint)index < (uint)scope.Elements.Length);

        var elementId = plan.ScopeElementIds[scope.Elements.Start + index];
        Debug.Assert((uint)elementId < (uint)plan.Elements.Length);
        Debug.Assert(plan.Elements[elementId].ScopeId == scope.Id);
        return elementId;
    }

    private static ref readonly ComponentElementPlan GetElement(
        in ComponentPlan plan,
        int elementId)
    {
        Debug.Assert((uint)elementId < (uint)plan.Elements.Length);
        ref readonly var element = ref plan.Elements.ItemRef(elementId);
        Debug.Assert(element.Id == elementId);
        return ref element;
    }

    private enum ComponentPropertyWriteFilter : byte
    {
        UpdateOnly,
        RuntimeUpdate,
    }
}
