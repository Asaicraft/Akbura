using Akbura.Language.Binder;
using Akbura.Language.BoundTree;
using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Runtime.CompilerServices;
using AkburaSyntaxKind = Akbura.Language.Syntax.SyntaxKind;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;
using CSharpSyntaxFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Akbura.Language;

internal sealed partial class AkburaSemanticModel
{
    internal BoundNode CreateBoundTailwindUtilityAttribute(TailwindAttributeSyntax attribute)
    {
        var diagnosticsBag = new BindingDiagnosticBag();

        var containingComponent = GetContainingMarkupComponentSymbol(attribute);
        var componentName = containingComponent?.Name ?? "<unknown>";
        var utilityName = GetTailwindUtilityName(attribute);
        var arguments = CreateTailwindUtilityArguments(attribute);
        var validatedArguments = arguments;
        var condition = CreateTailwindCondition(attribute);
        ITailwindUtilitySymbol? utility = null;
        {
            using var diagnosticsBuilder = ImmutableArrayBuilder<AkburaSemanticDiagnostic>.Rent();
            utility = ResolveTailwindUtilityForAttribute(
                attribute,
                utilityName,
                arguments,
                containingComponent,
                diagnosticsBuilder,
                out validatedArguments);
            AddTailwindExpressionDiagnostics(attribute, validatedArguments, diagnosticsBuilder);
            diagnosticsBag.AddRange(diagnosticsBuilder.ToImmutable());
        }

        var diagnostics = SetSemanticDiagnostics(attribute, diagnosticsBag);

        return new BoundTailwindUtilityAttribute(
            attribute,
            GetBinder(attribute, BinderUsage.Expression),
            containingComponent,
            utilityName,
            utility,
            validatedArguments,
            condition.HasCondition,
            condition.Text,
            condition.Type,
            condition.Operation,
            diagnostics,
            hasErrors: utility == null || diagnostics.Length > 0 || componentName.Length == 0);
    }

    private ITailwindUtilitySymbol? ResolveTailwindUtilityForAttribute(
        TailwindAttributeSyntax attribute,
        string utilityName,
        ImmutableArray<BoundTailwindUtilityArgument> arguments,
        IMarkupComponentSymbol? containingComponent,
        ImmutableArrayBuilder<AkburaSemanticDiagnostic> diagnosticsBuilder,
        out ImmutableArray<BoundTailwindUtilityArgument> validatedArguments)
    {
        validatedArguments = arguments;
        var localCandidates = FindTailwindUtilityCandidates(
            GetLocalAkcssUtilityDeclarations(diagnosticsBuilder),
            utilityName,
            arguments.Length,
            containingComponent);
        if (localCandidates.Length > 1)
        {
            diagnosticsBuilder.Add(CreateTailwindUtilityAmbiguousDiagnostic(
                attribute,
                utilityName,
                containingComponent));
            return null;
        }

        if (localCandidates.Length == 1)
        {
            return ValidateTailwindUtilityArguments(
                attribute,
                localCandidates[0],
                arguments,
                diagnosticsBuilder,
                out validatedArguments);
        }

        foreach (var importLayer in GetImportedAkcssUtilityDeclarationLayers(diagnosticsBuilder))
        {
            var importCandidates = FindTailwindUtilityCandidates(
                importLayer,
                utilityName,
                arguments.Length,
                containingComponent);
            if (importCandidates.Length > 1)
            {
                diagnosticsBuilder.Add(CreateTailwindUtilityAmbiguousDiagnostic(
                    attribute,
                    utilityName,
                    containingComponent));
                return null;
            }

            if (importCandidates.Length == 1)
            {
                return ValidateTailwindUtilityArguments(
                    attribute,
                    importCandidates[0],
                    arguments,
                    diagnosticsBuilder,
                    out validatedArguments);
            }
        }

        diagnosticsBuilder.Add(CreateTailwindUtilityNotFoundDiagnostic(
            attribute,
            utilityName,
            containingComponent));
        return null;
    }

    private ImmutableArray<ITailwindUtilitySymbol> FindTailwindUtilityCandidates(
        ImmutableArray<AkcssUtilityDeclarationSyntax> declarations,
        string utilityName,
        int argumentCount,
        IMarkupComponentSymbol? containingComponent)
    {
        using var builder = ImmutableArrayBuilder<ITailwindUtilitySymbol>.Rent();
        foreach (var declaration in declarations)
        {
            if (declaration.Selector.Name.Identifier.ValueText != utilityName ||
                declaration.Selector.Parameters.Count != argumentCount)
            {
                continue;
            }

            var symbol = CreateTailwindUtilitySymbol(declaration);
            if (IsTailwindUtilityTargetCompatible(symbol, containingComponent))
            {
                builder.Add(symbol);
            }
        }

        return builder.ToImmutable();
    }

    private ITailwindUtilitySymbol? ValidateTailwindUtilityArguments(
        TailwindAttributeSyntax attribute,
        ITailwindUtilitySymbol utility,
        ImmutableArray<BoundTailwindUtilityArgument> arguments,
        ImmutableArrayBuilder<AkburaSemanticDiagnostic> diagnosticsBuilder,
        out ImmutableArray<BoundTailwindUtilityArgument> validatedArguments)
    {
        validatedArguments = arguments;
        if (utility.Parameters.Length != arguments.Length)
        {
            diagnosticsBuilder.Add(CreateTailwindUtilityArgumentMismatchDiagnostic(
                attribute,
                utility,
                arguments.Length));
            return null;
        }

        using var validatedBuilder = ImmutableArrayBuilder<BoundTailwindUtilityArgument>.Rent(arguments.Length);
        for (var i = 0; i < arguments.Length; i++)
        {
            var argument = arguments[i];
            var argumentType = argument.Type.Symbol as ITypeSymbol;
            var parameterType = utility.Parameters[i].Type.Symbol as ITypeSymbol;
            if (parameterType == null)
            {
                validatedBuilder.Add(argument);
                continue;
            }

            if (TryCreateEnumTailwindUtilityArgument(
                argument,
                parameterType,
                out var enumArgument))
            {
                validatedBuilder.Add(enumArgument);
                continue;
            }

            if (argumentType == null ||
                Compilation.CSharpCompilation.ClassifyConversion(argumentType, parameterType).IsImplicit)
            {
                validatedBuilder.Add(argument);
                continue;
            }

            diagnosticsBuilder.Add(CreateTailwindUtilityArgumentMismatchDiagnostic(
                attribute,
                utility,
                arguments.Length));
            return null;
        }

        validatedArguments = validatedBuilder.ToImmutable();
        return utility;
    }

    private bool TryCreateEnumTailwindUtilityArgument(
        BoundTailwindUtilityArgument argument,
        ITypeSymbol parameterType,
        out BoundTailwindUtilityArgument enumArgument)
    {
        enumArgument = default;
        if (parameterType is not INamedTypeSymbol { TypeKind: TypeKind.Enum } enumType ||
            !TryGetTailwindEnumArgumentMemberName(argument.Syntax, enumType, out var memberName))
        {
            return false;
        }

        var enumMember = enumType
            .GetMembers(memberName)
            .OfType<IFieldSymbol>()
            .FirstOrDefault(static field => field.HasConstantValue);
        if (enumMember == null)
        {
            return false;
        }

        var expressionText =
            enumType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
            "." +
            memberName;
        CSharp.ExpressionSyntax expression;
        try
        {
            expression = CSharpSyntaxFactory.ParseExpression(expressionText);
        }
        catch (ArgumentException)
        {
            expression = null!;
        }

        var binding = expression == null
            ? CSharpBindingResult.Empty
            : BindMarkupAttributeExpression(argument.Syntax, expression);

        enumArgument = new BoundTailwindUtilityArgument(
            argument.Syntax,
            argument.Text,
            new CSharpSymbolDefinition(enumType),
            binding.OperationDefinition,
            enumMember.ConstantValue);
        return true;
    }

    private static bool TryGetTailwindEnumArgumentMemberName(
        TailwindSegmentSyntax syntax,
        INamedTypeSymbol enumType,
        out string memberName)
    {
        switch (syntax.Kind)
        {
            case AkburaSyntaxKind.TailwindIdentifierSegmentSyntax:
                memberName = Unsafe.As<TailwindIdentifierSegmentSyntax>(syntax).Name.Identifier.ValueText;
                return !string.IsNullOrWhiteSpace(memberName);

            case AkburaSyntaxKind.TailwindExpressionSegmentSyntax:
                var expressionSegment = Unsafe.As<TailwindExpressionSegmentSyntax>(syntax);
                try
                {
                    var expression = CSharpSyntaxFactory.ParseExpression(
                        expressionSegment.Expression.Expression.ToFullString());
                    return TryGetExpectedTypeMemberName(expression, enumType, out memberName);
                }
                catch (ArgumentException)
                {
                    memberName = string.Empty;
                    return false;
                }

            default:
                memberName = string.Empty;
                return false;
        }
    }

    private ITailwindUtilitySymbol CreateTailwindUtilitySymbol(
        AkcssUtilityDeclarationSyntax utilityDeclaration)
    {
        if (!TryResolveAkcssTargetType(utilityDeclaration.Selector.TargetType, out var targetType))
        {
            targetType = default;
        }

        var symbol = new TailwindUtilitySymbol(
            utilityDeclaration,
            targetType,
            CreateTailwindUtilityParameters(utilityDeclaration),
            ImmutableArray<IAkcssOperation>.Empty);
        symbol.SetOperations(CreateAkcssOperations(utilityDeclaration.Members, symbol));
        return symbol;
    }

    private bool IsTailwindUtilityTargetCompatible(
        ITailwindUtilitySymbol utility,
        IMarkupComponentSymbol? containingComponent)
    {
        if (!utility.HasTargetType)
        {
            return true;
        }

        return containingComponent?.ComponentType != null &&
            utility.TargetType.Symbol is ITypeSymbol targetType &&
            IsAssignableTo(containingComponent.ComponentType, targetType);
    }

    private ImmutableArray<AkcssUtilityDeclarationSyntax> GetLocalAkcssUtilityDeclarations(
        ImmutableArrayBuilder<AkburaSemanticDiagnostic> diagnosticsBuilder)
    {
        using var builder = ImmutableArrayBuilder<AkcssUtilityDeclarationSyntax>.Rent();
        AddInlineAkcssUtilityDeclarations(builder);

        var companion = GetCompanionAkcssSyntaxTree();
        if (companion != null)
        {
            AddAkcssDocumentUtilityDeclarations(companion.GetRoot(), builder);
        }

        return builder.ToImmutable();
    }

