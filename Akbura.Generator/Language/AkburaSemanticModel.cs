using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
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
using RoslynPropertySymbol = Microsoft.CodeAnalysis.IPropertySymbol;
using RoslynSymbol = Microsoft.CodeAnalysis.ISymbol;

namespace Akbura.Language;

internal sealed class AkburaSemanticModel
{
    private readonly Dictionary<AkburaSyntax, AkburaSymbolInfo> _symbolInfoCache = new();
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
                    CreateAkburaComponentParameters(syntaxTree));

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
        RoslynPropertySymbol? clrProperty = null;
        RoslynFieldSymbol? avaloniaProperty = null;

        if (componentType != null)
        {
            clrProperty = FindPublicClrProperty(componentType, propertyName);
            avaloniaProperty = FindAvaloniaPropertyField(componentType, propertyName);
        }

        if (parameter == null &&
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
            GetMarkupPropertyType(parameter, clrProperty, avaloniaProperty),
            avaloniaProperty == null ? default : new CSharpSymbolDefinition(avaloniaProperty),
            clrProperty == null ? default : new CSharpSymbolDefinition(clrProperty),
            parameter,
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
        RoslynPropertySymbol? clrProperty,
        RoslynFieldSymbol? avaloniaProperty)
    {
        if (parameter != null)
        {
            return parameter.Type;
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
                : AkburaCandidateReason.NotFound);
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
                : AkburaCandidateReason.NotFound);
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
            candidateReason: AkburaCandidateReason.NotFound);

        public CSharpTypeBinding(
            ITypeSymbol? typeSymbol,
            RoslynSymbol? symbol,
            ITypeSymbol? receiverType,
            bool isBindingPath,
            ImmutableArray<RoslynSymbol> candidateSymbols,
            AkburaCandidateReason candidateReason)
        {
            TypeSymbol = typeSymbol;
            Symbol = symbol;
            ReceiverType = receiverType;
            IsBindingPath = isBindingPath;
            CandidateSymbols = candidateSymbols.IsDefault
                ? ImmutableArray<RoslynSymbol>.Empty
                : candidateSymbols;
            CandidateReason = candidateReason;
        }

        public ITypeSymbol? TypeSymbol { get; }

        public RoslynSymbol? Symbol { get; }

        public ITypeSymbol? ReceiverType { get; }

        public bool IsBindingPath { get; }

        public ImmutableArray<RoslynSymbol> CandidateSymbols { get; }

        public AkburaCandidateReason CandidateReason { get; }
    }
}
