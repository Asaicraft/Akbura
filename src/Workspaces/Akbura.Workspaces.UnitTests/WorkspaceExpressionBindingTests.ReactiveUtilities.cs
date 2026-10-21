using Akbura.Workspaces.Classification;
using Akbura.Workspaces.Documents;
using Microsoft.CodeAnalysis.Text;

namespace Akbura.Workspaces.UnitTests;

public sealed partial class WorkspaceExpressionBindingTests
{
    [Theory]
    [InlineData("${|}:hidden")]
    [InlineData("${x.|}:hidden")]
    [InlineData("${x.bi|}:hidden")]
    public void UtilityConditionPrefixCompletion_PreservesItsColonAndUtility(string prefix)
    {
        using var workspace = new AkburaWorkspace();
        var (document, position) = Parse("<TextBlock " + prefix + "/>");
        var result = workspace.LanguageServices.Completion.GetCompletions(document, null, position);
        var item = Assert.Single(result.Items, item => item.DisplayText == "x.bind");
        var change = workspace.LanguageServices.Completion.GetCompletionChange(document, null, position, item);
        Assert.Equal("<TextBlock ${x.bind }:hidden/>", document.Text.WithChanges(change.Changes).ToString());
        Assert.True(change.TriggerNextCompletion);
    }

    [Theory]
    [InlineData("var current = Vm.Profile; <TextBlock ${x.bind current.|}:hidden/>", "Name")]
    [InlineData("<StackPanel>$foreach (var item in Vm.Items) { <TextBlock ${x.bind item.|}:hidden/> }</StackPanel>", "Name")]
    [InlineData("<TextBlock x.Name=\"text\" ${x.bind text.|}:hidden/>", "Width")]
    [InlineData("<ItemsControl><ItemsControl.ItemTemplate x.DataType=\"Person\" x.ItemName=\"person\"><TextBlock ${x.bind person.|}:hidden/></ItemsControl.ItemTemplate></ItemsControl>", "Name")]
    public async Task UtilityConditionCompletion_PreservesEachReceiverScope(string body, string member) =>
        await AssertCompletion(Prelude + body, member);

    [Fact]
    public void UtilityConditionReferencesAndRename_IncludeItsRealStateOccurrence()
    {
        using var workspace = CreateWorkspace();
        var source = Prelude + "<StackPanel><TextBlock Width={Count}/>" +
            "<TextBlock Text=${x.bind Count.ToString()}/><TextBlock ${x.bind Count > 0}:hidden/></StackPanel>";
        var context = Open(workspace, SourceText.From(source));
        foreach (var position in new[] { source.IndexOf("Count =", StringComparison.Ordinal), source.LastIndexOf("Count", StringComparison.Ordinal) })
        {
            var references = workspace.LanguageServices.References.FindReferences(context, position, includeDeclaration: true);
            Assert.Equal(4, references.Locations.Length);
            var changes = Assert.Single(workspace.LanguageServices.Rename.GetRenameChanges(context, position, "Total").Changes).Value;
            Assert.Equal(4, changes.Length);
            Assert.Contains("${x.bind Total > 0}:hidden", context.Document.Text.WithChanges(changes).ToString());
        }
    }

    [Theory]
    [InlineData("<StackPanel>$foreach (var item in Vm.Items) { <TextBlock ${x.bind ite|m.Name == \"Ada\"}:hidden/> }</StackPanel>", 2)]
    [InlineData("<TextBlock x.Name=\"text\" ${x.bind tex|t.Width > 100}:hidden/>", 2)]
    [InlineData("var Vm = Person; <TextBlock ${x.bind V|m.Name == \"Ada\"}:hidden/>", 2)]
    public void UtilityConditionRename_PreservesProjectedOrigins(string body, int occurrences)
    {
        using var workspace = CreateWorkspace();
        var (document, position) = Parse(Prelude + body);
        var context = Open(workspace, document.Text);
        var references = workspace.LanguageServices.References.FindReferences(context, position, includeDeclaration: true);
        Assert.Equal(occurrences, references.Locations.Length);
        var changes = Assert.Single(workspace.LanguageServices.Rename.GetRenameChanges(context, position, "renamed").Changes).Value;
        Assert.Equal(occurrences, changes.Length);
        Assert.Contains("${x.bind renamed.", document.Text.WithChanges(changes).ToString());
    }

