using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Immutable;
using System.Globalization;
using AkburaPropertySymbol = Akbura.Language.Symbols.IPropertySymbol;
using CSharpExpressionSyntax = Microsoft.CodeAnalysis.CSharp.Syntax.ExpressionSyntax;
using CSharpSyntaxFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using CSharpSyntaxFacts = Microsoft.CodeAnalysis.CSharp.SyntaxFacts;
using CSharpSyntaxKind = Microsoft.CodeAnalysis.CSharp.SyntaxKind;
using CSharpSymbolDefinition = Akbura.Language.Symbols.CSharpSymbolDefinition;
using RoslynFieldSymbol = Microsoft.CodeAnalysis.IFieldSymbol;
using RoslynMethodSymbol = Microsoft.CodeAnalysis.IMethodSymbol;
using RoslynPropertySymbol = Microsoft.CodeAnalysis.IPropertySymbol;
using RoslynSymbol = Microsoft.CodeAnalysis.ISymbol;

namespace Akbura.Language.CodeGeneration;

internal readonly struct AkcssGeneratedValue
{
    public AkcssGeneratedValue(
        string expression,
        AkcssDynamicResourceBinding? dynamicResource,
        bool requiresResourceHost)
    {
        Expression = expression;
        DynamicResource = dynamicResource;
        RequiresResourceHost = requiresResourceHost;
    }

    public string Expression { get; }

    public AkcssDynamicResourceBinding? DynamicResource { get; }

    public bool RequiresResourceHost { get; }
}

internal readonly struct AkcssDynamicResourceBinding
{
    public AkcssDynamicResourceBinding(string keyExpression, string valueParameterName)
    {
        KeyExpression = keyExpression;
        ValueParameterName = valueParameterName;
    }

    public string KeyExpression { get; }

    public string ValueParameterName { get; }
}

internal static class AkcssExpressionGenerator
{
    internal const string MetadataTargetName = "__target";
    internal const string MetadataArgumentsName = "__arguments";

    private static readonly SymbolDisplayFormat s_metadataTypeDisplayFormat =
        SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions &
            ~SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

    public static AkcssGeneratedValue GetValueExpression(
        IAkcssPropertySetterOperation operation,
        string targetName,
        bool observeDynamicResource,
        ArrayBuilder<AkcssIdentifierValue>? identifierValues = null,
        int identifierValueCount = -1,
        bool preserveAmxResources = false)
    {
        var valueCount = GetIdentifierValueCount(identifierValues, identifierValueCount);

        if (operation is IMetadataAkcssOperation metadataOperation)
        {
            var metadataValue = RewriteMetadataExpression(
                metadataOperation.Expression,
                targetName,
                operation.ContainingAkcssSymbol,
                identifierValues,
                valueCount,
                observeDynamicResource,
                preserveAmxResources);

            if (!operation.RequiresBrushConversion)
            {
                return metadataValue;
            }

            return new AkcssGeneratedValue(
                WrapSolidColorBrush(metadataValue.Expression),
                metadataValue.DynamicResource,
                metadataValue.RequiresResourceHost);
        }

        var rewriter = AkcssAmxExpressionRewriter.GetInstance(
            targetName,
            observeDynamicResource,
            GetTargetParameterName(operation.ContainingAkcssSymbol),
            identifierValues,
            valueCount,
            preserveAmxResources);

        try
        {
            string expression;

            switch (operation.ConvertedValue)
            {
                case AkcssColorValue color:
                    expression = CreateColorExpression(color);
                    break;

                case AkcssThicknessValue thickness:
                    expression = CreateThicknessExpression(thickness);
                    break;

                case AkcssThicknessExpressionValue thickness:
                    expression = CreateThicknessExpression(
                        thickness,
                        rewriter,
                        operation.ValueOperation.Operation?.SemanticModel);

                    break;

                case CSharpSymbolDefinition definition
                    when GetStaticMemberReference(definition.Symbol) is { } member:
                    expression = member;
                    break;

                default:
                    expression = RewriteExpression(
                        operation.ValueOperation.Syntax as CSharpExpressionSyntax ??
                        operation.Syntax?.Expression.GetRawCSharpExpression(),
                        rewriter,
                        operation.ValueOperation.Operation?.SemanticModel);

                    break;
            }

            if (operation.RequiresBrushConversion)
            {
                expression = WrapSolidColorBrush(expression);
            }

            return new AkcssGeneratedValue(
                expression,
                rewriter.DynamicResource,
                rewriter.RequiresResourceHost);
        }
        finally
        {
            rewriter.Free();
        }
    }

