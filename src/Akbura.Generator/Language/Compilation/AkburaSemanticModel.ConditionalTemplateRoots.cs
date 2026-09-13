using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Language.BoundTree;
using Akbura.Language.Binder;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Immutable;

namespace Akbura.Language;

internal abstract partial class AkburaSemanticModel
{
    internal bool IsConditionalTemplateRootDestination(MarkupElementSyntax boundary)
    {
        var info = GetConditionalTemplateRootInfo(boundary);
        return info.IsSupported && info.IsImplicitControlRoot;
    }

    private void AddExplicitConditionalTemplateItemDiagnostics(MarkupElementSyntax boundary,
        ReadOnlySpan<BoundMarkupConditionalBranch> branches,
        ImmutableArrayBuilder<AkburaSemanticDiagnostic> diagnostics)
    {
        var info = GetConditionalTemplateRootInfo(boundary);
        if (info.Kind != MarkupConditionalTemplateRootKind.DataTemplate || info.IsImplicitControlRoot ||
            !MarkupDataTypeResolver.HasItemNameDirective(boundary))
        {
            return;
        }

        foreach (var branch in branches)
        {
            if (branch.ConditionSyntax is { } condition)
            {
                AddOutsideBuildTemplateItemDiagnostic(boundary, condition,
                    GetCSharpSymbolReferences(condition), diagnostics);
            }

            var body = branch.Syntax switch
            {
                MarkupIfStatementSyntax statement => statement.Body,
                MarkupElseIfClauseSyntax clause => clause.Body,
                MarkupElseClauseSyntax clause => clause.Body,
                _ => null,
            };
            if (body == null)
            {
                continue;
            }

            foreach (var syntax in body.DescendantNodes())
            {
                switch (syntax)
                {
                    case MarkupAttributeSyntax attribute:
                        AddOutsideBuildTemplateItemDiagnostic(boundary, attribute,
                            GetCSharpSymbolReferences(attribute), diagnostics);
                        break;
                    case MarkupInlineExpressionSyntax expression:
                        AddOutsideBuildTemplateItemDiagnostic(boundary, expression,
                            GetCSharpSymbolReferences(expression.Expression), diagnostics);
                        break;
                    case CSharpExpressionSyntax expression when CSharpProbeBuilder.IsMarkupCondition(expression):
                        AddOutsideBuildTemplateItemDiagnostic(boundary, expression,
                            GetCSharpSymbolReferences(expression), diagnostics);
                        break;
                }
            }
        }
    }

    private static void AddOutsideBuildTemplateItemDiagnostic(MarkupElementSyntax boundary,
        AkburaSyntax syntax, ImmutableArray<CSharpSymbolReference> references,
        ImmutableArrayBuilder<AkburaSemanticDiagnostic> diagnostics)
    {
        foreach (var diagnostic in diagnostics.WrittenSpan)
        {
            if (diagnostic.Code == ErrorCodes.AKBURA_SEMANTIC_ConditionalTemplateItemOutsideBuild &&
                ReferenceEquals(diagnostic.Syntax, syntax))
            {
                return;
            }
        }

        foreach (var reference in references)
        {
            if (reference.AkburaSymbol is IMarkupItemSymbol item &&
                ReferenceEquals(GetContainingMarkupElement(item.DeclarationSyntax), boundary))
            {
                diagnostics.Add(new AkburaSemanticDiagnostic(syntax,
                    ErrorCodes.AKBURA_SEMANTIC_ConditionalTemplateItemOutsideBuild, [item.Name]));
                return;
            }
        }
    }

