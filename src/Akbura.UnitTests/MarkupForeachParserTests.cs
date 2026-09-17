using Akbura.Language;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis.Text;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Akbura.UnitTests;

public sealed class MarkupForeachParserTests
{
    [Fact]
    public void ForeachHeaderAndMixedGuards_HaveTypedSyntaxAndExactSpans()
    {
        const string source = "<StackPanel><TextBlock/>$foreach (var item in array; key: item.Id) {" +
            "var doubled = item * 2; if (doubled % 2 == 0) { continue; } " +
            "if (item == 3) { <Button/> break; } else if (item == 4) { <Border/> } " +
            "<TextBlock Text={$\"{item}:{@index}\"}/>}<Separator/></StackPanel>";
        var tree = Parse(source);
        var loop = Assert.Single(tree.GetRoot().DescendantNodes().OfType<MarkupForeachStatementSyntax>());
        var header = Assert.IsType<CSharp.ForEachStatementSyntax>(loop.Header.GetRawCSharpForeach());

        Assert.Equal(source, tree.GetRoot().ToFullString());
        Assert.False(tree.GetRoot().ContainsDiagnostics);
        Assert.Equal("item", header.Identifier.ValueText);
        Assert.Equal("array", header.Expression.ToString());
        Assert.Equal("item", source.Substring(loop.Header.GetAbsoluteCSharpSpan(header.Identifier.Span).Start,
            header.Identifier.Span.Length));
        Assert.Equal("item.Id", loop.KeyClause!.Expression.ToFullString().Trim());
        Assert.Equal(2, loop.Body.Content.OfType<MarkupCodeIfStatementSyntax>().Count());
        Assert.IsType<CSharp.LocalDeclarationStatementSyntax>(loop.Body.Content
            .OfType<MarkupCodeStatementSyntax>().First().GetRawCSharpStatement());
        Assert.Single(loop.Body.Content.OfType<MarkupCodeIfStatementSyntax>().Last().ElseBody!.Content
            .OfType<MarkupCodeIfStatementSyntax>());
        Assert.Equal(2, GetRootElement(tree).Body.OfType<MarkupElementContentSyntax>().Count());
    }

    [Theory]
    [InlineData("var item in values.Where(value => Call(value, 2))")]
    [InlineData("Dictionary<string, List<int>> item in values")]
    [InlineData("var item in new[] { 1, 2, 3 }")]
    [InlineData("var item in new List<int> { 1, 2, 3 }")]
    [InlineData("var item in Call(\"in; ) }\", @\";)\")")]
    [InlineData("var item in Call($\"{Call(1)} ) }}\")")]
    [InlineData("var item in Call(\"\"\"in; ) }\"\"\")")]
    [InlineData("var item /* in ;) */ in values // ;)\r\n")]
    [InlineData("var item in from value in values where value > 0 select value")]
    public void Header_BalancedRoslynFragmentsDoNotConsumeTheBody(string header)
    {
        var source = "<StackPanel>$foreach (" + header + ") { <Button/> }</StackPanel>";
        var tree = Parse(source);
        var loop = Assert.Single(tree.GetRoot().DescendantNodes().OfType<MarkupForeachStatementSyntax>());

        Assert.Equal(source, tree.GetRoot().ToFullString());
        Assert.False(tree.GetRoot().ContainsDiagnostics);
        Assert.Equal(header, loop.Header.Token.ToFullString());
        Assert.Single(loop.Body.Content.OfType<MarkupElementContentSyntax>());
    }

    [Fact]
    public void NestedConditionalsAndLoops_KeepOrdinaryElementTextInMarkupMode()
    {
        const string source = "<StackPanel>$foreach (var item in values) {" +
            "$if (item > 0) { if (item == 2) continue; <Button>if continue; } text</Button> } " +
            "$foreach (var nested in item.Values) { <TextBlock Text={nested}/> }}" +
            "<TextBlock>if (literal) continue;</TextBlock></StackPanel>";
        var tree = Parse(source);

        Assert.Equal(source, tree.GetRoot().ToFullString());
        Assert.False(tree.GetRoot().ContainsDiagnostics);
        Assert.Equal(2, tree.GetRoot().DescendantNodes().OfType<MarkupForeachStatementSyntax>().Count());
        Assert.Single(tree.GetRoot().DescendantNodes().OfType<MarkupCodeIfStatementSyntax>());
        Assert.Contains("if continue; } text", tree.GetRoot().DescendantNodes()
            .OfType<MarkupTextLiteralSyntax>().First().ToFullString());
    }

