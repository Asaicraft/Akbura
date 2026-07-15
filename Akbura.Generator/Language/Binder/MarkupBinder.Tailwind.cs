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
using CSharpSyntaxKind = Microsoft.CodeAnalysis.CSharp.SyntaxKind;

namespace Akbura.Language.Binder;

internal sealed partial class MarkupBinder
{
    internal BoundNode CreateBoundTailwindUtilityAttribute(TailwindAttributeSyntax attribute)
    {
        var diagnosticsBag = BindingDiagnosticBag.GetInstance();

        var containingComponent = SemanticModel.GetContainingMarkupComponentSymbol(attribute);
        var componentName = containingComponent?.Name ?? "<unknown>";
        var requestedUtilityName = AkburaSemanticModel.GetTailwindUtilityName(attribute);
        var utilityName = requestedUtilityName;
        var arguments = CreateTailwindUtilityArguments(attribute);
        var validatedArguments = arguments;
        var condition = CreateTailwindCondition(attribute);
        var utilities = ImmutableArray<ITailwindUtilitySymbol>.Empty;
        {
            using var diagnosticsBuilder = ImmutableArrayBuilder<AkburaSemanticDiagnostic>.Rent();
            utilities = ResolveTailwindUtilitiesForAttribute(
                attribute,
                requestedUtilityName,
                arguments,
                containingComponent,
                diagnosticsBuilder,
                out utilityName,
                out validatedArguments);
            AddTailwindExpressionDiagnostics(attribute, validatedArguments, diagnosticsBuilder);
            diagnosticsBag.AddRange(diagnosticsBuilder.ToImmutable());
        }

        var diagnostics = SemanticModel.SetSemanticDiagnostics(attribute, diagnosticsBag);

        return new BoundTailwindUtilityAttribute(
            attribute,
            this,
            containingComponent,
            utilityName,
            utilities.Length == 0 ? null : utilities[0],
            utilities,
            validatedArguments,
            condition.HasCondition,
            condition.Text,
            condition.Type,
            condition.Operation,
            diagnostics,
            hasErrors: utilities.Length == 0 || diagnostics.Length > 0 || componentName.Length == 0);
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
            var expression = AkburaSemanticModel.ParseInlineExpression(expressionSegment.Expression);
            if (expression != null)
            {
                SemanticModel.AddMarkupExpressionDiagnostics(
                    attribute,
                    SemanticModel.BindMarkupAttributeExpression(argument.Syntax, expression),
                    diagnosticsBuilder);
            }
        }

        if (fullAttribute.Prefix?.Kind == AkburaSyntaxKind.ExpressionConditionalPrefixSyntax)
        {
            var expressionPrefix = Unsafe.As<ExpressionConditionalPrefixSyntax>(fullAttribute.Prefix);
            var expression = AkburaSemanticModel.ParseInlineExpression(expressionPrefix.Expression);
            if (expression != null)
            {
                SemanticModel.AddMarkupExpressionDiagnostics(
                    attribute,
                    SemanticModel.BindMarkupAttributeExpression(attribute, expression),
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
                CSharpSyntaxKind.StringLiteralExpression,
                CSharpSyntaxFactory.Literal(Unsafe.As<TailwindIdentifierSegmentSyntax>(segment).Name.Identifier.ValueText)),
            AkburaSyntaxKind.TailwindExpressionSegmentSyntax => AkburaSemanticModel.ParseInlineExpression(
                Unsafe.As<TailwindExpressionSegmentSyntax>(segment).Expression),
            _ => null,
        };

        var binding = expression == null
            ? CSharpBindingResult.Empty
            : SemanticModel.BindMarkupAttributeExpression(segment, expression);

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
        var expression = AkburaSemanticModel.ParseInlineExpression(expressionPrefix.Expression);
        if (expression == null)
        {
            return (true, expressionPrefix.Expression.ToFullString(), default, default);
        }

        var binding = SemanticModel.BindMarkupAttributeExpression(attribute, expression);
        return (
            true,
            expressionPrefix.Expression.Expression.ToFullString(),
            binding.TypeSymbol == null ? default : new CSharpSymbolDefinition(binding.TypeSymbol),
            binding.OperationDefinition);
    }

