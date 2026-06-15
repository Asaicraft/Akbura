using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Language.Syntax.Green;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using AkburaCandidateReason = Akbura.Language.Symbols.CandidateReason;
using AkburaSymbol = Akbura.Language.Symbols.ISymbol;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;
using CSharpSyntaxFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using RoslynFieldSymbol = Microsoft.CodeAnalysis.IFieldSymbol;
using AkburaOperation = Akbura.Language.Operations.IOperation;
using AkburaPropertySymbol = Akbura.Language.Symbols.IPropertySymbol;
using RoslynPropertySymbol = Microsoft.CodeAnalysis.IPropertySymbol;
using RoslynSymbol = Microsoft.CodeAnalysis.ISymbol;

namespace Akbura.Language;

internal sealed class AkburaSemanticModel
{
    private readonly Dictionary<AkburaSyntax, AkburaSymbolInfo> _symbolInfoCache = new();
    private readonly Dictionary<AkburaSyntax, AkburaOperation?> _operationCache = new();
    private readonly Dictionary<AkburaSyntax, ImmutableArray<AkburaSemanticDiagnostic>> _semanticDiagnosticsCache = new();

    public AkburaSemanticModel(AkburaCompilation compilation, AkburaSyntaxTree syntaxTree)
    {
        Compilation = compilation ?? throw new ArgumentNullException(nameof(compilation));
        SyntaxTree = syntaxTree ?? throw new ArgumentNullException(nameof(syntaxTree));
    }

    public AkburaCompilation Compilation { get; }

    public AkburaSyntaxTree SyntaxTree { get; }

    public AkburaSymbolInfo GetSymbolInfo(AkburaSyntax syntax)
    {
        if (syntax == null)
        {
            throw new ArgumentNullException(nameof(syntax));
        }

        ValidateSyntaxTreeOwnership(syntax);

        if (_symbolInfoCache.TryGetValue(syntax, out var symbolInfo))
        {
            return symbolInfo;
        }

        symbolInfo = syntax switch
        {
            StateDeclarationSyntax stateDeclaration => ResolveState(stateDeclaration),
            ParamDeclarationSyntax paramDeclaration => ResolveParam(paramDeclaration),
            InjectDeclarationSyntax injectDeclaration => ResolveInject(injectDeclaration),
            CommandDeclarationSyntax commandDeclaration => ResolveCommand(commandDeclaration),
            AkcssStyleRuleSyntax styleRule => ResolveAkcssStyle(styleRule),
            AkcssUtilityDeclarationSyntax utilityDeclaration => ResolveTailwindUtility(utilityDeclaration),
            MarkupElementSyntax markupElement => ResolveMarkupComponent(markupElement),
            MarkupAttributeSyntax markupAttribute => ResolveMarkupProperty(markupAttribute),
            _ => AkburaSymbolInfo.None(AkburaCandidateReason.UnsupportedSyntax),
        };

        _symbolInfoCache.Add(syntax, symbolInfo);
        return symbolInfo;
    }

    public ImmutableArray<AkburaSemanticDiagnostic> GetSemanticDiagnostics(AkburaSyntax syntax)
    {
        if (syntax == null)
        {
            throw new ArgumentNullException(nameof(syntax));
        }

        ValidateSyntaxTreeOwnership(syntax);
        _ = GetSymbolInfo(syntax);

        return _semanticDiagnosticsCache.TryGetValue(syntax, out var diagnostics)
            ? diagnostics
            : ImmutableArray<AkburaSemanticDiagnostic>.Empty;
    }

    public AkburaOperation? GetOperation(AkburaSyntax syntax)
    {
        if (syntax == null)
        {
            throw new ArgumentNullException(nameof(syntax));
        }

        ValidateSyntaxTreeOwnership(syntax);

        if (_operationCache.TryGetValue(syntax, out var operation))
        {
            return operation;
        }

        operation = syntax switch
        {
            AkcssAssignmentSyntax assignment => ResolveAkcssPropertySetterOperation(assignment),
            AkcssIfDirectiveSyntax ifDirective => ResolveAkcssIfOperation(ifDirective),
            _ => null,
        };

        _operationCache[syntax] = operation;
        return operation;
    }

    private AkburaSymbolInfo ResolveState(StateDeclarationSyntax stateDeclaration)
    {
        var name = stateDeclaration.Name.Identifier.ValueText;
        if (string.IsNullOrWhiteSpace(name))
        {
            return AkburaSymbolInfo.None(AkburaCandidateReason.UnsupportedSyntax);
        }

        var hasExplicitType = stateDeclaration.Type != null;
        var initializerBinding = BindStateInitializerExpression(stateDeclaration);
        var initializerType = initializerBinding.TypeSymbol == null
            ? default
            : new CSharpSymbolDefinition(initializerBinding.TypeSymbol);
        var type = hasExplicitType
            ? ResolveExplicitStateType(stateDeclaration)
            : initializerType;
        var bindingKind = GetStateBindingKind(stateDeclaration.Initializer);
        var diagnostics = CreateStateBindingDiagnostics(
            stateDeclaration,
            bindingKind,
            type,
            initializerBinding);
        SetSemanticDiagnostics(stateDeclaration, diagnostics);

        return AkburaSymbolInfo.Success(new StateSymbol(
            stateDeclaration,
            type,
            initializerType,
            hasExplicitType,
            bindingKind));
    }

    private AkburaSymbolInfo ResolveParam(ParamDeclarationSyntax paramDeclaration)
    {
        var name = paramDeclaration.Name.Identifier.ValueText;
        if (string.IsNullOrWhiteSpace(name))
        {
            return AkburaSymbolInfo.None(AkburaCandidateReason.UnsupportedSyntax);
        }

        var hasExplicitType = paramDeclaration.Type != null;
        var defaultValueType = ResolveParamDefaultValueType(paramDeclaration);
        var type = hasExplicitType
            ? ResolveExplicitParamType(paramDeclaration)
            : defaultValueType;
        var bindingKind = GetParamBindingKind(paramDeclaration);

        SetSemanticDiagnostics(paramDeclaration, ImmutableArray<AkburaSemanticDiagnostic>.Empty);

        return AkburaSymbolInfo.Success(new ParamSymbol(
            paramDeclaration,
            type,
            defaultValueType,
            hasExplicitType,
            bindingKind));
    }

    private AkburaSymbolInfo ResolveInject(InjectDeclarationSyntax injectDeclaration)
    {
        var name = injectDeclaration.Name.Identifier.ValueText;
        if (string.IsNullOrWhiteSpace(name))
        {
            return AkburaSymbolInfo.None(AkburaCandidateReason.UnsupportedSyntax);
        }

        var type = ResolveInjectType(injectDeclaration);

        SetSemanticDiagnostics(injectDeclaration, ImmutableArray<AkburaSemanticDiagnostic>.Empty);

        return AkburaSymbolInfo.Success(new InjectSymbol(injectDeclaration, type));
    }

    private AkburaSymbolInfo ResolveCommand(CommandDeclarationSyntax commandDeclaration)
    {
        var name = commandDeclaration.Name.Identifier.ValueText;
        if (string.IsNullOrWhiteSpace(name))
        {
            return AkburaSymbolInfo.None(AkburaCandidateReason.UnsupportedSyntax);
        }

        var returnType = ResolveCommandReturnType(commandDeclaration);
        var parameters = CreateCommandParameters(commandDeclaration);
        var isVoid = returnType.Symbol is ITypeSymbol { SpecialType: SpecialType.System_Void };
        var isAsyncLike = returnType.Symbol is ITypeSymbol returnTypeSymbol &&
            IsTaskLikeType(returnTypeSymbol);
        var resultType = GetCommandResultType(returnType, isVoid, isAsyncLike);
        var hasResult = !resultType.IsDefault;

        SetSemanticDiagnostics(commandDeclaration, ImmutableArray<AkburaSemanticDiagnostic>.Empty);

        return AkburaSymbolInfo.Success(new CommandSymbol(
            commandDeclaration,
            returnType,
            resultType,
            parameters,
            isVoid,
            isAsyncLike,
            hasResult));
    }

    private AkburaSymbolInfo ResolveAkcssStyle(
        AkcssStyleRuleSyntax styleRule)
    {
        var name = styleRule.Selector.Name.Identifier.ValueText;
        if (string.IsNullOrWhiteSpace(name))
        {
            return AkburaSymbolInfo.None(AkburaCandidateReason.UnsupportedSyntax);
        }

        if (!TryResolveAkcssTargetType(styleRule.Selector.TargetType, out var targetType))
        {
            SetSemanticDiagnostics(styleRule, ImmutableArray<AkburaSemanticDiagnostic>.Empty);
            return AkburaSymbolInfo.None(AkburaCandidateReason.NotFound);
        }

        var symbol = new AkcssStyleSymbol(
            styleRule,
            targetType,
            ImmutableArray<IAkcssOperation>.Empty);
        symbol.SetOperations(CreateAkcssOperations(styleRule.Members, symbol));

        SetSemanticDiagnostics(styleRule, ImmutableArray<AkburaSemanticDiagnostic>.Empty);

        return AkburaSymbolInfo.Success(symbol);
    }

    private AkburaSymbolInfo ResolveTailwindUtility(
        AkcssUtilityDeclarationSyntax utilityDeclaration)
    {
        var name = utilityDeclaration.Selector.Name.Identifier.ValueText;
        if (string.IsNullOrWhiteSpace(name))
        {
            return AkburaSymbolInfo.None(AkburaCandidateReason.UnsupportedSyntax);
        }

        if (!TryResolveAkcssTargetType(utilityDeclaration.Selector.TargetType, out var targetType))
        {
            SetSemanticDiagnostics(utilityDeclaration, ImmutableArray<AkburaSemanticDiagnostic>.Empty);
            return AkburaSymbolInfo.None(AkburaCandidateReason.NotFound);
        }

        var symbol = new TailwindUtilitySymbol(
            utilityDeclaration,
            targetType,
            CreateTailwindUtilityParameters(utilityDeclaration),
            ImmutableArray<IAkcssOperation>.Empty);
        symbol.SetOperations(CreateAkcssOperations(utilityDeclaration.Members, symbol));

        SetSemanticDiagnostics(utilityDeclaration, ImmutableArray<AkburaSemanticDiagnostic>.Empty);

        return AkburaSymbolInfo.Success(symbol);
    }

    private CSharpSymbolDefinition ResolveExplicitStateType(StateDeclarationSyntax stateDeclaration)
    {
        var typeSyntax = stateDeclaration.Type;
        if (typeSyntax == null)
        {
            return default;
        }

        CSharp.TypeSyntax csharpType;
        try
        {
            csharpType = typeSyntax.ToCSharp();
        }
        catch (InvalidOperationException)
        {
            return default;
        }

        var binding = BindCSharpType(csharpType);
        var typeSymbol = binding.TypeSymbol;
        return typeSymbol == null ? default : new CSharpSymbolDefinition(typeSymbol);
    }

