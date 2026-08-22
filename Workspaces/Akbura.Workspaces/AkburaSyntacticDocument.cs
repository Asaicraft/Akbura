using Akbura.Language;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;

namespace Akbura.Workspaces;

/// <summary>
/// Represents one immutable, syntax-only Akbura document.
/// </summary>
public sealed partial class AkburaSyntacticDocument
{
    private readonly ImmutableArray<BlockBoundaries>
        _indentationBlocks;

    private AkburaSyntacticDocument(
        SourceText text,
        string filePath,
        AkburaSyntaxTree syntaxTree,
        ImmutableArray<AkburaOutliningRegion> outliningRegions,
        ImmutableArray<BlockBoundaries> indentationBlocks)
    {
        Text = text;
        FilePath = filePath;
        SyntaxTree = syntaxTree;
        OutliningRegions = outliningRegions;
        _indentationBlocks = indentationBlocks;
    }

    public SourceText Text { get; }

    public string FilePath { get; }

    public ImmutableArray<AkburaOutliningRegion> OutliningRegions { get; }

    internal AkburaSyntaxTree SyntaxTree { get; }

    /// <summary>
    /// Parses a document without creating a project or semantic model.
    /// </summary>
    public static AkburaSyntacticDocument Parse(
        SourceText text,
        string filePath = "",
        CancellationToken cancellationToken = default)
    {
        if (text == null)
        {
            throw new ArgumentNullException(
                nameof(text));
        }

        filePath ??= string.Empty;

        var syntaxTree =
            AkburaDocumentSnapshot.CreateSyntaxTree(
                text,
                filePath,
                rootNamespace: string.Empty,
                projectDirectory: string.Empty,
                cancellationToken);
        var outliningRegions =
            CreateSyntacticFacts(
                syntaxTree.GetRootSyntax(),
                text,
                out var indentationBlocks,
                cancellationToken);

        return new AkburaSyntacticDocument(
            text,
            filePath,
            syntaxTree,
            outliningRegions,
            indentationBlocks);
    }

