using Akbura.Language.Syntax;
using Akbura.Workspaces.AutomaticPairing;
using Akbura.Workspaces.Completion;
using Akbura.Workspaces.Projects;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Akbura.Workspaces.UnitTests;

public sealed partial class WorkspaceAkcssContextualTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BaseThemeCompletionUsesTheExportAndPreservesTheQualifier(bool inline)
    {
        using var workspace = WorkspaceControlEventHookTests.CreateWorkspace();
        Open(workspace, "ThemeLibrary.akcss", Imports + Theme);
        var (context, document, position) = OpenAt(workspace,
            Imports + "@theme Button T { @based-on ThemeLibrary.|; }", inline);
        var services = workspace.LanguageServices;
        var completion = await services.ProjectedCSharp.GetCompletionsAsync(document, context, position, new(true, false, '\0'));
        Assert.NotNull(completion);
        var item = Assert.Single(completion.Value.Items, candidate => candidate.DisplayText == "MyTheme");
        var resolution = await services.ProjectedCSharp.ResolveCompletionAsync(document, context, position, item.ResolveKey);
        Assert.NotNull(resolution);
        Assert.Contains("@based-on ThemeLibrary.MyTheme;", document.Text.WithChanges(resolution.Change.Changes).ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReceiverChangesExpectedTypeAndTemplateBindingAllowsNativeConversion(bool inline)
    {
        const string receivers = """
            using Avalonia;
            using Avalonia.Controls;
            using Avalonia.Controls.Presenters;
            using Avalonia.Layout;

            namespace Targets;

            public class Parent : Button
            {
                public static readonly StyledProperty<HorizontalAlignment> ChoiceProperty = AvaloniaProperty.Register<Parent, HorizontalAlignment>("Choice");
                public HorizontalAlignment Choice { get => GetValue(ChoiceProperty); set => SetValue(ChoiceProperty, value); }
            }

            public class Child : ContentPresenter
            {
                public static readonly StyledProperty<VerticalAlignment> ChoiceProperty =
                    AvaloniaProperty.Register<Child, VerticalAlignment>("Choice");
                public VerticalAlignment Choice { get => GetValue(ChoiceProperty); set => SetValue(ChoiceProperty, value); }
            }
            """;
        const string prefix = Imports + "@using Targets;\r\n";
        using var workspace = WorkspaceControlEventHookTests.CreateWorkspace(receivers);
        var outer = OpenAt(workspace, prefix + "@theme Parent T { Choice: |; ^ /template/ Child { Choice: VerticalAlignment.Top; } }", inline);
        var inner = OpenAt(workspace, prefix + "@theme Parent T { Choice: HorizontalAlignment.Left; ^ /template/ Child { Choice: |; } }", inline);
        var services = workspace.LanguageServices;
        Assert.Contains(services.Completion.GetCompletions(outer.Syntactic, outer.Context, outer.Position).Items,
            item => item.DisplayText.EndsWith("Right", StringComparison.Ordinal));
        var nested = services.Completion.GetCompletions(inner.Syntactic, inner.Context, inner.Position);
        Assert.Contains(nested.Items, item => item.DisplayText.EndsWith("Top", StringComparison.Ordinal));
        Assert.DoesNotContain(nested.Items, item => item.DisplayText.EndsWith("Right", StringComparison.Ordinal));
        var projected = await services.ProjectedCSharp.GetCompletionsAsync(inner.Syntactic, inner.Context,
            inner.Position, new(true, false, '\0'));
        Assert.NotNull(projected);
        Assert.Contains(projected.Value.Items, item => item.DisplayText == "VerticalAlignment");
        Assert.DoesNotContain(projected.Value.Items, item => item.DisplayText.StartsWith("__", StringComparison.Ordinal));

        var binding = OpenAt(workspace, prefix + "@theme Parent T { @template { <Child> @template-bind Ch|; </Child> } }", inline);
        var item = Assert.Single(services.Completion.GetCompletions(binding.Syntactic, binding.Context, binding.Position)
            .Items, candidate => candidate.DisplayText == "Choice");
        Assert.Contains("VerticalAlignment", item.Description);
        Assert.Contains("HorizontalAlignment", item.Description);
    }

    [Fact]
    public void TemplateFormattingIsIdempotentAndTypingDistinguishesTraversalFromMarkup()
    {
        const string source = Imports +
            "@mixin Button Configure(double width) {\r\nWidth: width;\r\n}\r\n" +
            "@theme Button T {\r\n@template {\r\n<TextBlock>  literal  </TextBlock>\r\n}\r\n" +
            "@include Configure(\r\n2d\r\n);\r\n^ /template/ (global::Avalonia.Controls.TextBlock) {\r\nOpacity: 1d;\r\n}\r\n}";
        using var workspace = WorkspaceControlEventHookTests.CreateWorkspace();
        var document = AkburaSyntacticDocument.Parse(SourceText.From(source), "Hello.akcss");
        var formatting = workspace.LanguageServices.Formatting;
        var changes = formatting.FormatDocument(document, new());
        Assert.NotEmpty(changes);
        var formatted = document.WithText(document.Text.WithChanges(changes));
        Assert.Empty(formatting.FormatDocument(formatted, new()));
        Assert.Contains(">  literal  </TextBlock>", formatted.Text.ToString());
        Assert.Contains("^ /template/ (global::Avalonia.Controls.TextBlock)", formatted.Text.ToString());

        var typing = workspace.LanguageServices.Typing;
        var traversal = OpenAt(workspace, Imports + "@theme Button T { ^ /template| TextBlock { } }", inline: false);
        var slash = typing.GetResult(traversal.Syntactic, new(AkburaTypingCommandKind.Type, traversal.Position, "/", new(4, 4, true, "\r\n"), null));
        var slashChanges = slash.Handled ? slash.Changes : [new TextChange(new TextSpan(traversal.Position, 0), "/")];
        Assert.Equal(traversal.Syntactic.Text.ToString().Insert(traversal.Position, "/"),
            traversal.Syntactic.Text.WithChanges(slashChanges).ToString());
        var tag = OpenAt(workspace, Imports + "@theme Button T { @template { <TextBlock| } }", inline: false);
        var close = typing.GetResult(tag.Syntactic, new(AkburaTypingCommandKind.Type, tag.Position, ">", new(4, 4, true, "\r\n"), null));
        Assert.True(close.Handled);
        Assert.Contains("</TextBlock>", tag.Syntactic.Text.WithChanges(close.Changes).ToString());
    }

    [Fact]
    public void RootNamespaceAndCSharpReferenceChangesRebuildThemeIdentity()
    {
        const string viewSource = "using Avalonia.Controls;\r\n<Button Theme={global::Gallery.Hello.MyTheme} />";
        using var workspace = WorkspaceControlEventHookTests.CreateWorkspace();
        var theme = Open(workspace, "Hello.akcss", Imports + Theme);
        var original = Open(workspace, "View.akbura", viewSource);
        var position = viewSource.IndexOf("MyTheme", StringComparison.Ordinal);
        var definition = workspace.LanguageServices.Definition;
        Assert.Equal(theme.Document.FilePath, definition.GetDefinition(original, position)!.TargetFilePath);

        var context = original.Project.Context;
        var updatedCompilation = context.CSharpCompilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(
            "namespace Gallery { public static class Hello { public static Avalonia.Styling.ControlTheme MyTheme => null!; } }",
            path: Path.Combine(Environment.CurrentDirectory, "ExternalTheme.cs")));
        var updatedContext = new ProjectContext(context.RoslynProjectId, context.ProjectFilePath, context.ProjectDirectory,
            "NewRoot", updatedCompilation, context.ProjectReferences);
        workspace.AddOrUpdateProject(updatedContext);
        var updated = InCurrentSnapshot(workspace, original);
        var external = definition.GetDefinition(updated, position);
        Assert.NotNull(external);
        Assert.EndsWith("Hello.metadata.cs", external.TargetFilePath);
        Assert.Contains("MyTheme", external.TargetText!.ToString());
        Assert.Equal(theme.Document.FilePath, definition.GetDefinition(original, position)!.TargetFilePath);
        var changedSource = viewSource.Replace("Gallery", "NewRoot", StringComparison.Ordinal);
        var changed = Open(workspace, "View.akbura", changedSource);
        Assert.Equal(theme.Document.FilePath, definition.GetDefinition(changed, changedSource.IndexOf("MyTheme", StringComparison.Ordinal))!.TargetFilePath);
    }
}
