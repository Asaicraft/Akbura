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
using AkburaCandidateReason = Akbura.Language.Symbols.CandidateReason;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;
using CSharpSyntaxFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using AkburaOperation = Akbura.Language.Operations.IOperation;

namespace Akbura.Language;

internal sealed partial class AkburaSemanticModel
{
    private AkburaOperation? ResolveMarkupAttributeOperation(MarkupAttributeSyntax markupAttribute)
    {
        return markupAttribute switch
        {
            TailwindAttributeSyntax tailwindAttribute => ResolveTailwindUtilityAttributeOperation(tailwindAttribute),
            MarkupPlainAttributeSyntax or MarkupPrefixedAttributeSyntax => ResolveMarkupPropertySetterOperation(markupAttribute),
            _ => null,
        };
    }

    private AkburaOperation? ResolveMarkupPropertySetterOperation(MarkupAttributeSyntax markupAttribute)
    {
        var propertyInfo = GetSymbolInfo(markupAttribute);
        var property = propertyInfo.Symbol as Symbols.IPropertySymbol;
        var containingComponent = GetContainingMarkupComponentSymbol(markupAttribute);
        var valueSyntax = GetMarkupAttributeValue(markupAttribute);
        var bindingKind = GetMarkupAttributeBindingKind(markupAttribute);
        var valueKind = MarkupAttributeValueKind.None;
        var literalValue = default(string);
        var valueType = default(CSharpSymbolDefinition);
        var valueOperation = default(CSharpOperationDefinition);

        if (valueSyntax is MarkupLiteralAttributeValueSyntax literalValueSyntax)
        {
            valueKind = MarkupAttributeValueKind.Literal;
            literalValue = GetMarkupLiteralAttributeValueText(literalValueSyntax);
            var binding = BindMarkupAttributeExpression(CSharpSyntaxFactory.LiteralExpression(
                Microsoft.CodeAnalysis.CSharp.SyntaxKind.StringLiteralExpression,
                CSharpSyntaxFactory.Literal(literalValue)));
            valueType = binding.TypeSymbol == null ? default : new CSharpSymbolDefinition(binding.TypeSymbol);
            valueOperation = binding.OperationDefinition;
        }
        else if (valueSyntax is MarkupDynamicAttributeValueSyntax dynamicValueSyntax)
        {
            var expression = ParseInlineExpression(dynamicValueSyntax.Expression);
            if (expression == null)
            {
                valueKind = MarkupAttributeValueKind.Error;
            }
            else
            {
                valueKind = MarkupAttributeValueKind.DynamicExpression;
                var binding = BindMarkupAttributeExpression(expression);
                valueType = binding.TypeSymbol == null ? default : new CSharpSymbolDefinition(binding.TypeSymbol);
                valueOperation = binding.OperationDefinition;
            }
        }

        ImmutableArray<AkburaSemanticDiagnostic> diagnostics;
        if (property == null)
        {
            diagnostics = _semanticDiagnosticsCache.TryGetValue(markupAttribute, out var cachedDiagnostics)
                ? cachedDiagnostics
                : ImmutableArray<AkburaSemanticDiagnostic>.Empty;
        }
        else
        {
            using var diagnosticsBuilder = ImmutableArrayBuilder<AkburaSemanticDiagnostic>.Rent();
            AddMarkupAttributeBindingDiagnostics(
                markupAttribute,
                property,
                bindingKind,
                diagnosticsBuilder);
            AddDuplicateMarkupPropertySetterDiagnostics(
                markupAttribute,
                property,
                bindingKind,
                diagnosticsBuilder);
            if (valueSyntax != null && valueKind == MarkupAttributeValueKind.DynamicExpression)
            {
                AddMarkupAttributeValueDiagnostics(
                    markupAttribute,
                    property,
                    valueType,
                    diagnosticsBuilder);
            }

            diagnostics = diagnosticsBuilder.ToImmutable();
            SetSemanticDiagnostics(markupAttribute, diagnostics);
        }

        return new MarkupPropertySetterOperation(
            markupAttribute,
            containingComponent,
            property,
            valueType,
            valueOperation,
            bindingKind,
            valueKind,
            valueSyntax,
            literalValue,
            property == null || valueKind == MarkupAttributeValueKind.Error || diagnostics.Length > 0);
    }