    private ImmutableArray<ITailwindUtilitySymbol> ResolveTailwindUtilitiesForAttribute(
        TailwindAttributeSyntax attribute,
        string utilityName,
        ImmutableArray<BoundTailwindUtilityArgument> arguments,
        IMarkupComponentSymbol? containingComponent,
        ImmutableArrayBuilder<AkburaSemanticDiagnostic> diagnosticsBuilder,
        out string resolvedUtilityName,
        out ImmutableArray<BoundTailwindUtilityArgument> validatedArguments)
    {
        resolvedUtilityName = utilityName;
        validatedArguments = arguments;
        if (TryResolveTailwindUtilityDeclarationLayer(
                GetLocalAkcssUtilityDeclarations(diagnosticsBuilder),
                attribute,
                utilityName,
                arguments,
                containingComponent,
                diagnosticsBuilder,
                out resolvedUtilityName,
                out validatedArguments,
                out var localUtilities))
        {
            return localUtilities;
        }

        foreach (var importLayer in GetImportedAkcssUtilityDeclarationLayers(diagnosticsBuilder))
        {
            if (TryResolveTailwindUtilityDeclarationLayer(
                    importLayer,
                    attribute,
                    utilityName,
                    arguments,
                    containingComponent,
                    diagnosticsBuilder,
                    out resolvedUtilityName,
                    out validatedArguments,
                    out var importedUtilities))
            {
                return importedUtilities;
            }
        }

        resolvedUtilityName = GetTailwindUtilityCandidateName(
            attribute,
            utilityName,
            GetStaticTailwindNameSegmentCount(attribute));
        diagnosticsBuilder.Add(SemanticModel.CreateTailwindUtilityNotFoundDiagnostic(
            attribute,
            resolvedUtilityName,
            containingComponent));
        return ImmutableArray<ITailwindUtilitySymbol>.Empty;
    }

    private bool TryResolveTailwindUtilityDeclarationLayer(
        ImmutableArray<AkcssUtilityDeclarationSyntax> declarations,
        TailwindAttributeSyntax attribute,
        string utilityName,
        ImmutableArray<BoundTailwindUtilityArgument> arguments,
        IMarkupComponentSymbol? containingComponent,
        ImmutableArrayBuilder<AkburaSemanticDiagnostic> diagnosticsBuilder,
        out string resolvedUtilityName,
        out ImmutableArray<BoundTailwindUtilityArgument> validatedArguments,
        out ImmutableArray<ITailwindUtilitySymbol> utilities)
    {
        var staticSegmentCount = GetStaticTailwindNameSegmentCount(attribute);
        for (var consumedSegmentCount = staticSegmentCount;
             consumedSegmentCount >= 0;
             consumedSegmentCount--)
        {
            var candidateName = GetTailwindUtilityCandidateName(
                attribute,
                utilityName,
                consumedSegmentCount);
            var candidateArguments = GetTailwindUtilityArgumentSuffix(
                arguments,
                consumedSegmentCount);
            var candidates = FindTailwindUtilityCandidates(
                declarations,
                candidateName,
                candidateArguments.Length,
                containingComponent);
            if (TryResolveTailwindUtilityLayer(
                    attribute,
                    candidateName,
                    candidates,
                    candidateArguments,
                    containingComponent,
                    diagnosticsBuilder,
                    out validatedArguments,
                    out utilities))
            {
                resolvedUtilityName = candidateName;
                return true;
            }
        }

        resolvedUtilityName = utilityName;
        validatedArguments = arguments;
        utilities = ImmutableArray<ITailwindUtilitySymbol>.Empty;
        return false;
    }

    private static int GetStaticTailwindNameSegmentCount(TailwindAttributeSyntax attribute)
    {
        if (attribute.Kind != AkburaSyntaxKind.TailwindFullAttributeSyntax)
        {
            return 0;
        }

        var fullAttribute = Unsafe.As<TailwindFullAttributeSyntax>(attribute);
        var count = 0;
        foreach (var segment in fullAttribute.Segments)
        {
            if (segment.Kind is not (
                    AkburaSyntaxKind.TailwindIdentifierSegmentSyntax or
                    AkburaSyntaxKind.TailwindNumericSegmentSyntax))
            {
                break;
            }

            count++;
        }

        return count;
    }

