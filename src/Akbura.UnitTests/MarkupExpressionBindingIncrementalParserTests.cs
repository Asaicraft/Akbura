using Akbura.Language;
using Akbura.Language.Syntax;
using Akbura.Language.Syntax.Green;
using Microsoft.CodeAnalysis.Text;

namespace Akbura.UnitTests;

public sealed class MarkupExpressionBindingIncrementalParserTests
{
    [Theory]
    [MemberData(nameof(MarkupExpressionBindingParserTests.Expressions), MemberType = typeof(MarkupExpressionBindingParserTests))]
    public void ComplexExpressionEdits_MatchFreshParse(string expression)
    {
        var tree = ComponentSyntaxTree.ParseText("<TextBlock Text=${x.bind Vm.Name} Width=\"100\" />");
        AssertMatchesFreshParse(Replace(tree, "Vm.Name", expression));
    }

    [Theory]
    [InlineData("Vm.Name", "Vm.FullName")]
    [InlineData("Vm.Count", "Vm.Count + 1")]
    [InlineData("Vm.Name", "Vm.Person.Name")]
    [InlineData("Vm.Name", "$\"{Vm.FirstName} {Vm.LastName}\"")]
    [InlineData("Vm.Person.Name", "Vm.Person?.Name ?? \"Unknown\"")]
    [InlineData("Vm.Name", "vm.Name")]
    [InlineData("Vm.Name", "Vm.Names")]
    [InlineData("Vm.Items[3].Name", "Vm.Items[Vm.Index + 1].Name")]
    [InlineData("new Person { Name = Vm.Name }", "new Person { Name = $\"{Vm.Name}\" }")]
    [InlineData("Vm.Name", "(Vm.Name")]
    [InlineData("(Vm.Name", "(Vm.Name)")]
    [InlineData("Vm.Name", "Vm.Name /* } /> */")]
    public void ExpressionEdits_MatchFreshParseAndReuseUnaffectedSiblings(string before, string after)
    {
        var code = "state int before = 0;\r\n<Grid Tag=\"before\" Text=${x.bind " + before + "} Width=\"100\" ToolTip=${x.bind Vm.Tip}><Button Content=\"Next\" /></Grid>\r\nstate int after = 1;";
        var tree = ComponentSyntaxTree.ParseText(code);
        var oldBindings = Bindings(tree);
        var updated = Replace(tree, before, after);
        AssertMatchesFreshParse(updated);
        var newBindings = Bindings(updated);
        var oldAttributes = Attributes(tree);
        var newAttributes = Attributes(updated);

        Assert.NotSame(oldBindings[0].Green, newBindings[0].Green);
        Assert.NotSame(oldBindings[0].Expression.Tokens[0].Node, newBindings[0].Expression.Tokens[0].Node);
        Assert.Equal(after, newBindings[0].Expression.ToFullString());
        Assert.Same(oldBindings[1].Green, newBindings[1].Green);
        Assert.Same(oldAttributes[0].Green, newAttributes[0].Green);
        Assert.Same(oldAttributes[2].Green, newAttributes[2].Green);
        Assert.Same(tree.GreenRoot.Members[0], updated.GreenRoot.Members[0]);
        Assert.Same(tree.GreenRoot.Members[^1], updated.GreenRoot.Members[^1]);
        Assert.Same(tree.GetRoot().DescendantNodes().OfType<MarkupElementSyntax>().Last().Green,
            updated.GetRoot().DescendantNodes().OfType<MarkupElementSyntax>().Last().Green);
    }

