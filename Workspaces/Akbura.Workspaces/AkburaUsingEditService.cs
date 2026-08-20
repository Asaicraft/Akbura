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

        var root = syntaxTree.GetRootSyntax();
        return root is AkcssDocumentSyntax akcssRoot
            ? CreateAkcssImportContext(text, akcssRoot)
            : CreateComponentImportContext(text, root);
    }

    public static bool TryCreateNamespaceImportChange(
        SourceText text,
        AkburaSyntaxTree syntaxTree,
        string namespaceName,
        out TextChange change)
    {
        change = default;

        if (text == null)
        {
            throw new ArgumentNullException(nameof(text));
        }

        if (syntaxTree == null)
        {
            throw new ArgumentNullException(nameof(syntaxTree));
        }

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
        var context = CreateImportContext(text, syntaxTree);
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
            builder.Append(context.NewLine);
        }

        for (var i = 0; i < imports.Length; i++)
        {
            if (i != 0)
            {
                builder.Append(context.NewLine);
            }

            builder.Append(context.SyntaxKind ==
                    AkburaCSharpImportSyntaxKind.Akcss
                ? "@using "
                : "using ");
            builder.Append(imports[i].Name);
            builder.Append(';');
        }

        if (context.NeedsTrailingLineBreak)
        {
            builder.Append(context.NewLine);
        }

        return builder.ToString();
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

                try
                {
                    existingImports.Add(CSharpUsingKey.Create(
                        usingDirective.ToCSharp()));
                }
                catch (Exception exception)
                    when (exception is InvalidOperationException or
                          ArgumentException or InvalidCastException)
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
            existingImports.ToImmutable());
    }

    private static AkburaCSharpImportContext CreateAkcssImportContext(
        SourceText text,
        AkcssDocumentSyntax root)
    {
        using var csharpUsings =
            ImmutableArrayBuilder<AkcssUsingDirectiveSyntax>.Rent();
        AkcssUsingDirectiveSyntax? firstModuleImport = null;
        AkburaSyntax? firstDeclaration = null;
        var existingImports =
            ImmutableHashSet.CreateBuilder<CSharpUsingKey>();

        foreach (var member in root.Members)
        {
            if (member is AkcssUsingDirectiveSyntax usingDirective)
            {
                if (usingDirective.IsAkcssModuleImport)
                {
                    firstModuleImport ??= usingDirective;
                    continue;
                }

                try
                {
                    existingImports.Add(CSharpUsingKey.Create(
                        usingDirective.ToCSharp()));
                    csharpUsings.Add(usingDirective);
                }
                catch (Exception exception)
                    when (exception is InvalidOperationException or
                          ArgumentException or InvalidCastException)
                {
                }

                continue;
            }

            firstDeclaration ??= member;
        }

        var insertionPosition = csharpUsings.Count != 0
            ? csharpUsings.WrittenSpan[^1].Span.End
            : firstModuleImport?.FullSpan.Start ??
              firstDeclaration?.FullSpan.Start ??
              0;

        return CreateContext(
            AkburaCSharpImportSyntaxKind.Akcss,
            text,
            insertionPosition,
            existingImports.ToImmutable());
    }

    private static AkburaCSharpImportContext CreateContext(
        AkburaCSharpImportSyntaxKind syntaxKind,
        SourceText text,
        int insertionPosition,
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
            needsLeadingLineBreak,
            needsTrailingLineBreak,
            existingImports);
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