    private static string GetTailwindUtilityCandidateName(
        TailwindAttributeSyntax attribute,
        string utilityName,
        int consumedSegmentCount)
    {
        if (consumedSegmentCount == 0 ||
            attribute.Kind != AkburaSyntaxKind.TailwindFullAttributeSyntax)
        {
            return utilityName;
        }

        var fullAttribute = Unsafe.As<TailwindFullAttributeSyntax>(attribute);
        var builder = new StringBuilder(utilityName);
        for (var index = 0; index < consumedSegmentCount; index++)
        {
            builder.Append('-');
            builder.Append(fullAttribute.Segments[index].ToFullString().Trim());
        }

        return builder.ToString();
    }

    private static ImmutableArray<BoundTailwindUtilityArgument> GetTailwindUtilityArgumentSuffix(
        ImmutableArray<BoundTailwindUtilityArgument> arguments,
        int consumedSegmentCount)
    {
        if (consumedSegmentCount == 0)
        {
            return arguments;
        }

        if (consumedSegmentCount == arguments.Length)
        {
            return ImmutableArray<BoundTailwindUtilityArgument>.Empty;
        }

        using var builder = ImmutableArrayBuilder<BoundTailwindUtilityArgument>.Rent(
            arguments.Length - consumedSegmentCount);
        for (var index = consumedSegmentCount; index < arguments.Length; index++)
        {
            builder.Add(arguments[index]);
        }

        return builder.ToImmutable();
    }