    private ImmutableArray<ImmutableArray<AkcssUtilityDeclarationSyntax>> GetImportedAkcssUtilityDeclarationLayers(
        ImmutableArrayBuilder<AkburaSemanticDiagnostic> diagnosticsBuilder)
    {
        using var layersBuilder = ImmutableArrayBuilder<ImmutableArray<AkcssUtilityDeclarationSyntax>>.Rent();
        foreach (var importName in GetAkcssImportNames())
        {
            var matches = Compilation.AkcssSyntaxTrees
                .Where(tree => string.Equals(tree.LogicalName, importName, StringComparison.Ordinal))
                .ToImmutableArray();
            if (matches.Length == 0)
            {
                diagnosticsBuilder.Add(CreateAkcssImportNotFoundDiagnostic(importName));
                continue;
            }

            using var layerBuilder = ImmutableArrayBuilder<AkcssUtilityDeclarationSyntax>.Rent();
            foreach (var tree in matches)
            {
                AddAkcssDocumentUtilityDeclarations(tree.GetRoot(), layerBuilder);
            }

            layersBuilder.Add(layerBuilder.ToImmutable());
        }

        return layersBuilder.ToImmutable();
    }

    private void AddInlineAkcssUtilityDeclarations(
        ImmutableArrayBuilder<AkcssUtilityDeclarationSyntax> builder)
    {
        foreach (var block in SyntaxTree.GetRoot().Members.OfType<InlineAkcssBlockSyntax>())
        {
            foreach (var section in block.Members.OfType<AkcssUtilitiesSectionSyntax>())
            {
                foreach (var utility in section.Utilities)
                {
                    builder.Add(utility);
                }
            }
        }
    }

    private static void AddAkcssDocumentUtilityDeclarations(
        AkcssDocumentSyntax document,
        ImmutableArrayBuilder<AkcssUtilityDeclarationSyntax> builder)
    {
        foreach (var section in document.Members.OfType<AkcssUtilitiesSectionSyntax>())
        {
            foreach (var utility in section.Utilities)
            {
                builder.Add(utility);
            }
        }
    }

    private AkcssSyntaxTree? GetCompanionAkcssSyntaxTree()
    {
        if (string.IsNullOrWhiteSpace(SyntaxTree.FilePath) ||
            string.IsNullOrWhiteSpace(SyntaxTree.ComponentName))
        {
            return null;
        }

        var directory = Path.GetDirectoryName(SyntaxTree.FilePath);
        var expectedPath = string.IsNullOrWhiteSpace(directory)
            ? SyntaxTree.ComponentName + ".akcss"
            : Path.Combine(directory, SyntaxTree.ComponentName + ".akcss");

        foreach (var tree in Compilation.AkcssSyntaxTrees)
        {
            if (PathsEqual(tree.FilePath, expectedPath))
            {
                return tree;
            }
        }

        return null;
    }

    private ImmutableArray<string> GetAkcssImportNames()
    {
        using var builder = ImmutableArrayBuilder<string>.Rent();
        foreach (var member in SyntaxTree.GetRoot().Members)
        {
            if (member.Kind != AkburaSyntaxKind.UsingDirectiveSyntax)
            {
                continue;
            }

            if (TryGetAkcssImportName(Unsafe.As<UsingDirectiveSyntax>(member), out var importName))
            {
                builder.Add(importName);
            }
        }

        return builder.ToImmutable();
    }

    private static bool IsAkcssUsingDirective(UsingDirectiveSyntax usingDirective)
    {
        return TryGetAkcssImportName(usingDirective, out _);
    }

