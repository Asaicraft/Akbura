using Akbura.LanguageServer.Protocol;
using Microsoft.CodeAnalysis.Text;
using System.Text.Json;

namespace Akbura.LanguageServer.IntegrationTests;

public sealed partial class SemanticProjectLifecycleTests
{
    [Fact]
    public async Task ExpressionBinding_RealProjectLifecycleExposesTheSharedLanguageServices()
    {
        await using var fixture = await LanguageServerFixture.CreateAsync(false, true);
        const string prefix = "using Avalonia.Controls;\r\n// 😀 UTF-16\r\nstate int Count = 0;\r\n";
        const string incomplete = prefix + "<TextBlock Text=${x.bi} />";
        await fixture.OpenAsync(fixture.ComponentUri, "akbura", incomplete, 1);
        var completion = await fixture.WaitForCompletionAsync(fixture.ComponentUri,
            PositionAfter(incomplete, "x.bi"), "x.bind");
        Assert.Single(completion.Items, item => item.Label == "x.bind");

        const string memberSource = prefix + "<TextBlock Text=${x.bind Count.ToStr} />";
        await fixture.ChangeAsync(fixture.ComponentUri, memberSource, 2);
        completion = await fixture.WaitForCompletionAsync(fixture.ComponentUri,
            PositionAfter(memberSource, "Count.ToStr"), "ToString");
        var member = Assert.Single(completion.Items, item => item.Label == "ToString");
        var resolved = await fixture.RequestAsync<CompletionItem>(LspMethods.CompletionResolve, member);
        Assert.NotNull(resolved.TextEdit);

        const string source = prefix + "<StackPanel><TextBlock Text=${x.bind Count.ToString()} />" +
            "<TextBlock Text=${x.bind $\"Value: {Count}\"} /></StackPanel>";
        await fixture.ChangeAsync(fixture.ComponentUri, source, 3);
        var document = new TextDocumentIdentifier { Uri = fixture.ComponentUri.AbsoluteUri };
        var position = PositionInsideLast(source, "Count");
        var definitions = await fixture.WaitForDefinitionAsync(fixture.ComponentUri, position,
            fixture.ComponentUri.AbsoluteUri);
        Assert.Equal(2, Assert.Single(definitions).TargetSelectionRange.Start.Line);
        var hover = await fixture.RequestAsync<Hover>(LspMethods.Hover,
            new TextDocumentPositionParams { TextDocument = document, Position = position });
        Assert.NotNull(hover);
        var references = await fixture.RequestAsync<Location[]>(LspMethods.References,
            new ReferenceParams { TextDocument = document, Position = position,
                Context = new ReferenceContext { IncludeDeclaration = true } });
        Assert.Equal(3, references.Length);
        var prepare = await fixture.RequestAsync<PrepareRenameResult>(LspMethods.PrepareRename,
            new PrepareRenameParams { TextDocument = document, Position = position });
        Assert.Equal("Count", prepare.Placeholder);
        var rename = await fixture.RequestAsync<JsonElement>(LspMethods.Rename,
            new RenameParams { TextDocument = document, Position = position, NewName = "Total" });
        Assert.Contains("Total", rename.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain(".projection.cs", rename.GetRawText(), StringComparison.Ordinal);

        var tokens = await fixture.RequestAsync<SemanticTokens>(LspMethods.SemanticTokensFull,
            new SemanticTokensParams { TextDocument = document });
        Assert.NotEmpty(tokens.Data);
        Assert.Equal(0, tokens.Data.Length % 5);
        var ranged = await fixture.RequestAsync<SemanticTokens>(LspMethods.SemanticTokensRange,
            new SemanticTokensRangeParams { TextDocument = document, Range = new Protocol.Range
            {
                Start = new Position(), End = PositionAfter(source, "</StackPanel>"),
            } });
        Assert.NotEmpty(ranged.Data);
        var delta = await fixture.RequestAsync<JsonElement>(LspMethods.SemanticTokensFullDelta,
            new SemanticTokensDeltaParams { TextDocument = document, PreviousResultId = tokens.ResultId! });
        Assert.True(delta.TryGetProperty("edits", out _) || delta.TryGetProperty("data", out _));

        const string invocation = prefix + "<TextBlock Text=${x.bind Count.ToString(\"D\")} />";
        await fixture.ChangeAsync(fixture.ComponentUri, invocation, 4);
        var signatures = await fixture.RequestAsync<SignatureHelp>(LspMethods.SignatureHelp,
            new SignatureHelpParams { TextDocument = document, Position = PositionAfter(invocation, "ToString(") });
        Assert.NotEmpty(signatures.Signatures);
        const string invalid = prefix + "<TextBlock Text=${x.bind Count.UnknownMember} />";
        await fixture.ChangeAsync(fixture.ComponentUri, invalid, 5);
        var diagnostics = await fixture.RequestAsync<JsonElement>(LspMethods.DocumentDiagnostic,
            new DocumentDiagnosticParams { TextDocument = document });
        Assert.Contains("UnknownMember", diagnostics.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain(".projection.cs", diagnostics.GetRawText(), StringComparison.Ordinal);
        await fixture.RequestAsync<JsonElement>(LspMethods.CodeAction, new CodeActionParams
        {
            TextDocument = document,
            Range = new Protocol.Range { Start = new Position(), End = PositionAfter(invalid, " />") },
        });
        await fixture.CloseAsync(fixture.ComponentUri);
        await fixture.ShutdownAsync();
    }
}
