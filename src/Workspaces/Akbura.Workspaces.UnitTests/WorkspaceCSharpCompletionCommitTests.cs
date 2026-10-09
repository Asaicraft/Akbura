using Akbura.Language.Syntax;
using Akbura.Workspaces.Completion;
using Akbura.Workspaces.Documents;
using Microsoft.CodeAnalysis.Text;

namespace Akbura.Workspaces.UnitTests;

public sealed class WorkspaceCSharpCompletionCommitTests
{
    [Fact]
    public void ContextMismatch_ReportsTypedRejectionReasons()
    {
        var (text, position) = ParseCaret("<TextBlock Text={count|}/>");
        var document = AkburaSyntacticDocument.Parse(text, "Counter.akbura");
        Assert.True(document.TryGetCSharpCompletionContext(position, out var source));

        var changedKind = new AkburaCSharpCompletionContext(
            AkburaCSharpCompletionContextKind.Statement, source.OwnerKind,
            source.OwnerSpan, source.HostSpan, source.HostPosition);
        AssertReason(AkburaCSharpCompletionRejectionReason.ContextKindChanged,
            changedKind, source.OwnerSpan, source.HostSpan);

        var changedSlot = new AkburaCSharpCompletionContext(
            source.Kind, source.OwnerKind, source.OwnerSpan, source.HostSpan, source.HostPosition,
            AkburaCSharpCompletionLogicalSlot.DeclarationType, source.OwnerSpan);
        AssertReason(AkburaCSharpCompletionRejectionReason.LogicalSlotChanged,
            changedSlot, source.OwnerSpan, source.HostSpan);

        var changedOwnerKind = new AkburaCSharpCompletionContext(
            source.Kind, SyntaxKind.CSharpStatementSyntax,
            source.OwnerSpan, source.HostSpan, source.HostPosition);
        AssertReason(AkburaCSharpCompletionRejectionReason.OwnerKindChanged,
            changedOwnerKind, source.OwnerSpan, source.HostSpan);

        AssertReason(AkburaCSharpCompletionRejectionReason.OwnerChanged, source,
            new TextSpan(source.OwnerSpan.Start, source.OwnerSpan.Length - 1), source.HostSpan);
        AssertReason(AkburaCSharpCompletionRejectionReason.HostSpanChanged, source,
            source.OwnerSpan, new TextSpan(source.HostSpan.Start, source.HostSpan.Length - 1));
        AssertReason(AkburaCSharpCompletionRejectionReason.TextOutsideHostSpanChanged, source,
            source.OwnerSpan, source.HostSpan, text.WithChanges(new TextChange(new TextSpan(1, 1), "X")));

        void AssertReason(AkburaCSharpCompletionRejectionReason expected, AkburaCSharpCompletionContext current, TextSpan ownerSpan, TextSpan hostSpan, SourceText? currentText = null)
        {
            Assert.False(AkburaCSharpCompletionCommitFacts.IsMatchingContext(
                text, currentText ?? text, source, current, ownerSpan, hostSpan, out var reason));
            Assert.Equal(expected, reason);
        }
    }

