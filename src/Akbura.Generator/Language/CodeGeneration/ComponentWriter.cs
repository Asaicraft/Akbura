using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using CSharpSyntaxFacts = Microsoft.CodeAnalysis.CSharp.SyntaxFacts;
using CSharpSyntaxKind = Microsoft.CodeAnalysis.CSharp.SyntaxKind;

namespace Akbura.Language.CodeGeneration;

/// <summary>
/// Owns the generation state and immutable generation plan for one component.
/// </summary>
internal sealed class ComponentWriter
{
    private readonly CodeWriter _writer;
    private readonly ComponentPlan _plan;
    private readonly BindingWriterEnvironment _bindingEnvironment;
    private readonly ComponentGenerationSourceMap _sourceMap;
    private readonly string _ownerTypeName;

    public ComponentWriter(
        CodeWriter writer,
        IAkburaComponentSymbol component,
        AkburaSemanticModel semanticModel,
        IReadOnlyDictionary<AkburaSyntax, string> akcssModuleTypeNames)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));

        if (component == null)
        {
            throw new ArgumentNullException(nameof(component));
        }

        if (semanticModel == null)
        {
            throw new ArgumentNullException(nameof(semanticModel));
        }

        if (akcssModuleTypeNames == null)
        {
            throw new ArgumentNullException(nameof(akcssModuleTypeNames));
        }

        if (component.SyntaxTree is not ComponentSyntaxTree syntaxTree)
        {
            throw new ArgumentException(
                "The component must be declared in a component syntax tree.",
                nameof(component));
        }

        _bindingEnvironment = BindingWriterEnvironment.Create(semanticModel, component);
        var resultEnvironment = MarkupExtensionResultEnvironment.Create(semanticModel);
        Debug.Assert(resultEnvironment.IsValid);
        _sourceMap = new ComponentGenerationSourceMap(syntaxTree);
        _ownerTypeName = GetGeneratedOwnerTypeName(component);
        _plan = ComponentPlanner.Create(
            component,
            semanticModel,
            akcssModuleTypeNames,
            in resultEnvironment);
    }

    public ref readonly ComponentPlan Plan
    {
        get => ref _plan;
    }

    public ImmutableArray<ComponentElementPlan> Elements => _plan.Elements;

    public bool HasAkcss => !_plan.Akcss.IsEmpty;

    public bool WriteElementFields()
    {
        var writer = new ElementWriter(_writer, _sourceMap);
        var wroteAny = false;

        for (var i = 0; i < _plan.Elements.Length; i++)
        {
            ref readonly var element = ref _plan.Elements.ItemRef(i);
            if (element.IsLocal)
            {
                continue;
            }

            writer.WriteField(element);
            wroteAny = true;
        }

        return wroteAny;
    }

    public void WriteElementCreation(int elementId)
    {
        ref readonly var element = ref GetElement(elementId);
        var writer = new ElementWriter(_writer, _sourceMap);
        writer.WriteCreation(element);
    }

    public void WriteBeginInit(int elementId)
    {
        ref readonly var element = ref GetElement(elementId);
        var writer = new ElementWriter(_writer, _sourceMap);
        writer.WriteBeginInit(element);
    }

    public void WriteEndInit(int elementId)
    {
        ref readonly var element = ref GetElement(elementId);
        var writer = new ElementWriter(_writer, _sourceMap);
        writer.WriteEndInit(element);
    }

    public bool WriteFirstUpdateActions(
        int elementId,
        in MarkupExtensionWriteContext context)
    {
        ref readonly var element = ref GetElement(elementId);
        if (element.FirstUpdateActions.IsEmpty)
        {
            return false;
        }

        var indent = _writer.CurrentIndent;
        try
        {
            var targetExpression = EscapeIdentifier(element.Identifier);
            var targetProperty = context.TargetProperty;
            var propertyContext = context.WithTarget(
                targetExpression,
                targetProperty,
                element.ScopeId,
                _plan.ElementReferences.AsSpan());
            var propertyWriter = new ComponentPropertyWriter(
                _writer,
                in _bindingEnvironment,
                _sourceMap);
            var subscriptionWriter = new PropertySubscriptionWriter(_writer, _sourceMap);
            var actionWriter = new ComponentFirstUpdateActionWriter(_writer, _sourceMap);

            for (var i = 0; i < element.FirstUpdateActions.Length; i++)
            {
                ref readonly var action = ref _plan.FirstUpdateActions.ItemRef(
                    element.FirstUpdateActions.Start + i);

                switch (action.Kind)
                {
                    case ComponentFirstUpdateActionKind.NameAssignment:
                    {
                        Debug.Assert((uint)action.Index < (uint)_plan.NameAssignments.Length);
                        ref readonly var name = ref _plan.NameAssignments.ItemRef(action.Index);
                        actionWriter.WriteNameAssignment(name, targetExpression);
                        break;
                    }
                    case ComponentFirstUpdateActionKind.PropertyWrite:
                    {
                        Debug.Assert((uint)action.Index < (uint)_plan.PropertyWrites.Length);
                        ref readonly var property = ref _plan.PropertyWrites.ItemRef(action.Index);
                        propertyWriter.Write(_plan, property, targetExpression, propertyContext);
                        break;
                    }
                    case ComponentFirstUpdateActionKind.PropertySubscription:
                    {
                        Debug.Assert((uint)action.Index < (uint)_plan.PropertySubscriptions.Length);
                        ref readonly var subscription = ref _plan.PropertySubscriptions.ItemRef(action.Index);
                        subscriptionWriter.WriteRegistration(element, subscription);
                        break;
                    }
                    case ComponentFirstUpdateActionKind.RoutedEvent:
                    {
                        Debug.Assert((uint)action.Index < (uint)_plan.RoutedEvents.Length);
                        ref readonly var routedEvent = ref _plan.RoutedEvents.ItemRef(action.Index);
                        actionWriter.WriteRoutedEvent(routedEvent, targetExpression);
                        break;
                    }
                    case ComponentFirstUpdateActionKind.CommandBinding:
                    {
                        Debug.Assert((uint)action.Index < (uint)_plan.CommandBindings.Length);
                        ref readonly var command = ref _plan.CommandBindings.ItemRef(action.Index);
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
        finally
        {
            _writer.CurrentIndent = indent;
        }
    }

    public bool WriteUpdateProperties(
        int elementId,
        in MarkupExtensionWriteContext context)
    {
        return WriteProperties(elementId, isFirstUpdate: false, context);
    }

    public bool WriteFirstUpdateContent(int elementId)
    {
        return WriteElementContent(elementId, isFirstUpdate: true);
    }

    public bool WriteUpdateContent(int elementId)
    {
        ref readonly var element = ref GetElement(elementId);
        if (!element.Content.IsValid && element.PropertyElements.IsEmpty)
        {
            return false;
        }

        var indent = _writer.CurrentIndent;
        try
        {
            var writer = new ComponentContentWriter(_writer, _sourceMap);
            var wroteAny = WriteContentTarget(
                writer,
                element.Content,
                isFirstUpdate: false);

            for (var i = 0; i < element.PropertyElements.Length; i++)
            {
                ref readonly var propertyElement = ref _plan.PropertyElements.ItemRef(
                    element.PropertyElements.Start + i);
                wroteAny |= WriteContentTarget(
                    writer,
                    propertyElement.Content,
                    isFirstUpdate: false);
            }

            return wroteAny;
        }
        finally
        {
            _writer.CurrentIndent = indent;
        }
    }

    public bool WritePropertyElements(int elementId)
    {
        ref readonly var element = ref GetElement(elementId);
        if (element.PropertyElements.IsEmpty)
        {
            return false;
        }

        var indent = _writer.CurrentIndent;
        try
        {
            var writer = new ComponentContentWriter(_writer, _sourceMap);
            var wroteAny = false;

            for (var i = 0; i < element.PropertyElements.Length; i++)
            {
                ref readonly var propertyElement = ref _plan.PropertyElements.ItemRef(
                    element.PropertyElements.Start + i);
                wroteAny |= WriteContentTarget(
                    writer,
                    propertyElement.Content,
                    isFirstUpdate: true);
            }

            return wroteAny;
        }
        finally
        {
            _writer.CurrentIndent = indent;
        }
    }

    public bool WritePropertySubscriptionHandlers()
    {
        if (_plan.PropertySubscriptions.IsDefaultOrEmpty)
        {
            return false;
        }

        var indent = _writer.CurrentIndent;
        try
        {
            var writer = new PropertySubscriptionWriter(_writer, _sourceMap);
            var wroteAny = false;

            for (var i = 0; i < _plan.PropertySubscriptions.Length; i++)
            {
                ref readonly var subscription = ref _plan.PropertySubscriptions.ItemRef(i);
                ref readonly var element = ref GetElement(subscription.ElementId);

                if (element.IsLocal)
                {
                    continue;
                }

                if (wroteAny)
                {
                    _writer.WriteLine();
                }

                writer.WriteHandler(subscription);
                wroteAny = true;
            }

            return wroteAny;
        }
        finally
        {
            _writer.CurrentIndent = indent;
        }
    }

    public bool WriteStaticMembers()
    {
        var indent = _writer.CurrentIndent;
        try
        {
            var wroteAny = WriteCachedBindingPaths();
            if (!_plan.Akcss.IsEmpty)
            {
                if (wroteAny)
                {
                    _writer.WriteLine();
                }

                var writer = new AkcssActivatorWriter(
                    _writer,
                    in _bindingEnvironment,
                    _ownerTypeName,
                    _sourceMap);
                writer.WriteStaticMembers(_plan.Akcss);
                wroteAny = true;
            }

            return wroteAny;
        }
        finally
        {
            _writer.CurrentIndent = indent;
        }
    }

    private bool WriteCachedBindingPaths()
    {
        var writer = new MarkupExtensionWriter(_writer, in _bindingEnvironment);
        var wroteAny = false;

        for (var i = 0; i < _plan.Bindings.Length; i++)
        {
            ref readonly var binding = ref _plan.Bindings.ItemRef(i);
            if (!binding.HasCachedPath)
            {
                continue;
            }

            if (wroteAny)
            {
                _writer.WriteLine();
            }

            writer.WriteCachedBindingPath(binding);
            wroteAny = true;
        }

        return wroteAny;
    }

    public bool WriteFactoryMethods(
        int elementId,
        in MarkupExtensionWriteContext context)
    {
        ref readonly var element = ref GetElement(elementId);
        if (element.Akcss.MarkupExtensionSlots.IsEmpty)
        {
            return false;
        }

        var indent = _writer.CurrentIndent;
        try
        {
            var writer = new AkcssActivatorWriter(
                _writer,
                in _bindingEnvironment,
                _ownerTypeName,
                _sourceMap);
            return writer.WriteFactoryMethods(_plan.Akcss, element.Akcss, context);
        }
        finally
        {
            _writer.CurrentIndent = indent;
        }
    }

    public bool WriteSetStyles(
        int elementId,
        string targetExpression,
        in MarkupExtensionWriteContext context)
    {
        ref readonly var element = ref GetElement(elementId);
        if (element.Akcss.Activators.IsEmpty)
        {
            return false;
        }

        var indent = _writer.CurrentIndent;
        try
        {
            var writer = new AkcssActivatorWriter(
                _writer,
                in _bindingEnvironment,
                _ownerTypeName,
                _sourceMap);
            writer.WriteSetStyles(
                _plan.Akcss,
                element.Akcss.Activators,
                targetExpression,
                context);
            return true;
        }
        finally
        {
            _writer.CurrentIndent = indent;
        }
    }

    public bool WriteRefresh(
        int elementId,
        string targetExpression)
    {
        ref readonly var element = ref GetElement(elementId);
        if (element.Akcss.Activators.IsEmpty)
        {
            return false;
        }

        var indent = _writer.CurrentIndent;
        try
        {
            var writer = new AkcssActivatorWriter(
                _writer,
                in _bindingEnvironment,
                _ownerTypeName,
                _sourceMap);
            writer.WriteRefresh(element.Akcss.Activators, targetExpression);
            return true;
        }
        finally
        {
            _writer.CurrentIndent = indent;
        }
    }

    private bool WriteProperties(
        int elementId,
        bool isFirstUpdate,
        in MarkupExtensionWriteContext context)
    {
        ref readonly var element = ref GetElement(elementId);
        if (element.PropertyWrites.IsEmpty)
        {
            return false;
        }

        var indent = _writer.CurrentIndent;
        try
        {
            var targetExpression = EscapeIdentifier(element.Identifier);
            var targetProperty = context.TargetProperty;
            var propertyContext = context.WithTarget(
                targetExpression,
                targetProperty,
                element.ScopeId,
                _plan.ElementReferences.AsSpan());
            var writer = new ComponentPropertyWriter(
                _writer,
                in _bindingEnvironment,
                _sourceMap);
            var wroteAny = false;

            for (var i = 0; i < element.PropertyWrites.Length; i++)
            {
                ref readonly var property = ref _plan.PropertyWrites.ItemRef(element.PropertyWrites.Start + i);
                if (property.IsFirstUpdate != isFirstUpdate)
                {
                    continue;
                }

                writer.Write(_plan, property, targetExpression, propertyContext);
                wroteAny = true;
            }

            return wroteAny;
        }
        finally
        {
            _writer.CurrentIndent = indent;
        }
    }

    private bool WriteElementContent(int elementId, bool isFirstUpdate)
    {
        ref readonly var element = ref GetElement(elementId);
        if (!element.Content.IsValid)
        {
            return false;
        }

        var indent = _writer.CurrentIndent;
        try
        {
            var writer = new ComponentContentWriter(_writer, _sourceMap);
            return WriteContentTarget(writer, element.Content, isFirstUpdate);
        }
        finally
        {
            _writer.CurrentIndent = indent;
        }
    }

    private bool WriteContentTarget(
        ComponentContentWriter writer,
        in ComponentContentTargetReference target,
        bool isFirstUpdate)
    {
        switch (target.Kind)
        {
            case ComponentContentTargetKind.Property:
                Debug.Assert((uint)target.Index < (uint)_plan.PropertyContents.Length);
                return writer.WriteProperty(
                    _plan,
                    _plan.PropertyContents.ItemRef(target.Index),
                    isFirstUpdate);

            case ComponentContentTargetKind.Collection:
                if (!isFirstUpdate)
                {
                    return false;
                }

                Debug.Assert((uint)target.Index < (uint)_plan.CollectionContents.Length);
                return writer.WriteCollection(
                    _plan,
                    _plan.CollectionContents.ItemRef(target.Index));

            default:
                return false;
        }
    }

    private ref readonly ComponentElementPlan GetElement(int elementId)
    {
        if ((uint)elementId >= (uint)_plan.Elements.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(elementId));
        }

        ref readonly var element = ref _plan.Elements.ItemRef(elementId);
        Debug.Assert(element.Id == elementId);
        return ref element;
    }

    private static string GetGeneratedOwnerTypeName(IAkburaComponentSymbol component)
    {
        var typeName = EscapeIdentifier(component.Name);
        return string.IsNullOrEmpty(component.NamespaceName)
            ? "global::" + typeName
            : "global::" + component.NamespaceName + "." + typeName;
    }

    private static string EscapeIdentifier(string identifier)
    {
        return CSharpSyntaxFacts.GetKeywordKind(identifier) != CSharpSyntaxKind.None ||
            CSharpSyntaxFacts.GetContextualKeywordKind(identifier) != CSharpSyntaxKind.None
                ? "@" + identifier
                : identifier;
    }
}
