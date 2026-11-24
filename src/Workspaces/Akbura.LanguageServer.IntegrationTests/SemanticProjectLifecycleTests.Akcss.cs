using Akbura.LanguageServer.Protocol;
using System.Text.Json;

namespace Akbura.LanguageServer.IntegrationTests;

public sealed partial class SemanticProjectLifecycleTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AkcssChangesRefreshUnchangedConsumers(bool pull)
    {
        const string shared = "@using Avalonia.Controls;\r\n" +
            "@mixin Button Configure() { Width: 25d; }\r\n@theme Button MyTheme { }";
        const string view = "using Avalonia.Controls;\r\n" +
            "@akcss { @using Avalonia.Controls; @using Akbura.Previewer.Styles.akcss; " +
            "@theme Button LocalTheme { @include Configure(); } }\r\n" +
            "<StackPanel><TextBlock Text=\"🐣\" /><Button Theme={global::Akbura.Previewer.Styles.MyTheme} /></StackPanel>";
        await using var fixture = await LanguageServerFixture.CreateAsync(
            failWatcherRegistration: false, supportsSemanticTokensRefresh: true, supportsPullDiagnostics: pull);
        await fixture.OpenAsync(fixture.ComponentUri, "akbura", view, version: 1);
        await fixture.Notifications.WaitForProjectLoadEndCountAsync(1, fixture.CancellationToken);
        await fixture.OpenAsync(fixture.StylesUri, "akcss", shared, version: 1);
        await fixture.WaitForDefinitionAsync(fixture.ComponentUri, PositionInsideLast(view, "Configure"), fixture.StylesUri.AbsoluteUri);
        var definition = Assert.Single(await fixture.WaitForDefinitionAsync(
            fixture.ComponentUri, PositionInsideLast(view, "MyTheme"), fixture.StylesUri.AbsoluteUri));
        var expected = PositionInsideLast(shared, "MyTheme");
        var selection = definition.TargetSelectionRange.Start;
        Assert.Equal((expected.Line, expected.Character - 1), (selection.Line, selection.Character));

        await ChangeAndVerifyAsync(shared.Replace("Configure", "Other", StringComparison.Ordinal), 2, "Configure", expectError: true);
        await ChangeAndVerifyAsync(shared, 3, "Configure", expectError: false);
        await ChangeAndVerifyAsync(shared.Replace("MyTheme", "DifferentTheme", StringComparison.Ordinal), 4, "MyTheme", expectError: true);
        await ChangeAndVerifyAsync(shared, 5, "MyTheme", expectError: false);
        await fixture.ShutdownAsync();

        async Task ChangeAndVerifyAsync(string text, int version, string name, bool expectError)
        {
            var consumer = fixture.ComponentUri;
            var diagnosticsRefresh = fixture.Notifications.DiagnosticRefreshCount + 1;
            var tokensRefresh = fixture.Notifications.SemanticTokensRefreshCount + 1;
            var previous = pull ? await GetReportAsync(consumer) : default;
            var pushed = pull ? Task.CompletedTask : fixture.Notifications.ExpectDiagnostics(
                consumer.AbsoluteUri, version: 1,
                diagnostics => diagnostics.Any(diagnostic => diagnostic.Message.Contains(name, StringComparison.Ordinal)) == expectError);

            await fixture.SendChangeAsync(fixture.StylesUri, text, version);
            await pushed.WaitAsync(fixture.CancellationToken);
            await fixture.Notifications.WaitForRefreshAsync(tokensRefresh, pull ? diagnosticsRefresh : 0, fixture.CancellationToken);
            if (pull)
            {
                var report = await GetReportAsync(consumer, previous.GetProperty("resultId").GetString());
                Assert.Equal("full", report.GetProperty("kind").GetString());
                Assert.Equal(expectError, report.GetProperty("items").EnumerateArray()
                    .Any(diagnostic => diagnostic.GetProperty("message").GetString()!.Contains(name, StringComparison.Ordinal)));
            }
        }

        Task<JsonElement> GetReportAsync(Uri uri, string? previous = null) =>
            fixture.RequestAsync<JsonElement>(LspMethods.DocumentDiagnostic, new DocumentDiagnosticParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = uri.AbsoluteUri },
                PreviousResultId = previous,
            });
    }
}