    [Theory]
    [InlineData("$foreach (var item values) { <Button/> }")]
    [InlineData("$foreach (var item in values { <Button/> }")]
    [InlineData("$foreach (var item in values) { <Button/>")]
    [InlineData("$foreach (var item in values; wrong: item) {}")]
    [InlineData("$foreach (ref var item in values) {}")]
    [InlineData("$foreach (var (left, right) in values) {}")]
    [InlineData("$foreach (var item in values) { while (ready) { break; } }")]
    [InlineData("$foreach (var item in values) { else { <Button/> } }")]
    [InlineData("$foreach (var item in values) { if (ready { <Button/> } }")]
    public void InvalidForeach_PreservesOwnerEndTagAndReportsSourceDiagnostics(string content)
    {
        var source = "<StackPanel>" + content + "</StackPanel>";
        var tree = Parse(source);

        Assert.Equal(source, tree.GetRoot().ToFullString());
        Assert.True(tree.GetRoot().ContainsDiagnostics);
        Assert.Equal("</StackPanel>", GetRootElement(tree).EndTag!.ToFullString().Trim());
    }

    [Theory]
    [InlineData("$foreach")]
    [InlineData("$foreach (")]
    [InlineData("$foreach (var")]
    [InlineData("$foreach (var item in")]
    [InlineData("$foreach (var item in values; key:")]
    [InlineData("$foreach (var item in values) { if (")]
    [InlineData("$fore")]
    public void IncompleteForeach_RoundTripsAndTerminates(string content)
    {
        var source = "<StackPanel>" + content;
        var tree = Parse(source);

        Assert.Equal(source, tree.GetRoot().ToFullString());
        Assert.True(tree.GetRoot().ContainsDiagnostics);
    }

    [Theory]
    [InlineData("$foreach (")]
    [InlineData("$foreach (var item in values; key: ")]
    public void MissingHeaderAndBody_DoNotConsumeTheFollowingSiblingElement(string partial)
    {
        var source = "<StackPanel>" + partial + "<Separator/></StackPanel>";
        var tree = Parse(source);
        var root = GetRootElement(tree);

        Assert.Equal(source, tree.GetRoot().ToFullString());
        Assert.True(tree.GetRoot().ContainsDiagnostics);
        Assert.Single(root.Body.OfType<MarkupElementContentSyntax>());
        Assert.Empty(Assert.Single(root.Body.OfType<MarkupForeachStatementSyntax>()).Body.Content);
    }

    [Fact]
    public void ForeachBodyEdit_ReusesHeaderAndUnchangedSiblingGreens()
    {
        const string source = "<StackPanel><Button/>$foreach (var item in values) {" +
            "<TextBlock Text={item}/><Border/>}<Separator/></StackPanel>";
        var oldTree = Parse(source);
        var oldLoop = Assert.Single(oldTree.GetRoot().DescendantNodes().OfType<MarkupForeachStatementSyntax>());
        var position = source.IndexOf("Text={item}", StringComparison.Ordinal) + "Text={".Length;
        var text = oldTree.Text.WithChanges(new TextChange(new TextSpan(position, 4), "item.Name"));
        var changed = oldTree.WithChangedText(text);
        var newLoop = Assert.Single(changed.GetRoot().DescendantNodes().OfType<MarkupForeachStatementSyntax>());
        var fresh = Parse(text.ToString());

        Assert.Equal(text.ToString(), changed.GetRoot().ToFullString());
        Assert.Equal(fresh.GetRoot().ContainsDiagnostics, changed.GetRoot().ContainsDiagnostics);
        Assert.Same(oldLoop.Header.Green, newLoop.Header.Green);
        Assert.Same(oldLoop.Body.Content.Last().Green, newLoop.Body.Content.Last().Green);
        Assert.Same(GetRootElement(oldTree).Body.First().Green, GetRootElement(changed).Body.First().Green);
        Assert.Same(GetRootElement(oldTree).Body.Last().Green, GetRootElement(changed).Body.Last().Green);
    }

