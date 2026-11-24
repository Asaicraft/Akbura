using Akbura.Language;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;

namespace Akbura.Workspaces.Symbols;

internal sealed class AkburaDocumentSymbolService :
    IAkburaDocumentSymbolService
{
    public ImmutableArray<AkburaDocumentSymbol> GetSymbols(
        AkburaDocumentContext context,
        CancellationToken cancellationToken = default)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        return GetSymbols(
            AkburaSyntacticDocument.Create(
                context.Document,
                cancellationToken),
            cancellationToken);
    }

    public ImmutableArray<AkburaDocumentSymbol> GetSymbols(
        AkburaSyntacticDocument document,
        CancellationToken cancellationToken = default)
    {
        if (document == null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        using var builder =
            ImmutableArrayBuilder<AkburaDocumentSymbol>.Rent();
        var root = document.SyntaxTree.GetRootSyntax();

        switch (root)
        {
            case AkburaDocumentSyntax component:
                AddComponentSymbols(component, builder, cancellationToken);
                break;

            case AkcssDocumentSyntax stylesheet:
                AddAkcssMembers(
                    stylesheet.Members,
                    builder,
                    cancellationToken);
                break;
        }

        return builder.ToImmutable();
    }

    private static void AddComponentSymbols(
        AkburaDocumentSyntax document,
        ImmutableArrayBuilder<AkburaDocumentSymbol> builder,
        CancellationToken cancellationToken)
    {
        foreach (var member in document.Members)
        {
            cancellationToken.ThrowIfCancellationRequested();

            switch (member)
            {
                case NamespaceDeclarationSyntax declaration:
                    builder.Add(CreateLeaf(
                        declaration.Name.ToString(),
                        "namespace",
                        AkburaWorkspaceSymbolKind.Namespace,
                        declaration,
                        declaration.Name.Span));
                    break;

                case StateDeclarationSyntax declaration:
                    builder.Add(CreateLeaf(
                        declaration.Name.Identifier.ValueText,
                        declaration.Type?.ToString(),
                        AkburaWorkspaceSymbolKind.Field,
                        declaration,
                        declaration.Name.Span));
                    break;

                case ParamDeclarationSyntax declaration:
                    builder.Add(CreateLeaf(
                        declaration.Name.Identifier.ValueText,
                        declaration.Type?.ToString(),
                        AkburaWorkspaceSymbolKind.Property,
                        declaration,
                        declaration.Name.Span));
                    break;

                case InjectDeclarationSyntax declaration:
                    builder.Add(CreateLeaf(
                        declaration.Name.Identifier.ValueText,
                        declaration.Type.ToString(),
                        AkburaWorkspaceSymbolKind.Field,
                        declaration,
                        declaration.Name.Span));
                    break;

                case CommandDeclarationSyntax declaration:
                    builder.Add(CreateLeaf(
                        declaration.Name.Identifier.ValueText,
                        "command",
                        AkburaWorkspaceSymbolKind.Method,
                        declaration,
                        declaration.Name.Span));
                    break;

                case MarkupRootSyntax markup:
                    builder.Add(CreateMarkupElement(
                        markup.Element,
                        cancellationToken));
                    break;

                case InlineAkcssBlockSyntax inlineAkcss:
                    using (var children =
                        ImmutableArrayBuilder<AkburaDocumentSymbol>.Rent())
                    {
                        AddAkcssMembers(
                            inlineAkcss.Members,
                            children,
                            cancellationToken);
                        builder.Add(new AkburaDocumentSymbol(
                            "@akcss",
                            "inline stylesheet",
                            AkburaWorkspaceSymbolKind.Module,
                            inlineAkcss.Span,
                            inlineAkcss.AkcssKeyword.Span,
                            children.ToImmutable()));
                    }
                    break;
            }
        }
    }

    private static void AddAkcssMembers<TMember>(
        SyntaxList<TMember> members,
        ImmutableArrayBuilder<AkburaDocumentSymbol> builder,
        CancellationToken cancellationToken)
        where TMember : AkburaSyntax
    {
        foreach (var member in members)
        {
            cancellationToken.ThrowIfCancellationRequested();

            switch (member)
            {
                case AkcssStyleRuleSyntax style:
                    var styleName = style.Selector.ToString().Trim();
                    var styleSelection = style.Selector.Name?.Span ??
                        style.Selector.TargetType?.Span ??
                        style.Selector.Span;
                    builder.Add(CreateAkcssContainer(styleName, "style", AkburaWorkspaceSymbolKind.Class,
                        style, styleSelection, style.Members, cancellationToken));
                    break;

                case AkcssThemeDeclarationSyntax theme:
                    builder.Add(CreateAkcssContainer(theme.Name.Identifier.ValueText, "theme " + theme.TargetType.ToString().Trim(),
                        AkburaWorkspaceSymbolKind.Class, theme, theme.Name.Span, theme.Members, cancellationToken));
                    break;

                case AkcssMixinDeclarationSyntax mixin:
                    builder.Add(CreateAkcssContainer(mixin.Name.Identifier.ValueText,
                        "mixin " + mixin.TargetType.ToString().Trim() + " " + mixin.Parameters.ToString().Trim(),
                        AkburaWorkspaceSymbolKind.Function, mixin, mixin.Name.Span, mixin.Members, cancellationToken));
                    break;

                case AkcssSelectorRuleSyntax rule:
                    builder.Add(CreateAkcssContainer(rule.Selector.ToString().Trim(), "selector", AkburaWorkspaceSymbolKind.Class,
                        rule, rule.Selector.Span, rule.Members, cancellationToken));
                    break;

                case AkcssNestedStyleRuleSyntax nested:
                    builder.Add(CreateAkcssContainer(nested.Selector.ToString().Trim(), "nested selector", AkburaWorkspaceSymbolKind.Class,
                        nested, nested.Selector.Span, nested.Members, cancellationToken));
                    break;

                case AkcssIfDirectiveSyntax condition:
                    AddAkcssMembers(condition.Members, builder, cancellationToken);
                    break;

                case AkcssTemplateDirectiveSyntax template:
                    var elements = ArrayBuilder<AkburaDocumentSymbol>.GetInstance();
                    try
                    {
                        foreach (var content in template.Body.Content)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            if (content is MarkupElementContentSyntax child)
                            {
                                elements.Add(CreateMarkupElement(child.Element, cancellationToken, isTemplate: true));
                            }
                        }

                        builder.Add(new AkburaDocumentSymbol("@template", "control template", AkburaWorkspaceSymbolKind.Object,
                            template.Span, template.TemplateKeyword.Span, elements.ToImmutable()));
                    }
                    finally
                    {
                        elements.Free();
                    }

                    break;

                case AkcssUtilitiesSectionSyntax utilities:
                    using (var children =
                        ImmutableArrayBuilder<AkburaDocumentSymbol>.Rent())
                    {
                        foreach (var utility in utilities.Utilities)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            children.Add(CreateUtility(utility, cancellationToken));
                        }

                        builder.Add(new AkburaDocumentSymbol(
                            "@utilities",
                            null,
                            AkburaWorkspaceSymbolKind.Module,
                            utilities.Span,
                            utilities.UtilitiesToken.Span,
                            children.ToImmutable()));
                    }
                    break;
            }
        }
    }

    private static AkburaDocumentSymbol CreateUtility(AkcssUtilityDeclarationSyntax utility, CancellationToken cancellationToken) =>
        CreateAkcssContainer(utility.Selector.Name.Identifier.ValueText, "utility", AkburaWorkspaceSymbolKind.Function,
            utility, utility.Selector.Name.Span, utility.Members, cancellationToken);

    private static AkburaDocumentSymbol CreateAkcssContainer(string name, string detail, AkburaWorkspaceSymbolKind kind,
        AkburaSyntax node, TextSpan selection, SyntaxList<AkcssBodyMemberSyntax> members, CancellationToken cancellationToken)
    {
        using var children = ImmutableArrayBuilder<AkburaDocumentSymbol>.Rent();
        AddAkcssMembers(members, children, cancellationToken);
        return new AkburaDocumentSymbol(name, detail, kind, node.Span, selection, children.ToImmutable());
    }

    private static AkburaDocumentSymbol CreateMarkupElement(
        MarkupElementSyntax element,
        CancellationToken cancellationToken,
        bool isTemplate = false)
    {
        var startTag = element.StartTag;
        if (startTag == null)
        {
            return new AkburaDocumentSymbol(
                "<incomplete>",
                "markup",
                AkburaWorkspaceSymbolKind.Object,
                element.Span,
                element.Span);
        }

        using var children =
            ImmutableArrayBuilder<AkburaDocumentSymbol>.Rent();
        foreach (var content in element.Body)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (content is MarkupElementContentSyntax child)
            {
                children.Add(CreateMarkupElement(
                    child.Element,
                    cancellationToken,
                    isTemplate));
            }
        }

        var name = startTag.Name.ToString().Trim();
        var selection = startTag.Name.Span;
        var detail = "markup";
        if (isTemplate && startTag.Attributes.FirstOrDefault(AkburaSemanticModel.IsMarkupNameDirective) is
                MarkupAttachedPropertyAttributeSyntax { Value: MarkupLiteralAttributeValueSyntax literal })
        {
            detail = name;
            name = AkburaSemanticModel.GetMarkupLiteralAttributeValueText(literal);
            selection = literal.Span.Length >= 2
                ? new TextSpan(literal.Span.Start + 1, literal.Span.Length - 2) : literal.Span;
        }

        return new AkburaDocumentSymbol(name, detail, AkburaWorkspaceSymbolKind.Object, element.Span, selection, children.ToImmutable());
    }

    private static AkburaDocumentSymbol CreateLeaf(
        string name,
        string? detail,
        AkburaWorkspaceSymbolKind kind,
        AkburaSyntax syntax,
        TextSpan selectionSpan)
    {
        return new AkburaDocumentSymbol(
            name,
            detail,
            kind,
            syntax.Span,
            selectionSpan);
    }
}
