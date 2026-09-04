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
internal sealed class ComponentWriter : IDisposable
{
    private readonly CodeWriter _writer;
    private readonly ComponentPlan _plan;
    private readonly ComponentMemberPlan _memberPlan;
    private readonly BindingWriterEnvironment _bindingEnvironment;
    private readonly ComponentGenerationSourceMap _sourceMap;
    private readonly string _ownerTypeName;
    private readonly string _resourcePath;
    private bool _disposed;

    public ComponentWriter(
        CodeWriter writer,
        IAkburaComponentSymbol component,
        AkburaSemanticModel semanticModel,
        string resourcePath,
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

        if (string.IsNullOrWhiteSpace(resourcePath))
        {
            throw new ArgumentException(
                "The component resource path cannot be empty.",
                nameof(resourcePath));
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
        _resourcePath = NormalizeResourcePath(resourcePath);
        if (_resourcePath.Length == 0)
        {
            throw new ArgumentException(
                "The component resource path must contain a file name.",
                nameof(resourcePath));
        }

        _plan = ComponentPlanner.Create(
            component,
            semanticModel,
            akcssModuleTypeNames,
            in resultEnvironment);
        _memberPlan = ComponentMemberPlanner.Create(
            component,
            semanticModel);
    }

    public ref readonly ComponentPlan Plan
    {
        get => ref _plan;
    }

    public ref readonly ComponentMemberPlan MemberPlan
    {
        get => ref _memberPlan;
    }

    public ImmutableArray<ComponentElementPlan> Elements => _plan.Elements;

    public bool HasAkcss => !_plan.Akcss.IsEmpty;

    public bool WriteComponentMembers()
    {
        var indent = _writer.CurrentIndent;

        try
        {
            var writer = new ComponentMemberWriter(
                _writer,
                _sourceMap,
                _ownerTypeName);
            return writer.WriteDeclarations(_memberPlan);
        }
        finally
        {
            _writer.CurrentIndent = indent;
        }
    }

    public void WriteDescriptorMembers()
    {
        var indent = _writer.CurrentIndent;

        try
        {
            var writer = new ComponentMemberWriter(
                _writer,
                _sourceMap,
                _ownerTypeName);
            writer.WriteDescriptors(_memberPlan);
        }
        finally
        {
            _writer.CurrentIndent = indent;
        }
    }

    public bool WriteLifecycleFields()
    {
        var indent = _writer.CurrentIndent;

        try
        {
            var writer = CreateLifecycleWriter();
            return writer.WriteSupportFields(_plan);
        }
        finally
        {
            _writer.CurrentIndent = indent;
        }
    }

    public void WriteLifecycleMembers()
    {
        var indent = _writer.CurrentIndent;

        try
        {
            var writer = CreateLifecycleWriter();
            writer.WriteMembers(_plan);
        }
        finally
        {
            _writer.CurrentIndent = indent;
        }
    }

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
        var indent = _writer.CurrentIndent;

        try
        {
            var writer = CreateScopeWriter();
            return writer.WriteFirstUpdateActions(_plan, elementId, context);
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
        var indent = _writer.CurrentIndent;

        try
        {
            var writer = CreateScopeWriter();
            return writer.WriteUpdateProperties(_plan, elementId, context);
        }
        finally
        {
            _writer.CurrentIndent = indent;
        }
    }

    internal bool WriteFirstUpdateContent(int elementId)
    {
        return WriteFirstUpdateContent(elementId, default);
    }

    public bool WriteFirstUpdateContent(
        int elementId,
        in MarkupExtensionWriteContext context)
    {
        var indent = _writer.CurrentIndent;

        try
        {
            var writer = CreateScopeWriter();
            return writer.WriteFirstUpdateContent(_plan, elementId, context);
        }
        finally
        {
            _writer.CurrentIndent = indent;
        }
    }

    internal bool WriteUpdateContent(int elementId)
    {
        return WriteUpdateContent(elementId, default);
    }

    public bool WriteUpdateContent(
        int elementId,
        in MarkupExtensionWriteContext context)
    {
        var indent = _writer.CurrentIndent;

        try
        {
            var writer = CreateScopeWriter();
            return writer.WriteUpdateContent(_plan, elementId, context);
        }
        finally
        {
            _writer.CurrentIndent = indent;
        }
    }

    internal bool WritePropertyElements(int elementId)
    {
        return WritePropertyElements(elementId, default);
    }

    public bool WritePropertyElements(
        int elementId,
        in MarkupExtensionWriteContext context)
    {
        var indent = _writer.CurrentIndent;

        try
        {
            var writer = CreateScopeWriter();
            return writer.WritePropertyElements(_plan, elementId, context);
        }
        finally
        {
            _writer.CurrentIndent = indent;
        }
    }

    public bool WriteDeferredContentBuilders()
    {
        if (_plan.DeferredContents.IsDefaultOrEmpty)
        {
            return false;
        }

        var indent = _writer.CurrentIndent;

        try
        {
            var writer = new DeferredContentWriter(
                _writer,
                in _bindingEnvironment,
                _sourceMap,
                _ownerTypeName);
            var wroteAny = false;

            for (var i = 0; i < _plan.DeferredContents.Length; i++)
            {
                ref readonly var deferred = ref _plan.DeferredContents.ItemRef(i);
                if (!DeferredContentWriter.CanWriteBuilder(_plan, deferred))
                {
                    continue;
                }

                if (wroteAny)
                {
                    _writer.WriteLine();
                }

                var wroteBuilder = writer.WriteBuilder(_plan, deferred);
                Debug.Assert(wroteBuilder);
                wroteAny |= wroteBuilder;
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

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _memberPlan.ReturnToPool();
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
        var indent = _writer.CurrentIndent;

        try
        {
            var writer = CreateScopeWriter();
            return writer.WriteSetStyles(
                _plan,
                elementId,
                targetExpression,
                context);
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

    private ComponentScopeWriter CreateScopeWriter()
    {
        return new ComponentScopeWriter(
            _writer,
            in _bindingEnvironment,
            _sourceMap,
            _ownerTypeName);
    }

    private ComponentLifecycleWriter CreateLifecycleWriter()
    {
        return new ComponentLifecycleWriter(
            _writer,
            in _bindingEnvironment,
            _sourceMap,
            _ownerTypeName,
            _resourcePath);
    }

    private static string NormalizeResourcePath(string resourcePath)
    {
        return resourcePath.Replace('\\', '/').TrimStart('/');
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
