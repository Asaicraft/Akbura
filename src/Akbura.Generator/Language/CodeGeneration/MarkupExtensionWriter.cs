using Akbura.Language.Binder;
using Akbura.Language.Operations;
using CSharpSymbolDefinition = Akbura.Language.Symbols.CSharpSymbolDefinition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Diagnostics;
using System.Globalization;

namespace Akbura.Language.CodeGeneration;

/// <summary>
/// Expressions required when a markup extension requests
/// an IServiceProvider or when a binding path uses ElementName.
/// </summary>
internal readonly ref struct MarkupExtensionWriteContext
{
    public MarkupExtensionWriteContext(
        string targetObjectExpression,
        string targetPropertyExpression,
        string intermediateRootExpression,
        string baseUriExpression,
        string directParentsStackExpression,
        string? fallbackServiceProviderExpression,
        string? nameScopeExpression,
        int scopeId,
        ReadOnlySpan<BindingElementReference> elementReferences = default)
    {
        TargetObjectExpression = targetObjectExpression;
        TargetPropertyExpression = targetPropertyExpression;
        IntermediateRootExpression = intermediateRootExpression;
        BaseUriExpression = baseUriExpression;
        DirectParentsStackExpression = directParentsStackExpression;
        FallbackServiceProviderExpression = fallbackServiceProviderExpression;
        NameScopeExpression = nameScopeExpression;
        ScopeId = scopeId;
        ElementReferences = elementReferences;
    }

    public string TargetObjectExpression { get; }

    public string TargetPropertyExpression { get; }

    public string IntermediateRootExpression { get; }

    public string BaseUriExpression { get; }

    public string DirectParentsStackExpression { get; }

    public string? FallbackServiceProviderExpression { get; }

    public string? NameScopeExpression { get; }

    public int ScopeId { get; }

    public ReadOnlySpan<BindingElementReference> ElementReferences { get; }

    internal MarkupExtensionWriteContext WithElementReferences(ReadOnlySpan<BindingElementReference> elementReferences)
    {
        if (elementReferences.IsEmpty)
        {
            return this;
        }

        return new MarkupExtensionWriteContext(
            TargetObjectExpression,
            TargetPropertyExpression,
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
/// Allocation-free writer facade for markup-extension code generation.
/// </summary>
internal readonly ref struct MarkupExtensionWriter
{
    private static readonly SymbolDisplayFormat s_typeDisplayFormat = SymbolDisplayFormat.FullyQualifiedFormat;

    private readonly CodeWriter _writer;
    private readonly BindingWriterEnvironment _environment;

    public MarkupExtensionWriter(
        CodeWriter writer,
        in BindingWriterEnvironment environment)
    {
        Debug.Assert(writer != null);
        _writer = writer!;
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
            var inlinePlan =
                BindingWritePlan.CreateInline(
                    in _environment,
                    extension,
                    context.ScopeId,
                    context.NameScopeExpression,
                    context.ElementReferences);

            WriteBinding(
                inlinePlan,
                context);

            return;
        }


        if (extension.ProvideValueMethod.Symbol is not IMethodSymbol provideValue)
        {
            WriteCreation(
                extension,
                context);

            return;
        }

        _writer.Write("(");

        WriteCreation(
            extension,
            context);

        _writer.Write(").");

        WriteProvideValueInvocationCore(
            provideValue,
            context);
    }

    /// <summary>
    /// Writes a binding from its existing plan so cached paths are retained.
    /// </summary>
    public void WriteBinding(
        in BindingWritePlan plan,
        in MarkupExtensionWriteContext context)
    {
        var bindingWriter =
            new BindingWriter(
                _writer,
                in _environment,
                context.ElementReferences);

        bindingWriter.WriteBinding(
            plan,
            context);
    }

    /// <summary>
    /// Writes the cached path field assigned to an existing binding plan.
    /// </summary>
    public void WriteCachedBindingPath(in BindingWritePlan plan)
    {
        var bindingWriter =
            new BindingWriter(
                _writer,
                in _environment);

        bindingWriter.WriteCachedPathField(plan);
    }

    /// <summary>
    /// Writes only construction and initialization of a markup extension.
    /// </summary>
    public void WriteCreation(
        MarkupExtensionValue extension,
        in MarkupExtensionWriteContext context)
    {
        var extensionType =
            extension.ExtensionType.Symbol
                as ITypeSymbol;

        Debug.Assert(extensionType != null);

        _writer.Write("new ");

        WriteTypeName(
            _writer,
            extensionType);

        _writer.Write("(");

        for (var i = 0;
             i < extension.Arguments.Length;
             i++)
        {
            if (i > 0)
            {
                _writer.Write(", ");
            }

            WriteArgumentValue(
                extension.Arguments[i],
                context);
        }

        _writer.Write(")");

        if (extension.Properties.IsDefaultOrEmpty)
        {
            return;
        }

        _writer.Write(" { ");

        for (var i = 0;
             i < extension.Properties.Length;
             i++)
        {
            if (i > 0)
            {
                _writer.Write(", ");
            }

            var property =
                extension.Properties[i];

            WriteIdentifier(_writer, property.Name);

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
        Debug.Assert(
            !string.IsNullOrEmpty(
                instanceExpression));

        _writer.Write(instanceExpression);


        if (extension.ProvideValueMethod.Symbol is not IMethodSymbol provideValue)
        {
            Debug.Fail("The markup extension has no ProvideValue method.");

            return;
        }

        _writer.Write(".");

        WriteProvideValueInvocationCore(
            provideValue,
            context);
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

        var constant =
            operation.ConstantValue;

        if (constant.HasValue)
        {
            WriteConstant(
                _writer,
                constant.Value,
                targetType);

            return;
        }

        if (!operation.IsDefault &&
            operation.Syntax != null)
        {
            _writer.Write(operation.Syntax.ToString());

            return;
        }

        if (convertedValue
            is CSharpSymbolDefinition definition &&
            TryWriteStaticMemberReference(
                _writer,
                definition.Symbol))
        {
            return;
        }

        if (convertedValue != null)
        {
            WriteConstant(
                _writer,
                convertedValue,
                targetType);

            return;
        }

        _writer.WriteStringLiteral(TrimQuotes(text));
    }

    private void WriteProvideValueInvocationCore(
        IMethodSymbol provideValue,
        in MarkupExtensionWriteContext context)
    {
        WriteIdentifier(
            _writer,
            provideValue.Name);

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
        if (string.IsNullOrEmpty(
                context.TargetObjectExpression) ||
            string.IsNullOrEmpty(
                context.TargetPropertyExpression) ||
            string.IsNullOrEmpty(
                context.IntermediateRootExpression) ||
            string.IsNullOrEmpty(
                context.BaseUriExpression) ||
            string.IsNullOrEmpty(
                context.DirectParentsStackExpression))
        {
            Debug.Fail(
                "The markup extension service-provider context is incomplete.");

            _writer.Write("default!");
            return;
        }

        _writer
            .Write(
                "CreateMarkupServiceProvider(" +
                "targetObject: ")
            .Write(context.TargetObjectExpression)
            .Write(", targetProperty: ")
            .Write(context.TargetPropertyExpression)
            .Write(", intermediateRootObject: ")
            .Write(context.IntermediateRootExpression)
            .Write(", baseUri: ")
            .Write(context.BaseUriExpression)
            .Write(", directParentsStack: ")
            .Write(context.DirectParentsStackExpression);

        if (!string.IsNullOrEmpty(
                context.FallbackServiceProviderExpression))
        {
            _writer
                .Write(", fallbackServiceProvider: ")
                .Write(context.FallbackServiceProviderExpression!);
        }

        _writer.Write(")");
    }

    internal static void WriteIdentifier(
        CodeWriter writer,
        string identifier)
    {
        writer.WriteIdentifierEscapeIfNeeded(identifier);

        writer.Write(identifier);
    }

    internal static void WriteTypeName(
        CodeWriter writer,
        ITypeSymbol? type)
    {
        if (type == null || ContainsErrorType(type))
        {
            writer.Write("global::System.Object");

            return;
        }

        // SymbolDisplay currently allocates the resulting string,
        // but avoids introducing a generator-wide dictionary whose
        // retained memory would usually cost more than these temporary
        // generation-time strings.
        writer.Write(type.ToDisplayString(s_typeDisplayFormat));
    }

    internal static void WriteStaticMemberReference(
        CodeWriter writer,
        ISymbol symbol)
    {
        Debug.Assert(
            symbol is IFieldSymbol
            {
                IsStatic: true,
            } or
            IPropertySymbol
            {
                IsStatic: true,
            });

        WriteTypeName(
            writer,
            symbol.ContainingType);

        writer.Write(".");

        WriteIdentifier(
            writer,
            symbol.Name);
    }

    private static bool TryWriteStaticMemberReference(
        CodeWriter writer,
        ISymbol? symbol)
    {
        if (symbol is not
            IFieldSymbol
            {
                IsStatic: true,
            } and not
            IPropertySymbol
            {
                IsStatic: true,
            })
        {
            return false;
        }

        WriteStaticMemberReference(
            writer,
            symbol);

        return true;
    }

    internal static void WriteConstant(
        CodeWriter writer,
        object? value,
        ISymbol? targetType)
    {
        if (value == null)
        {
            writer.Write("null");
            return;
        }

        if (targetType is
            INamedTypeSymbol
            {
                TypeKind: TypeKind.Enum,
            } unsignedEnumType &&
            value is ulong unsignedEnumValue &&
            unsignedEnumValue > long.MaxValue)
        {
            writer.Write("unchecked((");

            WriteTypeName(
                writer,
                unsignedEnumType);

            writer.Write(")");

            writer.Write(
                unsignedEnumValue.ToString(CultureInfo.InvariantCulture));

            writer.Write("UL)");
            return;
        }

        if (targetType is
            INamedTypeSymbol
            {
                TypeKind: TypeKind.Enum,
            } enumType &&
            TryConvertToInt64(
                value,
                out var enumValue))
        {
            writer.Write("(");

            WriteTypeName(
                writer,
                enumType);

            writer.Write(")");

            writer.Write(
                enumValue.ToString(CultureInfo.InvariantCulture));

            return;
        }

        switch (value)
        {
            case CSharpSymbolDefinition definition
                when TryWriteStaticMemberReference(
                    writer,
                    definition.Symbol):
                return;

            case ITypeSymbol type:
                writer.Write("typeof(");
                WriteTypeName(writer, type);
                writer.Write(")");
                return;

            case string text:
                writer.WriteStringLiteral(text);
                return;

            case char character:
                writer.Write(
                    SymbolDisplay.FormatLiteral(
                        character,
                        quote: true));
                return;

            case bool boolean:
                writer.WriteBooleanLiteral(boolean);
                return;

            case byte number:
                writer.WriteIntegerLiteral(number);
                return;

            case sbyte number:
                writer.WriteIntegerLiteral(number);
                return;

            case short number:
                writer.WriteIntegerLiteral(number);
                return;

            case ushort number:
                writer.WriteIntegerLiteral(number);
                return;

            case int number:
                writer.WriteIntegerLiteral(number);
                return;

            case uint number:
                writer.Write(
                    number.ToString(
                        CultureInfo.InvariantCulture));

                writer.Write("u");
                return;

            case long number:
                writer.Write(
                    number.ToString(
                        CultureInfo.InvariantCulture));

                writer.Write("L");
                return;

            case ulong number:
                writer.Write(
                    number.ToString(
                        CultureInfo.InvariantCulture));

                writer.Write("UL");
                return;

            case float number:
                WriteSingleLiteral(
                    writer,
                    number);

                return;

            case double number:
                WriteDoubleLiteral(
                    writer,
                    number);

                return;

            case decimal number:
                writer.Write(
                    number.ToString(
                        CultureInfo.InvariantCulture));

                writer.Write("m");
                return;

            default:
                Debug.Fail(
                    "Unsupported constant value: " +
                    value.GetType().FullName);

                writer.WriteStringLiteral(
                    value.ToString() ??
                    string.Empty);

                return;
        }
    }

    private static void WriteSingleLiteral(
        CodeWriter writer,
        float value)
    {
        if (float.IsNaN(value))
        {
            writer.Write("global::System.Single.NaN");

            return;
        }

        if (float.IsPositiveInfinity(value))
        {
            writer.Write("global::System.Single.PositiveInfinity");

            return;
        }

        if (float.IsNegativeInfinity(value))
        {
            writer.Write("global::System.Single.NegativeInfinity");

            return;
        }

        writer.Write(
            value.ToString(
                "R",
                CultureInfo.InvariantCulture));

        writer.Write("f");
    }

    private static void WriteDoubleLiteral(
        CodeWriter writer,
        double value)
    {
        if (double.IsNaN(value))
        {
            writer.Write("global::System.Double.NaN");

            return;
        }

        if (double.IsPositiveInfinity(value))
        {
            writer.Write("global::System.Double.PositiveInfinity");

            return;
        }

        if (double.IsNegativeInfinity(value))
        {
            writer.Write("global::System.Double.NegativeInfinity");

            return;
        }

        writer.Write(
            value.ToString(
                "R",
                CultureInfo.InvariantCulture));

        writer.Write("d");
    }

    private static bool TryConvertToInt64(
        object value,
        out long result)
    {
        switch (value)
        {
            case byte number:
                result = number;
                return true;

            case sbyte number:
                result = number;
                return true;

            case short number:
                result = number;
                return true;

            case ushort number:
                result = number;
                return true;

            case int number:
                result = number;
                return true;

            case uint number:
                result = number;
                return true;

            case long number:
                result = number;
                return true;

            case ulong number
                when number <= long.MaxValue:
                result = (long)number;
                return true;

            default:
                result = 0;
                return false;
        }
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

    private static bool ContainsErrorType(ITypeSymbol type)
    {
        if (type is IErrorTypeSymbol ||
            type.TypeKind == TypeKind.Error)
        {
            return true;
        }

        switch (type)
        {
            case IArrayTypeSymbol array:
                return ContainsErrorType(
                    array.ElementType);

            case IPointerTypeSymbol pointer:
                return ContainsErrorType(
                    pointer.PointedAtType);

            case INamedTypeSymbol named:
                for (var i = 0; i < named.TypeArguments.Length; i++)
                {
                    if (ContainsErrorType(named.TypeArguments[i]))
                    {
                        return true;
                    }
                }

                return false;

            case IFunctionPointerTypeSymbol functionPointer:
                if (ContainsErrorType(functionPointer.Signature.ReturnType))
                {
                    return true;
                }

                var parameters = functionPointer.Signature.Parameters;

                for (var i = 0; i < parameters.Length; i++)
                {
                    if (ContainsErrorType(parameters[i].Type))
                    {
                        return true;
                    }
                }

                return false;

            default:
                return false;
        }
    }
}
