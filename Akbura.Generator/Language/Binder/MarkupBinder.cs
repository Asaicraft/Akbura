using Akbura.Language.BoundTree;
using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using AkburaSyntaxKind = Akbura.Language.Syntax.SyntaxKind;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;
using CSharpTypeSymbol = Microsoft.CodeAnalysis.ITypeSymbol;
using CSharpSyntaxKind = Microsoft.CodeAnalysis.CSharp.SyntaxKind;
using CSharpSyntaxFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Akbura.Language.Binder;

internal sealed partial class MarkupBinder : Binder
{
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

    public override BoundNode BindOperationSyntax(AkburaSyntax syntax)
    {
        return syntax.Kind switch
        {
            AkburaSyntaxKind.MarkupElementSyntax =>
                BindMarkupComponent(Unsafe.As<MarkupElementSyntax>(syntax)),
            AkburaSyntaxKind.MarkupPlainAttributeSyntax or
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

        var contentSetter = BindMarkupContentSetter(markupElement, componentSymbol);
        if (contentSetter != null)
        {
            childrenBuilder.Add(contentSetter);
        }

        var boundComponent = new BoundMarkupComponent(
            markupElement,
            this,
            symbolInfo,
            SemanticModel.GetCachedSemanticDiagnostics(markupElement),
            childrenBuilder.ToImmutable());
        SemanticModel.SetCachedBoundNode(markupElement, boundComponent);
        return boundComponent;
    }

    private BoundMarkupContentSetter? BindMarkupContentSetter(
        MarkupElementSyntax markupElement,
        IMarkupComponentSymbol? componentSymbol)
    {
        if (componentSymbol == null ||
            componentSymbol.ContentModel.IsDefault ||
            AkburaSemanticModel.HasElementContent(markupElement) ||
            !AkburaSemanticModel.TryCreateMarkupContentValueExpression(
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
        return SemanticModel.GetSymbolInfo(markupAttribute).Symbol is IRoutedEventSymbol routedEvent
            ? BindMarkupRoutedEvent(markupAttribute, routedEvent)
            : BindMarkupPropertySetter(markupAttribute);
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
        var dynamicExpression = default(CSharp.ExpressionSyntax);
        var valueBinding = CSharpBindingResult.Empty;
        var targetType = GetExpectedValueType(property);
        var appliedAkcssSymbols = ImmutableArray<IAkcssSymbol>.Empty;

        if (valueSyntax?.Kind == AkburaSyntaxKind.MarkupLiteralAttributeValueSyntax)
        {
            var literalValueSyntax = Unsafe.As<MarkupLiteralAttributeValueSyntax>(valueSyntax);
            valueKind = MarkupAttributeValueKind.Literal;
            literalValue = AkburaSemanticModel.GetMarkupLiteralAttributeValueText(literalValueSyntax);
            valueBinding = SemanticModel.BindMarkupAttributeExpression(markupAttribute, CSharpSyntaxFactory.LiteralExpression(
                CSharpSyntaxKind.StringLiteralExpression,
                CSharpSyntaxFactory.Literal(literalValue)));
            valueType = valueBinding.TypeSymbol == null ? default : new CSharpSymbolDefinition(valueBinding.TypeSymbol);
            valueOperation = valueBinding.OperationDefinition;
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
            }
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
            bindingKind,
            valueKind,
            valueSyntax,
            literalValue,
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
