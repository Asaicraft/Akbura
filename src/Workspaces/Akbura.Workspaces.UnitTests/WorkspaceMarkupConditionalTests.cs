using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;
using SyntaxKind = Akbura.Language.Syntax.SyntaxKind;

namespace Akbura.Workspaces.UnitTests;

public sealed class WorkspaceMarkupConditionalTests
{
    [Theory]
    [InlineData("<Host>$|</Host>", "$")]
    [InlineData("<Host>$i|</Host>", "$i")]
    [InlineData("<Host>$if|</Host>", "$if")]
    [InlineData("<Host>\r\n    |\r\n</Host>", "")]
    [InlineData("<Host><Host.Children>$i|</Host.Children></Host>", "$i")]
    [InlineData("<Host>$if (true) { $i| }</Host>", "$i")]
    public void DirectiveCompletion_UsesWholeDollarPrefixInContent(string source, string prefix)
    {
        var (document, position) = Parse(source);
        using var workspace = new AkburaWorkspace();
        var context = document.GetCompletionContext(position);
        var result = workspace.LanguageServices.Completion.GetCompletions(document, null, position);

        Assert.Equal(AkburaCompletionContextKind.MarkupStatement, context.Kind);
        Assert.Equal(prefix, document.Text.ToString(result.ApplicableSpan));
        Assert.Equal(prefix is "" or "$" ? 2 : 1, result.Items.Length);
        var item = Assert.Single(result.Items, static item => item.DisplayText == "$if");
        Assert.Equal("$if", item.DisplayText);
        Assert.Equal("$if ()", item.InsertText);
        Assert.Equal(1, item.CaretOffsetFromEnd);
        Assert.False(result.IsIncomplete);
    }

    [Theory]
    [InlineData("<Host>$if (true) {} $|</Host>")]
    [InlineData("<Host>$if (true) {}\r\n $e|</Host>")]
    [InlineData("<Host>$if (true) {} $else i|</Host>")]
    [InlineData("<Host>$if (true) {} $else if (false) {} $|</Host>")]
    [InlineData("<Host>$if (true) {} /* explanation */ $e|</Host>")]
    [InlineData("<Host>$if (true) {} // explanation\r\n $e|</Host>")]
    public void ContinuationCompletion_IsAvailableImmediatelyAfterEligibleChain(string source)
    {
        var (document, position) = Parse(source);
        using var workspace = new AkburaWorkspace();
        var context = document.GetCompletionContext(position);
        var result = workspace.LanguageServices.Completion.GetCompletions(document, null, position);

        Assert.Equal(AkburaCompletionContextKind.MarkupConditionalContinuation, context.Kind);
        Assert.Contains(result.Items, static item => item.DisplayText == "$else if");
        if (context.Prefix is "$" or "$e")
        {
            Assert.Contains(result.Items, static item => item.DisplayText == "$else");
        }
        Assert.All(result.Items, item => Assert.StartsWith(context.Prefix, item.DisplayText));
    }

    [Theory]
    [InlineData("<Host>$e|</Host>")]
    [InlineData("<Host>$if (true) {} <Leaf/> $e|</Host>")]
    [InlineData("<Host>$if (true) {} intervening text $e|</Host>")]
    [InlineData("<Host>$if (true) {} $else {} $e|</Host>")]
    [InlineData("<Host>$if (true) {<Host>$e|</Host>}</Host>")]
    public void ContinuationCompletion_DoesNotCrossContentOrFinalElse(string source)
    {
        var (document, position) = Parse(source);
        using var workspace = new AkburaWorkspace();
        var result = workspace.LanguageServices.Completion.GetCompletions(document, null, position);

        Assert.DoesNotContain(result.Items, static item => item.DisplayText is "$else" or "$else if");
    }

    [Theory]
    [InlineData("$i|")]
    [InlineData("<Host Title=\"$if|\"/>")]
    [InlineData("<Host Title={'$' + \"if|\"}/>")]
    [InlineData("<Host>{\"$if|\"}</Host>")]
    [InlineData("<Host>$ifdef|</Host>")]
    [InlineData("<Host Title=${Bind|}/>")]
    public void DirectiveCompletion_DoesNotStealOtherContexts(string source)
    {
        var (document, position) = Parse(source);
        using var workspace = new AkburaWorkspace();
        var result = workspace.LanguageServices.Completion.GetCompletions(document, null, position);

        Assert.DoesNotContain(result.Items, static item => item.DisplayText is "$if" or "$else" or "$else if");
    }

