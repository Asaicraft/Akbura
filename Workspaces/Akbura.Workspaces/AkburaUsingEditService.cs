using Akbura.Language;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using System.Text;
using CSharpSyntaxFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Akbura.Workspaces;

internal static class AkburaUsingEditService
{
    public static AkburaCSharpImportContext CreateImportContext(
        AkburaSyntacticDocument document)
    {
        if (document == null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        return CreateImportContext(
            document.Text,
            document.SyntaxTree);
    }

    public static AkburaCSharpImportContext CreateImportContext(
        AkburaSyntacticDocument document,
        int position)
    {
        if (document == null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        return CreateImportContext(
            document.Text,
            document.SyntaxTree,
            position);
    }

    public static AkburaCSharpImportContext CreateImportContext(
        SourceText text,
        AkburaSyntaxTree syntaxTree)
    {
        ValidateArguments(text, syntaxTree);

        var root = syntaxTree.GetRootSyntax();
        if (root is AkcssDocumentSyntax &&
            AkcssLanguageRegion.TryCreate(
                syntaxTree,
                text,
                position: 0,
                out var region))
        {
            return CreateAkcssImportContext(
                text,
                root,
                region);
        }

        return CreateComponentImportContext(
            text,
            root);
    }

    public static AkburaCSharpImportContext CreateImportContext(
        SourceText text,
        AkburaSyntaxTree syntaxTree,
        int position)
    {
        ValidateArguments(text, syntaxTree);

        if ((uint)position > (uint)text.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(position));
        }

        var root = syntaxTree.GetRootSyntax();
        return AkcssLanguageRegion.TryCreate(
            syntaxTree,
            text,
            position,
            out var region)
            ? CreateAkcssImportContext(
                text,
                root,
                region)
            : CreateComponentImportContext(
                text,
                root);
    }

    public static bool TryCreateNamespaceImportChange(
        SourceText text,
        AkburaSyntaxTree syntaxTree,
        string namespaceName,
        out TextChange change)
    {
        return TryCreateNamespaceImportChangeCore(
            text,
            syntaxTree,
            namespaceName,
            position: null,
            out change);
    }

    public static bool TryCreateNamespaceImportChange(
        SourceText text,
        AkburaSyntaxTree syntaxTree,
        string namespaceName,
        int position,
        out TextChange change)
    {
        return TryCreateNamespaceImportChangeCore(
            text,
            syntaxTree,
            namespaceName,
            position,
            out change);
    }

    public static string CreateUsingText(
        ReadOnlySpan<CSharpUsingKey> imports,
        AkburaCSharpImportContext context)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var builder = new StringBuilder();
        if (context.NeedsLeadingLineBreak)
        {
            AppendLineBreakAndIndentation(
                builder,
                context);
        }
        else
        {
            builder.Append(context.Indentation);
        }

        for (var i = 0; i < imports.Length; i++)
        {
            if (i != 0)
            {
                AppendLineBreakAndIndentation(
                    builder,
                    context);
            }

            builder.Append(context.SyntaxKind ==
                    AkburaCSharpImportSyntaxKind.Component
                ? "using "
                : "@using ");
            builder.Append(imports[i].Name);
            builder.Append(';');
        }

        if (context.NeedsTrailingLineBreak)
        {
            AppendLineBreakAndIndentation(
                builder,
                context);
        }

        return builder.ToString();
    }

    private static bool TryCreateNamespaceImportChangeCore(
        SourceText text,
        AkburaSyntaxTree syntaxTree,
        string namespaceName,
        int? position,
        out TextChange change)
    {
        change = default;
        ValidateArguments(text, syntaxTree);

        if (string.IsNullOrWhiteSpace(namespaceName))
        {
            return false;
        }

        var parsedName = CSharpSyntaxFactory.ParseName(namespaceName);
        if (parsedName.ContainsDiagnostics)
        {
            return false;
        }

        var key = CSharpUsingKey.Create(
            CSharpSyntaxFactory.UsingDirective(parsedName));
        var context = position is int hostPosition
            ? CreateImportContext(
                text,
                syntaxTree,
                hostPosition)
            : CreateImportContext(
                text,
                syntaxTree);
        if (context.ExistingImports.Contains(key) ||
            (uint)context.InsertionPosition > (uint)text.Length)
        {
            return false;
        }

        change = new TextChange(
            new TextSpan(context.InsertionPosition, 0),
            CreateUsingText([key], context));
        return true;
    }

