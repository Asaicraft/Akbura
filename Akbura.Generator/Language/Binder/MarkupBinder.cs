using Akbura.Language.BoundTree;
using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Threading;
using AkburaSyntaxKind = Akbura.Language.Syntax.SyntaxKind;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;
using CSharpTypeSymbol = Microsoft.CodeAnalysis.ITypeSymbol;
using CSharpSyntaxKind = Microsoft.CodeAnalysis.CSharp.SyntaxKind;
using CSharpSyntaxFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Akbura.Language.Binder;

internal sealed partial class MarkupBinder : Binder
{
    private IMarkupItemSymbol? _lazyItemSymbol;
    private int _itemSymbolInitialized;

    public MarkupBinder(
        AkburaSemanticModel semanticModel,
        Binder next,
        Declaration declaration,
        AkburaBinderFlags flags = AkburaBinderFlags.None)
        : base(
            semanticModel,
            next,
            declaration,
            DeclarationFacts.GetSyntax(declaration),
            flags | AkburaBinderFlags.InMarkup)
    {
    }

    public IMarkupComponentSymbol? TargetComponentSymbol
    {
        get
        {
            return Declaration != null ? DeclarationFacts.GetSyntax(Declaration) switch
            {
                MarkupRootSyntax markupRoot => SemanticModel.GetSymbolInfo(markupRoot.Element).Symbol as IMarkupComponentSymbol,
                MarkupElementSyntax markupElement => SemanticModel.GetSymbolInfo(markupElement).Symbol as IMarkupComponentSymbol,
                _ => null,
            } : null;
        }
    }

    public override ImmutableArray<ISymbol> GetDeclaredSymbolsForScope(AkburaSyntax scopeDesignator)
    {
        if (!OwnsScope(scopeDesignator))
        {
            return base.GetDeclaredSymbolsForScope(scopeDesignator);
        }

        var itemSymbol = GetDeclaredItemSymbol();
        var nameSymbols = GetDeclaredNameSymbols();
        if (itemSymbol == null)
        {
            return nameSymbols.IsEmpty
                ? base.GetDeclaredSymbolsForScope(scopeDesignator)
                : nameSymbols;
        }

        if (nameSymbols.IsEmpty)
        {
            return ImmutableArray.Create<ISymbol>(itemSymbol);
        }

        using var builder = ImmutableArrayBuilder<ISymbol>.Rent(nameSymbols.Length + 1);
        builder.Add(itemSymbol);
        foreach (var nameSymbol in nameSymbols)
        {
            builder.Add(nameSymbol);
        }

        return builder.ToImmutable();
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
        var itemSymbol = GetDeclaredItemSymbol();
        if (itemSymbol != null &&
            string.Equals(itemSymbol.Name, name, System.StringComparison.Ordinal))
        {
            result.SetSymbol(itemSymbol);
        }

        if (!result.IsComplete)
        {
            var nameSymbol = FindDeclaredSymbol(GetDeclaredNameSymbols(), name);
            if (nameSymbol != null)
            {
                result.SetSymbol(nameSymbol);
            }
        }
    }

    internal IMarkupItemSymbol? GetDeclaredItemSymbol()
    {
        if (Volatile.Read(ref _itemSymbolInitialized) != 0)
        {
            return _lazyItemSymbol;
        }

        IMarkupItemSymbol? itemSymbol = null;
        if (ScopeDesignator?.Kind == AkburaSyntaxKind.MarkupElementSyntax)
        {
            var scope = Unsafe.As<MarkupElementSyntax>(ScopeDesignator);
            if (!MarkupDataTypeResolver.HasItemNameDirective(scope))
            {
                Volatile.Write(ref _itemSymbolInitialized, 1);
                return null;
            }

            SemanticModel.BindingSession.MarkupDataTypes.TryCreateItemSymbol(scope, out itemSymbol);
        }

        if (itemSymbol != null)
        {
            Interlocked.CompareExchange(ref _lazyItemSymbol, itemSymbol, comparand: null);
            Volatile.Write(ref _itemSymbolInitialized, 1);
        }

        return _lazyItemSymbol;
    }

    internal ImmutableArray<ISymbol> GetDeclaredNameSymbols()
    {
        var scope = GetNameScope();
        return scope?.GetDeclaredSymbols(SemanticModel) ?? ImmutableArray<ISymbol>.Empty;
    }

