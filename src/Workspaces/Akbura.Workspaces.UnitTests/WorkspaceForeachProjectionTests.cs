using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;

namespace Akbura.Workspaces.UnitTests;

public sealed class WorkspaceForeachProjectionTests
{
    [Theory]
    [InlineData("param int[] numbers = default!; <Host>$foreach (var item in num|bers) { <Leaf /> }</Host>", "numbers")]
    [InlineData("param int[] numbers = default!; <Host>$foreach (var item in numbers) { <Leaf Text={ite|m.ToString()} /> }</Host>", "item")]
    [InlineData("param int[] numbers = default!; <Host>$foreach (var item in numbers) { var doubled = item * 2; <Leaf Text={dou|bled.ToString()} /> }</Host>", "doubled")]
    [InlineData("param object[] people = default!; <Host>$foreach (var item in people) { if (item is not Person person) { continue; } <Leaf Text={per|son.Name} /> }</Host>", "person")]
    [InlineData("param int[] numbers = default!; <Host>$foreach (var item in numbers) { <Leaf Text={@ind|ex.ToString()} /> }</Host>", "index")]
    public async Task CompletionPreservesSourceAndTheRealTypedLoopScope(string source, string name)
    {
        var fixture = Create(source);
        using var workspace = fixture.Workspace;
        Assert.True(fixture.Document.TryGetCSharpCompletionContext(fixture.Position, out var context));
        Assert.True(AkburaCSharpProjectionFactory.TryCreate(fixture.Document, fixture.Context, context, out var projection));
        Assert.Equal(fixture.Document.Text.ToString(projection.HostSpan),
            projection.Root.ToFullString().Substring(projection.ProjectedSpan.Start, projection.ProjectedSpan.Length));
        Assert.True(projection.TryMapPositionToHost(projection.ProjectedPosition, out var mapped));
        Assert.Equal(fixture.Position, mapped);

        var completion = await workspace.LanguageServices.ProjectedCSharp.GetCompletionsAsync(
            fixture.Document, fixture.Context, fixture.Position, new(true, false, '\0'));

        Assert.NotNull(completion);
        Assert.Contains(completion.Value.Items, item => item.DisplayText == name);
    }

    [Theory]
    [InlineData("param int[] numbers = default!; <Host>$foreach (var item in numbers) { <Leaf Text={ite|m.ToString()} /> }</Host>", "item", "int")]
    [InlineData("param object[] people = default!; <Host>$foreach (var item in people) { if (item is not Person person) { continue; } <Leaf Text={per|son.Name} /> }</Host>", "person", "Person")]
    [InlineData("param int[] numbers = default!; <Host>$foreach (var item in numbers) { var doubled = item * 2; <Leaf Text={dou|bled.ToString()} /> }</Host>", "doubled", "int")]
    public void NavigationAndHoverMapLoopDeclarationsToTheHost(string source, string name, string type)
    {
        var fixture = Create(source);
        using var workspace = fixture.Workspace;
        var info = workspace.LanguageServices.QuickInfo.GetQuickInfo(fixture.Context, fixture.Position);
        var navigation = workspace.LanguageServices.Definition.GetDefinition(fixture.Context, fixture.Position);

        Assert.NotNull(info);
        Assert.Contains(type, info.Signature, StringComparison.Ordinal);
        Assert.Equal(name, fixture.Source.Substring(info.SourceSpan.Start, info.SourceSpan.Length));
        Assert.NotNull(navigation);
        Assert.Equal(fixture.Context.Document.FilePath, navigation.TargetFilePath);
        Assert.Equal(fixture.Document.Text.Lines.GetLinePosition(fixture.Source.IndexOf(name, StringComparison.Ordinal)),
            navigation.TargetLineSpan.Start);
    }

    [Fact]
    public void RenameUsesTheLoopDeclarationIdentityAcrossIndependentExpressionProbes()
    {
        var fixture = Create("param int[] numbers = default!; <Host>$foreach (var item in numbers) { <Leaf Text={ite|m.ToString()} /> <Leaf Text={item.ToString()} /> }</Host>");
        using var workspace = fixture.Workspace;
        var info = workspace.LanguageServices.Rename.GetRenameInfo(fixture.Context, fixture.Position);
        var references = workspace.LanguageServices.References.FindReferences(fixture.Context, fixture.Position,
            includeDeclaration: true);

        Assert.True(info.CanRename);
        Assert.Equal(3, references.Locations.Length);
        Assert.Single(references.Locations, location => location.IsDeclaration);
        var edits = workspace.LanguageServices.Rename.GetRenameChanges(fixture.Context, fixture.Position, "value");
        var changed = fixture.Document.Text.WithChanges(Assert.Single(edits.Changes).Value).ToString();
        Assert.DoesNotContain("item", changed, StringComparison.Ordinal);
        Assert.Contains("$foreach (var value in numbers)", changed, StringComparison.Ordinal);
    }

    [Fact]
    public void IndexHoverExplainsItsSourceCoordinateAndItCannotBeRenamed()
    {
        var fixture = Create("<Host>$foreach (var item in new[] { 1 }) { <Leaf Text={@ind|ex.ToString()} /> }</Host>");
        using var workspace = fixture.Workspace;
        var info = workspace.LanguageServices.QuickInfo.GetQuickInfo(fixture.Context, fixture.Position);

        Assert.NotNull(info);
        Assert.Contains(info.Details, detail => detail.Contains("Read-only zero-based", StringComparison.Ordinal));
        Assert.False(workspace.LanguageServices.Rename.GetRenameInfo(fixture.Context, fixture.Position).CanRename);
    }

    [Theory]
    [InlineData("<Host>$f|</Host>")]
    [InlineData("<Host>$foreach (var item in new[] { 1 }) { $f| }</Host>")]
    public void MarkupDirectiveCompletionIsAvailableInsideIterationBodies(string source)
    {
        var position = source.IndexOf('|');
        var document = AkburaSyntacticDocument.Parse(SourceText.From(source.Remove(position, 1)), "Loop.akbura");
        using var workspace = new AkburaWorkspace();
        var completion = workspace.LanguageServices.Completion.GetCompletions(document, null, position);

        var item = Assert.Single(completion.Items);
        Assert.Equal("$foreach", item.DisplayText);
        Assert.Equal("$foreach ()", item.InsertText);
        Assert.Equal(1, item.CaretOffsetFromEnd);
    }

    private static Fixture Create(string sourceWithCaret)
    {
        const string imports = "using Example; ";
        var caret = sourceWithCaret.IndexOf('|');
        var source = imports + sourceWithCaret.Remove(caret, 1);
        var position = imports.Length + caret;
        var platform = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?.Split(Path.PathSeparator) ?? [];
        var compilation = CSharpCompilation.Create("WorkspaceForeachProjectionTests",
            [CSharpSyntaxTree.ParseText(Definitions, path: Path.GetFullPath("LoopDefinitions.cs"))],
            platform.Select(static path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.DoesNotContain(compilation.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var workspace = new AkburaWorkspace(new ProjectContext(ProjectId.CreateNewId(), string.Empty,
            Environment.CurrentDirectory, string.Empty, compilation, ImmutableArray<ProjectReference>.Empty));
        var path = Path.GetFullPath("Loop.akbura");
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
            }
            public sealed class Leaf : Avalonia.Controls.Control { public string Text { get; set; } }
        }
        """;
}
