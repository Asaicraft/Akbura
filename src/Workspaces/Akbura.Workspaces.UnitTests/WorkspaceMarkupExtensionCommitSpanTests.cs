using System;
using Akbura.Workspaces.Completion;
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