    internal bool TryGetDeclaredNameDeclaration(
        MarkupAttachedPropertyAttributeSyntax attribute,
        out MarkupNameDeclaration declaration)
    {
        var scope = GetNameScope();
        if (scope != null)
        {
            return scope.TryGetDeclaration(attribute, out declaration);
        }

        declaration = null!;
        return false;
    }

    private MarkupNameScope? GetNameScope()
    {
        if (ScopeDesignator?.Kind != AkburaSyntaxKind.MarkupRootSyntax)
        {
            return null;
        }

        return SemanticModel.BindingSession.GetMarkupNameScope(
            Unsafe.As<MarkupRootSyntax>(ScopeDesignator));
    }

    public override BoundNode BindOperationSyntax(AkburaSyntax syntax)
    {
        return syntax.Kind switch
        {
            AkburaSyntaxKind.MarkupElementSyntax =>
                BindMarkupComponent(Unsafe.As<MarkupElementSyntax>(syntax)),
            AkburaSyntaxKind.MarkupPlainAttributeSyntax or
                AkburaSyntaxKind.MarkupAttachedPropertyAttributeSyntax or
                AkburaSyntaxKind.MarkupPrefixedAttributeSyntax or
                AkburaSyntaxKind.TailwindFlagAttributeSyntax or
                AkburaSyntaxKind.TailwindFullAttributeSyntax =>
                BindMarkupAttribute(Unsafe.As<MarkupAttributeSyntax>(syntax)),
            _ => base.BindOperationSyntax(syntax),
        };
    }

    public override BoundNode BindSemanticSyntax(AkburaSyntax syntax)
    {
        return syntax.Kind switch
        {
            AkburaSyntaxKind.MarkupRootSyntax or
                AkburaSyntaxKind.MarkupElementSyntax or
                AkburaSyntaxKind.MarkupElementContentSyntax or
            AkburaSyntaxKind.MarkupInlineExpressionSyntax or
                AkburaSyntaxKind.MarkupTextLiteralSyntax =>
                BindMarkupSemanticSyntax(syntax),
            AkburaSyntaxKind.MarkupPlainAttributeSyntax or
                AkburaSyntaxKind.MarkupAttachedPropertyAttributeSyntax or
                AkburaSyntaxKind.MarkupPrefixedAttributeSyntax or
                AkburaSyntaxKind.TailwindFlagAttributeSyntax or
                AkburaSyntaxKind.TailwindFullAttributeSyntax =>
                BindOperationSyntax(syntax),
            _ => base.BindSemanticSyntax(syntax),
        };
    }

    private BoundNode BindMarkupSemanticSyntax(AkburaSyntax syntax)
    {
        return syntax.Kind switch
        {
            AkburaSyntaxKind.MarkupRootSyntax =>
                BindMarkupRoot(Unsafe.As<MarkupRootSyntax>(syntax)),
            AkburaSyntaxKind.MarkupElementSyntax =>
                BindMarkupComponent(Unsafe.As<MarkupElementSyntax>(syntax)),
            AkburaSyntaxKind.MarkupElementContentSyntax or
                AkburaSyntaxKind.MarkupInlineExpressionSyntax or
                AkburaSyntaxKind.MarkupTextLiteralSyntax =>
                BindMarkupContent(Unsafe.As<MarkupContentSyntax>(syntax)),
            _ => new BoundDeclaration(
                syntax,
                this,
                AkburaSymbolInfo.None(CandidateReason.UnsupportedSyntax)),
        };
    }

    private BoundMarkupRoot BindMarkupRoot(MarkupRootSyntax markupRoot)
    {
        var element = SemanticModel.BindingSession.BindSemanticSyntax(markupRoot.Element);
        var symbolInfo = element.SymbolInfo;
        var boundRoot = new BoundMarkupRoot(
            markupRoot,
            this,
            symbolInfo,
            SemanticModel.GetCachedSemanticDiagnostics(markupRoot),
            ImmutableArray.Create(element));
        SemanticModel.SetCachedBoundNode(markupRoot, boundRoot);
        return boundRoot;
    }