    private static AkburaCSharpImportContext CreateComponentImportContext(
        SourceText text,
        AkburaSyntax root)
    {
        using var ordinaryUsings =
            ImmutableArrayBuilder<UsingDirectiveSyntax>.Rent();
        using var globalUsings =
            ImmutableArrayBuilder<UsingDirectiveSyntax>.Rent();
        var existingImports =
            ImmutableHashSet.CreateBuilder<CSharpUsingKey>();
        NamespaceDeclarationSyntax? namespaceDeclaration = null;
        AkburaSyntax? firstTopLevelMember = null;

        if (root is AkburaDocumentSyntax documentRoot)
        {
            foreach (var member in documentRoot.Members)
            {
                firstTopLevelMember ??= member;
                if (member is NamespaceDeclarationSyntax currentNamespace)
                {
                    namespaceDeclaration ??= currentNamespace;
                    continue;
                }

                if (member is not UsingDirectiveSyntax usingDirective ||
                    IsAkcssUsingDirective(usingDirective))
                {
                    continue;
                }

                if (!TryAddImport(
                        usingDirective,
                        existingImports))
                {
                    continue;
                }

                if (usingDirective.GlobalKeyword.RawKind != 0)
                {
                    globalUsings.Add(usingDirective);
                }
                else
                {
                    ordinaryUsings.Add(usingDirective);
                }
            }
        }

        var insertionPosition = ordinaryUsings.Count != 0
            ? ordinaryUsings.WrittenSpan[^1].Span.End
            : globalUsings.Count != 0
                ? globalUsings.WrittenSpan[^1].Span.End
                : namespaceDeclaration != null
                    ? namespaceDeclaration.Span.End
                    : firstTopLevelMember?.FullSpan.Start ?? 0;

        return CreateContext(
            AkburaCSharpImportSyntaxKind.Component,
            text,
            insertionPosition,
            indentation: string.Empty,
            existingImports.ToImmutable());
    }

    private static AkburaCSharpImportContext CreateAkcssImportContext(
        SourceText text,
        AkburaSyntax syntaxRoot,
        AkcssLanguageRegion region)
    {
        var existingImports =
            ImmutableHashSet.CreateBuilder<CSharpUsingKey>();

        foreach (var member in region.GetMembers())
        {
            if (member is AkcssUsingDirectiveSyntax usingDirective &&
                !usingDirective.IsAkcssModuleImport)
            {
                TryAddImport(
                    usingDirective,
                    existingImports);
            }
        }

        if (region.Kind == AkcssLanguageRegionKind.InlineBlock &&
            syntaxRoot is AkburaDocumentSyntax documentRoot)
        {
            foreach (var member in documentRoot.Members)
            {
                if (member is UsingDirectiveSyntax usingDirective &&
                    !IsAkcssUsingDirective(usingDirective))
                {
                    TryAddImport(
                        usingDirective,
                        existingImports);
                }
            }
        }

        return CreateContext(
            region.Kind == AkcssLanguageRegionKind.StandaloneDocument
                ? AkburaCSharpImportSyntaxKind.AkcssDocument
                : AkburaCSharpImportSyntaxKind.InlineAkcssBlock,
            text,
            region.ImportInsertionPosition,
            DetectAkcssIndentation(
                text,
                region),
            existingImports.ToImmutable());
    }

