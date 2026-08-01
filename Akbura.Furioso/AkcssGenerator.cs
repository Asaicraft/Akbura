using Akbura.Language;
using Akbura.Language.Binder;
using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using AkburaPropertySymbol = Akbura.Language.Symbols.IPropertySymbol;
using AkburaSyntaxList = Akbura.Language.Syntax.SyntaxList<Akbura.Language.Syntax.AkcssTopLevelMemberSyntax>;
using CSharpExpressionSyntax = Microsoft.CodeAnalysis.CSharp.Syntax.ExpressionSyntax;
using CSharpGenericNameSyntax = Microsoft.CodeAnalysis.CSharp.Syntax.GenericNameSyntax;
using CSharpIdentifierNameSyntax = Microsoft.CodeAnalysis.CSharp.Syntax.IdentifierNameSyntax;
using CSharpInvocationExpressionSyntax = Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax;
using CSharpMemberAccessExpressionSyntax = Microsoft.CodeAnalysis.CSharp.Syntax.MemberAccessExpressionSyntax;
using CSharpSyntaxFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using CSharpSyntaxFacts = Microsoft.CodeAnalysis.CSharp.SyntaxFacts;
using CSharpSyntaxKind = Microsoft.CodeAnalysis.CSharp.SyntaxKind;
using CSharpSyntaxRewriter = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxRewriter;
using CSharpUsingDirectiveSyntax = Microsoft.CodeAnalysis.CSharp.Syntax.UsingDirectiveSyntax;
using RoslynFieldSymbol = Microsoft.CodeAnalysis.IFieldSymbol;
using RoslynMethodSymbol = Microsoft.CodeAnalysis.IMethodSymbol;
using RoslynPropertySymbol = Microsoft.CodeAnalysis.IPropertySymbol;

namespace Akbura.Furioso;

internal static class AkcssGenerator
{
    private static readonly SymbolDisplayFormat s_metadataTypeDisplayFormat =
        SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions &
            ~SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

    private const string RuntimeStyleType = "global::Akbura.Akcss.AkcssStyle";
    private const string RuntimeClassType = "global::Akbura.Akcss.AkcssClass";
    private const string RuntimeUtilityType = "global::Akbura.Akcss.AkcssUtility";
    private const string RuntimeZeroUtilityType = "global::Akbura.Akcss.ZeroAkcssUtility";
    private const string StyleNameAttribute = "global::Akbura.CompilerAnotations.StyleNameAttribute";
    private const string InlinedStyleAttribute = "global::Akbura.CompilerAnotations.InlinedStyleAttribute";
    private const string ObservesPropertyAttribute = "global::Akbura.CompilerAnotations.ObservesPropertyAttribute";
    private const string AkcssModuleAttribute = "global::Akbura.CompilerAnotations.AkcssModuleAttribute";
    private const string AkcssModuleReferenceAttribute = "global::Akbura.CompilerAnotations.AkcssModuleReferenceAttribute";
    private const string AkcssSymbolAttribute = "global::Akbura.CompilerAnotations.AkcssSymbolAttribute";
    private const string AkcssSymbolKind = "global::Akbura.CompilerAnotations.AkcssSymbolKind";
    private const string AkcssUtilityParameterAttribute = "global::Akbura.CompilerAnotations.AkcssUtilityParameterAttribute";
    private const string CompilerGeneratedAttribute = "global::System.Runtime.CompilerServices.CompilerGeneratedAttribute";
    private const string EditorBrowsableAttribute = "global::System.ComponentModel.EditorBrowsableAttribute";
    private const string BrowsableAttribute = "global::System.ComponentModel.BrowsableAttribute";
    private const string RuntimeAkcssOperationAttribute = "global::Akbura.CompilerAnotations.AkcssOperationAttribute";
    private const string RuntimeAkcssOperationKind = "global::Akbura.CompilerAnotations.AkcssOperationKind";
    private const string RuntimeAkcssOperationOriginKind = "global::Akbura.CompilerAnotations.AkcssOperationOriginKind";
    private const string RuntimeAkcssPropertyAccessKind = "global::Akbura.CompilerAnotations.AkcssPropertyAccessKind";
    private const string RuntimeAkcssPropertyValueKind = "global::Akbura.CompilerAnotations.AkcssPropertyValueKind";
    private const string RuntimeAkcssOperationPriority = "global::Akbura.CompilerAnotations.AkcssOperationPriority";
    private const string RuntimeMetadataTargetName = "__target";
    private const string RuntimeMetadataArgumentsName = "__arguments";

    public static string GetHintName(
        IAkcssModuleSymbol symbol,
        string sourcePath,
        string? moduleIdentity = null)
    {
        if (symbol == null)
        {
            throw new ArgumentNullException(nameof(symbol));
        }

        var identity = GetModuleIdentity(symbol, moduleIdentity ?? sourcePath);
        return $"Akbura.Akcss.{SanitizeHintPart(identity)}.{AkcssGeneratedModuleNames.GetStableHash(identity):x8}.g.cs";
    }

    public static string Generate(
        IAkcssModuleSymbol symbol,
        AkcssGenerationSourceMap sourceMap,
        string sourcePath,
        string rootNamespace,
        string? moduleIdentity = null,
        ImmutableArray<CSharpUsingDirectiveSyntax> usingDirectives = default)
    {
        if (symbol == null)
        {
            throw new ArgumentNullException(nameof(symbol));
        }

        if (sourceMap == null)
        {
            throw new ArgumentNullException(nameof(sourceMap));
        }

        var identity = GetModuleIdentity(
            symbol,
            moduleIdentity ?? sourcePath);

        var mappedSourcePath = string.IsNullOrWhiteSpace(sourcePath)
            ? identity
            : AkcssGeneratedModuleNames.NormalizeSourcePath(sourcePath);

        var moduleTypeName =
            AkcssGeneratedModuleNames.GetTypeName(identity);

        var generatedNamespace =
            AkcssGeneratedModuleNames.GetNamespaceName(rootNamespace);

        var source = new StringBuilder();

        source.AppendLine("// <auto-generated />");
        source.AppendLine("#nullable enable");
        if (usingDirectives.IsDefault && symbol.DeclaringSyntax is { } declaringSyntax)
        {
            AppendUsingDirectives(source, declaringSyntax);
        }
        else if (!usingDirectives.IsDefault)
        {
            AppendUsingDirectives(source, usingDirectives);
        }

        source.AppendLine();
        source.Append("[assembly: ")
            .Append(AkcssModuleReferenceAttribute)
            .Append("(typeof(global::")
            .Append(generatedNamespace)
            .Append('.')
            .Append(moduleTypeName)
            .AppendLine("))]");
        source.AppendLine();

        source.Append("namespace ")
            .Append(generatedNamespace)
            .AppendLine();

        source.AppendLine("{");
        AppendHiddenApiAttributes(source, 1);
        AppendIndentedLine(
            source,
            1,
            $"[{AkcssModuleAttribute}({ToStringLiteral(mappedSourcePath)}, " +
            $"MetadataName = {ToStringLiteral(symbol.MetadataName)}, " +
            "FormatVersion = 4)]");
        source.Append("    public static class ").Append(moduleTypeName).AppendLine();
        source.AppendLine("    {");
        AppendHiddenApiAttributes(source, 2);
        source.Append("        public const string MetadataName = ")
            .Append(ToStringLiteral(symbol.MetadataName))
            .AppendLine(";");
        AppendHiddenApiAttributes(source, 2);
        source.Append("        public const string SourcePath = ")
            .Append(ToStringLiteral(mappedSourcePath))
            .AppendLine(";");

        AppendStyleCollection(source, symbol);

        for (var index = 0; index < symbol.AkcssSymbols.Length; index++)
        {
            var akcssSymbol =
                symbol.AkcssSymbols[index];

            source.AppendLine();

            if (akcssSymbol.IsIntercepted)
            {
                AppendInterceptMetadataType(
                    source,
                    symbol,
                    akcssSymbol,
                    index,
                    sourceMap);
            }
            else
            {
                AppendStyleType(
                    source,
                    symbol,
                    akcssSymbol,
                    index,
                    sourceMap);
            }
        }

        source.AppendLine("    }");
        source.AppendLine("}");
        return source.ToString();
    }

