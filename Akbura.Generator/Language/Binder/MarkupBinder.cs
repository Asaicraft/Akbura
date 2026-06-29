using Akbura.Language.Declarations;
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

internal sealed class MarkupBinder : Binder
{
    public MarkupBinder(
        AkburaSemanticModel semanticModel,
        Binder next,
        AkburaDeclaration declaration,
        AkburaBinderFlags flags = AkburaBinderFlags.None)
        : base(
            semanticModel,
            next,
            declaration,
            declaration.Syntax,
            flags | AkburaBinderFlags.InMarkup)
    {
    }

    public IMarkupComponentSymbol? TargetComponentSymbol
    {
        get
        {
            return Declaration?.Syntax switch
            {
                MarkupRootSyntax markupRoot => SemanticModel.GetSymbolInfo(markupRoot.Element).Symbol as IMarkupComponentSymbol,
                MarkupElementSyntax markupElement => SemanticModel.GetSymbolInfo(markupElement).Symbol as IMarkupComponentSymbol,
                _ => null,
            };
        }
    }

    public override BoundNode BindOperationSyntax(AkburaSyntax syntax)
    {
        return syntax.Kind switch
        {
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
                SemanticModel.CreateBoundMarkupSyntax(syntax),
            AkburaSyntaxKind.MarkupPlainAttributeSyntax or
                AkburaSyntaxKind.MarkupPrefixedAttributeSyntax or
                AkburaSyntaxKind.TailwindFlagAttributeSyntax or
                AkburaSyntaxKind.TailwindFullAttributeSyntax =>
                BindOperationSyntax(syntax),
            _ => base.BindSemanticSyntax(syntax),
        };
    }

    private BoundNode BindMarkupAttribute(MarkupAttributeSyntax markupAttribute)
    {
        return markupAttribute.Kind switch
        {
            AkburaSyntaxKind.TailwindFlagAttributeSyntax or
                AkburaSyntaxKind.TailwindFullAttributeSyntax =>
                SemanticModel.CreateBoundTailwindUtilityAttribute(Unsafe.As<TailwindAttributeSyntax>(markupAttribute)),
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
            var diagnosticsBag = new BindingDiagnosticBag();
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
        var diagnosticsBag = new BindingDiagnosticBag();
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