    private static AkburaCSharpImportContext CreateContext(
        AkburaCSharpImportSyntaxKind syntaxKind,
        SourceText text,
        int insertionPosition,
        string indentation,
        ImmutableHashSet<CSharpUsingKey> existingImports)
    {
        var needsLeadingLineBreak = insertionPosition > 0 &&
            text[insertionPosition - 1] != '\r' &&
            text[insertionPosition - 1] != '\n';
        var needsTrailingLineBreak = insertionPosition < text.Length &&
            text[insertionPosition] != '\r' &&
            text[insertionPosition] != '\n';

        return new AkburaCSharpImportContext(
            syntaxKind,
            insertionPosition,
            DetectNewLine(text),
            indentation,
            needsLeadingLineBreak,
            needsTrailingLineBreak,
            existingImports);
    }

    private static string DetectAkcssIndentation(
        SourceText text,
        AkcssLanguageRegion region)
    {
        if (region.Kind == AkcssLanguageRegionKind.StandaloneDocument)
        {
            return string.Empty;
        }

        foreach (var member in region.GetMembers())
        {
            var indentation = TryGetLineIndentation(
                text,
                member.Span.Start);
            if (!string.IsNullOrEmpty(indentation))
            {
                return indentation!;
            }
        }

        if (region.Root is not InlineAkcssBlockSyntax inlineBlock)
        {
            return string.Empty;
        }

        var parentIndentation = TryGetLineIndentation(
                text,
                inlineBlock.AtToken.Span.Start) ??
            string.Empty;
        return parentIndentation +
            (parentIndentation.Contains('\t')
                ? "\t"
                : "    ");
    }

    private static string? TryGetLineIndentation(
        SourceText text,
        int position)
    {
        position = Math.Min(
            Math.Max(position, 0),
            text.Length);
        var line = text.Lines.GetLineFromPosition(position);
        var prefix = text.ToString(
            TextSpan.FromBounds(
                line.Start,
                position));

        for (var i = 0; i < prefix.Length; i++)
        {
            if (prefix[i] is not (' ' or '\t'))
            {
                return null;
            }
        }

        return prefix;
    }

    private static bool TryAddImport(
        UsingDirectiveSyntax usingDirective,
        ImmutableHashSet<CSharpUsingKey>.Builder imports)
    {
        try
        {
            imports.Add(CSharpUsingKey.Create(
                usingDirective.ToCSharp()));
            return true;
        }
        catch (Exception exception)
            when (exception is InvalidOperationException or
                  ArgumentException or InvalidCastException)
        {
            return false;
        }
    }

    private static bool TryAddImport(
        AkcssUsingDirectiveSyntax usingDirective,
        ImmutableHashSet<CSharpUsingKey>.Builder imports)
    {
        try
        {
            imports.Add(CSharpUsingKey.Create(
                usingDirective.ToCSharp()));
            return true;
        }
        catch (Exception exception)
            when (exception is InvalidOperationException or
                  ArgumentException or InvalidCastException)
        {
            return false;
        }
    }

    private static void AppendLineBreakAndIndentation(
        StringBuilder builder,
        AkburaCSharpImportContext context)
    {
        builder.Append(context.NewLine);
        builder.Append(context.Indentation);
    }

    private static void ValidateArguments(
        SourceText text,
        AkburaSyntaxTree syntaxTree)
    {
        if (text == null)
        {
            throw new ArgumentNullException(nameof(text));
        }

        if (syntaxTree == null)
        {
            throw new ArgumentNullException(nameof(syntaxTree));
        }
    }

    private static string DetectNewLine(SourceText text)
    {
        foreach (var line in text.Lines)
        {
            var lineBreakSpan = TextSpan.FromBounds(
                line.End,
                line.EndIncludingLineBreak);
            if (lineBreakSpan.Length != 0)
            {
                return text.ToString(lineBreakSpan);
            }
        }

        return Environment.NewLine;
    }

    public static bool IsAkcssUsingDirective(
        UsingDirectiveSyntax usingDirective)
    {
        return usingDirective.Alias == null &&
            usingDirective.StaticKeyword.RawKind == 0 &&
            usingDirective.Name.ToFullString()
                .Trim()
                .EndsWith(".akcss", StringComparison.Ordinal);
    }
}
