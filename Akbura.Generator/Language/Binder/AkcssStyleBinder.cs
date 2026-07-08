using Akbura.Language.BoundTree;
using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using AkburaSymbol = Akbura.Language.Symbols.ISymbol;
using AkburaPropertySymbol = Akbura.Language.Symbols.IPropertySymbol;
using AkburaCandidateReason = Akbura.Language.Symbols.CandidateReason;
using AkburaSyntaxKind = Akbura.Language.Syntax.SyntaxKind;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;
using CSharpSyntaxFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using CSharpSyntaxKind = Microsoft.CodeAnalysis.CSharp.SyntaxKind;

namespace Akbura.Language.Binder;

internal sealed partial class AkcssStyleBinder : Binder
{
    private ImmutableArray<AkburaSymbol> _lazyDeclaredSymbols;

    public AkcssStyleBinder(
        AkburaSemanticModel semanticModel,
        Binder next,
        Declaration declaration,
        AkburaBinderFlags flags = AkburaBinderFlags.None)
        : base(
            semanticModel,
            next,
            declaration,
            DeclarationFacts.GetSyntax(declaration),
            flags | AkburaBinderFlags.InAkcss |
                (declaration.Kind == DeclarationKind.AkcssUtility
                    ? AkburaBinderFlags.InAkcssUtility
                    : AkburaBinderFlags.InAkcssStyle))
    {
    }

    public override ImmutableArray<AkburaSymbol> GetDeclaredSymbolsForScope(AkburaSyntax scopeDesignator)
    {
        if (!OwnsScope(scopeDesignator) ||
            Declaration?.Kind != DeclarationKind.AkcssUtility)
        {
            return base.GetDeclaredSymbolsForScope(scopeDesignator);
        }

        return GetDeclaredSymbols();
    }

    public override BoundNode BindOperationSyntax(AkburaSyntax syntax)
    {
        return syntax.Kind switch
        {
            AkburaSyntaxKind.AkcssAssignmentSyntax =>
                BindAkcssPropertySetter(Unsafe.As<AkcssAssignmentSyntax>(syntax)),
            AkburaSyntaxKind.AkcssIfDirectiveSyntax =>
                BindAkcssIf(Unsafe.As<AkcssIfDirectiveSyntax>(syntax)),
            AkburaSyntaxKind.AkcssApplyDirectiveSyntax =>
                BindAkcssApply(Unsafe.As<AkcssApplyDirectiveSyntax>(syntax)),
            AkburaSyntaxKind.AkcssInterceptDirectiveSyntax =>
                BindAkcssIntercept(Unsafe.As<AkcssInterceptDirectiveSyntax>(syntax)),
            _ => base.BindOperationSyntax(syntax),
        };
    }

    public override BoundNode BindSemanticSyntax(AkburaSyntax syntax)
    {
        return syntax.Kind switch
        {
            AkburaSyntaxKind.AkcssStyleRuleSyntax or
                AkburaSyntaxKind.AkcssUtilityDeclarationSyntax =>
                BindAkcssStyleSyntax(syntax),
            AkburaSyntaxKind.AkcssAssignmentSyntax or
                AkburaSyntaxKind.AkcssIfDirectiveSyntax or
                AkburaSyntaxKind.AkcssApplyDirectiveSyntax or
                AkburaSyntaxKind.AkcssInterceptDirectiveSyntax =>
                BindOperationSyntax(syntax),
            _ => base.BindSemanticSyntax(syntax),
        };
    }

    private BoundNode BindAkcssPropertySetter(AkcssAssignmentSyntax assignment)
    {
        if (!TryPrepareAkcssOperation(assignment, out var containingSymbol, out var boundNode))
        {
            return boundNode;
        }

        return BindAkcssPropertySetterCore(assignment, containingSymbol);
    }

    private BoundNode BindAkcssIf(AkcssIfDirectiveSyntax ifDirective)
    {
        if (!TryPrepareAkcssOperation(ifDirective, out var containingSymbol, out var boundNode))
        {
            return boundNode;
        }

        return BindAkcssIfCore(ifDirective, containingSymbol);
    }

    private BoundNode BindAkcssApply(AkcssApplyDirectiveSyntax applyDirective)
    {
        if (!TryPrepareAkcssOperation(applyDirective, out var containingSymbol, out var boundNode))
        {
            return boundNode;
        }

        return BindAkcssApplyCore(applyDirective, containingSymbol);
    }