    [Theory]
    [InlineData(false, "/>")]
    [InlineData(true, "/>")]
    [InlineData(false, "")]
    [InlineData(true, "")]
    public void PairedBrace_OriginalExpressionSessionCanCommit(bool pairBeforeSession, string suffix)
    {
        const string prefix = "state int count = 0;\r\n\r\n<TextBlock Text={";
        var text = SourceText.From(prefix + (pairBeforeSession ? "}" : "") + suffix);
        var document = AkburaSyntacticDocument.Parse(text, "Counter.akbura");
        var position = prefix.Length;
        Assert.True(document.TryGetCSharpCompletionContext(position, out var sourceContext));
        var sourceText = text;
        var translatedOwner = sourceContext.OwnerSpan;
        var translatedHost = sourceContext.HostSpan;
        if (!pairBeforeSession)
        {
            Insert("}", advanceCaret: false);
        }

        foreach (var character in "c")
        {
            Insert(character.ToString(), advanceCaret: true);
        }

        Assert.True(document.TryGetCSharpCompletionContext(position, out var currentContext));
        Assert.True(AkburaCSharpCompletionCommitFacts.IsMatchingContext(
            sourceText, text, sourceContext, currentContext, translatedOwner, translatedHost, out var reason),
            $"{reason}; sourceOwner={sourceContext.OwnerSpan}; translatedOwner={translatedOwner}; " +
            $"currentOwner={currentContext.OwnerSpan}; sourceHost={sourceContext.HostSpan}; " +
            $"translatedHost={translatedHost}; currentHost={currentContext.HostSpan}");
        Assert.Equal(AkburaCSharpCompletionRejectionReason.None, reason);
        Assert.True(AkburaCSharpCompletionCommitFacts.TryGetReplacementSpan(
            text, currentContext, translatedHost, out var replacement));
        var committed = text.WithChanges(new TextChange(replacement, "count"));
        var caret = replacement.Start + "count".Length;
        Assert.Equal(prefix + "count}" + suffix, committed.ToString());
        Assert.Equal('}', committed[caret]);

        void Insert(string insertion, bool advanceCaret)
        {
            text = text.WithChanges(new TextChange(new TextSpan(position, 0), insertion));
            document = document.WithText(text);
            translatedOwner = ExpandForInsertion(translatedOwner, position, insertion.Length);
            translatedHost = ExpandForInsertion(translatedHost, position, insertion.Length);
            if (advanceCaret)
            {
                position += insertion.Length;
            }
        }
    }

    [Theory]
    [InlineData("<TextBlock Text={|/>", "count")]
    [InlineData("<TextBlock Text={|}/>", "count")]
    [InlineData("state int other = |;", "count")]
    [InlineData("param |", "string")]
    public void UnpairedNameEdit_PreservesTheExistingContextContract(string marked, string insertion)
    {
        var (sourceText, position) = ParseCaret(marked);
        var document = AkburaSyntacticDocument.Parse(sourceText, "Counter.akbura");
        Assert.True(document.TryGetCSharpCompletionContext(position, out var sourceContext));
        var text = sourceText.WithChanges(new TextChange(new TextSpan(position, 0), insertion));
        document = document.WithText(text);
        Assert.True(document.TryGetCSharpCompletionContext(position + insertion.Length, out var currentContext));
        var sourceOwner = sourceContext.LogicalSlot == AkburaCSharpCompletionLogicalSlot.DeclarationType
            ? sourceContext.LogicalOwnerSpan
            : sourceContext.OwnerSpan;
        var translatedOwner = ExpandForInsertion(sourceOwner, position, insertion.Length);
        var translatedHost = ExpandForInsertion(sourceContext.HostSpan, position, insertion.Length);

        Assert.True(AkburaCSharpCompletionCommitFacts.IsMatchingContext(
            sourceText, text, sourceContext, currentContext, translatedOwner, translatedHost, out var reason), reason.ToString());
        Assert.Equal(AkburaCSharpCompletionRejectionReason.None, reason);
    }

