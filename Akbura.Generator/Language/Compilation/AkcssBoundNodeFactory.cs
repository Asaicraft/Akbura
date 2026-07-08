using Akbura.Language.Binder;
using Akbura.Language.BoundTree;
using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using AkburaCandidateReason = Akbura.Language.Symbols.CandidateReason;
using AkburaPropertySymbol = Akbura.Language.Symbols.IPropertySymbol;
using AkburaSyntaxKind = Akbura.Language.Syntax.SyntaxKind;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;
using CSharpSyntaxFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using CSharpSyntaxKind = Microsoft.CodeAnalysis.CSharp.SyntaxKind;

namespace Akbura.Language;

internal sealed class AkcssBoundNodeFactory
{
    private readonly AkburaSemanticModel _semanticModel;

    public AkcssBoundNodeFactory(AkburaSemanticModel semanticModel)
    {
        _semanticModel = semanticModel ?? throw new ArgumentNullException(nameof(semanticModel));
    }

    public BoundNode CreateSyntax(AkburaSyntax syntax)
    {
        return syntax.Kind switch
        {
            AkburaSyntaxKind.InlineAkcssBlockSyntax =>
                CreateInlineModule(Unsafe.As<InlineAkcssBlockSyntax>(syntax)),
            AkburaSyntaxKind.AkcssDocumentSyntax =>
                CreateExternalModule(Unsafe.As<AkcssDocumentSyntax>(syntax)),
            AkburaSyntaxKind.AkcssStyleRuleSyntax =>
                CreateStyle(Unsafe.As<AkcssStyleRuleSyntax>(syntax)),
            AkburaSyntaxKind.AkcssUtilityDeclarationSyntax =>
                CreateUtility(Unsafe.As<AkcssUtilityDeclarationSyntax>(syntax)),
            _ => new BoundDeclaration(
                syntax,
                _semanticModel.GetBinder(syntax, BinderUsage.Akcss),
                AkburaSymbolInfo.None(AkburaCandidateReason.UnsupportedSyntax)),
        };
    }

    public ImmutableArray<BoundAkcssOperation> CreateBoundOperations(
        Akbura.Language.Syntax.SyntaxList<AkcssBodyMemberSyntax> members,
        IAkcssSymbol containingSymbol)
    {
        using var builder = ImmutableArrayBuilder<BoundAkcssOperation>.Rent();
        foreach (var member in members)
        {
            if (_semanticModel.TryGetCachedBoundNode(member, out var cachedBoundNode) &&
                cachedBoundNode is BoundAkcssOperation cachedOperation)
            {
                builder.Add(cachedOperation);
                continue;
            }

            BoundNode? boundNode = member.Kind switch
            {
                AkburaSyntaxKind.AkcssAssignmentSyntax =>
                    CreatePropertySetter(
                        Unsafe.As<AkcssAssignmentSyntax>(member),
                        containingSymbol),
                AkburaSyntaxKind.AkcssIfDirectiveSyntax =>
                    CreateIf(
                        Unsafe.As<AkcssIfDirectiveSyntax>(member),
                        containingSymbol),
                AkburaSyntaxKind.AkcssApplyDirectiveSyntax =>
                    CreateApply(
                        Unsafe.As<AkcssApplyDirectiveSyntax>(member),
                        containingSymbol),
                AkburaSyntaxKind.AkcssInterceptDirectiveSyntax =>
                    CreateIntercept(
                        Unsafe.As<AkcssInterceptDirectiveSyntax>(member),
                        containingSymbol),
                _ => null,
            };

            if (boundNode is BoundAkcssOperation operation)
            {
                _semanticModel.SetCachedBoundNode(member, operation);
                builder.Add(operation);
            }
        }

        return builder.ToImmutable();
    }