    private bool TryResolveTailwindUtilityLayer(
        TailwindAttributeSyntax attribute,
        string utilityName,
        ImmutableArray<ITailwindUtilitySymbol> candidates,
        ImmutableArray<BoundTailwindUtilityArgument> arguments,
        IMarkupComponentSymbol? containingComponent,
        ImmutableArrayBuilder<AkburaSemanticDiagnostic> diagnosticsBuilder,
        out ImmutableArray<BoundTailwindUtilityArgument> validatedArguments,
        out ImmutableArray<ITailwindUtilitySymbol> utilities)
    {
        validatedArguments = arguments;
        utilities = ImmutableArray<ITailwindUtilitySymbol>.Empty;
        if (candidates.IsDefaultOrEmpty)
        {
            return false;
        }

        if (HasDuplicateAkcssSelector(candidates))
        {
            diagnosticsBuilder.Add(SemanticModel.CreateTailwindUtilityAmbiguousDiagnostic(
                attribute,
                utilityName,
                containingComponent));
            return true;
        }

        using var builder = ImmutableArrayBuilder<ITailwindUtilitySymbol>.Rent(candidates.Length);
        var hasValidatedArguments = false;
        foreach (var candidate in candidates)
        {
            var utility = ValidateTailwindUtilityArguments(
                attribute,
                candidate,
                arguments,
                diagnosticsBuilder,
                out var candidateArguments);
            if (utility == null)
            {
                utilities = ImmutableArray<ITailwindUtilitySymbol>.Empty;
                return true;
            }

            if (!hasValidatedArguments)
            {
                validatedArguments = candidateArguments;
                hasValidatedArguments = true;
            }

            builder.Add(utility);
        }

        utilities = builder.ToImmutable();
        return true;
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
            if (IsAkcssTargetCompatible(symbol, containingComponent))
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
            diagnosticsBuilder.Add(SemanticModel.CreateTailwindUtilityArgumentMismatchDiagnostic(
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

            diagnosticsBuilder.Add(SemanticModel.CreateTailwindUtilityArgumentMismatchDiagnostic(
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
            : SemanticModel.BindMarkupAttributeExpression(argument.Syntax, expression);

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
                    return AkburaSemanticModel.TryGetExpectedTypeMemberName(expression, enumType, out memberName);
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
        if (!SemanticModel.TryResolveAkcssTargetType(utilityDeclaration.Selector.TargetType, out var targetType))
        {
            targetType = default;
        }

        var symbol = new TailwindUtilitySymbol(
            utilityDeclaration,
            targetType,
            SemanticModel.CreateTailwindUtilityParameters(utilityDeclaration),
            ImmutableArray<IAkcssOperation>.Empty);
        symbol.SetOperations(SemanticModel.CreateAkcssOperations(utilityDeclaration.Members, symbol));
        return symbol;
    }

    private bool IsAkcssTargetCompatible(
        IAkcssSymbol symbol,
        IMarkupComponentSymbol? containingComponent)
    {
        if (!symbol.HasTargetType)
        {
            return true;
        }

        return containingComponent?.ComponentType != null &&
            symbol.TargetType.Symbol is ITypeSymbol targetType &&
            AkburaSemanticModel.IsAssignableTo(containingComponent.ComponentType, targetType);
    }

    private ImmutableArray<IAkcssSymbol> ResolveAkcssClassSymbolsForAttribute(
        MarkupAttributeSyntax attribute,
        string literalValue,
        IMarkupComponentSymbol? containingComponent,
        ImmutableArrayBuilder<AkburaSemanticDiagnostic> diagnosticsBuilder)
    {
        if (!IsClassAttribute(attribute))
        {
            return ImmutableArray<IAkcssSymbol>.Empty;
        }

        using var builder = ImmutableArrayBuilder<IAkcssSymbol>.Rent();
        foreach (var className in literalValue.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var symbol in ResolveAkcssClassNameSymbols(
                         attribute,
                         className,
                         containingComponent,
                         diagnosticsBuilder))
            {
                builder.Add(symbol);
            }
        }

        return builder.ToImmutable();
    }

    private ImmutableArray<IAkcssSymbol> ResolveAkcssClassNameSymbols(
        MarkupAttributeSyntax attribute,
        string className,
        IMarkupComponentSymbol? containingComponent,
        ImmutableArrayBuilder<AkburaSemanticDiagnostic> diagnosticsBuilder)
    {
        var localCandidates = FindAkcssStyleCandidates(
            GetLocalAkcssStyleDeclarations(),
            className,
            containingComponent);
        if (localCandidates.Length > 0)
        {
            return HasDuplicateAkcssSelector(localCandidates)
                ? ImmutableArray<IAkcssSymbol>.Empty
                : localCandidates;
        }

        foreach (var importLayer in GetImportedAkcssStyleDeclarationLayers(diagnosticsBuilder))
        {
            var candidates = FindAkcssStyleCandidates(
                importLayer,
                className,
                containingComponent);
            if (candidates.Length > 0)
            {
                return HasDuplicateAkcssSelector(candidates)
                    ? ImmutableArray<IAkcssSymbol>.Empty
                    : candidates;
            }
        }

        return ImmutableArray<IAkcssSymbol>.Empty;
    }

    private ImmutableArray<IAkcssSymbol> FindAkcssStyleCandidates(
        ImmutableArray<AkcssStyleRuleSyntax> declarations,
        string className,
        IMarkupComponentSymbol? containingComponent)
    {
        using var builder = ImmutableArrayBuilder<IAkcssSymbol>.Rent();
        foreach (var declaration in declarations)
        {
            if (declaration.Selector.Name?.Identifier.ValueText != className)
            {
                continue;
            }

            var symbol = CreateAkcssStyleSymbol(declaration);
            if (IsAkcssTargetCompatible(symbol, containingComponent))
            {
                builder.Add(symbol);
            }
        }

        return builder.ToImmutable();
    }

    private IAkcssSymbol CreateAkcssStyleSymbol(
        AkcssStyleRuleSyntax styleRule)
    {
        if (!SemanticModel.TryResolveAkcssTargetType(styleRule.Selector.TargetType, out var targetType))
        {
            targetType = default;
        }

        var symbol = new AkcssStyleSymbol(
            styleRule,
            targetType,
            ImmutableArray<IAkcssOperation>.Empty);
        symbol.SetOperations(SemanticModel.CreateAkcssOperations(styleRule.Members, symbol));
        return symbol;
    }

    private static bool IsClassAttribute(MarkupAttributeSyntax attribute)
    {
        return attribute.Kind == AkburaSyntaxKind.MarkupPlainAttributeSyntax &&
            Unsafe.As<MarkupPlainAttributeSyntax>(attribute).Name.Identifier.ValueText == "class";
    }

    private static bool HasDuplicateAkcssSelector<TSymbol>(ImmutableArray<TSymbol> symbols)
        where TSymbol : IAkcssSymbol
    {
        for (var leftIndex = 0; leftIndex < symbols.Length; leftIndex++)
        {
            for (var rightIndex = leftIndex + 1; rightIndex < symbols.Length; rightIndex++)
            {
                if (SameAkcssSelector(symbols[leftIndex], symbols[rightIndex]))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool SameAkcssSelector(
        IAkcssSymbol left,
        IAkcssSymbol right)
    {
        if (left.HasTargetType != right.HasTargetType ||
            left.Name != right.Name)
        {
            return false;
        }

        if (!left.HasTargetType)
        {
            return true;
        }

        return SymbolEqualityComparer.Default.Equals(left.TargetType.Symbol, right.TargetType.Symbol);
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

    private ImmutableArray<AkcssStyleRuleSyntax> GetLocalAkcssStyleDeclarations()
    {
        using var builder = ImmutableArrayBuilder<AkcssStyleRuleSyntax>.Rent();
        AddInlineAkcssStyleDeclarations(builder);

        var companion = GetCompanionAkcssSyntaxTree();
        if (companion != null)
        {
            AddAkcssDocumentStyleDeclarations(companion.GetRoot(), builder);
        }

        return builder.ToImmutable();
    }

    private ImmutableArray<ImmutableArray<AkcssUtilityDeclarationSyntax>> GetImportedAkcssUtilityDeclarationLayers(
        ImmutableArrayBuilder<AkburaSemanticDiagnostic> diagnosticsBuilder)
    {
        using var layersBuilder = ImmutableArrayBuilder<ImmutableArray<AkcssUtilityDeclarationSyntax>>.Rent();
        foreach (var importName in GetAkcssImportNames())
        {
            var matches = Compilation.GetAkcssSyntaxTreesByLogicalName(importName);
            if (matches.Length == 0)
            {
                diagnosticsBuilder.Add(SemanticModel.CreateAkcssImportNotFoundDiagnostic(importName));
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

    private ImmutableArray<ImmutableArray<AkcssStyleRuleSyntax>> GetImportedAkcssStyleDeclarationLayers(
        ImmutableArrayBuilder<AkburaSemanticDiagnostic> diagnosticsBuilder)
    {
        using var layersBuilder = ImmutableArrayBuilder<ImmutableArray<AkcssStyleRuleSyntax>>.Rent();
        foreach (var importName in GetAkcssImportNames())
        {
            var matches = Compilation.GetAkcssSyntaxTreesByLogicalName(importName);
            if (matches.Length == 0)
            {
                diagnosticsBuilder.Add(SemanticModel.CreateAkcssImportNotFoundDiagnostic(importName));
                continue;
            }

            using var layerBuilder = ImmutableArrayBuilder<AkcssStyleRuleSyntax>.Rent();
            foreach (var tree in matches)
            {
                AddAkcssDocumentStyleDeclarations(tree.GetRoot(), layerBuilder);
            }

            layersBuilder.Add(layerBuilder.ToImmutable());
        }

        return layersBuilder.ToImmutable();
    }

    private void AddInlineAkcssStyleDeclarations(
        ImmutableArrayBuilder<AkcssStyleRuleSyntax> builder)
    {
        foreach (var block in SemanticModel.SyntaxTree.GetRoot().Members.OfType<InlineAkcssBlockSyntax>())
        {
            foreach (var rule in block.Members.OfType<AkcssStyleRuleSyntax>())
            {
                builder.Add(rule);
            }
        }
    }

    private static void AddAkcssDocumentStyleDeclarations(
        AkcssDocumentSyntax document,
        ImmutableArrayBuilder<AkcssStyleRuleSyntax> builder)
    {
        foreach (var rule in document.Members.OfType<AkcssStyleRuleSyntax>())
        {
            builder.Add(rule);
        }
    }

    private void AddInlineAkcssUtilityDeclarations(
        ImmutableArrayBuilder<AkcssUtilityDeclarationSyntax> builder)
    {
        foreach (var block in SemanticModel.SyntaxTree.GetRoot().Members.OfType<InlineAkcssBlockSyntax>())
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
        if (string.IsNullOrWhiteSpace(SemanticModel.SyntaxTree.FilePath) ||
            string.IsNullOrWhiteSpace(SemanticModel.SyntaxTree.ComponentName))
        {
            return null;
        }

        var directory = Path.GetDirectoryName(SemanticModel.SyntaxTree.FilePath);
        var expectedPath = string.IsNullOrWhiteSpace(directory)
            ? SemanticModel.SyntaxTree.ComponentName + ".akcss"
            : Path.Combine(directory, SemanticModel.SyntaxTree.ComponentName + ".akcss");

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
        foreach (var member in SemanticModel.SyntaxTree.GetRoot().Members)
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

}