    private static void AppendUsingDirectives(StringBuilder source, AkburaSyntax syntax)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        if (syntax is AkcssDocumentSyntax document)
        {
            AppendUsingDirectives(source, document.Members, names);
        }
        else if (syntax is InlineAkcssBlockSyntax inlineBlock)
        {
            AppendUsingDirectives(source, inlineBlock.Members, names);
        }
    }

    private static void AppendUsingDirectives(
        StringBuilder source,
        ImmutableArray<CSharpUsingDirectiveSyntax> usingDirectives)
    {
        foreach (var usingDirective in usingDirectives)
        {
            source.AppendLine(
                usingDirective
                    .NormalizeWhitespace()
                    .ToFullString());
        }
    }

    private static void AppendUsingDirectives(
        StringBuilder source,
        AkburaSyntaxList members,
        HashSet<string> names)
    {
        foreach (var member in members)
        {
            if (member is not AkcssUsingDirectiveSyntax usingDirective)
            {
                continue;
            }

            var name = usingDirective.Name.ToFullString().Trim();
            if (name.Length == 0 ||
                name.EndsWith(".akcss", StringComparison.OrdinalIgnoreCase) ||
                !names.Add(name))
            {
                continue;
            }

            source.Append("using ").Append(name).AppendLine(";");
        }
    }

    private static void AppendStyleCollection(
        StringBuilder source,
        IAkcssModuleSymbol module)
    {
        var expressions = new List<string>(module.AkcssSymbols.Length);
        for (var index = 0; index < module.AkcssSymbols.Length; index++)
        {
            var symbol = module.AkcssSymbols[index];
            if (!symbol.IsIntercepted)
            {
                expressions.Add($"new Style_{index}()");
                continue;
            }

            if (TryGetInterceptorCreation(symbol, out var interceptorCreation))
            {
                expressions.Add(interceptorCreation);
            }
        }

        source.AppendLine();
        AppendHiddenApiAttributes(source, 2);
        source.Append("        public static readonly global::System.Collections.Immutable.ImmutableArray<")
            .Append(RuntimeStyleType)
            .AppendLine("> Styles =");

        if (expressions.Count == 0)
        {
            source.Append("            global::System.Collections.Immutable.ImmutableArray<")
                .Append(RuntimeStyleType)
                .AppendLine(">.Empty;");
            return;
        }

        source.Append("            global::System.Collections.Immutable.ImmutableArray.Create<")
            .Append(RuntimeStyleType)
            .AppendLine(">");
        source.AppendLine("            (");
        for (var index = 0; index < expressions.Count; index++)
        {
            source.Append("                ").Append(expressions[index]);
            source.AppendLine(index == expressions.Count - 1 ? string.Empty : ",");
        }

        source.AppendLine("            );");
    }

    private static bool TryGetInterceptorCreation(
        IAkcssSymbol symbol,
        out string creation)
    {
        creation = string.Empty;
        if (symbol.InterceptType.Symbol is not INamedTypeSymbol type || type.IsAbstract)
        {
            return false;
        }

        foreach (var constructor in type.InstanceConstructors)
        {
            if (constructor.Parameters.Length == 0 &&
                constructor.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal)
            {
                creation = $"new {GetTypeName(type)}()";
                return true;
            }
        }

        return false;
    }

    private static void AppendStyleType(
        StringBuilder source,
        IAkcssModuleSymbol module,
        IAkcssSymbol symbol,
        int index,
        AkcssGenerationSourceMap sourceMap)
    {
        AppendMetadataCarrier(source, module, symbol, index, sourceMap);
        source.AppendLine();

        source.Append("        [").Append(StyleNameAttribute).Append('(')
            .Append(ToStringLiteral(symbol.Name)).AppendLine(")] ");
        if (module.IsInlined)
        {
            source.Append("        [").Append(InlinedStyleAttribute).AppendLine("]");
        }

        AppendObservedPropertyAttributes(source, symbol, sourceMap);
        if (symbol is ITailwindUtilitySymbol utility)
        {
            AppendUtilityType(source, utility, index, sourceMap);
        }
        else
        {
            AppendClassType(source, symbol, index, sourceMap);
        }
    }

    private static void AppendInterceptMetadataType(
        StringBuilder source,
        IAkcssModuleSymbol module,
        IAkcssSymbol symbol,
        int index,
        AkcssGenerationSourceMap sourceMap)
    {
        AppendMetadataCarrier(source, module, symbol, index, sourceMap);
    }

    private static void AppendMetadataCarrier(
        StringBuilder source,
        IAkcssModuleSymbol module,
        IAkcssSymbol symbol,
        int symbolIndex,
        AkcssGenerationSourceMap sourceMap)
    {
        AppendHiddenApiAttributes(source, 2);
        AppendIndentedLine(source, 2, $"[{CompilerGeneratedAttribute}]");
        AppendAkcssSymbolAttribute(source, module, symbol, symbolIndex);

        if (symbol is ITailwindUtilitySymbol utility)
        {
            AppendUtilityParameterAttributes(source, utility);
        }

        AppendObservedPropertyAttributes(source, symbol, sourceMap);
        AppendOperationAttributes(source, symbol, sourceMap);

        source.Append("        public static class __AkcssMetadata_")
            .Append(symbolIndex)
            .AppendLine();
        source.AppendLine("        {");
        source.AppendLine("        }");
    }

    private static void AppendAkcssSymbolAttribute(
        StringBuilder source,
        IAkcssModuleSymbol module,
        IAkcssSymbol symbol,
        int symbolIndex)
    {
        var kind = symbol.IsIntercepted
            ? "Intercept"
            : symbol is ITailwindUtilitySymbol
                ? "Utility"
                : "Style";
        var arguments = new List<string>
        {
            $"Name = {ToStringLiteral(symbol.Name)}",
            $"MetadataName = {ToStringLiteral(symbol.MetadataName)}",
            $"Kind = {AkcssSymbolKind}.{kind}",
            $"RuntimeStyleIndex = {GetRuntimeStyleIndex(module, symbolIndex).ToString(CultureInfo.InvariantCulture)}",
        };

        if (symbol.TargetType.Symbol is ITypeSymbol targetType)
        {
            arguments.Add($"TargetType = typeof({GetTypeName(targetType)})");
        }

        if (symbol.InterceptType.Symbol is ITypeSymbol interceptType)
        {
            arguments.Add($"InterceptType = typeof({GetTypeName(interceptType)})");
        }

        if (symbol.ClassName != null)
        {
            arguments.Add($"ClassName = {ToStringLiteral(symbol.ClassName)}");
        }

        if (HasErrors(symbol.Operations))
        {
            arguments.Add("HasErrors = true");
        }

        AppendIndentedLine(source, 2, $"[{AkcssSymbolAttribute}(");
        for (var index = 0; index < arguments.Count; index++)
        {
            AppendIndentedLine(
                source,
                3,
                arguments[index] + (index == arguments.Count - 1 ? string.Empty : ","));
        }

        AppendIndentedLine(source, 2, ")]");
    }

    private static void AppendUtilityParameterAttributes(
        StringBuilder source,
        ITailwindUtilitySymbol utility)
    {
        foreach (var parameter in utility.Parameters)
        {
            AppendIndentedLine(source, 2, $"[{AkcssUtilityParameterAttribute}(");
            AppendIndentedLine(source, 3, $"Ordinal = {parameter.Ordinal.ToString(CultureInfo.InvariantCulture)},");
            AppendIndentedLine(source, 3, $"Name = {ToStringLiteral(parameter.Name)},");
            AppendIndentedLine(source, 3, $"Type = typeof({GetTypeName(parameter.Type.Symbol)}),");
            AppendIndentedLine(source, 3, $"CSharpName = {ToStringLiteral(GetParameterName(parameter))},");
            AppendIndentedLine(source, 3, $"IsOptional = {(parameter.IsOptional ? "true" : "false")}");
            AppendIndentedLine(source, 2, ")]");
        }
    }

    private static int GetRuntimeStyleIndex(
        IAkcssModuleSymbol module,
        int symbolIndex)
    {
        var runtimeIndex = 0;
        for (var index = 0; index <= symbolIndex; index++)
        {
            var candidate = module.AkcssSymbols[index];
            var isEmitted = !candidate.IsIntercepted ||
                TryGetInterceptorCreation(candidate, out _);
            if (!isEmitted)
            {
                continue;
            }

            if (index == symbolIndex)
            {
                return runtimeIndex;
            }

            runtimeIndex++;
        }

        return -1;
    }

    private static bool HasErrors(ImmutableArray<IAkcssOperation> operations)
    {
        foreach (var operation in operations)
        {
            if (operation.HasErrors ||
                operation is IAkcssIfOperation ifOperation && HasErrors(ifOperation.Operations))
            {
                return true;
            }
        }

        return false;
    }

    private static void AppendObservedPropertyAttributes(
        StringBuilder source,
        IAkcssSymbol symbol,
        AkcssGenerationSourceMap sourceMap)
    {
        var propertyNames = new SortedSet<string>(StringComparer.Ordinal);
        CollectObservedPropertyNames(
            symbol.Operations,
            propertyNames,
            new HashSet<IAkcssSymbol> { sourceMap.GetGenerationSymbol(symbol) },
            sourceMap);

        foreach (var propertyName in propertyNames)
        {
            source.Append("        [").Append(ObservesPropertyAttribute).Append('(')
                .Append(ToStringLiteral(propertyName)).AppendLine(")]");
        }
    }

    private static void AppendOperationAttributes(
        StringBuilder source,
        IAkcssSymbol symbol,
        AkcssGenerationSourceMap sourceMap)
    {
        var context = new AkcssOperationMetadataContext();

        AppendOperationAttributes(
            source,
            symbol.Operations,
            sourceMap,
            context,
            new HashSet<IAkcssSymbol>
            {
                sourceMap.GetGenerationSymbol(symbol),
            },
            CreateDirectMetadataScope(symbol),
            GeneratedAkcssOperationPriority.Style,
            parentOrder: -1,
            depth: 0);
    }

    private static void AppendOperationAttributes(
        StringBuilder source,
        ImmutableArray<IAkcssOperation> operations,
        AkcssGenerationSourceMap sourceMap,
        AkcssOperationMetadataContext context,
        HashSet<IAkcssSymbol> expansionPath,
        AkcssOperationMetadataScope scope,
        GeneratedAkcssOperationPriority priority,
        int parentOrder,
        int depth)
    {
        foreach (var operation in operations)
        {
            switch (operation)
            {
                case IAkcssPropertySetterOperation setter:
                    AppendPropertySetterAttribute(
                        source,
                        setter,
                        sourceMap,
                        context.NextOrder++,
                        parentOrder,
                        depth,
                        scope,
                        priority);
                    break;

                case IAkcssIfOperation ifOperation:
                    AppendIfOperationAttributes(
                        source,
                        ifOperation,
                        sourceMap,
                        context,
                        expansionPath,
                        scope,
                        priority,
                        parentOrder,
                        depth);
                    break;

                case IAkcssApplyOperation applyOperation:
                    AppendApplyOperationAttributes(
                        source,
                        applyOperation,
                        sourceMap,
                        context,
                        expansionPath,
                        scope,
                        priority,
                        parentOrder,
                        depth);
                    break;

                case IAkcssInterceptOperation interceptOperation:
                    AppendInterceptOperationAttribute(
                        source,
                        interceptOperation,
                        sourceMap,
                        context.NextOrder++,
                        parentOrder,
                        depth,
                        scope,
                        priority);
                    break;

                default:
                    Debug.Fail(
                        $"Unsupported AKCSS operation: " +
                        $"{operation.GetType().FullName}");
                    break;
            }
        }
    }

    private static void AppendPropertySetterAttribute(
        StringBuilder source,
        IAkcssPropertySetterOperation operation,
        AkcssGenerationSourceMap sourceMap,
        int order,
        int parentOrder,
        int depth,
        AkcssOperationMetadataScope scope,
        GeneratedAkcssOperationPriority priority)
    {
        var metadata = CreatePropertySetterMetadata(
            operation,
            sourceMap,
            order,
            parentOrder,
            depth,
            scope,
            priority);

        AppendOperationAttribute(
            source,
            metadata);
    }

    private static void AppendOperationAttribute(
        StringBuilder source,
        AkcssOperationMetadata metadata)
    {
        var arguments = new List<string>
        {
            $"Order = {metadata.Order.ToString(CultureInfo.InvariantCulture)}",

            $"Kind = {RuntimeAkcssOperationKind}.{metadata.Kind}",

            $"Origin = {RuntimeAkcssOperationOriginKind}.{metadata.Origin}",

            $"PropertyAccessKind = " +
            $"{RuntimeAkcssPropertyAccessKind}.{metadata.PropertyAccessKind}",

            $"ValueKind = " +
            $"{RuntimeAkcssPropertyValueKind}.{metadata.ValueKind}",

            $"Priority = " +
            $"{RuntimeAkcssOperationPriority}.{metadata.Priority}",
        };

        if (metadata.ParentOrder >= 0)
        {
            arguments.Add(
                $"ParentOrder = " +
                metadata.ParentOrder.ToString(
                    CultureInfo.InvariantCulture));
        }

        if (metadata.Depth != 0)
        {
            arguments.Add(
                $"Depth = " +
                metadata.Depth.ToString(
                    CultureInfo.InvariantCulture));
        }

        if (metadata.IfStartOrder >= 0)
        {
            arguments.Add(
                $"IfStartOrder = " +
                metadata.IfStartOrder.ToString(
                    CultureInfo.InvariantCulture));
        }

        if (metadata.IfEndOrder >= 0)
        {
            arguments.Add(
                $"IfEndOrder = " +
                metadata.IfEndOrder.ToString(
                    CultureInfo.InvariantCulture));
        }

        if (metadata.TargetType != null)
        {
            arguments.Add(
                $"TargetType = typeof(" +
                $"{GetTypeName(metadata.TargetType)})");
        }

        if (metadata.Property != null)
        {
            arguments.Add(
                $"Property = {ToStringLiteral(metadata.Property)}");
        }

        if (metadata.AvaloniaProperty != null)
        {
            arguments.Add(
                $"AvaloniaProperty = " +
                ToStringLiteral(metadata.AvaloniaProperty));
        }

        if (metadata.AttachedGetter != null)
        {
            arguments.Add(
                $"AttachedGetter = " +
                ToStringLiteral(metadata.AttachedGetter));
        }

        if (metadata.AttachedSetter != null)
        {
            arguments.Add(
                $"AttachedSetter = " +
                ToStringLiteral(metadata.AttachedSetter));
        }

        if (metadata.PropertyOwnerType != null)
        {
            arguments.Add(
                $"PropertyOwnerType = typeof(" +
                $"{GetTypeName(metadata.PropertyOwnerType)})");
        }

        if (metadata.PropertyType != null)
        {
            arguments.Add(
                $"PropertyType = typeof(" +
                $"{GetTypeName(metadata.PropertyType)})");
        }

        if (metadata.AttachedTargetType != null)
        {
            arguments.Add(
                $"AttachedTargetType = typeof(" +
                $"{GetTypeName(metadata.AttachedTargetType)})");
        }

        if (metadata.Kind == GeneratedAkcssOperationKind.Set)
        {
            arguments.Add(
                $"CanRead = " +
                (metadata.CanRead ? "true" : "false"));

            arguments.Add(
                $"CanWrite = " +
                (metadata.CanWrite ? "true" : "false"));
        }

        if (metadata.Expression != null)
        {
            arguments.Add(
                $"Expression = " +
                ToStringLiteral(metadata.Expression));
        }

        if (metadata.ExpressionType != null)
        {
            arguments.Add(
                $"ExpressionType = typeof(" +
                $"{GetTypeName(metadata.ExpressionType)})");
        }

        if (metadata.RequiresBrushConversion)
        {
            arguments.Add(
                "RequiresBrushConversion = true");
        }

        if (metadata.ConstantValue != null)
        {
            arguments.Add(
                $"ConstantValue = " +
                ToStringLiteral(metadata.ConstantValue));
        }

        if (metadata.ConstantValueType != null)
        {
            arguments.Add(
                $"ConstantValueType = typeof(" +
                $"{GetTypeName(metadata.ConstantValueType)})");
        }

        if (metadata.ExpansionStartOrder >= 0)
        {
            arguments.Add(
                $"ExpansionStartOrder = " +
                metadata.ExpansionStartOrder.ToString(
                    CultureInfo.InvariantCulture));
        }

        if (metadata.ExpansionEndOrder >= 0)
        {
            arguments.Add(
                $"ExpansionEndOrder = " +
                metadata.ExpansionEndOrder.ToString(
                    CultureInfo.InvariantCulture));
        }

        if (metadata.ExpandedFromOrder >= 0)
        {
            arguments.Add(
                $"ExpandedFromOrder = " +
                metadata.ExpandedFromOrder.ToString(
                    CultureInfo.InvariantCulture));
        }

        if (metadata.DeclaringSymbol != null)
        {
            arguments.Add(
                $"DeclaringSymbol = " +
                ToStringLiteral(metadata.DeclaringSymbol));
        }

        if (!metadata.ApplyItems.IsDefaultOrEmpty)
        {
            arguments.Add(
                $"ApplyItems = " +
                CreateStringArrayExpression(
                    metadata.ApplyItems));
        }

        if (!metadata.AppliedSymbols.IsDefaultOrEmpty)
        {
            arguments.Add(
                $"AppliedSymbols = " +
                CreateStringArrayExpression(
                    metadata.AppliedSymbols));
        }

        if (metadata.InterceptType != null)
        {
            arguments.Add(
                $"InterceptType = typeof(" +
                $"{GetTypeName(metadata.InterceptType)})");
        }

        if (metadata.HasErrors)
        {
            arguments.Add(
                "HasErrors = true");
        }

        if (metadata.SourcePath != null)
        {
            arguments.Add(
                $"SourcePath = " +
                ToStringLiteral(metadata.SourcePath));
        }

        if (metadata.SourceStart >= 0)
        {
            arguments.Add(
                $"SourceStart = " +
                metadata.SourceStart.ToString(CultureInfo.InvariantCulture));

            arguments.Add(
                $"SourceLength = " +
                metadata.SourceLength.ToString(CultureInfo.InvariantCulture));
        }

        AppendIndentedLine(
            source,
            2,
            $"[{RuntimeAkcssOperationAttribute}(");

        for (var index = 0;
             index < arguments.Count;
             index++)
        {
            var suffix = index == arguments.Count - 1
                ? string.Empty
                : ",";

            AppendIndentedLine(
                source,
                3,
                arguments[index] + suffix);
        }

        AppendIndentedLine(
            source,
            2,
            ")]");
    }

    private static void AppendIfOperationAttributes(
        StringBuilder source,
        IAkcssIfOperation operation,
        AkcssGenerationSourceMap sourceMap,
        AkcssOperationMetadataContext context,
        HashSet<IAkcssSymbol> expansionPath,
        AkcssOperationMetadataScope scope,
        GeneratedAkcssOperationPriority priority,
        int parentOrder,
        int depth)
    {
        var order = context.NextOrder++;

        var childSource = new StringBuilder();
        var firstChildOrder = context.NextOrder;

        AppendOperationAttributes(
            childSource,
            operation.Operations,
            sourceMap,
            context,
            expansionPath,
            scope,
            GeneratedAkcssOperationPriority.StyleTrigger,
            parentOrder: order,
            depth: depth + 1);

        var hasChildren =
            context.NextOrder > firstChildOrder;

        var metadata = CreateIfOperationMetadata(
            operation,
            sourceMap,
            order,
            parentOrder,
            depth,
            scope,
            priority,
            ifStartOrder: hasChildren
                ? firstChildOrder
                : -1,
            ifEndOrder: hasChildren
                ? context.NextOrder - 1
                : -1);

        AppendOperationAttribute(
            source,
            metadata);

        source.Append(childSource);
    }

    private static void AppendApplyOperationAttributes(
        StringBuilder source,
        IAkcssApplyOperation operation,
        AkcssGenerationSourceMap sourceMap,
        AkcssOperationMetadataContext context,
        HashSet<IAkcssSymbol> expansionPath,
        AkcssOperationMetadataScope scope,
        GeneratedAkcssOperationPriority priority,
        int parentOrder,
        int depth)
    {
        var order = context.NextOrder++;

        var childSource = new StringBuilder();
        var firstExpansionOrder = context.NextOrder;
        var hasExpansionErrors = false;

        if (operation is IMetadataAkcssApplyOperation metadataApply)
        {
            foreach (var expandedOperation in metadataApply.ExpandedOperations)
            {
                var declaringSymbol = expandedOperation is IMetadataAkcssOperation metadataChild
                    ? metadataChild.DeclaringSymbolMetadataName
                    : null;
                var expansionScope = new AkcssOperationMetadataScope(
                    GeneratedAkcssOperationOriginKind.ApplyExpansion,
                    expandedFromOrder: order,
                    declaringSymbol,
                    scope.ParameterValues);
                AppendOperationAttributes(
                    childSource,
                    ImmutableArray.Create(expandedOperation),
                    sourceMap,
                    context,
                    expansionPath,
                    expansionScope,
                    priority,
                    parentOrder: order,
                    depth: depth + 1);
            }

            var hasMetadataExpansion = context.NextOrder > firstExpansionOrder;
            AppendOperationAttribute(
                source,
                CreateApplyOperationMetadata(
                    operation,
                    sourceMap,
                    order,
                    parentOrder,
                    depth,
                    scope,
                    priority,
                    expansionStartOrder: hasMetadataExpansion
                        ? firstExpansionOrder
                        : -1,
                    expansionEndOrder: hasMetadataExpansion
                        ? context.NextOrder - 1
                        : -1,
                    hasExpansionErrors: !metadataApply.HasErrors &&
                        !metadataApply.ExpandedOperations.IsEmpty &&
                        !hasMetadataExpansion));
            source.Append(childSource);
            return;
        }

        for (var index = 0;
             index < operation.AppliedSymbols.Length;
             index++)
        {
            var appliedSymbol =
                operation.AppliedSymbols[index];

            var generationSymbol =
                sourceMap.GetGenerationSymbol(appliedSymbol);

            if (!expansionPath.Add(generationSymbol))
            {
                hasExpansionErrors = true;
                continue;
            }

            IReadOnlyDictionary<string, CSharpExpressionSyntax>?
                parameterValues = null;

            if (generationSymbol is
                ITailwindUtilitySymbol
                {
                    Parameters.Length: > 0,
                } utility)
            {
                var item = index < operation.Items.Length
                    ? operation.Items[index]
                    : string.Empty;

                if (!TryCreateApplyMetadataParameterValues(
                        item,
                        utility,
                        operation,
                        scope,
                        out parameterValues))
                {
                    hasExpansionErrors = true;
                    expansionPath.Remove(generationSymbol);
                    continue;
                }
            }

            var expansionScope =
                new AkcssOperationMetadataScope(
                    GeneratedAkcssOperationOriginKind
                        .ApplyExpansion,
                    expandedFromOrder: order,
                    declaringSymbol:
                        generationSymbol.MetadataName,
                    parameterValues);

            AppendOperationAttributes(
                childSource,
                generationSymbol.Operations,
                sourceMap,
                context,
                expansionPath,
                expansionScope,
                priority,
                parentOrder: order,
                depth: depth + 1);

            expansionPath.Remove(generationSymbol);
        }

        var hasExpansion =
            context.NextOrder > firstExpansionOrder;

        var metadata = CreateApplyOperationMetadata(
            operation,
            sourceMap,
            order,
            parentOrder,
            depth,
            scope,
            priority,
            expansionStartOrder: hasExpansion
                ? firstExpansionOrder
                : -1,
            expansionEndOrder: hasExpansion
                ? context.NextOrder - 1
                : -1,
            hasExpansionErrors);

        AppendOperationAttribute(source, metadata);

        source.Append(childSource);
    }

    private static void AppendInterceptOperationAttribute(
        StringBuilder source,
        IAkcssInterceptOperation operation,
        AkcssGenerationSourceMap sourceMap,
        int order,
        int parentOrder,
        int depth,
        AkcssOperationMetadataScope scope,
        GeneratedAkcssOperationPriority priority)
    {
        GetOperationSource(
            operation,
            sourceMap,
            out var sourcePath,
            out var sourceStart,
            out var sourceLength);

        var metadata =
            new AkcssOperationMetadata
            {
                Order = order,
                ParentOrder = parentOrder,
                Depth = depth,

                Kind =
                    GeneratedAkcssOperationKind.Intercept,

                Origin = scope.Origin,

                TargetType = GetAkcssTargetType(
                    operation.ContainingAkcssSymbol),

                PropertyAccessKind =
                    GeneratedAkcssPropertyAccessKind.None,

                ValueKind =
                    GeneratedAkcssPropertyValueKind.None,

                Priority = priority,

                DeclaringSymbol =
                    scope.DeclaringSymbol,

                ExpandedFromOrder =
                    scope.ExpandedFromOrder,

                InterceptType =
                    operation.InterceptType.Symbol
                        as ITypeSymbol,

                HasErrors =
                    operation.HasErrors ||
                    operation.InterceptType.Symbol
                        is not ITypeSymbol,

                SourcePath = sourcePath,
                SourceStart = sourceStart,
                SourceLength = sourceLength,
            };

        AppendOperationAttribute(
            source,
            metadata);
    }

    private static AkcssOperationMetadata CreatePropertySetterMetadata(
        IAkcssPropertySetterOperation operation,
        AkcssGenerationSourceMap sourceMap,
        int order,
        int parentOrder,
        int depth,
        AkcssOperationMetadataScope scope,
        GeneratedAkcssOperationPriority priority)
    {
        var property = operation.Property;

        var generatedValue = GetValueExpression(
            operation,
            RuntimeMetadataTargetName,
            observeDynamicResource: false,
            scope.ParameterValues,
            preserveAmxResources: true);

        GetOperationSource(
            operation,
            sourceMap,
            out var sourcePath,
            out var sourceStart,
            out var sourceLength);

        TryGetConstantMetadata(
            operation,
            out var constantValue,
            out var constantValueType);

        return new AkcssOperationMetadata
        {
            Order = order,
            ParentOrder = parentOrder,
            Depth = depth,

            Kind = GeneratedAkcssOperationKind.Set,
            Origin = scope.Origin,

            DeclaringSymbol = scope.DeclaringSymbol,

            ExpandedFromOrder = scope.ExpandedFromOrder,

            Priority = priority,

            TargetType = GetAkcssTargetType(operation.ContainingAkcssSymbol),

            PropertyAccessKind = GetPropertyAccessKind(property),

            Property = property?.Name,

            AvaloniaProperty = GetAvaloniaPropertyName(property),

            AttachedGetter = GetAttachedGetterName(property),

            AttachedSetter = GetAttachedSetterName(property),

            PropertyOwnerType = GetPropertyOwnerType(property),

            PropertyType = property?.Type.Symbol as ITypeSymbol,

            AttachedTargetType = GetAttachedTargetType(property),

            CanRead = property?.CanRead ?? false,
            CanWrite = property?.CanWrite ?? false,

            ValueKind = GetPropertyValueKind(operation.ValueKind),

            Expression = generatedValue.Expression,

            ExpressionType = operation.ValueType.Symbol as ITypeSymbol,

            RequiresBrushConversion = operation.RequiresBrushConversion,

            ConstantValue = constantValue,
            ConstantValueType = constantValueType,

            HasErrors =
                operation.HasErrors ||
                property == null ||
                !property.CanWrite,

            SourcePath = sourcePath,
            SourceStart = sourceStart,
            SourceLength = sourceLength,
        };
    }

    private static AkcssOperationMetadata CreateIfOperationMetadata(
        IAkcssIfOperation operation,
        AkcssGenerationSourceMap sourceMap,
        int order,
        int parentOrder,
        int depth,
        AkcssOperationMetadataScope scope,
        GeneratedAkcssOperationPriority priority,
        int ifStartOrder,
        int ifEndOrder)
    {
        var condition = operation is IMetadataAkcssOperation metadataOperation
            ? RewriteMetadataExpression(
                metadataOperation.Expression,
                RuntimeMetadataTargetName,
                operation.ContainingAkcssSymbol,
                scope.ParameterValues,
                observeDynamicResource: false,
                preserveAmxResources: true).Expression
            : RewriteExpression(
                operation.ConditionOperation.Syntax as CSharpExpressionSyntax ??
                    operation.Syntax?.Condition.GetRawCSharpExpression(),
                new AmxExpressionRewriter(
                    RuntimeMetadataTargetName,
                    observeDynamicResource: false,
                    GetTargetParameterName(operation.ContainingAkcssSymbol),
                    scope.ParameterValues,
                    preserveResourceInvocations: true),
                operation.ConditionOperation.Operation?.SemanticModel);

        GetOperationSource(
            operation,
            sourceMap,
            out var sourcePath,
            out var sourceStart,
            out var sourceLength);

        return new AkcssOperationMetadata
        {
            Order = order,
            ParentOrder = parentOrder,
            Depth = depth,

            Kind = GeneratedAkcssOperationKind.If,

            Origin = scope.Origin,

            DeclaringSymbol = scope.DeclaringSymbol,

            ExpandedFromOrder = scope.ExpandedFromOrder,

            Priority = priority,

            TargetType = GetAkcssTargetType(operation.ContainingAkcssSymbol),

            PropertyAccessKind =
                GeneratedAkcssPropertyAccessKind.None,

            ValueKind =
                GeneratedAkcssPropertyValueKind.CSharpExpression,

            Expression = condition,

            ExpressionType =
                operation.ConditionType.Symbol as ITypeSymbol,

            IfStartOrder = ifStartOrder,
            IfEndOrder = ifEndOrder,

            HasErrors = operation.HasErrors,

            SourcePath = sourcePath,
            SourceStart = sourceStart,
            SourceLength = sourceLength,
        };
    }

    private static AkcssOperationMetadata CreateApplyOperationMetadata(
        IAkcssApplyOperation operation,
        AkcssGenerationSourceMap sourceMap,
        int order,
        int parentOrder,
        int depth,
        AkcssOperationMetadataScope scope,
        GeneratedAkcssOperationPriority priority,
        int expansionStartOrder,
        int expansionEndOrder,
        bool hasExpansionErrors)
    {
        GetOperationSource(
            operation,
            sourceMap,
            out var sourcePath,
            out var sourceStart,
            out var sourceLength);

        return new AkcssOperationMetadata
        {
            Order = order,
            ParentOrder = parentOrder,
            Depth = depth,

            Kind = GeneratedAkcssOperationKind.Apply,
            Origin = scope.Origin,

            TargetType = GetAkcssTargetType(
                operation.ContainingAkcssSymbol),

            PropertyAccessKind =
                GeneratedAkcssPropertyAccessKind.None,

            ValueKind =
                GeneratedAkcssPropertyValueKind.None,

            Priority = priority,

            DeclaringSymbol = scope.DeclaringSymbol,
            ExpandedFromOrder = scope.ExpandedFromOrder,

            ApplyItems = operation.Items,

            AppliedSymbols = GetAppliedSymbolNames(operation),

            ExpansionStartOrder = expansionStartOrder,
            ExpansionEndOrder = expansionEndOrder,

            HasErrors =
                operation.HasErrors ||
                hasExpansionErrors,

            SourcePath = sourcePath,
            SourceStart = sourceStart,
            SourceLength = sourceLength,
        };
    }

    private static ImmutableArray<string> GetAppliedSymbolNames(IAkcssApplyOperation operation)
    {
        if (operation is IMetadataAkcssApplyOperation metadataApply)
        {
            return metadataApply.AppliedSymbolMetadataNames;
        }

        if (operation.AppliedSymbols.IsDefaultOrEmpty)
        {
            return [];
        }

        var builder = ImmutableArray.CreateBuilder<string>(operation.AppliedSymbols.Length);

        foreach (var symbol in operation.AppliedSymbols)
        {
            builder.Add(symbol.MetadataName);
        }

        return builder.MoveToImmutable();
    }

    private static void GetOperationSource(
        IAkcssOperation operation,
        AkcssGenerationSourceMap sourceMap,
        out string? sourcePath,
        out int sourceStart,
        out int sourceLength)
    {
        if (operation is IMetadataAkcssOperation
            {
                SourcePath: { Length: > 0 } metadataPath,
                SourceSpan: { Length: > 0 } metadataSpan,
            })
        {
            sourcePath = metadataPath;
            sourceStart = metadataSpan.Start;
            sourceLength = metadataSpan.Length;
            return;
        }

        GetOperationSource(
            operation.Syntax,
            sourceMap,
            out sourcePath,
            out sourceStart,
            out sourceLength);
    }

    private static void GetOperationSource(
        AkburaSyntax? syntax,
        AkcssGenerationSourceMap sourceMap,
        out string? sourcePath,
        out int sourceStart,
        out int sourceLength)
    {
        if (syntax != null &&
            sourceMap.TryGetSourceSpan(syntax, out var span, out var path))
        {
            sourcePath = AkcssGeneratedModuleNames.NormalizeSourcePath(path);
            sourceStart = span.Start;
            sourceLength = span.Length;
            return;
        }

        sourcePath = null;
        sourceStart = -1;
        sourceLength = 0;
    }

    private static void TryGetConstantMetadata(
        IAkcssPropertySetterOperation operation,
        out string? value,
        out ITypeSymbol? type)
    {
        object? constant = null;
        var hasConstant = operation.ValueOperation.ConstantValue.HasValue;
        if (hasConstant)
        {
            constant = operation.ValueOperation.ConstantValue.Value;
        }
        else if (operation.ConvertedValue is string or char or bool or
                 byte or sbyte or short or ushort or int or uint or long or ulong or
                 float or double or decimal)
        {
            constant = operation.ConvertedValue;
            hasConstant = true;
        }

        if (!hasConstant || constant == null)
        {
            value = null;
            type = null;
            return;
        }

        value = constant switch
        {
            bool boolean => boolean ? "true" : "false",
            char character => character.ToString(),
            string text => text,
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => constant.ToString(),
        };
        type = operation.ValueType.Symbol as ITypeSymbol;
    }

    private static AkcssOperationMetadataScope CreateDirectMetadataScope(
        IAkcssSymbol symbol)
    {
        if (symbol is not ITailwindUtilitySymbol { Parameters.Length: > 0 } utility)
        {
            return AkcssOperationMetadataScope.Direct;
        }

        var values = new Dictionary<string, CSharpExpressionSyntax>(
            StringComparer.Ordinal);
        foreach (var parameter in utility.Parameters)
        {
            var expression = CSharpSyntaxFactory.ParseExpression(
                $"(({GetMetadataTypeName(parameter.Type.Symbol)})" +
                $"{RuntimeMetadataArgumentsName}[{parameter.Ordinal.ToString(CultureInfo.InvariantCulture)}])");
            values[parameter.Name] = expression;
            if (parameter.CSharpName.Length > 0)
            {
                values[parameter.CSharpName] = expression;
            }
        }

        return new AkcssOperationMetadataScope(
            GeneratedAkcssOperationOriginKind.Direct,
            expandedFromOrder: -1,
            declaringSymbol: null,
            values);
    }

    private static GeneratedAkcssPropertyValueKind GetPropertyValueKind(AkcssPropertyValueKind valueKind)
    {
        return valueKind switch
        {
            AkcssPropertyValueKind.CSharpExpression =>
                GeneratedAkcssPropertyValueKind.CSharpExpression,

            AkcssPropertyValueKind.ColorLiteral =>
                GeneratedAkcssPropertyValueKind.ColorLiteral,

            AkcssPropertyValueKind.ThicknessTuple =>
                GeneratedAkcssPropertyValueKind.ThicknessTuple,

            AkcssPropertyValueKind.AmxInvocation =>
                GeneratedAkcssPropertyValueKind.AmxInvocation,

            AkcssPropertyValueKind.Error =>
                GeneratedAkcssPropertyValueKind.Error,

            _ =>
                GeneratedAkcssPropertyValueKind.None,
        };
    }

    private static ITypeSymbol? GetAkcssTargetType(IAkcssSymbol symbol)
    {
        if (symbol == null)
        {
            throw new ArgumentNullException(nameof(symbol));
        }

        if (!symbol.HasTargetType)
        {
            return null;
        }

        if (symbol.TargetType.Symbol is ITypeSymbol targetType)
        {
            return targetType;
        }

        throw new InvalidOperationException(
            $"AKCSS symbol '{symbol.MetadataName}' has a target symbol " +
            $"that is not a C# type.");
    }

    private static GeneratedAkcssPropertyAccessKind GetPropertyAccessKind(AkburaPropertySymbol? property)
    {
        if (property == null)
        {
            return GeneratedAkcssPropertyAccessKind.None;
        }

        return property.WriteKind switch
        {
            PropertyAccessKind.AvaloniaProperty =>
                GeneratedAkcssPropertyAccessKind.AvaloniaProperty,

            PropertyAccessKind.AttachedAccessor =>
                GeneratedAkcssPropertyAccessKind.AttachedAccessor,

            PropertyAccessKind.ClrProperty =>
                GeneratedAkcssPropertyAccessKind.ClrProperty,

            PropertyAccessKind.Parameter =>
                GeneratedAkcssPropertyAccessKind.Parameter,

            PropertyAccessKind.Command =>
                GeneratedAkcssPropertyAccessKind.Command,

            _ => GeneratedAkcssPropertyAccessKind.None,
        };
    }

    private static string? GetAvaloniaPropertyName(AkburaPropertySymbol? property)
    {
        if (property == null)
        {
            return null;
        }

        var definition = !property.AvaloniaPropertyDefinition.IsDefault
            ? property.AvaloniaPropertyDefinition
            : property.AttachedPropertyDefinition;

        return definition.Symbol is RoslynFieldSymbol field
            ? field.Name
            : null;
    }

    private static ITypeSymbol? GetPropertyOwnerType(AkburaPropertySymbol? property)
    {
        return property?
            .WriteDefinition
            .Symbol?
            .ContainingType;
    }

    private static string? GetAttachedGetterName(AkburaPropertySymbol? property)
    {
        return property?.AttachedGetterDefinition.Symbol
            is RoslynMethodSymbol getter
                ? getter.Name
                : null;
    }

    private static string? GetAttachedSetterName(AkburaPropertySymbol? property)
    {
        return property?.AttachedSetterDefinition.Symbol
            is RoslynMethodSymbol setter
                ? setter.Name
                : null;
    }

    private static void CollectObservedPropertyNames(
        ImmutableArray<IAkcssOperation> operations,
        SortedSet<string> propertyNames,
        HashSet<IAkcssSymbol> expansionPath,
        AkcssGenerationSourceMap sourceMap)
    {
        foreach (var operation in operations)
        {
            switch (operation)
            {
                case IAkcssPropertySetterOperation setter:
                    CollectObservedPropertyNames(
                        setter.ValueOperation.Operation,
                        GetTargetParameterName(setter.ContainingAkcssSymbol),
                        propertyNames);
                    break;

                case IAkcssIfOperation ifOperation:
                    CollectObservedPropertyNames(
                        ifOperation.ConditionOperation.Operation,
                        GetTargetParameterName(ifOperation.ContainingAkcssSymbol),
                        propertyNames);
                    CollectObservedPropertyNames(
                        ifOperation.Operations,
                        propertyNames,
                        expansionPath,
                        sourceMap);
                    break;

                case IAkcssApplyOperation applyOperation:
                    foreach (var appliedSymbol in applyOperation.AppliedSymbols)
                    {
                        var generationSymbol = sourceMap.GetGenerationSymbol(appliedSymbol);
                        if (!expansionPath.Add(generationSymbol))
                        {
                            continue;
                        }

                        if (generationSymbol is IMetadataAkcssSymbol metadataSymbol)
                        {
                            propertyNames.UnionWith(metadataSymbol.ObservedProperties);
                        }

                        CollectObservedPropertyNames(
                            generationSymbol.Operations,
                            propertyNames,
                            expansionPath,
                            sourceMap);
                        expansionPath.Remove(generationSymbol);
                    }

                    break;
            }
        }
    }

    private static void CollectObservedPropertyNames(
        Microsoft.CodeAnalysis.IOperation? operation,
        string targetParameterName,
        SortedSet<string> propertyNames)
    {
        if (operation == null)
        {
            return;
        }

        if (operation is IPropertyReferenceOperation propertyReference &&
            IsTargetReference(propertyReference.Instance, targetParameterName) &&
            HasAvaloniaProperty(propertyReference.Property))
        {
            propertyNames.Add(propertyReference.Property.Name);
        }

        foreach (var child in operation.ChildOperations)
        {
            CollectObservedPropertyNames(child, targetParameterName, propertyNames);
        }
    }

    private static bool IsTargetReference(
        Microsoft.CodeAnalysis.IOperation? operation,
        string targetParameterName)
    {
        while (operation != null)
        {
            switch (operation)
            {
                case IConversionOperation conversion:
                    operation = conversion.Operand;
                    continue;
                case IParenthesizedOperation parenthesized:
                    operation = parenthesized.Operand;
                    continue;
                case IParameterReferenceOperation parameterReference:
                    return string.Equals(
                        parameterReference.Parameter.Name,
                        targetParameterName,
                        StringComparison.Ordinal);
                default:
                    return false;
            }
        }

        return false;
    }

    private static bool HasAvaloniaProperty(RoslynPropertySymbol property)
    {
        var fieldName = property.Name + "Property";
        for (INamedTypeSymbol? type = property.ContainingType;
             type != null;
             type = type.BaseType)
        {
            foreach (var field in type.GetMembers(fieldName))
            {
                if (field is RoslynFieldSymbol { IsStatic: true } propertyField &&
                    IsAvaloniaPropertyType(propertyField.Type))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsAvaloniaPropertyType(ITypeSymbol type)
    {
        for (var current = type as INamedTypeSymbol;
             current != null;
             current = current.BaseType)
        {
            if (current.Name == "AvaloniaProperty" &&
                current.ContainingNamespace.ToDisplayString() == "Avalonia")
            {
                return true;
            }
        }

        return false;
    }

    private static void AppendClassType(
        StringBuilder source,
        IAkcssSymbol symbol,
        int index,
        AkcssGenerationSourceMap sourceMap)
    {
        source.Append("        private sealed class Style_").Append(index)
            .Append(" : ").AppendLine(RuntimeClassType);
        source.AppendLine("        {");
        source.AppendLine("            public override void Update(object __target)");
        source.AppendLine("            {");
        source.AppendLine("                global::System.ArgumentNullException.ThrowIfNull(__target);");
        AppendOperations(
            source,
            symbol.Operations,
            "__target",
            4,
            new HashSet<IAkcssSymbol> { sourceMap.GetGenerationSymbol(symbol) },
            sourceMap);
        source.AppendLine("            }");
        AppendResetMethod(source, symbol, sourceMap);
        source.AppendLine("        }");
    }

    private static void AppendUtilityType(
        StringBuilder source,
        ITailwindUtilitySymbol symbol,
        int index,
        AkcssGenerationSourceMap sourceMap)
    {
        var targetName = GetTargetParameterName(symbol.Parameters);
        var baseType = GetUtilityBaseType(symbol.Parameters);
        source.Append("        private sealed class Style_").Append(index)
            .Append(" : ").AppendLine(baseType);
        source.AppendLine("        {");

        if (symbol.Parameters.Length <= 16)
        {
            source.Append("            public override void Update(object ").Append(targetName);
            for (var parameterIndex = 0; parameterIndex < symbol.Parameters.Length; parameterIndex++)
            {
                var parameter = symbol.Parameters[parameterIndex];
                source.Append(", ").Append(GetTypeName(parameter.Type.Symbol))
                    .Append(' ').Append(GetParameterName(parameter));
            }

            source.AppendLine(")");
            source.AppendLine("            {");
        }
        else
        {
            AppendUntypedUtilityMembers(source, symbol, targetName);
        }

        source.Append("                global::System.ArgumentNullException.ThrowIfNull(")
            .Append(targetName).AppendLine(");");
        AppendOperations(
            source,
            symbol.Operations,
            targetName,
            4,
            new HashSet<IAkcssSymbol> { sourceMap.GetGenerationSymbol(symbol) },
            sourceMap);
        source.AppendLine("            }");
        AppendResetMethod(source, symbol, sourceMap);
        source.AppendLine("        }");
    }

    private static string GetUtilityBaseType(
        ImmutableArray<ITailwindUtilityParameterSymbol> parameters)
    {
        if (parameters.Length == 0)
        {
            return RuntimeZeroUtilityType;
        }

        if (parameters.Length > 16)
        {
            return RuntimeUtilityType;
        }

        var result = new StringBuilder(RuntimeUtilityType).Append('<');
        for (var index = 0; index < parameters.Length; index++)
        {
            if (index > 0)
            {
                result.Append(", ");
            }

            result.Append(GetTypeName(parameters[index].Type.Symbol));
        }

        return result.Append('>').ToString();
    }

    private static void AppendUntypedUtilityMembers(
        StringBuilder source,
        ITailwindUtilitySymbol symbol,
        string targetName)
    {
        source.AppendLine("            public override global::System.Collections.Immutable.ImmutableArray<global::System.Type> Parameters =>");
        source.AppendLine("                global::System.Collections.Immutable.ImmutableArray.Create<global::System.Type>");
        source.AppendLine("                (");
        for (var index = 0; index < symbol.Parameters.Length; index++)
        {
            source.Append("                    typeof(")
                .Append(GetTypeName(symbol.Parameters[index].Type.Symbol)).Append(')');
            source.AppendLine(index == symbol.Parameters.Length - 1 ? string.Empty : ",");
        }

        source.AppendLine("                );");
        source.AppendLine();
        source.Append("            public override void Update(object ").Append(targetName)
            .AppendLine(", params object[] __parameters)");
        source.AppendLine("            {");
        for (var index = 0; index < symbol.Parameters.Length; index++)
        {
            source.Append("                var ").Append(GetParameterName(symbol.Parameters[index]))
                .Append(" = (").Append(GetTypeName(symbol.Parameters[index].Type.Symbol))
                .Append(")__parameters[").Append(index).AppendLine("]; ");
        }

    }

    private static void AppendOperations(
        StringBuilder source,
        ImmutableArray<IAkcssOperation> operations,
        string targetName,
        int indentation,
        HashSet<IAkcssSymbol> expansionPath,
        AkcssGenerationSourceMap sourceMap)
    {
        foreach (var operation in operations)
        {
            if (operation.HasErrors)
            {
                AppendIndentedLine(source, indentation, "// The invalid AKCSS operation was not emitted.");
                continue;
            }

            switch (operation)
            {
                case IAkcssPropertySetterOperation setter:
                    AppendPropertySetter(source, setter, targetName, indentation, sourceMap);
                    break;
                case IAkcssIfOperation ifOperation:
                    var condition = ifOperation is IMetadataAkcssOperation metadataIf
                        ? RewriteMetadataExpression(
                            metadataIf.Expression,
                            targetName,
                            ifOperation.ContainingAkcssSymbol,
                            identifierValues: null,
                            observeDynamicResource: false).Expression
                        : RewriteExpression(
                            ifOperation.ConditionOperation.Syntax as CSharpExpressionSyntax ??
                                ifOperation.Syntax?.Condition.GetRawCSharpExpression(),
                            new AmxExpressionRewriter(
                                targetName,
                                observeDynamicResource: false,
                                GetTargetParameterName(ifOperation.ContainingAkcssSymbol)),
                            ifOperation.ConditionOperation.Operation?.SemanticModel);
                    AppendIndentedLine(source, indentation, $"if ({condition})");
                    AppendIndentedLine(source, indentation, "{");
                    AppendOperations(
                        source,
                        ifOperation.Operations,
                        targetName,
                        indentation + 1,
                        expansionPath,
                        sourceMap);
                    AppendIndentedLine(source, indentation, "}");
                    break;
                case IAkcssApplyOperation applyOperation:
                    AppendAppliedOperations(
                        source,
                        applyOperation,
                        targetName,
                        indentation,
                        expansionPath,
                        sourceMap);
                    break;
            }
        }
    }

    private static void AppendAppliedOperations(
        StringBuilder source,
        IAkcssApplyOperation operation,
        string targetName,
        int indentation,
        HashSet<IAkcssSymbol> expansionPath,
        AkcssGenerationSourceMap sourceMap)
    {
        if (operation is IMetadataAkcssApplyOperation metadataApply)
        {
            AppendOperations(
                source,
                metadataApply.ExpandedOperations,
                targetName,
                indentation,
                expansionPath,
                sourceMap);
            return;
        }

        for (var index = 0; index < operation.AppliedSymbols.Length; index++)
        {
            var appliedSymbol = operation.AppliedSymbols[index];
            var generationSymbol = sourceMap.GetGenerationSymbol(appliedSymbol);

            if (!expansionPath.Add(generationSymbol))
            {
                AppendIndentedLine(source, indentation, "// Cyclic @apply was ignored.");
                continue;
            }

            if (generationSymbol is ITailwindUtilitySymbol { Parameters.Length: > 0 } utility)
            {
                var item = index < operation.Items.Length
                    ? operation.Items[index]
                    : string.Empty;
                if (!TryCreateApplyArgumentExpressions(
                        item,
                        utility,
                        operation.ContainingAkcssSymbol,
                        out var arguments))
                {
                    AppendIndentedLine(
                        source,
                        indentation,
                        "// The parameterized @apply arguments could not be emitted.");
                    expansionPath.Remove(generationSymbol);
                    continue;
                }

                AppendIndentedLine(source, indentation, "{");
                for (var parameterIndex = 0;
                     parameterIndex < utility.Parameters.Length;
                     parameterIndex++)
                {
                    var parameter = utility.Parameters[parameterIndex];
                    AppendIndentedLine(
                        source,
                        indentation + 1,
                        $"{GetTypeName(parameter.Type.Symbol)} {GetParameterName(parameter)} = {arguments[parameterIndex]};");
                }

                AppendOperations(
                    source,
                    generationSymbol.Operations,
                    targetName,
                    indentation + 1,
                    expansionPath,
                    sourceMap);
                AppendIndentedLine(source, indentation, "}");
            }
            else
            {
                AppendOperations(
                    source,
                    generationSymbol.Operations,
                    targetName,
                    indentation,
                    expansionPath,
                    sourceMap);
            }

            expansionPath.Remove(generationSymbol);
        }
    }

    private static bool TryCreateApplyArgumentExpressions(
        string item,
        ITailwindUtilitySymbol utility,
        IAkcssSymbol containingSymbol,
        out ImmutableArray<string> arguments)
    {
        arguments = ImmutableArray<string>.Empty;
        var prefix = utility.Name + "-";
        if (!item.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var argumentTexts = item[prefix.Length..].Split('-');
        if (argumentTexts.Length != utility.Parameters.Length)
        {
            return false;
        }

        var builder = ImmutableArray.CreateBuilder<string>(argumentTexts.Length);
        for (var index = 0; index < argumentTexts.Length; index++)
        {
            var argumentText = argumentTexts[index];
            if (argumentText.Length == 0 ||
                !TryCreateApplyArgumentExpression(
                    argumentText,
                    utility.Parameters[index].Type.Symbol,
                    containingSymbol,
                    out var expression))
            {
                return false;
            }

            builder.Add(expression);
        }

        arguments = builder.MoveToImmutable();
        return true;
    }

    private static bool TryCreateApplyArgumentExpression(
        string text,
        Microsoft.CodeAnalysis.ISymbol? parameterTypeSymbol,
        IAkcssSymbol containingSymbol,
        out string expression)
    {
        if (containingSymbol is ITailwindUtilitySymbol containingUtility)
        {
            foreach (var parameter in containingUtility.Parameters)
            {
                if (string.Equals(parameter.Name, text, StringComparison.Ordinal) ||
                    string.Equals(parameter.CSharpParameter?.Name, text, StringComparison.Ordinal))
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
            foreach (var member in enumType.GetMembers())
            {
                if (member is RoslynFieldSymbol field &&
                    field.HasConstantValue &&
                    string.Equals(field.Name, text, StringComparison.OrdinalIgnoreCase))
                {
                    expression =
                        $"{GetTypeName(enumType)}.{EscapeIdentifier(field.Name)}";
                    return true;
                }
            }

            expression = string.Empty;
            return false;
        }

        switch (parameterType?.SpecialType)
        {
            case SpecialType.System_String:
                expression = ToStringLiteral(text);
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
                if (uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var uintValue))
                {
                    expression = uintValue.ToString(CultureInfo.InvariantCulture) + "u";
                    return true;
                }

                break;
            case SpecialType.System_Int64:
                if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longValue))
                {
                    expression = longValue.ToString(CultureInfo.InvariantCulture) + "L";
                    return true;
                }

                break;
            case SpecialType.System_UInt64:
                if (ulong.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ulongValue))
                {
                    expression = ulongValue.ToString(CultureInfo.InvariantCulture) + "UL";
                    return true;
                }

                break;
            case SpecialType.System_Single:
                if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var floatValue))
                {
                    expression = floatValue.ToString("R", CultureInfo.InvariantCulture) + "f";
                    return true;
                }

                break;
            case SpecialType.System_Double:
                if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue))
                {
                    expression = FormatDouble(doubleValue);
                    return true;
                }

                break;
            case SpecialType.System_Decimal:
                if (decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var decimalValue))
                {
                    expression = decimalValue.ToString(CultureInfo.InvariantCulture) + "m";
                    return true;
                }

                break;
            case SpecialType.System_Object:
                if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                {
                    expression = text;
                    return true;
                }

                if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var objectDouble))
                {
                    expression = FormatDouble(objectDouble);
                    return true;
                }

                if (bool.TryParse(text, out var objectBoolean))
                {
                    expression = objectBoolean ? "true" : "false";
                    return true;
                }

                expression = ToStringLiteral(text);
                return true;
            default:
                if (parameterType == null)
                {
                    expression = text;
                    return true;
                }

                break;
        }

        expression = text;
        return expression.Length > 0;
    }

    private static bool TryCreateApplyMetadataParameterValues(
        string item,
        ITailwindUtilitySymbol utility,
        IAkcssApplyOperation operation,
        AkcssOperationMetadataScope currentScope,
        out IReadOnlyDictionary<string, CSharpExpressionSyntax>? parameterValues)
    {
        parameterValues = null;

        if (utility.Parameters.Length == 0)
        {
            return true;
        }

        if (!TryCreateApplyArgumentExpressions(
                item,
                utility,
                operation.ContainingAkcssSymbol,
                out var arguments))
        {
            return false;
        }

        var values =
            new Dictionary<string, CSharpExpressionSyntax>(
                StringComparer.Ordinal);

        for (var index = 0;
             index < utility.Parameters.Length;
             index++)
        {
            var parameter = utility.Parameters[index];

            var expression =
                CSharpSyntaxFactory.ParseExpression(
                    arguments[index]);

            if (currentScope.ParameterValues != null)
            {
                var rewriter = new AmxExpressionRewriter(
                    RuntimeMetadataTargetName,
                    observeDynamicResource: false,
                    GetTargetParameterName(
                        operation.ContainingAkcssSymbol),
                    currentScope.ParameterValues);

                expression =
                    rewriter.Visit(expression)
                        as CSharpExpressionSyntax ??
                    expression;
            }

            values[parameter.Name] = expression;

            if (parameter.CSharpParameter?.Name is
                { Length: > 0 } csharpName)
            {
                values[csharpName] = expression;
            }
        }

        parameterValues = values;
        return true;
    }

    private static void AppendPropertySetter(
        StringBuilder source,
        IAkcssPropertySetterOperation operation,
        string targetName,
        int indentation,
        AkcssGenerationSourceMap sourceMap)
    {
        if (operation.Property is not { } property || !property.CanWrite)
        {
            AppendIndentedLine(source, indentation, "// The unresolved AKCSS property was not emitted.");
            return;
        }

        var value = GetValueExpression(operation, targetName, observeDynamicResource: true);
        GeneratedStatement? statement = null;
        if (value.DynamicResource is { } dynamicResource)
        {
            statement = CreateDynamicResourceAssignment(
                property,
                targetName,
                value.Expression,
                dynamicResource);
        }

        if (statement == null)
        {
            value = GetValueExpression(operation, targetName, observeDynamicResource: false);
        }

        statement ??= property.WriteKind switch
        {
            PropertyAccessKind.ClrProperty => CreateClrPropertyAssignment(property, targetName, value.Expression),
            PropertyAccessKind.AvaloniaProperty => CreateAvaloniaPropertyAssignment(property, targetName, value.Expression),
            PropertyAccessKind.AttachedAccessor => CreateAttachedPropertyAssignment(property, targetName, value.Expression),
            _ => null,
        };

        if (statement == null)
        {
            AppendIndentedLine(
                source,
                indentation,
                "// This AKCSS property write kind is not emitted yet.");
            return;
        }

        AppendTargetCompatibleStatement(
            source,
            indentation,
            targetName,
            statement.Value,
            GetPropertyReceiverType(property),
            value.RequiresResourceHost,
            operation.Syntax?.Expression,
            sourceMap);
    }

    private static void AppendTargetCompatibleStatement(
        StringBuilder source,
        int indentation,
        string targetName,
        GeneratedStatement statement,
        ITypeSymbol? receiverType,
        bool requiresResourceHost,
        AkburaSyntax? syntax,
        AkcssGenerationSourceMap sourceMap)
    {
        var conditions = new List<string>(2);
        if (receiverType is { SpecialType: not SpecialType.System_Object })
        {
            conditions.Add($"{targetName} is {GetTypeName(receiverType)}");
        }

        if (requiresResourceHost)
        {
            conditions.Add($"{targetName} is global::Avalonia.Controls.IResourceHost");
        }

        var statementIndentation = indentation;
        if (conditions.Count > 0)
        {
            AppendIndentedLine(source, indentation, $"if ({string.Join(" && ", conditions)})");
            AppendIndentedLine(source, indentation, "{");
            statementIndentation++;
        }

        LinePositionSpan lineSpan = default;
        var path = string.Empty;
        var hasLineDirective = syntax != null &&
            sourceMap.TryGetLineDirective(
                syntax,
                out lineSpan,
                out path);
        if (hasLineDirective)
        {
            var start = lineSpan.Start;
            var end = lineSpan.End;
            var characterOffset = statementIndentation * 4 + statement.ValueOffset;
            AppendIndentedLine(
                source,
                statementIndentation,
                $"#line ({start.Line + 1},{start.Character + 1})-" +
                $"({end.Line + 1},{end.Character + 1}) {characterOffset} " +
                ToLineDirectivePath(path));
        }

        AppendIndentedLine(source, statementIndentation, statement.Text);
        if (hasLineDirective)
        {
            AppendIndentedLine(source, statementIndentation, "#line default");
        }

        if (conditions.Count > 0)
        {
            AppendIndentedLine(source, indentation, "}");
        }
    }

    private static GeneratedStatement? CreateDynamicResourceAssignment(
        AkburaPropertySymbol property,
        string targetName,
        string value,
        DynamicResourceBinding dynamicResource)
    {
        var propertyReference = GetStaticMemberReference(property.AvaloniaPropertyDefinition.Symbol) ??
                                GetStaticMemberReference(property.AttachedPropertyDefinition.Symbol);
        if (propertyReference == null && property.WriteKind == PropertyAccessKind.AvaloniaProperty)
        {
            propertyReference = GetStaticMemberReference(property.WriteDefinition.Symbol);
        }

        if (propertyReference == null)
        {
            return null;
        }

        var resourceValue = dynamicResource.ValueParameterName;
        var prefix =
            $"TrackSubscription({targetName}, " +
            $"((global::Avalonia.AvaloniaObject){targetName}).Bind(" +
            $"{propertyReference}, " +
            $"global::Avalonia.Controls.ResourceNodeExtensions.GetResourceObservable(" +
            $"(global::Avalonia.Controls.IResourceHost){targetName}, " +
            $"{dynamicResource.KeyExpression}, converter: " +
            $"{resourceValue} => global::System.Object.ReferenceEquals({resourceValue}, global::Avalonia.AvaloniaProperty.UnsetValue) " +
            $"? global::Avalonia.AvaloniaProperty.UnsetValue : (object?)(";
        return new GeneratedStatement(prefix + value + "))));", prefix.Length);
    }

    private static GeneratedStatement? CreateClrPropertyAssignment(
        AkburaPropertySymbol property,
        string targetName,
        string value)
    {
        if (property.WriteDefinition.Symbol is not RoslynPropertySymbol clrProperty)
        {
            return null;
        }

        var prefix =
            $"(({GetTypeName(clrProperty.ContainingType)}){targetName})." +
            $"{EscapeIdentifier(clrProperty.Name)} = ";
        return new GeneratedStatement(prefix + value + ";", prefix.Length);
    }

    private static GeneratedStatement? CreateAvaloniaPropertyAssignment(
        AkburaPropertySymbol property,
        string targetName,
        string value)
    {
        var propertyReference = GetStaticMemberReference(property.WriteDefinition.Symbol) ??
                                GetStaticMemberReference(property.AvaloniaPropertyDefinition.Symbol);
        if (propertyReference == null)
        {
            return null;
        }

        var prefix =
            $"((global::Avalonia.AvaloniaObject){targetName}).SetValue(" +
            $"{propertyReference}, ";
        return new GeneratedStatement(prefix + value + ");", prefix.Length);
    }

    private static GeneratedStatement? CreateAttachedPropertyAssignment(
        AkburaPropertySymbol property,
        string targetName,
        string value)
    {
        var setter = property.WriteDefinition.Symbol as RoslynMethodSymbol ??
                     property.AttachedSetterDefinition.Symbol as RoslynMethodSymbol;
        if (setter == null || setter.Parameters.Length == 0)
        {
            return null;
        }

        var prefix =
            $"{GetMethodReference(setter)}(({GetTypeName(setter.Parameters[0].Type)}){targetName}, ";
        return new GeneratedStatement(prefix + value + ");", prefix.Length);
    }

    private static GeneratedValue GetValueExpression(
        IAkcssPropertySetterOperation operation,
        string targetName,
        bool observeDynamicResource,
        IReadOnlyDictionary<string, CSharpExpressionSyntax>? identifierValues = null,
        bool preserveAmxResources = false)
    {
        if (operation is IMetadataAkcssOperation metadataOperation)
        {
            var metadataValue = RewriteMetadataExpression(
                metadataOperation.Expression,
                targetName,
                operation.ContainingAkcssSymbol,
                identifierValues,
                observeDynamicResource,
                preserveAmxResources);
            if (!operation.RequiresBrushConversion)
            {
                return metadataValue;
            }

            return new GeneratedValue(
                $"new global::Avalonia.Media.SolidColorBrush({metadataValue.Expression})",
                metadataValue.DynamicResource,
                metadataValue.RequiresResourceHost);
        }

        var rewriter = new AmxExpressionRewriter(
            targetName,
            observeDynamicResource,
            GetTargetParameterName(operation.ContainingAkcssSymbol),
            identifierValues,
            preserveResourceInvocations: preserveAmxResources);

        string value = operation.ConvertedValue switch
        {
            AkcssColorValue color =>
                $"global::Avalonia.Media.Color.FromArgb({color.A}, {color.R}, {color.G}, {color.B})",
            AkcssThicknessValue thickness => CreateThicknessExpression(thickness),
            AkcssThicknessExpressionValue thickness => CreateThicknessExpression(
                thickness,
                rewriter,
                operation.ValueOperation.Operation?.SemanticModel),
            CSharpSymbolDefinition definition when GetStaticMemberReference(definition.Symbol) is { } member => member,
            _ => RewriteExpression(
                operation.ValueOperation.Syntax as CSharpExpressionSyntax ??
                    operation.Syntax?.Expression.GetRawCSharpExpression(),
                rewriter,
                operation.ValueOperation.Operation?.SemanticModel),
        };

        if (operation.RequiresBrushConversion)
        {
            value = $"new global::Avalonia.Media.SolidColorBrush({value})";
        }

        return new GeneratedValue(
            value,
            rewriter.DynamicResource,
            rewriter.RequiresResourceHost);
    }

    private static GeneratedValue RewriteMetadataExpression(
        string? expression,
        string targetName,
        IAkcssSymbol containingSymbol,
        IReadOnlyDictionary<string, CSharpExpressionSyntax>? identifierValues,
        bool observeDynamicResource,
        bool preserveAmxResources = false)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return new GeneratedValue(
                "default",
                dynamicResource: null,
                requiresResourceHost: false);
        }

        var syntax = CSharpSyntaxFactory.ParseExpression(expression!);
        syntax = (CSharpExpressionSyntax?)new MetadataExpressionRewriter(
            targetName,
            containingSymbol,
            identifierValues).Visit(syntax) ?? syntax;

        var amxRewriter = new AmxExpressionRewriter(
            targetName,
            observeDynamicResource,
            RuntimeMetadataTargetName,
            preserveResourceInvocations: preserveAmxResources);
        var rewritten = amxRewriter.Visit(syntax)?.ToString() ?? "default";
        var requiresResourceHost =
            amxRewriter.RequiresResourceHost ||
            rewritten.Contains(
                "global::Avalonia.Controls.IResourceHost",
                StringComparison.Ordinal) ||
            rewritten.Contains(
                "global::Avalonia.Controls.ResourceNodeExtensions.",
                StringComparison.Ordinal);
        return new GeneratedValue(
            rewritten,
            amxRewriter.DynamicResource,
            requiresResourceHost);
    }

    private static string RewriteExpression(
        CSharpExpressionSyntax? expression,
        AmxExpressionRewriter rewriter,
        SemanticModel? semanticModel = null)
    {
        if (expression == null)
        {
            return "default";
        }

        if (semanticModel != null &&
            ReferenceEquals(expression.SyntaxTree, semanticModel.SyntaxTree))
        {
            expression = new FullyQualifiedExpressionRewriter(semanticModel)
                .Visit(expression) as CSharpExpressionSyntax ?? expression;
        }

        return rewriter.Visit(expression)?.ToString() ?? "default";
    }

    private static string CreateThicknessExpression(AkcssThicknessValue value)
    {
        return $"new global::Avalonia.Thickness({FormatDouble(value.Left)}, {FormatDouble(value.Top)}, {FormatDouble(value.Right)}, {FormatDouble(value.Bottom)})";
    }

    private static string CreateThicknessExpression(
        AkcssThicknessExpressionValue value,
        AmxExpressionRewriter rewriter,
        SemanticModel? semanticModel = null)
    {
        return
            $"new global::Avalonia.Thickness(" +
            $"{RewriteExpression(value.Left, rewriter, semanticModel)}, " +
            $"{RewriteExpression(value.Top, rewriter, semanticModel)}, " +
            $"{RewriteExpression(value.Right, rewriter, semanticModel)}, " +
            $"{RewriteExpression(value.Bottom, rewriter, semanticModel)})";
    }

    private static void AppendResetMethod(
        StringBuilder source,
        IAkcssSymbol symbol,
        AkcssGenerationSourceMap sourceMap)
    {
        var propertyReferences = new Dictionary<string, ITypeSymbol?>(StringComparer.Ordinal);
        CollectResetProperties(
            symbol.Operations,
            propertyReferences,
            new HashSet<IAkcssSymbol> { sourceMap.GetGenerationSymbol(symbol) },
            sourceMap);
        if (propertyReferences.Count == 0)
        {
            return;
        }

        source.AppendLine();
        source.AppendLine("            public override void Reset(object __target)");
        source.AppendLine("            {");
        source.AppendLine("                global::System.ArgumentNullException.ThrowIfNull(__target);");
        source.AppendLine("                base.Reset(__target);");
        foreach (var property in propertyReferences)
        {
            var propertyReference = property.Key;
            var receiverType = property.Value;
            source.Append("                if (__target is global::Avalonia.AvaloniaObject");
            if (receiverType is { SpecialType: not SpecialType.System_Object })
            {
                source.Append(" && __target is ").Append(GetTypeName(receiverType));
            }

            source.AppendLine(")");
            source.AppendLine("                {");
            source.Append("                    ((global::Avalonia.AvaloniaObject)__target).ClearValue(")
                .Append(propertyReference).AppendLine(");");
            source.AppendLine("                }");
        }

        source.AppendLine("            }");
    }

    private static void CollectResetProperties(
        ImmutableArray<IAkcssOperation> operations,
        Dictionary<string, ITypeSymbol?> properties,
        HashSet<IAkcssSymbol> expansionPath,
        AkcssGenerationSourceMap sourceMap)
    {
        foreach (var operation in operations)
        {
            switch (operation)
            {
                case IAkcssPropertySetterOperation { Property: { } property }:
                    var propertyReference = GetStaticMemberReference(property.AvaloniaPropertyDefinition.Symbol) ??
                                            GetStaticMemberReference(property.AttachedPropertyDefinition.Symbol);
                    if (propertyReference != null && !properties.ContainsKey(propertyReference))
                    {
                        properties.Add(propertyReference, GetPropertyReceiverType(property));
                    }

                    break;
                case IAkcssIfOperation ifOperation:
                    CollectResetProperties(
                        ifOperation.Operations,
                        properties,
                        expansionPath,
                        sourceMap);
                    break;
                case IAkcssApplyOperation applyOperation:
                    foreach (var appliedSymbol in applyOperation.AppliedSymbols)
                    {
                        var generationSymbol = sourceMap.GetGenerationSymbol(appliedSymbol);
                        if (!expansionPath.Add(generationSymbol))
                        {
                            continue;
                        }

                        CollectResetProperties(
                            generationSymbol.Operations,
                            properties,
                            expansionPath,
                            sourceMap);
                        expansionPath.Remove(generationSymbol);
                    }

                    break;
            }
        }
    }

    private static ITypeSymbol? GetPropertyReceiverType(AkburaPropertySymbol property)
    {
        return property.WriteKind switch
        {
            PropertyAccessKind.ClrProperty =>
                (property.WriteDefinition.Symbol as RoslynPropertySymbol)?.ContainingType,
            PropertyAccessKind.AvaloniaProperty =>
                property.WriteDefinition.Symbol?.ContainingType ??
                property.AvaloniaPropertyDefinition.Symbol?.ContainingType ??
                property.AttachedPropertyDefinition.Symbol?.ContainingType,
            PropertyAccessKind.AttachedAccessor => GetAttachedTargetType(property),
            _ => null,
        };
    }

    private static ITypeSymbol? GetAttachedTargetType(AkburaPropertySymbol? property)
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

    private static string? GetStaticMemberReference(Microsoft.CodeAnalysis.ISymbol? symbol)
    {
        return symbol switch
        {
            RoslynFieldSymbol { IsStatic: true } field =>
                $"{GetTypeName(field.ContainingType)}.{EscapeIdentifier(field.Name)}",
            RoslynPropertySymbol { IsStatic: true } property =>
                $"{GetTypeName(property.ContainingType)}.{EscapeIdentifier(property.Name)}",
            _ => null,
        };
    }

    private static string GetMethodReference(RoslynMethodSymbol method)
    {
        return $"{GetTypeName(method.ContainingType)}.{EscapeIdentifier(method.Name)}";
    }

    private static string GetTypeName(Microsoft.CodeAnalysis.ISymbol? symbol)
    {
        return symbol is ITypeSymbol type
            ? type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            : "global::System.Object";
    }

    private static string GetTargetParameterName(
        ImmutableArray<ITailwindUtilityParameterSymbol> parameters)
    {
        var name = "__target";
        var hasConflict = true;
        while (hasConflict)
        {
            hasConflict = false;
            foreach (var parameter in parameters)
            {
                if (string.Equals(parameter.Name, name, StringComparison.Ordinal) ||
                    string.Equals(parameter.CSharpParameter?.Name, name, StringComparison.Ordinal))
                {
                    name += "_";
                    hasConflict = true;
                    break;
                }
            }
        }

        return name;
    }

    private static string GetTargetParameterName(IAkcssSymbol symbol)
    {
        return symbol is ITailwindUtilitySymbol utility
            ? GetTargetParameterName(utility.Parameters)
            : "__target";
    }

    private static string GetParameterName(ITailwindUtilityParameterSymbol parameter)
    {
        var name = parameter.CSharpName;
        return CSharpSyntaxFacts.IsValidIdentifier(name)
            ? EscapeIdentifier(name)
            : $"parameter{parameter.Ordinal}";
    }

    private static string EscapeIdentifier(string name)
    {
        return CSharpSyntaxFacts.GetKeywordKind(name) != CSharpSyntaxKind.None ||
               CSharpSyntaxFacts.GetContextualKeywordKind(name) != CSharpSyntaxKind.None
            ? "@" + name
            : name;
    }

    private static string FormatDouble(double value)
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

    private static string GetModuleIdentity(
        IAkcssModuleSymbol symbol,
        string sourcePath)
    {
        if (!string.IsNullOrWhiteSpace(sourcePath))
        {
            return AkcssGeneratedModuleNames.NormalizeSourcePath(sourcePath);
        }

        return string.IsNullOrWhiteSpace(symbol.Path)
            ? symbol.MetadataName
            : AkcssGeneratedModuleNames.NormalizeSourcePath(symbol.Path!);
    }

    private static void AppendHiddenApiAttributes(StringBuilder source, int indentation)
    {
        AppendIndentedLine(
            source,
            indentation,
            $"[{EditorBrowsableAttribute}(global::System.ComponentModel.EditorBrowsableState.Never)]");
        AppendIndentedLine(source, indentation, $"[{BrowsableAttribute}(false)]");
    }

    private static string SanitizeHintPart(string value)
    {
        var result = new StringBuilder(Math.Min(value.Length, 64));
        foreach (var character in value)
        {
            if (result.Length == 64)
            {
                break;
            }

            result.Append(char.IsLetterOrDigit(character) || character is '.' or '_'
                ? character
                : '_');
        }

        return result.Length == 0 ? "module" : result.ToString();
    }

    private static string CreateStringArrayExpression(ImmutableArray<string> values)
    {
        var result = new StringBuilder(
            "new global::System.String[] { ");

        for (var index = 0;
             index < values.Length;
             index++)
        {
            if (index > 0)
            {
                result.Append(", ");
            }

            result.Append(
                ToStringLiteral(values[index]));
        }

        return result.Append(" }").ToString();
    }

    private static string ToStringLiteral(string value)
    {
        return SymbolDisplay.FormatLiteral(value, quote: true);
    }

    private static string ToLineDirectivePath(string path)
    {
        return "\"" + path + "\"";
    }

    private readonly struct GeneratedValue
    {
        public GeneratedValue(
            string expression,
            DynamicResourceBinding? dynamicResource,
            bool requiresResourceHost)
        {
            Expression = expression;
            DynamicResource = dynamicResource;
            RequiresResourceHost = requiresResourceHost;
        }

        public string Expression { get; }

        public DynamicResourceBinding? DynamicResource { get; }

        public bool RequiresResourceHost { get; }
    }

    private readonly struct GeneratedStatement
    {
        public GeneratedStatement(
            string text,
            int valueOffset)
        {
            Text = text;
            ValueOffset = valueOffset;
        }

        public string Text { get; }

        public int ValueOffset { get; }
    }

    private readonly struct DynamicResourceBinding
    {
        public DynamicResourceBinding(
            string keyExpression,
            string valueParameterName)
        {
            KeyExpression = keyExpression;
            ValueParameterName = valueParameterName;
        }

        public string KeyExpression { get; }

        public string ValueParameterName { get; }
    }

    private sealed class AkcssOperationMetadataContext
    {
        public int NextOrder { get; set; }
    }

    private sealed class AkcssOperationMetadataScope
    {
        public static AkcssOperationMetadataScope Direct { get; } =
            new(
                GeneratedAkcssOperationOriginKind.Direct,
                expandedFromOrder: -1,
                declaringSymbol: null,
                parameterValues: null);

        public AkcssOperationMetadataScope(
            GeneratedAkcssOperationOriginKind origin,
            int expandedFromOrder,
            string? declaringSymbol,
            IReadOnlyDictionary<string, CSharpExpressionSyntax>?
                parameterValues)
        {
            Origin = origin;
            ExpandedFromOrder = expandedFromOrder;
            DeclaringSymbol = declaringSymbol;
            ParameterValues = parameterValues;
        }

        public GeneratedAkcssOperationOriginKind Origin { get; }

        public int ExpandedFromOrder { get; }

        public string? DeclaringSymbol { get; }

        public IReadOnlyDictionary<string, CSharpExpressionSyntax>?
            ParameterValues
        { get; }
    }

    private static string GetMetadataTypeName(Microsoft.CodeAnalysis.ISymbol? symbol)
    {
        return symbol is ITypeSymbol type
            ? type.ToDisplayString(s_metadataTypeDisplayFormat)
            : "global::System.Object";
    }

    private sealed class FullyQualifiedExpressionRewriter : CSharpSyntaxRewriter
    {
        private readonly SemanticModel _semanticModel;

        public FullyQualifiedExpressionRewriter(SemanticModel semanticModel)
        {
            _semanticModel = semanticModel;
        }

        public override Microsoft.CodeAnalysis.SyntaxNode? VisitInvocationExpression(
            CSharpInvocationExpressionSyntax node)
        {
            var method = _semanticModel.GetSymbolInfo(node).Symbol as IMethodSymbol;
            if (method?.ReducedFrom != null &&
                node.Expression is CSharpMemberAccessExpressionSyntax memberAccess)
            {
                var arguments = new List<Microsoft.CodeAnalysis.CSharp.Syntax.ArgumentSyntax>(
                    node.ArgumentList.Arguments.Count + 1)
                {
                    CSharpSyntaxFactory.Argument(
                        (CSharpExpressionSyntax?)Visit(memberAccess.Expression) ??
                        memberAccess.Expression),
                };
                foreach (var argument in node.ArgumentList.Arguments)
                {
                    arguments.Add(
                        (Microsoft.CodeAnalysis.CSharp.Syntax.ArgumentSyntax?)Visit(argument) ??
                        argument);
                }

                var invocation = CSharpSyntaxFactory.InvocationExpression(
                    CSharpSyntaxFactory.ParseExpression(
                        GetMetadataTypeName(method.ContainingType) + "." + method.Name),
                    CSharpSyntaxFactory.ArgumentList(
                        CSharpSyntaxFactory.SeparatedList(arguments)));
                return invocation.WithTriviaFrom(node);
            }

            return base.VisitInvocationExpression(node);
        }

        public override Microsoft.CodeAnalysis.SyntaxNode? VisitMemberAccessExpression(
            CSharpMemberAccessExpressionSyntax node)
        {
            var symbol = _semanticModel.GetSymbolInfo(node).Symbol;
            if (symbol is { IsStatic: true, ContainingType: { } containingType })
            {
                var visitedName = Visit(node.Name)?.WithoutTrivia().ToString() ??
                    node.Name.WithoutTrivia().ToString();
                return CSharpSyntaxFactory.ParseExpression(
                        GetMetadataTypeName(containingType) + "." + visitedName)
                    .WithTriviaFrom(node);
            }

            return base.VisitMemberAccessExpression(node);
        }

        public override Microsoft.CodeAnalysis.SyntaxNode? VisitQualifiedName(
            Microsoft.CodeAnalysis.CSharp.Syntax.QualifiedNameSyntax node)
        {
            if (_semanticModel.GetSymbolInfo(node).Symbol is ITypeSymbol type)
            {
                return CSharpSyntaxFactory.ParseName(GetMetadataTypeName(type))
                    .WithTriviaFrom(node);
            }

            return base.VisitQualifiedName(node);
        }

        public override Microsoft.CodeAnalysis.SyntaxNode? VisitAliasQualifiedName(
            Microsoft.CodeAnalysis.CSharp.Syntax.AliasQualifiedNameSyntax node)
        {
            if (_semanticModel.GetSymbolInfo(node).Symbol is ITypeSymbol type)
            {
                return CSharpSyntaxFactory.ParseName(GetMetadataTypeName(type))
                    .WithTriviaFrom(node);
            }

            return base.VisitAliasQualifiedName(node);
        }

        public override Microsoft.CodeAnalysis.SyntaxNode? VisitIdentifierName(
            CSharpIdentifierNameSyntax node)
        {
            var alias = _semanticModel.GetAliasInfo(node);
            var symbol = alias?.Target ?? _semanticModel.GetSymbolInfo(node).Symbol;
            if (symbol is ITypeSymbol type &&
                node.Parent is not Microsoft.CodeAnalysis.CSharp.Syntax.QualifiedNameSyntax &&
                node.Parent is not Microsoft.CodeAnalysis.CSharp.Syntax.AliasQualifiedNameSyntax)
            {
                return CSharpSyntaxFactory.ParseName(GetMetadataTypeName(type))
                    .WithTriviaFrom(node);
            }

            var isMemberName =
                node.Parent is CSharpMemberAccessExpressionSyntax memberAccess &&
                ReferenceEquals(memberAccess.Name, node);
            if (!isMemberName &&
                symbol is
                {
                    IsStatic: true,
                    ContainingType: { } containingType,
                })
            {
                return CSharpSyntaxFactory.ParseExpression(
                        GetMetadataTypeName(containingType) + "." + symbol.Name)
                    .WithTriviaFrom(node);
            }

            return base.VisitIdentifierName(node);
        }

        public override Microsoft.CodeAnalysis.SyntaxNode? VisitPredefinedType(
            Microsoft.CodeAnalysis.CSharp.Syntax.PredefinedTypeSyntax node)
        {
            return _semanticModel.GetTypeInfo(node).Type is { } type
                ? CSharpSyntaxFactory.ParseTypeName(GetMetadataTypeName(type))
                    .WithTriviaFrom(node)
                : base.VisitPredefinedType(node);
        }
    }

    private sealed class MetadataExpressionRewriter : CSharpSyntaxRewriter
    {
        private readonly string _targetName;
        private readonly ITailwindUtilitySymbol? _utility;
        private readonly IReadOnlyDictionary<string, CSharpExpressionSyntax>?
            _identifierValues;

        public MetadataExpressionRewriter(
            string targetName,
            IAkcssSymbol containingSymbol,
            IReadOnlyDictionary<string, CSharpExpressionSyntax>?
                identifierValues)
        {
            _targetName = targetName;
            _utility = containingSymbol as ITailwindUtilitySymbol;
            _identifierValues = identifierValues;
        }

        public override Microsoft.CodeAnalysis.SyntaxNode? VisitIdentifierName(
            CSharpIdentifierNameSyntax node)
        {
            return string.Equals(
                    node.Identifier.ValueText,
                    RuntimeMetadataTargetName,
                    StringComparison.Ordinal)
                ? CSharpSyntaxFactory.IdentifierName(_targetName)
                    .WithTriviaFrom(node)
                : base.VisitIdentifierName(node);
        }

        public override Microsoft.CodeAnalysis.SyntaxNode? VisitElementAccessExpression(
            Microsoft.CodeAnalysis.CSharp.Syntax.ElementAccessExpressionSyntax node)
        {
            if (_utility == null ||
                node.Expression is not CSharpIdentifierNameSyntax identifier ||
                !string.Equals(
                    identifier.Identifier.ValueText,
                    RuntimeMetadataArgumentsName,
                    StringComparison.Ordinal) ||
                node.ArgumentList.Arguments.Count != 1 ||
                node.ArgumentList.Arguments[0].Expression is not
                    Microsoft.CodeAnalysis.CSharp.Syntax.LiteralExpressionSyntax literal ||
                literal.Token.Value is not int ordinal)
            {
                return base.VisitElementAccessExpression(node);
            }

            foreach (var parameter in _utility.Parameters)
            {
                if (parameter.Ordinal != ordinal)
                {
                    continue;
                }

                if (_identifierValues != null &&
                    (_identifierValues.TryGetValue(parameter.Name, out var value) ||
                     _identifierValues.TryGetValue(parameter.CSharpName, out value)))
                {
                    return value.WithTriviaFrom(node);
                }

                return CSharpSyntaxFactory.IdentifierName(
                        GetParameterName(parameter))
                    .WithTriviaFrom(node);
            }

            return base.VisitElementAccessExpression(node);
        }
    }

    private sealed class AmxExpressionRewriter : CSharpSyntaxRewriter
    {
        private const string ResourceValueParameter = "__resourceValue";
        private readonly string _targetName;
        private readonly bool _observeDynamicResource;
        private readonly string _sourceTargetName;
        private readonly IReadOnlyDictionary<string, CSharpExpressionSyntax>? _identifierValues;
        private readonly bool _preserveResourceInvocations;

        public AmxExpressionRewriter(
            string targetName,
            bool observeDynamicResource,
            string? sourceTargetName = null,
            IReadOnlyDictionary<string, CSharpExpressionSyntax>? identifierValues = null,
            bool preserveResourceInvocations = false)
        {
            _targetName = targetName;
            _observeDynamicResource = observeDynamicResource;
            _sourceTargetName = sourceTargetName ?? targetName;
            _identifierValues = identifierValues;
            _preserveResourceInvocations = preserveResourceInvocations;
        }

        public DynamicResourceBinding? DynamicResource { get; private set; }

        public bool RequiresResourceHost { get; private set; }

        public override Microsoft.CodeAnalysis.SyntaxNode? VisitIdentifierName(CSharpIdentifierNameSyntax node)
        {
            if (_identifierValues != null && _identifierValues.TryGetValue(node.Identifier.ValueText, out var value))
            {
                return value.WithTriviaFrom(node);
            }

            return !string.Equals(
                       _sourceTargetName,
                       _targetName,
                       StringComparison.Ordinal) &&
                   string.Equals(
                       node.Identifier.ValueText,
                       _sourceTargetName,
                       StringComparison.Ordinal)
                ? CSharpSyntaxFactory
                    .IdentifierName(_targetName)
                    .WithTriviaFrom(node)
                : base.VisitIdentifierName(node);
        }

        public override Microsoft.CodeAnalysis.SyntaxNode? VisitInvocationExpression(
            CSharpInvocationExpressionSyntax node)
        {
            if (!TryGetAmxInvocation(node, out var methodName, out var genericName) ||
                node.ArgumentList.Arguments.Count != 1)
            {
                return base.VisitInvocationExpression(node);
            }

            if (_preserveResourceInvocations &&
                methodName is "DynamicResource" or "StaticResource")
            {
                return base.VisitInvocationExpression(node);
            }

            if (methodName is "DynamicResource" or "StaticResource")
            {
                RequiresResourceHost = true;
            }

            var keyExpression = node.ArgumentList.Arguments[0].Expression;
            if (methodName == "DynamicResource" &&
                _observeDynamicResource &&
                DynamicResource == null)
            {
                DynamicResource = new DynamicResourceBinding(
                    keyExpression.ToString(),
                    ResourceValueParameter);
                var resourceValue = CSharpSyntaxFactory.PostfixUnaryExpression(
                    CSharpSyntaxKind.SuppressNullableWarningExpression,
                    CSharpSyntaxFactory.IdentifierName(ResourceValueParameter));
                return CSharpSyntaxFactory.CastExpression(
                        genericName.TypeArgumentList.Arguments[0].WithoutTrivia(),
                        resourceValue)
                    .WithTriviaFrom(node);
            }

            if (methodName is "DynamicResource" or "StaticResource")
            {
                return CreateStaticResourceAccess(node, genericName, keyExpression);
            }

            return base.VisitInvocationExpression(node);
        }

        private CSharpExpressionSyntax CreateStaticResourceAccess(
            CSharpInvocationExpressionSyntax original,
            CSharpGenericNameSyntax genericName,
            CSharpExpressionSyntax keyExpression)
        {
            var findResource = CSharpSyntaxFactory.ParseExpression(
                "global::Avalonia.Controls.ResourceNodeExtensions.FindResource");
            var target = CSharpSyntaxFactory.CastExpression(
                CSharpSyntaxFactory.ParseTypeName("global::Avalonia.Controls.IResourceHost"),
                CSharpSyntaxFactory.IdentifierName(_targetName));
            var invocation = CSharpSyntaxFactory.InvocationExpression(
                findResource,
                CSharpSyntaxFactory.ArgumentList(
                    CSharpSyntaxFactory.SeparatedList(
                    [
                        CSharpSyntaxFactory.Argument(target),
                        CSharpSyntaxFactory.Argument(keyExpression.WithoutTrivia()),
                    ])));
            var resource = CSharpSyntaxFactory.PostfixUnaryExpression(
                CSharpSyntaxKind.SuppressNullableWarningExpression,
                invocation);
            return CSharpSyntaxFactory.CastExpression(
                    genericName.TypeArgumentList.Arguments[0].WithoutTrivia(),
                    resource)
                .WithTriviaFrom(original);
        }

        private static bool TryGetAmxInvocation(
            CSharpInvocationExpressionSyntax invocation,
            out string methodName,
            out CSharpGenericNameSyntax genericName)
        {
            methodName = string.Empty;
            genericName = null!;
            if (invocation.Expression is not CSharpMemberAccessExpressionSyntax
                {
                    Expression: { } receiver,
                    Name: CSharpGenericNameSyntax name,
                } ||
                name.TypeArgumentList.Arguments.Count != 1 ||
                receiver.WithoutTrivia().ToString() is not ("Amx" or "global::Akbura.Amx"))
            {
                return false;
            }

            methodName = name.Identifier.ValueText;
            genericName = name;
            return true;
        }
    }

    private static void AppendIndentedLine(
        StringBuilder source,
        int indentation,
        string text)
    {
        source.Append(' ', indentation * 4).AppendLine(text);
    }
}
