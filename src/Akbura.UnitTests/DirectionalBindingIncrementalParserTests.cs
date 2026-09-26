using Akbura.Language;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Akbura.UnitTests;

public sealed class DirectionalBindingIncrementalParserTests
{
    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void StateModeTypePathHookAndRecoveryTransitionsMatchFreshAndReuseNeighbors(
        string newline)
    {
        var declarations = new[]
        {
            "state int value = 0;",
            "state value = 0;",
            "state string value = \"\";",
            "state string value = bind vm.Name;",
            "state string value = out vm.Name;",
            "state string value = in vm.People[0].Name;",
            "state string value = useState(\"hook\");",
            "state string value = bind vm.People[;",
            "state string value = bind vm.People[1].Name;",
        };
        var prefix = "state object before = new();" + newline;
        var suffix = newline +
            "state object after = new();" + newline +
            "<Border />" + newline;
        var text = SourceText.From(prefix + declarations[0] + suffix);
        var tree = ComponentSyntaxTree.ParseText(text, "StateDemo.akbura");
        var originalBefore = tree.GetRoot().Members[0].Green;
        var originalAfter = tree.GetRoot().Members[^2].Green;
        var originalMarkup = tree.GetRoot().Members[^1].Green;
        var currentLength = declarations[0].Length;
        var transitions = new List<string>();
        for (var index = 1; index < declarations.Length; index++)
        {
            transitions.Add(declarations[index]);
        }

        for (var index = declarations.Length - 2; index >= 0; index--)
        {
            transitions.Add(declarations[index]);
        }

        foreach (var declaration in transitions)
        {
            text = text.WithChanges(new TextChange(
                new TextSpan(prefix.Length, currentLength),
                declaration));
            tree = tree.WithChangedText(text);
            currentLength = declaration.Length;

            AssertMatchesFresh(tree, text);
            Assert.Same(originalBefore, tree.GetRoot().Members[0].Green);
            Assert.Same(originalAfter, tree.GetRoot().Members[^2].Green);
            Assert.Same(originalMarkup, tree.GetRoot().Members[^1].Green);
        }
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void StateBindingPathTypedAndDeletedCharacterByCharacterMatchesFreshAndReusesNeighbors(
        string newline)
    {
        var source =
            "state object neighbor = new();" + newline +
            "state string name = ;" + newline +
            "<Border />" + newline;
        var text = SourceText.From(source);
        var tree = ComponentSyntaxTree.ParseText(text, "StateDemo.akbura");
        var position = source.IndexOf(';', source.IndexOf("state string name", StringComparison.Ordinal));
        var originalNeighbor = tree.GetRoot().Members[0].Green;
        var originalMarkup = tree.GetRoot().Members[^1].Green;
        const string binding = "bind vm.Items[0].Name";

        foreach (var character in binding)
        {
            text = text.WithChanges(new TextChange(
                new TextSpan(position, 0),
                character.ToString()));
            tree = tree.WithChangedText(text);
            position++;

            AssertMatchesFresh(tree, text);
            Assert.Same(originalNeighbor, tree.GetRoot().Members[0].Green);
            AssertMarkupReusedWhenRecovered(tree, originalMarkup);
        }

        for (var index = binding.Length - 1; index >= 0; index--)
        {
            position--;
            text = text.WithChanges(new TextChange(
                new TextSpan(position, 1),
                string.Empty));
            tree = tree.WithChangedText(text);

            AssertMatchesFresh(tree, text);
            Assert.Same(originalNeighbor, tree.GetRoot().Members[0].Green);
            AssertMarkupReusedWhenRecovered(tree, originalMarkup);
        }
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void StateBindingModeTransitionsMatchFreshAndPreserveNeighbors(string newline)
    {
        var source =
            "state object vm = new();" + newline +
            "state string value = bind vm.Name;" + newline +
            "state int neighbor = 1;" + newline;
        var text = SourceText.From(source);
        var tree = ComponentSyntaxTree.ParseText(text, "StateDemo.akbura");
        var originalFirst = tree.GetRoot().Members[0].Green;
        var originalLast = tree.GetRoot().Members[^1].Green;

        foreach (var mode in new[] { "out", "in", "bind" })
        {
            var current = text.ToString();
            var equals = current.IndexOf("value = ", StringComparison.Ordinal) + "value = ".Length;
            var end = current.IndexOf(' ', equals);
            text = text.WithChanges(new TextChange(
                TextSpan.FromBounds(equals, end),
                mode));
            tree = tree.WithChangedText(text);

            AssertMatchesFresh(tree, text);
            Assert.Same(originalFirst, tree.GetRoot().Members[0].Green);
            Assert.Same(originalLast, tree.GetRoot().Members[^1].Green);
        }
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void ParameterDeclarationTypedCharacterByCharacterMatchesFreshAndReusesNeighbors(string newline)
    {
        var source =
            "namespace Demo;" + newline + newline +
            "param int Neighbor;" + newline +
            "param ";
        var text = SourceText.From(source);
        var tree = ComponentSyntaxTree.ParseText(
            text,
            "EditorField.akbura");
        var position = source.LastIndexOf(
                "param ",
                StringComparison.Ordinal) +
            "param ".Length;
        var originalNeighbor = Assert.Single(
            tree.GetRoot().Members,
            static member => member.ToFullString().Contains(
                "param int Neighbor;",
                StringComparison.Ordinal)).Green;

        AssertMatchesFresh(tree, text);
        foreach (var character in "bind bool IsActive = false;")
        {
            text = text.WithChanges(new TextChange(
                new TextSpan(position, 0),
                character.ToString()));
            tree = tree.WithChangedText(text);
            position++;

            AssertMatchesFresh(tree, text);
            Assert.Same(
                originalNeighbor,
                Assert.Single(
                    tree.GetRoot().Members,
                    static member => member.ToFullString().Contains(
                        "param int Neighbor;",
                        StringComparison.Ordinal)).Green);
        }
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void ParameterBindingModeTransitionsMatchFreshAndPreserveSibling(string newline)
    {
        var source =
            "namespace Demo;" + newline + newline +
            "param bool IsActive = false;" + newline +
            "param int Neighbor;" + newline + newline +
            "<Border />" + newline;
        var text = SourceText.From(source);
        var tree = ComponentSyntaxTree.ParseText(
            text,
            "EditorField.akbura");
        var originalNeighbor = tree.GetRoot().Members[^2].Green;
        var originalMarkup = tree.GetRoot().Members[^1].Green;

        foreach (var mode in new[] { "bind ", "out ", string.Empty })
        {
            var current = text.ToString();
            var declarationStart = current.IndexOf(
                "param ",
                StringComparison.Ordinal);
            var typeStart = current.IndexOf(
                "bool",
                declarationStart,
                StringComparison.Ordinal);
            var modeSpan = TextSpan.FromBounds(
                declarationStart + "param ".Length,
                typeStart);
            text = text.WithChanges(new TextChange(modeSpan, mode));
            tree = tree.WithChangedText(text);

            AssertMatchesFresh(tree, text);
            Assert.Same(
                originalNeighbor,
                tree.GetRoot().Members[^2].Green);
            Assert.Same(
                originalMarkup,
                tree.GetRoot().Members[^1].Green);
        }
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void AttributeBindingModeTransitionsMatchFreshAndPreserveSibling(string newline)
    {
        var source =
            "namespace Demo;" + newline + newline +
            "state string text = \"\";" + newline + newline +
            "<StackPanel>" + newline +
            "    <TextBox Text={text} />" + newline +
            "    <TextBlock Text={text} />" + newline +
            "</StackPanel>" + newline;
        var text = SourceText.From(source);
        var tree = ComponentSyntaxTree.ParseText(
            text,
            "EditorField.akbura");

        foreach (var attributeName in new[] { "bind:Text", "out:Text", "Text" })
        {
            var current = text.ToString();
            var currentName = new[] { "bind:Text", "out:Text", "Text" }
                .First(candidate => current.Contains(
                    candidate + "={text}",
                    StringComparison.Ordinal));
            var replacementStart = current.IndexOf(
                currentName + "={text}",
                StringComparison.Ordinal);
            text = text.WithChanges(new TextChange(
                new TextSpan(
                    replacementStart,
                    currentName.Length),
                attributeName));
            tree = tree.WithChangedText(text);

            AssertMatchesFresh(tree, text);
            var root = tree.GetRoot();
            Assert.Equal(
                "TextBlock",
                root.DescendantNodes()
                    .OfType<MarkupStartTagSyntax>()
                    .Last()
                    .Name.ToFullString().Trim());
        }
    }

    [Theory]
    [InlineData("\n", "bind:Text={text} ")]
    [InlineData("\n", "out:Text={text} ")]
    [InlineData("\r\n", "bind:Text={text} ")]
    [InlineData("\r\n", "out:Text={text} ")]
    public void DirectionalAttributeTypedAndDeletedCharacterByCharacterMatchesFreshAndReusesNeighbor(string newline, string attribute)
    {
        var source =
            "namespace Demo;" + newline + newline +
            "state string text = \"\";" + newline + newline +
            "<StackPanel>" + newline +
            "    <TextBlock Text={text} />" + newline +
            "    <TextBox />" + newline +
            "</StackPanel>" + newline;
        var text = SourceText.From(source);
        var tree = ComponentSyntaxTree.ParseText(
            text,
            "EditorField.akbura");
        var position = source.IndexOf(
            "/>",
            source.IndexOf("<TextBox", StringComparison.Ordinal),
            StringComparison.Ordinal);
        var originalNeighbor = Assert.Single(
            tree.GetRoot().DescendantNodes()
                .OfType<MarkupElementSyntax>(),
            static element =>
                element.StartTag!.Name.ToFullString().Trim() ==
                    "TextBlock").Green;

        foreach (var character in attribute)
        {
            text = text.WithChanges(new TextChange(
                new TextSpan(position, 0),
                character.ToString()));
            tree = tree.WithChangedText(text);
            position++;

            AssertMatchesFresh(tree, text);
            Assert.Same(originalNeighbor, GetTextBlock(tree).Green);
        }

        for (var index = attribute.Length - 1; index >= 0; index--)
        {
            position--;
            text = text.WithChanges(new TextChange(
                new TextSpan(position, 1),
                string.Empty));
            tree = tree.WithChangedText(text);

            AssertMatchesFresh(tree, text);
            Assert.Same(originalNeighbor, GetTextBlock(tree).Green);
        }
    }

    private static void AssertMatchesFresh(ComponentSyntaxTree incremental, SourceText text)
    {
        var fresh = ComponentSyntaxTree.ParseText(
            text,
            incremental.FilePath);
        var expected = Describe(fresh.GetRoot());
        var actual = Describe(incremental.GetRoot());

        Assert.Equal(text.ToString(), incremental.GetRoot().ToFullString());
        Assert.True(
            expected.SequenceEqual(actual),
            "Incremental syntax differs from fresh syntax for: " +
                text.ToString()
                    .Replace("\r", "\\r", StringComparison.Ordinal)
                    .Replace("\n", "\\n", StringComparison.Ordinal));
        Assert.Equal(expected, actual);
    }

    private static string[] Describe(AkburaSyntax root)
    {
        return root.DescendantNodesAndTokensAndSelf(descendIntoTrivia: true)
            .Select(static node =>
                $"{node.Kind}|{node.IsMissing}|{node.Span}|" +
                $"{node.FullSpan}|{node.ToFullString()}|" +
                string.Join(
                    ";",
                    node.GetDiagnostics().Select(static diagnostic =>
                        diagnostic.Code + ":" +
                        diagnostic.Message)))
            .ToArray();
    }

    private static MarkupElementSyntax GetTextBlock(ComponentSyntaxTree tree)
    {
        return Assert.Single(
            tree.GetRoot().DescendantNodes()
                .OfType<MarkupElementSyntax>(),
            static element =>
                element.StartTag!.Name.ToFullString().Trim() ==
                    "TextBlock");
    }

    private static void AssertMarkupReusedWhenRecovered(
        ComponentSyntaxTree tree,
        object originalMarkup)
    {
        if (tree.GetRoot().Members[^1] is MarkupRootSyntax markup)
        {
            Assert.Same(originalMarkup, markup.Green);
        }
    }
}