    [Theory]
    [InlineData("Vm.Ena|bled", "Enabled", "bool")]
    [InlineData("Cou|nt > 0", "Count", "Int32")]
    public async Task UtilityConditionHoverAndDefinition_MapToTheActualDeclaration(string expression, string name, string type)
    {
        using var fixture = new CSharpSourceFixture();
        using var workspace = CreateWorkspace(fixture.FilePath);
        var (document, position) = Parse(Prelude + "<TextBlock ${x.bind " + expression + "}:hidden/>");
        var context = Open(workspace, document.Text);
        var info = workspace.LanguageServices.QuickInfo.GetQuickInfo(context, position);
        Assert.NotNull(info);
        Assert.Contains(type, info.Signature, StringComparison.OrdinalIgnoreCase);
        var projected = await workspace.LanguageServices.ProjectedCSharp.GetQuickInfoAsync(document, context, position);
        Assert.NotNull(projected);
        Assert.Equal(name, document.Text.ToString(projected.SourceSpan));
        var definition = workspace.LanguageServices.Definition.GetDefinition(context, position);
        Assert.NotNull(definition);
        Assert.EndsWith(name == "Count" ? ".akbura" : "ViewModel.cs", definition.TargetFilePath);
    }

    [Fact]
    public void UtilityConditionClassification_KeepsDirectiveCSharpAndUtilityDistinct()
    {
        using var workspace = CreateWorkspace();
        var source = Prelude + "<TextBlock ${x.bind Vm.Enabled}:hidden/>";
        var context = Open(workspace, SourceText.From(source));
        var document = AkburaSyntacticDocument.Create(context.Document);
        var span = new TextSpan(0, source.Length);
        foreach (var classifications in new[] {
            workspace.LanguageServices.Classification.GetSyntacticClassifications(document, span),
            workspace.LanguageServices.Classification.GetClassifications(context, span) })
        {
            Assert.Single(classifications, item => item.Span == new TextSpan(source.IndexOf("bind Vm", StringComparison.Ordinal), 4) &&
                item.Kind == AkburaClassificationKind.Directive);
            Assert.Single(classifications, item => context.Document.Text.ToString(item.Span) == "hidden" &&
                item.Kind == AkburaClassificationKind.Utility);
        }
        var semantic = workspace.LanguageServices.Classification.GetClassifications(context, span);
        Assert.Single(semantic, item => context.Document.Text.ToString(item.Span) == "Enabled" &&
            item.Kind == AkburaClassificationKind.PropertyName);
        Assert.Equal(semantic.Length, semantic.Distinct().Count());
    }

    [Theory]
    [InlineData("Vm.Name")]
    [InlineData("Vm.UnknownMember")]
    public void UtilityConditionDiagnostics_KeepTheOriginalExpressionSpan(string expression)
    {
        using var workspace = CreateWorkspace();
        var source = Prelude + "// 😀\r\n<TextBlock ${x.bind " + expression + "}:hidden/>";
        var context = Open(workspace, SourceText.From(source));
        var diagnostics = workspace.LanguageServices.Diagnostics.GetDiagnostics(context, new TextSpan(0, source.Length));
        Assert.Contains(diagnostics, item => source.Substring(item.Span.Start, item.Span.Length)
            .Contains(expression == "Vm.Name" ? expression : "UnknownMember", StringComparison.Ordinal));
        Assert.All(diagnostics, item => Assert.InRange(item.Span.End, 0, source.Length));
    }
}
