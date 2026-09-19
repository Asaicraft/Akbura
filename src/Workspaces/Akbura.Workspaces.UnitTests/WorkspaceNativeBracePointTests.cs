using Akbura.Workspaces.AutomaticPairing;
using Akbura.Workspaces.Documents;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace Akbura.Workspaces.UnitTests;

public sealed class WorkspaceNativeBracePointTests
{
    [Theory]
    [InlineData("<StackPanel>$foreach (var item in values) |{</StackPanel>")]
    [InlineData("<StackPanel>\r\n$foreach (var item in values)\r\n|{\r\n</StackPanel>")]
    [InlineData("<StackPanel>$foreach (var item in values; key: item.Id) |{</StackPanel>")]
    [InlineData("<StackPanel>$foreach (var item in values) { if (item > 0) |{} }</StackPanel>")]
    [InlineData("<StackPanel>$foreach (var item in values) { $foreach (var inner in values) |{} }</StackPanel>")]
    [InlineData("<Border>$if (ready) |{</Border>")]
    public void NativePoint_OnOrAfterStructuralBrace_UsesTheSameToken(string marked)
    {
        var position = marked.IndexOf('|');
        var source = marked.Remove(position, 1);
        var document = AkburaSyntacticDocument.Parse(SourceText.From(source), "Brace.akbura");

        Assert.True(document.TryGetStructuralCurlyBraceAtCompletionPoint(position, out var onBrace));
        Assert.True(document.TryGetStructuralCurlyBraceAtCompletionPoint(position + 1, out var afterBrace));
        Assert.Equal(position, onBrace);
        Assert.Equal(position, afterBrace);
    }

    [Theory]
    [InlineData("<StackPanel>$foreach (var item in values) ")]
    [InlineData("<StackPanel>\r\n$foreach (var item in values)\r\n")]
    public void PreInsertionPoint_StillUsesSharedTypingRules(string source)
    {
        var document = AkburaSyntacticDocument.Parse(SourceText.From(source), "Brace.akbura");
        using var workspace = new AkburaWorkspace();
        var result = workspace.LanguageServices.Typing.GetResult(document,
            new AkburaTypingCommand(AkburaTypingCommandKind.Type, source.Length, "{",
                new AkburaTypingOptions(TabSize: 4, IndentSize: 4, InsertSpaces: true, NewLine: "\r\n"),
                Session: null));
        Assert.True(result.Handled);
        Assert.Equal("{}", Assert.Single(result.Changes).NewText);
        Assert.NotNull(result.Session);
    }

    [Theory]
    [InlineData("<TextBlock Text=\"|{text\" />")]
    [InlineData("// |{\r\n<Border/>")]
    [InlineData("/* |{ */ <Border/>")]
    [InlineData("<TextBlock Text={\"|{text\"} />")]
    public void NativePoint_InLiteralOrComment_DoesNotBecomeStructural(string marked)
    {
        var position = marked.IndexOf('|');
        var document = AkburaSyntacticDocument.Parse(
            SourceText.From(marked.Remove(position, 1)), "Brace.akbura");
        Assert.False(document.IsStructuralCurlyBraceAtCompletionPoint(position));
        Assert.False(document.IsStructuralCurlyBraceAtCompletionPoint(position + 1));
    }
}