    private AkburaOperation ResolveTailwindUtilityAttributeOperation(TailwindAttributeSyntax attribute)
    {
        using var diagnosticsBuilder = ImmutableArrayBuilder<AkburaSemanticDiagnostic>.Rent();

        var containingComponent = GetContainingMarkupComponentSymbol(attribute);
        var componentName = containingComponent?.Name ?? "<unknown>";
        var utilityName = GetTailwindUtilityName(attribute);
        var arguments = CreateTailwindUtilityArguments(attribute);
        var condition = CreateTailwindCondition(attribute);
        var utility = ResolveTailwindUtilityForAttribute(
            attribute,
            utilityName,
            arguments,
            containingComponent,
            diagnosticsBuilder);

        var diagnostics = diagnosticsBuilder.ToImmutable();
        SetSemanticDiagnostics(attribute, diagnostics);

        return new TailwindUtilityAttributeOperation(
            attribute,
            containingComponent,
            utilityName,
            utility,
            arguments,
            condition.HasCondition,
            condition.Text,
            condition.Type,
            condition.Operation,
            hasErrors: utility == null || diagnostics.Length > 0 || componentName.Length == 0);
    }

    private ITailwindUtilitySymbol? ResolveTailwindUtilityForAttribute(
        TailwindAttributeSyntax attribute,
        string utilityName,
        ImmutableArray<TailwindUtilityArgument> arguments,
        IMarkupComponentSymbol? containingComponent,
        ImmutableArrayBuilder<AkburaSemanticDiagnostic> diagnosticsBuilder)
    {
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
                diagnosticsBuilder);
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
                    diagnosticsBuilder);
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
        ImmutableArray<TailwindUtilityArgument> arguments,
        ImmutableArrayBuilder<AkburaSemanticDiagnostic> diagnosticsBuilder)
    {
        if (utility.Parameters.Length != arguments.Length)
        {
            diagnosticsBuilder.Add(CreateTailwindUtilityArgumentMismatchDiagnostic(
                attribute,
                utility,
                arguments.Length));
            return null;
        }

        for (var i = 0; i < arguments.Length; i++)
        {
            var argumentType = arguments[i].Type.Symbol as ITypeSymbol;
            var parameterType = utility.Parameters[i].Type.Symbol as ITypeSymbol;
            if (argumentType == null ||
                parameterType == null ||
                Compilation.CSharpCompilation.ClassifyConversion(argumentType, parameterType).IsImplicit)
            {
                continue;
            }

            diagnosticsBuilder.Add(CreateTailwindUtilityArgumentMismatchDiagnostic(
                attribute,
                utility,
                arguments.Length));
            return null;
        }

        return utility;
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
            if (member is UsingDirectiveSyntax usingDirective &&
                TryGetAkcssImportName(usingDirective, out var importName))
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

    private IMarkupComponentSymbol? GetContainingMarkupComponentSymbol(MarkupAttributeSyntax markupAttribute)
    {
        var markupElement = GetContainingMarkupElement(markupAttribute);
        return markupElement == null
            ? null
            : GetSymbolInfo(markupElement).Symbol as IMarkupComponentSymbol;
    }

    private static MarkupAttributeValueSyntax? GetMarkupAttributeValue(MarkupAttributeSyntax markupAttribute)
    {
        return markupAttribute switch
        {
            MarkupPlainAttributeSyntax plainAttribute => plainAttribute.Value,
            MarkupPrefixedAttributeSyntax prefixedAttribute => prefixedAttribute.Value,
            _ => null,
        };
    }

    private static MarkupAttributeBindingKind GetMarkupAttributeBindingKind(MarkupAttributeSyntax markupAttribute)
    {
        return markupAttribute is MarkupPrefixedAttributeSyntax prefixedAttribute
            ? prefixedAttribute.Prefix.Kind switch
            {
                Akbura.Language.Syntax.SyntaxKind.BindToken => MarkupAttributeBindingKind.Bind,
                Akbura.Language.Syntax.SyntaxKind.OutToken => MarkupAttributeBindingKind.Out,
                _ => MarkupAttributeBindingKind.None,
            }
            : MarkupAttributeBindingKind.None;
    }

    private static string GetMarkupLiteralAttributeValueText(MarkupLiteralAttributeValueSyntax literalValue)
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

    private static CSharp.ExpressionSyntax? ParseInlineExpression(InlineExpressionSyntax inlineExpression)
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

    private static void AddMarkupAttributeBindingDiagnostics(
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

    private static void AddDuplicateMarkupPropertySetterDiagnostics(
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

    private static MarkupStartTagSyntax? GetContainingMarkupStartTag(MarkupAttributeSyntax markupAttribute)
    {
        for (var node = markupAttribute.Parent; node != null; node = node.Parent)
        {
            if (node is MarkupStartTagSyntax startTag)
            {
                return startTag;
            }
        }

        return null;
    }

    private void AddMarkupAttributeValueDiagnostics(
        MarkupAttributeSyntax markupAttribute,
        Symbols.IPropertySymbol property,
        CSharpSymbolDefinition valueType,
        ImmutableArrayBuilder<AkburaSemanticDiagnostic> diagnosticsBuilder)
    {
        if (valueType.Symbol is not ITypeSymbol sourceType ||
            property.Type.Symbol is not ITypeSymbol targetType ||
            Compilation.CSharpCompilation.ClassifyConversion(sourceType, targetType).IsImplicit)
        {
            return;
        }

        var sourceTypeText = sourceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var targetTypeText = targetType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        diagnosticsBuilder.Add(new AkburaSemanticDiagnostic(
            markupAttribute,
            ErrorCodes.AKBURA_SEMANTIC_MarkupAttributeValueCannotConvert,
            [property.Name, sourceTypeText, targetTypeText]));
    }

    private CSharpTypeBinding BindMarkupAttributeExpression(CSharp.ExpressionSyntax expressionSyntax)
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

        var compilationUnit = CSharpSyntaxFactory.CompilationUnit()
            .WithExterns(CSharpSyntaxFactory.List(GetCSharpExternAliases()))
            .WithUsings(CSharpSyntaxFactory.List(GetCSharpUsingDirectives()));

        var namespaceDeclaration = GetCSharpNamespaceDeclaration();
        if (namespaceDeclaration != null)
        {
            compilationUnit = compilationUnit.WithMembers(
                CSharpSyntaxFactory.SingletonList<CSharp.MemberDeclarationSyntax>(
                    namespaceDeclaration.WithMembers(
                        CSharpSyntaxFactory.SingletonList<CSharp.MemberDeclarationSyntax>(probeClass))));
        }
        else
        {
            compilationUnit = compilationUnit.WithMembers(
                CSharpSyntaxFactory.SingletonList<CSharp.MemberDeclarationSyntax>(probeClass));
        }

        var parseOptions = Compilation.CSharpCompilation.SyntaxTrees.FirstOrDefault()?.Options as CSharpParseOptions ??
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

        var syntaxTree = CSharpSyntaxTree.Create(compilationUnit, parseOptions);
        var probeCompilation = Compilation.CSharpCompilation.AddSyntaxTrees(syntaxTree);
        var semanticModel = probeCompilation.GetSemanticModel(syntaxTree);
        var probeExpression = syntaxTree
            .GetCompilationUnitRoot()
            .DescendantNodes()
            .OfType<CSharp.ReturnStatementSyntax>()
            .Single()
            .Expression;

        if (probeExpression == null)
        {
            return CSharpTypeBinding.Empty;
        }

        var typeInfo = semanticModel.GetTypeInfo(probeExpression);
        var symbolInfo = semanticModel.GetSymbolInfo(probeExpression);
        var operation = semanticModel.GetOperation(probeExpression);
        var receiverType = GetExpressionReceiverType(semanticModel, probeExpression);
        var typeSymbol = typeInfo.Type?.TypeKind == TypeKind.Error
            ? null
            : typeInfo.Type;

        return new CSharpTypeBinding(
            typeSymbol,
            symbolInfo.Symbol,
            receiverType,
            isBindingPath: true,
            symbolInfo.CandidateSymbols,
            symbolInfo.CandidateReason == Microsoft.CodeAnalysis.CandidateReason.Ambiguous
                ? AkburaCandidateReason.Ambiguous
                : AkburaCandidateReason.NotFound,
            operation == null ? default : new CSharpOperationDefinition(operation));
    }

    private ImmutableArray<CSharp.MemberDeclarationSyntax> CreateMarkupAttributeProbeFields()
    {
        using var builder = ImmutableArrayBuilder<CSharp.MemberDeclarationSyntax>.Rent();
        foreach (var member in SyntaxTree.GetRoot().Members)
        {
            switch (member)
            {
                case StateDeclarationSyntax stateDeclaration:
                    if (TryCreateStateProbeField(stateDeclaration, out var stateField))
                    {
                        builder.Add(stateField);
                    }

                    break;

                case ParamDeclarationSyntax paramDeclaration:
                    if (TryCreateParamProbeField(paramDeclaration, out var paramField))
                    {
                        builder.Add(paramField);
                    }

                    break;

                case InjectDeclarationSyntax injectDeclaration:
                    if (TryCreateInjectProbeField(injectDeclaration, out var injectField))
                    {
                        builder.Add(injectField);
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
        return attribute switch
        {
            TailwindFlagAttributeSyntax flag => flag.Name.Identifier.ValueText,
            TailwindFullAttributeSyntax full => full.Name.Identifier.ValueText,
            _ => string.Empty,
        };
    }

    private ImmutableArray<TailwindUtilityArgument> CreateTailwindUtilityArguments(TailwindAttributeSyntax attribute)
    {
        if (attribute is not TailwindFullAttributeSyntax fullAttribute ||
            fullAttribute.Segments.Count == 0)
        {
            return ImmutableArray<TailwindUtilityArgument>.Empty;
        }

        using var builder = ImmutableArrayBuilder<TailwindUtilityArgument>.Rent();
        foreach (var segment in fullAttribute.Segments)
        {
            builder.Add(CreateTailwindUtilityArgument(segment));
        }

        return builder.ToImmutable();
    }

    private TailwindUtilityArgument CreateTailwindUtilityArgument(TailwindSegmentSyntax segment)
    {
        CSharp.ExpressionSyntax? expression = segment switch
        {
            TailwindNumericSegmentSyntax numeric => CSharpSyntaxFactory.ParseExpression(numeric.Number.ToFullString()),
            TailwindIdentifierSegmentSyntax identifier => CSharpSyntaxFactory.LiteralExpression(
                Microsoft.CodeAnalysis.CSharp.SyntaxKind.StringLiteralExpression,
                CSharpSyntaxFactory.Literal(identifier.Name.Identifier.ValueText)),
            TailwindExpressionSegmentSyntax expressionSegment => ParseInlineExpression(expressionSegment.Expression),
            _ => null,
        };

        var binding = expression == null
            ? CSharpTypeBinding.Empty
            : BindMarkupAttributeExpression(expression);

        return new TailwindUtilityArgument(
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
        if (attribute is not TailwindFullAttributeSyntax fullAttribute ||
            fullAttribute.Prefix is not ExpressionConditionalPrefixSyntax expressionPrefix)
        {
            return (false, null, default, default);
        }

        var expression = ParseInlineExpression(expressionPrefix.Expression);
        if (expression == null)
        {
            return (true, expressionPrefix.Expression.ToFullString(), default, default);
        }

        var binding = BindMarkupAttributeExpression(expression);
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

    private AkburaSemanticDiagnostic CreateAkcssImportNotFoundDiagnostic(string importName)
    {
        return new AkburaSemanticDiagnostic(
            SyntaxTree.GetRoot(),
            ErrorCodes.AKBURA_SEMANTIC_AkcssImportNotFound,
            [importName]);
    }
}