    private BoundMarkupComponent BindMarkupComponent(MarkupElementSyntax markupElement)
    {
        var symbolInfo = SemanticModel.GetSymbolInfo(markupElement);
        var componentSymbol = symbolInfo.Symbol as IMarkupComponentSymbol;
        var propertySymbol = symbolInfo.Symbol as IPropertySymbol;
        using var childrenBuilder = ImmutableArrayBuilder<BoundNode>.Rent();

        if (markupElement.StartTag != null)
        {
            foreach (var attribute in markupElement.StartTag.Attributes)
            {
                childrenBuilder.Add(SemanticModel.BindingSession.BindOperationSyntax(attribute));
            }
        }

        foreach (var content in markupElement.Body)
        {
            childrenBuilder.Add(SemanticModel.BindingSession.BindSemanticSyntax(content));
        }

        var contentSetter = propertySymbol == null
            ? BindMarkupContentSetter(markupElement, componentSymbol)
            : BindMarkupPropertyElementSetter(markupElement, propertySymbol);
        if (contentSetter != null)
        {
            childrenBuilder.Add(contentSetter);
        }

        var diagnostics = AddRequiredComponentParameterDiagnostics(markupElement, componentSymbol);
        var boundComponent = new BoundMarkupComponent(
            markupElement,
            this,
            symbolInfo,
            diagnostics,
            childrenBuilder.ToImmutable());
        SemanticModel.SetCachedBoundNode(markupElement, boundComponent);
        return boundComponent;
    }

    private ImmutableArray<AkburaSemanticDiagnostic> AddRequiredComponentParameterDiagnostics(
        MarkupElementSyntax markupElement,
        IMarkupComponentSymbol? componentSymbol)
    {
        var diagnostics = SemanticModel.GetCachedSemanticDiagnostics(markupElement);
        var component = componentSymbol?.AkburaComponent;
        if (component == null || component.Parameters.IsDefaultOrEmpty)
        {
            return diagnostics;
        }

        using var diagnosticsBuilder = ImmutableArrayBuilder<AkburaSemanticDiagnostic>.Rent(
            diagnostics.Length + component.Parameters.Length);
        diagnosticsBuilder.AddRange(diagnostics);
        var initialCount = diagnosticsBuilder.Count;

        foreach (var parameter in component.Parameters)
        {
            if (!parameter.ReceivesValueFromParent ||
                parameter.HasDefaultValue ||
                IsComponentParameterSet(markupElement, parameter))
            {
                continue;
            }

            AkburaSyntax diagnosticSyntax = markupElement.StartTag == null
                ? markupElement
                : markupElement.StartTag.Name;
            diagnosticsBuilder.Add(new AkburaSemanticDiagnostic(
                diagnosticSyntax,
                ErrorCodes.AKBURA_SEMANTIC_MarkupRequiredParameterNotSet,
                [parameter.Name, component.MetadataName]));
        }

        if (diagnosticsBuilder.Count == initialCount)
        {
            return diagnostics;
        }

        diagnostics = diagnosticsBuilder.ToImmutable();
        SemanticModel.SetSemanticDiagnostics(markupElement, diagnostics);
        return diagnostics;
    }

    private bool IsComponentParameterSet(
        MarkupElementSyntax markupElement,
        IParamSymbol parameter)
    {
        if (markupElement.StartTag == null)
        {
            return false;
        }

        foreach (var attribute in markupElement.StartTag.Attributes)
        {
            if (SemanticModel.GetSymbolInfo(attribute).Symbol is IPropertySymbol
                {
                    Parameter: { } setParameter
                } &&
                ReferenceEquals(setParameter, parameter))
            {
                return true;
            }
        }

        foreach (var content in markupElement.Body)
        {
            if (content is MarkupElementContentSyntax elementContent &&
                SemanticModel.GetSymbolInfo(elementContent.Element).Symbol is IPropertySymbol
                {
                    Parameter: { } setParameter
                } &&
                ReferenceEquals(setParameter, parameter))
            {
                return true;
            }
        }

        var component =
            SemanticModel.GetSymbolInfo(markupElement).Symbol as IMarkupComponentSymbol;
        var componentContentParameter = component?.ContentModel.ContentParameter;
        if (componentContentParameter == null ||
            !string.Equals(
                componentContentParameter.Name,
                parameter.Name,
                StringComparison.Ordinal))
        {
            return false;
        }

        if (component?.ContentModel.IsCollection == true)
        {
            return true;
        }

        foreach (var content in markupElement.Body)
        {
            switch (content)
            {
                case MarkupElementContentSyntax elementContent
                    when SemanticModel.GetSymbolInfo(elementContent.Element).Symbol is not IPropertySymbol:
                case MarkupInlineExpressionSyntax:
                    return true;
                case MarkupTextLiteralSyntax text
                    when !string.IsNullOrWhiteSpace(text.ToFullString()):
                    return true;
            }
        }

        return false;
    }

