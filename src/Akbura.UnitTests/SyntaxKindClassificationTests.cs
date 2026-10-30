using Akbura.Language;
using Akbura.Language.Syntax;
using Akbura.Language.Syntax.Green;
using Microsoft.CodeAnalysis.Text;

namespace Akbura.UnitTests;

public sealed class SyntaxKindClassificationTests
{
    [Fact]
    public void AkcssBodyMemberKinds_CoverTheConcreteSyntaxHierarchy()
    {
        var memberKinds = typeof(AkcssBodyMemberSyntax).Assembly.GetTypes()
            .Where(static type => !type.IsAbstract &&
                typeof(AkcssBodyMemberSyntax).IsAssignableFrom(type))
            .Select(static type => Enum.Parse<SyntaxKind>(type.Name))
            .ToHashSet();
        var greenMemberKinds = typeof(GreenAkcssBodyMemberSyntax).Assembly.GetTypes()
            .Where(static type => !type.IsAbstract &&
                typeof(GreenAkcssBodyMemberSyntax).IsAssignableFrom(type))
            .Select(static type => Enum.Parse<SyntaxKind>(type.Name.Substring("Green".Length)))
            .ToHashSet();

        Assert.NotEmpty(memberKinds);
        Assert.True(memberKinds.SetEquals(greenMemberKinds));
        foreach (var kind in Enum.GetValues<SyntaxKind>())
        {
            Assert.Equal(memberKinds.Contains(kind), SyntaxFacts.IsAkcssBodyMember(kind));
        }

        Assert.False(SyntaxFacts.IsAkcssBodyMember((SyntaxKind)ushort.MaxValue));
    }

    [Theory]
    [InlineData("Width: 1;")]
    [InlineData("@apply surface;")]
    [InlineData("@intercept Demo.Style;")]
    [InlineData("@if(IsEnabled) { Width: 1; }")]
    [InlineData("@hover { Width: 1; }")]
    public void IncrementalAkcssEdit_ReusesEachUnchangedBodyMember(string memberText)
    {
        var source = ".bench {\r\n" + memberText + "\r\nHeight: 2;\r\n}";
        var oldText = SourceText.From(source);
        var oldTree = AkcssSyntaxTree.ParseText(oldText, "Styles.akcss");
        var oldRule = Assert.IsType<AkcssStyleRuleSyntax>(oldTree.GetRoot().Members[0]);
        var position = source.IndexOf("Height: 2", StringComparison.Ordinal) + "Height: ".Length;
        var change = new TextChange(new TextSpan(position, 1), "3");
        var newText = oldText.WithChanges(change);

        var incrementalTree = oldTree.WithChangedText(
            newText,
            [new TextChangeRange(change.Span, change.NewText!.Length)]);
        var fullTree = AkcssSyntaxTree.ParseText(newText, "Styles.akcss");
        var newRule = Assert.IsType<AkcssStyleRuleSyntax>(incrementalTree.GetRoot().Members[0]);

        Assert.Same(oldRule.Members[0].Green, newRule.Members[0].Green);
        Assert.Equal(fullTree.GetRoot().ToFullString(), incrementalTree.GetRoot().ToFullString());
        Assert.Equal(
            fullTree.GetRoot().DescendantNodesAndSelf().Select(static node => (node.Kind, node.FullSpan)),
            incrementalTree.GetRoot().DescendantNodesAndSelf().Select(static node => (node.Kind, node.FullSpan)));
        Assert.Equal(fullTree.GetRoot().ContainsDiagnostics, incrementalTree.GetRoot().ContainsDiagnostics);
    }
}