    /// <summary>
    /// Returns the structural indentation level for the specified line.
    /// </summary>
    public int GetDesiredIndentationLevel(
        int lineNumber,
        CancellationToken cancellationToken = default)
    {
        if ((uint)lineNumber >=
            (uint)Text.Lines.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lineNumber));
        }

        var line = Text.Lines[lineNumber];
        var position = GetFirstNonWhitespacePosition(
            Text,
            line);
        return GetDesiredIndentationLevelAtPosition(
            position,
            cancellationToken);
    }

    private int GetDesiredIndentationLevelAtPosition(
        int position,
        CancellationToken cancellationToken)
    {
        var level = 0;
        foreach (var boundaries in _indentationBlocks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (boundaries.BodyStart <= position &&
                position < boundaries.CloseStart)
            {
                level++;
            }
        }

        return level;
    }

    private static ImmutableArray<AkburaOutliningRegion>
        CreateSyntacticFacts(
            AkburaSyntax root,
            SourceText text,
            out ImmutableArray<BlockBoundaries> indentationBlocks,
            CancellationToken cancellationToken)
    {
        using var builder =
            ImmutableArrayBuilder<AkburaOutliningRegion>.Rent();
        using var indentationBuilder =
            ImmutableArrayBuilder<BlockBoundaries>.Rent();

        foreach (var node in root.DescendantNodes())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!TryGetBlockBoundaries(
                    node,
                    text.Length,
                    out var boundaries))
            {
                continue;
            }

            indentationBuilder.Add(boundaries);

            if (!boundaries.CanOutline ||
                boundaries.OutlineSpan.End > text.Length)
            {
                continue;
            }

            var startLine = text.Lines.GetLineFromPosition(
                boundaries.OutlineSpan.Start);
            var endLine = text.Lines.GetLineFromPosition(
                Math.Max(
                    boundaries.OutlineSpan.Start,
                    boundaries.OutlineSpan.End - 1));
            if (startLine.LineNumber == endLine.LineNumber)
            {
                continue;
            }

            builder.Add(
                new AkburaOutliningRegion(
                    boundaries.OutlineSpan,
                    boundaries.CollapsedText));
        }

        indentationBlocks = indentationBuilder.AsEnumerable()
            .OrderBy(static boundaries =>
                boundaries.BodyStart)
            .ToImmutableArray();

        return builder.AsEnumerable()
            .OrderBy(static region => region.Span.Start)
            .ThenByDescending(static region => region.Span.Length)
            .ToImmutableArray();
    }

    private static int GetFirstNonWhitespacePosition(
        SourceText text,
        TextLine line)
    {
        var position = line.Start;
        while (position < line.End &&
               char.IsWhiteSpace(text[position]))
        {
            position++;
        }

        return position;
    }

    private static bool TryGetBlockBoundaries(
        AkburaSyntax node,
        int textLength,
        out BlockBoundaries boundaries)
    {
        if (node is MarkupElementSyntax markupElement &&
            markupElement.StartTag is { } startTag)
        {
            if (startTag.CloseToken.IsMissing ||
                startTag.CloseToken.Kind ==
                    SyntaxKind.SlashGreaterToken)
            {
                boundaries = default;
                return false;
            }

            var endTag = markupElement.EndTag;
            var hasEndTag = endTag != null &&
                !endTag.IsMissing &&
                endTag.Span.End > startTag.Span.End;
            var markupCloseStart = hasEndTag
                ? endTag!.Span.Start
                : GetOpenEndedClosePosition(textLength);
            var outlineSpan = hasEndTag
                ? TextSpan.FromBounds(
                    startTag.Span.End,
                    endTag!.Span.End)
                : default;

            boundaries = new BlockBoundaries(
                bodyStart: startTag.Span.End,
                closeStart: markupCloseStart,
                outlineSpan,
                collapsedText: "...",
                canOutline: hasEndTag);
            return true;
        }

        SyntaxToken openBrace;
        SyntaxToken closeBrace;

        switch (node)
        {
            case CSharpBlockSyntax block:
                openBrace = block.OpenBrace;
                closeBrace = block.CloseBrace;
                break;

            case InlineAkcssBlockSyntax block:
                openBrace = block.OpenBrace;
                closeBrace = block.CloseBrace;
                break;

            case AkcssUtilitiesSectionSyntax section:
                openBrace = section.OpenBrace;
                closeBrace = section.CloseBrace;
                break;

            case AkcssUtilityDeclarationSyntax utility:
                openBrace = utility.OpenBrace;
                closeBrace = utility.CloseBrace;
                break;

            case AkcssStyleRuleSyntax style:
                openBrace = style.OpenBrace;
                closeBrace = style.CloseBrace;
                break;

            case AkcssIfDirectiveSyntax ifDirective:
                openBrace = ifDirective.OpenBrace;
                closeBrace = ifDirective.CloseBrace;
                break;

            case AkcssPseudoBlockSyntax pseudoBlock:
                openBrace = pseudoBlock.OpenBrace;
                closeBrace = pseudoBlock.CloseBrace;
                break;

            default:
                boundaries = default;
                return false;
        }

        if (openBrace.IsMissing)
        {
            boundaries = default;
            return false;
        }

        var hasCloseBrace = !closeBrace.IsMissing &&
            openBrace.Span.End < node.Span.End;
        var closeEnd = hasCloseBrace
            ? node.Span.End
            : 0;
        var closeStart = hasCloseBrace
            ? Math.Max(
                openBrace.Span.End,
                closeEnd - Math.Max(
                    1,
                    closeBrace.Span.Length))
            : GetOpenEndedClosePosition(textLength);

        boundaries = new BlockBoundaries(
            bodyStart: openBrace.Span.End,
            closeStart,
            hasCloseBrace
                ? TextSpan.FromBounds(
                    openBrace.Span.Start,
                    closeEnd)
                : default,
            collapsedText: "{ ... }",
            canOutline: hasCloseBrace);
        return true;
    }

    private static int GetOpenEndedClosePosition(
        int textLength)
    {
        return textLength == int.MaxValue
            ? int.MaxValue
            : textLength + 1;
    }

    private readonly struct BlockBoundaries
    {
        public BlockBoundaries(
            int bodyStart,
            int closeStart,
            TextSpan outlineSpan,
            string collapsedText,
            bool canOutline)
        {
            BodyStart = bodyStart;
            CloseStart = closeStart;
            OutlineSpan = outlineSpan;
            CollapsedText = collapsedText;
            CanOutline = canOutline;
        }

        public int BodyStart { get; }

        public int CloseStart { get; }

        public TextSpan OutlineSpan { get; }

        public string CollapsedText { get; }

        public bool CanOutline { get; }
    }
}
