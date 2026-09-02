using Akbura.Language.Binder;
using Akbura.Language.Operations;
using CSharpSymbolDefinition = Akbura.Language.Symbols.CSharpSymbolDefinition;
using Microsoft.CodeAnalysis;
using System;
using System.Diagnostics;

namespace Akbura.Language.CodeGeneration;

/// <summary>
/// Expressions required when a markup extension requests an IServiceProvider
/// or when a binding path uses ElementName.
/// </summary>
internal readonly ref struct MarkupExtensionWriteContext
{
    public MarkupExtensionWriteContext(
        string targetObjectExpression,
        MarkupTargetPropertyPlan targetProperty,
        string intermediateRootExpression,
        string baseUriExpression,
        string directParentsStackExpression,
        string? fallbackServiceProviderExpression,
        string? nameScopeExpression,
        int scopeId,
        ReadOnlySpan<BindingElementReference> elementReferences = default)
    {
        TargetObjectExpression = targetObjectExpression;
        TargetProperty = targetProperty;
        IntermediateRootExpression = intermediateRootExpression;
        BaseUriExpression = baseUriExpression;
        DirectParentsStackExpression = directParentsStackExpression;
        FallbackServiceProviderExpression = fallbackServiceProviderExpression;
        NameScopeExpression = nameScopeExpression;
        ScopeId = scopeId;
        ElementReferences = elementReferences;
    }

    public string TargetObjectExpression { get; }

    public MarkupTargetPropertyPlan TargetProperty { get; }

    public string IntermediateRootExpression { get; }

    public string BaseUriExpression { get; }

    public string DirectParentsStackExpression { get; }

    public string? FallbackServiceProviderExpression { get; }

    public string? NameScopeExpression { get; }

    public int ScopeId { get; }

    public ReadOnlySpan<BindingElementReference> ElementReferences { get; }

    internal MarkupExtensionWriteContext WithTarget(
        string targetObjectExpression,
        MarkupTargetPropertyPlan targetProperty)
    {
        return WithTarget(
            targetObjectExpression,
            targetProperty,
            ScopeId,
            ElementReferences);
    }

    internal MarkupExtensionWriteContext WithTarget(
        string targetObjectExpression,
        MarkupTargetPropertyPlan targetProperty,
        int scopeId,
        ReadOnlySpan<BindingElementReference> elementReferences)
    {
        return new MarkupExtensionWriteContext(
            targetObjectExpression,
            targetProperty,
            IntermediateRootExpression,
            BaseUriExpression,
            DirectParentsStackExpression,
            FallbackServiceProviderExpression,
            NameScopeExpression,
            scopeId,
            elementReferences);
    }

    internal MarkupExtensionWriteContext WithElementReferences(
        ReadOnlySpan<BindingElementReference> elementReferences)
    {
        if (elementReferences.IsEmpty)
        {
            return this;
        }

        return new MarkupExtensionWriteContext(
            TargetObjectExpression,
            TargetProperty,
            IntermediateRootExpression,
            BaseUriExpression,
            DirectParentsStackExpression,
            FallbackServiceProviderExpression,
            NameScopeExpression,
            ScopeId,
            elementReferences);
    }
}

