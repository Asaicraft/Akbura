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
    private readonly BindingWriterEnvironment _bindingEnvironment;
    private readonly ComponentGenerationSourceMap _sourceMap;
    private readonly string _ownerTypeName;
    private readonly AkcssComponentActivatorPlan _akcssPlan;

    public ComponentWriter(
        CodeWriter writer,
        IAkburaComponentSymbol component,
        AkburaSemanticModel semanticModel,
        ReadOnlySpan<AkcssActivatorElementInput> elements,
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

        for (var i = 0; i < elements.Length; i++)
        {
            if (elements[i].Id != i)
            {
                throw new ArgumentException(
                    "Component element identifiers must be dense and ordered.",
                    nameof(elements));
            }
        }

        _bindingEnvironment = BindingWriterEnvironment.Create(semanticModel, component);
        _sourceMap = new ComponentGenerationSourceMap(syntaxTree);
        _ownerTypeName = GetGeneratedOwnerTypeName(component);
        _akcssPlan = AkcssActivatorPlanner.Create(
            semanticModel,
            elements,
            akcssModuleTypeNames);
    }

    public ref readonly AkcssComponentActivatorPlan AkcssPlan
    {
        get => ref _akcssPlan;
    }

    public ImmutableArray<AkcssElementActivatorPlan> Elements => _akcssPlan.Elements;

    public bool HasAkcss => !_akcssPlan.IsEmpty;

    public bool WriteStaticMembers()
    {
        if (_akcssPlan.IsEmpty)
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
            writer.WriteStaticMembers(_akcssPlan);
            return true;
        }
        finally
        {
            _writer.CurrentIndent = indent;
        }
    }

    public bool WriteFactoryMethods(
        int elementId,
        in MarkupExtensionWriteContext context)
    {
        var element = GetElement(elementId);
        if (element.MarkupExtensionSlots.IsEmpty)
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
            return writer.WriteFactoryMethods(_akcssPlan, element, context);
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
        var element = GetElement(elementId);
        if (element.Activators.IsEmpty)
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
                _akcssPlan,
                element.Activators,
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
        var element = GetElement(elementId);
        if (element.Activators.IsEmpty)
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
            writer.WriteRefresh(element.Activators, targetExpression);
            return true;
        }
        finally
        {
            _writer.CurrentIndent = indent;
        }
    }

    private AkcssElementActivatorPlan GetElement(int elementId)
    {
        if ((uint)elementId >= (uint)_akcssPlan.Elements.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(elementId));
        }

        var element = _akcssPlan.Elements[elementId];
        Debug.Assert(element.ElementId == elementId);
        return element;
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