    private BoundMarkupContentSetter BindMarkupPropertyElementSetter(
        MarkupElementSyntax markupElement,
        IPropertySymbol property)
    {
        var containingElement = AkburaSemanticModel.GetParentMarkupElement(markupElement);
        var containingComponent = containingElement == null
            ? null
            : SemanticModel.GetSymbolInfo(containingElement).Symbol as IMarkupComponentSymbol;
        var contentModel = SemanticModel.CreateMarkupPropertyElementContentModel(property);
        var content = SemanticModel.CreateMarkupChildren(
            markupElement,
            contentModel,
            out var contentDiagnostics);

        using var diagnosticsBuilder = ImmutableArrayBuilder<AkburaSemanticDiagnostic>.Rent();
        diagnosticsBuilder.AddRange(contentDiagnostics);
        if (!contentModel.IsCollection && !property.CanWrite)
        {
            diagnosticsBuilder.Add(new AkburaSemanticDiagnostic(
                markupElement,
                ErrorCodes.AKBURA_SEMANTIC_MarkupPropertyAccessNotSupported,
                [property.Name, "public setter", "node"]));
        }

        var diagnostics = diagnosticsBuilder.ToImmutable();
        SemanticModel.SetSemanticDiagnostics(markupElement, diagnostics);

        var valueType = content.Length == 1
            ? content[0].Type
            : property.Type;
        var valueConversion = content.Length == 1
            ? Conversions.ClassifyConversion(
                content[0].Type.Symbol as CSharpTypeSymbol,
                contentModel.AllowedChildType.Symbol as CSharpTypeSymbol)
            : default;

        return new BoundMarkupContentSetter(
            markupElement,
            this,
            containingComponent,
            property,
            contentModel,
            content,
            valueType,
            valueOperation: default,
            valueConversion,
            literalValue: null,
            isSynthesizedString: false,
            diagnostics,
            diagnostics.Length > 0);
    }

    private BoundMarkupContentSetter? BindMarkupContentSetter(
        MarkupElementSyntax markupElement,
        IMarkupComponentSymbol? componentSymbol)
    {
        if (componentSymbol == null ||
            componentSymbol.ContentModel.IsDefault)
        {
            return null;
        }

        if (AkburaSemanticModel.HasElementContent(markupElement))
        {
            if (componentSymbol.Children.IsDefaultOrEmpty)
            {
                return null;
            }

            var elementProperty = SemanticModel.CreateMarkupContentPropertySymbol(componentSymbol);
            var elementDiagnostics = SemanticModel.GetCachedSemanticDiagnostics(markupElement);
            var elementValueType = componentSymbol.Children.Length == 1
                ? componentSymbol.Children[0].Type
                : componentSymbol.ContentModel.AllowedChildType;
            var elementConversion = componentSymbol.Children.Length == 1
                ? Conversions.ClassifyConversion(
                    componentSymbol.Children[0].Type.Symbol as CSharpTypeSymbol,
                    componentSymbol.ContentModel.AllowedChildType.Symbol as CSharpTypeSymbol)
                : default;
            return new BoundMarkupContentSetter(
                markupElement,
                this,
                componentSymbol,
                elementProperty,
                componentSymbol.ContentModel,
                componentSymbol.Children,
                elementValueType,
                valueOperation: default,
                elementConversion,
                literalValue: null,
                isSynthesizedString: false,
                elementDiagnostics,
                elementProperty == null || elementDiagnostics.Length > 0);
        }

        if (!AkburaSemanticModel.TryCreateMarkupContentValueExpression(
                markupElement,
                out var expression,
                out var literalValue,
                out var isSynthesizedString,
                out var hasText,
                out var diagnosticSyntax))
        {
            return null;
        }

        var property = SemanticModel.CreateMarkupContentPropertySymbol(componentSymbol);
        var targetType = AkburaSemanticModel.GetMarkupContentTargetType(componentSymbol.ContentModel);
        var binding = SemanticModel.BindMarkupAttributeExpression(diagnosticSyntax, expression, targetType);
        var valueTypeSymbol = binding.TypeSymbol ??
            binding.Conversion.SourceType ??
            binding.OperationDefinition.Type;
        if (valueTypeSymbol == null &&
            !isSynthesizedString &&
            !hasText &&
            diagnosticSyntax.Kind == AkburaSyntaxKind.MarkupInlineExpressionSyntax)
        {
            var directBinding = SemanticModel.BindMarkupAttributeExpression(
                diagnosticSyntax,
                expression);
            valueTypeSymbol = directBinding.TypeSymbol ??
                directBinding.OperationDefinition.Type;
        }

        var valueType = valueTypeSymbol == null
            ? default
            : new CSharpSymbolDefinition(valueTypeSymbol);

        using var diagnosticsBuilder = ImmutableArrayBuilder<AkburaSemanticDiagnostic>.Rent();
        SemanticModel.AddMarkupExpressionDiagnostics(
            markupElement,
            expression.ToFullString(),
            binding,
            diagnosticsBuilder);
        SemanticModel.AddMarkupContentValueDiagnostics(
            diagnosticSyntax,
            componentSymbol.ContentModel,
            binding,
            hasText,
            diagnosticsBuilder);
        var diagnostics = diagnosticsBuilder.ToImmutable();

        return new BoundMarkupContentSetter(
            markupElement,
            this,
            componentSymbol,
            property,
            componentSymbol.ContentModel,
            componentSymbol.Children,
            valueType,
            binding.OperationDefinition,
            binding.Conversion,
            literalValue,
            isSynthesizedString,
            diagnostics,
            property == null || diagnostics.Length > 0);
    }