    private CSharpSymbolDefinition ResolveExplicitParamType(ParamDeclarationSyntax paramDeclaration)
    {
        var typeSyntax = paramDeclaration.Type;
        if (typeSyntax == null)
        {
            return default;
        }

        CSharp.TypeSyntax csharpType;
        try
        {
            csharpType = typeSyntax.ToCSharp();
        }
        catch (InvalidOperationException)
        {
            return default;
        }

        var binding = BindCSharpType(csharpType);
        var typeSymbol = binding.TypeSymbol;
        return typeSymbol == null ? default : new CSharpSymbolDefinition(typeSymbol);
    }

    private CSharpSymbolDefinition ResolveInjectType(InjectDeclarationSyntax injectDeclaration)
    {
        CSharp.TypeSyntax csharpType;
        try
        {
            csharpType = injectDeclaration.Type.ToCSharp();
        }
        catch (InvalidOperationException)
        {
            return default;
        }

        var binding = BindCSharpType(csharpType);
        var typeSymbol = binding.TypeSymbol;
        return typeSymbol == null ? default : new CSharpSymbolDefinition(typeSymbol);
    }

    private CSharpSymbolDefinition ResolveCommandReturnType(CommandDeclarationSyntax commandDeclaration)
    {
        CSharp.TypeSyntax csharpType;
        try
        {
            csharpType = commandDeclaration.ReturnType.ToCSharp();
        }
        catch (InvalidOperationException)
        {
            return default;
        }

        var binding = BindCSharpType(csharpType);
        var typeSymbol = binding.TypeSymbol;
        return typeSymbol == null ? default : new CSharpSymbolDefinition(typeSymbol);
    }

    private ImmutableArray<ICommandParameterSymbol> CreateCommandParameters(
        CommandDeclarationSyntax commandDeclaration)
    {
        var csharpParameters = GetCSharpParameterList(commandDeclaration.Parameters);
        if (csharpParameters == null)
        {
            return ImmutableArray<ICommandParameterSymbol>.Empty;
        }

        using var builder = ImmutableArrayBuilder<ICommandParameterSymbol>.Rent();
        for (var index = 0; index < csharpParameters.Parameters.Count; index++)
        {
            var parameter = csharpParameters.Parameters[index];
            var name = parameter.Identifier.ValueText;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var type = parameter.Type == null
                ? default
                : ResolveCSharpParameterType(parameter.Type);

            builder.Add(new CommandParameterSymbol(
                name,
                index,
                type));
        }

        return builder.ToImmutable();
    }

    private CSharpSymbolDefinition ResolveCSharpParameterType(CSharp.TypeSyntax typeSyntax)
    {
        var binding = BindCSharpType(typeSyntax);
        return binding.TypeSymbol == null ? default : new CSharpSymbolDefinition(binding.TypeSymbol);
    }

    private ImmutableArray<ITailwindUtilityParameterSymbol> CreateTailwindUtilityParameters(
        AkcssUtilityDeclarationSyntax utilityDeclaration)
    {
        var csharpParameters = BindTailwindUtilityCSharpParameters(utilityDeclaration);
        using var builder = ImmutableArrayBuilder<ITailwindUtilityParameterSymbol>.Rent();

        for (var index = 0; index < utilityDeclaration.Selector.Parameters.Count; index++)
        {
            var parameter = utilityDeclaration.Selector.Parameters[index];
            var csharpParameter = index < csharpParameters.Length
                ? csharpParameters[index]
                : null;
            var type = csharpParameter?.Type == null
                ? ResolveTailwindUtilityParameterType(parameter)
                : new CSharpSymbolDefinition(csharpParameter.Type);

            builder.Add(new TailwindUtilityParameterSymbol(
                parameter,
                index,
                type,
                csharpParameter));
        }

        return builder.ToImmutable();
    }

    private ImmutableArray<IParameterSymbol> BindTailwindUtilityCSharpParameters(
        AkcssUtilityDeclarationSyntax utilityDeclaration)
    {
        using var parametersBuilder = ImmutableArrayBuilder<CSharp.ParameterSyntax>.Rent();
        foreach (var parameter in utilityDeclaration.Selector.Parameters)
        {
            var parameterName = parameter.ParamName.Identifier.ValueText;
            if (string.IsNullOrWhiteSpace(parameterName))
            {
                return ImmutableArray<IParameterSymbol>.Empty;
            }

            try
            {
                parametersBuilder.Add(CSharpSyntaxFactory.Parameter(
                        CSharpSyntaxFactory.Identifier(parameterName))
                    .WithType(parameter.Type.ToCSharp()));
            }
            catch (InvalidOperationException)
            {
                return ImmutableArray<IParameterSymbol>.Empty;
            }
        }

        var method = CSharpSyntaxFactory.MethodDeclaration(
                CSharpSyntaxFactory.PredefinedType(
                    CSharpSyntaxFactory.Token(Microsoft.CodeAnalysis.CSharp.SyntaxKind.VoidKeyword)),
                "__AkburaUtility")
            .WithParameterList(CSharpSyntaxFactory.ParameterList(
                CSharpSyntaxFactory.SeparatedList(parametersBuilder.ToImmutable())))
            .WithBody(CSharpSyntaxFactory.Block());

        var probeClass = CSharpSyntaxFactory.ClassDeclaration("__AkburaSemanticProbe")
            .WithMembers(CSharpSyntaxFactory.SingletonList<CSharp.MemberDeclarationSyntax>(method));

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
        var probeMethod = syntaxTree
            .GetCompilationUnitRoot()
            .DescendantNodes()
            .OfType<CSharp.MethodDeclarationSyntax>()
            .Single();

        return semanticModel.GetDeclaredSymbol(probeMethod)?.Parameters ??
            ImmutableArray<IParameterSymbol>.Empty;
    }

    private CSharpSymbolDefinition ResolveTailwindUtilityParameterType(
        AkcssUtilityParameterSyntax parameter)
    {
        try
        {
            return ResolveCSharpParameterType(parameter.Type.ToCSharp());
        }
        catch (InvalidOperationException)
        {
            return default;
        }
    }

    private ImmutableArray<IAkcssOperation> CreateAkcssOperations(
        Akbura.Language.Syntax.SyntaxList<AkcssBodyMemberSyntax> members,
        IAkcssSymbol containingSymbol)
    {
        using var builder = ImmutableArrayBuilder<IAkcssOperation>.Rent();
        foreach (var member in members)
        {
            if (member is not AkcssAssignmentSyntax assignment)
            {
                if (member is AkcssIfDirectiveSyntax ifDirective)
                {
                    var ifOperation = CreateAkcssIfOperation(ifDirective, containingSymbol);
                    _operationCache[ifDirective] = ifOperation;
                    builder.Add(ifOperation);
                }

                continue;
            }

            var operation = CreateAkcssPropertySetterOperation(assignment, containingSymbol);
            _operationCache[assignment] = operation;
            builder.Add(operation);
        }

        return builder.ToImmutable();
    }

    private AkburaOperation? ResolveAkcssPropertySetterOperation(AkcssAssignmentSyntax assignment)
    {
        var containingSymbol = GetContainingAkcssSymbol(assignment);
        if (containingSymbol == null)
        {
            SetSemanticDiagnostics(assignment, ImmutableArray<AkburaSemanticDiagnostic>.Empty);
            return null;
        }

        if (_operationCache.TryGetValue(assignment, out var cachedOperation))
        {
            return cachedOperation;
        }

        return CreateAkcssPropertySetterOperation(assignment, containingSymbol);
    }

    private AkburaOperation? ResolveAkcssIfOperation(AkcssIfDirectiveSyntax ifDirective)
    {
        var containingSymbol = GetContainingAkcssSymbol(ifDirective);
        if (containingSymbol == null)
        {
            SetSemanticDiagnostics(ifDirective, ImmutableArray<AkburaSemanticDiagnostic>.Empty);
            return null;
        }

        if (_operationCache.TryGetValue(ifDirective, out var cachedOperation))
        {
            return cachedOperation;
        }

        return CreateAkcssIfOperation(ifDirective, containingSymbol);
    }

    private IAkcssSymbol? GetContainingAkcssSymbol(AkburaSyntax syntax)
    {
        for (var node = syntax.Parent; node != null; node = node.Parent)
        {
            if (node is AkcssStyleRuleSyntax styleRule)
            {
                return GetSymbolInfo(styleRule).Symbol as IAkcssSymbol;
            }

            if (node is AkcssUtilityDeclarationSyntax utilityDeclaration)
            {
                return GetSymbolInfo(utilityDeclaration).Symbol as IAkcssSymbol;
            }
        }

        return null;
    }

    private AkcssIfOperation CreateAkcssIfOperation(
        AkcssIfDirectiveSyntax ifDirective,
        IAkcssSymbol containingSymbol)
    {
        var expression = ParseAkcssConditionExpression(ifDirective);
        var binding = expression == null
            ? CSharpTypeBinding.Empty
            : BindAkcssExpression(expression, containingSymbol);
        var conditionType = binding.TypeSymbol == null
            ? default
            : new CSharpSymbolDefinition(binding.TypeSymbol);
        var operations = CreateAkcssOperations(ifDirective.Members, containingSymbol);

        SetSemanticDiagnostics(ifDirective, ImmutableArray<AkburaSemanticDiagnostic>.Empty);

        return new AkcssIfOperation(
            ifDirective,
            containingSymbol,
            conditionType,
            binding.OperationDefinition,
            operations,
            hasErrors: expression == null || operations.Any(static operation => operation.HasErrors));
    }