    [Theory]
    [InlineData("${Binding Name}", "${x.bind Vm.Name}")]
    [InlineData("${x.bind Vm.Name}", "${CustomExtension Name}")]
    [InlineData("${x.bind Vm.Name}", "${x.other Vm.Name}")]
    [InlineData("${x.other Vm.Name}", "${x.bind Vm.Name}")]
    [InlineData("${x.bind Vm.Name}", "${x.bindable Vm.Name}")]
    [InlineData("${x.bindable Vm.Name}", "${x.bind Vm.Name}")]
    [InlineData("${x.bind Vm.Name}", "${X.bind Vm.Name}")]
    [InlineData("${x.bind Vm.Name}", "${x bind Vm.Name}")]
    [InlineData("${x.bind Vm.Name}", "${x. bind Vm.Name}")]
    [InlineData("${x.bind Vm.Name}", "${x.bindVm.Name}")]
    [InlineData("${x.bind Vm.Name}", "${x.bind\tVm.Name}")]
    [InlineData("${x.bind Vm.Name}", "${x.bind /* comment */ Vm.Name}")]
    [InlineData("${x.bind /* comment */ Vm.Name}", "${x.bind Vm.Name}")]
    [InlineData("${x.bind Vm.Name}", "${x.bind\r\n /* before */ Vm.Name /* after */\r\n}")]
    [InlineData("${x.bind Vm.Name}", "${x.bind Vm.Name")]
    [InlineData("${x.bind Vm.Name", "${x.bind Vm.Name}")]
    [InlineData("${x.bind Vm.Name}", "\"Literal\"")]
    [InlineData("\"Literal\"", "${x.bind Vm.Name}")]
    public void IntroducerAndCategoryEdits_MatchFreshParse(string before, string after)
    {
        var tree = ComponentSyntaxTree.ParseText("<Grid Tag=\"before\" Text=" + before + " /><Button Content=\"Next\" />");
        var updated = Replace(tree, before, after);

        AssertMatchesFreshParse(updated);
        Assert.Equal(ComponentSyntaxTree.ParseText(updated.Text).GetRoot().DescendantNodes().OfType<MarkupExpressionBindingSyntax>().Count(),
            Bindings(updated).Length);
    }

    [Theory]
    [InlineData("", "Text=${x.bind Vm.Name} ")]
    [InlineData("Text=${x.bind Vm.Name} ", "")]
    [InlineData("Text=${x.bind Vm.Name} ", "Text=\"Literal\" ")]
    public void InsertDeleteAndReplaceBinding_MatchFreshParse(string before, string after)
    {
        const string prefix = "<Grid Tag=\"before\" ";
        var code = prefix + before + "Width=\"100\" />";
        var tree = ComponentSyntaxTree.ParseText(code);
        var newText = tree.Text.WithChanges(new TextChange(new TextSpan(prefix.Length, before.Length), after));
        var updated = tree.WithChangedText(newText);

        AssertMatchesFreshParse(updated);
        Assert.Same(Attributes(tree)[0].Green, Attributes(updated)[0].Green);
        Assert.Same(Attributes(tree)[^1].Green, Attributes(updated)[^1].Green);
    }

    [Fact]
    public void ChangingAttributeName_ReusesCompleteBinding()
    {
        var tree = ComponentSyntaxTree.ParseText("<TextBlock Text=${x.bind Vm.Name} />");
        var updated = Replace(tree, " Text=", " ToolTip=");

        AssertMatchesFreshParse(updated);
        Assert.Same(Bindings(tree)[0].Green, Bindings(updated)[0].Green);
    }

    [Fact]
    public void SuccessiveExpressionAndIntroducerEdits_DoNotKeepStaleTokens()
    {
        const string prefix = "<Grid Tag=\"before\" Text=";
        const string suffix = " Width=\"100\" ToolTip=${x.bind Vm.Tip} />";
        string[] values =
        [
            "${x.bind Vm.Name}",
            "${x.bind Vm.Names}",
            "${x.bind $\"{Vm.FirstName} {Vm.LastName}\"}",
            "${x.bind new[] { Vm.Name, Vm.Tip }[0]}",
            "${x.bind new[] { Vm.Name, Vm.Tip [0]}",
            "${Binding Name}",
            "${x.bind Vm.Person?.Name ?? \"Unknown\"}",
            "${x.bind Vm.}",
            "${x.bind }",
            "${x.bind Vm.Name}",
            "\"Done\"",
        ];
        var tree = ComponentSyntaxTree.ParseText(prefix + values[0] + suffix);
        for (var i = 1; i < values.Length; i++)
        {
            tree = Replace(tree, values[i - 1], values[i]);
            AssertMatchesFreshParse(tree);
        }
    }

