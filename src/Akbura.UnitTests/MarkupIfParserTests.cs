using Akbura.Language.Syntax;
using Akbura.Language.Syntax.Green;
using Microsoft.CodeAnalysis.Text;

namespace Akbura.UnitTests;

public sealed class MarkupIfParserTests
{
    [Theory]
    [InlineData("")]
    [InlineData("\r\n")]
    public void ConditionalAdjacentNamedChildren_ParseBothAndMatchIncrementalInsertion(string separator)
    {
        const string prefix = "<StackPanel>$if (ready) {";
        const string target = "<TextBlock Text=${Binding #outerSource.Text} />";
        const string named = "<TextBox x.Name=\"outerSource\" Text=\"inner scoped\" />";
        const string suffix = "}</StackPanel>";
        var original = prefix + target + suffix;
        var insertion = named + separator;
        var source = prefix + insertion + target + suffix;
        var tree = Parse(source);
        var statement = Assert.Single(GetRoot(tree).Body.OfType<MarkupIfStatementSyntax>());
        var children = Elements(statement.Body.Content).ToArray();

        Assert.False(tree.ContainsDiagnostics);
        Assert.Equal(source, tree.ToFullString());
        Assert.Equal(2, children.Length);
        Assert.Equal(named, children[0].ToString());
        Assert.Equal(target, children[1].ToString());
        AssertIncrementalMatchesFull(original, source,
            new TextChangeRange(new TextSpan(prefix.Length, 0), insertion.Length));
    }

    [Fact]
    public void ConditionalChain_IsOneContentStatementWithTypedBlocks()
    {
        const string source = "<StackPanel><TextBlock/> $if (ready) { <Button/> }\r\n" +
            "$else if (waiting) { <ProgressBar/> }\r\n$else { <Border/> } <Separator/></StackPanel>";
        var tree = Parse(source);
        var root = GetRoot(tree);
        var statement = Assert.Single(root.Body.OfType<MarkupIfStatementSyntax>());

        Assert.Equal(source, tree.ToFullString());
        Assert.False(tree.ContainsDiagnostics);
        Assert.Equal(SyntaxKind.DollarToken, statement.DollarToken.Kind);
        Assert.Equal("ready", statement.Condition.ToFullString().Trim());
        Assert.Single(Elements(statement.Body.Content));
        var elseIf = Assert.Single(statement.ElseIfClauses);
        Assert.Equal("waiting", elseIf.Condition.ToFullString().Trim());
        Assert.Single(Elements(elseIf.Body.Content));
        Assert.NotNull(statement.ElseClause);
        Assert.Single(Elements(statement.ElseClause.Body.Content));
        Assert.Equal(2, Elements(root.Body).Count());
    }

    [Theory]
    [InlineData("$if (ready) {}")]
    [InlineData("$if (ready) {} $else {}")]
    [InlineData("$if (ready) { $if (nested) { <Button/> } $else {} }")]
    [InlineData("$if (ready) { text } $else { other text }")]
    [InlineData("<Border.Child>$if (ready) { <Button/> } $else { <TextBlock/> }</Border.Child>")]
    public void ConditionalContent_NestingEmptyBlocksAndPropertyElementsRoundTrip(string content)
    {
        var source = "<Border>" + content + "</Border>";
        var tree = Parse(source);

        Assert.Equal(source, tree.ToFullString());
        Assert.False(tree.ContainsDiagnostics);
        Assert.NotEmpty(((AkburaDocumentSyntax)tree.CreateRed()).DescendantNodes()
            .OfType<MarkupIfStatementSyntax>());
    }

    [Fact]
    public void ConditionalChain_CommentsBetweenBranchesBelongToSameStatement()
    {
        const string source = "<Border>$if (ready) {} /* branch */ $else if (waiting) {} " +
            "// final branch\r\n$else {}</Border>";
        var tree = Parse(source);
        var statement = Assert.Single(GetRoot(tree).Body.OfType<MarkupIfStatementSyntax>());

        Assert.Equal(source, tree.ToFullString());
        Assert.False(tree.ContainsDiagnostics);
        Assert.Single(statement.ElseIfClauses);
        Assert.NotNull(statement.ElseClause);
    }

