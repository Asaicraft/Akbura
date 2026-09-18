using System;
using System.Collections.Immutable;
using System.Linq;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace Akbura.Workspaces.UnitTests;

public sealed class WorkspaceMarkupForeachClassificationTests
{
    [Theory]
    [InlineData("<StackPanel>$foreach (var item in values) {}</StackPanel>", 1)]
    [InlineData("<StackPanel><StackPanel.Children>$foreach (var item in values) {<TextBlock/>}</StackPanel.Children></StackPanel>", 1)]
    [InlineData("<StackPanel>$if (true) {$foreach (var item in values) {}}</StackPanel>", 1)]
    [InlineData("<StackPanel>$foreach (var outer in values) {$foreach (var inner in values) {}}</StackPanel>", 2)]
    [InlineData("<StackPanel>$foreach (var item in values; key: item.Id) {<TextBlock/>}</StackPanel>", 1)]
    public void ForeachSyntax_SeparatesDirectiveKeywordAndDelimiters(string source, int expectedLoops)
    {
        var document = Parse(source);
        using var workspace = new AkburaWorkspace();
        var result = Classify(workspace, document);
        var loops = document.SyntaxTree.GetRootSyntax().DescendantNodes()
            .OfType<MarkupForeachStatementSyntax>().ToArray();

        Assert.Equal(expectedLoops, loops.Length);

        foreach (var loop in loops)
        {
            AssertKind(result, loop.DollarToken.Span, AkburaClassificationKind.Directive);
            AssertKind(result, loop.ForeachKeyword.Span, AkburaClassificationKind.Keyword);
            AssertKind(result, loop.OpenParenToken.Span, AkburaClassificationKind.Punctuation);
            AssertKind(result, loop.CloseParenToken.Span, AkburaClassificationKind.Punctuation);
            AssertKind(result, loop.Body.OpenBraceToken.Span, AkburaClassificationKind.Punctuation);
            AssertKind(result, loop.Body.CloseBraceToken.Span, AkburaClassificationKind.Punctuation);
        }
    }

    [Fact]
    public void MixedBody_ClassifiesHostForeachAndEmbeddedCSharpIndependently()
    {
        const string source = """
            using Avalonia.Controls;
            using System.Collections.ObjectModel;

            state ObservableCollection<int> array = [1, 2, 3, 4, 5];

            <StackPanel>
                <TextBlock Text="Starting loop" />
                $if (true) {}
                $foreach (var item in array)
                {
                    var doubled = item * 2;
                    if (item % 2 == 0)
                    {
                        continue;
                    }
                    if (item % 3 == 0)
                    {
                        break;
                    }
                    <TextBlock Text={$"Item {item}, doubled {doubled}, index {@index}"} />
                }
                <TextBlock Text="Ending loop" />
            </StackPanel>
            """;
        var document = Parse(source);
        using var workspace = new AkburaWorkspace();
        var result = Classify(workspace, document);

        AssertKind(result, FindSpan(source, "$if", 1, 2), AkburaClassificationKind.Keyword);
        AssertKind(result, FindSpan(source, "$foreach", 0, 1), AkburaClassificationKind.Directive);
        AssertKind(result, FindSpan(source, "$foreach", 1, 7), AkburaClassificationKind.Keyword);
        AssertKind(result, FindSpan(source, "var item", 0, 3), AkburaClassificationKind.Keyword);
        AssertKind(result, FindSpan(source, "in array", 0, 2), AkburaClassificationKind.Keyword);
        AssertKind(result, FindSpan(source, "var doubled", 0, 3), AkburaClassificationKind.Keyword);
        AssertKind(result, FindSpan(source, "if (item % 2", 0, 2), AkburaClassificationKind.Keyword);
        AssertKind(result, FindSpan(source, "if (item % 3", 0, 2), AkburaClassificationKind.Keyword);
        AssertKind(result, FindSpan(source, "continue;", 0, 8), AkburaClassificationKind.Keyword);
        AssertKind(result, FindSpan(source, "break;", 0, 5), AkburaClassificationKind.Keyword);
    }

    [Theory]
    [InlineData("$foreach")]
    [InlineData("$foreach (")]
    [InlineData("$foreach (var item in")]
    public void IncompleteForeach_StillClassifiesItsExistingDirectiveAndKeyword(string content)
    {
        var document = Parse("<StackPanel>" + content);
        using var workspace = new AkburaWorkspace();
        var result = Classify(workspace, document);
        var loop = Assert.Single(document.SyntaxTree.GetRootSyntax().DescendantNodes()
            .OfType<MarkupForeachStatementSyntax>());

        AssertKind(result, loop.DollarToken.Span, AkburaClassificationKind.Directive);
        AssertKind(result, loop.ForeachKeyword.Span, AkburaClassificationKind.Keyword);
    }