    [Theory]
    [InlineData("${x.bind }")]
    [InlineData("${x.bind (Vm.Count + 1}")]
    [InlineData("${x.bind Vm.Name ")]
    public void RecoveryNodes_AreReparsedWhenExpressionIsCompleted(string before)
    {
        var tree = ComponentSyntaxTree.ParseText("<TextBlock Text=" + before + "/><Button Content=\"Next\" />");
        var updated = Replace(tree, before, "${x.bind Vm.Name}");

        AssertMatchesFreshParse(updated);
        Assert.NotSame(Bindings(tree)[0].Green, Bindings(updated)[0].Green);
        Assert.False(updated.GetRoot().ContainsDiagnostics);
    }

    [Fact]
    public void SingleCharacterEditsAcrossBinding_MatchFreshParse()
    {
        const string binding = "${x.bind Vm.Items[3].Name}";
        const string prefix = "<Grid Text=";
        var tree = ComponentSyntaxTree.ParseText(prefix + binding + " Width=\"100\" /><Button Content=\"Next\" />");
        for (var offset = 0; offset <= binding.Length; offset++)
        {
            foreach (var inserted in new[] { "x", " ", "}", ".", "/", "\"" })
            {
                var newText = tree.Text.WithChanges(new TextChange(new TextSpan(prefix.Length + offset, 0), inserted));
                AssertEditMatchesFreshParse(tree, newText, offset, "insert " + inserted);
                var edited = tree.WithChangedText(newText);
                AssertEditMatchesFreshParse(edited, newText.WithChanges(new TextChange(
                    new TextSpan(prefix.Length + offset, inserted.Length), string.Empty)), offset, "undo insert " + inserted);
            }

            if (offset < binding.Length)
            {
                var newText = tree.Text.WithChanges(new TextChange(new TextSpan(prefix.Length + offset, 1), string.Empty));
                AssertEditMatchesFreshParse(tree, newText, offset, "delete");
                var edited = tree.WithChangedText(newText);
                AssertEditMatchesFreshParse(edited, newText.WithChanges(new TextChange(
                    new TextSpan(prefix.Length + offset, 0), binding[offset].ToString())), offset, "undo delete");
            }
        }
    }

    private static void AssertEditMatchesFreshParse(ComponentSyntaxTree tree, SourceText newText, int offset, string edit)
    {
        try
        {
            AssertMatchesFreshParse(tree.WithChangedText(newText));
        }
        catch (Xunit.Sdk.XunitException exception)
        {
            Assert.Fail($"Binding offset {offset}, {edit}: {newText}\n{exception}");
        }
    }

    private static MarkupExpressionBindingSyntax[] Bindings(ComponentSyntaxTree tree) =>
        tree.GetRoot().DescendantNodes().OfType<MarkupExpressionBindingSyntax>().ToArray();

    private static MarkupPlainAttributeSyntax[] Attributes(ComponentSyntaxTree tree) =>
        tree.GetRoot().DescendantNodes().OfType<MarkupPlainAttributeSyntax>().ToArray();

    private static ComponentSyntaxTree Replace(ComponentSyntaxTree tree, string before, string after)
    {
        var code = tree.Text.ToString();
        var start = code.IndexOf(before, StringComparison.Ordinal);
        Assert.True(start >= 0);
        // Use the smallest changed range, including edits at a token boundary.
        var commonPrefix = 0;
        while (commonPrefix < before.Length && commonPrefix < after.Length && before[commonPrefix] == after[commonPrefix]) commonPrefix++;
        var oldEnd = before.Length;
        var newEnd = after.Length;
        while (oldEnd > commonPrefix && newEnd > commonPrefix && before[oldEnd - 1] == after[newEnd - 1])
        {
            oldEnd--;
            newEnd--;
        }

        return tree.WithChangedText(tree.Text.WithChanges(new TextChange(
            new TextSpan(start + commonPrefix, oldEnd - commonPrefix), after[commonPrefix..newEnd])));
    }