    [Fact]
    public void DirectiveCommit_KeepsNeighborContentAndPlacesCaretInsideSinglePair()
    {
        var (document, position) = Parse("<Host>$i| <Leaf/></Host>");
        using var workspace = new AkburaWorkspace();
        var result = workspace.LanguageServices.Completion.GetCompletions(document, null, position);
        var item = Assert.Single(result.Items);
        var change = workspace.LanguageServices.Completion.GetCompletionChange(document, null, position, item);
        var changed = document.Text.WithChanges(change.Changes).ToString();

        Assert.Equal("<Host>$if () <Leaf/></Host>", changed);
        Assert.Equal(')', changed[change.NewPosition]);
        Assert.True(AkburaMarkupStatementCompletionFacts.IncludesCommitCharacter(item, '('));
        Assert.True(AkburaMarkupStatementCompletionFacts.IncludesCommitCharacter(item, ' '));
        Assert.False(AkburaMarkupStatementCompletionFacts.IncludesCommitCharacter(item, '{'));
    }

    [Fact]
    public void DirectiveCommit_DoesNotInsertSecondExistingParenthesisPair()
    {
        var (document, position) = Parse("<Host>$if| (true) {<Leaf/>}</Host>");
        using var workspace = new AkburaWorkspace();
        var item = Assert.Single(workspace.LanguageServices.Completion.GetCompletions(document, null, position).Items);
        var change = workspace.LanguageServices.Completion.GetCompletionChange(document, null, position, item);

        Assert.Equal("<Host>$if (true) {<Leaf/>}</Host>", document.Text.WithChanges(change.Changes).ToString());
        Assert.False(AkburaMarkupStatementCompletionFacts.IncludesOpeningParenthesis(item));
    }

    [Theory]
    [InlineData(false, "$if ")]
    [InlineData(true, "$if")]
    public void AdapterCommit_NormalizesOldItemWhenParenthesisWasInsertedDuringSession(bool whitespace, string expected)
    {
        var item = new AkburaCompletionItem("$if", "$if ()", AkburaCompletionKind.Keyword, "condition");

        Assert.Equal(expected, AkburaMarkupStatementCompletionFacts.GetInsertTextBeforeExistingParenthesis(item, whitespace));
    }

    [Fact]
    public void ElseCommit_IncludesItsSpaceButDoesNotCommitOpeningParenthesis()
    {
        var item = new AkburaCompletionItem("$else", "$else ", AkburaCompletionKind.Keyword, "final branch");

        Assert.True(AkburaMarkupStatementCompletionFacts.IncludesCommitCharacter(item, ' '));
        Assert.False(AkburaMarkupStatementCompletionFacts.IncludesCommitCharacter(item, '('));
    }

    [Theory]
    [InlineData("<Host>$if |</Host>", '(', ")")]
    [InlineData("<Host>$if (true) |</Host>", '{', "}")]
    [InlineData("<Host>$if (true) {} $else if |</Host>", '(', ")")]
    [InlineData("<Host>$if (true) {} $else |</Host>", '{', "}")]
    [InlineData("<Host>$if (Call|) {}</Host>", '(', ")")]
    public void ConditionalDelimiterPairing_UsesMarkupAndCSharpContexts(string source, char opening, string closing)
    {
        var (document, position) = Parse(source);
        var decision = document.GetAutomaticPairDecision(position, opening);

        Assert.True(decision.IsValid);
        Assert.Equal(closing, decision.ClosingText);
    }

    [Fact]
    public void ConditionalBlocks_IndentAndFoldIndependentlyOfTheirMarkupChildren()
    {
        var (document, _) = Parse("<Host>\r\n$if (true)\r\n{\r\n<Leaf/>\r\n}\r\n$else\r\n{\r\n<Host>\r\n$if (false)\r\n{\r\n<Leaf/>\r\n}\r\n</Host>\r\n}\r\n</Host>|");

        Assert.Equal(new[] { 0, 1, 1, 2, 1, 1, 1, 2, 3, 3, 4, 3, 2, 1, 0 },
            Enumerable.Range(0, document.Text.Lines.Count).Select(line => document.GetDesiredIndentationLevel(line)));
        var blocks = document.SyntaxTree.GetRootSyntax().DescendantNodes().OfType<MarkupBlockSyntax>().ToArray();
        Assert.Equal(3, blocks.Length);
        Assert.All(blocks, block => Assert.Contains(document.OutliningRegions,
            region => region.Span.Start == block.OpenBraceToken.Span.Start &&
                region.Span.End == block.CloseBraceToken.Span.End));
    }