    public static string GetIfConditionExpression(
        IAkcssIfOperation operation,
        string targetName,
        ArrayBuilder<AkcssIdentifierValue>? identifierValues = null,
        int identifierValueCount = -1,
        bool preserveAmxResources = false)
    {
        var valueCount = GetIdentifierValueCount(identifierValues, identifierValueCount);

        if (operation is IMetadataAkcssOperation metadataOperation)
        {
            return RewriteMetadataExpression(
                metadataOperation.Expression,
                targetName,
                operation.ContainingAkcssSymbol,
                identifierValues,
                valueCount,
                observeDynamicResource: false,
                preserveAmxResources).Expression;
        }

        var rewriter = AkcssAmxExpressionRewriter.GetInstance(
            targetName,
            observeDynamicResource: false,
            GetTargetParameterName(operation.ContainingAkcssSymbol),
            identifierValues,
            valueCount,
            preserveAmxResources);

        try
        {
            return RewriteExpression(
                operation.ConditionOperation.Syntax as CSharpExpressionSyntax ??
                operation.Syntax?.Condition.GetRawCSharpExpression(),
                rewriter,
                operation.ConditionOperation.Operation?.SemanticModel);
        }
        finally
        {
            rewriter.Free();
        }
    }

    public static AkcssGeneratedValue RewriteMetadataExpression(
        string? expression,
        string targetName,
        IAkcssSymbol containingSymbol,
        ArrayBuilder<AkcssIdentifierValue>? identifierValues,
        int identifierValueCount,
        bool observeDynamicResource,
        bool preserveAmxResources = false)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return new AkcssGeneratedValue(
                "default",
                dynamicResource: null,
                requiresResourceHost: false);
        }

        var syntax = CSharpSyntaxFactory.ParseExpression(expression!);

        var metadataRewriter = AkcssMetadataExpressionRewriter.GetInstance(
            targetName,
            containingSymbol,
            identifierValues,
            identifierValueCount);

        try
        {
            syntax = metadataRewriter.Visit(syntax) as CSharpExpressionSyntax ?? syntax;
        }
        finally
        {
            metadataRewriter.Free();
        }

        var amxRewriter = AkcssAmxExpressionRewriter.GetInstance(
            targetName,
            observeDynamicResource,
            MetadataTargetName,
            identifierValues,
            identifierValueCount,
            preserveAmxResources);

        try
        {
            var rewritten = amxRewriter.Visit(syntax)?.ToString() ?? "default";

            var requiresResourceHost =
                amxRewriter.RequiresResourceHost ||
                rewritten.Contains(
                    "global::Avalonia.Controls.IResourceHost",
                    StringComparison.Ordinal) ||
                rewritten.Contains(
                    "global::Avalonia.Controls.ResourceNodeExtensions.",
                    StringComparison.Ordinal);

            return new AkcssGeneratedValue(
                rewritten,
                amxRewriter.DynamicResource,
                requiresResourceHost);
        }
        finally
        {
            amxRewriter.Free();
        }
    }

    public static AkcssGeneratedValue RewriteGeneratedExpression(
        string? expression,
        string targetName,
        bool observeDynamicResource)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return new AkcssGeneratedValue(
                "default",
                dynamicResource: null,
                requiresResourceHost: false);
        }

        var syntax = CSharpSyntaxFactory.ParseExpression(expression!);

        var rewriter = AkcssAmxExpressionRewriter.GetInstance(
            targetName,
            observeDynamicResource,
            MetadataTargetName,
            identifierValues: null,
            identifierValueCount: 0,
            preserveResourceInvocations: false);

        try
        {
            var rewritten = rewriter.Visit(syntax)?.ToString() ?? "default";

            return new AkcssGeneratedValue(
                rewritten,
                rewriter.DynamicResource,
                rewriter.RequiresResourceHost);
        }
        finally
        {
            rewriter.Free();
        }
    }

    public static bool TryAppendApplyArgumentExpressions(
        string item,
        ITailwindUtilitySymbol utility,
        IAkcssSymbol containingSymbol,
        ArrayBuilder<string> expressions)
    {
        var originalCount = expressions.Count;

        if (!ApplyArgumentReader.TryCreate(
                item,
                utility.Name,
                utility.Parameters.Length,
                out var reader))
        {
            return false;
        }

        for (var i = 0; i < utility.Parameters.Length; i++)
        {
            if (!reader.TryRead(out var start, out var length))
            {
                expressions.Count = originalCount;
                return false;
            }

            var text = item.Substring(start, length);

            if (!TryCreateApplyArgumentExpression(
                    text,
                    utility.Parameters[i].Type.Symbol,
                    containingSymbol,
                    out var expression))
            {
                expressions.Count = originalCount;
                return false;
            }

            expressions.Add(expression);
        }

        if (!reader.IsComplete)
        {
            expressions.Count = originalCount;
            return false;
        }

        return true;
    }

    public static bool TryPushApplyParameterValues(
        string item,
        ITailwindUtilitySymbol utility,
        IAkcssApplyOperation operation,
        string targetName,
        ArrayBuilder<AkcssIdentifierValue> identifierValues,
        out int previousCount)
    {
        previousCount = identifierValues.Count;

        if (utility.Parameters.IsEmpty)
        {
            return true;
        }

        if (!ApplyArgumentReader.TryCreate(
                item,
                utility.Name,
                utility.Parameters.Length,
                out var reader))
        {
            return false;
        }

        var outerValueCount = previousCount;

        var rewriter = AkcssAmxExpressionRewriter.GetInstance(
            targetName,
            observeDynamicResource: false,
            GetTargetParameterName(operation.ContainingAkcssSymbol),
            identifierValues,
            outerValueCount,
            preserveResourceInvocations: false);

        try
        {
            for (var i = 0; i < utility.Parameters.Length; i++)
            {
                if (!reader.TryRead(out var start, out var length))
                {
                    identifierValues.Count = previousCount;
                    return false;
                }

                var parameter = utility.Parameters[i];
                var text = item.Substring(start, length);

                if (!TryCreateApplyArgumentExpression(
                        text,
                        parameter.Type.Symbol,
                        operation.ContainingAkcssSymbol,
                        out var argument))
                {
                    identifierValues.Count = previousCount;
                    return false;
                }

                var expression = CSharpSyntaxFactory.ParseExpression(argument);
                expression = rewriter.Visit(expression) as CSharpExpressionSyntax ?? expression;

                AddIdentifierAliases(identifierValues, parameter, expression);
            }

            if (!reader.IsComplete)
            {
                identifierValues.Count = previousCount;
                return false;
            }

            return true;
        }
        finally
        {
            rewriter.Free();
        }
    }

    public static void AddDirectMetadataParameterValues(
        ITailwindUtilitySymbol utility,
        ArrayBuilder<AkcssIdentifierValue> identifierValues)
    {
        AddArgumentParameterValues(utility, MetadataArgumentsName, identifierValues);
    }

    public static void AddArgumentParameterValues(
        ITailwindUtilitySymbol utility,
        string argumentsExpression,
        ArrayBuilder<AkcssIdentifierValue> identifierValues)
    {
        AkburaDebug.Assert(utility != null);
        AkburaDebug.Assert(!string.IsNullOrEmpty(argumentsExpression));
        AkburaDebug.Assert(identifierValues != null);

        var parameters = utility.Parameters;

        for (var i = 0; i < parameters.Length; i++)
        {
            var parameter = parameters[i];
            var expression = CreateArgumentExpression(parameter, argumentsExpression);

            AddIdentifierAliases(identifierValues, parameter, expression);
        }
    }

    public static ITypeSymbol? GetAkcssTargetType(IAkcssSymbol symbol)
    {
        return symbol.HasTargetType
            ? symbol.TargetType.Symbol as ITypeSymbol
            : null;
    }

    public static ITypeSymbol? GetPropertyReceiverType(AkburaPropertySymbol property)
    {
        return property.WriteKind switch
        {
            PropertyAccessKind.ClrProperty =>
                (property.WriteDefinition.Symbol as RoslynPropertySymbol)?.ContainingType,

            PropertyAccessKind.AvaloniaProperty =>
                property.WriteDefinition.Symbol?.ContainingType ??
                property.AvaloniaPropertyDefinition.Symbol?.ContainingType ??
                property.AttachedPropertyDefinition.Symbol?.ContainingType,

            PropertyAccessKind.AttachedAccessor =>
                GetAttachedTargetType(property),

            _ =>
                null,
        };
    }

    public static ITypeSymbol? GetAttachedTargetType(AkburaPropertySymbol? property)
    {
        if (property == null)
        {
            return null;
        }

        if (property.AttachedTargetType.Symbol is ITypeSymbol targetType)
        {
            return targetType;
        }

        var setter =
            property.WriteDefinition.Symbol as RoslynMethodSymbol ??
            property.AttachedSetterDefinition.Symbol as RoslynMethodSymbol;

        return setter is { Parameters.Length: > 0 }
            ? setter.Parameters[0].Type
            : null;
    }

    public static string? GetStaticMemberReference(RoslynSymbol? symbol)
    {
        switch (symbol)
        {
            case RoslynFieldSymbol { IsStatic: true } field:
                return CreateMemberReference(field.ContainingType, field.Name);

            case RoslynPropertySymbol { IsStatic: true } property:
                return CreateMemberReference(property.ContainingType, property.Name);

            default:
                return null;
        }
    }

    public static string GetMethodReference(RoslynMethodSymbol method)
    {
        return CreateMemberReference(method.ContainingType, method.Name);
    }

    public static string GetTypeName(RoslynSymbol? symbol)
    {
        return symbol is ITypeSymbol type
            ? type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            : "global::System.Object";
    }

    public static string GetMetadataTypeName(RoslynSymbol? symbol)
    {
        return symbol is ITypeSymbol type
            ? type.ToDisplayString(s_metadataTypeDisplayFormat)
            : "global::System.Object";
    }

    public static string GetTargetParameterName(IAkcssSymbol symbol)
    {
        return symbol is ITailwindUtilitySymbol utility
            ? GetTargetParameterName(utility.Parameters)
            : "__target";
    }

    public static string GetTargetParameterName(
        ImmutableArray<ITailwindUtilityParameterSymbol> parameters)
    {
        var name = "__target";
        var hasConflict = true;

        while (hasConflict)
        {
            hasConflict = false;

            for (var i = 0; i < parameters.Length; i++)
            {
                var parameter = parameters[i];

                if (StringComparer.Ordinal.Equals(parameter.Name, name) ||
                    StringComparer.Ordinal.Equals(parameter.CSharpParameter?.Name, name))
                {
                    name += "_";
                    hasConflict = true;
                    break;
                }
            }
        }

        return name;
    }

    public static string GetParameterName(ITailwindUtilityParameterSymbol parameter)
    {
        var name = parameter.CSharpName;

        return CSharpSyntaxFacts.IsValidIdentifier(name)
            ? EscapeIdentifier(name)
            : "parameter" + parameter.Ordinal.ToString(CultureInfo.InvariantCulture);
    }

    public static string EscapeIdentifier(string name)
    {
        return CSharpSyntaxFacts.GetKeywordKind(name) != CSharpSyntaxKind.None ||
            CSharpSyntaxFacts.GetContextualKeywordKind(name) != CSharpSyntaxKind.None
                ? "@" + name
                : name;
    }

    public static string FormatDouble(double value)
    {
        if (double.IsNaN(value))
        {
            return "global::System.Double.NaN";
        }

        if (double.IsPositiveInfinity(value))
        {
            return "global::System.Double.PositiveInfinity";
        }

        if (double.IsNegativeInfinity(value))
        {
            return "global::System.Double.NegativeInfinity";
        }

        return value.ToString("R", CultureInfo.InvariantCulture);
    }

    private static void AddIdentifierAliases(
        ArrayBuilder<AkcssIdentifierValue> values,
        ITailwindUtilityParameterSymbol parameter,
        CSharpExpressionSyntax expression)
    {
        AkcssIdentifierValueLookup.Add(values, parameter.Name, expression);

        if (!StringComparer.Ordinal.Equals(parameter.CSharpName, parameter.Name))
        {
            AkcssIdentifierValueLookup.Add(values, parameter.CSharpName, expression);
        }
    }

    private static CSharpExpressionSyntax CreateArgumentExpression(
        ITailwindUtilityParameterSymbol parameter,
        string argumentsExpression)
    {
        var ordinal = CSharpSyntaxFactory.LiteralExpression(
            CSharpSyntaxKind.NumericLiteralExpression,
            CSharpSyntaxFactory.Literal(parameter.Ordinal));

        var argument = CSharpSyntaxFactory.Argument(ordinal);

        var elementAccess = CSharpSyntaxFactory.ElementAccessExpression(
            CSharpSyntaxFactory.IdentifierName(argumentsExpression),
            CSharpSyntaxFactory.BracketedArgumentList(
                CSharpSyntaxFactory.SingletonSeparatedList(argument)));

        return CSharpSyntaxFactory.CastExpression(
            CSharpSyntaxFactory.ParseTypeName(GetMetadataTypeName(parameter.Type.Symbol)),
            CSharpSyntaxFactory.PostfixUnaryExpression(
                CSharpSyntaxKind.SuppressNullableWarningExpression,
                elementAccess));
    }

    private static bool TryCreateApplyArgumentExpression(
        string text,
        RoslynSymbol? parameterTypeSymbol,
        IAkcssSymbol containingSymbol,
        out string expression)
    {
        if (containingSymbol is ITailwindUtilitySymbol containingUtility)
        {
            var parameters = containingUtility.Parameters;

            for (var i = 0; i < parameters.Length; i++)
            {
                var parameter = parameters[i];

                if (StringComparer.Ordinal.Equals(parameter.Name, text) ||
                    StringComparer.Ordinal.Equals(parameter.CSharpParameter?.Name, text))
                {
                    expression = GetParameterName(parameter);
                    return true;
                }
            }
        }

        var parameterType = parameterTypeSymbol as ITypeSymbol;

        if (parameterType is INamedTypeSymbol
            {
                OriginalDefinition.SpecialType: SpecialType.System_Nullable_T,
                TypeArguments.Length: 1,
            } nullableType)
        {
            parameterType = nullableType.TypeArguments[0];
        }

        if (parameterType is INamedTypeSymbol { TypeKind: TypeKind.Enum } enumType)
        {
            var members = enumType.GetMembers();

            for (var i = 0; i < members.Length; i++)
            {
                if (members[i] is RoslynFieldSymbol field &&
                    field.HasConstantValue &&
                    string.Equals(field.Name, text, StringComparison.OrdinalIgnoreCase))
                {
                    expression = CreateMemberReference(enumType, field.Name);
                    return true;
                }
            }

            expression = string.Empty;
            return false;
        }

        switch (parameterType?.SpecialType)
        {
            case SpecialType.System_String:
                expression = SymbolDisplay.FormatLiteral(text, quote: true);
                return true;

            case SpecialType.System_Char when text.Length == 1:
                expression = SymbolDisplay.FormatLiteral(text[0], quote: true);
                return true;

            case SpecialType.System_Boolean when bool.TryParse(text, out var boolean):
                expression = boolean ? "true" : "false";
                return true;

            case SpecialType.System_SByte:
            case SpecialType.System_Byte:
            case SpecialType.System_Int16:
            case SpecialType.System_UInt16:
            case SpecialType.System_Int32:
                if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                {
                    expression = text;
                    return true;
                }

                break;

            case SpecialType.System_UInt32:
                if (uint.TryParse(
                        text,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var uintValue))
                {
                    expression = uintValue.ToString(CultureInfo.InvariantCulture) + "u";
                    return true;
                }

                break;

            case SpecialType.System_Int64:
                if (long.TryParse(
                        text,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var longValue))
                {
                    expression = longValue.ToString(CultureInfo.InvariantCulture) + "L";
                    return true;
                }

                break;

            case SpecialType.System_UInt64:
                if (ulong.TryParse(
                        text,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var ulongValue))
                {
                    expression = ulongValue.ToString(CultureInfo.InvariantCulture) + "UL";
                    return true;
                }

                break;

            case SpecialType.System_Single:
                if (float.TryParse(
                        text,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out var floatValue))
                {
                    expression = floatValue.ToString("R", CultureInfo.InvariantCulture) + "f";
                    return true;
                }

                break;

            case SpecialType.System_Double:
                if (double.TryParse(
                        text,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out var doubleValue))
                {
                    expression = FormatDouble(doubleValue);
                    return true;
                }

                break;

            case SpecialType.System_Decimal:
                if (decimal.TryParse(
                        text,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out var decimalValue))
                {
                    expression = decimalValue.ToString(CultureInfo.InvariantCulture) + "m";
                    return true;
                }

                break;

            case SpecialType.System_Object:
                if (long.TryParse(
                        text,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out _))
                {
                    expression = text;
                    return true;
                }

                if (double.TryParse(
                        text,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out var objectDouble))
                {
                    expression = FormatDouble(objectDouble);
                    return true;
                }

                if (bool.TryParse(text, out var objectBoolean))
                {
                    expression = objectBoolean ? "true" : "false";
                    return true;
                }

                expression = SymbolDisplay.FormatLiteral(text, quote: true);
                return true;

            default:
                if (parameterType == null)
                {
                    expression = text;
                    return expression.Length > 0;
                }

                break;
        }

        expression = text;
        return expression.Length > 0;
    }

    private static string RewriteExpression(
        CSharpExpressionSyntax? expression,
        AkcssAmxExpressionRewriter rewriter,
        SemanticModel? semanticModel)
    {
        if (expression == null)
        {
            return "default";
        }

        if (semanticModel != null &&
            ReferenceEquals(expression.SyntaxTree, semanticModel.SyntaxTree))
        {
            var fullyQualifiedRewriter =
                AkcssFullyQualifiedExpressionRewriter.GetInstance(semanticModel);

            try
            {
                expression =
                    fullyQualifiedRewriter.Visit(expression) as CSharpExpressionSyntax ??
                    expression;
            }
            finally
            {
                fullyQualifiedRewriter.Free();
            }
        }

        return rewriter.Visit(expression)?.ToString() ?? "default";
    }

    private static int GetIdentifierValueCount(
        ArrayBuilder<AkcssIdentifierValue>? values,
        int count)
    {
        if (values == null)
        {
            return 0;
        }

        if (count < 0)
        {
            return values.Count;
        }

        AkburaDebug.Assert((uint)count <= (uint)values.Count);

        return count;
    }

    private static string CreateColorExpression(AkcssColorValue value)
    {
        var pooled = PooledStringBuilder.GetInstance();
        var builder = pooled.Builder;

        builder.Append("global::Avalonia.Media.Color.FromArgb(");
        builder.Append(value.A);
        builder.Append(", ");
        builder.Append(value.R);
        builder.Append(", ");
        builder.Append(value.G);
        builder.Append(", ");
        builder.Append(value.B);
        builder.Append(')');

        return pooled.ToStringAndFree();
    }

    private static string CreateThicknessExpression(AkcssThicknessValue value)
    {
        var pooled = PooledStringBuilder.GetInstance();
        var builder = pooled.Builder;

        builder.Append("new global::Avalonia.Thickness(");
        builder.Append(FormatDouble(value.Left));
        builder.Append(", ");
        builder.Append(FormatDouble(value.Top));
        builder.Append(", ");
        builder.Append(FormatDouble(value.Right));
        builder.Append(", ");
        builder.Append(FormatDouble(value.Bottom));
        builder.Append(')');

        return pooled.ToStringAndFree();
    }

    private static string CreateThicknessExpression(
        AkcssThicknessExpressionValue value,
        AkcssAmxExpressionRewriter rewriter,
        SemanticModel? semanticModel)
    {
        var pooled = PooledStringBuilder.GetInstance();
        var builder = pooled.Builder;

        builder.Append("new global::Avalonia.Thickness(");
        builder.Append(RewriteExpression(value.Left, rewriter, semanticModel));
        builder.Append(", ");
        builder.Append(RewriteExpression(value.Top, rewriter, semanticModel));
        builder.Append(", ");
        builder.Append(RewriteExpression(value.Right, rewriter, semanticModel));
        builder.Append(", ");
        builder.Append(RewriteExpression(value.Bottom, rewriter, semanticModel));
        builder.Append(')');

        return pooled.ToStringAndFree();
    }

    private static string WrapSolidColorBrush(string expression)
    {
        var pooled = PooledStringBuilder.GetInstance();
        var builder = pooled.Builder;

        builder.Append("new global::Avalonia.Media.SolidColorBrush(");
        builder.Append(expression);
        builder.Append(')');

        return pooled.ToStringAndFree();
    }

    private static string CreateMemberReference(RoslynSymbol containingType, string memberName)
    {
        var pooled = PooledStringBuilder.GetInstance();
        var builder = pooled.Builder;

        builder.Append(GetTypeName(containingType));
        builder.Append('.');
        builder.Append(EscapeIdentifier(memberName));

        return pooled.ToStringAndFree();
    }

    private struct ApplyArgumentReader
    {
        private readonly string _item;
        private int _position;
        private int _remaining;

        private ApplyArgumentReader(string item, int position, int remaining)
        {
            _item = item;
            _position = position;
            _remaining = remaining;
        }

        public readonly bool IsComplete => _remaining == 0 && _position == _item.Length;

        public static bool TryCreate(
            string item,
            string utilityName,
            int parameterCount,
            out ApplyArgumentReader reader)
        {
            if (parameterCount <= 0 ||
                item.Length <= utilityName.Length ||
                !item.StartsWith(utilityName, StringComparison.Ordinal) ||
                item[utilityName.Length] != '-')
            {
                reader = default;
                return false;
            }

            reader = new ApplyArgumentReader(
                item,
                utilityName.Length + 1,
                parameterCount);

            return true;
        }

        public bool TryRead(out int start, out int length)
        {
            start = _position;
            length = 0;

            if (_remaining <= 0 || start >= _item.Length)
            {
                return false;
            }

            int end;

            if (_remaining == 1)
            {
                end = _item.IndexOf('-', start);

                if (end >= 0)
                {
                    return false;
                }

                end = _item.Length;
            }
            else
            {
                end = _item.IndexOf('-', start);

                if (end < 0)
                {
                    return false;
                }
            }

            if (end == start)
            {
                return false;
            }

            length = end - start;
            _remaining--;
            _position = _remaining == 0
                ? end
                : end + 1;

            return true;
        }
    }
}