    [Theory]
    [InlineData("values", "otherValues")]
    [InlineData("var item", "Customer item")]
    [InlineData("item.Id", "item.OtherId")]
    public void ForeachHeaderOrKeyEdit_PreservesUnchangedBodyAndSiblings(string oldValue, string newValue)
    {
        const string source = "<StackPanel><Button/>$foreach (var item in values; key: item.Id) {" +
            "<TextBlock Text={item}/>}<Separator/></StackPanel>";
        var tree = Parse(source);
        var oldLoop = Assert.Single(tree.GetRoot().DescendantNodes().OfType<MarkupForeachStatementSyntax>());
        var position = source.IndexOf(oldValue, StringComparison.Ordinal);
        var text = tree.Text.WithChanges(new TextChange(new TextSpan(position, oldValue.Length), newValue));
        var changed = tree.WithChangedText(text);
        var newLoop = Assert.Single(changed.GetRoot().DescendantNodes().OfType<MarkupForeachStatementSyntax>());

        Assert.Equal(text.ToString(), changed.GetRoot().ToFullString());
        Assert.Equal(Shape(Parse(text.ToString())), Shape(changed));
        Assert.Same(oldLoop.Body.Green, newLoop.Body.Green);
        Assert.Same(GetRootElement(tree).Body.First().Green, GetRootElement(changed).Body.First().Green);
        Assert.Same(GetRootElement(tree).Body.Last().Green, GetRootElement(changed).Body.Last().Green);
    }

    [Fact]
    public void MovingStatementInsideAnElement_ReparsesItAsLiteralMarkupText()
    {
        const string prefix = "<StackPanel>$foreach (var item in values) {";
        const string statement = "continue;";
        const string suffix = "}</StackPanel>";
        var tree = Parse(prefix + statement + suffix);
        var text = tree.Text.WithChanges(
            new TextChange(new TextSpan(prefix.Length, 0), "<TextBlock>"),
            new TextChange(new TextSpan(prefix.Length + statement.Length, 0), "</TextBlock>"));
        var changed = tree.WithChangedText(text);

        Assert.Equal(text.ToString(), changed.GetRoot().ToFullString());
        Assert.Equal(Shape(Parse(text.ToString())), Shape(changed));
        Assert.Empty(changed.GetRoot().DescendantNodes().OfType<MarkupCodeStatementSyntax>());
        Assert.Contains(statement, Assert.Single(changed.GetRoot().DescendantNodes()
            .OfType<MarkupTextLiteralSyntax>()).ToFullString());
    }

    [Fact]
    public void ForeachTyping_EachCharacterMatchesFreshSyntaxAndDiagnostics()
    {
        const string prefix = "<StackPanel><Button/>";
        const string suffix = "<Separator/></StackPanel>";
        const string insertion = "$foreach (var item in values; key: item.Id) { if (item > 0) { continue; } <TextBlock Text={item}/> }";
        var text = SourceText.From(prefix + suffix);
        var tree = Parse(text.ToString());
        var position = prefix.Length;
        foreach (var character in insertion)
        {
            text = text.WithChanges(new TextChange(new TextSpan(position++, 0), character.ToString()));
            tree = tree.WithChangedText(text);
            var fresh = Parse(text.ToString());

            Assert.Equal(text.ToString(), tree.GetRoot().ToFullString());
            Assert.Equal(Shape(fresh), Shape(tree));
        }
    }

    private static object[] Shape(ComponentSyntaxTree tree) => tree.GetRoot().DescendantNodesAndTokensAndSelf()
        .Select(node => (object)(node.RawKind, node.Span, node.FullSpan, node.IsMissing,
            string.Join("|", node.GetDiagnostics().Select(diagnostic => diagnostic.Code + ":" +
                string.Join(",", diagnostic.Parameters.Select(parameter => parameter?.ToString())))))).ToArray();

    private static ComponentSyntaxTree Parse(string source) => ComponentSyntaxTree.ParseText(source, "Loop.akbura");

    private static MarkupElementSyntax GetRootElement(ComponentSyntaxTree tree) =>
        Assert.IsType<MarkupRootSyntax>(tree.GetRoot().Members.Single()).Element;
}