    private BoundNode BindAkcssIntercept(AkcssInterceptDirectiveSyntax interceptDirective)
    {
        if (!TryPrepareAkcssOperation(
                interceptDirective,
                out var containingSymbol,
                out var boundNode,
                suppressInterceptedMembers: false))
        {
            return boundNode;
        }

        return BindAkcssInterceptCore(interceptDirective, containingSymbol);
    }

    internal BoundAkcssOperation BindAkcssOperation(
        AkcssBodyMemberSyntax member,
        IAkcssSymbol containingSymbol)
    {
        if (SemanticModel.TryGetCachedBoundNode(member, out var cachedBoundNode) &&
            cachedBoundNode is BoundAkcssOperation cachedOperation)
        {
            return cachedOperation;
        }

        BoundAkcssOperation boundNode = member.Kind switch
        {
            AkburaSyntaxKind.AkcssAssignmentSyntax =>
                BindAkcssPropertySetterCore(
                    Unsafe.As<AkcssAssignmentSyntax>(member),
                    containingSymbol),
            AkburaSyntaxKind.AkcssIfDirectiveSyntax =>
                BindAkcssIfCore(
                    Unsafe.As<AkcssIfDirectiveSyntax>(member),
                    containingSymbol),
            AkburaSyntaxKind.AkcssApplyDirectiveSyntax =>
                BindAkcssApplyCore(
                    Unsafe.As<AkcssApplyDirectiveSyntax>(member),
                    containingSymbol),
            AkburaSyntaxKind.AkcssInterceptDirectiveSyntax =>
                BindAkcssInterceptCore(
                    Unsafe.As<AkcssInterceptDirectiveSyntax>(member),
                    containingSymbol),
            _ => throw new InvalidOperationException(
                $"Unsupported AKCSS body member syntax: {member.Kind}."),
        };
        SemanticModel.SetCachedBoundNode(member, boundNode);
        return boundNode;
    }

    private bool TryPrepareAkcssOperation(
        AkcssBodyMemberSyntax member,
        out IAkcssSymbol containingSymbol,
        out BoundNode boundNode,
        bool suppressInterceptedMembers = true)
    {
        if (!TryGetContainingAkcssSymbol(member, out containingSymbol))
        {
            boundNode = CreateMissingContainingAkcssSymbolBoundNode(member);
            return false;
        }

        if (SemanticModel.TryGetCachedBoundNode(member, out var cachedBoundNode))
        {
            boundNode = cachedBoundNode;
            return false;
        }

        if (suppressInterceptedMembers &&
            SemanticModel.TrySuppressAkcssOperationDueToIntercept(member, containingSymbol))
        {
            boundNode = new BoundDeclaration(
                member,
                this,
                AkburaSymbolInfo.None(AkburaCandidateReason.None));
            return false;
        }

        boundNode = null!;
        return true;
    }

    private bool TryGetContainingAkcssSymbol(
        AkburaSyntax syntax,
        out IAkcssSymbol containingSymbol)
    {
        containingSymbol = SemanticModel.GetContainingAkcssSymbol(syntax)!;
        return containingSymbol != null;
    }

    private BoundDeclaration CreateMissingContainingAkcssSymbolBoundNode(AkburaSyntax syntax)
    {
        SemanticModel.SetSemanticDiagnostics(
            syntax,
            ImmutableArray<AkburaSemanticDiagnostic>.Empty);
        return new BoundDeclaration(
            syntax,
            this,
            AkburaSymbolInfo.None(AkburaCandidateReason.NotFound));
    }

    private BoundNode BindAkcssStyleSyntax(AkburaSyntax syntax)
    {
        return syntax.Kind switch
        {
            AkburaSyntaxKind.AkcssStyleRuleSyntax =>
                BindAkcssStyle(Unsafe.As<AkcssStyleRuleSyntax>(syntax)),
            AkburaSyntaxKind.AkcssUtilityDeclarationSyntax =>
                BindAkcssUtility(Unsafe.As<AkcssUtilityDeclarationSyntax>(syntax)),
            _ => new BoundDeclaration(
                syntax,
                this,
                AkburaSymbolInfo.None(AkburaCandidateReason.UnsupportedSyntax)),
        };
    }