    [Theory]
    [InlineData("state int count = 0;\r\n<TextBlock Text={|/>", "state int count = 0;\r\n<TextBlock Text={c|} Tag=\"changed\"/>")]
    [InlineData("state int count = 0;\r\n<TextBlock Text={|/>", "state int count = 1;\r\n<TextBlock Text={c|}/>")]
    [InlineData("<TextBlock Text={|/>", "<TextBlock Name={c|}/>")]
    [InlineData("<TextBlock Text={|/>", "<TextBlock Text={c|}}/>")]
    [InlineData("<TextBlock Text={|/>", "<TextBlock Text={c|}>")]
    [InlineData("<TextBlock Text={|/>", "<TextBlock Text={c} Tag={d|}/>")]
    [InlineData("<TextBlock Text={|}/>", "<TextBlock Text={c|}}/>")]
    public void ExpressionCommit_RejectsChangedOwnerOrTextOutsideThePair(string sourceMarked, string currentMarked)
    {
        var (sourceText, sourcePosition) = ParseCaret(sourceMarked);
        var (currentText, currentPosition) = ParseCaret(currentMarked);
        var document = AkburaSyntacticDocument.Parse(sourceText, "Counter.akbura");
        Assert.True(document.TryGetCSharpCompletionContext(sourcePosition, out var sourceContext));
        document = document.WithText(currentText);
        Assert.True(document.TryGetCSharpCompletionContext(currentPosition, out var currentContext));

        // Even permissive translated ranges must not authorize a stale commit.
        var translatedOwner = TextSpan.FromBounds(sourceContext.OwnerSpan.Start, currentText.Length);
        var translatedHost = TextSpan.FromBounds(sourceContext.HostSpan.Start, currentText.Length);
        Assert.False(AkburaCSharpCompletionCommitFacts.IsMatchingContext(
            sourceText, currentText, sourceContext, currentContext, translatedOwner, translatedHost, out var reason));
        Assert.NotEqual(AkburaCSharpCompletionRejectionReason.None, reason);
    }

    [Theory]
    [InlineData("<TextBlock Text={c|}/>", "count")]
    [InlineData("<TextBlock Text={count.ToStr|}/>", "ToString")]
    [InlineData("<TextBlock Text={f(c|)}/>", "count")]
    [InlineData("<TextBlock Text={(new[] { 1, 2 })[c|]}/>", "count")]
    public void Replacement_WithinTheFragmentPreservesExpressionSuffix(string marked, string insertion)
    {
        var (text, position) = ParseCaret(marked);
        var document = AkburaSyntacticDocument.Parse(text, "Counter.akbura");
        Assert.True(document.TryGetCSharpCompletionContext(position, out var context));
        var start = position;
        while (start > context.HostSpan.Start && char.IsLetterOrDigit(text[start - 1]))
        {
            start--;
        }

        var span = TextSpan.FromBounds(start, position);
        Assert.True(AkburaCSharpCompletionCommitFacts.TryGetReplacementSpan(text, context, span, out var replacement));
        Assert.Equal(span, replacement);
        var result = text.WithChanges(new TextChange(replacement, insertion));
        Assert.Equal(text.ToString(TextSpan.FromBounds(position, text.Length)),
            result.ToString(TextSpan.FromBounds(start + insertion.Length, result.Length)));
    }

    [Theory]
    [InlineData("<TextBlock Text={c|}/>", -1, 0)]
    [InlineData("<TextBlock Text={c|}/>", 2, 1)]
    [InlineData("<TextBlock Text={c|}/>", 0, 100)]
    [InlineData("state int count = c|;", 0, 1)]
    public void Replacement_RejectsRangesOutsideTheExpression(string marked, int startOffset, int endOffset)
    {
        var (text, position) = ParseCaret(marked);
        var document = AkburaSyntacticDocument.Parse(text, "Counter.akbura");
        Assert.True(document.TryGetCSharpCompletionContext(position, out var context));
        var span = TextSpan.FromBounds(context.HostSpan.Start + startOffset, context.HostSpan.End + endOffset);
        Assert.False(AkburaCSharpCompletionCommitFacts.TryGetReplacementSpan(text, context, span, out _));
    }

    private static (SourceText Text, int Position) ParseCaret(string marked)
    {
        var position = marked.IndexOf('|');
        Assert.True(position >= 0);
        return (SourceText.From(marked.Remove(position, 1)), position);
    }

    internal static TextSpan ExpandForInsertion(TextSpan span, int position, int length)
    {
        // Inclusive tracking keeps insertions at either edge, including the
        // native } edit at the end of an initially empty completion range.
        if (position < span.Start)
        {
            return new TextSpan(span.Start + length, span.Length);
        }

        return position <= span.End ? new TextSpan(span.Start, span.Length + length) : span;
    }
}
