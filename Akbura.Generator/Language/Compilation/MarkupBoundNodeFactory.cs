using Akbura.Language.Binder;
using Akbura.Language.BoundTree;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using AkburaCandidateReason = Akbura.Language.Symbols.CandidateReason;
using AkburaPropertySymbol = Akbura.Language.Symbols.IPropertySymbol;
using AkburaSyntaxKind = Akbura.Language.Syntax.SyntaxKind;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Akbura.Language;

internal sealed class MarkupBoundNodeFactory
{
    private readonly AkburaSemanticModel _semanticModel;

    public MarkupBoundNodeFactory(AkburaSemanticModel semanticModel)
    {
        _semanticModel = semanticModel ?? throw new ArgumentNullException(nameof(semanticModel));
    }

    public BoundNode CreateSyntax(AkburaSyntax syntax)
    {
        return syntax.Kind switch
        {
            AkburaSyntaxKind.MarkupRootSyntax =>
                CreateRoot(Unsafe.As<MarkupRootSyntax>(syntax)),
            AkburaSyntaxKind.MarkupElementSyntax =>
                CreateComponent(Unsafe.As<MarkupElementSyntax>(syntax)),
            AkburaSyntaxKind.MarkupElementContentSyntax or
                AkburaSyntaxKind.MarkupInlineExpressionSyntax or
                AkburaSyntaxKind.MarkupTextLiteralSyntax =>
                CreateContent(Unsafe.As<MarkupContentSyntax>(syntax)),
            _ => new BoundDeclaration(
                syntax,
                _semanticModel.GetBinder(syntax, BinderUsage.Markup),
                AkburaSymbolInfo.None(AkburaCandidateReason.UnsupportedSyntax)),
        };
    }

    private BoundMarkupRoot CreateRoot(MarkupRootSyntax markupRoot)
    {
        var element = _semanticModel.BindingSession.BindSemanticSyntax(markupRoot.Element);
        var symbolInfo = element.SymbolInfo;
        var boundRoot = new BoundMarkupRoot(
            markupRoot,
            _semanticModel.GetBinder(markupRoot, BinderUsage.Markup),
            symbolInfo,
            _semanticModel.GetCachedSemanticDiagnostics(markupRoot),
            ImmutableArray.Create(element));
        _semanticModel.SetCachedBoundNode(markupRoot, boundRoot);
        return boundRoot;
    }

    private BoundMarkupComponent CreateComponent(MarkupElementSyntax markupElement)
    {
        var symbolInfo = _semanticModel.GetSymbolInfo(markupElement);
        var componentSymbol = symbolInfo.Symbol as IMarkupComponentSymbol;
        using var childrenBuilder = ImmutableArrayBuilder<BoundNode>.Rent();

        if (markupElement.StartTag != null)
        {
            foreach (var attribute in markupElement.StartTag.Attributes)
            {
                childrenBuilder.Add(_semanticModel.BindingSession.BindOperationSyntax(attribute));
            }
        }

        foreach (var content in markupElement.Body)
        {
            childrenBuilder.Add(_semanticModel.BindingSession.BindSemanticSyntax(content));
        }

        var contentSetter = CreateContentSetter(markupElement, componentSymbol);
        if (contentSetter != null)
        {
            childrenBuilder.Add(contentSetter);
        }

        var boundComponent = new BoundMarkupComponent(
            markupElement,
            _semanticModel.GetBinder(markupElement, BinderUsage.Markup),
            symbolInfo,
            _semanticModel.GetCachedSemanticDiagnostics(markupElement),
            childrenBuilder.ToImmutable());
        _semanticModel.SetCachedBoundNode(markupElement, boundComponent);
        return boundComponent;
    }

    private BoundMarkupContentSetter? CreateContentSetter(
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

        var property = _semanticModel.CreateMarkupContentPropertySymbol(componentSymbol);
        var targetType = AkburaSemanticModel.GetMarkupContentTargetType(componentSymbol.ContentModel);
        var binding = _semanticModel.BindMarkupAttributeExpression(diagnosticSyntax, expression, targetType);
        var valueTypeSymbol = binding.TypeSymbol ??
            binding.Conversion.SourceType ??
            binding.OperationDefinition.Type;
        if (valueTypeSymbol == null &&
            !isSynthesizedString &&
            !hasText &&
            diagnosticSyntax.Kind == AkburaSyntaxKind.MarkupInlineExpressionSyntax)
        {
            var directBinding = _semanticModel.BindMarkupAttributeExpression(
                diagnosticSyntax,
                expression);
            valueTypeSymbol = directBinding.TypeSymbol ??
                directBinding.OperationDefinition.Type;
        }

        var valueType = valueTypeSymbol == null
            ? default
            : new CSharpSymbolDefinition(valueTypeSymbol);

        using var diagnosticsBuilder = ImmutableArrayBuilder<AkburaSemanticDiagnostic>.Rent();
        _semanticModel.AddMarkupExpressionDiagnostics(
            markupElement,
            expression.ToFullString(),
            binding,
            diagnosticsBuilder);
        _semanticModel.AddMarkupContentValueDiagnostics(
            diagnosticSyntax,
            componentSymbol.ContentModel,
            binding,
            hasText,
            diagnosticsBuilder);
        var diagnostics = diagnosticsBuilder.ToImmutable();

        return new BoundMarkupContentSetter(
            markupElement,
            _semanticModel.GetBinder(markupElement, BinderUsage.Markup),
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

    private BoundMarkupContent CreateContent(MarkupContentSyntax content)
    {
        using var childrenBuilder = ImmutableArrayBuilder<BoundNode>.Rent();
        AkburaSymbolInfo symbolInfo = AkburaSymbolInfo.None(AkburaCandidateReason.None);
        ImmutableArray<AkburaSemanticDiagnostic> diagnostics = default;

        if (content.Kind == AkburaSyntaxKind.MarkupElementContentSyntax)
        {
            var elementContent = Unsafe.As<MarkupElementContentSyntax>(content);
            var element = _semanticModel.BindingSession.BindSemanticSyntax(elementContent.Element);
            symbolInfo = element.SymbolInfo;
            childrenBuilder.Add(element);
        }
        else if (content.Kind == AkburaSyntaxKind.MarkupInlineExpressionSyntax)
        {
            var inlineExpression = Unsafe.As<MarkupInlineExpressionSyntax>(content);
            var expression = AkburaSemanticModel.ParseInlineExpression(inlineExpression.Expression);
            if (expression != null)
            {
                var binding = _semanticModel.BindMarkupAttributeExpression(inlineExpression, expression);
                using var diagnosticsBuilder = ImmutableArrayBuilder<AkburaSemanticDiagnostic>.Rent();
                _semanticModel.AddMarkupExpressionDiagnostics(
                    inlineExpression,
                    inlineExpression.Expression.ToFullString().Trim(),
                    binding,
                    diagnosticsBuilder);
                diagnostics = diagnosticsBuilder.ToImmutable();
                _semanticModel.SetSemanticDiagnostics(inlineExpression, diagnostics);
            }
        }

        var boundContent = new BoundMarkupContent(
            content,
            _semanticModel.GetBinder(content, BinderUsage.Markup),
            symbolInfo,
            diagnostics.IsDefault ? _semanticModel.GetCachedSemanticDiagnostics(content) : diagnostics,
            childrenBuilder.ToImmutable());
        _semanticModel.SetCachedBoundNode(content, boundContent);
        return boundContent;
    }
}
