using System;
using Akbura.Workspaces;
using Akbura.Workspaces.Completion;
using Akbura.Workspaces.Documents;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace Akbura.Workspaces.UnitTests;

public sealed class WorkspaceMarkupExtensionSessionTests
{
    [Theory]
    [InlineData("<NavIcon Geometries=${|} />", "")]
    [InlineData("<NavIcon Geometries=${St|} />", "St")]
    [InlineData("<NavIcon Geometries=${| />", "")]
    [InlineData("<NavIcon ${| />", "")]
    [InlineData("<NavIcon ${St|}:p-5 />", "St")]
    [InlineData("<NavIcon Geometries=${StaticResource \"Icon.Home\"}  ${| />", "")]
    [InlineData("<NavIcon Geometries=${StaticResource \"Icon.Home\"}  ${St|} />", "St")]
    [InlineData("<Border p-${|} />", "")]
    [InlineData("<Border Tag=${Outer Value=${St|}} />", "St")]
    [InlineData("<Border Tag=${  St|} />", "St")]
    public void OpenerSelectsExtensionTypes_NotAttributeNames(string marked, string prefix)
    {
        var (document, position, opening) = Parse(marked);
        Assert.True(AkburaMarkupExtensionCompletionFacts.TryGetTypeNameSpan(
            document, position, opening, out var nameSpan));
        Assert.Equal(prefix, document.Text.ToString(nameSpan));
        Assert.Equal(AkburaCompletionContextKind.MarkupExtensionType,
            document.GetCompletionContext(position).Kind);
    }

    [Theory]
    [InlineData("\n", false)]
    [InlineData("\r\n", true)]
    public void AttributeToExtensionTransition_UsesNewNameAnchor(string newline, bool pairedBrace)
    {
        var prefix = "state int count = 0;" + newline + newline +
            "<NavIcon Geometries=${StaticResource \"Icon.Home\"}  ";
        var text = SourceText.From(prefix + " />");
        var document = AkburaSyntacticDocument.Parse(text, "MainView.akbura");
        var position = prefix.Length;
        var attributeContext = document.GetCompletionContext(position);
        Assert.Equal(AkburaCompletionContextKind.AttributeName, attributeContext.Kind);

        foreach (var character in "${")
        {
            text = text.WithChanges(new TextChange(new TextSpan(position++, 0), character.ToString()));
            document = document.WithText(text);
        }
        var opening = position - 1;
        if (pairedBrace)
        {
            text = text.WithChanges(new TextChange(new TextSpan(position, 0), "}"));
            document = document.WithText(text);
        }

        AssertTransition(document, position, opening, prefix.Length);
        foreach (var character in "St")
        {
            text = text.WithChanges(new TextChange(new TextSpan(position++, 0), character.ToString()));
            document = document.WithText(text);
            AssertTransition(document, position, opening, prefix.Length);
        }

        // Simulate the name-only replacement issued after a fresh request.
        // The surrounding ${, optional }, existing resource and tag must survive.
        Assert.True(AkburaMarkupExtensionCompletionFacts.TryGetTypeNameSpan(
            document, position, opening, out var span));
        var committed = text.WithChanges(new TextChange(span, "StaticResource"));
        Assert.Equal(prefix + "${StaticResource" + (pairedBrace ? "}" : "") + " />", committed.ToString());
    }

    [Theory]
    [InlineData("<TextBlock Text=\"literal ${|}\" />")]
    [InlineData("state string text = \"${|}\";")]
    [InlineData("// ${|")]
    [InlineData("<Border Tag=${Binding Path|} />")]
    [InlineData("<Border Tag=${StaticResource \"Icon.|Home\"} />")]
    public void LiteralAndArgumentContexts_DoNotSwitchCatalogs(string marked)
    {
        var (document, position, opening) = Parse(marked);
        Assert.False(AkburaMarkupExtensionCompletionFacts.TryGetTypeNameSpan(
            document, position, opening, out _));
    }

    [Fact]
    public void ARequestForAnOuterOpener_CannotSwitchANestedSession()
    {
        const string marked = "<Border Tag=${Outer Value=${St|}} />";
        var (document, position, _) = Parse(marked);
        var outerOpening = marked.IndexOf("${", StringComparison.Ordinal) + 1;
        Assert.False(AkburaMarkupExtensionCompletionFacts.TryGetTypeNameSpan(
            document, position, outerOpening, out _));
    }

    [Theory]
    [InlineData("<Border Tag=${| />")]
    [InlineData("<Border ${| />")]
    [InlineData("<Border Tag=${|} />")]
    public void EmptyNameBeforeTagBoundary_CanBeCommittedWithoutRemovingSuffix(string marked)
    {
        var (document, position, _) = Parse(marked);
        Assert.True(AkburaMarkupExtensionCompletionFacts.TryGetNameReplacementSpan(
            document.Text, new TextSpan(position, 0), out var span));
        Assert.Equal(new TextSpan(position, 0), span);
        Assert.Equal(marked.Replace("|", "StaticResource", StringComparison.Ordinal),
            document.Text.WithChanges(new TextChange(span, "StaticResource")).ToString());
    }

    private static void AssertTransition(
        AkburaSyntacticDocument incremental, int position, int opening, int previousStart)
    {
        var full = AkburaSyntacticDocument.Parse(incremental.Text, "MainView.akbura");
        Assert.True(AkburaMarkupExtensionCompletionFacts.TryGetTypeNameSpan(
            incremental, position, opening, out var incrementalSpan));
        Assert.True(AkburaMarkupExtensionCompletionFacts.TryGetTypeNameSpan(
            full, position, opening, out var fullSpan));
        Assert.Equal(fullSpan, incrementalSpan);
        Assert.False(AkburaMarkupExtensionCompletionFacts.IsTypeNameSession(
            TextSpan.FromBounds(previousStart, position), incrementalSpan));
        Assert.True(AkburaMarkupExtensionCompletionFacts.IsTypeNameSession(incrementalSpan, incrementalSpan));

        if (position < incremental.Text.Length && incremental.Text[position] == '}')
        {
            Assert.True(AkburaMarkupExtensionCompletionFacts.IsTypeNameSession(
                TextSpan.FromBounds(incrementalSpan.Start, position + 1), incrementalSpan));
        }
    }

    private static (AkburaSyntacticDocument Document, int Position, int Opening) Parse(string marked)
    {
        var position = marked.IndexOf('|');
        Assert.True(position >= 0);
        var source = marked.Remove(position, 1);
        var opening = source.LastIndexOf("${", position - 1, StringComparison.Ordinal) + 1;
        Assert.True(opening > 0);
        return (AkburaSyntacticDocument.Parse(SourceText.From(source), "MainView.akbura"), position, opening);
    }
}
