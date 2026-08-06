using Akbura.Language;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;

using AkburaSymbol =
    Akbura.Language.Symbols.ISymbol;

using RoslynSymbol =
    Microsoft.CodeAnalysis.ISymbol;

namespace Akbura.Workspaces;

internal sealed class AkburaDefinitionService : IAkburaDefinitionService
{
    public AkburaDefinition? GetDefinition(
        AkburaDocumentContext context,
        int position,
        CancellationToken cancellationToken = default)
    {
        if (context == null)
        {
            throw new ArgumentNullException(
                nameof(context));
        }

        var document =
            context.Document;

        if ((uint)position >=
            (uint)document.Text.Length)
        {
            return null;
        }

        cancellationToken
            .ThrowIfCancellationRequested();

        var root =
            document.SyntaxTree.GetRoot();

        var token =
            root.FindTokenInternal(
                position);

        var semanticModel =
            context.Project.Compilation
                .GetSemanticModel(
                    document.SyntaxTree);

        for (var node = token.Parent;
             node != null;
             node = node.Parent)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            switch (node)
            {
                case CSharpStatementSyntax statement:
                    return GetCSharpDefinition(
                        context,
                        semanticModel.GetCSharpSymbolReferences(
                            statement),
                        position,
                        cancellationToken);

                case InlineExpressionSyntax expression:
                    return GetCSharpDefinition(
                        context,
                        semanticModel.GetCSharpSymbolReferences(
                            expression),
                        position,
                        cancellationToken);

                case MarkupElementSyntax element:
                    {
                        var definition =
                            GetMarkupComponentDefinition(
                                context,
                                semanticModel,
                                element,
                                position,
                                cancellationToken);

                        if (definition != null)
                        {
                            return definition;
                        }

                        break;
                    }
            }
        }