    [Fact]
    public void ConditionalClassification_SeparatesDirectiveKeywordDelimitersAndExpression()
    {
        var (document, _) = Parse("<Host>$if (ready) {<Leaf/>} $else if (other) {} $else {}</Host>|");
        using var workspace = new AkburaWorkspace();
        var result = workspace.LanguageServices.Classification.GetSyntacticClassifications(document,
            new TextSpan(0, document.Text.Length));
        foreach (var token in document.SyntaxTree.GetRootSyntax().DescendantTokens())
        {
            var expected = token.Kind switch
            {
                SyntaxKind.DollarToken => AkburaClassificationKind.Directive,
                SyntaxKind.IfKeyword or SyntaxKind.ElseKeyword => AkburaClassificationKind.Keyword,
                SyntaxKind.OpenParenToken or SyntaxKind.CloseParenToken or SyntaxKind.OpenBraceToken or
                    SyntaxKind.CloseBraceToken => AkburaClassificationKind.Punctuation,
                _ => (AkburaClassificationKind?)null,
            };
            if (expected != null)
            {
                Assert.Contains(result, span => span.Span == token.Span && span.Kind == expected);
            }
        }
        var ready = document.Text.ToString().IndexOf("ready", StringComparison.Ordinal);
        Assert.Contains(result, span => span.Span == new TextSpan(ready, 5) &&
            span.Kind != AkburaClassificationKind.MarkupText);
    }

    [Theory]
    [InlineData("state bool ready = true; <Host>$if (rea|dy) {}</Host>", "ready")]
    [InlineData("state object model = new Person(); <Host>$if (model is Person person) {<Leaf Text={per|son.Name}/>}</Host>", "person")]
    [InlineData("state object model = new Person(); <Host>$if (model is Person person) {$if (true) {<Leaf Text={per|son.Name}/>}}</Host>", "person")]
    [InlineData("state string input = \"7\"; <Host>$if (int.TryParse(input, out var amount)) {<Leaf Text={amo|unt.ToString()}/>}</Host>", "amount")]
    [InlineData("state object model = new Person(); <Host>$if (model is not Person person) {} $else {<Leaf Text={per|son.Name}/>}</Host>", "person")]
    [InlineData("state object model = new Person(); <Host>$if (model is not Person person) {} $else if (person.Name != null) {<Leaf Text={per|son.Name}/>}</Host>", "person")]
    public async Task ConnectedCSharpCompletion_PreservesConditionAndNestedBranchScope(string source, string expected)
    {
        var fixture = Create(source);
        using var workspace = fixture.Workspace;
        Assert.True(fixture.Document.TryGetCSharpCompletionContext(fixture.Position, out var context));
        Assert.True(AkburaCSharpProjectionFactory.TryCreate(fixture.Document, fixture.Context, context, out var projection));
        Assert.Contains(projection.Root.DescendantNodes().OfType<CSharp.IfStatementSyntax>(),
            statement => statement.Condition != null);
        Assert.True(projection.TryMapPositionToHost(projection.ProjectedPosition, out var mapped));
        Assert.Equal(fixture.Position, mapped);

        var completion = await workspace.LanguageServices.ProjectedCSharp.GetCompletionsAsync(
            fixture.Document, fixture.Context, fixture.Position, new(true, false, '\0'));
        Assert.NotNull(completion);
        Assert.Contains(completion.Value.Items, item => item.DisplayText == expected);
    }

    [Fact]
    public async Task AncestorConditionEdit_RefreshesBranchLocalsWhileOldSnapshotRemainsValid()
    {
        var fixture = Create("state object model = new Person(); <Host>$if (model is Person person) {<Leaf Text={per|}/>}</Host>");
        using var workspace = fixture.Workspace;
        var oldCompletion = await workspace.LanguageServices.ProjectedCSharp.GetCompletionsAsync(
            fixture.Document, fixture.Context, fixture.Position, new(true, false, '\0'));
        Assert.Contains(oldCompletion!.Value.Items, static item => item.DisplayText == "person");
        var changedSource = fixture.Source.Replace("Person person", "Person other", StringComparison.Ordinal);
        var changedText = SourceText.From(changedSource);
        var changedContext = workspace.OpenOrChangeDocumentContext(fixture.Context.Document.Uri, changedText);
        var changedDocument = AkburaSyntacticDocument.Parse(changedText, fixture.Context.Document.FilePath);
        var changedPosition = fixture.Position - 1;
        var newCompletion = await workspace.LanguageServices.ProjectedCSharp.GetCompletionsAsync(
            changedDocument, changedContext, changedPosition, new(true, false, '\0'));
        Assert.DoesNotContain(newCompletion!.Value.Items, static item => item.DisplayText == "person");
        var retained = await workspace.LanguageServices.ProjectedCSharp.GetCompletionsAsync(
            fixture.Document, fixture.Context, fixture.Position, new(true, false, '\0'));
        Assert.Contains(retained!.Value.Items, static item => item.DisplayText == "person");
    }

