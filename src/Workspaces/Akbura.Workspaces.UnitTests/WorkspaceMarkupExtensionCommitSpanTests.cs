using System;
using Akbura.Workspaces.Completion;
using Akbura.Workspaces.Documents;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace Akbura.Workspaces.UnitTests;

public sealed class WorkspaceMarkupExtensionCommitSpanTests
{
    // [|...|] is the already translated replacement span. Some cases
    // deliberately include delimiters swallowed by EdgeInclusive tracking.
    [Theory]
    [InlineData("<NavIcon Geometries=${[|St|]} />", "<NavIcon Geometries=${StaticResource} />")]
    [InlineData("<NavIcon Geometries=${[|St}|] />", "<NavIcon Geometries=${StaticResource} />")]
    [InlineData("<NavIcon Geometries=${[|St} Tag=\"keep\" />|]", "<NavIcon Geometries=${StaticResource} Tag=\"keep\" />")]
    [InlineData("<NavIcon Geometries=${[|St \"Icon.Home\"}|] />", "<NavIcon Geometries=${StaticResource \"Icon.Home\"} />")]
    [InlineData("<NavIcon Geometries=${Outer Value=${[|St}}|] />", "<NavIcon Geometries=${Outer Value=${StaticResource}} />")]
    [InlineData("<NavIcon Geometries=${[|  St}|] />", "<NavIcon Geometries=${  StaticResource} />")]
    [InlineData("<NavIcon Geometries=${ [|St|]} />", "<NavIcon Geometries=${ StaticResource} />")]
    [InlineData("<NavIcon Geometries=${[||]} />", "<NavIcon Geometries=${StaticResource} />")]
    [InlineData("<NavIcon Geometries=${[|}|] />", "<NavIcon Geometries=${StaticResource} />")]
    [InlineData("<NavIcon Geometries=${[|St|]", "<NavIcon Geometries=${StaticResource")]
    public void Replacement_PreservesEveryCharacterOutsideTheName(string marked, string expected)
    {
        var (text, translated) = ParseSpan(marked);
        Assert.True(AkburaMarkupExtensionCompletionFacts.TryGetNameReplacementSpan(
            text, translated, out var replacement));
        Assert.True(replacement.Start >= translated.Start && replacement.End <= translated.End);
        var result = text.WithChanges(new TextChange(replacement, "StaticResource"));
        Assert.Equal(expected, result.ToString());
    }

    [Fact]
    public void SessionStartedBeforeClosingBrace_DoesNotReplaceTheBrace()
    {
        const string prefix = "<NavIcon Geometries=${";
        var before = SourceText.From(prefix + " />");
        var current = before.WithChanges(new TextChange(new TextSpan(prefix.Length, 0), "}"));
        current = current.WithChanges(new TextChange(new TextSpan(prefix.Length, 0), "St"));

        // This is the range produced when an initially empty span expands
        // over both edits at its right edge. The test does not host VS.
        var translated = new TextSpan(prefix.Length, "St}".Length);
        Assert.True(AkburaMarkupExtensionCompletionFacts.TryGetNameReplacementSpan(
            current, translated, out var replacement));
        Assert.Equal("St", current.ToString(replacement));
        Assert.Equal(prefix + "StaticResource} />",
            current.WithChanges(new TextChange(replacement, "StaticResource")).ToString());
    }