    private BoundAkcssStyle BindAkcssStyle(AkcssStyleRuleSyntax styleRule)
    {
        var symbolInfo = SemanticModel.GetDeclarationSymbolInfo(styleRule);
        var symbol = symbolInfo.Symbol as IAkcssSymbol;
        var children = symbol == null
            ? ImmutableArray<BoundNode>.Empty
            : CastBoundNodes(BindAkcssOperations(styleRule.Members, symbol));
        var boundStyle = new BoundAkcssStyle(
            styleRule,
            this,
            symbolInfo,
            SemanticModel.GetCachedSemanticDiagnostics(styleRule),
            children);
        SemanticModel.SetCachedBoundNode(styleRule, boundStyle);
        return boundStyle;
    }

    private BoundAkcssUtility BindAkcssUtility(AkcssUtilityDeclarationSyntax utilityDeclaration)
    {
        var symbolInfo = SemanticModel.GetDeclarationSymbolInfo(utilityDeclaration);
        var symbol = symbolInfo.Symbol as IAkcssSymbol;
        var children = symbol == null
            ? ImmutableArray<BoundNode>.Empty
            : CastBoundNodes(BindAkcssOperations(utilityDeclaration.Members, symbol));
        var boundUtility = new BoundAkcssUtility(
            utilityDeclaration,
            this,
            symbolInfo,
            SemanticModel.GetCachedSemanticDiagnostics(utilityDeclaration),
            children);
        SemanticModel.SetCachedBoundNode(utilityDeclaration, boundUtility);
        return boundUtility;
    }

    private ImmutableArray<BoundAkcssOperation> BindAkcssOperations(
        Akbura.Language.Syntax.SyntaxList<AkcssBodyMemberSyntax> members,
        IAkcssSymbol containingSymbol)
    {
        using var builder = ImmutableArrayBuilder<BoundAkcssOperation>.Rent();
        foreach (var member in members)
        {
            if (SemanticModel.TryGetCachedBoundNode(member, out var cachedBoundNode) &&
                cachedBoundNode is BoundAkcssOperation cachedOperation)
            {
                builder.Add(cachedOperation);
                continue;
            }

            BoundNode? boundNode = member.Kind switch
            {
                AkburaSyntaxKind.AkcssAssignmentSyntax =>
                    BindAkcssPropertySetterCore(
                        Unsafe.As<AkcssAssignmentSyntax>(member),
                        containingSymbol),
                AkburaSyntaxKind.AkcssIfDirectiveSyntax =>
                    BindAkcssIfCore(
                        Unsafe.As<AkcssIfDirectiveSyntax>(member),
                        containingSymbol),
                AkburaSyntaxKind.AkcssApplyDirectiveSyntax =>
                    BindAkcssApplyCore(
                        Unsafe.As<AkcssApplyDirectiveSyntax>(member),
                        containingSymbol),
                AkburaSyntaxKind.AkcssInterceptDirectiveSyntax =>
                    BindAkcssInterceptCore(
                        Unsafe.As<AkcssInterceptDirectiveSyntax>(member),
                        containingSymbol),
                _ => null,
            };

            if (boundNode is BoundAkcssOperation operation)
            {
                SemanticModel.SetCachedBoundNode(member, operation);
                builder.Add(operation);
            }
        }

        return builder.ToImmutable();
    }

    private BoundAkcssIf BindAkcssIfCore(
        AkcssIfDirectiveSyntax ifDirective,
        IAkcssSymbol containingSymbol)
    {
        var expression = ParseAkcssConditionExpression(ifDirective);
        var binding = expression == null
            ? CSharpBindingResult.Empty
            : SemanticModel.BindAkcssExpression(expression, containingSymbol);
        var conditionType = binding.TypeSymbol == null
            ? default
            : new CSharpSymbolDefinition(binding.TypeSymbol);
        var operations = BindAkcssOperations(ifDirective.Members, containingSymbol);

        var diagnosticsBag = BindingDiagnosticBag.GetInstance();
        {
            using var diagnosticsBuilder = ImmutableArrayBuilder<AkburaSemanticDiagnostic>.Rent();
            SemanticModel.AddAkcssExpressionDiagnostics(
                ifDirective,
                ifDirective.Condition.ToFullString().Trim(),
                binding,
                diagnosticsBuilder);
            diagnosticsBag.AddRange(diagnosticsBuilder.ToImmutable());
        }
        var diagnostics = SemanticModel.SetSemanticDiagnostics(ifDirective, diagnosticsBag);

        return new BoundAkcssIf(
            ifDirective,
            this,
            containingSymbol,
            conditionType,
            binding.OperationDefinition,
            operations,
            diagnostics,
            hasErrors: expression == null ||
                diagnostics.Length > 0 ||
                operations.Any(static operation => operation.HasErrors));
    }