    private static bool TryGetAkcssImportName(
        UsingDirectiveSyntax usingDirective,
        out string importName)
    {
        importName = string.Empty;
        if (usingDirective.Alias != null ||
            usingDirective.StaticKeyword.RawKind != 0)
        {
            return false;
        }

        var name = usingDirective.Name.ToFullString().Trim();
        if (!name.EndsWith(".akcss", StringComparison.Ordinal))
        {
            return false;
        }

        importName = name;
        return true;
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            StringComparison.OrdinalIgnoreCase);
    }

    internal IMarkupComponentSymbol? GetContainingMarkupComponentSymbol(MarkupAttributeSyntax markupAttribute)
    {
        var markupElement = GetContainingMarkupElement(markupAttribute);
        return markupElement == null
            ? null
            : GetSymbolInfo(markupElement).Symbol as IMarkupComponentSymbol;
    }

    internal static MarkupAttributeValueSyntax? GetMarkupAttributeValue(MarkupAttributeSyntax markupAttribute)
    {
        return markupAttribute.Kind switch
        {
            AkburaSyntaxKind.MarkupPlainAttributeSyntax => Unsafe.As<MarkupPlainAttributeSyntax>(markupAttribute).Value,
            AkburaSyntaxKind.MarkupPrefixedAttributeSyntax => Unsafe.As<MarkupPrefixedAttributeSyntax>(markupAttribute).Value,
            _ => null,
        };
    }

    internal static MarkupAttributeBindingKind GetMarkupAttributeBindingKind(MarkupAttributeSyntax markupAttribute)
    {
        return markupAttribute.Kind == AkburaSyntaxKind.MarkupPrefixedAttributeSyntax
            ? Unsafe.As<MarkupPrefixedAttributeSyntax>(markupAttribute).Prefix.Kind switch
            {
                Akbura.Language.Syntax.SyntaxKind.BindToken => MarkupAttributeBindingKind.Bind,
                Akbura.Language.Syntax.SyntaxKind.OutToken => MarkupAttributeBindingKind.Out,
                _ => MarkupAttributeBindingKind.None,
            }
            : MarkupAttributeBindingKind.None;
    }

    internal static string GetMarkupLiteralAttributeValueText(MarkupLiteralAttributeValueSyntax literalValue)
    {
        var text = (literalValue.Value?.ToFullString() ?? string.Empty).Trim();
        if (text.Length >= 2 &&
            ((text[0] == '"' && text[^1] == '"') ||
             (text[0] == '\'' && text[^1] == '\'')))
        {
            return text[1..^1];
        }

        return text;
    }

    internal static CSharp.ExpressionSyntax? ParseInlineExpression(InlineExpressionSyntax inlineExpression)
    {
        try
        {
            return CSharpSyntaxFactory.ParseExpression(inlineExpression.Expression.ToFullString());
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    internal static void AddMarkupAttributeBindingDiagnostics(
        MarkupAttributeSyntax markupAttribute,
        Symbols.IPropertySymbol property,
        MarkupAttributeBindingKind bindingKind,
        ImmutableArrayBuilder<AkburaSemanticDiagnostic> diagnosticsBuilder)
    {
        if (property.Parameter == null)
        {
            return;
        }

        var parameter = property.Parameter;
        var isAllowed = bindingKind switch
        {
            MarkupAttributeBindingKind.None => parameter.ReceivesValueFromParent,
            MarkupAttributeBindingKind.Bind => parameter.IsTwoWayBinding,
            MarkupAttributeBindingKind.Out => parameter.SendsValueToParent,
            _ => false,
        };

        if (isAllowed)
        {
            return;
        }

        diagnosticsBuilder.Add(CreateMarkupAttributeBindingNotAllowedDiagnostic(
            markupAttribute,
            property,
            bindingKind));
    }

    private static AkburaSemanticDiagnostic CreateMarkupAttributeBindingNotAllowedDiagnostic(
        MarkupAttributeSyntax markupAttribute,
        Symbols.IPropertySymbol property,
        MarkupAttributeBindingKind bindingKind)
    {
        return new AkburaSemanticDiagnostic(
            markupAttribute,
            ErrorCodes.AKBURA_SEMANTIC_MarkupAttributeBindingNotAllowed,
            [GetMarkupAttributeBindingText(bindingKind), property.Name, GetParamBindingText(property.Parameter!.BindingKind)]);
    }

    private static string GetMarkupAttributeBindingText(MarkupAttributeBindingKind bindingKind)
    {
        return bindingKind switch
        {
            MarkupAttributeBindingKind.Bind => "bind",
            MarkupAttributeBindingKind.Out => "out",
            _ => "set",
        };
    }

    private static string GetParamBindingText(ParamBindingKind bindingKind)
    {
        return bindingKind switch
        {
            ParamBindingKind.Bind => "param bind",
            ParamBindingKind.Out => "param out",
            _ => "param",
        };
    }

    internal static void AddDuplicateMarkupPropertySetterDiagnostics(
        MarkupAttributeSyntax markupAttribute,
        Symbols.IPropertySymbol property,
        MarkupAttributeBindingKind bindingKind,
        ImmutableArrayBuilder<AkburaSemanticDiagnostic> diagnosticsBuilder)
    {
        if (!IsMarkupPropertySetter(bindingKind) ||
            GetContainingMarkupStartTag(markupAttribute) is not { } startTag)
        {
            return;
        }

        var propertyName = property.Name;
        foreach (var attribute in startTag.Attributes)
        {
            if (attribute.Position >= markupAttribute.Position)
            {
                break;
            }

            if (GetMarkupPropertyName(attribute) == propertyName &&
                IsMarkupPropertySetter(GetMarkupAttributeBindingKind(attribute)))
            {
                diagnosticsBuilder.Add(new AkburaSemanticDiagnostic(
                    markupAttribute,
                    ErrorCodes.AKBURA_SEMANTIC_MarkupDuplicatePropertySetter,
                    [propertyName]));
                return;
            }
        }
    }

    private static bool IsMarkupPropertySetter(MarkupAttributeBindingKind bindingKind)
    {
        return bindingKind is MarkupAttributeBindingKind.None or MarkupAttributeBindingKind.Bind;
    }

    internal static void AddMarkupEventBindingDiagnostics(
        MarkupAttributeSyntax markupAttribute,
        IRoutedEventSymbol routedEvent,
        MarkupAttributeBindingKind bindingKind,
        ImmutableArrayBuilder<AkburaSemanticDiagnostic> diagnosticsBuilder)
    {
        if (bindingKind == MarkupAttributeBindingKind.None)
        {
            return;
        }

        diagnosticsBuilder.Add(new AkburaSemanticDiagnostic(
            markupAttribute,
            ErrorCodes.AKBURA_SEMANTIC_MarkupEventBindingNotAllowed,
            [routedEvent.Name, GetMarkupAttributeBindingText(bindingKind)]));
    }

    internal void AddMarkupEventHandlerSignatureDiagnostics(
        MarkupAttributeSyntax markupAttribute,
        IRoutedEventSymbol routedEvent,
        CSharp.ExpressionSyntax? expression,
        ImmutableArrayBuilder<AkburaSemanticDiagnostic> diagnosticsBuilder)
    {
        if (expression == null ||
            !TryGetLambdaParameterTypes(expression, out var parameterTypes))
        {
            return;
        }

        var expectedParameterTypes = GetEventHandlerParameterTypes(routedEvent);
        if (expectedParameterTypes.IsDefaultOrEmpty ||
            HasCompatibleEventHandlerParameters(expectedParameterTypes, parameterTypes))
        {
            return;
        }

        diagnosticsBuilder.Add(new AkburaSemanticDiagnostic(
            markupAttribute,
            ErrorCodes.AKBURA_SEMANTIC_MarkupEventHandlerSignatureMismatch,
            [
                routedEvent.Name,
                FormatEventHandlerSignature(expectedParameterTypes),
                FormatEventHandlerSignature(parameterTypes)
            ]));
    }

    private static bool TryGetLambdaParameterTypes(
        CSharp.ExpressionSyntax expression,
        out ImmutableArray<CSharp.TypeSyntax?> parameterTypes)
    {
        using var builder = ImmutableArrayBuilder<CSharp.TypeSyntax?>.Rent();
        switch (expression)
        {
            case CSharp.ParenthesizedLambdaExpressionSyntax lambda:
                foreach (var parameter in lambda.ParameterList.Parameters)
                {
                    builder.Add(parameter.Type);
                }

                parameterTypes = builder.ToImmutable();
                return true;

            case CSharp.SimpleLambdaExpressionSyntax lambda:
                builder.Add(lambda.Parameter.Type);
                parameterTypes = builder.ToImmutable();
                return true;

            case CSharp.AnonymousMethodExpressionSyntax { ParameterList: { } parameterList }:
                foreach (var parameter in parameterList.Parameters)
                {
                    builder.Add(parameter.Type);
                }

                parameterTypes = builder.ToImmutable();
                return true;
        }

        parameterTypes = default;
        return false;
    }

    private static ImmutableArray<ITypeSymbol> GetEventHandlerParameterTypes(
        IRoutedEventSymbol routedEvent)
    {
        if (routedEvent.HandlerType.Symbol is not INamedTypeSymbol { DelegateInvokeMethod: { } invokeMethod })
        {
            return ImmutableArray<ITypeSymbol>.Empty;
        }

        using var builder = ImmutableArrayBuilder<ITypeSymbol>.Rent();
        foreach (var parameter in invokeMethod.Parameters)
        {
            builder.Add(parameter.Type);
        }

        return builder.ToImmutable();
    }

    private bool HasCompatibleEventHandlerParameters(
        ImmutableArray<ITypeSymbol> expectedParameterTypes,
        ImmutableArray<CSharp.TypeSyntax?> actualParameterTypes)
    {
        if (expectedParameterTypes.Length != actualParameterTypes.Length)
        {
            return false;
        }

        for (var index = 0; index < actualParameterTypes.Length; index++)
        {
            var actualParameterType = actualParameterTypes[index];
            if (actualParameterType == null)
            {
                continue;
            }

            var binding = BindCSharpType(actualParameterType);
            if (binding.TypeSymbol == null ||
                !IsSameType(expectedParameterTypes[index], binding.TypeSymbol))
            {
                return false;
            }
        }

        return true;
    }

    private static string FormatEventHandlerSignature(
        ImmutableArray<ITypeSymbol> parameterTypes)
    {
        return "(" +
            string.Join(
                ", ",
                parameterTypes.Select(static type => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))) +
            ")";
    }

    private static string FormatEventHandlerSignature(
        ImmutableArray<CSharp.TypeSyntax?> parameterTypes)
    {
        return "(" +
            string.Join(
                ", ",
                parameterTypes.Select(static type => type?.ToString() ?? "<inferred>")) +
            ")";
    }

    private static MarkupStartTagSyntax? GetContainingMarkupStartTag(MarkupAttributeSyntax markupAttribute)
    {
        for (var node = markupAttribute.Parent; node != null; node = node.Parent)
        {
            if (node.Kind == AkburaSyntaxKind.MarkupStartTagSyntax)
            {
                return Unsafe.As<MarkupStartTagSyntax>(node);
            }
        }

        return null;
    }

    internal void AddMarkupAttributeValueDiagnostics(
        MarkupAttributeSyntax markupAttribute,
        Symbols.IPropertySymbol property,
        CSharpBindingResult binding,
        ImmutableArrayBuilder<AkburaSemanticDiagnostic> diagnosticsBuilder)
    {
        if (property.Type.Symbol is not ITypeSymbol targetType)
        {
            return;
        }

        var conversion = binding.Conversion;
        if (conversion.TargetType != null)
        {
            if (conversion.SourceType != null &&
                IsSameType(conversion.SourceType, targetType))
            {
                return;
            }

            if (conversion.IsImplicit)
            {
                return;
            }

            if (conversion.SourceType != null)
            {
                AddMarkupAttributeCannotConvertDiagnostic(
                    markupAttribute,
                    property,
                    conversion.SourceType,
                    targetType,
                    diagnosticsBuilder);
            }

            return;
        }

        if (binding.TypeSymbol is not ITypeSymbol sourceType ||
            IsSameType(sourceType, targetType) ||
            Compilation.CSharpCompilation.ClassifyConversion(sourceType, targetType).IsImplicit)
        {
            return;
        }

        AddMarkupAttributeCannotConvertDiagnostic(
            markupAttribute,
            property,
            sourceType,
            targetType,
            diagnosticsBuilder);
    }

    private static void AddMarkupAttributeCannotConvertDiagnostic(
        MarkupAttributeSyntax markupAttribute,
        Symbols.IPropertySymbol property,
        ITypeSymbol sourceType,
        ITypeSymbol targetType,
        ImmutableArrayBuilder<AkburaSemanticDiagnostic> diagnosticsBuilder)
    {
        var sourceTypeText = sourceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var targetTypeText = targetType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        diagnosticsBuilder.Add(new AkburaSemanticDiagnostic(
            markupAttribute,
            ErrorCodes.AKBURA_SEMANTIC_MarkupAttributeValueCannotConvert,
            [property.Name, sourceTypeText, targetTypeText]));
    }

    internal void AddMarkupExpressionDiagnostics(
        MarkupAttributeSyntax markupAttribute,
        CSharpBindingResult binding,
        ImmutableArrayBuilder<AkburaSemanticDiagnostic> diagnosticsBuilder)
    {
        AddMarkupExpressionDiagnostics(
            markupAttribute,
            GetMarkupExpressionDiagnosticText(markupAttribute),
            binding,
            diagnosticsBuilder);
    }

    internal void AddMarkupExpressionDiagnostics(
        MarkupAttributeSyntax markupAttribute,
        ImmutableArray<Diagnostic> diagnostics,
        ImmutableArrayBuilder<AkburaSemanticDiagnostic> diagnosticsBuilder)
    {
        AddMarkupExpressionDiagnostics(
            markupAttribute,
            GetMarkupExpressionDiagnosticText(markupAttribute),
            diagnostics,
            diagnosticsBuilder);
    }

    internal void AddMarkupExpressionDiagnostics(
        AkburaSyntax syntax,
        string expressionText,
        CSharpBindingResult binding,
        ImmutableArrayBuilder<AkburaSemanticDiagnostic> diagnosticsBuilder)
    {
        AddMarkupExpressionDiagnostics(
            syntax,
            expressionText,
            binding.Diagnostics,
            diagnosticsBuilder);
    }

    internal void AddMarkupExpressionDiagnostics(
        AkburaSyntax syntax,
        string expressionText,
        ImmutableArray<Diagnostic> diagnostics,
        ImmutableArrayBuilder<AkburaSemanticDiagnostic> diagnosticsBuilder)
    {
        if (diagnostics.IsDefaultOrEmpty)
        {
            return;
        }

        foreach (var diagnostic in diagnostics)
        {
            if (diagnostic.Severity == DiagnosticSeverity.Error)
            {
                diagnosticsBuilder.Add(CreateMarkupExpressionErrorDiagnostic(
                    syntax,
                    expressionText,
                    diagnostic));
            }
        }
    }

    internal void AddMarkupCommandHandlerSignatureDiagnostics(
        MarkupAttributeSyntax markupAttribute,
        ICommandSymbol command,
        CSharp.ExpressionSyntax? expression,
        MarkupCommandHandlerAnalysis handler,
        ImmutableArrayBuilder<AkburaSemanticDiagnostic> diagnosticsBuilder)
    {
        if (handler.Kind != MarkupCommandHandlerKind.Lambda ||
            expression == null)
        {
            return;
        }

        if (handler.ParameterCount != 0 &&
            handler.ParameterCount != command.Parameters.Length)
        {
            diagnosticsBuilder.Add(CreateMarkupCommandHandlerSignatureMismatchDiagnostic(
                markupAttribute,
                command,
                FormatCommandHandlerParameterExpectation(command),
                handler.ParameterCount + " parameter(s)"));
            return;
        }

        if (handler.ParameterCount != 0 &&
            TryGetLambdaParameterTypes(expression, out var actualParameterTypes) &&
            !HasCompatibleCommandHandlerParameters(command, actualParameterTypes))
        {
            diagnosticsBuilder.Add(CreateMarkupCommandHandlerSignatureMismatchDiagnostic(
                markupAttribute,
                command,
                FormatCommandHandlerParameterTypes(command),
                FormatEventHandlerSignature(actualParameterTypes)));
            return;
        }

        if (handler.ResultMode != MarkupCommandResultMode.ReturnsResult)
        {
            return;
        }

        if (!command.HasResult)
        {
            diagnosticsBuilder.Add(CreateMarkupCommandHandlerSignatureMismatchDiagnostic(
                markupAttribute,
                command,
                "no result",
                FormatCommandHandlerResultType(handler.ResultType)));
            return;
        }

        if (handler.ResultType.Symbol is not ITypeSymbol sourceType ||
            command.ResultType.Symbol is not ITypeSymbol targetType ||
            IsSameType(sourceType, targetType) ||
            Compilation.CSharpCompilation.ClassifyConversion(sourceType, targetType).IsImplicit)
        {
            return;
        }

        diagnosticsBuilder.Add(CreateMarkupCommandHandlerSignatureMismatchDiagnostic(
            markupAttribute,
            command,
            targetType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            sourceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
    }

    private bool HasCompatibleCommandHandlerParameters(
        ICommandSymbol command,
        ImmutableArray<CSharp.TypeSyntax?> actualParameterTypes)
    {
        if (actualParameterTypes.Length != command.Parameters.Length)
        {
            return false;
        }

        for (var index = 0; index < actualParameterTypes.Length; index++)
        {
            var actualParameterType = actualParameterTypes[index];
            if (actualParameterType == null)
            {
                continue;
            }

            if (command.Parameters[index].Type.Symbol is not ITypeSymbol expectedType)
            {
                return false;
            }

            var binding = BindCSharpType(actualParameterType);
            if (binding.TypeSymbol == null ||
                !IsSameType(expectedType, binding.TypeSymbol))
            {
                return false;
            }
        }

        return true;
    }

    private static string FormatCommandHandlerParameterExpectation(ICommandSymbol command)
    {
        return command.Parameters.Length == 0
            ? "0 parameter(s)"
            : "0 or " + command.Parameters.Length + " parameter(s)";
    }

    private static string FormatCommandHandlerParameterTypes(ICommandSymbol command)
    {
        if (command.Parameters.Length == 0)
        {
            return "()";
        }

        return "(" +
            string.Join(
                ", ",
                command.Parameters.Select(static parameter => parameter.Type.IsDefault
                    ? "object"
                    : parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))) +
            ")";
    }

    private static string FormatCommandHandlerResultType(CSharpSymbolDefinition resultType)
    {
        return resultType.IsDefault
            ? "<unknown>"
            : resultType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }

    internal MarkupCommandHandlerAnalysis AnalyzeMarkupCommandHandler(
        MarkupAttributeSyntax markupAttribute,
        ICommandSymbol command,
        CSharp.ExpressionSyntax? expression,
        CSharpSymbolDefinition handlerType,
        CSharpOperationDefinition handlerOperation)
    {
        if (expression == null)
        {
            return MarkupCommandHandlerAnalysis.Error;
        }

        return expression switch
        {
            CSharp.ParenthesizedLambdaExpressionSyntax lambda => AnalyzeMarkupCommandLambda(
                markupAttribute,
                command,
                lambda.ParameterList.Parameters.Select(static parameter => parameter.Identifier.ValueText).ToImmutableArray(),
                lambda.AsyncKeyword.RawKind != 0,
                lambda.Body),
            CSharp.SimpleLambdaExpressionSyntax lambda => AnalyzeMarkupCommandLambda(
                markupAttribute,
                command,
                ImmutableArray.Create(lambda.Parameter.Identifier.ValueText),
                lambda.AsyncKeyword.RawKind != 0,
                lambda.Body),
            CSharp.AnonymousMethodExpressionSyntax anonymousMethod => AnalyzeMarkupCommandLambda(
                markupAttribute,
                command,
                anonymousMethod.ParameterList?.Parameters.Select(static parameter => parameter.Identifier.ValueText).ToImmutableArray() ??
                    ImmutableArray<string>.Empty,
                anonymousMethod.AsyncKeyword.RawKind != 0,
                anonymousMethod.Body),
            _ => new MarkupCommandHandlerAnalysis(
                MarkupCommandHandlerKind.DirectReference,
                MarkupCommandArgumentMode.None,
                MarkupCommandResultMode.Unknown,
                parameterCount: 0,
                isAsync: ContainsAwaitExpression(expression),
                containsAwait: ContainsAwaitExpression(expression),
                handlerType,
                resultType: default,
                handlerOperation),
        };
    }

    internal MarkupEventHandlerAnalysis AnalyzeMarkupEventHandler(
        MarkupAttributeSyntax markupAttribute,
        IRoutedEventSymbol routedEvent,
        CSharp.ExpressionSyntax? expression)
    {
        if (expression == null)
        {
            return MarkupEventHandlerAnalysis.Error;
        }

        return expression switch
        {
            CSharp.ParenthesizedLambdaExpressionSyntax lambda => AnalyzeMarkupEventLambda(
                markupAttribute,
                routedEvent,
                lambda.ParameterList.Parameters.Select(static parameter => parameter.Identifier.ValueText).ToImmutableArray(),
                lambda.AsyncKeyword.RawKind != 0,
                lambda.Body),
            CSharp.SimpleLambdaExpressionSyntax lambda => AnalyzeMarkupEventLambda(
                markupAttribute,
                routedEvent,
                ImmutableArray.Create(lambda.Parameter.Identifier.ValueText),
                lambda.AsyncKeyword.RawKind != 0,
                lambda.Body),
            CSharp.AnonymousMethodExpressionSyntax anonymousMethod => AnalyzeMarkupEventLambda(
                markupAttribute,
                routedEvent,
                anonymousMethod.ParameterList?.Parameters.Select(static parameter => parameter.Identifier.ValueText).ToImmutableArray() ??
                    ImmutableArray<string>.Empty,
                anonymousMethod.AsyncKeyword.RawKind != 0,
                anonymousMethod.Body),
            CSharp.IdentifierNameSyntax or CSharp.MemberAccessExpressionSyntax =>
                CreateDirectMarkupEventHandlerAnalysis(markupAttribute, expression),
            _ => CreateExpressionMarkupEventHandlerAnalysis(markupAttribute, routedEvent, expression),
        };
    }

    private MarkupEventHandlerAnalysis CreateDirectMarkupEventHandlerAnalysis(
        MarkupAttributeSyntax markupAttribute,
        CSharp.ExpressionSyntax expression)
    {
        var binding = BindMarkupAttributeExpression(markupAttribute, expression);
        return new MarkupEventHandlerAnalysis(
            MarkupCommandHandlerKind.DirectReference,
            MarkupCommandArgumentMode.None,
            parameterCount: 0,
            isAsync: ContainsAwaitExpression(expression),
            containsAwait: ContainsAwaitExpression(expression),
            binding.OperationDefinition,
            binding.Diagnostics);
    }

    private MarkupEventHandlerAnalysis CreateExpressionMarkupEventHandlerAnalysis(
        MarkupAttributeSyntax markupAttribute,
        IRoutedEventSymbol routedEvent,
        CSharp.ExpressionSyntax expression)
    {
        var binding = BindMarkupEventHandlerStatementExpression(
            markupAttribute,
            routedEvent,
            ImmutableArray<string>.Empty,
            expression,
            isAsync: false);

        return new MarkupEventHandlerAnalysis(
            MarkupCommandHandlerKind.Expression,
            MarkupCommandArgumentMode.IgnoresCommandArgument,
            parameterCount: 0,
            isAsync: ContainsAwaitExpression(expression),
            containsAwait: ContainsAwaitExpression(expression),
            binding.OperationDefinition,
            binding.Diagnostics);
    }

    private MarkupEventHandlerAnalysis AnalyzeMarkupEventLambda(
        MarkupAttributeSyntax markupAttribute,
        IRoutedEventSymbol routedEvent,
        ImmutableArray<string> parameterNames,
        bool isAsync,
        SyntaxNode body)
    {
        var containsAwait = ContainsAwaitExpression(body);
        var binding = body switch
        {
            CSharp.ExpressionSyntax expression => BindMarkupEventHandlerStatementExpression(
                markupAttribute,
                routedEvent,
                parameterNames,
                expression,
                isAsync || containsAwait),
            CSharp.BlockSyntax block => BindMarkupEventHandlerBlock(
                markupAttribute,
                routedEvent,
                parameterNames,
                block,
                isAsync || containsAwait),
            _ => CSharpBindingResult.Empty,
        };

        return new MarkupEventHandlerAnalysis(
            MarkupCommandHandlerKind.Lambda,
            parameterNames.Length == 0
                ? MarkupCommandArgumentMode.IgnoresCommandArgument
                : MarkupCommandArgumentMode.ReceivesCommandArgument,
            parameterNames.Length,
            isAsync || containsAwait,
            containsAwait,
            binding.OperationDefinition,
            binding.Diagnostics);
    }

    private MarkupCommandHandlerAnalysis AnalyzeMarkupCommandLambda(
        MarkupAttributeSyntax markupAttribute,
        ICommandSymbol command,
        ImmutableArray<string> parameterNames,
        bool isAsync,
        SyntaxNode body)
    {
        var containsAwait = ContainsAwaitExpression(body);
        var argumentMode = parameterNames.Length == 0
            ? MarkupCommandArgumentMode.IgnoresCommandArgument
            : MarkupCommandArgumentMode.ReceivesCommandArgument;
        var resultMode = MarkupCommandResultMode.NoResult;
        var resultType = default(CSharpSymbolDefinition);
        var operation = default(CSharpOperationDefinition);
        var diagnostics = ImmutableArray<Diagnostic>.Empty;

        if (body is CSharp.ExpressionSyntax expressionBody)
        {
            var resultBinding = BindCommandHandlerResultExpression(markupAttribute, command, parameterNames, expressionBody);
            operation = resultBinding.OperationDefinition;
            diagnostics = resultBinding.Diagnostics;
            if (TryGetAwaitedLocalCommandExecuteResultType(expressionBody, out var awaitedCommandResultType))
            {
                resultMode = MarkupCommandResultMode.ReturnsResult;
                resultType = awaitedCommandResultType;
            }
            else if (resultBinding.TypeSymbol is { SpecialType: SpecialType.System_Void } ||
                resultBinding.Symbol is IMethodSymbol { ReturnsVoid: true })
            {
                resultMode = MarkupCommandResultMode.NoResult;
            }
            else if (resultBinding.TypeSymbol != null)
            {
                resultMode = MarkupCommandResultMode.ReturnsResult;
                resultType = new CSharpSymbolDefinition(resultBinding.TypeSymbol);
            }
            else if (expressionBody is CSharp.InvocationExpressionSyntax)
            {
                var statementBinding = BindCommandHandlerStatementExpression(markupAttribute, command, parameterNames, expressionBody);
                operation = statementBinding.OperationDefinition;
                diagnostics = statementBinding.Diagnostics;
                if (statementBinding.Symbol is IMethodSymbol { ReturnsVoid: true })
                {
                    resultMode = MarkupCommandResultMode.NoResult;
                }
                else if (statementBinding.TypeSymbol != null)
                {
                    resultMode = MarkupCommandResultMode.ReturnsResult;
                    resultType = new CSharpSymbolDefinition(statementBinding.TypeSymbol);
                }
                else
                {
                    resultMode = MarkupCommandResultMode.Unknown;
                }
            }
            else
            {
                resultMode = MarkupCommandResultMode.Unknown;
            }
        }
        else if (body is CSharp.BlockSyntax block)
        {
            var returnExpression = block
                .DescendantNodes()
                .OfType<CSharp.ReturnStatementSyntax>()
                .Select(static returnStatement => returnStatement.Expression)
                .FirstOrDefault(expression => expression != null);
            if (returnExpression != null)
            {
                var resultBinding = BindCommandHandlerResultExpression(markupAttribute, command, parameterNames, returnExpression);
                operation = resultBinding.OperationDefinition;
                diagnostics = resultBinding.Diagnostics;
                resultMode = MarkupCommandResultMode.ReturnsResult;
                resultType = resultBinding.TypeSymbol == null
                    ? default
                    : new CSharpSymbolDefinition(resultBinding.TypeSymbol);
            }
        }

        return new MarkupCommandHandlerAnalysis(
            MarkupCommandHandlerKind.Lambda,
            argumentMode,
            resultMode,
            parameterNames.Length,
            isAsync || containsAwait,
            containsAwait,
            type: default,
            resultType,
            operation,
            diagnostics);
    }

    private bool TryGetAwaitedLocalCommandExecuteResultType(
        CSharp.ExpressionSyntax expression,
        out CSharpSymbolDefinition resultType)
    {
        resultType = default;

        if (expression is not CSharp.AwaitExpressionSyntax awaitExpression ||
            awaitExpression.Expression is not CSharp.InvocationExpressionSyntax invocation ||
            invocation.Expression is not CSharp.MemberAccessExpressionSyntax memberAccess ||
            memberAccess.Name.Identifier.ValueText != "Execute" ||
            memberAccess.Expression is not CSharp.IdentifierNameSyntax receiver)
        {
            return false;
        }

        var commandName = receiver.Identifier.ValueText;
        foreach (var member in SyntaxTree.GetRoot().Members)
        {
            if (member.Kind != AkburaSyntaxKind.CommandDeclarationSyntax)
            {
                continue;
            }

            var commandDeclaration = Unsafe.As<CommandDeclarationSyntax>(member);
            if (commandDeclaration.Name.Identifier.ValueText != commandName ||
                GetSymbolInfo(commandDeclaration).Symbol is not ICommandSymbol command ||
                command.ResultType.IsDefault)
            {
                continue;
            }

            resultType = command.ResultType;
            return true;
        }

        return false;
    }

    private CSharpBindingResult BindCommandHandlerResultExpression(
        MarkupAttributeSyntax markupAttribute,
        ICommandSymbol command,
        ImmutableArray<string> parameterNames,
        CSharp.ExpressionSyntax expressionSyntax)
    {
        var probeScope = CreateMarkupHandlerProbeScope(
            markupAttribute,
            expressionSyntax,
            parameterNames);
        var returnStatement = CSharpSyntaxFactory.ReturnStatement(expressionSyntax);
        var method = CSharpSyntaxFactory.MethodDeclaration(
                ContainsAwaitExpression(expressionSyntax)
                    ? CSharpSyntaxFactory.ParseTypeName("global::System.Threading.Tasks.Task<object>")
                    : CSharpSyntaxFactory.PredefinedType(CSharpSyntaxFactory.Token(Microsoft.CodeAnalysis.CSharp.SyntaxKind.ObjectKeyword)),
                "__AkburaCommandHandlerProbe")
            .WithParameterList(CreateCommandHandlerProbeParameterList(command, parameterNames))
            .WithBody(CreateMarkupHandlerProbeBlock(probeScope.LocalStatements, returnStatement));

        if (ContainsAwaitExpression(expressionSyntax))
        {
            method = method.WithModifiers(CSharpSyntaxFactory.TokenList(
                CSharpSyntaxFactory.Token(Microsoft.CodeAnalysis.CSharp.SyntaxKind.AsyncKeyword)));
        }

        using var membersBuilder = ImmutableArrayBuilder<CSharp.MemberDeclarationSyntax>.Rent();
        AddMarkupAttributeProbeMembers(membersBuilder, probeScope);

        membersBuilder.Add(method);

        var probeClass = CSharpSyntaxFactory.ClassDeclaration("__AkburaSemanticProbe")
            .WithMembers(CSharpSyntaxFactory.List(membersBuilder.ToImmutable()));

        var compilationUnit = CreateCSharpProbeCompilationUnit(probeClass);
        return BindingSession
            .GetCSharpProbeBinder(GetMarkupBindingScope(markupAttribute), BinderUsage.Markup)
            .BindReturnExpression(compilationUnit, isBindingPath: false);
    }

    private CSharpBindingResult BindCommandHandlerStatementExpression(
        MarkupAttributeSyntax markupAttribute,
        ICommandSymbol command,
        ImmutableArray<string> parameterNames,
        CSharp.ExpressionSyntax expressionSyntax)
    {
        var probeScope = CreateMarkupHandlerProbeScope(
            markupAttribute,
            expressionSyntax,
            parameterNames);
        var statement = CSharpSyntaxFactory.ExpressionStatement(expressionSyntax);
        var method = CSharpSyntaxFactory.MethodDeclaration(
                CSharpSyntaxFactory.PredefinedType(CSharpSyntaxFactory.Token(Microsoft.CodeAnalysis.CSharp.SyntaxKind.VoidKeyword)),
                "__AkburaCommandHandlerProbe")
            .WithParameterList(CreateCommandHandlerProbeParameterList(command, parameterNames))
            .WithBody(CreateMarkupHandlerProbeBlock(probeScope.LocalStatements, statement));

        using var membersBuilder = ImmutableArrayBuilder<CSharp.MemberDeclarationSyntax>.Rent();
        AddMarkupAttributeProbeMembers(membersBuilder, probeScope);

        membersBuilder.Add(method);

        var probeClass = CSharpSyntaxFactory.ClassDeclaration("__AkburaSemanticProbe")
            .WithMembers(CSharpSyntaxFactory.List(membersBuilder.ToImmutable()));

        var compilationUnit = CreateCSharpProbeCompilationUnit(probeClass);
        return BindingSession
            .GetCSharpProbeBinder(GetMarkupBindingScope(markupAttribute), BinderUsage.Markup)
            .BindExpressionStatement(compilationUnit, isBindingPath: false);
    }

    private CSharpBindingResult BindMarkupEventHandlerStatementExpression(
        MarkupAttributeSyntax markupAttribute,
        IRoutedEventSymbol routedEvent,
        ImmutableArray<string> parameterNames,
        CSharp.ExpressionSyntax expressionSyntax,
        bool isAsync)
    {
        var probeScope = CreateMarkupHandlerProbeScope(
            markupAttribute,
            expressionSyntax,
            parameterNames);
        var statement = CSharpSyntaxFactory.ExpressionStatement(expressionSyntax);
        var method = CSharpSyntaxFactory.MethodDeclaration(
                CSharpSyntaxFactory.PredefinedType(CSharpSyntaxFactory.Token(Microsoft.CodeAnalysis.CSharp.SyntaxKind.VoidKeyword)),
                "__AkburaEventHandlerProbe")
            .WithParameterList(CreateEventHandlerProbeParameterList(routedEvent, parameterNames))
            .WithBody(CreateMarkupHandlerProbeBlock(probeScope.LocalStatements, statement));

        if (isAsync)
        {
            method = method.WithModifiers(CSharpSyntaxFactory.TokenList(
                CSharpSyntaxFactory.Token(Microsoft.CodeAnalysis.CSharp.SyntaxKind.AsyncKeyword)));
        }

        using var membersBuilder = ImmutableArrayBuilder<CSharp.MemberDeclarationSyntax>.Rent();
        AddMarkupAttributeProbeMembers(membersBuilder, probeScope);

        membersBuilder.Add(method);

        var probeClass = CSharpSyntaxFactory.ClassDeclaration("__AkburaSemanticProbe")
            .WithMembers(CSharpSyntaxFactory.List(membersBuilder.ToImmutable()));

        var compilationUnit = CreateCSharpProbeCompilationUnit(probeClass);
        return BindingSession
            .GetCSharpProbeBinder(GetMarkupBindingScope(markupAttribute), BinderUsage.Markup)
            .BindExpressionStatement(compilationUnit, isBindingPath: false);
    }

    private CSharpBindingResult BindMarkupEventHandlerBlock(
        MarkupAttributeSyntax markupAttribute,
        IRoutedEventSymbol routedEvent,
        ImmutableArray<string> parameterNames,
        CSharp.BlockSyntax block,
        bool isAsync)
    {
        var probeScope = CreateMarkupHandlerProbeScope(
            markupAttribute,
            block,
            parameterNames);
        var method = CSharpSyntaxFactory.MethodDeclaration(
                CSharpSyntaxFactory.PredefinedType(CSharpSyntaxFactory.Token(Microsoft.CodeAnalysis.CSharp.SyntaxKind.VoidKeyword)),
                "__AkburaEventHandlerProbe")
            .WithParameterList(CreateEventHandlerProbeParameterList(routedEvent, parameterNames))
            .WithBody(PrependMarkupHandlerProbeLocals(block, probeScope.LocalStatements));

        if (isAsync)
        {
            method = method.WithModifiers(CSharpSyntaxFactory.TokenList(
                CSharpSyntaxFactory.Token(Microsoft.CodeAnalysis.CSharp.SyntaxKind.AsyncKeyword)));
        }

        using var membersBuilder = ImmutableArrayBuilder<CSharp.MemberDeclarationSyntax>.Rent();
        AddMarkupAttributeProbeMembers(membersBuilder, probeScope);

        membersBuilder.Add(method);

        var probeClass = CSharpSyntaxFactory.ClassDeclaration("__AkburaSemanticProbe")
            .WithMembers(CSharpSyntaxFactory.List(membersBuilder.ToImmutable()));

        var compilationUnit = CreateCSharpProbeCompilationUnit(probeClass);
        return BindingSession
            .GetCSharpProbeBinder(GetMarkupBindingScope(markupAttribute), BinderUsage.Markup)
            .BindMethodBlock(compilationUnit, "__AkburaEventHandlerProbe");
    }

    private CSharpProbeScope CreateMarkupHandlerProbeScope(
        MarkupAttributeSyntax markupAttribute,
        SyntaxNode csharpNode,
        ImmutableArray<string> parameterNames)
    {
        var scope = GetMarkupBindingScope(markupAttribute);
        return BindingSession
            .GetCSharpProbeBinder(scope, BinderUsage.Markup)
            .CreateProbeScope(scope, csharpNode, parameterNames);
    }

    private void AddMarkupAttributeProbeMembers(
        ImmutableArrayBuilder<CSharp.MemberDeclarationSyntax> membersBuilder,
        CSharpProbeScope probeScope)
    {
        var addedMemberKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in CreateMarkupAttributeProbeFields())
        {
            AddMarkupAttributeProbeMember(membersBuilder, addedMemberKeys, field);
        }

        foreach (var member in probeScope.MemberDeclarations)
        {
            AddMarkupAttributeProbeMember(membersBuilder, addedMemberKeys, member);
        }
    }

    private static void AddMarkupAttributeProbeMember(
        ImmutableArrayBuilder<CSharp.MemberDeclarationSyntax> membersBuilder,
        HashSet<string> addedMemberKeys,
        CSharp.MemberDeclarationSyntax member)
    {
        var key = GetMarkupAttributeProbeMemberKey(member);
        if (key == null ||
            addedMemberKeys.Add(key))
        {
            membersBuilder.Add(member);
        }
    }

    private static string? GetMarkupAttributeProbeMemberKey(CSharp.MemberDeclarationSyntax member)
    {
        return member switch
        {
            CSharp.ClassDeclarationSyntax classDeclaration => "class:" + classDeclaration.Identifier.ValueText,
            CSharp.StructDeclarationSyntax structDeclaration => "struct:" + structDeclaration.Identifier.ValueText,
            CSharp.MethodDeclarationSyntax methodDeclaration => "method:" + methodDeclaration.Identifier.ValueText,
            CSharp.FieldDeclarationSyntax fieldDeclaration when fieldDeclaration.Declaration.Variables.Count == 1 =>
                "field:" + fieldDeclaration.Declaration.Variables[0].Identifier.ValueText,
            _ => null,
        };
    }

    private static CSharp.BlockSyntax CreateMarkupHandlerProbeBlock(
        ImmutableArray<CSharp.StatementSyntax> localStatements,
        CSharp.StatementSyntax statement)
    {
        if (localStatements.IsDefaultOrEmpty)
        {
            return CSharpSyntaxFactory.Block(statement);
        }

        using var statements = ImmutableArrayBuilder<CSharp.StatementSyntax>.Rent(localStatements.Length + 1);
        statements.AddRange(localStatements.AsSpan());
        statements.Add(statement);
        return CSharpSyntaxFactory.Block(CSharpSyntaxFactory.List(statements.ToImmutable()));
    }

    private static CSharp.BlockSyntax PrependMarkupHandlerProbeLocals(
        CSharp.BlockSyntax block,
        ImmutableArray<CSharp.StatementSyntax> localStatements)
    {
        if (localStatements.IsDefaultOrEmpty)
        {
            return block;
        }

        using var statements = ImmutableArrayBuilder<CSharp.StatementSyntax>.Rent(
            localStatements.Length + block.Statements.Count);
        statements.AddRange(localStatements.AsSpan());
        foreach (var statement in block.Statements)
        {
            statements.Add(statement);
        }

        return block.WithStatements(CSharpSyntaxFactory.List(statements.ToImmutable()));
    }

    private static CSharp.ParameterListSyntax CreateCommandHandlerProbeParameterList(
        ICommandSymbol command,
        ImmutableArray<string> parameterNames)
    {
        using var parameters = ImmutableArrayBuilder<CSharp.ParameterSyntax>.Rent();
        for (var index = 0; index < parameterNames.Length; index++)
        {
            var name = string.IsNullOrWhiteSpace(parameterNames[index])
                ? "__arg" + index
                : parameterNames[index];
            var type = index < command.Parameters.Length && !command.Parameters[index].Type.IsDefault
                ? CSharpSyntaxFactory.ParseTypeName(command.Parameters[index].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                : CSharpSyntaxFactory.PredefinedType(CSharpSyntaxFactory.Token(Microsoft.CodeAnalysis.CSharp.SyntaxKind.ObjectKeyword));

            parameters.Add(CSharpSyntaxFactory.Parameter(CSharpSyntaxFactory.Identifier(name)).WithType(type));
        }

        return CSharpSyntaxFactory.ParameterList(
            CSharpSyntaxFactory.SeparatedList(parameters.ToImmutable()));
    }

    private static CSharp.ParameterListSyntax CreateEventHandlerProbeParameterList(
        IRoutedEventSymbol routedEvent,
        ImmutableArray<string> parameterNames)
    {
        if (routedEvent.HandlerType.Symbol is not INamedTypeSymbol { DelegateInvokeMethod: { } invokeMethod })
        {
            return CSharpSyntaxFactory.ParameterList();
        }

        using var parameters = ImmutableArrayBuilder<CSharp.ParameterSyntax>.Rent();
        for (var index = 0; index < invokeMethod.Parameters.Length; index++)
        {
            var delegateParameter = invokeMethod.Parameters[index];
            var name = index < parameterNames.Length && !string.IsNullOrWhiteSpace(parameterNames[index])
                ? parameterNames[index]
                : string.IsNullOrWhiteSpace(delegateParameter.Name)
                    ? "__arg" + index
                    : delegateParameter.Name;
            var type = CSharpSyntaxFactory.ParseTypeName(
                delegateParameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));

            parameters.Add(CSharpSyntaxFactory.Parameter(CSharpSyntaxFactory.Identifier(name)).WithType(type));
        }

        return CSharpSyntaxFactory.ParameterList(
            CSharpSyntaxFactory.SeparatedList(parameters.ToImmutable()));
    }

    private static bool ContainsAwaitExpression(SyntaxNode node)
    {
        return node.DescendantNodesAndSelf().OfType<CSharp.AwaitExpressionSyntax>().Any();
    }

    private CSharpBindingResult BindMarkupAttributeExpression(CSharp.ExpressionSyntax expressionSyntax)
    {
        var returnStatement = CSharpSyntaxFactory.ReturnStatement(expressionSyntax);
        var method = CSharpSyntaxFactory.MethodDeclaration(
                CSharpSyntaxFactory.PredefinedType(CSharpSyntaxFactory.Token(Microsoft.CodeAnalysis.CSharp.SyntaxKind.ObjectKeyword)),
                "__AkburaSemanticProbe")
            .WithBody(CSharpSyntaxFactory.Block(returnStatement));

        using var membersBuilder = ImmutableArrayBuilder<CSharp.MemberDeclarationSyntax>.Rent();
        foreach (var field in CreateMarkupAttributeProbeFields())
        {
            membersBuilder.Add(field);
        }

        membersBuilder.Add(method);

        var probeClass = CSharpSyntaxFactory.ClassDeclaration("__AkburaSemanticProbe")
            .WithMembers(CSharpSyntaxFactory.List(membersBuilder.ToImmutable()));

        var compilationUnit = CreateCSharpProbeCompilationUnit(probeClass);
        return BindingSession
            .GetCSharpProbeBinder(SyntaxTree.GetRoot(), BinderUsage.Markup)
            .BindReturnExpression(compilationUnit, isBindingPath: true);
    }

    internal CSharpBindingResult BindMarkupAttributeExpression(
        AkburaSyntax scopeSyntax,
        CSharp.ExpressionSyntax expressionSyntax,
        ITypeSymbol? targetType = null)
    {
        var scope = GetMarkupBindingScope(scopeSyntax);
        var bound = BindingSession
            .GetCSharpProbeBinder(scope, BinderUsage.Markup)
            .BindExpression(scope, expressionSyntax, targetType);

        return GetCSharpBindingResult(bound);
    }

    private static CSharpBindingResult GetCSharpBindingResult(BoundExpression expression)
    {
        return expression.Kind switch
        {
            BoundKind.CSharpExpression => Unsafe.As<BoundCSharpExpression>(expression).BindingResult,
            BoundKind.LiteralExpression => Unsafe.As<BoundLiteralExpression>(expression).BindingResult,
            BoundKind.BinaryExpression => Unsafe.As<BoundBinaryExpression>(expression).BindingResult,
            BoundKind.CallExpression => Unsafe.As<BoundCallExpression>(expression).BindingResult,
            BoundKind.ConversionExpression => GetCSharpConversionBindingResult(Unsafe.As<BoundConversionExpression>(expression)),
            _ => CSharpBindingResult.Empty,
        };
    }

    private static CSharpBindingResult GetCSharpConversionBindingResult(BoundConversionExpression expression)
    {
        return GetCSharpBindingResult(expression.Operand)
            .WithConversion(expression.Conversion);
    }

    private AkburaSyntax GetMarkupBindingScope(AkburaSyntax syntax)
    {
        for (var node = syntax; node != null; node = node.Parent)
        {
            switch (node.Kind)
            {
                case AkburaSyntaxKind.MarkupRootSyntax:
                    return Unsafe.As<MarkupRootSyntax>(node);
                case AkburaSyntaxKind.MarkupElementSyntax:
                    var markupElement = Unsafe.As<MarkupElementSyntax>(node);
                    if (markupElement.Parent?.Kind == AkburaSyntaxKind.MarkupRootSyntax)
                    {
                        return Unsafe.As<MarkupRootSyntax>(markupElement.Parent);
                    }

                    return markupElement;
            }
        }

        return SyntaxTree.GetRoot();
    }

    private ImmutableArray<CSharp.MemberDeclarationSyntax> CreateMarkupAttributeProbeFields()
    {
        using var builder = ImmutableArrayBuilder<CSharp.MemberDeclarationSyntax>.Rent();
        foreach (var member in SyntaxTree.GetRoot().Members)
        {
            switch (member.Kind)
            {
                case AkburaSyntaxKind.StateDeclarationSyntax:
                    var stateDeclaration = Unsafe.As<StateDeclarationSyntax>(member);
                    if (TryCreateStateProbeField(stateDeclaration, out var stateField))
                    {
                        builder.Add(stateField);
                    }

                    break;

                case AkburaSyntaxKind.ParamDeclarationSyntax:
                    var paramDeclaration = Unsafe.As<ParamDeclarationSyntax>(member);
                    if (TryCreateParamProbeField(paramDeclaration, out var paramField))
                    {
                        builder.Add(paramField);
                    }

                    break;

                case AkburaSyntaxKind.InjectDeclarationSyntax:
                    var injectDeclaration = Unsafe.As<InjectDeclarationSyntax>(member);
                    if (TryCreateInjectProbeField(injectDeclaration, out var injectField))
                    {
                        builder.Add(injectField);
                    }

                    break;

                case AkburaSyntaxKind.CommandDeclarationSyntax:
                    var commandDeclaration = Unsafe.As<CommandDeclarationSyntax>(member);
                    foreach (var commandMember in CreateCommandProbeMembers(commandDeclaration))
                    {
                        builder.Add(commandMember);
                    }

                    break;
            }
        }

        return builder.ToImmutable();
    }

    private bool TryCreateParamProbeField(
        ParamDeclarationSyntax paramDeclaration,
        out CSharp.FieldDeclarationSyntax field)
    {
        field = null!;

        var name = paramDeclaration.Name.Identifier.ValueText;
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        CSharp.TypeSyntax? type = null;
        if (paramDeclaration.Type != null)
        {
            try
            {
                type = paramDeclaration.Type.ToCSharp();
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
        else if (GetSymbolInfo(paramDeclaration).Symbol is IParamSymbol paramSymbol &&
                 paramSymbol.Type.Symbol is ITypeSymbol typeSymbol)
        {
            type = CSharpSyntaxFactory.ParseTypeName(
                typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        }

        if (type == null)
        {
            return false;
        }

        field = CreateProbeField(type, name);
        return true;
    }

    private static bool TryCreateInjectProbeField(
        InjectDeclarationSyntax injectDeclaration,
        out CSharp.FieldDeclarationSyntax field)
    {
        field = null!;

        var name = injectDeclaration.Name.Identifier.ValueText;
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        try
        {
            field = CreateProbeField(injectDeclaration.Type.ToCSharp(), name);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private ImmutableArray<CSharp.MemberDeclarationSyntax> CreateCommandProbeMembers(
        CommandDeclarationSyntax commandDeclaration)
    {
        if (GetSymbolInfo(commandDeclaration).Symbol is not ICommandSymbol command)
        {
            return ImmutableArray<CSharp.MemberDeclarationSyntax>.Empty;
        }

        var commandTypeName = "__AkburaCommand_" + ToCSharpIdentifier(command.Name);
        var commandType = CSharpSyntaxFactory.IdentifierName(commandTypeName);
        var commandClass = CSharpSyntaxFactory.ClassDeclaration(commandTypeName)
            .WithModifiers(CSharpSyntaxFactory.TokenList(
                CSharpSyntaxFactory.Token(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PrivateKeyword),
                CSharpSyntaxFactory.Token(Microsoft.CodeAnalysis.CSharp.SyntaxKind.SealedKeyword)))
            .WithMembers(CSharpSyntaxFactory.List(CreateCommandProbeTypeMembers(commandDeclaration, command)));
        var commandField = CreateProbeField(commandType, command.Name);

        return ImmutableArray.Create<CSharp.MemberDeclarationSyntax>(commandClass, commandField);
    }

    private ImmutableArray<CSharp.MemberDeclarationSyntax> CreateCommandProbeTypeMembers(
        CommandDeclarationSyntax commandDeclaration,
        ICommandSymbol command)
    {
        using var builder = ImmutableArrayBuilder<CSharp.MemberDeclarationSyntax>.Rent();

        builder.Add(CSharpSyntaxFactory.PropertyDeclaration(
                CSharpSyntaxFactory.ParseTypeName("global::System.IObservable<bool>"),
                "IsExecuting")
            .WithModifiers(CSharpSyntaxFactory.TokenList(
                CSharpSyntaxFactory.Token(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PublicKeyword)))
            .WithExpressionBody(CSharpSyntaxFactory.ArrowExpressionClause(
                CSharpSyntaxFactory.LiteralExpression(Microsoft.CodeAnalysis.CSharp.SyntaxKind.DefaultLiteralExpression)))
            .WithSemicolonToken(CSharpSyntaxFactory.Token(Microsoft.CodeAnalysis.CSharp.SyntaxKind.SemicolonToken)));

        builder.Add(CSharpSyntaxFactory.PropertyDeclaration(
                CSharpSyntaxFactory.ParseTypeName("global::System.IObservable<bool>"),
                "CanExecute")
            .WithModifiers(CSharpSyntaxFactory.TokenList(
                CSharpSyntaxFactory.Token(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PublicKeyword)))
            .WithExpressionBody(CSharpSyntaxFactory.ArrowExpressionClause(
                CSharpSyntaxFactory.LiteralExpression(Microsoft.CodeAnalysis.CSharp.SyntaxKind.DefaultLiteralExpression)))
            .WithSemicolonToken(CSharpSyntaxFactory.Token(Microsoft.CodeAnalysis.CSharp.SyntaxKind.SemicolonToken)));

        var parameterList = GetCSharpParameterList(commandDeclaration.Parameters) ??
            CSharpSyntaxFactory.ParameterList();
        var execute = CSharpSyntaxFactory.MethodDeclaration(
                GetCommandExecuteReturnTypeSyntax(command),
                "Execute")
            .WithModifiers(CSharpSyntaxFactory.TokenList(
                CSharpSyntaxFactory.Token(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PublicKeyword)))
            .WithParameterList(parameterList)
            .WithBody(CSharpSyntaxFactory.Block(CSharpSyntaxFactory.ThrowStatement(
                CSharpSyntaxFactory.ObjectCreationExpression(
                        CSharpSyntaxFactory.ParseTypeName("global::System.NotImplementedException"))
                    .WithArgumentList(CSharpSyntaxFactory.ArgumentList()))));

        builder.Add(execute);
        return builder.ToImmutable();
    }

    private static CSharp.TypeSyntax GetCommandExecuteReturnTypeSyntax(ICommandSymbol command)
    {
        if (command.HasResult &&
            !command.ResultType.IsDefault)
        {
            return CSharpSyntaxFactory.ParseTypeName(
                "global::System.Threading.Tasks.ValueTask<" +
                command.ResultType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
                ">");
        }

        return CSharpSyntaxFactory.ParseTypeName("global::System.Threading.Tasks.ValueTask");
    }

    private static string ToCSharpIdentifier(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "_";
        }

        var builder = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            var ch = value[index];
            builder.Append(index == 0
                ? Microsoft.CodeAnalysis.CSharp.SyntaxFacts.IsIdentifierStartCharacter(ch) ? ch : '_'
                : Microsoft.CodeAnalysis.CSharp.SyntaxFacts.IsIdentifierPartCharacter(ch) ? ch : '_');
        }

        return builder.ToString();
    }

    private static CSharp.FieldDeclarationSyntax CreateProbeField(
        CSharp.TypeSyntax type,
        string name)
    {
        return CSharpSyntaxFactory.FieldDeclaration(
                CSharpSyntaxFactory.VariableDeclaration(type)
                    .WithVariables(CSharpSyntaxFactory.SingletonSeparatedList(
                        CSharpSyntaxFactory.VariableDeclarator(
                            CSharpSyntaxFactory.Identifier(name)))))
            .WithModifiers(CSharpSyntaxFactory.TokenList(
                CSharpSyntaxFactory.Token(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PrivateKeyword)));
    }

    private static string GetTailwindUtilityName(TailwindAttributeSyntax attribute)
    {
        return attribute.Kind switch
        {
            AkburaSyntaxKind.TailwindFlagAttributeSyntax => Unsafe.As<TailwindFlagAttributeSyntax>(attribute).Name.Identifier.ValueText,
            AkburaSyntaxKind.TailwindFullAttributeSyntax => Unsafe.As<TailwindFullAttributeSyntax>(attribute).Name.Identifier.ValueText,
            _ => string.Empty,
        };
    }

    private ImmutableArray<BoundTailwindUtilityArgument> CreateTailwindUtilityArguments(TailwindAttributeSyntax attribute)
    {
        if (attribute.Kind != AkburaSyntaxKind.TailwindFullAttributeSyntax)
        {
            return ImmutableArray<BoundTailwindUtilityArgument>.Empty;
        }

        var fullAttribute = Unsafe.As<TailwindFullAttributeSyntax>(attribute);
        if (fullAttribute.Segments.Count == 0)
        {
            return ImmutableArray<BoundTailwindUtilityArgument>.Empty;
        }

        using var builder = ImmutableArrayBuilder<BoundTailwindUtilityArgument>.Rent();
        foreach (var segment in fullAttribute.Segments)
        {
            builder.Add(CreateTailwindUtilityArgument(segment));
        }

        return builder.ToImmutable();
    }

    private void AddTailwindExpressionDiagnostics(
        TailwindAttributeSyntax attribute,
        ImmutableArray<BoundTailwindUtilityArgument> arguments,
        ImmutableArrayBuilder<AkburaSemanticDiagnostic> diagnosticsBuilder)
    {
        if (attribute.Kind != AkburaSyntaxKind.TailwindFullAttributeSyntax)
        {
            return;
        }

        var fullAttribute = Unsafe.As<TailwindFullAttributeSyntax>(attribute);
        foreach (var argument in arguments)
        {
            if (argument.Syntax.Kind != AkburaSyntaxKind.TailwindExpressionSegmentSyntax ||
                IsConvertedEnumTailwindUtilityArgument(argument))
            {
                continue;
            }

            var expressionSegment = Unsafe.As<TailwindExpressionSegmentSyntax>(argument.Syntax);
            var expression = ParseInlineExpression(expressionSegment.Expression);
            if (expression != null)
            {
                AddMarkupExpressionDiagnostics(
                    attribute,
                    BindMarkupAttributeExpression(argument.Syntax, expression),
                    diagnosticsBuilder);
            }
        }

        if (fullAttribute.Prefix?.Kind == AkburaSyntaxKind.ExpressionConditionalPrefixSyntax)
        {
            var expressionPrefix = Unsafe.As<ExpressionConditionalPrefixSyntax>(fullAttribute.Prefix);
            var expression = ParseInlineExpression(expressionPrefix.Expression);
            if (expression != null)
            {
                AddMarkupExpressionDiagnostics(
                    attribute,
                    BindMarkupAttributeExpression(attribute, expression),
                    diagnosticsBuilder);
            }
        }
    }

    private static bool IsConvertedEnumTailwindUtilityArgument(BoundTailwindUtilityArgument argument)
    {
        return argument.Type.Symbol is INamedTypeSymbol { TypeKind: TypeKind.Enum } &&
            argument.ConstantValue != null;
    }

    private BoundTailwindUtilityArgument CreateTailwindUtilityArgument(TailwindSegmentSyntax segment)
    {
        CSharp.ExpressionSyntax? expression = segment.Kind switch
        {
            AkburaSyntaxKind.TailwindNumericSegmentSyntax => CSharpSyntaxFactory.ParseExpression(
                Unsafe.As<TailwindNumericSegmentSyntax>(segment).Number.ToFullString()),
            AkburaSyntaxKind.TailwindIdentifierSegmentSyntax => CSharpSyntaxFactory.LiteralExpression(
                Microsoft.CodeAnalysis.CSharp.SyntaxKind.StringLiteralExpression,
                CSharpSyntaxFactory.Literal(Unsafe.As<TailwindIdentifierSegmentSyntax>(segment).Name.Identifier.ValueText)),
            AkburaSyntaxKind.TailwindExpressionSegmentSyntax => ParseInlineExpression(
                Unsafe.As<TailwindExpressionSegmentSyntax>(segment).Expression),
            _ => null,
        };

        var binding = expression == null
            ? CSharpBindingResult.Empty
            : BindMarkupAttributeExpression(segment, expression);

        return new BoundTailwindUtilityArgument(
            segment,
            segment.ToFullString().Trim(),
            binding.TypeSymbol == null ? default : new CSharpSymbolDefinition(binding.TypeSymbol),
            binding.OperationDefinition,
            binding.OperationDefinition.ConstantValue.HasValue
                ? binding.OperationDefinition.ConstantValue.Value
                : null);
    }

    private (bool HasCondition, string? Text, CSharpSymbolDefinition Type, CSharpOperationDefinition Operation)
        CreateTailwindCondition(TailwindAttributeSyntax attribute)
    {
        if (attribute.Kind != AkburaSyntaxKind.TailwindFullAttributeSyntax)
        {
            return (false, null, default, default);
        }

        var fullAttribute = Unsafe.As<TailwindFullAttributeSyntax>(attribute);
        if (fullAttribute.Prefix?.Kind != AkburaSyntaxKind.ExpressionConditionalPrefixSyntax)
        {
            return (false, null, default, default);
        }

        var expressionPrefix = Unsafe.As<ExpressionConditionalPrefixSyntax>(fullAttribute.Prefix);
        var expression = ParseInlineExpression(expressionPrefix.Expression);
        if (expression == null)
        {
            return (true, expressionPrefix.Expression.ToFullString(), default, default);
        }

        var binding = BindMarkupAttributeExpression(attribute, expression);
        return (
            true,
            expressionPrefix.Expression.Expression.ToFullString(),
            binding.TypeSymbol == null ? default : new CSharpSymbolDefinition(binding.TypeSymbol),
            binding.OperationDefinition);
    }

    private AkburaSemanticDiagnostic CreateTailwindUtilityNotFoundDiagnostic(
        TailwindAttributeSyntax syntax,
        string utilityName,
        IMarkupComponentSymbol? componentSymbol)
    {
        return new AkburaSemanticDiagnostic(
            syntax,
            ErrorCodes.AKBURA_SEMANTIC_TailwindUtilityNotFound,
            [utilityName, componentSymbol?.Name ?? "<unknown>"]);
    }

    private AkburaSemanticDiagnostic CreateTailwindUtilityAmbiguousDiagnostic(
        TailwindAttributeSyntax syntax,
        string utilityName,
        IMarkupComponentSymbol? componentSymbol)
    {
        return new AkburaSemanticDiagnostic(
            syntax,
            ErrorCodes.AKBURA_SEMANTIC_TailwindUtilityAmbiguous,
            [utilityName, componentSymbol?.Name ?? "<unknown>"]);
    }

    private AkburaSemanticDiagnostic CreateTailwindUtilityArgumentMismatchDiagnostic(
        TailwindAttributeSyntax syntax,
        ITailwindUtilitySymbol utility,
        int actualCount)
    {
        return new AkburaSemanticDiagnostic(
            syntax,
            ErrorCodes.AKBURA_SEMANTIC_TailwindUtilityArgumentMismatch,
            [utility.Name, utility.Parameters.Length, actualCount]);
    }

    private static AkburaSemanticDiagnostic CreateMarkupExpressionErrorDiagnostic(
        MarkupAttributeSyntax syntax,
        Diagnostic diagnostic)
    {
        return CreateMarkupExpressionErrorDiagnostic(
            syntax,
            GetMarkupExpressionDiagnosticText(syntax),
            diagnostic);
    }

    private static string GetMarkupExpressionDiagnosticText(MarkupAttributeSyntax syntax)
    {
        var valueSyntax = GetMarkupAttributeValue(syntax);
        return valueSyntax == null
            ? syntax.ToFullString().Trim()
            : valueSyntax.ToFullString().Trim();
    }

    private static AkburaSemanticDiagnostic CreateMarkupExpressionErrorDiagnostic(
        AkburaSyntax syntax,
        string expressionText,
        Diagnostic diagnostic)
    {
        return new AkburaSemanticDiagnostic(
            syntax,
            ErrorCodes.AKBURA_SEMANTIC_MarkupExpressionError,
            [expressionText, diagnostic.GetMessage()]);
    }

    private static AkburaSemanticDiagnostic CreateMarkupCommandHandlerSignatureMismatchDiagnostic(
        MarkupAttributeSyntax markupAttribute,
        ICommandSymbol command,
        string expected,
        string actual)
    {
        return new AkburaSemanticDiagnostic(
            markupAttribute,
            ErrorCodes.AKBURA_SEMANTIC_MarkupCommandHandlerSignatureMismatch,
            [command.Name, expected, actual]);
    }

    private AkburaSemanticDiagnostic CreateAkcssImportNotFoundDiagnostic(string importName)
    {
        return new AkburaSemanticDiagnostic(
            SyntaxTree.GetRoot(),
            ErrorCodes.AKBURA_SEMANTIC_AkcssImportNotFound,
            [importName]);
    }

    internal readonly struct MarkupCommandHandlerAnalysis
    {
        public static MarkupCommandHandlerAnalysis Error { get; } = new(
            MarkupCommandHandlerKind.Error,
            MarkupCommandArgumentMode.None,
            MarkupCommandResultMode.Unknown,
            parameterCount: 0,
            isAsync: false,
            containsAwait: false,
            type: default,
            resultType: default,
            operation: default,
            diagnostics: ImmutableArray<Diagnostic>.Empty);

        public MarkupCommandHandlerAnalysis(
            MarkupCommandHandlerKind kind,
            MarkupCommandArgumentMode argumentMode,
            MarkupCommandResultMode resultMode,
            int parameterCount,
            bool isAsync,
            bool containsAwait,
            CSharpSymbolDefinition type,
            CSharpSymbolDefinition resultType,
            CSharpOperationDefinition operation,
            ImmutableArray<Diagnostic> diagnostics = default)
        {
            Kind = kind;
            ArgumentMode = argumentMode;
            ResultMode = resultMode;
            ParameterCount = parameterCount;
            IsAsync = isAsync;
            ContainsAwait = containsAwait;
            Type = type;
            ResultType = resultType;
            Operation = operation;
            Diagnostics = diagnostics.IsDefault
                ? ImmutableArray<Diagnostic>.Empty
                : diagnostics;
        }

        public MarkupCommandHandlerKind Kind { get; }

        public MarkupCommandArgumentMode ArgumentMode { get; }

        public MarkupCommandResultMode ResultMode { get; }

        public int ParameterCount { get; }

        public bool IsAsync { get; }

        public bool ContainsAwait { get; }

        public CSharpSymbolDefinition Type { get; }

        public CSharpSymbolDefinition ResultType { get; }

        public CSharpOperationDefinition Operation { get; }

        public ImmutableArray<Diagnostic> Diagnostics { get; }
    }

    internal readonly struct MarkupEventHandlerAnalysis
    {
        public static MarkupEventHandlerAnalysis Error { get; } = new(
            MarkupCommandHandlerKind.Error,
            MarkupCommandArgumentMode.None,
            parameterCount: 0,
            isAsync: false,
            containsAwait: false,
            operation: default,
            diagnostics: ImmutableArray<Diagnostic>.Empty);

        public MarkupEventHandlerAnalysis(
            MarkupCommandHandlerKind kind,
            MarkupCommandArgumentMode argumentMode,
            int parameterCount,
            bool isAsync,
            bool containsAwait,
            CSharpOperationDefinition operation,
            ImmutableArray<Diagnostic> diagnostics)
        {
            Kind = kind;
            ArgumentMode = argumentMode;
            ParameterCount = parameterCount;
            IsAsync = isAsync;
            ContainsAwait = containsAwait;
            Operation = operation;
            Diagnostics = diagnostics.IsDefault
                ? ImmutableArray<Diagnostic>.Empty
                : diagnostics;
        }

        public MarkupCommandHandlerKind Kind { get; }

        public MarkupCommandArgumentMode ArgumentMode { get; }

        public int ParameterCount { get; }

        public bool IsAsync { get; }

        public bool ContainsAwait { get; }

        public CSharpOperationDefinition Operation { get; }

        public ImmutableArray<Diagnostic> Diagnostics { get; }
    }
}
