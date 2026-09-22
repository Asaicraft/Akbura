using Akbura.Workspaces.Completion;
using Akbura.Workspaces.Documents;
using Microsoft.CodeAnalysis.Text;

namespace Akbura.Workspaces.UnitTests;

public sealed class WorkspaceResourceKeyCompletionFactsTests
{
    [Theory]
    [InlineData(
        "<Border Background=${StaticResource \"Icon.Ho|me\"} />",
        "Icon.Ho",
        "Icon.Home",
        '"')]
    [InlineData(
        "<Border Background=${DynamicResource '--color-|slate-300'} />",
        "--color-",
        "--color-slate-300",
        '\'')]
    [InlineData(
        "<Border Background=${StaticResource Icon.Ho|me} />",
        "Icon.Ho",
        "Icon.Home",
        '\0')]
    [InlineData(
        "<Border Background=${StaticResource ResourceKey=Accent|Brush} />",
        "Accent",
        "AccentBrush",
        '\0')]
    [InlineData(
        "<Border Background=${StaticResourceExtension Accent|Brush} />",
        "Accent",
        "AccentBrush",
        '\0')]
    [InlineData(
        "<Border Background=${Avalonia.Markup.Xaml.MarkupExtensions.StaticResource Accent|Brush} />",
        "Accent",
        "AccentBrush",
        '\0')]
    public void Context_ReplacesTheWholeKeyAndPreservesDelimiters(string marked, string expectedPrefix, string expectedValue, char expectedQuote)
    {
        var (document, position) = Parse(marked);

        Assert.True(AkburaResourceKeyCompletionFacts.TryGetContext(
            document,
            position,
            out var context));
        Assert.Equal(expectedPrefix, context.Prefix);
        Assert.Equal(expectedValue, document.Text.ToString(
            context.ApplicableSpan));
        Assert.Equal(expectedQuote, context.Quote);

        var changed = document.Text.WithChanges(
            new TextChange(context.ApplicableSpan, "Replacement Key"));
        Assert.Equal(
            marked.Remove(marked.IndexOf('|'), 1)
                .Replace(expectedValue, "Replacement Key", StringComparison.Ordinal),
            changed.ToString());
    }

    [Theory]
    [InlineData("<Border Background=${StaticResource |} />")]
    [InlineData("<Border Background=${StaticResource ResourceKey=|} />")]
    [InlineData("<Border Background=${DynamicResource \"|\"} />")]
    public void EmptyKey_HasAnEmptyReplacementSpan(string marked)
    {
        var (document, position) = Parse(marked);

        Assert.True(AkburaResourceKeyCompletionFacts.TryGetContext(
            document,
            position,
            out var context));
        Assert.Equal(new TextSpan(position, 0), context.ApplicableSpan);
        Assert.Empty(context.Prefix);
    }

    [Fact]
    public void NestedResourceExtension_UsesTheInnerArgument()
    {
        var (document, position) = Parse(
            "<TextBlock Text=${Binding Converter=${StaticResource Co|nverter.Main}} />");

        Assert.True(AkburaResourceKeyCompletionFacts.TryGetContext(
            document,
            position,
            out var context));
        Assert.Equal("StaticResource", context.ExtensionName);
        Assert.Equal("Co", context.Prefix);
        Assert.Equal(
            "Converter.Main",
            document.Text.ToString(context.ApplicableSpan));
    }

    [Theory]
    [InlineData("<TextBlock Text=\"Icon.Ho|me\" />")]
    [InlineData("<TextBlock Text=${Binding Icon.Ho|me} />")]
    [InlineData("<Border Tag=${StaticResource Resource|Key=AccentBrush} />")]
    [InlineData("<Border Tag=${StaticResource ResourceKey={Get|Key()}} />")]
    public void OtherContexts_AreNotResourceKeys(string marked)
    {
        var (document, position) = Parse(marked);

        Assert.False(AkburaResourceKeyCompletionFacts.TryGetContext(
            document,
            position,
            out _));
    }

    private static (AkburaSyntacticDocument Document, int Position) Parse(string marked)
    {
        var position = marked.IndexOf('|');
        Assert.True(position >= 0);
        var source = marked.Remove(position, 1);
        return (
            AkburaSyntacticDocument.Parse(
                SourceText.From(source),
                "MainView.akbura"),
            position);
    }
}