    [Theory]
    [InlineData("Call(outer(inner(1)), values[2])")]
    [InlineData("text == \"}) { (\"")]
    [InlineData("character == ')' ")]
    [InlineData("text == @\"}) { (\"")]
    [InlineData("text == $\"{Call(1)} ) }} (\"")]
    [InlineData("text == \"\"\" }) { ( \"\"\"")]
    [InlineData("text == $$\"\"\" {{Call(1)}} ) } ( \"\"\"")]
    [InlineData("ready /* ) { } */ && other")]
    [InlineData("ready // ) { }\r\n && other")]
    [InlineData("new Item { Name = \"})\" }.Name.Length > 0")]
    [InlineData("items.Any(value => { return value > 0; })")]
    [InlineData("value is Item { Name: not null }")]
    [InlineData("(left, right) == (1, 2)")]
    public void Condition_UsesBalancedCSharpFragmentRatherThanFirstCloseParenthesis(string condition)
    {
        var source = "<Border>$if (" + condition + ") { <Button/> }</Border>";
        var tree = Parse(source);
        var statement = Assert.Single(GetRoot(tree).Body.OfType<MarkupIfStatementSyntax>());

        Assert.Equal(source, tree.ToFullString());
        Assert.False(tree.ContainsDiagnostics);
        Assert.Equal(condition.Trim(), statement.Condition.ToFullString().Trim());
        var expression = Assert.IsAssignableFrom<Microsoft.CodeAnalysis.CSharp.Syntax.ExpressionSyntax>(
            statement.Condition.GetRawCSharpExpression());
        Assert.False(expression.ContainsDiagnostics);
        Assert.Single(Elements(statement.Body.Content));
    }

    [Theory]
    [InlineData("<Border>$ifdef ready</Border>")]
    [InlineData("<Border>$ if ready</Border>")]
    [InlineData("<Border Text=\"$if\"/>")]
    [InlineData("<Border Text=${Binding Path=Title}/>")]
    public void ExistingTextAndAttributes_AreNotConditionalDirectives(string source)
    {
        var tree = Parse(source);

        Assert.Equal(source, tree.ToFullString());
        Assert.Empty(((AkburaDocumentSyntax)tree.CreateRed()).DescendantNodes()
            .OfType<MarkupIfStatementSyntax>());
    }

    [Fact]
    public void NestedElementText_CloseBraceDoesNotCloseEnclosingConditionalBlock()
    {
        const string source = "<Border>$if (ready) { <Button>literal } text</Button> }</Border>";
        var tree = Parse(source);
        var statement = Assert.Single(GetRoot(tree).Body.OfType<MarkupIfStatementSyntax>());
        var button = Assert.Single(Elements(statement.Body.Content));

        Assert.Equal(source, tree.ToFullString());
        Assert.False(tree.ContainsDiagnostics);
        Assert.Contains("literal } text", string.Concat(button.Body.Select(static content => content.ToFullString())));
        Assert.False(statement.Body.CloseBraceToken.IsMissing);
    }

    [Theory]
    [InlineData("$else { <Button/> }")]
    [InlineData("$if (ready) {} $else {} $else if (waiting) {}")]
    [InlineData("$if (ready) {} <Button/> $else {}")]
    [InlineData("$if (ready) {} meaningful $else {}")]
    [InlineData("$if (ready { <Button/> }")]
    [InlineData("$if ready) { <Button/> }")]
    [InlineData("$if () { <Button/> }")]
    [InlineData("$if (   ) { <Button/> }")]
    [InlineData("$if (/* empty */) { <Button/> }")]
    [InlineData("$if (ready) { <Button/>")]
    [InlineData("$if (ready) <Button/> }")]
    public void InvalidConditionalInput_ReportsErrorsAndPreservesOwnerClosingTag(string content)
    {
        var source = "<Border>" + content + "</Border>";
        var tree = Parse(source);
        var root = GetRoot(tree);

        Assert.Equal(source, tree.ToFullString());
        Assert.True(tree.ContainsDiagnostics);
        Assert.NotNull(root.EndTag);
        Assert.Equal("</Border>", root.EndTag.ToFullString().Trim());
    }