    internal MarkupConditionalTemplateRootInfo GetConditionalTemplateRootInfo(MarkupElementSyntax boundary,
        bool resolveExpressionRootTypes = true)
    {
        if (!HasMarkupConditionalContent(boundary.Body))
        {
            return default;
        }

        var symbol = GetSymbolInfo(boundary).Symbol;
        var model = symbol switch
        {
            Symbols.IPropertySymbol property => CreateMarkupPropertyElementContentModel(property),
            IMarkupComponentSymbol component => component.ContentModel,
            _ => default,
        };
        if (model.ContentProperty.Symbol is not Microsoft.CodeAnalysis.IPropertySymbol destination ||
            Compilation.CSharpCompilation.GetTypeByMetadataName("Avalonia.Controls.Control") is not { } controlType)
        {
            return default;
        }

        var resolver = BindingSession.MarkupTemplateContent;
        var deferred = resolver.IsDeferredContentProperty(destination);
        if (!deferred && !resolver.IsDataTemplateProperty(destination))
        {
            return default;
        }

        var kind = MarkupConditionalTemplateRootKind.DataTemplate;
        var supported = false;
        var templateType = Compilation.CSharpCompilation.GetTypeByMetadataName("Avalonia.Controls.Templates.IDataTemplate");
        var rootKinds = GetConditionalTemplateRootKinds(boundary.Body, controlType, templateType,
            resolveExpressionRootTypes);
        // Empty alternatives still describe one template whose current result is null.
        var implicitRoot = deferred || rootKinds.HasControl || !rootKinds.HasTemplate;
        var resultType = deferred ? resolver.GetDeferredResultType(destination) : controlType;
        if (deferred)
        {
            kind = destination.ContainingType.ToDisplayString() switch
            {
                "Avalonia.Markup.Xaml.Templates.DataTemplate" => MarkupConditionalTemplateRootKind.DeferredDataTemplate,
                "Avalonia.Markup.Xaml.Templates.ControlTemplate" => MarkupConditionalTemplateRootKind.DeferredControlTemplate,
                _ => MarkupConditionalTemplateRootKind.UnsupportedDeferredTemplate,
            };
            supported = kind != MarkupConditionalTemplateRootKind.UnsupportedDeferredTemplate &&
                SymbolEqualityComparer.Default.Equals(resultType, controlType);
        }
        else if (templateType != null)
        {
            // A concrete DataTemplate property cannot receive the host-aware IDataTemplate adapter.
            supported = Compilation.CSharpCompilation.ClassifyConversion(templateType, destination.Type).IsImplicit;
        }

        var rootModel = new MarkupContentModel(model.ContentProperty,
            new CSharpSymbolDefinition(resultType), isCollection: false, allowsText: false,
            contentParameter: model.ContentParameter);
        return new(boundary, destination, resultType, rootModel, kind, implicitRoot, supported);
    }

    private (bool HasControl, bool HasTemplate) GetConditionalTemplateRootKinds(
        Akbura.Language.Syntax.SyntaxList<MarkupContentSyntax> content,
        ITypeSymbol controlType, ITypeSymbol? templateType, bool resolveExpressionRootTypes)
    {
        var hasControl = false;
        var hasTemplate = false;
        foreach (var child in content)
        {
            switch (child)
            {
                case MarkupElementContentSyntax element:
                    if (TryGetMarkupElementReferenceType(element.Element, out var reference) &&
                        reference.Symbol is ITypeSymbol type)
                    {
                        AddType(type);
                    }
                    break;
                case MarkupInlineExpressionSyntax expression when resolveExpressionRootTypes:
                    if (ParseInlineExpression(expression.Expression) is { } parsed)
                    {
                        var binding = BindMarkupAttributeExpression(expression, parsed, targetType: null);
                        if ((binding.TypeSymbol ?? binding.Conversion.SourceType ?? binding.OperationDefinition.Type) is { } expressionType)
                        {
                            AddType(expressionType);
                        }
                    }
                    break;
                case MarkupIfStatementSyntax conditional:
                    AddContent(conditional.Body.Content);
                    foreach (var clause in conditional.ElseIfClauses)
                    {
                        AddContent(clause.Body.Content);
                    }
                    if (conditional.ElseClause is { } finalClause)
                    {
                        AddContent(finalClause.Body.Content);
                    }
                    break;
            }
        }

        return (hasControl, hasTemplate);

        void AddContent(Akbura.Language.Syntax.SyntaxList<MarkupContentSyntax> nested)
        {
            var kinds = GetConditionalTemplateRootKinds(nested, controlType, templateType, resolveExpressionRootTypes);
            hasControl |= kinds.HasControl;
            hasTemplate |= kinds.HasTemplate;
        }

        void AddType(ITypeSymbol type)
        {
            hasControl |= Compilation.CSharpCompilation.ClassifyConversion(type, controlType).IsImplicit;
            hasTemplate |= templateType != null &&
                Compilation.CSharpCompilation.ClassifyConversion(type, templateType).IsImplicit;
        }
    }
}
