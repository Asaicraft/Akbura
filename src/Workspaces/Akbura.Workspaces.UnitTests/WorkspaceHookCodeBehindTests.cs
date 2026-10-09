using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Akbura.Workspaces.UnitTests;

public sealed class WorkspaceHookCodeBehindTests
{
    private const string OwnerSource = """
        namespace Gallery;

        public partial class MyComponent
        {
            private int _myField = 200;

            protected override void OnInitialized()
            {
                base.OnInitialized();
                text.Text = "Hello from class";
            }
        }
        """;

    [Theory]
    [InlineData("_")]
    [InlineData("_my")]
    [InlineData("this._my")]
    public async Task Completion_PrivateCodeBehindFieldCanBeSelectedInMarkup(string expression)
    {
        // MyComponent.akbura: <TextBlock x.Name="text" Width={_myField}/>
        var source = "using Avalonia.Controls;\r\n" +
            $"<TextBlock x.Name=\"text\" Width={{{expression}}}/>";
        var position = source.IndexOf(expression, StringComparison.Ordinal) + expression.Length;
        var path = Path.GetFullPath("MyComponent.akbura");
        var text = SourceText.From(source);
        using var workspace = WorkspaceControlEventHookTests.CreateWorkspace(OwnerSource);
        var context = workspace.OpenOrChangeDocumentContext(new Uri(path), text);
        var document = AkburaSyntacticDocument.Create(context.Document);
        var completion = await workspace.LanguageServices.ProjectedCSharp.GetCompletionsAsync(
            document, context, position,
            new AkburaProjectedCompletionTrigger(IsExplicit: true, IsIncomplete: false, Character: '\0'));

        Assert.NotNull(completion);
        var item = Assert.Single(completion.Value.Items, static item => item.DisplayText == "_myField");
        var resolution = await workspace.LanguageServices.ProjectedCSharp.ResolveCompletionAsync(
            document, context, position, item.ResolveKey);

        Assert.NotNull(resolution);
        var changed = text.WithChanges(resolution.Change.Changes);
        var expectedExpression = expression.StartsWith("this.", StringComparison.Ordinal) ? "this._myField" : "_myField";
        Assert.Contains($"Width={{{expectedExpression}}}", changed.ToString(), StringComparison.Ordinal);

        context = workspace.OpenOrChangeDocumentContext(new Uri(path), changed);
        var diagnostics = workspace.LanguageServices.Diagnostics.GetDiagnostics(context, new TextSpan(0, changed.Length));
        Assert.DoesNotContain(diagnostics, static diagnostic => diagnostic.Severity == AkburaDiagnosticSeverity.Error);
    }

    [Theory]
    [InlineData("useAvaloniaProperty(Width)")]
    [InlineData("useAvaloniaProperty(this, WidthProperty)")]
    [InlineData("AvaloniaPropertyHooks.useAvaloniaProperty(this, WidthProperty)")]
    public void HookInvocation_AllFormsHaveNoWorkspaceErrors(string invocation)
    {
        var source = "using Akbura.Hooks;\r\nusing Avalonia.Controls;\r\n" +
            $"state double selfWidth = {invocation};\r\n" +
            "<TextBlock x.Name=\"text\" Text={$\"Current component Width is {selfWidth}\"}/>";
        var path = Path.GetFullPath("MyComponent.akbura");
        using var workspace = WorkspaceControlEventHookTests.CreateWorkspace(OwnerSource);
        var context = workspace.OpenOrChangeDocumentContext(new Uri(path), SourceText.From(source));
        var diagnostics = workspace.LanguageServices.Diagnostics.GetDiagnostics(context, new TextSpan(0, source.Length));

        Assert.DoesNotContain(diagnostics, static diagnostic => diagnostic.Severity == AkburaDiagnosticSeverity.Error);
    }

    [Fact]
    public void Completion_OffersAvaloniaPropertyHookFromRuntimeMetadata()
    {
        const string source = "using Akbura.Hooks;\r\nusing Avalonia.Controls;\r\n" +
            "state double selfWidth = useAva;\r\n<TextBlock x.Name=\"text\"/>";
        var position = source.IndexOf("useAva", StringComparison.Ordinal) + "useAva".Length;
        var path = Path.GetFullPath("MyComponent.akbura");
        using var workspace = WorkspaceControlEventHookTests.CreateWorkspace(OwnerSource);
        var context = workspace.OpenOrChangeDocumentContext(new Uri(path), SourceText.From(source));
        var document = AkburaSyntacticDocument.Create(context.Document);
        var completion = workspace.LanguageServices.Completion.GetCompletions(document, context, position);
        var item = Assert.Single(completion.Items, static item => item.DisplayText == "useAvaloniaProperty");

        Assert.Equal(AkburaCompletionKind.Hook, item.Kind);
    }
}
