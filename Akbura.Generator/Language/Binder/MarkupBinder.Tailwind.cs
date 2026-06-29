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

namespace Akbura.Language.Binder;

internal sealed partial class MarkupBinder
{
    internal BoundNode CreateBoundTailwindUtilityAttribute(TailwindAttributeSyntax attribute)
    {
        var diagnosticsBag = new BindingDiagnosticBag();

        var containingComponent = SemanticModel.GetContainingMarkupComponentSymbol(attribute);
        var componentName = containingComponent?.Name ?? "<unknown>";
        var utilityName = AkburaSemanticModel.GetTailwindUtilityName(attribute);
        var arguments = SemanticModel.CreateTailwindUtilityArguments(attribute);
        var validatedArguments = arguments;
        var condition = SemanticModel.CreateTailwindCondition(attribute);
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
            SemanticModel.AddTailwindExpressionDiagnostics(attribute, validatedArguments, diagnosticsBuilder);
            diagnosticsBag.AddRange(diagnosticsBuilder.ToImmutable());
        }

        var diagnostics = SemanticModel.SetSemanticDiagnostics(attribute, diagnosticsBag);

        return new BoundTailwindUtilityAttribute(
            attribute,
            this,
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
            diagnosticsBuilder.Add(SemanticModel.CreateTailwindUtilityAmbiguousDiagnostic(
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
                diagnosticsBuilder.Add(SemanticModel.CreateTailwindUtilityAmbiguousDiagnostic(
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

        diagnosticsBuilder.Add(SemanticModel.CreateTailwindUtilityNotFoundDiagnostic(
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
            AkburaSemanticModel.IsAssignableTo(containingComponent.ComponentType, targetType);
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
