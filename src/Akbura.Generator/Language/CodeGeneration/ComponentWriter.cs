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
/// Owns the generation state and the immutable AKCSS plan for one component.
/// </summary>
internal sealed class ComponentWriter
{
    private readonly CodeWriter _writer;
    private readonly ComponentPlan _plan;
    private readonly BindingWriterEnvironment _bindingEnvironment;
    private readonly MarkupExtensionResultEnvironment _resultEnvironment;
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
        _resultEnvironment = MarkupExtensionResultEnvironment.Create(semanticModel);
        Debug.Assert(_resultEnvironment.IsValid);
        _sourceMap = new ComponentGenerationSourceMap(syntaxTree);
        _ownerTypeName = GetGeneratedOwnerTypeName(component);
        _plan = ComponentPlanner.Create(
            component,
            semanticModel,
            akcssModuleTypeNames,
            in _resultEnvironment);
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

    public bool WriteFirstUpdateProperties(
        int elementId,
        in MarkupExtensionWriteContext context)
    {
        return WriteProperties(elementId, isFirstUpdate: true, context);
    }

    public bool WriteUpdateProperties(
        int elementId,
        in MarkupExtensionWriteContext context)
    {
        return WriteProperties(elementId, isFirstUpdate: false, context);
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
            var propertyContext = context.WithTarget(
                targetExpression,
                context.TargetPropertyExpression,
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
