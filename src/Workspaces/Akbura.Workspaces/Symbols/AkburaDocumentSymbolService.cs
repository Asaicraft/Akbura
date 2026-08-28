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
        IEnumerable<TMember> members,
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
                    builder.Add(CreateLeaf(
                        styleName,
                        "style",
                        AkburaWorkspaceSymbolKind.Class,
                        style,
                        styleSelection));
                    break;

                case AkcssUtilitiesSectionSyntax utilities:
                    using (var children =
                        ImmutableArrayBuilder<AkburaDocumentSymbol>.Rent())
                    {
                        foreach (var utility in utilities.Utilities)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            children.Add(CreateUtility(utility));
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

    private static AkburaDocumentSymbol CreateUtility(
        AkcssUtilityDeclarationSyntax utility)
    {
        var name = utility.Selector.Name.Identifier.ValueText;
        return CreateLeaf(
            name,
            "utility",
            AkburaWorkspaceSymbolKind.Function,
            utility,
            utility.Selector.Name.Span);
    }

    private static AkburaDocumentSymbol CreateMarkupElement(
        MarkupElementSyntax element,
        CancellationToken cancellationToken)
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
                    cancellationToken));
            }
        }

        return new AkburaDocumentSymbol(
            startTag.Name.ToString().Trim(),
            "markup",
            AkburaWorkspaceSymbolKind.Object,
            element.Span,
            startTag.Name.Span,
            children.ToImmutable());
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