    private BoundMarkupContent BindMarkupContent(MarkupContentSyntax content)
    {
        using var childrenBuilder = ImmutableArrayBuilder<BoundNode>.Rent();
        AkburaSymbolInfo symbolInfo = AkburaSymbolInfo.None(CandidateReason.None);
        ImmutableArray<AkburaSemanticDiagnostic> diagnostics = default;

        switch (content.Kind)
        {
            case AkburaSyntaxKind.MarkupElementContentSyntax:
            {
                var elementContent = Unsafe.As<MarkupElementContentSyntax>(content);
                var element = SemanticModel.BindingSession.BindSemanticSyntax(elementContent.Element);
                symbolInfo = element.SymbolInfo;
                childrenBuilder.Add(element);
                break;
            }

            case AkburaSyntaxKind.MarkupInlineExpressionSyntax:
            {
                var inlineExpression = Unsafe.As<MarkupInlineExpressionSyntax>(content);
                var expression = AkburaSemanticModel.ParseInlineExpression(inlineExpression.Expression);
                if (expression != null)
                {
                    var binding = SemanticModel.BindMarkupAttributeExpression(inlineExpression, expression);
                    using var diagnosticsBuilder = ImmutableArrayBuilder<AkburaSemanticDiagnostic>.Rent();
                    SemanticModel.AddMarkupExpressionDiagnostics(
                        inlineExpression,
                        inlineExpression.Expression.ToFullString().Trim(),
                        binding,
                        diagnosticsBuilder);
                    diagnostics = diagnosticsBuilder.ToImmutable();
                    SemanticModel.SetSemanticDiagnostics(inlineExpression, diagnostics);
                }

                break;
            }
        }

        var boundContent = new BoundMarkupContent(
            content,
            this,
            symbolInfo,
            diagnostics.IsDefault ? SemanticModel.GetCachedSemanticDiagnostics(content) : diagnostics,
            childrenBuilder.ToImmutable());
        SemanticModel.SetCachedBoundNode(content, boundContent);
        return boundContent;
    }

    private BoundNode BindMarkupAttribute(MarkupAttributeSyntax markupAttribute)
    {
        return markupAttribute.Kind switch
        {
            AkburaSyntaxKind.TailwindFlagAttributeSyntax or
                AkburaSyntaxKind.TailwindFullAttributeSyntax =>
                CreateBoundTailwindUtilityAttribute(Unsafe.As<TailwindAttributeSyntax>(markupAttribute)),
            AkburaSyntaxKind.MarkupPlainAttributeSyntax or
                AkburaSyntaxKind.MarkupAttachedPropertyAttributeSyntax or
                AkburaSyntaxKind.MarkupPrefixedAttributeSyntax =>
                BindMarkupPropertyOrEvent(markupAttribute),
            _ => new BoundDeclaration(
                markupAttribute,
                this,
                AkburaSymbolInfo.None(CandidateReason.UnsupportedSyntax)),
        };
    }

    private BoundNode BindMarkupPropertyOrEvent(MarkupAttributeSyntax markupAttribute)
    {
        if (AkburaSemanticModel.IsMarkupNameDirective(markupAttribute))
        {
            return BindMarkupNameAssignment(
                Unsafe.As<MarkupAttachedPropertyAttributeSyntax>(markupAttribute));
        }

        return SemanticModel.GetSymbolInfo(markupAttribute).Symbol is IRoutedEventSymbol routedEvent
            ? BindMarkupRoutedEvent(markupAttribute, routedEvent)
            : BindMarkupPropertySetter(markupAttribute);
    }