    public BoundAkcssIf CreateIf(
        AkcssIfDirectiveSyntax ifDirective,
        IAkcssSymbol containingSymbol)
    {
        var expression = ParseAkcssConditionExpression(ifDirective);
        var binding = expression == null
            ? CSharpBindingResult.Empty
            : _semanticModel.BindAkcssExpression(expression, containingSymbol);
        var conditionType = binding.TypeSymbol == null
            ? default
            : new CSharpSymbolDefinition(binding.TypeSymbol);
        var operations = CreateBoundOperations(ifDirective.Members, containingSymbol);

        var diagnosticsBag = new BindingDiagnosticBag();
        {
            using var diagnosticsBuilder = ImmutableArrayBuilder<AkburaSemanticDiagnostic>.Rent();
            _semanticModel.AddAkcssExpressionDiagnostics(
                ifDirective,
                ifDirective.Condition.ToFullString().Trim(),
                binding,
                diagnosticsBuilder);
            diagnosticsBag.AddRange(diagnosticsBuilder.ToImmutable());
        }
        var diagnostics = _semanticModel.SetSemanticDiagnostics(ifDirective, diagnosticsBag);

        return new BoundAkcssIf(
            ifDirective,
            _semanticModel.GetBinder(ifDirective, BinderUsage.Akcss),
            containingSymbol,
            conditionType,
            binding.OperationDefinition,
            operations,
            diagnostics,
            hasErrors: expression == null ||
                diagnostics.Length > 0 ||
                operations.Any(static operation => operation.HasErrors));
    }

    public BoundAkcssApply CreateApply(
        AkcssApplyDirectiveSyntax applyDirective,
        IAkcssSymbol containingSymbol)
    {
        var diagnosticsBag = new BindingDiagnosticBag();
        using var itemsBuilder = ImmutableArrayBuilder<string>.Rent();
        using var symbolsBuilder = ImmutableArrayBuilder<IAkcssSymbol>.Rent();

        foreach (var item in GetAkcssApplyItems(applyDirective))
        {
            itemsBuilder.Add(item);
            using var diagnosticsBuilder = ImmutableArrayBuilder<AkburaSemanticDiagnostic>.Rent();
            var resolved = _semanticModel.ResolveAkcssApplyItem(
                    applyDirective,
                    item,
                    containingSymbol,
                    diagnosticsBuilder);
            diagnosticsBag.AddRange(diagnosticsBuilder.ToImmutable());
            if (resolved != null)
            {
                symbolsBuilder.Add(resolved);
            }
        }

        var diagnostics = _semanticModel.SetSemanticDiagnostics(applyDirective, diagnosticsBag);

        return new BoundAkcssApply(
            applyDirective,
            _semanticModel.GetBinder(applyDirective, BinderUsage.Akcss),
            containingSymbol,
            itemsBuilder.ToImmutable(),
            symbolsBuilder.ToImmutable(),
            diagnostics,
            hasErrors: diagnostics.Length > 0);
    }

    public BoundAkcssIntercept CreateIntercept(
        AkcssInterceptDirectiveSyntax interceptDirective,
        IAkcssSymbol containingSymbol)
    {
        var diagnosticsBag = new BindingDiagnosticBag();
        var interceptType = default(CSharpSymbolDefinition);

        CSharp.TypeSyntax typeSyntax;
        try
        {
            typeSyntax = interceptDirective.Type.ToCSharp();
        }
        catch (InvalidOperationException)
        {
            typeSyntax = CSharpSyntaxFactory.ParseTypeName(interceptDirective.Type.ToFullString());
        }

        var binding = _semanticModel.BindCSharpType(
            typeSyntax,
            _semanticModel.GetAkcssCSharpUsingDirectives(containingSymbol));
        if (binding.TypeSymbol is not INamedTypeSymbol namedType)
        {
            diagnosticsBag.Add(_semanticModel.CreateAkcssInterceptTypeNotFoundDiagnostic(
                interceptDirective,
                interceptDirective.Type.ToFullString().Trim()));
        }
        else if (!_semanticModel.IsAkcssInterceptRuntimeType(namedType, containingSymbol, out var expectedBaseType))
        {
            diagnosticsBag.Add(_semanticModel.CreateAkcssInterceptTypeInvalidDiagnostic(
                interceptDirective,
                namedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                expectedBaseType));
        }
        else
        {
            interceptType = new CSharpSymbolDefinition(namedType);
        }

        var diagnostics = _semanticModel.SetSemanticDiagnostics(interceptDirective, diagnosticsBag);

        return new BoundAkcssIntercept(
            interceptDirective,
            _semanticModel.GetBinder(interceptDirective, BinderUsage.Akcss),
            containingSymbol,
            interceptType,
            diagnostics,
            hasErrors: diagnostics.Length > 0);
    }