    [Theory]
    [InlineData("<TextBlock Text=\"$foreach\" />")]
    [InlineData("<TextBlock Text={\"$foreach\"} />")]
    public void ForeachTextInsideString_IsNotClassifiedAsDirectiveOrKeyword(string source)
    {
        var document = Parse(source);
        using var workspace = new AkburaWorkspace();
        var result = Classify(workspace, document);
        var textSpan = FindSpan(source, "$foreach", 0, 8);

        Assert.Contains(result, span => span.Kind == AkburaClassificationKind.String &&
            span.Span.Contains(textSpan));
        Assert.DoesNotContain(result, span => span.Span.OverlapsWith(textSpan) &&
            span.Kind is AkburaClassificationKind.Keyword or AkburaClassificationKind.Directive);
    }

    [Fact]
    public void MarkupExtensionDollar_KeepsItsOwnClassification()
    {
        const string source = "<TextBlock Text=${Binding Title} />";
        var document = Parse(source);
        using var workspace = new AkburaWorkspace();
        var result = Classify(workspace, document);

        AssertKind(result, FindSpan(source, "${Binding", 0, 1),
            AkburaClassificationKind.MarkupExtensionPunctuation);
    }

    [Theory]
    [InlineData(0, 7)]
    [InlineData(2, 1)]
    public void RequestedSpanWithinForeachKeyword_ReturnsKeywordClassification(int offset, int length)
    {
        var document = Parse("<StackPanel>$foreach (var item in values) {}</StackPanel>");
        var loop = Assert.Single(document.SyntaxTree.GetRootSyntax().DescendantNodes()
            .OfType<MarkupForeachStatementSyntax>());
        var requestedSpan = new TextSpan(loop.ForeachKeyword.Span.Start + offset, length);
        using var workspace = new AkburaWorkspace();
        var result = workspace.LanguageServices.Classification.GetSyntacticClassifications(
            document, requestedSpan);

        AssertKind(result, loop.ForeachKeyword.Span, AkburaClassificationKind.Keyword);
    }

    [Fact]
    public void CharacterwiseForeachTyping_FullAndIncrementalClassificationsAgree()
    {
        const string prefix = "<StackPanel>\r\n    ";
        const string suffix = "\r\n</StackPanel>";
        const string insertion = "$foreach (var item in values) {}";
        var text = SourceText.From(prefix + suffix);
        var document = Parse(text.ToString());
        var position = prefix.Length;
        using var workspace = new AkburaWorkspace();

        foreach (var character in insertion)
        {
            var editPosition = position++;
            text = text.WithChanges(new TextChange(new TextSpan(editPosition, 0), character.ToString()));
            document = document.WithText(text);
            var fresh = Parse(text.ToString());
            var actual = Classify(workspace, document);
            var expected = Classify(workspace, fresh);

            Assert.True(expected.Select(span => (span.Span, span.Kind))
                    .SequenceEqual(actual.Select(span => (span.Span, span.Kind))),
                $"Classifications differ after U+{(int)character:X4} at {editPosition}. Text: {text}");

            foreach (var loop in document.SyntaxTree.GetRootSyntax().DescendantNodes()
                         .OfType<MarkupForeachStatementSyntax>())
            {
                AssertKind(actual, loop.DollarToken.Span, AkburaClassificationKind.Directive);
                AssertKind(actual, loop.ForeachKeyword.Span, AkburaClassificationKind.Keyword);
            }
        }

        // Ensure the comparison did not pass merely because both paths omitted foreach.
        Assert.Single(document.SyntaxTree.GetRootSyntax().DescendantNodes()
            .OfType<MarkupForeachStatementSyntax>());
    }

    private static TextSpan FindSpan(string source, string fragment, int offset, int length)
    {
        var start = source.IndexOf(fragment, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing test fragment: {fragment}");
        return new TextSpan(start + offset, length);
    }

    private static void AssertKind(ImmutableArray<AkburaClassifiedSpan> result,
        TextSpan expectedSpan, AkburaClassificationKind expectedKind)
    {
        var actual = Assert.Single(result, span => span.Span == expectedSpan);
        Assert.Equal(expectedKind, actual.Kind);
        Assert.DoesNotContain(result, span => span.Span.OverlapsWith(expectedSpan) &&
            span.Kind != expectedKind);
    }

    private static ImmutableArray<AkburaClassifiedSpan> Classify(AkburaWorkspace workspace,
        AkburaSyntacticDocument document) =>
        workspace.LanguageServices.Classification.GetSyntacticClassifications(
            document, new TextSpan(0, document.Text.Length));

    private static AkburaSyntacticDocument Parse(string source) =>
        AkburaSyntacticDocument.Parse(SourceText.From(source), "ForeachClassification.akbura");
}