    [Theory]
    [InlineData(false, "St", "StaticResource")]
    [InlineData(true, "St", "StaticResource")]
    [InlineData(false, "B", "Binding")]
    [InlineData(true, "B", "Binding")]
    [InlineData(false, "", "StaticResource")]
    [InlineData(true, "", "StaticResource")]
    public void PairedBrace_OriginalTypeSessionCanCommit(bool pairBeforeSession, string typedName, string insertion)
    {
        const string prefix = "<Border Child=${";
        var text = SourceText.From(prefix + " />");
        var document = AkburaSyntacticDocument.Parse(text, "MainView.akbura");
        var position = prefix.Length;
        if (pairBeforeSession)
        {
            text = text.WithChanges(new TextChange(new TextSpan(position, 0), "}"));
            document = document.WithText(text);
        }

        var sourceText = text;
        var sourceContext = document.GetCompletionContext(position);
        Assert.Equal(AkburaCompletionContextKind.MarkupExtensionType, sourceContext.Kind);
        if (!pairBeforeSession)
        {
            text = text.WithChanges(new TextChange(new TextSpan(position, 0), "}"));
            document = document.WithText(text);
        }

        foreach (var character in typedName)
        {
            text = text.WithChanges(new TextChange(new TextSpan(position++, 0), character.ToString()));
            document = document.WithText(text);
        }

        // The same session survives both the paired delimiter and name filtering.
        // EdgeInclusive absorbs } only when pairing happened after the catalog opened.
        var translated = new TextSpan(prefix.Length, typedName.Length + (pairBeforeSession ? 0 : 1));
        var currentContext = document.GetCompletionContext(position);
        Assert.True(AkburaMarkupExtensionCompletionFacts.IsMatchingTypeNameContext(
            sourceText, text, sourceContext, currentContext, translated));
        Assert.True(AkburaMarkupExtensionCompletionFacts.TryGetNameReplacementSpan(
            text, translated, out var replacement));
        var committed = text.WithChanges(new TextChange(replacement, insertion));
        var caret = replacement.Start + insertion.Length;
        Assert.Equal(prefix + insertion + "} />", committed.ToString());
        Assert.Equal('}', committed[caret]);
    }

    [Theory]
    [InlineData("<Border Child=${[||] />", "<Border Child=${[|St}|] />", "<Border Child=${St[||]} />")]
    [InlineData("<Border Child=${[||]} />", "<Border Child=${[|St|]} />", "<Border Child=${St[||]} />")]
    [InlineData("<Border Child=${[|S|] />", "<Border Child=${[|St}|] />", "<Border Child=${St[||]} />")]
    [InlineData("<Border Child=${[||]", "<Border Child=${[|St}|]", "<Border Child=${St[||]}")]
    [InlineData("<Border Child=${[||] />", "<Border Child=${[|St|] />", "<Border Child=${St[||] />")]
    [InlineData("<Border Child=${ [||] />", "<Border Child=${ [|St}|] />", "<Border Child=${ St[||]} />")]
    [InlineData("<Border Child=${Outer Value=${[||]} />", "<Border Child=${Outer Value=${[|St}|]} />", "<Border Child=${Outer Value=${St[||]}} />")]
    public void TypeSession_MatchesNameEditsWithOrWithoutPairing(string sourceMarked, string translatedMarked, string caretMarked)
    {
        var (sourceText, sourceSpan) = ParseSpan(sourceMarked);
        var (currentText, translatedSpan) = ParseSpan(translatedMarked);
        var (caretText, caretSpan) = ParseSpan(caretMarked);
        Assert.Equal(currentText.ToString(), caretText.ToString());
        var document = AkburaSyntacticDocument.Parse(sourceText, "MainView.akbura");
        var sourceContext = document.GetCompletionContext(sourceSpan.End);
        document = document.WithText(currentText);
        var currentContext = document.GetCompletionContext(caretSpan.Start);

        Assert.True(AkburaMarkupExtensionCompletionFacts.IsMatchingTypeNameContext(
            sourceText, currentText, sourceContext, currentContext, translatedSpan));
    }

