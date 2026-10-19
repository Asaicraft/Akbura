namespace Akbura.LanguageServer.UnitTests;

public sealed partial class BindingCompletionProtocolTests
{
    [Theory]
    [InlineData("${|}")]
    [InlineData("${x.|}")]
    [InlineData("${x.bi|}")]
    public async Task ExpressionBindingPrefix_UsesExistingProtocolWithExactUtf16Edit(string value)
    {
        var source = "namespace Gallery;\r\nusing Avalonia.Controls;\r\n// 😀\r\n<Border Tag=" + value + "/>";
        await using var fixture = new CompletionFixture(source.Replace("|", "", StringComparison.Ordinal), source);
        var completion = await fixture.CompleteAsync();
        var item = Assert.Single(completion.Items, item => item.Label == "x.bind");
        Assert.Equal(14, item.Kind); // LSP Keyword, not a CLR extension class.
        Assert.Equal("x.bind ", fixture.Apply(item));
        Assert.Equal("editor.action.triggerSuggest", item.Command?.Command);
        var json = System.Text.Json.JsonSerializer.Serialize(item, Protocol.Serialization.AkburaProtocolJson.CreateOptions());
        using var serialized = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal("editor.action.triggerSuggest", serialized.RootElement.GetProperty("command").GetProperty("command").GetString());
        Assert.Contains("Tag=${x.bind }/>", fixture.ApplyToDocument(item), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Vm.Customer.Na|me", "Name")]
    [InlineData("$\"😀 {Vm.Customer.Na|me}\"", "Name")]
    [InlineData("Vm.Cus|tomer.Name", "Customer")]
    public async Task ExpressionBindingMember_CompletionAndResolveKeepHostSpans(string expression, string member)
    {
        var marked = "namespace Gallery;\r\nusing Avalonia.Controls;\r\ninject MainViewModel Vm;\r\n" +
            "<Border Tag=${x.bind " + expression + "}/>";
        var semantic = marked.Replace("|", "", StringComparison.Ordinal);
        await using var fixture = new CompletionFixture(semantic, marked);
        var result = await fixture.CompleteAsync();
        var item = Assert.Single(result.Items, item => item.Label == member);
        // The initial projected edit and lazily resolved canonical edit must both
        // replace the whole existing identifier, never the introducer or brace.
        Assert.Equal(member, fixture.GetEditedText(item));
        Assert.Equal(semantic, fixture.ApplyToDocument(item));
        Assert.NotNull(item.Data);
        var resolved = await fixture.ResolveAsync(item);
        Assert.Equal(1, resolved.InsertTextFormat);
        Assert.Equal(member, fixture.Apply(resolved));
        Assert.Equal(member, fixture.GetEditedText(resolved));
        Assert.Equal(semantic, fixture.ApplyToDocument(resolved));
    }

    [Fact]
    public async Task ExpressionBinding_StaleSemanticSnapshotUsesCurrentExpressionScope()
    {
        const string prefix = "namespace Gallery;\r\nusing Avalonia.Controls;\r\ninject MainViewModel Vm;\r\n";
        const string oldBody = "<Border Tag=${x.bind Vm.Customer.N}/>";
        const string newBody = "<Border Tag=${x.bind Vm.Customer.Na|me}/>";
        await using var fixture = new CompletionFixture(prefix + oldBody, prefix + newBody);
        var result = await fixture.CompleteAsync();
        var item = Assert.Single(result.Items, item => item.Label == "Name");
        Assert.Equal("Name", fixture.GetEditedText(item));
        Assert.DoesNotContain("NameName", fixture.ApplyToDocument(item), StringComparison.Ordinal);
        var resolved = await fixture.ResolveAsync(item);
        Assert.Equal(1, resolved.InsertTextFormat);
        Assert.Equal("Name", fixture.Apply(resolved));
        Assert.Contains("Vm.Customer.Name", fixture.ApplyToDocument(resolved), StringComparison.Ordinal);
    }
}