        return null;
    }

    private static AkburaDefinition? GetCSharpDefinition(
        AkburaDocumentContext context,
        ImmutableArray<CSharpSymbolReference> references,
        int position,
        CancellationToken cancellationToken)
    {
        CSharpSymbolReference bestReference =
            default;

        var hasReference = false;

        foreach (var reference in references)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            if (!reference.SourceSpan.Contains(
                    position))
            {
                continue;
            }

            if (!hasReference ||
                reference.SourceSpan.Length <
                bestReference.SourceSpan.Length)
            {
                bestReference = reference;
                hasReference = true;
            }
        }

        if (!hasReference)
        {
            return null;
        }

        return CreateDefinition(
            context,
            bestReference.SourceSpan,
            bestReference.AkburaSymbol,
            bestReference.CSharpDefinition.Symbol,
            cancellationToken);
    }

    private static AkburaDefinition? GetMarkupComponentDefinition(
            AkburaDocumentContext context,
            AkburaSemanticModel semanticModel,
            MarkupElementSyntax element,
            int position,
            CancellationToken cancellationToken)
    {
        var sourceSpan =
            GetMarkupComponentSourceSpan(
                element,
                position);

        if (sourceSpan == null)
        {
            return null;
        }

        var symbol =
            semanticModel
                .GetSymbolInfo(element)
                .Symbol;

        if (symbol == null)
        {
            return null;
        }

        return CreateDefinition(
            context,
            sourceSpan.Value,
            symbol,
            symbol.CSharpDefinition.Symbol,
            cancellationToken);
    }

    private static TextSpan? GetMarkupComponentSourceSpan(
            MarkupElementSyntax element,
            int position)
    {
        var startName =
            element.StartTag?.Name;

        if (startName != null &&
            startName.Span.Contains(position))
        {
            return startName.Span;
        }

        var endName =
            element.EndTag?.Name;

        if (endName != null &&
            endName.Span.Contains(position))
        {
            return endName.Span;
        }

        return null;
    }

    private static AkburaDefinition? CreateDefinition(
            AkburaDocumentContext context,
            TextSpan sourceSpan,
            AkburaSymbol? akburaSymbol,
            RoslynSymbol? csharpSymbol,
            CancellationToken cancellationToken)
    {
        if (akburaSymbol != null &&
            TryCreateAkburaDefinition(
                context,
                sourceSpan,
                akburaSymbol,
                cancellationToken,
                out var akburaDefinition))
        {
            return akburaDefinition;
        }

        if (csharpSymbol != null &&
            TryCreateCSharpDefinition(
                sourceSpan,
                csharpSymbol,
                out var csharpDefinition))
        {
            return csharpDefinition;
        }

        if (akburaSymbol?.CSharpDefinition.Symbol is
            { } underlyingSymbol &&
            TryCreateCSharpDefinition(
                sourceSpan,
                underlyingSymbol,
                out var underlyingDefinition))
        {
            return underlyingDefinition;
        }

        return null;
    }

    private static bool TryCreateAkburaDefinition(
        AkburaDocumentContext context,
        TextSpan sourceSpan,
        AkburaSymbol symbol,
        CancellationToken cancellationToken,
        out AkburaDefinition definition)
    {
        foreach (var reference in
                 symbol.DeclaringSyntaxReferences)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            var declaration =
                reference.GetSyntax(
                    cancellationToken);

            if (!TryFindDocument(
                    context.Project,
                    declaration,
                    out var document))
            {
                continue;
            }

            var targetSpan =
                reference.Span.Length > 0
                    ? reference.Span
                    : GetDeclarationNameSpan(
                        declaration);

            definition =
                CreateAkburaDefinition(
                    sourceSpan,
                    document,
                    targetSpan);

            return true;
        }

        var declarationSyntax =
            GetDeclarationSyntax(symbol);

        if (declarationSyntax != null &&
            TryFindDocument(
                context.Project,
                declarationSyntax,
                out var declarationDocument))
        {
            definition =
                CreateAkburaDefinition(
                    sourceSpan,
                    declarationDocument,
                    GetDeclarationNameSpan(
                        declarationSyntax));

            return true;
        }

        definition = null!;
        return false;
    }

    private static AkburaSyntax? GetDeclarationSyntax(AkburaSymbol symbol)
    {
        return symbol switch
        {
            IStateSymbol state =>
                state.DeclarationSyntax,

            IParamSymbol parameter =>
                parameter.DeclarationSyntax,

            IInjectSymbol inject =>
                inject.DeclarationSyntax,

            ICommandSymbol command =>
                command.DeclarationSyntax,

            IAkburaComponentSymbol component =>
                component.DeclarationSyntax,

            CSharpLocalSymbol local =>
                local.DeclarationSyntax,

            _ => null,
        };
    }

    private static TextSpan GetDeclarationNameSpan(AkburaSyntax declaration)
    {
        return declaration switch
        {
            StateDeclarationSyntax state =>
                state.Name.Span,

            ParamDeclarationSyntax parameter =>
                parameter.Name.Span,

            InjectDeclarationSyntax inject =>
                inject.Name.Span,

            CommandDeclarationSyntax command =>
                command.Name.Span,

            AkburaDocumentSyntax =>
                new TextSpan(
                    start: 0,
                    length: 0),

            _ => declaration.Span,
        };
    }

    private static bool TryFindDocument(
        AkburaProjectSnapshot project,
        AkburaSyntax syntax,
        out AkburaDocumentSnapshot document)
    {
        var root =
            syntax.Root;

        foreach (var candidate in
                 project.Documents.Values)
        {
            if (ReferenceEquals(
                    candidate.SyntaxTree
                        .GetRootSyntax(),
                    root))
            {
                document = candidate;
                return true;
            }
        }

        document = null!;
        return false;
    }

    private static AkburaDefinition
        CreateAkburaDefinition(
            TextSpan sourceSpan,
            AkburaDocumentSnapshot document,
            TextSpan targetSpan)
    {
        var safeTargetSpan =
            ClampSpan(
                targetSpan,
                document.Text.Length);

        var lineSpan =
            document.Text.Lines
                .GetLinePositionSpan(
                    safeTargetSpan);

        return new AkburaDefinition(
            sourceSpan,
            document.FilePath,
            lineSpan);
    }

    private static bool TryCreateCSharpDefinition(
        TextSpan sourceSpan,
        RoslynSymbol symbol,
        out AkburaDefinition definition)
    {
        symbol = GetNavigationSymbol(
            symbol);

        foreach (var location in symbol.Locations)
        {
            if (!location.IsInSource)
            {
                continue;
            }

            var lineSpan =
                location.GetLineSpan();

            var filePath =
                lineSpan.Path;

            if (string.IsNullOrWhiteSpace(
                    filePath))
            {
                filePath =
                    location.SourceTree
                        ?.FilePath;
            }

            if (string.IsNullOrWhiteSpace(
                    filePath))
            {
                continue;
            }

            definition =
                new AkburaDefinition(
                    sourceSpan,
                    filePath,
                    lineSpan.Span);

            return true;
        }

        definition = null!;
        return false;
    }

    private static RoslynSymbol
        GetNavigationSymbol(
            RoslynSymbol symbol)
    {
        if (symbol is IAliasSymbol alias)
        {
            symbol = alias.Target;
        }

        if (symbol is IMethodSymbol
            {
                ReducedFrom: not null
            } method)
        {
            symbol = method.ReducedFrom;
        }

        return symbol.OriginalDefinition;
    }

    private static TextSpan ClampSpan(
        TextSpan span,
        int textLength)
    {
        var start =
            Math.Max(
                0,
                Math.Min(
                    span.Start,
                    textLength));

        var end =
            Math.Max(
                start,
                Math.Min(
                    span.End,
                    textLength));

        return TextSpan.FromBounds(
            start,
            end);
    }
}