    [Theory]
    [InlineData("<Border>$if")]
    [InlineData("<Border>$if (")]
    [InlineData("<Border>$if (ready)")]
    [InlineData("<Border>$else")]
    [InlineData("<Border>$if (ready) {} $else if (")]
    public void IncompleteConditionalInput_RoundTripsAndTerminates(string source)
    {
        var tree = Parse(source);

        Assert.Equal(source, tree.ToFullString());
        Assert.True(tree.ContainsDiagnostics);
    }

    [Theory]
    [InlineData("value is Item { Name: not null }")]
    [InlineData("new Item { Ready = true }")]
    [InlineData("new() { Ready = true }")]
    [InlineData("value is Item")]
    [InlineData("new Item()")]
    public void MissingConditionCloseParen_AfterInitializerOrPatternPreservesMarkupBody(string condition)
    {
        var source = "<Border>$if (" + condition + " { <Button/> }</Border>";
        var tree = Parse(source);
        var root = GetRoot(tree);
        var statement = Assert.Single(root.Body.OfType<MarkupIfStatementSyntax>());

        Assert.Equal(source, tree.ToFullString());
        Assert.True(tree.ContainsDiagnostics);
        Assert.True(statement.CloseParenToken.IsMissing);
        Assert.Equal(condition, statement.Condition.ToFullString().Trim());
        Assert.Single(Elements(statement.Body.Content));
        Assert.Equal("</Border>", root.EndTag!.ToFullString().Trim());
    }

    [Theory]
    [InlineData("ready", "other && Call(1)")]
    [InlineData("<Button/>", "<TextBlock Text=\"changed\"/>")]
    [InlineData("$if (nested) {}", "$if (nested) {} $else { <Border/> }")]
    [InlineData("$if (ready) { <Button/> }", "$if (ready) { <Button/> } $else { <TextBlock/> }")]
    [InlineData("$if (ready) { <Button/> }", "$if (ready) { <Button/> } $else if (waiting) {}")]
    [InlineData("$else { <TextBlock/> }", "")]
    [InlineData("$else if (waiting) {}", "$else { <TextBlock/> }")]
    [InlineData("plain text", "$if (ready) {}")]
    [InlineData("$if (ready) {}", "plain text")]
    [InlineData("text == \"})\"", "text == $$\"\"\" {{Call(1)}} ) } \"\"\"")]
    public void IncrementalConditionalEdits_MatchFullTree(string oldFragment, string newFragment)
    {
        const string prefix = "<StackPanel><Separator/>";
        const string suffix = "<ProgressBar/></StackPanel>";
        var content = oldFragment switch
        {
            "ready" => "$if (ready) { <Button/> }",
            "<Button/>" => "$if (ready) { <Button/> }",
            "$if (nested) {}" => "$if (ready) { $if (nested) {} }",
            "$else { <TextBlock/> }" => "$if (ready) {} $else { <TextBlock/> }",
            "$else if (waiting) {}" => "$if (ready) {} $else if (waiting) {}",
            "text == \"})\"" => "$if (text == \"})\") { <Button/> }",
            _ => oldFragment,
        };
        var oldSource = prefix + content + suffix;
        var changeStart = oldSource.IndexOf(oldFragment, prefix.Length, StringComparison.Ordinal);
        var newSource = oldSource.Remove(changeStart, oldFragment.Length).Insert(changeStart, newFragment);
        AssertIncrementalMatchesFull(oldSource, newSource,
            new TextChangeRange(new TextSpan(changeStart, oldFragment.Length), newFragment.Length));
    }

    [Fact]
    public void IncrementalAppendElse_ReparsesChainButReusesUnchangedSiblings()
    {
        const string oldSource = "<StackPanel><Separator/>$if (ready) {}<ProgressBar/></StackPanel>";
        const string inserted = "$else { <Button/> }";
        var position = oldSource.IndexOf("<ProgressBar/>", StringComparison.Ordinal);
        var newSource = oldSource.Insert(position, inserted);
        var oldTree = Parse(oldSource);
        var oldRoot = GetRoot(oldTree);
        using var parser = ParserHelper.MakeIncrementalParser(newSource,
            (AkburaDocumentSyntax)oldTree.CreateRed(),
            [new TextChangeRange(new TextSpan(position, 0), inserted.Length)]);
        var incremental = parser.ParseCompilationUnit();
        var newRoot = GetRoot(incremental);

        AssertTreeEqual(Parse(newSource), incremental);
        var statement = Assert.Single(newRoot.Body.OfType<MarkupIfStatementSyntax>());
        Assert.NotNull(statement.ElseClause);
        Assert.Same(Elements(oldRoot.Body).First().Green,
            Elements(newRoot.Body).First().Green);
        Assert.Same(Elements(oldRoot.Body).Last().Green,
            Elements(newRoot.Body).Last().Green);
    }