    internal static void AssertMatchesFreshParse(ComponentSyntaxTree incremental)
    {
        var code = incremental.Text.ToString();
        var fresh = ComponentSyntaxTree.ParseText(incremental.Text);
        Assert.Equal(code, incremental.GetRoot().ToFullString());
        Assert.Equal(code.Length, incremental.GetRoot().FullWidth);
        AssertGreen(fresh.GreenRoot, incremental.GreenRoot);
        var expected = fresh.GetRoot().DescendantNodesAndTokensAndSelf(descendIntoTrivia: true).ToArray();
        var actual = incremental.GetRoot().DescendantNodesAndTokensAndSelf(descendIntoTrivia: true).ToArray();
        Assert.Equal(expected.Length, actual.Length);
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i].RawKind, actual[i].RawKind);
            Assert.Equal(expected[i].Span, actual[i].Span);
            Assert.Equal(expected[i].FullSpan, actual[i].FullSpan);
            Assert.Equal(expected[i].IsMissing, actual[i].IsMissing);
        }
    }

    private static void AssertGreen(GreenNode expected, GreenNode actual)
    {
        Assert.Equal(expected.Kind, actual.Kind);
        Assert.Equal(expected.FullWidth, actual.FullWidth);
        Assert.Equal(expected.IsMissing, actual.IsMissing);
        Assert.Equal(expected.SlotCount, actual.SlotCount);
        Assert.Equal(expected.GetDiagnostics().Select(DescribeDiagnostic), actual.GetDiagnostics().Select(DescribeDiagnostic));
        if (expected is GreenSyntaxToken expectedToken)
        {
            var actualToken = Assert.IsAssignableFrom<GreenSyntaxToken>(actual);
            Assert.Equal(expectedToken.Text, actualToken.Text);
            Assert.Equal(expectedToken.ToFullString(), actualToken.ToFullString());
            if (expectedToken is GreenSyntaxToken.CSharpRawToken expectedRaw)
            {
                var actualRaw = Assert.IsType<GreenSyntaxToken.CSharpRawToken>(actual);
                Assert.Equal(expectedRaw.RawNode?.ToFullString(), actualRaw.RawNode?.ToFullString());
                Assert.Equal(expectedRaw.RawNode?.DescendantNodesAndTokensAndSelf().Select(n => (n.RawKind, n.Span, n.FullSpan, n.IsMissing)),
                    actualRaw.RawNode?.DescendantNodesAndTokensAndSelf().Select(n => (n.RawKind, n.Span, n.FullSpan, n.IsMissing)));
                Assert.Equal(expectedRaw.RawNode?.GetDiagnostics().Select(d => (d.Id, d.Severity, d.Location.SourceSpan)),
                    actualRaw.RawNode?.GetDiagnostics().Select(d => (d.Id, d.Severity, d.Location.SourceSpan)));
            }
        }

        for (var i = 0; i < expected.SlotCount; i++)
        {
            var expectedChild = expected.GetSlot(i);
            var actualChild = actual.GetSlot(i);
            Assert.Equal(expectedChild == null, actualChild == null);
            if (expectedChild != null) AssertGreen(expectedChild, actualChild!);
        }
    }

    private static object DescribeDiagnostic(AkburaDiagnostic diagnostic) =>
        (diagnostic.Code, diagnostic.Severity, diagnostic.Message,
            diagnostic is SyntaxDiagnosticInfo info ? info.Position : 0,
            diagnostic is SyntaxDiagnosticInfo width ? width.Width : 0);
}
