using Akbura.Language;
using Akbura.Language.Syntax.Green;

namespace Akbura.UnitTests;

public sealed class SeparatedGreenSyntaxRewriterTests
{
    [Theory]
    [InlineData("<Grid space-2-4/>")]
    [InlineData("<Grid Value=${SomeExtension A, B}/>")]
    [InlineData("<Control{int, string}/>")]
    [InlineData("<Demo.Controls.Button/>")]
    public void GreenSyntaxRewriter_VisitsSeparatedListsWithoutCastingSeparators(
        string code)
    {
        var tree = ComponentSyntaxTree.ParseText(code);
        var root = tree.GetRoot();

        var rewritten = new GreenSyntaxRewriter().Visit(root.Green);

        Assert.Same(root.Green, rewritten);
        Assert.Equal(code, rewritten!.ToFullString());
    }

    [Fact]
    public void ParserRecovery_RewritesMalformedSeparatedTailwindListWithoutLosingText()
    {
        const string code = "<Grid , space-2-4/>";

        var tree = ComponentSyntaxTree.ParseText(code);
        var root = tree.GetRoot();
        var rewritten = new GreenSyntaxRewriter().Visit(root.Green);

        Assert.True(root.ContainsDiagnostics);
        Assert.True(rewritten!.ContainsDiagnostics);
        Assert.Equal(code, root.ToFullString());
        Assert.Equal(code, rewritten.ToFullString());
    }
}