    [Theory]
    [InlineData("<Border Child=${[||] />", "<Border Child=${[|St}}|] />", "<Border Child=${St[||]}} />")]
    [InlineData("<Border Child=${[||] />", "<Border Child=${[|St}|] Tag=\"changed\" />", "<Border Child=${St[||]} Tag=\"changed\" />")]
    [InlineData("<Border Child=${[||] />", "<Border Tag=${[|St}|] />", "<Border Tag=${St[||]} />")]
    [InlineData("<Border Child=${[||] />", "<Button Child=${[|St}|] />", "<Button Child=${St[||]} />")]
    [InlineData("<Border Child=${[||]} />", "<Border Child=${[|St|]} />", "<Border Child=${S[||]t} />")]
    [InlineData("<Border Child=${[||]} />", "<Border Child=${[|St |]} />", "<Border Child=${St [||]} />")]
    [InlineData("<Border Child=${[||]} />", "<Border Child=${[|Binding Value=${St}|]} />", "<Border Child=${Binding Value=${St[||]}} />")]
    [InlineData("<Border [||] />", "<Border Child=${[|St}|] />", "<Border Child=${St[||]} />")]
    [InlineData("<Border Child=${[||] />", "<Border Child=[|${St}|] />", "<Border Child=${St[||]} />")]
    [InlineData("<Border Child=${[||] />", "<Border Child=${[|St|]} />", "<Border Child=${St[||]} />")]
    [InlineData("<Border Child=${[||]} Tag=${B} />", "<Border Child=${[||]} Tag=${B} />", "<Border Child=${} Tag=${B[||]} />")]
    public void TypeSession_RejectsStaleContextOrUnrelatedEdits(string sourceMarked, string translatedMarked, string caretMarked)
    {
        var (sourceText, sourceSpan) = ParseSpan(sourceMarked);
        var (currentText, translatedSpan) = ParseSpan(translatedMarked);
        var (caretText, caretSpan) = ParseSpan(caretMarked);
        Assert.Equal(currentText.ToString(), caretText.ToString());
        var document = AkburaSyntacticDocument.Parse(sourceText, "MainView.akbura");
        var sourceContext = document.GetCompletionContext(sourceSpan.End);
        document = document.WithText(currentText);
        var currentContext = document.GetCompletionContext(caretSpan.Start);

        Assert.False(AkburaMarkupExtensionCompletionFacts.IsMatchingTypeNameContext(
            sourceText, currentText, sourceContext, currentContext, translatedSpan));
    }

    [Theory]
    [InlineData("<NavIcon Geometries=${[|a::St|]} />", "a::StaticResource")]
    [InlineData("<NavIcon Geometries=${[|Resources.St|]} />", "Resources.StaticResource")]
    public void QualifiedNames_KeepTheSameReplacementContract(string marked, string insertion)
    {
        var (text, translated) = ParseSpan(marked);
        Assert.True(AkburaMarkupExtensionCompletionFacts.TryGetNameReplacementSpan(
            text, translated, out var replacement));
        Assert.Equal(translated, replacement);
        var result = text.WithChanges(new TextChange(replacement, insertion));
        Assert.Equal("<NavIcon Geometries=${" + insertion + "} />", result.ToString());
    }

    [Theory]
    [InlineData("<NavIcon Geometries=${Binding [|Path}|] />")]
    [InlineData("<TextBlock Text=\"[|St|]\" />")]
    [InlineData("<[|St|] />")]
    [InlineData("<NavIcon Geometries=${[|\"Icon.Home\"}|] />")]
    public void StaleOrNonNameContext_IsRejected(string marked)
    {
        var (text, translated) = ParseSpan(marked);
        Assert.False(AkburaMarkupExtensionCompletionFacts.TryGetNameReplacementSpan(
            text, translated, out _));
    }

    [Fact]
    public void OutOfBoundsSpan_IsRejected()
    {
        var text = SourceText.From("${St}");
        Assert.False(AkburaMarkupExtensionCompletionFacts.TryGetNameReplacementSpan(
            text, new TextSpan(text.Length + 1, 0), out _));
    }

    private static (SourceText Text, TextSpan Span) ParseSpan(string marked)
    {
        var start = marked.IndexOf("[|", StringComparison.Ordinal);
        var end = marked.IndexOf("|]", StringComparison.Ordinal);
        Assert.True(start >= 0 && end >= start + 2);
        var source = marked.Remove(end, 2).Remove(start, 2);
        return (SourceText.From(source), new TextSpan(start, end - start - 2));
    }
}