    [Theory]
    [InlineData("<Border>$if (ready) { <Button/></Border>", "<Border>$if (ready) { <Button/> }</Border>")]
    [InlineData("<Border>$if (ready { <Button/> }</Border>", "<Border>$if (ready) { <Button/> }</Border>")]
    [InlineData("<Border>$if (ready) {} $else if (waiting) {}</Border>", "<Border>$if (ready) {} $else if (waiting) {} $else {}</Border>")]
    [InlineData("<Border>$if (ready) {} $else {}</Border>", "<Border>$if (ready) {} $else {} $else {}</Border>")]
    public void IncrementalRecoveryAndChainBoundaryEdits_MatchFullParse(string oldSource, string newSource)
    {
        var start = 0;
        while (start < oldSource.Length && start < newSource.Length && oldSource[start] == newSource[start])
        {
            start++;
        }
        var oldEnd = oldSource.Length;
        var newEnd = newSource.Length;
        while (oldEnd > start && newEnd > start && oldSource[oldEnd - 1] == newSource[newEnd - 1])
        {
            oldEnd--;
            newEnd--;
        }

        AssertIncrementalMatchesFull(oldSource, newSource,
            new TextChangeRange(TextSpan.FromBounds(start, oldEnd), newEnd - start));
    }

    [Fact]
    public void SyntaxUpdatesAndRewriters_RetainIdentityAndAnnotations()
    {
        var original = Assert.Single(GetRoot(Parse("<Border>$if (ready) {} $else if (waiting) {} $else {}</Border>"))
            .Body.OfType<MarkupIfStatementSyntax>());
        var annotation = new AkburaSyntaxAnnotation("test", "conditional");
        var statement = (MarkupIfStatementSyntax)original.WithAnnotations([annotation]);

        Assert.Same(statement, statement.WithBody(statement.Body));
        Assert.Same(statement, new IdentityRewriter().Visit(statement));
        Assert.Same(statement.Green, new GreenSyntaxRewriter().Visit(statement.Green));
        var changed = statement.WithCondition(original.ElseIfClauses[0].Condition);
        Assert.Contains(annotation, changed.GetAnnotations());
        Assert.Equal("waiting", changed.Condition.ToFullString().Trim());
        Assert.Throws<ArgumentException>(() => statement.WithIfKeyword(statement.DollarToken));
    }

    [Fact]
    public void ConditionalNodes_DispatchTypedVisitorsAndPreserveDiagnosticsOnUpdate()
    {
        var tree = Parse("<Border>$if (ready) {} $else if (waiting) {} $else {}</Border>");
        var nodes = ((AkburaDocumentSyntax)tree.CreateRed()).DescendantNodes()
            .Where(static node => node is MarkupIfStatementSyntax or MarkupElseIfClauseSyntax or
                MarkupElseClauseSyntax or MarkupBlockSyntax).ToArray();
        var visitor = new ConditionalVisitor();
        var resultVisitor = new ConditionalResultVisitor();
        var parameterVisitor = new ConditionalParameterVisitor();
        foreach (var node in nodes)
        {
            visitor.Visit(node);
            Assert.Equal(node.Kind, resultVisitor.Visit(node));
            Assert.Equal("visited:" + node.Kind, parameterVisitor.Visit(node, "visited:"));
        }

        Assert.Equal(nodes.Length, visitor.Count);
        var invalid = Assert.Single(GetRoot(Parse("<Border>$if (ready) {} $else {} $else {}</Border>"))
            .Body.OfType<MarkupIfStatementSyntax>(), static statement => statement.IfKeyword.IsMissing);
        var changed = invalid.WithBody(nodes.OfType<MarkupBlockSyntax>().First());
        Assert.True(changed.ContainsDiagnostics);
        Assert.Equal(invalid.GetDiagnostics().Select(static diagnostic => diagnostic.Code),
            changed.GetDiagnostics().Select(static diagnostic => diagnostic.Code));
    }