    public BoundAkcssPropertySetter CreatePropertySetter(
        AkcssAssignmentSyntax assignment,
        IAkcssSymbol containingSymbol)
    {
        var diagnosticsBag = new BindingDiagnosticBag();

        var property = _semanticModel.ResolveAkcssPropertyWithDiagnostics(
            assignment,
            containingSymbol,
            diagnosticsBag);
        var expression = ParseAkcssAssignmentExpression(assignment);
        var binding = expression == null
            ? CSharpBindingResult.Empty
            : _semanticModel.BindAkcssExpression(expression, containingSymbol);

        var valueType = binding.TypeSymbol == null
            ? default
            : new CSharpSymbolDefinition(binding.TypeSymbol);
        var valueOperation = binding.OperationDefinition;
        var valueKind = AkcssPropertyValueKind.CSharpExpression;
        var requiresBrushConversion = false;
        object? convertedValue = null;
        var activeBinding = binding;
        var hasExpectedTypeBinding = false;

        if (property?.Type.Symbol is ITypeSymbol expectedType && expression != null)
        {
            if (_semanticModel.IsAkcssColorPropertyType(expectedType))
            {
                if (TryGetAkcssColorIdentifierText(expression, out var colorName))
                {
                    if (_semanticModel.TryBindAvaloniaNamedColor(colorName, containingSymbol, out var namedColorBinding))
                    {
                        valueKind = AkcssPropertyValueKind.ColorLiteral;
                        convertedValue = new CSharpSymbolDefinition(namedColorBinding.Symbol!);
                        valueOperation = namedColorBinding.OperationDefinition;
                        activeBinding = namedColorBinding;
                        requiresBrushConversion = _semanticModel.IsAvaloniaBrushType(expectedType);
                        valueType = namedColorBinding.TypeSymbol == null
                            ? valueType
                            : new CSharpSymbolDefinition(namedColorBinding.TypeSymbol);
                    }
                    else
                    {
                        valueKind = AkcssPropertyValueKind.Error;
                        diagnosticsBag.Add(_semanticModel.CreateAkcssInvalidColorDiagnostic(
                            assignment,
                            colorName,
                            property.Name));
                    }
                }
                else if (TryGetAkcssColorStringLiteralText(expression, out var colorText))
                {
                    if (ColorParser.TryParse(colorText, out var color))
                    {
                        valueKind = AkcssPropertyValueKind.ColorLiteral;
                        convertedValue = color;
                        requiresBrushConversion = _semanticModel.IsAvaloniaBrushType(expectedType);
                        if (_semanticModel.TryGetAvaloniaColorType(out var colorType))
                        {
                            valueType = new CSharpSymbolDefinition(colorType);
                        }
                    }
                    else if (_semanticModel.TryBindAvaloniaNamedColor(colorText, containingSymbol, out var namedColorBinding))
                    {
                        valueKind = AkcssPropertyValueKind.ColorLiteral;
                        convertedValue = new CSharpSymbolDefinition(namedColorBinding.Symbol!);
                        valueOperation = namedColorBinding.OperationDefinition;
                        activeBinding = namedColorBinding;
                        requiresBrushConversion = _semanticModel.IsAvaloniaBrushType(expectedType);
                        valueType = namedColorBinding.TypeSymbol == null
                            ? valueType
                            : new CSharpSymbolDefinition(namedColorBinding.TypeSymbol);
                    }
                    else
                    {
                        valueKind = AkcssPropertyValueKind.Error;
                        diagnosticsBag.Add(_semanticModel.CreateAkcssInvalidColorDiagnostic(
                            assignment,
                            colorText,
                            property.Name));
                    }
                }
                else if (binding.TypeSymbol != null &&
                         _semanticModel.IsAvaloniaColorType(binding.TypeSymbol) &&
                         _semanticModel.IsAvaloniaBrushType(expectedType))
                {
                    requiresBrushConversion = true;
                }
            }

            if (valueKind == AkcssPropertyValueKind.CSharpExpression &&
                _semanticModel.TryBindExpectedTypeStaticMember(
                    expression,
                    expectedType,
                    containingSymbol,
                    out var expectedTypeMemberBinding))
            {
                convertedValue = expectedTypeMemberBinding.Symbol == null
                    ? null
                    : new CSharpSymbolDefinition(expectedTypeMemberBinding.Symbol);
                valueOperation = expectedTypeMemberBinding.OperationDefinition;
                activeBinding = expectedTypeMemberBinding;
                hasExpectedTypeBinding = true;
                valueType = expectedTypeMemberBinding.TypeSymbol == null
                    ? valueType
                    : new CSharpSymbolDefinition(expectedTypeMemberBinding.TypeSymbol);
            }
            else if (valueKind == AkcssPropertyValueKind.CSharpExpression &&
                     _semanticModel.TryAcceptExpectedTypeCastExpression(expression, expectedType, containingSymbol))
            {
                valueType = new CSharpSymbolDefinition(expectedType);
                activeBinding = CSharpBindingResult.Empty;
                hasExpectedTypeBinding = true;
            }

            var isThicknessPropertyType = _semanticModel.IsAvaloniaThicknessType(expectedType);
            var isThicknessTuple = false;
            object? thickness = null;
            if (isThicknessPropertyType &&
                AkburaSemanticModel.TryCreateAkcssThicknessValue(
                    expression,
                    assignment.Expression.ToFullString(),
                    out thickness,
                    out isThicknessTuple))
            {
                valueKind = AkcssPropertyValueKind.ThicknessTuple;
                convertedValue = thickness;
                valueType = new CSharpSymbolDefinition(expectedType);
                activeBinding = CSharpBindingResult.Empty;
            }
            else if (isThicknessPropertyType && isThicknessTuple)
            {
                valueKind = AkcssPropertyValueKind.Error;
                diagnosticsBag.Add(_semanticModel.CreateAkcssInvalidThicknessDiagnostic(
                    assignment,
                    assignment.Expression.ToFullString().Trim(),
                    property.Name));
            }

            if (valueKind == AkcssPropertyValueKind.CSharpExpression &&
                _semanticModel.TryCreateAkcssAmxInvocationValue(expression, out var amxInvocation))
            {
                valueKind = AkcssPropertyValueKind.AmxInvocation;
                convertedValue = amxInvocation;
            }

            if (valueKind == AkcssPropertyValueKind.CSharpExpression &&
                !hasExpectedTypeBinding &&
                !requiresBrushConversion)
            {
                activeBinding = _semanticModel.BindAkcssExpression(
                    expression,
                    containingSymbol,
                    expectedType);
                valueOperation = activeBinding.OperationDefinition;
                valueType = activeBinding.TypeSymbol == null
                    ? valueType
                    : new CSharpSymbolDefinition(activeBinding.TypeSymbol);
            }
        }

        if (valueKind != AkcssPropertyValueKind.Error)
        {
            using var diagnosticsBuilder = ImmutableArrayBuilder<AkburaSemanticDiagnostic>.Rent();
            _semanticModel.AddAkcssExpressionDiagnostics(
                    assignment,
                    activeBinding,
                    diagnosticsBuilder);
            _semanticModel.AddAkcssValueConversionDiagnostics(
                    assignment,
                    property,
                    valueKind,
                    requiresBrushConversion,
                    activeBinding,
                    diagnosticsBuilder);
            diagnosticsBag.AddRange(diagnosticsBuilder.ToImmutable());
        }

        var diagnostics = _semanticModel.SetSemanticDiagnostics(assignment, diagnosticsBag);

        return new BoundAkcssPropertySetter(
            assignment,
            _semanticModel.GetBinder(assignment, BinderUsage.Akcss),
            containingSymbol,
            property,
            valueType,
            valueOperation,
            valueKind,
            requiresBrushConversion,
            convertedValue,
            diagnostics,
            property == null || valueKind == AkcssPropertyValueKind.Error || diagnostics.Length > 0);
    }

