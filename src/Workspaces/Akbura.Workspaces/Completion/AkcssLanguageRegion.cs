using Akbura.Language;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Akbura.Workspaces.Completion;

internal enum AkcssLanguageRegionKind
{
    StandaloneDocument,
    InlineBlock,
}

internal readonly struct AkcssLanguageRegion
{
    private AkcssLanguageRegion(
        AkcssLanguageRegionKind kind,
        AkburaSyntax root,
        TextSpan membersSpan,
        int importInsertionPosition)
    {
        Kind = kind;
        Root = root;
        MembersSpan = membersSpan;
        ImportInsertionPosition = importInsertionPosition;
    }

    public AkcssLanguageRegionKind Kind { get; }

    public AkburaSyntax? Root { get; }

    public TextSpan MembersSpan { get; }

    public int ImportInsertionPosition { get; }

    public bool IsDefault => Root == null;

    public IEnumerable<AkcssTopLevelMemberSyntax> GetMembers()
    {
        switch (Root)
        {
            case AkcssDocumentSyntax document:
                foreach (var member in document.Members)
                {
                    yield return member;
                }

                break;

            case InlineAkcssBlockSyntax inlineBlock:
                foreach (var member in inlineBlock.Members)
                {
                    yield return member;
                }

                break;
        }
    }

    public static bool TryCreate(
        AkburaSyntaxTree syntaxTree,
        SourceText text,
        int position,
        out AkcssLanguageRegion region)
    {
        if (syntaxTree == null)
        {
            throw new ArgumentNullException(nameof(syntaxTree));
        }

        if (text == null)
        {
            throw new ArgumentNullException(nameof(text));
        }

        if ((uint)position > (uint)text.Length)
        {
            region = default;
            return false;
        }

        var root = syntaxTree.GetRootSyntax();
        if (root is AkcssDocumentSyntax akcssDocument)
        {
            var membersSpan = new TextSpan(0, text.Length);
            region = new AkcssLanguageRegion(
                AkcssLanguageRegionKind.StandaloneDocument,
                akcssDocument,
                membersSpan,
                GetImportInsertionPosition(
                    akcssDocument.Members,
                    membersSpan.Start));
            return true;
        }

        InlineAkcssBlockSyntax? inlineBlock = null;
        foreach (var node in root.DescendantNodesAndSelf())
        {
            if (node is not InlineAkcssBlockSyntax candidate ||
                !IsInside(
                    candidate,
                    position,
                    text.Length))
            {
                continue;
            }

            if (inlineBlock == null ||
                candidate.FullSpan.Length <
                    inlineBlock.FullSpan.Length ||
                candidate.FullSpan.Length ==
                    inlineBlock.FullSpan.Length &&
                candidate.FullSpan.Start >
                    inlineBlock.FullSpan.Start)
            {
                inlineBlock = candidate;
            }
        }
        if (inlineBlock == null)
        {
            region = default;
            return false;
        }

        var membersStart = Math.Min(
            text.Length,
            inlineBlock.OpenBrace.Span.End);
        var membersEnd = inlineBlock.CloseBrace.IsMissing
            ? Math.Min(
                text.Length,
                Math.Max(membersStart, inlineBlock.FullSpan.End))
            : Math.Min(
                text.Length,
                Math.Max(membersStart, inlineBlock.CloseBrace.Span.Start));
        var inlineMembersSpan = TextSpan.FromBounds(
            membersStart,
            membersEnd);
        region = new AkcssLanguageRegion(
            AkcssLanguageRegionKind.InlineBlock,
            inlineBlock,
            inlineMembersSpan,
            GetImportInsertionPosition(
                inlineBlock.Members,
                inlineMembersSpan.Start));
        return true;
    }

    private static bool IsInside(
        InlineAkcssBlockSyntax inlineBlock,
        int position,
        int textLength)
    {
        if (inlineBlock.OpenBrace.IsMissing ||
            position < inlineBlock.OpenBrace.Span.End)
        {
            return false;
        }

        var end = inlineBlock.CloseBrace.IsMissing
            ? Math.Min(textLength, inlineBlock.FullSpan.End)
            : inlineBlock.CloseBrace.Span.Start;
        return position <= Math.Max(
            inlineBlock.OpenBrace.Span.End,
            end);
    }

    private static int GetImportInsertionPosition(
        SyntaxList<AkcssTopLevelMemberSyntax> members,
        int emptyPosition)
    {
        AkcssUsingDirectiveSyntax? lastCSharpUsing = null;
        AkcssUsingDirectiveSyntax? firstModuleImport = null;
        AkcssTopLevelMemberSyntax? firstDeclaration = null;

        foreach (var member in members)
        {
            if (member is AkcssUsingDirectiveSyntax usingDirective)
            {
                if (usingDirective.IsAkcssModuleImport)
                {
                    firstModuleImport ??= usingDirective;
                }
                else
                {
                    lastCSharpUsing = usingDirective;
                }

                continue;
            }

            firstDeclaration ??= member;
        }

        return lastCSharpUsing?.Span.End ??
            firstModuleImport?.FullSpan.Start ??
            firstDeclaration?.FullSpan.Start ??
            emptyPosition;
    }
}