    private static GreenAkburaDocumentSyntax Parse(string source)
    {
        using var parser = ParserHelper.MakeParser(source);
        return parser.ParseCompilationUnit();
    }

    private sealed class IdentityRewriter : SyntaxRewriter
    {
    }

    private sealed class ConditionalVisitor : SyntaxVisitor
    {
        public int Count { get; private set; }
        public override void VisitMarkupIfStatementSyntax(MarkupIfStatementSyntax node) => Count++;
        public override void VisitMarkupElseIfClauseSyntax(MarkupElseIfClauseSyntax node) => Count++;
        public override void VisitMarkupElseClauseSyntax(MarkupElseClauseSyntax node) => Count++;
        public override void VisitMarkupBlockSyntax(MarkupBlockSyntax node) => Count++;
    }

    private sealed class ConditionalResultVisitor : SyntaxVisitor<SyntaxKind>
    {
        public override SyntaxKind VisitMarkupIfStatementSyntax(MarkupIfStatementSyntax node) => node.Kind;
        public override SyntaxKind VisitMarkupElseIfClauseSyntax(MarkupElseIfClauseSyntax node) => node.Kind;
        public override SyntaxKind VisitMarkupElseClauseSyntax(MarkupElseClauseSyntax node) => node.Kind;
        public override SyntaxKind VisitMarkupBlockSyntax(MarkupBlockSyntax node) => node.Kind;
    }

    private sealed class ConditionalParameterVisitor : SyntaxVisitor<string, string>
    {
        public override string VisitMarkupIfStatementSyntax(MarkupIfStatementSyntax node, string argument) => argument + node.Kind;
        public override string VisitMarkupElseIfClauseSyntax(MarkupElseIfClauseSyntax node, string argument) => argument + node.Kind;
        public override string VisitMarkupElseClauseSyntax(MarkupElseClauseSyntax node, string argument) => argument + node.Kind;
        public override string VisitMarkupBlockSyntax(MarkupBlockSyntax node, string argument) => argument + node.Kind;
    }

    private static MarkupElementSyntax GetRoot(GreenAkburaDocumentSyntax tree) =>
        ((AkburaDocumentSyntax)tree.CreateRed()).DescendantNodes().OfType<MarkupElementSyntax>().First();

    private static IEnumerable<MarkupElementSyntax> Elements(SyntaxList<MarkupContentSyntax> content)
    {
        foreach (var child in content)
        {
            if (child is MarkupElementContentSyntax element)
            {
                yield return element.Element;
            }
        }
    }

    private static void AssertIncrementalMatchesFull(string oldSource, string newSource, TextChangeRange change)
    {
        var oldTree = Parse(oldSource);
        using var parser = ParserHelper.MakeIncrementalParser(newSource,
            (AkburaDocumentSyntax)oldTree.CreateRed(), [change]);
        var incremental = parser.ParseCompilationUnit();

        Assert.Equal(newSource, incremental.ToFullString());
        AssertTreeEqual(Parse(newSource), incremental);
    }

    private static void AssertTreeEqual(GreenNode expected, GreenNode actual)
    {
        Assert.Equal(expected.Kind, actual.Kind);
        Assert.Equal(expected.ToFullString(), actual.ToFullString());
        Assert.Equal(expected.SlotCount, actual.SlotCount);
        Assert.Equal(expected.IsMissing, actual.IsMissing);
        Assert.Equal(expected.GetDiagnostics().Select(static diagnostic => diagnostic.Code),
            actual.GetDiagnostics().Select(static diagnostic => diagnostic.Code));
        for (var i = 0; i < expected.SlotCount; i++)
        {
            var expectedChild = expected.GetSlot(i);
            var actualChild = actual.GetSlot(i);
            Assert.Equal(expectedChild == null, actualChild == null);
            if (expectedChild != null && actualChild != null)
            {
                AssertTreeEqual(expectedChild, actualChild);
            }
        }
    }
}