    private BoundAkcssModule CreateInlineModule(InlineAkcssBlockSyntax inlineAkcssBlock)
    {
        var symbolInfo = _semanticModel.GetDeclarationSymbolInfo(inlineAkcssBlock);
        var boundModule = CreateModule(
            inlineAkcssBlock,
            inlineAkcssBlock.Members,
            symbolInfo);
        _semanticModel.SetCachedBoundNode(inlineAkcssBlock, boundModule);
        return boundModule;
    }

    private BoundAkcssModule CreateExternalModule(AkcssDocumentSyntax document)
    {
        var symbolInfo = _semanticModel.GetDeclarationSymbolInfo(document);
        var boundModule = CreateModule(
            document,
            document.Members,
            symbolInfo);
        _semanticModel.SetCachedBoundNode(document, boundModule);
        return boundModule;
    }

    private BoundAkcssModule CreateModule(
        AkburaSyntax syntax,
        Akbura.Language.Syntax.SyntaxList<AkcssTopLevelMemberSyntax> members,
        AkburaSymbolInfo symbolInfo)
    {
        using var childrenBuilder = ImmutableArrayBuilder<BoundNode>.Rent();
        foreach (var member in members)
        {
            switch (member.Kind)
            {
                case AkburaSyntaxKind.AkcssStyleRuleSyntax:
                    childrenBuilder.Add(_semanticModel.BindingSession.BindSemanticSyntax(member));
                    break;

                case AkburaSyntaxKind.AkcssUtilitiesSectionSyntax:
                    foreach (var utility in Unsafe.As<AkcssUtilitiesSectionSyntax>(member).Utilities)
                    {
                        childrenBuilder.Add(_semanticModel.BindingSession.BindSemanticSyntax(utility));
                    }

                    break;
            }
        }

        return new BoundAkcssModule(
            syntax,
            _semanticModel.GetBinder(syntax, BinderUsage.Akcss),
            symbolInfo,
            _semanticModel.GetCachedSemanticDiagnostics(syntax),
            childrenBuilder.ToImmutable());
    }