    private BoundMarkupNameAssignment BindMarkupNameAssignment(
        MarkupAttachedPropertyAttributeSyntax markupAttribute)
    {
        var symbolInfo = SemanticModel.GetSymbolInfo(markupAttribute);
        var containingComponent = SemanticModel.GetContainingMarkupComponentSymbol(markupAttribute);
        var diagnostics = SemanticModel.GetCachedSemanticDiagnostics(markupAttribute);
        return new BoundMarkupNameAssignment(
            markupAttribute,
            this,
            symbolInfo,
            containingComponent,
            diagnostics,
            symbolInfo.Symbol is not IMarkupNameSymbol || diagnostics.Length > 0);
    }

    private BoundNode BindMarkupPropertySetter(MarkupAttributeSyntax markupAttribute)
    {
        var propertyInfo = SemanticModel.GetSymbolInfo(markupAttribute);
        var property = propertyInfo.Symbol as IPropertySymbol;
        var containingComponent = SemanticModel.GetContainingMarkupComponentSymbol(markupAttribute);
        var valueSyntax = AkburaSemanticModel.GetMarkupAttributeValue(markupAttribute);
        var bindingKind = AkburaSemanticModel.GetMarkupAttributeBindingKind(markupAttribute);
        var valueKind = MarkupAttributeValueKind.None;
        var literalValue = default(string);
        var valueType = default(CSharpSymbolDefinition);
        var valueOperation = default(CSharpOperationDefinition);
        var valueConversion = default(AkburaConversion);
        var dynamicExpression = default(CSharp.ExpressionSyntax);
        var valueBinding = CSharpBindingResult.Empty;
        var markupExtensionBinding = default(MarkupExtensionBindingResult);
        var literalConversionStatus = MarkupLiteralConversionStatus.Unsupported;
        var targetType = GetExpectedValueType(property);
        object? convertedValue = null;
        var appliedAkcssSymbols = ImmutableArray<IAkcssSymbol>.Empty;

        if (valueSyntax?.Kind == AkburaSyntaxKind.MarkupLiteralAttributeValueSyntax)
        {
            var literalValueSyntax = Unsafe.As<MarkupLiteralAttributeValueSyntax>(valueSyntax);
            valueKind = MarkupAttributeValueKind.Literal;
            literalValue = AkburaSemanticModel.GetMarkupLiteralAttributeValueText(literalValueSyntax);
            valueBinding = SemanticModel.BindMarkupAttributeExpression(
                markupAttribute,
                CSharpSyntaxFactory.LiteralExpression(
                    CSharpSyntaxKind.StringLiteralExpression,
                    CSharpSyntaxFactory.Literal(literalValue)),
                targetType);
            valueType = valueBinding.TypeSymbol == null ? default : new CSharpSymbolDefinition(valueBinding.TypeSymbol);
            valueOperation = valueBinding.OperationDefinition;
            valueConversion = valueBinding.Conversion;
            if (AkburaSemanticModel.IsMarkupDataTypeDirective(markupAttribute) &&
                SemanticModel.TryBindMarkupDataTypeDirective(literalValue, out var dataType))
            {
                valueType = property?.Type ?? valueType;
                convertedValue = new CSharpSymbolDefinition(dataType);
            }
            else if (property?.Type.Symbol is CSharpTypeSymbol literalTargetType &&
                AkburaSemanticModel.IsAvaloniaGridDefinitionListType(literalTargetType))
            {
                if (GridDefinitionLiteralParser.TryParse(literalValue, out var gridDefinitions))
                {
                    valueType = new CSharpSymbolDefinition(literalTargetType);
                    convertedValue = gridDefinitions;
                }
            }
            else if (property?.Type.Symbol is CSharpTypeSymbol targetLiteralType)
            {
                literalConversionStatus = MarkupLiteralValueConverter.Convert(
                    literalValue,
                    targetLiteralType,
                    SemanticModel.Compilation.CSharpCompilation,
                    out convertedValue);
                if (literalConversionStatus == MarkupLiteralConversionStatus.Success)
                {
                    if (targetLiteralType.SpecialType != Microsoft.CodeAnalysis.SpecialType.System_Object)
                    {
                        valueType = new CSharpSymbolDefinition(targetLiteralType);
                    }
                }
            }
        }
        else if (valueSyntax?.Kind == AkburaSyntaxKind.MarkupDynamicAttributeValueSyntax)
        {
            var dynamicValueSyntax = Unsafe.As<MarkupDynamicAttributeValueSyntax>(valueSyntax);
            var expression = AkburaSemanticModel.ParseInlineExpression(dynamicValueSyntax.Expression);
            if (expression == null)
            {
                valueKind = MarkupAttributeValueKind.Error;
            }
            else
            {
                dynamicExpression = expression;
                valueKind = MarkupAttributeValueKind.DynamicExpression;
                valueBinding = SemanticModel.BindMarkupAttributeExpression(markupAttribute, expression, targetType);
                valueType = valueBinding.TypeSymbol == null ? default : new CSharpSymbolDefinition(valueBinding.TypeSymbol);
                valueOperation = valueBinding.OperationDefinition;
                valueConversion = valueBinding.Conversion;
            }
        }
        else if (valueSyntax?.Kind == AkburaSyntaxKind.MarkupExtensionAttributeValueSyntax)
        {
            var markupExtensionValueSyntax = Unsafe.As<MarkupExtensionAttributeValueSyntax>(valueSyntax);
            valueKind = MarkupAttributeValueKind.MarkupExtension;
            markupExtensionBinding = SemanticModel.BindMarkupExtensionAttributeValue(
                markupAttribute,
                markupExtensionValueSyntax.Extension,
                property);
            valueType = markupExtensionBinding.ResultType;
            valueConversion = markupExtensionBinding.Conversion;
            convertedValue = markupExtensionBinding.Value;
        }

        var commandHandler = default(AkburaSemanticModel.MarkupCommandHandlerAnalysis);
        if (property?.Command is { } propertyCommand)
        {
            commandHandler = SemanticModel.AnalyzeMarkupCommandHandler(
                markupAttribute,
                propertyCommand,
                dynamicExpression,
                valueType,
                valueOperation);
        }

        ImmutableArray<AkburaSemanticDiagnostic> diagnostics;
        if (property == null)
        {
            diagnostics = SemanticModel.GetCachedSemanticDiagnostics(markupAttribute);
        }
        else
        {
            var diagnosticsBag = BindingDiagnosticBag.GetInstance();
            {
                using var diagnosticsBuilder = ImmutableArrayBuilder<AkburaSemanticDiagnostic>.Rent();
                AkburaSemanticModel.AddMarkupAttributeBindingDiagnostics(
                    markupAttribute,
                    property,
                    bindingKind,
                    diagnosticsBuilder);
                AkburaSemanticModel.AddDuplicateMarkupPropertySetterDiagnostics(
                    markupAttribute,
                    property,
                    bindingKind,
                    diagnosticsBuilder);
                if (literalValue != null)
                {
                    appliedAkcssSymbols = ResolveAkcssClassSymbolsForAttribute(
                        markupAttribute,
                        literalValue,
                        containingComponent,
                        diagnosticsBuilder);

                    SemanticModel.AddMarkupDefinitionListLiteralDiagnostics(
                        markupAttribute,
                        property,
                        literalValue,
                        diagnosticsBuilder);
                    SemanticModel.AddMarkupLiteralValueDiagnostics(
                        markupAttribute,
                        property,
                        literalConversionStatus,
                        diagnosticsBuilder);
                }

                if (valueSyntax != null && valueKind == MarkupAttributeValueKind.DynamicExpression)
                {
                    if (property.Command == null)
                    {
                        SemanticModel.AddMarkupExpressionDiagnostics(
                            markupAttribute,
                            valueBinding,
                            diagnosticsBuilder);

                        SemanticModel.AddMarkupAttributeValueDiagnostics(
                            markupAttribute,
                            property,
                            valueBinding,
                            diagnosticsBuilder);
                    }
                }
                else if (valueKind == MarkupAttributeValueKind.MarkupExtension)
                {
                    diagnosticsBuilder.AddRange(markupExtensionBinding.Diagnostics);
                }

                if (property.Command is { } commandSymbol)
                {
                    SemanticModel.AddMarkupCommandHandlerSignatureDiagnostics(
                        markupAttribute,
                        commandSymbol,
                        dynamicExpression,
                        commandHandler,
                        diagnosticsBuilder);
                }

                SemanticModel.AddMarkupExpressionDiagnostics(
                    markupAttribute,
                    commandHandler.Diagnostics,
                    diagnosticsBuilder);
                diagnosticsBag.AddRange(diagnosticsBuilder.ToImmutable());
            }

            diagnostics = SemanticModel.SetSemanticDiagnostics(markupAttribute, diagnosticsBag);
        }

        if (property?.Command is { } command)
        {
            return new BoundMarkupCommandBinding(
                markupAttribute,
                this,
                containingComponent,
                property,
                command,
                bindingKind,
                valueKind,
                valueSyntax,
                commandHandler.Kind,
                commandHandler.ArgumentMode,
                commandHandler.ResultMode,
                commandHandler.ParameterCount,
                commandHandler.IsAsync,
                commandHandler.ContainsAwait,
                commandHandler.Type,
                commandHandler.ResultType,
                commandHandler.Operation,
                diagnostics,
                valueKind == MarkupAttributeValueKind.Error || diagnostics.Length > 0);
        }

        return new BoundMarkupPropertySetter(
            markupAttribute,
            this,
            containingComponent,
            property,
            appliedAkcssSymbols,
            valueType,
            valueOperation,
            valueConversion,
            bindingKind,
            valueKind,
            valueSyntax,
            literalValue,
            convertedValue,
            diagnostics,
            property == null || valueKind == MarkupAttributeValueKind.Error || diagnostics.Length > 0);
    }