/// <summary>
/// Writes markup-extension expressions directly to CodeWriter.
/// </summary>
internal readonly ref struct MarkupExtensionWriter
{
    private readonly CodeWriter _writer;
    private readonly CSharpValueWriter _valueWriter;
    private readonly BindingWriterEnvironment _environment;

    public MarkupExtensionWriter(
        CodeWriter writer,
        in BindingWriterEnvironment environment)
    {
        Debug.Assert(writer != null);

        _writer = writer!;
        _valueWriter = new CSharpValueWriter(_writer);
        _environment = environment;
    }

    /// <summary>
    /// Writes a complete markup-extension expression.
    /// Bindings without a long-lived plan are written inline.
    /// </summary>
    public void Write(
        MarkupExtensionValue extension,
        in MarkupExtensionWriteContext context)
    {
        if (extension.Binding != null)
        {
            var inlinePlan = BindingWritePlan.CreateInline(
                in _environment,
                extension,
                context.ScopeId,
                context.NameScopeExpression,
                context.ElementReferences);

            WriteBinding(inlinePlan, context);
            return;
        }

        if (extension.ProvideValueMethod.Symbol is not IMethodSymbol provideValue)
        {
            WriteCreation(extension, context);
            return;
        }

        _writer.Write("(");
        WriteCreation(extension, context);
        _writer.Write(").");
        WriteProvideValueInvocationCore(provideValue, context);
    }

    /// <summary>
    /// Writes a binding from its existing plan so cached paths are retained.
    /// </summary>
    public void WriteBinding(
        in BindingWritePlan plan,
        in MarkupExtensionWriteContext context)
    {
        var bindingWriter = new BindingWriter(_writer, in _environment, context.ElementReferences);

        bindingWriter.WriteBinding(plan, context);
    }

    /// <summary>
    /// Writes the cached path field assigned to an existing binding plan.
    /// </summary>
    public void WriteCachedBindingPath(in BindingWritePlan plan)
    {
        var bindingWriter = new BindingWriter(_writer, in _environment);
        bindingWriter.WriteCachedPathField(plan);
    }

    /// <summary>
    /// Writes only construction and initialization of a markup extension.
    /// </summary>
    public void WriteCreation(
        MarkupExtensionValue extension,
        in MarkupExtensionWriteContext context)
    {
        var extensionType = extension.ExtensionType.Symbol as ITypeSymbol;

        Debug.Assert(extensionType != null);

        _writer.Write("new ");
        _valueWriter.WriteTypeName(extensionType);
        _writer.Write("(");

        for (var i = 0; i < extension.Arguments.Length; i++)
        {
            if (i > 0)
            {
                _writer.Write(", ");
            }

            WriteArgumentValue(extension.Arguments[i], context);
        }

        _writer.Write(")");

        if (extension.Properties.IsDefaultOrEmpty)
        {
            return;
        }

        _writer.Write(" { ");

        for (var i = 0; i < extension.Properties.Length; i++)
        {
            if (i > 0)
            {
                _writer.Write(", ");
            }

            var property = extension.Properties[i];

            _valueWriter.WriteIdentifier(property.Name);
            _writer.Write(" = ");
            WritePropertyValue(property, context);
        }

        _writer.Write(" }");
    }

    /// <summary>
    /// Writes a ProvideValue invocation for an existing extension instance.
    /// </summary>
    public void WriteProvideValueInvocation(
        MarkupExtensionValue extension,
        string instanceExpression,
        in MarkupExtensionWriteContext context)
    {
        Debug.Assert(!string.IsNullOrEmpty(instanceExpression));

        _writer.Write(instanceExpression);

        if (extension.ProvideValueMethod.Symbol is not IMethodSymbol provideValue)
        {
            Debug.Fail("The markup extension has no ProvideValue method.");
            return;
        }

        _writer.Write(".");
        WriteProvideValueInvocationCore(provideValue, context);
    }

    internal void WritePropertyValue(
        in MarkupExtensionPropertyValue property,
        in MarkupExtensionWriteContext context)
    {
        WriteBoundValue(
            property.Operation,
            property.ConvertedValue,
            property.Value,
            property.Type.Symbol,
            property.NestedValue,
            context);
    }

    private void WriteArgumentValue(
        in MarkupExtensionArgumentValue argument,
        in MarkupExtensionWriteContext context)
    {
        WriteBoundValue(
            argument.Operation,
            argument.ConvertedValue,
            argument.Text,
            argument.Type.Symbol,
            argument.NestedValue,
            context);
    }

    private void WriteBoundValue(
        CSharpOperationDefinition operation,
        object? convertedValue,
        string text,
        ISymbol? targetType,
        MarkupExtensionValue? nestedValue,
        in MarkupExtensionWriteContext context)
    {
        if (nestedValue != null)
        {
            Write(nestedValue, context);
            return;
        }

        var constant = operation.ConstantValue;

        if (constant.HasValue)
        {
            _valueWriter.WriteConstant(constant.Value, targetType);
            return;
        }

        if (!operation.IsDefault && operation.Syntax != null)
        {
            _writer.Write(operation.Syntax.ToString());
            return;
        }

        if (convertedValue is CSharpSymbolDefinition definition &&
            _valueWriter.TryWriteStaticMemberReference(definition.Symbol))
        {
            return;
        }

        if (convertedValue != null)
        {
            _valueWriter.WriteConstant(convertedValue, targetType);
            return;
        }

        _writer.WriteStringLiteral(TrimQuotes(text));
    }

    private void WriteProvideValueInvocationCore(
        IMethodSymbol provideValue,
        in MarkupExtensionWriteContext context)
    {
        _valueWriter.WriteIdentifier(provideValue.Name);
        _writer.Write("(");

        Debug.Assert(provideValue.Parameters.Length <= 1);

        if (provideValue.Parameters.Length == 1)
        {
            WriteMarkupServiceProvider(context);
        }

        _writer.Write(")");
    }

    private void WriteMarkupServiceProvider(
        in MarkupExtensionWriteContext context)
    {
        if (string.IsNullOrEmpty(context.TargetObjectExpression) ||
            string.IsNullOrEmpty(context.IntermediateRootExpression) ||
            string.IsNullOrEmpty(context.BaseUriExpression) ||
            string.IsNullOrEmpty(context.DirectParentsStackExpression))
        {
            Debug.Fail("The markup extension service-provider context is incomplete.");
            _writer.Write("default!");
            return;
        }

        _writer
            .Write("CreateMarkupServiceProvider(targetObject: ")
            .Write(context.TargetObjectExpression)
            .Write(", targetProperty: ");
        var targetPropertyWriter = new MarkupTargetPropertyWriter(_writer);
        targetPropertyWriter.Write(context.TargetProperty);
        _writer.Write(", intermediateRootObject: ")
            .Write(context.IntermediateRootExpression)
            .Write(", baseUri: ")
            .Write(context.BaseUriExpression)
            .Write(", directParentsStack: ")
            .Write(context.DirectParentsStackExpression);

        if (!string.IsNullOrEmpty(context.FallbackServiceProviderExpression))
        {
            _writer
                .Write(", fallbackServiceProvider: ")
                .Write(context.FallbackServiceProviderExpression!);
        }

        _writer.Write(")");
    }

    private static ReadOnlyMemory<char> TrimQuotes(string text)
    {
        var value = text.AsMemory();

        if (value.Length < 2)
        {
            return value;
        }

        var span = value.Span;

        if ((span[0] == '"' && span[^1] == '"') ||
            (span[0] == '\'' && span[^1] == '\''))
        {
            return value[1..^1];
        }

        return value;
    }
}