    [Fact]
    public void ConditionHoverAndNavigation_MapStateReferenceToHostDeclaration()
    {
        var fixture = Create("state bool ready = true; <Host>$if (rea|dy) {<Leaf/>}</Host>");
        using var workspace = fixture.Workspace;
        var referenceStart = fixture.Source.LastIndexOf("ready", StringComparison.Ordinal);
        var info = workspace.LanguageServices.QuickInfo.GetQuickInfo(fixture.Context, fixture.Position);
        Assert.NotNull(info);
        Assert.Equal(new TextSpan(referenceStart, 5), info.SourceSpan);
        Assert.Contains("ready", info.Signature, StringComparison.Ordinal);
        var navigation = workspace.LanguageServices.Definition.GetDefinition(fixture.Context, fixture.Position);
        Assert.NotNull(navigation);
        Assert.Equal(new TextSpan(referenceStart, 5), navigation.SourceSpan);
        Assert.Equal(fixture.Context.Document.FilePath, navigation.TargetFilePath);
        Assert.Equal(fixture.Document.Text.Lines.GetLinePosition(fixture.Source.IndexOf("ready", StringComparison.Ordinal)),
            navigation.TargetLineSpan.Start);
    }

    [Theory]
    [InlineData("state object model = new Person(); <Host>$if (model is Person person) {<Leaf Text={per|son.Name}/>}</Host>", "person", "Person")]
    [InlineData("state string input = \"7\"; <Host>$if (int.TryParse(input, out var amount)) {<Leaf Text={amo|unt.ToString()}/>}</Host>", "amount", "int")]
    public void BranchConditionLocalHoverAndNavigation_MapToConditionDeclaration(string source, string name, string type)
    {
        var fixture = Create(source);
        using var workspace = fixture.Workspace;
        var referenceStart = fixture.Source.LastIndexOf(name, StringComparison.Ordinal);
        var info = workspace.LanguageServices.QuickInfo.GetQuickInfo(fixture.Context, fixture.Position);
        Assert.NotNull(info);
        Assert.Equal(new TextSpan(referenceStart, name.Length), info.SourceSpan);
        Assert.Contains(type, info.Signature, StringComparison.Ordinal);
        var navigation = workspace.LanguageServices.Definition.GetDefinition(fixture.Context, fixture.Position);
        Assert.NotNull(navigation);
        Assert.Equal(fixture.Context.Document.FilePath, navigation.TargetFilePath);
        Assert.Equal(fixture.Document.Text.Lines.GetLinePosition(fixture.Source.IndexOf(name, StringComparison.Ordinal)),
            navigation.TargetLineSpan.Start);
    }

    private static (AkburaSyntacticDocument Document, int Position) Parse(string source)
    {
        var position = source.IndexOf('|');
        Assert.True(position >= 0);
        return (AkburaSyntacticDocument.Parse(SourceText.From(source.Remove(position, 1)), "Conditional.akbura"), position);
    }

    private static Fixture Create(string sourceWithCaret)
    {
        const string imports = "using Example; ";
        var position = imports.Length + sourceWithCaret.IndexOf('|');
        var source = imports + sourceWithCaret.Remove(sourceWithCaret.IndexOf('|'), 1);
        var platform = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?.Split(Path.PathSeparator) ?? [];
        var compilation = CSharpCompilation.Create("WorkspaceMarkupConditionalTests",
            [CSharpSyntaxTree.ParseText(Definitions, path: Path.GetFullPath("ConditionalDefinitions.cs"))],
            platform.Select(static path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.DoesNotContain(compilation.GetDiagnostics(), static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var workspace = new AkburaWorkspace(new ProjectContext(ProjectId.CreateNewId(), string.Empty,
            Environment.CurrentDirectory, string.Empty, compilation, ImmutableArray<ProjectReference>.Empty));
        var path = Path.GetFullPath("Conditional.akbura");
        var text = SourceText.From(source);
        return new(workspace, workspace.OpenOrChangeDocumentContext(new Uri(path), text),
            AkburaSyntacticDocument.Parse(text, path), source, position);
    }

    private sealed record Fixture(AkburaWorkspace Workspace, AkburaDocumentContext Context,
        AkburaSyntacticDocument Document, string Source, int Position);

    private const string Definitions = """
        using System;
        using System.Collections.Generic;
        namespace Avalonia.Controls { public class Control { } }
        namespace Avalonia.Metadata
        {
            [AttributeUsage(AttributeTargets.Property)]
            public sealed class ContentAttribute : Attribute { }
        }
        namespace Example
        {
            public sealed class Person { public string Name { get; set; } = "Ada"; }
            public sealed class Host : Avalonia.Controls.Control
            {
                [Avalonia.Metadata.Content]
                public List<Avalonia.Controls.Control> Children { get; } = new();
                public string Title { get; set; }
            }
            public sealed class Leaf : Avalonia.Controls.Control { public string Text { get; set; } }
        }
        """;
}