    private BoundNode BindMarkupRoutedEvent(
        MarkupAttributeSyntax markupAttribute,
        IRoutedEventSymbol routedEvent)
    {
        var containingComponent = SemanticModel.GetContainingMarkupComponentSymbol(markupAttribute);
        var valueSyntax = AkburaSemanticModel.GetMarkupAttributeValue(markupAttribute);
        var bindingKind = AkburaSemanticModel.GetMarkupAttributeBindingKind(markupAttribute);
        var valueKind = MarkupAttributeValueKind.None;
        var expression = default(CSharp.ExpressionSyntax);

        if (valueSyntax?.Kind == AkburaSyntaxKind.MarkupDynamicAttributeValueSyntax)
        {
            var dynamicValueSyntax = Unsafe.As<MarkupDynamicAttributeValueSyntax>(valueSyntax);
            expression = AkburaSemanticModel.ParseInlineExpression(dynamicValueSyntax.Expression);
            valueKind = expression == null
                ? MarkupAttributeValueKind.Error
                : MarkupAttributeValueKind.DynamicExpression;
        }
        else if (valueSyntax?.Kind == AkburaSyntaxKind.MarkupLiteralAttributeValueSyntax)
        {
            valueKind = MarkupAttributeValueKind.Literal;
        }
        else if (valueSyntax?.Kind == AkburaSyntaxKind.MarkupExtensionAttributeValueSyntax)
        {
            valueKind = MarkupAttributeValueKind.MarkupExtension;
        }

        var handler = SemanticModel.AnalyzeMarkupEventHandler(markupAttribute, routedEvent, expression);
        var diagnosticsBag = BindingDiagnosticBag.GetInstance();
        {
            using var diagnosticsBuilder = ImmutableArrayBuilder<AkburaSemanticDiagnostic>.Rent();
            AkburaSemanticModel.AddMarkupEventBindingDiagnostics(
                markupAttribute,
                routedEvent,
                bindingKind,
                diagnosticsBuilder);
            SemanticModel.AddMarkupEventHandlerSignatureDiagnostics(
                markupAttribute,
                routedEvent,
                expression,
                diagnosticsBuilder);
            SemanticModel.AddMarkupExpressionDiagnostics(
                markupAttribute,
                handler.Diagnostics,
                diagnosticsBuilder);
            diagnosticsBag.AddRange(diagnosticsBuilder.ToImmutable());
        }
        var diagnostics = SemanticModel.SetSemanticDiagnostics(markupAttribute, diagnosticsBag);

        return new BoundMarkupRoutedEventBinding(
            markupAttribute,
            this,
            containingComponent,
            routedEvent,
            bindingKind,
            valueKind,
            valueSyntax,
            handler.Kind,
            handler.ArgumentMode,
            handler.ParameterCount,
            handler.IsAsync,
            handler.ContainsAwait,
            handler.Operation,
            diagnostics,
            valueKind != MarkupAttributeValueKind.DynamicExpression ||
                handler.Kind == MarkupCommandHandlerKind.Error ||
                diagnostics.Length > 0);
    }

    private static CSharpTypeSymbol? GetExpectedValueType(IPropertySymbol? property)
    {
        if (property?.Command != null ||
            property?.Type.Symbol is not CSharpTypeSymbol type ||
            type.SpecialType == Microsoft.CodeAnalysis.SpecialType.System_Object)
        {
            return null;
        }

        return type;
    }
}
