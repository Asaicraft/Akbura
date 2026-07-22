using Akbura.Language;
using Microsoft.CodeAnalysis.Text;
using System.IO;

namespace Akbura.UnitTests;

public sealed class SyntaxTreeTests
{
    [Fact]
    public void AkcssSyntaxTree_ParseTextSourceTextAndPath_PreservesPathAndLogicalName()
    {
        var text = SourceText.From(".card { Width: 100; }");
        var path = Path.Combine("Styles", "Theme.akcss");

        AkcssSyntaxTree syntaxTree = AkcssSyntaxTree.ParseText(text, path);

        Assert.Same(text, syntaxTree.Text);
        Assert.Equal(path, syntaxTree.FilePath);
        Assert.Equal("Theme.akcss", syntaxTree.LogicalName);
        Assert.Equal(SyntaxTreeKind.Akcss, syntaxTree.Kind);
    }
}