    private BoundAkcssStyle CreateStyle(AkcssStyleRuleSyntax styleRule)
    {
        var symbolInfo = _semanticModel.GetDeclarationSymbolInfo(styleRule);
        var symbol = symbolInfo.Symbol as IAkcssSymbol;
        var children = symbol == null
            ? ImmutableArray<BoundNode>.Empty
            : CastBoundNodes(CreateBoundOperations(styleRule.Members, symbol));
        var boundStyle = new BoundAkcssStyle(
            styleRule,
            _semanticModel.GetBinder(styleRule, BinderUsage.Akcss),
            symbolInfo,
            _semanticModel.GetCachedSemanticDiagnostics(styleRule),
            children);
        _semanticModel.SetCachedBoundNode(styleRule, boundStyle);
        return boundStyle;
    }

    private BoundAkcssUtility CreateUtility(AkcssUtilityDeclarationSyntax utilityDeclaration)
    {
        var symbolInfo = _semanticModel.GetDeclarationSymbolInfo(utilityDeclaration);
        var symbol = symbolInfo.Symbol as IAkcssSymbol;
        var children = symbol == null
            ? ImmutableArray<BoundNode>.Empty
            : CastBoundNodes(CreateBoundOperations(utilityDeclaration.Members, symbol));
        var boundUtility = new BoundAkcssUtility(
            utilityDeclaration,
            _semanticModel.GetBinder(utilityDeclaration, BinderUsage.Akcss),
            symbolInfo,
            _semanticModel.GetCachedSemanticDiagnostics(utilityDeclaration),
            children);
        _semanticModel.SetCachedBoundNode(utilityDeclaration, boundUtility);
        return boundUtility;
    }

    private static ImmutableArray<BoundNode> CastBoundNodes(ImmutableArray<BoundAkcssOperation> operations)
    {
        if (operations.IsDefaultOrEmpty)
        {
            return ImmutableArray<BoundNode>.Empty;
        }

        using var builder = ImmutableArrayBuilder<BoundNode>.Rent(operations.Length);
        foreach (var operation in operations)
        {
            builder.Add(operation);
        }

        return builder.ToImmutable();
    }

    private static ImmutableArray<string> GetAkcssApplyItems(AkcssApplyDirectiveSyntax applyDirective)
    {
        var text = applyDirective.Items.ToFullString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return ImmutableArray<string>.Empty;
        }

        return text
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .ToImmutableArray();
    }

    private static CSharp.ExpressionSyntax? ParseAkcssAssignmentExpression(AkcssAssignmentSyntax assignment)
    {
        return assignment.Expression.GetRawCSharpExpression();
    }

    private static CSharp.ExpressionSyntax? ParseAkcssConditionExpression(AkcssIfDirectiveSyntax ifDirective)
    {
        return ifDirective.Condition.GetRawCSharpExpression();
    }

    private static bool TryGetAkcssColorIdentifierText(
        CSharp.ExpressionSyntax expression,
        out string text)
    {
        text = expression is CSharp.IdentifierNameSyntax identifier
            ? identifier.Identifier.ValueText
            : string.Empty;

        return text.Length > 0;
    }

    private static bool TryGetAkcssColorStringLiteralText(
        CSharp.ExpressionSyntax expression,
        out string text)
    {
        text = expression is CSharp.LiteralExpressionSyntax literal &&
            literal.IsKind(CSharpSyntaxKind.StringLiteralExpression)
                ? literal.Token.ValueText
                : string.Empty;

        return text.Length > 0;
    }
}