    private AkcssPropertySetterOperation CreateAkcssPropertySetterOperation(
        AkcssAssignmentSyntax assignment,
        IAkcssSymbol containingSymbol)
    {
        using var diagnosticsBuilder = ImmutableArrayBuilder<AkburaSemanticDiagnostic>.Rent();

        var property = ResolveAkcssProperty(assignment, containingSymbol, diagnosticsBuilder);
        var expression = ParseAkcssAssignmentExpression(assignment);
        var binding = expression == null
            ? CSharpTypeBinding.Empty
            : BindAkcssExpression(expression, containingSymbol);

        var valueType = binding.TypeSymbol == null
            ? default
            : new CSharpSymbolDefinition(binding.TypeSymbol);
        var valueOperation = binding.OperationDefinition;
        var valueKind = AkcssPropertyValueKind.CSharpExpression;
        var requiresBrushConversion = false;
        object? convertedValue = null;

        if (property?.Type.Symbol is ITypeSymbol expectedType && expression != null)
        {
            if (IsAkcssColorPropertyType(expectedType))
            {
                if (TryGetAkcssColorIdentifierText(expression, out var colorName))
                {
                    if (TryBindAvaloniaNamedColor(colorName, containingSymbol, out var namedColorBinding))
                    {
                        valueKind = AkcssPropertyValueKind.ColorLiteral;
                        convertedValue = new CSharpSymbolDefinition(namedColorBinding.Symbol!);
                        valueOperation = namedColorBinding.OperationDefinition;
                        requiresBrushConversion = IsAvaloniaBrushType(expectedType);
                        valueType = namedColorBinding.TypeSymbol == null
                            ? valueType
                            : new CSharpSymbolDefinition(namedColorBinding.TypeSymbol);
                    }
                    else
                    {
                        valueKind = AkcssPropertyValueKind.Error;
                        diagnosticsBuilder.Add(CreateAkcssInvalidColorDiagnostic(
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
                        requiresBrushConversion = IsAvaloniaBrushType(expectedType);
                        if (TryGetAvaloniaColorType(out var colorType))
                        {
                            valueType = new CSharpSymbolDefinition(colorType);
                        }
                    }
                    else if (TryBindAvaloniaNamedColor(colorText, containingSymbol, out var namedColorBinding))
                    {
                        valueKind = AkcssPropertyValueKind.ColorLiteral;
                        convertedValue = new CSharpSymbolDefinition(namedColorBinding.Symbol!);
                        valueOperation = namedColorBinding.OperationDefinition;
                        requiresBrushConversion = IsAvaloniaBrushType(expectedType);
                        valueType = namedColorBinding.TypeSymbol == null
                            ? valueType
                            : new CSharpSymbolDefinition(namedColorBinding.TypeSymbol);
                    }
                    else
                    {
                        valueKind = AkcssPropertyValueKind.Error;
                        diagnosticsBuilder.Add(CreateAkcssInvalidColorDiagnostic(
                            assignment,
                            colorText,
                            property.Name));
                    }
                }
                else if (binding.TypeSymbol != null &&
                         IsAvaloniaColorType(binding.TypeSymbol) &&
                         IsAvaloniaBrushType(expectedType))
                {
                    requiresBrushConversion = true;
                }
            }

            var isThicknessPropertyType = IsAvaloniaThicknessType(expectedType);
            var isThicknessTuple = false;
            object? thickness = null;
            if (isThicknessPropertyType &&
                TryCreateAkcssThicknessValue(
                    expression,
                    assignment.Expression.ToFullString(),
                    out thickness,
                    out isThicknessTuple))
            {
                valueKind = AkcssPropertyValueKind.ThicknessTuple;
                convertedValue = thickness;
                valueType = new CSharpSymbolDefinition(expectedType);
            }
            else if (isThicknessPropertyType && isThicknessTuple)
            {
                valueKind = AkcssPropertyValueKind.Error;
                diagnosticsBuilder.Add(CreateAkcssInvalidThicknessDiagnostic(
                    assignment,
                    assignment.Expression.ToFullString().Trim(),
                    property.Name));
            }

            if (valueKind == AkcssPropertyValueKind.CSharpExpression &&
                TryCreateAkcssAmxInvocationValue(expression, out var amxInvocation))
            {
                valueKind = AkcssPropertyValueKind.AmxInvocation;
                convertedValue = amxInvocation;
            }
        }

        var diagnostics = diagnosticsBuilder.ToImmutable();
        SetSemanticDiagnostics(assignment, diagnostics);

        return new AkcssPropertySetterOperation(
            assignment,
            containingSymbol,
            property,
            valueType,
            valueOperation,
            valueKind,
            requiresBrushConversion,
            convertedValue,
            property == null || valueKind == AkcssPropertyValueKind.Error || diagnostics.Length > 0);
    }

    private AkburaPropertySymbol? ResolveAkcssProperty(
        AkcssAssignmentSyntax assignment,
        IAkcssSymbol containingSymbol,
        ImmutableArrayBuilder<AkburaSemanticDiagnostic> diagnosticsBuilder)
    {
        var propertyName = assignment.PropertyName.Identifier.ValueText;
        if (string.IsNullOrWhiteSpace(propertyName) ||
            !TryGetAkcssPropertyOwner(containingSymbol, out var ownerType))
        {
            return null;
        }

        var clrProperty = FindPublicClrProperty(ownerType, propertyName);
        var avaloniaProperty = FindAvaloniaPropertyField(ownerType, propertyName);
        if (clrProperty == null && avaloniaProperty == null)
        {
            diagnosticsBuilder.Add(CreateAkcssPropertyNotFoundDiagnostic(
                assignment,
                propertyName,
                ownerType));
            return null;
        }

        return new PropertySymbol(
            propertyName,
            GetMarkupPropertyType(
                parameter: null,
                command: null,
                clrProperty,
                avaloniaProperty),
            avaloniaProperty == null ? default : new CSharpSymbolDefinition(avaloniaProperty),
            clrProperty == null ? default : new CSharpSymbolDefinition(clrProperty),
            language: SymbolLanguage.Akcss,
            containingSymbol: containingSymbol);
    }

    private bool TryGetAkcssPropertyOwner(
        IAkcssSymbol containingSymbol,
        out INamedTypeSymbol ownerType)
    {
        if (containingSymbol.TargetType.Symbol is INamedTypeSymbol targetType)
        {
            ownerType = targetType;
            return true;
        }

        return TryGetDefaultAkcssStyleTargetType(out ownerType);
    }

    private bool TryGetDefaultAkcssStyleTargetType(out INamedTypeSymbol targetType)
    {
        targetType = Compilation.CSharpCompilation.GetTypeByMetadataName(
            "Avalonia.Controls.Primitives.TemplatedControl")!;
        return targetType != null;
    }

    private static CSharp.ExpressionSyntax? ParseAkcssAssignmentExpression(AkcssAssignmentSyntax assignment)
    {
        try
        {
            return CSharpSyntaxFactory.ParseExpression(assignment.Expression.ToFullString());
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static CSharp.ExpressionSyntax? ParseAkcssConditionExpression(AkcssIfDirectiveSyntax ifDirective)
    {
        try
        {
            return CSharpSyntaxFactory.ParseExpression(ifDirective.Condition.ToFullString());
        }
        catch (ArgumentException)
        {
            return null;
        }
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
            literal.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.StringLiteralExpression)
                ? literal.Token.ValueText
                : string.Empty;

        return text.Length > 0;
    }

    private bool TryBindAvaloniaNamedColor(
        string colorName,
        IAkcssSymbol containingSymbol,
        out CSharpTypeBinding binding)
    {
        binding = CSharpTypeBinding.Empty;
        if (string.IsNullOrWhiteSpace(colorName) ||
            !IsValidCSharpIdentifier(colorName))
        {
            return false;
        }

        var expression = CSharpSyntaxFactory.ParseExpression(
            "global::Avalonia.Media.Colors." + colorName);
        binding = BindAkcssExpression(expression, containingSymbol);
        return binding.Symbol is RoslynPropertySymbol property &&
            property.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
            "global::Avalonia.Media.Colors" &&
            binding.TypeSymbol != null &&
            IsAvaloniaColorType(binding.TypeSymbol);
    }

    private static bool IsValidCSharpIdentifier(string text)
    {
        if (text.Length == 0 ||
            !Microsoft.CodeAnalysis.CSharp.SyntaxFacts.IsIdentifierStartCharacter(text[0]))
        {
            return false;
        }

        for (var i = 1; i < text.Length; i++)
        {
            if (!Microsoft.CodeAnalysis.CSharp.SyntaxFacts.IsIdentifierPartCharacter(text[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryCreateAkcssThicknessValue(
        CSharp.ExpressionSyntax expression,
        string rawText,
        out object? thickness,
        out bool isThicknessTuple)
    {
        if (expression is CSharp.TupleExpressionSyntax tupleExpression)
        {
            isThicknessTuple = true;
            if (TryCreateAkcssThicknessValue(tupleExpression, out thickness))
            {
                return true;
            }

            var tupleText = rawText.Trim();
            return tupleText.StartsWith("(", StringComparison.Ordinal) &&
                tupleText.EndsWith(")", StringComparison.Ordinal) &&
                tupleText.IndexOf(':') >= 0 &&
                TryCreateNamedAkcssThicknessValue(tupleText[1..^1], out thickness);
        }

        var text = rawText.Trim();
        if (!text.StartsWith("(", StringComparison.Ordinal) ||
            !text.EndsWith(")", StringComparison.Ordinal) ||
            text.IndexOf(':') < 0)
        {
            thickness = default;
            isThicknessTuple = false;
            return false;
        }

        isThicknessTuple = true;
        return TryCreateNamedAkcssThicknessValue(text[1..^1], out thickness);
    }

    private static bool TryCreateAkcssThicknessValue(
        CSharp.TupleExpressionSyntax tupleExpression,
        out object? thickness)
    {
        thickness = null;
        var arguments = tupleExpression.Arguments;

        if (arguments.Count == 2 &&
            arguments[0].NameColon == null &&
            arguments[1].NameColon == null)
        {
            return TryCreateAkcssThicknessValue(
                arguments[0].Expression,
                arguments[1].Expression,
                arguments[0].Expression,
                arguments[1].Expression,
                out thickness);
        }

        if (arguments.Count == 4 &&
            arguments.All(static argument => argument.NameColon == null))
        {
            return TryCreateAkcssThicknessValue(
                arguments[0].Expression,
                arguments[1].Expression,
                arguments[2].Expression,
                arguments[3].Expression,
                out thickness);
        }

        var zero = CreateAkcssZeroExpression();
        var left = zero;
        var top = zero;
        var right = zero;
        var bottom = zero;
        foreach (var argument in arguments)
        {
            if (argument.NameColon == null)
            {
                return false;
            }

            switch (argument.NameColon.Name.Identifier.ValueText)
            {
                case "left":
                    left = argument.Expression;
                    break;
                case "top":
                    top = argument.Expression;
                    break;
                case "right":
                    right = argument.Expression;
                    break;
                case "bottom":
                    bottom = argument.Expression;
                    break;
                case "horizontal":
                    left = right = argument.Expression;
                    break;
                case "vertical":
                    top = bottom = argument.Expression;
                    break;
                default:
                    return false;
            }
        }

        return arguments.Count > 0 &&
            TryCreateAkcssThicknessValue(left, top, right, bottom, out thickness);
    }

    private static bool TryCreateNamedAkcssThicknessValue(
        string text,
        out object? thickness)
    {
        thickness = null;
        var zero = CreateAkcssZeroExpression();
        var left = zero;
        var top = zero;
        var right = zero;
        var bottom = zero;

        foreach (var component in text.Split(','))
        {
            var separatorIndex = component.IndexOf(':');
            if (separatorIndex <= 0 ||
                separatorIndex == component.Length - 1)
            {
                return false;
            }

            CSharp.ExpressionSyntax expression;
            try
            {
                expression = CSharpSyntaxFactory.ParseExpression(
                    component[(separatorIndex + 1)..].Trim());
            }
            catch (ArgumentException)
            {
                return false;
            }

            switch (component[..separatorIndex].Trim())
            {
                case "left":
                    left = expression;
                    break;
                case "top":
                    top = expression;
                    break;
                case "right":
                    right = expression;
                    break;
                case "bottom":
                    bottom = expression;
                    break;
                case "horizontal":
                    left = right = expression;
                    break;
                case "vertical":
                    top = bottom = expression;
                    break;
                default:
                    return false;
            }
        }

        return TryCreateAkcssThicknessValue(left, top, right, bottom, out thickness);
    }

    private static bool TryCreateAkcssThicknessValue(
        CSharp.ExpressionSyntax left,
        CSharp.ExpressionSyntax top,
        CSharp.ExpressionSyntax right,
        CSharp.ExpressionSyntax bottom,
        out object thickness)
    {
        if (TryParseAkcssDouble(left, out var leftValue) &&
            TryParseAkcssDouble(top, out var topValue) &&
            TryParseAkcssDouble(right, out var rightValue) &&
            TryParseAkcssDouble(bottom, out var bottomValue))
        {
            thickness = new AkcssThicknessValue(leftValue, topValue, rightValue, bottomValue);
            return true;
        }

        thickness = new AkcssThicknessExpressionValue(left, top, right, bottom);
        return true;
    }

    private static CSharp.ExpressionSyntax CreateAkcssZeroExpression()
    {
        return CSharpSyntaxFactory.LiteralExpression(
            Microsoft.CodeAnalysis.CSharp.SyntaxKind.NumericLiteralExpression,
            CSharpSyntaxFactory.Literal(0));
    }

    private static bool TryParseAkcssDouble(
        CSharp.ExpressionSyntax expression,
        out double value)
    {
        return double.TryParse(
            expression.ToString(),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out value);
    }

    private bool TryCreateAkcssAmxInvocationValue(
        CSharp.ExpressionSyntax expression,
        out AkcssAmxInvocationValue value)
    {
        foreach (var invocation in expression
                     .DescendantNodesAndSelf()
                     .OfType<CSharp.InvocationExpressionSyntax>())
        {
            if (!TryGetAkcssAmxInvocationKind(invocation.Expression, out var kind, out var genericName))
            {
                continue;
            }

            var typeArgument = default(CSharpSymbolDefinition);
            if (genericName?.TypeArgumentList.Arguments.Count == 1)
            {
                var binding = BindCSharpType(genericName.TypeArgumentList.Arguments[0]);
                if (binding.TypeSymbol != null)
                {
                    typeArgument = new CSharpSymbolDefinition(binding.TypeSymbol);
                }
            }

            value = new AkcssAmxInvocationValue(
                kind,
                typeArgument,
                invocation.ArgumentList.Arguments.Select(static argument => argument.Expression).ToImmutableArray(),
                methodSymbol: null);
            return true;
        }

        value = default;
        return false;
    }

    private static bool TryGetAkcssAmxInvocationKind(
        CSharp.ExpressionSyntax expression,
        out AkcssAmxInvocationKind kind,
        out CSharp.GenericNameSyntax? genericName)
    {
        kind = AkcssAmxInvocationKind.None;
        genericName = null;

        if (expression is not CSharp.MemberAccessExpressionSyntax memberAccess ||
            memberAccess.Expression.ToString() != "Amx")
        {
            return false;
        }

        var methodName = memberAccess.Name switch
        {
            CSharp.GenericNameSyntax name => name.Identifier.ValueText,
            CSharp.IdentifierNameSyntax name => name.Identifier.ValueText,
            _ => string.Empty,
        };

        genericName = memberAccess.Name as CSharp.GenericNameSyntax;
        kind = methodName switch
        {
            "Extend" => AkcssAmxInvocationKind.Extend,
            "StaticResource" => AkcssAmxInvocationKind.StaticResource,
            "DynamicResource" => AkcssAmxInvocationKind.DynamicResource,
            _ => AkcssAmxInvocationKind.None,
        };

        return kind != AkcssAmxInvocationKind.None;
    }

    private bool IsAkcssColorPropertyType(ITypeSymbol type)
    {
        return IsAvaloniaColorType(type) || IsAvaloniaBrushType(type);
    }

    private bool IsAvaloniaColorType(ITypeSymbol type)
    {
        return TryGetAvaloniaColorType(out var colorType) &&
            IsSameType(type, colorType);
    }

    private bool IsAvaloniaBrushType(ITypeSymbol type)
    {
        return TryGetAvaloniaBrushType(out var brushType) &&
            IsAssignableTo(type, brushType);
    }

    private bool IsAvaloniaThicknessType(ITypeSymbol type)
    {
        return TryGetAvaloniaThicknessType(out var thicknessType) &&
            IsSameType(type, thicknessType);
    }

    private bool TryGetAvaloniaColorType(out INamedTypeSymbol colorType)
    {
        colorType = Compilation.CSharpCompilation.GetTypeByMetadataName("Avalonia.Media.Color")!;
        return colorType != null;
    }

    private bool TryGetAvaloniaBrushType(out INamedTypeSymbol brushType)
    {
        brushType = Compilation.CSharpCompilation.GetTypeByMetadataName("Avalonia.Media.IBrush")!;
        return brushType != null;
    }

    private bool TryGetAvaloniaThicknessType(out INamedTypeSymbol thicknessType)
    {
        thicknessType = Compilation.CSharpCompilation.GetTypeByMetadataName("Avalonia.Thickness")!;
        return thicknessType != null;
    }

    private AkburaSemanticDiagnostic CreateAkcssPropertyNotFoundDiagnostic(
        AkcssAssignmentSyntax syntax,
        string propertyName,
        INamedTypeSymbol ownerType)
    {
        return new AkburaSemanticDiagnostic(
            syntax,
            ErrorCodes.AKBURA_SEMANTIC_AkcssPropertyNotFound,
            [propertyName, ownerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)]);
    }

    private AkburaSemanticDiagnostic CreateAkcssInvalidColorDiagnostic(
        AkcssAssignmentSyntax syntax,
        string colorText,
        string propertyName)
    {
        return new AkburaSemanticDiagnostic(
            syntax,
            ErrorCodes.AKBURA_SEMANTIC_AkcssInvalidColor,
            [colorText, propertyName]);
    }

    private AkburaSemanticDiagnostic CreateAkcssInvalidThicknessDiagnostic(
        AkcssAssignmentSyntax syntax,
        string tupleText,
        string propertyName)
    {
        return new AkburaSemanticDiagnostic(
            syntax,
            ErrorCodes.AKBURA_SEMANTIC_AkcssInvalidThickness,
            [tupleText, propertyName]);
    }

    private bool TryResolveAkcssTargetType(
        SimpleNameSyntax? targetTypeSyntax,
        out CSharpSymbolDefinition targetType)
    {
        targetType = default;
        if (targetTypeSyntax == null)
        {
            return true;
        }

        var targetTypeName = targetTypeSyntax.Identifier.ValueText;
        if (string.IsNullOrWhiteSpace(targetTypeName))
        {
            return false;
        }

        var binding = BindCSharpType(CSharpSyntaxFactory.ParseTypeName(targetTypeName));
        if (binding.TypeSymbol is INamedTypeSymbol boundType &&
            IsAvaloniaControlTargetType(boundType))
        {
            targetType = new CSharpSymbolDefinition(boundType);
            return true;
        }

        var avaloniaType = Compilation.CSharpCompilation.GetTypeByMetadataName(
            "Avalonia.Controls." + targetTypeName);
        if (avaloniaType != null &&
            IsAvaloniaControlTargetType(avaloniaType))
        {
            targetType = new CSharpSymbolDefinition(avaloniaType);
            return true;
        }

        return false;
    }

    private bool IsAvaloniaControlTargetType(INamedTypeSymbol type)
    {
        return TryGetAvaloniaControlType(out var controlType) &&
            IsAssignableTo(type, controlType);
    }

    private static CSharp.ParameterListSyntax? GetCSharpParameterList(
        CSharpParameterListSyntax parameterListSyntax)
    {
        return parameterListSyntax.Parameters.Node is GreenSyntaxToken.CSharpRawToken rawToken
            ? rawToken.RawNode as CSharp.ParameterListSyntax
            : null;
    }

    private CSharpSymbolDefinition GetCommandResultType(
        CSharpSymbolDefinition returnType,
        bool isVoid,
        bool isAsyncLike)
    {
        if (returnType.Symbol is not ITypeSymbol returnTypeSymbol ||
            isVoid)
        {
            return default;
        }

        if (isAsyncLike)
        {
            return TryGetTaskLikeResultType(returnTypeSymbol, out var resultType)
                ? new CSharpSymbolDefinition(resultType)
                : default;
        }

        return returnType;
    }

    private static bool IsTaskLikeType(ITypeSymbol type)
    {
        return IsTaskLikeMetadataName(type, genericOnly: false);
    }

    private static bool TryGetTaskLikeResultType(
        ITypeSymbol type,
        out ITypeSymbol resultType)
    {
        if (type is INamedTypeSymbol namedType &&
            namedType.TypeArguments.Length == 1 &&
            IsTaskLikeMetadataName(namedType, genericOnly: true))
        {
            resultType = namedType.TypeArguments[0];
            return true;
        }

        resultType = null!;
        return false;
    }

    private static bool IsTaskLikeMetadataName(
        ITypeSymbol type,
        bool genericOnly)
    {
        if (type is not INamedTypeSymbol namedType)
        {
            return false;
        }

        var metadataName = namedType.OriginalDefinition.ToDisplayString();
        return metadataName is "System.Threading.Tasks.Task" or "System.Threading.Tasks.ValueTask" ||
            (!genericOnly &&
             metadataName is "System.Threading.Tasks.Task<TResult>" or "System.Threading.Tasks.ValueTask<TResult>") ||
            (genericOnly &&
             metadataName is "System.Threading.Tasks.Task<TResult>" or "System.Threading.Tasks.ValueTask<TResult>");
    }

    private CSharpSymbolDefinition ResolveParamDefaultValueType(ParamDeclarationSyntax paramDeclaration)
    {
        var defaultValue = paramDeclaration.DefaultValue;
        if (defaultValue == null)
        {
            return default;
        }

        CSharp.ExpressionSyntax csharpExpression;
        try
        {
            csharpExpression = CSharpSyntaxFactory.ParseExpression(defaultValue.ToFullString());
        }
        catch (ArgumentException)
        {
            return default;
        }

        var binding = BindCSharpExpression(csharpExpression);
        var typeSymbol = binding.TypeSymbol;
        return typeSymbol == null ? default : new CSharpSymbolDefinition(typeSymbol);
    }

    private CSharpTypeBinding BindStateInitializerExpression(StateDeclarationSyntax stateDeclaration)
    {
        CSharp.ExpressionSyntax csharpExpression;
        try
        {
            csharpExpression = CSharpSyntaxFactory.ParseExpression(stateDeclaration.Initializer.Expression.ToFullString());
        }
        catch (ArgumentException)
        {
            return CSharpTypeBinding.Empty;
        }

        return BindCSharpExpression(
            csharpExpression,
            stateDeclaration,
            isBindingPath: IsStateBindingPath(csharpExpression));
    }

    private static StateBindingKind GetStateBindingKind(StateInitializerSyntax initializer)
    {
        if (initializer is not BindableStateInitializerSyntax bindableInitializer)
        {
            return StateBindingKind.None;
        }

        return bindableInitializer.BindingKeyword.Kind switch
        {
            Akbura.Language.Syntax.SyntaxKind.InToken => StateBindingKind.In,
            Akbura.Language.Syntax.SyntaxKind.OutToken => StateBindingKind.Out,
            Akbura.Language.Syntax.SyntaxKind.BindToken => StateBindingKind.Bind,
            _ => StateBindingKind.None,
        };
    }

    private static ParamBindingKind GetParamBindingKind(ParamDeclarationSyntax paramDeclaration)
    {
        return paramDeclaration.BindingKeyword.Kind switch
        {
            Akbura.Language.Syntax.SyntaxKind.BindToken => ParamBindingKind.Bind,
            Akbura.Language.Syntax.SyntaxKind.OutToken => ParamBindingKind.Out,
            _ => ParamBindingKind.Default,
        };
    }

    private ImmutableArray<AkburaSemanticDiagnostic> CreateStateBindingDiagnostics(
        StateDeclarationSyntax stateDeclaration,
        StateBindingKind bindingKind,
        CSharpSymbolDefinition stateType,
        CSharpTypeBinding initializerBinding)
    {
        if (bindingKind == StateBindingKind.None)
        {
            return ImmutableArray<AkburaSemanticDiagnostic>.Empty;
        }

        using var diagnosticsBuilder = ImmutableArrayBuilder<AkburaSemanticDiagnostic>.Rent();

        if (!initializerBinding.IsBindingPath)
        {
            diagnosticsBuilder.Add(CreateStateBindingExpressionExpectedDiagnostic(stateDeclaration));
            return diagnosticsBuilder.ToImmutable();
        }

        if (RequiresWritableStateBindingTarget(bindingKind) &&
            !IsWritableStateBindingTarget(initializerBinding.Symbol))
        {
            diagnosticsBuilder.Add(CreateStateBindingTargetNotWritableDiagnostic(stateDeclaration));
        }

        if (RequiresObservableStateBindingSource(bindingKind) &&
            !CanObserveStateBindingSource(initializerBinding, stateType))
        {
            diagnosticsBuilder.Add(CreateStateBindingSourceNotObservableDiagnostic(
                stateDeclaration,
                stateType,
                initializerBinding));
        }

        return diagnosticsBuilder.ToImmutable();
    }

    private static bool RequiresWritableStateBindingTarget(StateBindingKind bindingKind)
    {
        return bindingKind is StateBindingKind.Bind or StateBindingKind.In;
    }

    private static bool RequiresObservableStateBindingSource(StateBindingKind bindingKind)
    {
        return bindingKind is StateBindingKind.Bind or StateBindingKind.Out;
    }

    private static bool IsWritableStateBindingTarget(RoslynSymbol? symbol)
    {
        return symbol switch
        {
            RoslynPropertySymbol property => property.SetMethod?.DeclaredAccessibility == Accessibility.Public,
            RoslynFieldSymbol field => !field.IsReadOnly && !field.IsConst,
            null => true,
            _ => false,
        };
    }

    private bool CanObserveStateBindingSource(
        CSharpTypeBinding binding,
        CSharpSymbolDefinition stateType)
    {
        if (binding.TypeSymbol != null &&
            TryGetIObservableElementType(binding.TypeSymbol, out var observableElementType) &&
            (stateType.Symbol is not ITypeSymbol expectedType ||
             IsSameType(observableElementType, expectedType)))
        {
            return true;
        }

        var containingType = GetBindingSourceContainingType(binding.Symbol) ??
            binding.ReceiverType as INamedTypeSymbol;

        if (containingType != null &&
            ImplementsINotifyPropertyChanged(containingType))
        {
            return true;
        }

        return binding.Symbol is RoslynFieldSymbol or RoslynPropertySymbol &&
            binding.ReceiverType == null &&
            binding.TypeSymbol != null &&
            ImplementsINotifyPropertyChanged(binding.TypeSymbol);
    }

    private static bool IsStateBindingPath(CSharp.ExpressionSyntax expression)
    {
        return expression switch
        {
            CSharp.IdentifierNameSyntax => true,
            CSharp.ThisExpressionSyntax => true,
            CSharp.BaseExpressionSyntax => true,
            CSharp.ParenthesizedExpressionSyntax parenthesized => IsStateBindingPath(parenthesized.Expression),
            CSharp.MemberAccessExpressionSyntax memberAccess => IsStateBindingPath(memberAccess.Expression),
            CSharp.ElementAccessExpressionSyntax elementAccess => IsStateBindingPath(elementAccess.Expression),
            _ => false,
        };
    }

    private static INamedTypeSymbol? GetBindingSourceContainingType(RoslynSymbol? symbol)
    {
        return symbol switch
        {
            RoslynPropertySymbol property => property.ContainingType,
            RoslynFieldSymbol field => field.ContainingType,
            _ => null,
        };
    }

    private static bool ImplementsINotifyPropertyChanged(ITypeSymbol type)
    {
        foreach (var @interface in type.AllInterfaces)
        {
            if (@interface.Name == "INotifyPropertyChanged" &&
                @interface.ContainingNamespace.ToDisplayString() == "System.ComponentModel")
            {
                return true;
            }
        }

        return false;
    }

    private bool TryGetIObservableElementType(ITypeSymbol type, out ITypeSymbol elementType)
    {
        if (type is INamedTypeSymbol namedType &&
            IsIObservableOfT(namedType))
        {
            elementType = namedType.TypeArguments[0];
            return true;
        }

        foreach (var @interface in type.AllInterfaces)
        {
            if (IsIObservableOfT(@interface))
            {
                elementType = @interface.TypeArguments[0];
                return true;
            }
        }

        elementType = null!;
        return false;
    }

    private static bool IsIObservableOfT(INamedTypeSymbol type)
    {
        var original = type.OriginalDefinition;
        return original.Name == "IObservable" &&
            original.Arity == 1 &&
            original.ContainingNamespace.ToDisplayString() == "System";
    }

    private AkburaSemanticDiagnostic CreateStateBindingSourceNotObservableDiagnostic(
        StateDeclarationSyntax stateDeclaration,
        CSharpSymbolDefinition stateType,
        CSharpTypeBinding binding)
    {
        var sourceText = stateDeclaration.Initializer.Expression.ToFullString().Trim();
        var stateTypeText = stateType.IsDefault
            ? "state type"
            : stateType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        var ownerText = GetBindingSourceContainingType(binding.Symbol)?.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat) ??
            binding.ReceiverType?.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat) ??
            "source object";

        return new AkburaSemanticDiagnostic(
            stateDeclaration,
            ErrorCodes.AKBURA_SEMANTIC_StateBindingSourceNotObservable,
            [sourceText, stateTypeText, ownerText],
            AkburaDiagnosticSeverity.Warning);
    }

    private AkburaSemanticDiagnostic CreateStateBindingExpressionExpectedDiagnostic(
        StateDeclarationSyntax stateDeclaration)
    {
        var expressionText = stateDeclaration.Initializer.Expression.ToFullString().Trim();

        return new AkburaSemanticDiagnostic(
            stateDeclaration,
            ErrorCodes.AKBURA_SEMANTIC_StateBindingExpressionExpected,
            [expressionText],
            AkburaDiagnosticSeverity.Error);
    }

    private AkburaSemanticDiagnostic CreateStateBindingTargetNotWritableDiagnostic(
        StateDeclarationSyntax stateDeclaration)
    {
        var targetText = stateDeclaration.Initializer.Expression.ToFullString().Trim();

        return new AkburaSemanticDiagnostic(
            stateDeclaration,
            ErrorCodes.AKBURA_SEMANTIC_StateBindingTargetNotWritable,
            [targetText],
            AkburaDiagnosticSeverity.Error);
    }

    private AkburaSymbolInfo ResolveMarkupComponent(MarkupElementSyntax markupElement)
    {
        var startTag = markupElement.StartTag;
        if (startTag == null)
        {
            return AkburaSymbolInfo.None(AkburaCandidateReason.UnsupportedSyntax);
        }

        var componentName = startTag.Name;
        var componentNameText = componentName.ToFullString().Trim();
        if (string.IsNullOrWhiteSpace(componentNameText))
        {
            return AkburaSymbolInfo.None(AkburaCandidateReason.UnsupportedSyntax);
        }

        CSharp.TypeSyntax csharpType;
        try
        {
            csharpType = componentName.ToCSharp();
        }
        catch (InvalidOperationException)
        {
            return AkburaSymbolInfo.None(AkburaCandidateReason.UnsupportedSyntax);
        }

        var binding = BindCSharpType(csharpType);
        if (binding.TypeSymbol is INamedTypeSymbol namedType &&
            namedType.TypeKind != TypeKind.Error)
        {
            var contentModel = CreateMarkupContentModel(namedType);
            var children = CreateMarkupChildren(markupElement, contentModel, out var diagnostics);
            SetSemanticDiagnostics(markupElement, diagnostics);

            return AkburaSymbolInfo.Success(new MarkupComponentSymbol(
                componentNameText,
                new CSharpSymbolDefinition(namedType),
                contentModel,
                children));
        }

        SetSemanticDiagnostics(markupElement, ImmutableArray<AkburaSemanticDiagnostic>.Empty);

        var candidates = CreateMarkupComponentCandidates(componentNameText, binding.CandidateSymbols);
        if (candidates.Length > 0)
        {
            return AkburaSymbolInfo.Candidates(candidates, binding.CandidateReason);
        }

        if (TryResolveAkburaMarkupComponent(markupElement, componentNameText, out var akburaComponentSymbol))
        {
            return AkburaSymbolInfo.Success(akburaComponentSymbol);
        }

        return AkburaSymbolInfo.None(AkburaCandidateReason.NotFound);
    }

    private bool TryResolveAkburaMarkupComponent(
        MarkupElementSyntax markupElement,
        string componentNameText,
        out IMarkupComponentSymbol symbol)
    {
        foreach (var candidateMetadataName in GetAkburaComponentCandidateMetadataNames(markupElement.StartTag!.Name))
        {
            foreach (var syntaxTree in Compilation.SyntaxTrees)
            {
                var metadataName = GetAkburaComponentMetadataName(syntaxTree);
                if (metadataName.Length == 0 ||
                    metadataName != candidateMetadataName)
                {
                    continue;
                }

                symbol = new AkburaMarkupComponentSymbol(
                    componentNameText,
                    metadataName,
                    syntaxTree,
                    CreateAkburaComponentParameters(syntaxTree),
                    CreateAkburaComponentCommands(syntaxTree));

                SetSemanticDiagnostics(markupElement, ImmutableArray<AkburaSemanticDiagnostic>.Empty);
                return true;
            }
        }

        symbol = null!;
        return false;
    }

    private IEnumerable<string> GetAkburaComponentCandidateMetadataNames(
        MarkupComponentNameSyntax componentName)
    {
        var nameText = componentName.ToFullString().Trim();
        if (string.IsNullOrWhiteSpace(nameText))
        {
            yield break;
        }

        if (nameText.StartsWith("global::", StringComparison.Ordinal))
        {
            yield return nameText["global::".Length..];
            yield break;
        }

        if (nameText.IndexOf("::", StringComparison.Ordinal) >= 0)
        {
            yield break;
        }

        if (nameText.IndexOf(".", StringComparison.Ordinal) >= 0)
        {
            yield return nameText;
            yield break;
        }

        foreach (var @namespace in GetAkburaUsingNamespaces())
        {
            yield return @namespace + "." + nameText;
        }

        var currentNamespace = GetAkburaNamespaceText(SyntaxTree.GetRoot());
        if (currentNamespace.Length > 0)
        {
            yield return currentNamespace + "." + nameText;
        }

        yield return nameText;
    }

    private ImmutableArray<string> GetAkburaUsingNamespaces()
    {
        using var builder = ImmutableArrayBuilder<string>.Rent();
        foreach (var member in SyntaxTree.GetRoot().Members)
        {
            if (member is not UsingDirectiveSyntax usingDirective ||
                usingDirective.Alias != null ||
                usingDirective.StaticKeyword.RawKind != 0)
            {
                continue;
            }

            var namespaceText = usingDirective.Name.ToFullString().Trim();
            if (namespaceText.Length > 0)
            {
                builder.Add(namespaceText);
            }
        }

        return builder.ToImmutable();
    }

    private ImmutableArray<IParamSymbol> CreateAkburaComponentParameters(AkburaSyntaxTree syntaxTree)
    {
        using var builder = ImmutableArrayBuilder<IParamSymbol>.Rent();
        var semanticModel = Compilation.GetSemanticModel(syntaxTree);

        foreach (var member in syntaxTree.GetRoot().Members)
        {
            if (member is ParamDeclarationSyntax paramDeclaration &&
                semanticModel.GetSymbolInfo(paramDeclaration).Symbol is IParamSymbol paramSymbol)
            {
                builder.Add(paramSymbol);
            }
        }

        return builder.ToImmutable();
    }

    private ImmutableArray<ICommandSymbol> CreateAkburaComponentCommands(AkburaSyntaxTree syntaxTree)
    {
        using var builder = ImmutableArrayBuilder<ICommandSymbol>.Rent();
        var semanticModel = Compilation.GetSemanticModel(syntaxTree);

        foreach (var member in syntaxTree.GetRoot().Members)
        {
            if (member is CommandDeclarationSyntax commandDeclaration &&
                semanticModel.GetSymbolInfo(commandDeclaration).Symbol is ICommandSymbol commandSymbol)
            {
                builder.Add(commandSymbol);
            }
        }

        return builder.ToImmutable();
    }

    private static string GetAkburaComponentMetadataName(AkburaSyntaxTree syntaxTree)
    {
        var componentName = syntaxTree.ComponentName;
        if (componentName.Length == 0)
        {
            return string.Empty;
        }

        var namespaceText = GetAkburaNamespaceText(syntaxTree.GetRoot());
        return namespaceText.Length == 0
            ? componentName
            : namespaceText + "." + componentName;
    }

    private static string GetAkburaNamespaceText(AkburaDocumentSyntax root)
    {
        foreach (var member in root.Members)
        {
            if (member is NamespaceDeclarationSyntax namespaceDeclaration)
            {
                return namespaceDeclaration.Name.ToFullString().Trim();
            }
        }

        return string.Empty;
    }

    private AkburaSymbolInfo ResolveMarkupProperty(MarkupAttributeSyntax markupAttribute)
    {
        var propertyName = GetMarkupPropertyName(markupAttribute);
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            return AkburaSymbolInfo.None(AkburaCandidateReason.UnsupportedSyntax);
        }

        var markupElement = GetContainingMarkupElement(markupAttribute);
        if (markupElement == null)
        {
            return AkburaSymbolInfo.None(AkburaCandidateReason.UnsupportedSyntax);
        }

        var componentSymbolInfo = GetSymbolInfo(markupElement);
        if (componentSymbolInfo.Symbol is not IMarkupComponentSymbol componentSymbol)
        {
            SetSemanticDiagnostics(markupAttribute, ImmutableArray<AkburaSemanticDiagnostic>.Empty);
            return AkburaSymbolInfo.None(componentSymbolInfo.CandidateReason);
        }

        var componentType = componentSymbol.ComponentType;
        var parameter = FindComponentParameter(componentSymbol, propertyName);
        var command = FindComponentCommand(componentSymbol, propertyName);
        RoslynPropertySymbol? clrProperty = null;
        RoslynFieldSymbol? avaloniaProperty = null;

        if (componentType != null)
        {
            clrProperty = FindPublicClrProperty(componentType, propertyName);
            avaloniaProperty = FindAvaloniaPropertyField(componentType, propertyName);
        }

        if (parameter == null &&
            command == null &&
            clrProperty == null &&
            avaloniaProperty == null)
        {
            SetSemanticDiagnostics(
                markupAttribute,
                ImmutableArray.Create(CreateMarkupPropertyNotFoundDiagnostic(
                    markupAttribute,
                    propertyName,
                    componentSymbol)));

            return AkburaSymbolInfo.None(AkburaCandidateReason.NotFound);
        }

        SetSemanticDiagnostics(markupAttribute, ImmutableArray<AkburaSemanticDiagnostic>.Empty);

        return AkburaSymbolInfo.Success(new PropertySymbol(
            propertyName,
            GetMarkupPropertyType(parameter, command, clrProperty, avaloniaProperty),
            avaloniaProperty == null ? default : new CSharpSymbolDefinition(avaloniaProperty),
            clrProperty == null ? default : new CSharpSymbolDefinition(clrProperty),
            parameter,
            command,
            containingSymbol: componentSymbol));
    }

    private static string GetMarkupPropertyName(MarkupAttributeSyntax markupAttribute)
    {
        return markupAttribute switch
        {
            MarkupPlainAttributeSyntax plainAttribute => plainAttribute.Name.Identifier.ValueText,
            MarkupPrefixedAttributeSyntax prefixedAttribute => prefixedAttribute.Name.Identifier.ValueText,
            _ => string.Empty,
        };
    }

    private static MarkupElementSyntax? GetContainingMarkupElement(MarkupAttributeSyntax markupAttribute)
    {
        for (var node = markupAttribute.Parent; node != null; node = node.Parent)
        {
            if (node is MarkupElementSyntax markupElement)
            {
                return markupElement;
            }
        }

        return null;
    }

    private static IParamSymbol? FindComponentParameter(
        IMarkupComponentSymbol componentSymbol,
        string propertyName)
    {
        foreach (var parameter in componentSymbol.Parameters)
        {
            if (parameter.Name == propertyName)
            {
                return parameter;
            }
        }

        return null;
    }

    private static ICommandSymbol? FindComponentCommand(
        IMarkupComponentSymbol componentSymbol,
        string propertyName)
    {
        foreach (var command in componentSymbol.Commands)
        {
            if (command.Name == propertyName)
            {
                return command;
            }
        }

        return null;
    }

    private AkburaSemanticDiagnostic CreateMarkupPropertyNotFoundDiagnostic(
        MarkupAttributeSyntax syntax,
        string propertyName,
        IMarkupComponentSymbol componentSymbol)
    {
        var componentName = componentSymbol.CSharpDefinition.IsDefault
            ? componentSymbol.Name
            : componentSymbol.CSharpDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        return new AkburaSemanticDiagnostic(
            syntax,
            ErrorCodes.AKBURA_SEMANTIC_MarkupPropertyNotFound,
            [propertyName, componentName]);
    }

    private static RoslynPropertySymbol? FindPublicClrProperty(
        INamedTypeSymbol componentType,
        string propertyName)
    {
        for (var current = componentType; current != null; current = current.BaseType)
        {
            foreach (var property in current.GetMembers(propertyName).OfType<RoslynPropertySymbol>())
            {
                if (!property.IsStatic &&
                    property.DeclaredAccessibility == Accessibility.Public)
                {
                    return property;
                }
            }
        }

        foreach (var @interface in componentType.AllInterfaces)
        {
            foreach (var property in @interface.GetMembers(propertyName).OfType<RoslynPropertySymbol>())
            {
                if (!property.IsStatic &&
                    property.DeclaredAccessibility == Accessibility.Public)
                {
                    return property;
                }
            }
        }

        return null;
    }

    private RoslynFieldSymbol? FindAvaloniaPropertyField(
        INamedTypeSymbol componentType,
        string propertyName)
    {
        var avaloniaPropertyName = propertyName + "Property";
        for (var current = componentType; current != null; current = current.BaseType)
        {
            foreach (var field in current.GetMembers(avaloniaPropertyName).OfType<RoslynFieldSymbol>())
            {
                if (field.IsStatic &&
                    field.DeclaredAccessibility == Accessibility.Public &&
                    IsAvaloniaPropertyType(field.Type))
                {
                    return field;
                }
            }
        }

        return null;
    }

    private bool IsAvaloniaPropertyType(ITypeSymbol type)
    {
        return TryGetAvaloniaPropertyType(out var avaloniaPropertyType) &&
            IsAssignableTo(type, avaloniaPropertyType);
    }

    private bool TryGetAvaloniaPropertyType(out INamedTypeSymbol avaloniaPropertyType)
    {
        avaloniaPropertyType = Compilation.CSharpCompilation.GetTypeByMetadataName("Avalonia.AvaloniaProperty")!;
        return avaloniaPropertyType != null;
    }

    private static CSharpSymbolDefinition GetMarkupPropertyType(
        IParamSymbol? parameter,
        ICommandSymbol? command,
        RoslynPropertySymbol? clrProperty,
        RoslynFieldSymbol? avaloniaProperty)
    {
        if (parameter != null)
        {
            return parameter.Type;
        }

        if (command != null)
        {
            return command.ReturnType;
        }

        if (clrProperty?.Type is { TypeKind: not TypeKind.Error } clrPropertyType)
        {
            return new CSharpSymbolDefinition(clrPropertyType);
        }

        if (avaloniaProperty != null &&
            TryGetAvaloniaPropertyValueType(avaloniaProperty.Type, out var avaloniaPropertyType))
        {
            return new CSharpSymbolDefinition(avaloniaPropertyType);
        }

        return default;
    }

    private static bool TryGetAvaloniaPropertyValueType(
        ITypeSymbol propertyType,
        out ITypeSymbol valueType)
    {
        for (var current = propertyType as INamedTypeSymbol; current != null; current = current.BaseType)
        {
            if (current.ContainingNamespace.ToDisplayString() != "Avalonia")
            {
                continue;
            }

            if (current.Name is "StyledProperty" or "AttachedProperty" or "AvaloniaProperty" &&
                current.TypeArguments.Length == 1 &&
                current.TypeArguments[0].TypeKind != TypeKind.Error)
            {
                valueType = current.TypeArguments[0];
                return true;
            }

            if (current.Name == "DirectProperty" &&
                current.TypeArguments.Length == 2 &&
                current.TypeArguments[1].TypeKind != TypeKind.Error)
            {
                valueType = current.TypeArguments[1];
                return true;
            }
        }

        valueType = null!;
        return false;
    }

    private MarkupContentModel CreateMarkupContentModel(INamedTypeSymbol componentType)
    {
        if (TryGetAvaloniaControlType(out var controlType) &&
            IsAssignableTo(componentType, controlType))
        {
            var contentProperty = FindAvaloniaContentProperty(componentType);
            if (contentProperty == null)
            {
                return default;
            }

            var contentType = contentProperty.Type;
            if (TryGetIListElementType(contentType, out var itemType))
            {
                return new MarkupContentModel(
                    new CSharpSymbolDefinition(contentProperty),
                    new CSharpSymbolDefinition(itemType),
                    isCollection: true,
                    allowsText: AllowsTextContent(itemType));
            }

            return new MarkupContentModel(
                new CSharpSymbolDefinition(contentProperty),
                new CSharpSymbolDefinition(contentType),
                isCollection: false,
                allowsText: AllowsTextContent(contentType));
        }

        if (TryGetIListElementType(componentType, out var elementType))
        {
            return new MarkupContentModel(
                contentProperty: default,
                allowedChildType: new CSharpSymbolDefinition(elementType),
                isCollection: true,
                allowsText: AllowsTextContent(elementType));
        }

        return default;
    }

    private ImmutableArray<MarkupChildContent> CreateMarkupChildren(
        MarkupElementSyntax markupElement,
        MarkupContentModel contentModel,
        out ImmutableArray<AkburaSemanticDiagnostic> diagnostics)
    {
        using var childrenBuilder = ImmutableArrayBuilder<MarkupChildContent>.Rent();
        using var diagnosticsBuilder = ImmutableArrayBuilder<AkburaSemanticDiagnostic>.Rent();

        foreach (var childSyntax in markupElement.Body)
        {
            switch (childSyntax)
            {
                case MarkupElementContentSyntax elementContent:
                    AddElementChild(elementContent, contentModel, childrenBuilder, diagnosticsBuilder);
                    break;

                case MarkupTextLiteralSyntax textLiteral:
                    AddTextChild(textLiteral, contentModel, childrenBuilder, diagnosticsBuilder);
                    break;

                case MarkupInlineExpressionSyntax inlineExpression:
                    AddExpressionChild(inlineExpression, contentModel, childrenBuilder, diagnosticsBuilder);
                    break;
            }
        }

        diagnostics = diagnosticsBuilder.ToImmutable();
        return childrenBuilder.ToImmutable();
    }

    private void AddElementChild(
        MarkupElementContentSyntax elementContent,
        MarkupContentModel contentModel,
        ImmutableArrayBuilder<MarkupChildContent> childrenBuilder,
        ImmutableArrayBuilder<AkburaSemanticDiagnostic> diagnosticsBuilder)
    {
        var symbolInfo = GetSymbolInfo(elementContent.Element);
        var componentSymbol = symbolInfo.Symbol as IMarkupComponentSymbol;
        var childType = componentSymbol?.CSharpDefinition ?? default;

        childrenBuilder.Add(new MarkupChildContent(
            elementContent,
            MarkupChildKind.Element,
            childType,
            componentSymbol));

        if (componentSymbol?.ComponentType == null)
        {
            return;
        }

        if (!IsAllowedMarkupChildType(componentSymbol.ComponentType, contentModel))
        {
            diagnosticsBuilder.Add(CreateInvalidMarkupChildDiagnostic(
                elementContent,
                componentSymbol.CSharpDefinition,
                contentModel));
        }
    }

    private void AddTextChild(
        MarkupTextLiteralSyntax textLiteral,
        MarkupContentModel contentModel,
        ImmutableArrayBuilder<MarkupChildContent> childrenBuilder,
        ImmutableArrayBuilder<AkburaSemanticDiagnostic> diagnosticsBuilder)
    {
        var text = textLiteral.ToFullString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var stringType = Compilation.CSharpCompilation.GetSpecialType(SpecialType.System_String);
        var textType = new CSharpSymbolDefinition(stringType);
        childrenBuilder.Add(new MarkupChildContent(
            textLiteral,
            MarkupChildKind.Text,
            textType,
            text: text));

        if (!contentModel.AllowsText)
        {
            diagnosticsBuilder.Add(CreateInvalidMarkupChildDiagnostic(
                textLiteral,
                textType,
                contentModel));
        }
    }

    private void AddExpressionChild(
        MarkupInlineExpressionSyntax inlineExpression,
        MarkupContentModel contentModel,
        ImmutableArrayBuilder<MarkupChildContent> childrenBuilder,
        ImmutableArrayBuilder<AkburaSemanticDiagnostic> diagnosticsBuilder)
    {
        childrenBuilder.Add(new MarkupChildContent(
            inlineExpression,
            MarkupChildKind.Expression,
            type: default));

        if (!contentModel.AllowsChildren)
        {
            diagnosticsBuilder.Add(CreateInvalidMarkupChildDiagnostic(
                inlineExpression,
                childType: default,
                contentModel));
        }
    }

    private AkburaSemanticDiagnostic CreateInvalidMarkupChildDiagnostic(
        MarkupContentSyntax syntax,
        CSharpSymbolDefinition childType,
        MarkupContentModel contentModel)
    {
        var childTypeText = childType.IsDefault
            ? "expression"
            : childType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var expectedTypeText = contentModel.AllowedChildType.IsDefault
            ? "no children"
            : contentModel.AllowedChildType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        return new AkburaSemanticDiagnostic(
            syntax,
            ErrorCodes.AKBURA_SEMANTIC_InvalidMarkupChild,
            [childTypeText, expectedTypeText]);
    }

    private bool IsAllowedMarkupChildType(ITypeSymbol childType, MarkupContentModel contentModel)
    {
        return contentModel.AllowedChildType.Symbol is ITypeSymbol allowedType &&
            IsAssignableTo(childType, allowedType);
    }

    private bool AllowsTextContent(ITypeSymbol type)
    {
        return type.SpecialType is SpecialType.System_Object or SpecialType.System_String;
    }

    private RoslynPropertySymbol? FindAvaloniaContentProperty(INamedTypeSymbol type)
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            foreach (var property in current.GetMembers().OfType<RoslynPropertySymbol>())
            {
                if (property.IsStatic ||
                    property.DeclaredAccessibility != Accessibility.Public)
                {
                    continue;
                }

                if (HasAvaloniaContentAttribute(property))
                {
                    return property;
                }
            }
        }

        return null;
    }

    private bool HasAvaloniaContentAttribute(RoslynPropertySymbol property)
    {
        foreach (var attribute in property.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
                "global::Avalonia.Metadata.ContentAttribute")
            {
                return true;
            }
        }

        return false;
    }

    private bool TryGetAvaloniaControlType(out INamedTypeSymbol controlType)
    {
        controlType = Compilation.CSharpCompilation.GetTypeByMetadataName("Avalonia.Controls.Control")!;
        return controlType != null;
    }

    private bool TryGetIListElementType(ITypeSymbol type, out ITypeSymbol elementType)
    {
        if (type is INamedTypeSymbol namedType &&
            IsIListOfT(namedType))
        {
            elementType = namedType.TypeArguments[0];
            return true;
        }

        foreach (var @interface in type.AllInterfaces)
        {
            if (IsIListOfT(@interface))
            {
                elementType = @interface.TypeArguments[0];
                return true;
            }
        }

        elementType = null!;
        return false;
    }

    private static bool IsIListOfT(INamedTypeSymbol type)
    {
        var original = type.OriginalDefinition;
        return original.Name == "IList" &&
            original.Arity == 1 &&
            original.ContainingNamespace.ToDisplayString() == "System.Collections.Generic";
    }

    private static bool IsAssignableTo(ITypeSymbol source, ITypeSymbol target)
    {
        if (target.SpecialType == SpecialType.System_Object ||
            IsSameType(source, target))
        {
            return true;
        }

        if (source is INamedTypeSymbol namedSource &&
            target is INamedTypeSymbol namedTarget)
        {
            for (var current = namedSource.BaseType; current != null; current = current.BaseType)
            {
                if (IsSameType(current, namedTarget))
                {
                    return true;
                }
            }
        }

        foreach (var @interface in source.AllInterfaces)
        {
            if (IsSameType(@interface, target))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSameType(ITypeSymbol left, ITypeSymbol right)
    {
        return SymbolEqualityComparer.Default.Equals(left, right) ||
            left.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
            right.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }

    private CSharpTypeBinding BindCSharpType(CSharp.TypeSyntax typeSyntax)
    {
        var field = CSharpSyntaxFactory.FieldDeclaration(
            CSharpSyntaxFactory.VariableDeclaration(typeSyntax)
                .WithVariables(CSharpSyntaxFactory.SingletonSeparatedList(
                    CSharpSyntaxFactory.VariableDeclarator("__akbura_value"))));

        var probeClass = CSharpSyntaxFactory.ClassDeclaration("__AkburaSemanticProbe")
            .WithMembers(CSharpSyntaxFactory.SingletonList<CSharp.MemberDeclarationSyntax>(field));

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
        var probeType = syntaxTree
            .GetCompilationUnitRoot()
            .DescendantNodes()
            .OfType<CSharp.FieldDeclarationSyntax>()
            .Single()
            .Declaration
            .Type;

        var typeInfo = semanticModel.GetTypeInfo(probeType);
        var symbolInfo = semanticModel.GetSymbolInfo(probeType);
        var typeSymbol = typeInfo.Type?.TypeKind == TypeKind.Error
            ? null
            : typeInfo.Type;

        return new CSharpTypeBinding(
            typeSymbol,
            symbolInfo.Symbol,
            receiverType: null,
            isBindingPath: true,
            symbolInfo.CandidateSymbols,
            symbolInfo.CandidateReason == Microsoft.CodeAnalysis.CandidateReason.Ambiguous
                ? AkburaCandidateReason.Ambiguous
                : AkburaCandidateReason.NotFound,
            operationDefinition: default);
    }

    private CSharpTypeBinding BindCSharpExpression(
        CSharp.ExpressionSyntax expressionSyntax,
        StateDeclarationSyntax? scopeStateDeclaration = null,
        bool isBindingPath = true)
    {
        var returnStatement = CSharpSyntaxFactory.ReturnStatement(expressionSyntax);
        var method = CSharpSyntaxFactory.MethodDeclaration(
                CSharpSyntaxFactory.PredefinedType(CSharpSyntaxFactory.Token(Microsoft.CodeAnalysis.CSharp.SyntaxKind.ObjectKeyword)),
                "__AkburaSemanticProbe")
            .WithBody(CSharpSyntaxFactory.Block(returnStatement));

        using var membersBuilder = ImmutableArrayBuilder<CSharp.MemberDeclarationSyntax>.Rent();
        if (scopeStateDeclaration != null)
        {
            foreach (var field in CreateStateProbeFieldsBefore(scopeStateDeclaration))
            {
                membersBuilder.Add(field);
            }
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
            isBindingPath,
            symbolInfo.CandidateSymbols,
            symbolInfo.CandidateReason == Microsoft.CodeAnalysis.CandidateReason.Ambiguous
                ? AkburaCandidateReason.Ambiguous
                : AkburaCandidateReason.NotFound,
            operation == null ? default : new CSharpOperationDefinition(operation));
    }

    private CSharpTypeBinding BindAkcssExpression(
        CSharp.ExpressionSyntax expressionSyntax,
        IAkcssSymbol containingSymbol)
    {
        var returnStatement = CSharpSyntaxFactory.ReturnStatement(expressionSyntax);
        var method = CSharpSyntaxFactory.MethodDeclaration(
                CSharpSyntaxFactory.PredefinedType(CSharpSyntaxFactory.Token(Microsoft.CodeAnalysis.CSharp.SyntaxKind.ObjectKeyword)),
                "__AkburaSemanticProbe")
            .WithParameterList(CreateAkcssExpressionParameterList(containingSymbol))
            .WithBody(CSharpSyntaxFactory.Block(returnStatement));

        var probeClass = CSharpSyntaxFactory.ClassDeclaration("__AkburaSemanticProbe")
            .WithMembers(CSharpSyntaxFactory.SingletonList<CSharp.MemberDeclarationSyntax>(method));

        var compilationUnit = CSharpSyntaxFactory.CompilationUnit()
            .WithExterns(CSharpSyntaxFactory.List(GetCSharpExternAliases()))
            .WithUsings(CSharpSyntaxFactory.List(GetAkcssCSharpUsingDirectives()));

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

    private CSharp.ParameterListSyntax CreateAkcssExpressionParameterList(IAkcssSymbol containingSymbol)
    {
        if (containingSymbol is not ITailwindUtilitySymbol utilitySymbol ||
            utilitySymbol.Parameters.Length == 0)
        {
            return CSharpSyntaxFactory.ParameterList();
        }

        using var builder = ImmutableArrayBuilder<CSharp.ParameterSyntax>.Rent();
        foreach (var parameter in utilitySymbol.Parameters)
        {
            var parameterType = parameter.Type.Symbol == null
                ? CSharpSyntaxFactory.PredefinedType(CSharpSyntaxFactory.Token(Microsoft.CodeAnalysis.CSharp.SyntaxKind.ObjectKeyword))
                : CSharpSyntaxFactory.ParseTypeName(
                    parameter.Type.Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));

            builder.Add(CSharpSyntaxFactory.Parameter(
                    CSharpSyntaxFactory.Identifier(parameter.Name))
                .WithType(parameterType));
        }

        return CSharpSyntaxFactory.ParameterList(
            CSharpSyntaxFactory.SeparatedList(builder.ToImmutable()));
    }

    private ImmutableArray<CSharp.MemberDeclarationSyntax> CreateStateProbeFieldsBefore(
        StateDeclarationSyntax scopeStateDeclaration)
    {
        using var builder = ImmutableArrayBuilder<CSharp.MemberDeclarationSyntax>.Rent();
        foreach (var member in SyntaxTree.GetRoot().Members)
        {
            if (member.Position >= scopeStateDeclaration.Position)
            {
                break;
            }

            if (member is StateDeclarationSyntax stateDeclaration &&
                TryCreateStateProbeField(stateDeclaration, out var field))
            {
                builder.Add(field);
            }
        }

        return builder.ToImmutable();
    }

    private bool TryCreateStateProbeField(
        StateDeclarationSyntax stateDeclaration,
        out CSharp.FieldDeclarationSyntax field)
    {
        field = null!;

        var name = stateDeclaration.Name.ToFullString().Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var type = GetStateProbeFieldType(stateDeclaration);
        if (type == null)
        {
            return false;
        }

        field = CSharpSyntaxFactory.FieldDeclaration(
                CSharpSyntaxFactory.VariableDeclaration(type)
                    .WithVariables(CSharpSyntaxFactory.SingletonSeparatedList(
                        CSharpSyntaxFactory.VariableDeclarator(
                            CSharpSyntaxFactory.Identifier(name)))))
            .WithModifiers(CSharpSyntaxFactory.TokenList(
                CSharpSyntaxFactory.Token(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PrivateKeyword)));

        return true;
    }

    private CSharp.TypeSyntax? GetStateProbeFieldType(StateDeclarationSyntax stateDeclaration)
    {
        if (stateDeclaration.Type != null)
        {
            try
            {
                return stateDeclaration.Type.ToCSharp();
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        if (GetSymbolInfo(stateDeclaration).Symbol is not IStateSymbol stateSymbol ||
            stateSymbol.Type.Symbol is not ITypeSymbol typeSymbol)
        {
            return null;
        }

        return CSharpSyntaxFactory.ParseTypeName(
            typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
    }

    private static ITypeSymbol? GetExpressionReceiverType(
        SemanticModel semanticModel,
        CSharp.ExpressionSyntax expression)
    {
        return expression switch
        {
            CSharp.MemberAccessExpressionSyntax memberAccess =>
                semanticModel.GetTypeInfo(memberAccess.Expression).Type,
            CSharp.ConditionalAccessExpressionSyntax conditionalAccess =>
                semanticModel.GetTypeInfo(conditionalAccess.Expression).Type,
            _ => null,
        };
    }

    private ImmutableArray<CSharp.UsingDirectiveSyntax> GetCSharpUsingDirectives()
    {
        using var builder = ImmutableArrayBuilder<CSharp.UsingDirectiveSyntax>.Rent();
        foreach (var member in SyntaxTree.GetRoot().Members)
        {
            if (member is UsingDirectiveSyntax usingDirective)
            {
                builder.Add(usingDirective.ToCSharp());
            }
        }

        return builder.ToImmutable();
    }

    private ImmutableArray<CSharp.UsingDirectiveSyntax> GetAkcssCSharpUsingDirectives()
    {
        using var builder = ImmutableArrayBuilder<CSharp.UsingDirectiveSyntax>.Rent();
        foreach (var usingDirective in GetCSharpUsingDirectives())
        {
            builder.Add(usingDirective);
        }

        AddAkcssImplicitUsing(builder, "Avalonia");
        AddAkcssImplicitUsing(builder, "Avalonia.Media");
        AddAkcssImplicitUsing(builder, "Akbura");
        return builder.ToImmutable();
    }

    private static void AddAkcssImplicitUsing(
        ImmutableArrayBuilder<CSharp.UsingDirectiveSyntax> builder,
        string namespaceName)
    {
        builder.Add(CSharpSyntaxFactory.UsingDirective(
            CSharpSyntaxFactory.ParseName(namespaceName)));
    }

    private ImmutableArray<CSharp.ExternAliasDirectiveSyntax> GetCSharpExternAliases()
    {
        using var builder = ImmutableArrayBuilder<CSharp.ExternAliasDirectiveSyntax>.Rent();
        var seenAliases = new HashSet<string>(StringComparer.Ordinal);
        foreach (var reference in Compilation.CSharpCompilation.References)
        {
            foreach (var alias in reference.Properties.Aliases)
            {
                if (string.IsNullOrWhiteSpace(alias) ||
                    alias == "global" ||
                    !seenAliases.Add(alias))
                {
                    continue;
                }

                builder.Add(CSharpSyntaxFactory.ExternAliasDirective(
                    CSharpSyntaxFactory.Identifier(alias)));
            }
        }

        return builder.ToImmutable();
    }

    private CSharp.FileScopedNamespaceDeclarationSyntax? GetCSharpNamespaceDeclaration()
    {
        foreach (var member in SyntaxTree.GetRoot().Members)
        {
            if (member is NamespaceDeclarationSyntax namespaceDeclaration)
            {
                return namespaceDeclaration.ToCSharp();
            }
        }

        return null;
    }

    private static ImmutableArray<AkburaSymbol> CreateMarkupComponentCandidates(
        string componentName,
        ImmutableArray<RoslynSymbol> csharpCandidates)
    {
        if (csharpCandidates.IsDefaultOrEmpty)
        {
            return ImmutableArray<AkburaSymbol>.Empty;
        }

        using var builder = ImmutableArrayBuilder<AkburaSymbol>.Rent();
        var seenSymbols = new HashSet<RoslynSymbol>(SymbolEqualityComparer.Default);
        foreach (var candidate in csharpCandidates)
        {
            if (candidate is not INamedTypeSymbol namedType ||
                namedType.TypeKind == TypeKind.Error ||
                !seenSymbols.Add(namedType))
            {
                continue;
            }

            builder.Add(new MarkupComponentSymbol(
                componentName,
                new CSharpSymbolDefinition(namedType)));
        }

        return builder.ToImmutable();
    }

    private void ValidateSyntaxTreeOwnership(AkburaSyntax syntax)
    {
        if (!ReferenceEquals(syntax.Root, SyntaxTree.GetRoot()))
        {
            throw new ArgumentException("Syntax node is not part of this semantic model syntax tree.", nameof(syntax));
        }
    }

    private void SetSemanticDiagnostics(
        AkburaSyntax syntax,
        ImmutableArray<AkburaSemanticDiagnostic> diagnostics)
    {
        _semanticDiagnosticsCache[syntax] = diagnostics.IsDefault
            ? ImmutableArray<AkburaSemanticDiagnostic>.Empty
            : diagnostics;
    }

    private readonly struct CSharpTypeBinding
    {
        public static CSharpTypeBinding Empty { get; } = new(
            typeSymbol: null,
            symbol: null,
            receiverType: null,
            isBindingPath: false,
            candidateSymbols: ImmutableArray<RoslynSymbol>.Empty,
            candidateReason: AkburaCandidateReason.NotFound,
            operationDefinition: default);

        public CSharpTypeBinding(
            ITypeSymbol? typeSymbol,
            RoslynSymbol? symbol,
            ITypeSymbol? receiverType,
            bool isBindingPath,
            ImmutableArray<RoslynSymbol> candidateSymbols,
            AkburaCandidateReason candidateReason,
            CSharpOperationDefinition operationDefinition)
        {
            TypeSymbol = typeSymbol;
            Symbol = symbol;
            ReceiverType = receiverType;
            IsBindingPath = isBindingPath;
            CandidateSymbols = candidateSymbols.IsDefault
                ? ImmutableArray<RoslynSymbol>.Empty
                : candidateSymbols;
            CandidateReason = candidateReason;
            OperationDefinition = operationDefinition;
        }

        public ITypeSymbol? TypeSymbol { get; }

        public RoslynSymbol? Symbol { get; }

        public ITypeSymbol? ReceiverType { get; }

        public bool IsBindingPath { get; }

        public ImmutableArray<RoslynSymbol> CandidateSymbols { get; }

        public AkburaCandidateReason CandidateReason { get; }

        public CSharpOperationDefinition OperationDefinition { get; }
    }
}