    private BoundAkcssApply BindAkcssApplyCore(
        AkcssApplyDirectiveSyntax applyDirective,
        IAkcssSymbol containingSymbol)
    {
        var diagnosticsBag = BindingDiagnosticBag.GetInstance();
        using var itemsBuilder = ImmutableArrayBuilder<string>.Rent();
        using var symbolsBuilder = ImmutableArrayBuilder<IAkcssSymbol>.Rent();

        foreach (var item in GetAkcssApplyItems(applyDirective))
        {
            itemsBuilder.Add(item);
            using var diagnosticsBuilder = ImmutableArrayBuilder<AkburaSemanticDiagnostic>.Rent();
            var resolved = SemanticModel.ResolveAkcssApplyItem(
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

        var diagnostics = SemanticModel.SetSemanticDiagnostics(applyDirective, diagnosticsBag);

        return new BoundAkcssApply(
            applyDirective,
            this,
            containingSymbol,
            itemsBuilder.ToImmutable(),
            symbolsBuilder.ToImmutable(),
            diagnostics,
            hasErrors: diagnostics.Length > 0);
    }

    private BoundAkcssIntercept BindAkcssInterceptCore(
        AkcssInterceptDirectiveSyntax interceptDirective,
        IAkcssSymbol containingSymbol)
    {
        var diagnosticsBag = BindingDiagnosticBag.GetInstance();
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

        var binding = SemanticModel.BindCSharpType(
            typeSyntax,
            SemanticModel.GetAkcssCSharpUsingDirectives(containingSymbol));
        if (binding.TypeSymbol is not INamedTypeSymbol namedType)
        {
            diagnosticsBag.Add(SemanticModel.CreateAkcssInterceptTypeNotFoundDiagnostic(
                interceptDirective,
                interceptDirective.Type.ToFullString().Trim()));
        }
        else if (!SemanticModel.IsAkcssInterceptRuntimeType(namedType, containingSymbol, out var expectedBaseType))
        {
            diagnosticsBag.Add(SemanticModel.CreateAkcssInterceptTypeInvalidDiagnostic(
                interceptDirective,
                namedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                expectedBaseType));
        }
        else
        {
            interceptType = new CSharpSymbolDefinition(namedType);
        }

        var diagnostics = SemanticModel.SetSemanticDiagnostics(interceptDirective, diagnosticsBag);

        return new BoundAkcssIntercept(
            interceptDirective,
            this,
            containingSymbol,
            interceptType,
            diagnostics,
            hasErrors: diagnostics.Length > 0);
    }

    private BoundAkcssPropertySetter BindAkcssPropertySetterCore(
        AkcssAssignmentSyntax assignment,
        IAkcssSymbol containingSymbol)
    {
        var diagnosticsBag = BindingDiagnosticBag.GetInstance();

        var property = SemanticModel.ResolveAkcssPropertyWithDiagnostics(
            assignment,
            containingSymbol,
            diagnosticsBag);
        var expression = ParseAkcssAssignmentExpression(assignment);
        var binding = expression == null
            ? CSharpBindingResult.Empty
            : SemanticModel.BindAkcssExpression(expression, containingSymbol);

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
            if (SemanticModel.IsAkcssColorPropertyType(expectedType))
            {
                if (TryGetAkcssColorIdentifierText(expression, out var colorName))
                {
                    if (SemanticModel.TryBindAvaloniaNamedColor(colorName, containingSymbol, out var namedColorBinding))
                    {
                        valueKind = AkcssPropertyValueKind.ColorLiteral;
                        convertedValue = new CSharpSymbolDefinition(namedColorBinding.Symbol!);
                        valueOperation = namedColorBinding.OperationDefinition;
                        activeBinding = namedColorBinding;
                        requiresBrushConversion = SemanticModel.IsAvaloniaBrushType(expectedType);
                        valueType = namedColorBinding.TypeSymbol == null
                            ? valueType
                            : new CSharpSymbolDefinition(namedColorBinding.TypeSymbol);
                    }
                    else
                    {
                        valueKind = AkcssPropertyValueKind.Error;
                        diagnosticsBag.Add(SemanticModel.CreateAkcssInvalidColorDiagnostic(
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
                        requiresBrushConversion = SemanticModel.IsAvaloniaBrushType(expectedType);
                        if (SemanticModel.TryGetAvaloniaColorType(out var colorType))
                        {
                            valueType = new CSharpSymbolDefinition(colorType);
                        }
                    }
                    else if (SemanticModel.TryBindAvaloniaNamedColor(colorText, containingSymbol, out var namedColorBinding))
                    {
                        valueKind = AkcssPropertyValueKind.ColorLiteral;
                        convertedValue = new CSharpSymbolDefinition(namedColorBinding.Symbol!);
                        valueOperation = namedColorBinding.OperationDefinition;
                        activeBinding = namedColorBinding;
                        requiresBrushConversion = SemanticModel.IsAvaloniaBrushType(expectedType);
                        valueType = namedColorBinding.TypeSymbol == null
                            ? valueType
                            : new CSharpSymbolDefinition(namedColorBinding.TypeSymbol);
                    }
                    else
                    {
                        valueKind = AkcssPropertyValueKind.Error;
                        diagnosticsBag.Add(SemanticModel.CreateAkcssInvalidColorDiagnostic(
                            assignment,
                            colorText,
                            property.Name));
                    }
                }
                else if (binding.TypeSymbol != null &&
                         SemanticModel.IsAvaloniaColorType(binding.TypeSymbol) &&
                         SemanticModel.IsAvaloniaBrushType(expectedType))
                {
                    requiresBrushConversion = true;
                }
            }

            if (valueKind == AkcssPropertyValueKind.CSharpExpression &&
                SemanticModel.TryBindExpectedTypeStaticMember(
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
                     SemanticModel.TryAcceptExpectedTypeCastExpression(expression, expectedType, containingSymbol))
            {
                valueType = new CSharpSymbolDefinition(expectedType);
                activeBinding = CSharpBindingResult.Empty;
                hasExpectedTypeBinding = true;
            }

            var isThicknessPropertyType = SemanticModel.IsAvaloniaThicknessType(expectedType);
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
                diagnosticsBag.Add(SemanticModel.CreateAkcssInvalidThicknessDiagnostic(
                    assignment,
                    assignment.Expression.ToFullString().Trim(),
                    property.Name));
            }

            if (valueKind == AkcssPropertyValueKind.CSharpExpression &&
                SemanticModel.TryCreateAkcssAmxInvocationValue(expression, out var amxInvocation))
            {
                valueKind = AkcssPropertyValueKind.AmxInvocation;
                convertedValue = amxInvocation;
            }

            if (valueKind == AkcssPropertyValueKind.CSharpExpression &&
                !hasExpectedTypeBinding &&
                !requiresBrushConversion)
            {
                activeBinding = SemanticModel.BindAkcssExpression(
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
            SemanticModel.AddAkcssExpressionDiagnostics(
                    assignment,
                    activeBinding,
                    diagnosticsBuilder);
            SemanticModel.AddAkcssValueConversionDiagnostics(
                    assignment,
                    property,
                    valueKind,
                    requiresBrushConversion,
                    activeBinding,
                    diagnosticsBuilder);
            diagnosticsBag.AddRange(diagnosticsBuilder.ToImmutable());
        }

        var diagnostics = SemanticModel.SetSemanticDiagnostics(assignment, diagnosticsBag);

        return new BoundAkcssPropertySetter(
            assignment,
            this,
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

    protected override void LookupSymbolsInSingleBinder(
        LookupResult result,
        string name,
        int arity,
        BinderLookupOptions options,
        Binder originalBinder,
        AkburaSyntax syntax,
        BindingDiagnosticBag diagnostics)
    {
        if (Declaration?.Kind != DeclarationKind.AkcssUtility)
        {
            return;
        }

        var symbol = FindDeclaredSymbol(GetDeclaredSymbolsForScope(DeclarationFacts.GetSyntax(Declaration)), name);
        if (symbol != null)
        {
            result.SetSymbol(symbol);
        }
    }

    private ImmutableArray<AkburaSymbol> GetDeclaredSymbols()
    {
        var symbols = _lazyDeclaredSymbols;
        if (!symbols.IsDefault)
        {
            return symbols;
        }

        symbols = SemanticModel.DeclarationSymbols.GetTailwindUtilityParameters(Declaration!);

        ImmutableInterlocked.InterlockedInitialize(ref _lazyDeclaredSymbols, symbols);
        return _lazyDeclaredSymbols;
    }
}
