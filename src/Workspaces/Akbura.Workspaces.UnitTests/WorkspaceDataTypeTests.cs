using Akbura.Language;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;

namespace Akbura.Workspaces.UnitTests;

public sealed class WorkspaceDataTypeTests
{
    private const string FileName = "MyContentableComponent.akbura";
    private const string Source =
        "param Control Content;\r\n\r\n" +
        "<Border x.DataType=\"MyContentableComponent\" bg-red-300 p-4 Child=${Binding Content}/>";

    [Fact]
    public void DataType_WithUtilitiesAndBindingHasNoSyntaxErrors()
    {
        using var workspace = new AkburaWorkspace();
        var document = AkburaSyntacticDocument.Parse(SourceText.From(Source), FileName);

        Assert.Empty(workspace.LanguageServices.Diagnostics.GetSyntacticDiagnostics(
            document,
            new TextSpan(0, Source.Length)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("x.Name=\"content\"")]
    public void DataType_IncrementalAttributeTypingMatchesFullParse(string originalAttribute)
    {
        const string prefix = "param Control Content;\r\n\r\n<Border ";
        const string suffix = " bg-red-300 p-4 Child=${Binding Content}/>";
        const string attribute = "x.DataType=\"MyContentableComponent\"";
        using var workspace = new AkburaWorkspace();
        var text = SourceText.From(prefix + originalAttribute + suffix);
        var document = AkburaSyntacticDocument.Parse(text, FileName);
        text = text.WithChanges(new TextChange(new TextSpan(prefix.Length, originalAttribute.Length), ""));
        document = document.WithText(text);
        var position = prefix.Length;

        foreach (var character in attribute)
        {
            text = text.WithChanges(new TextChange(new TextSpan(position++, 0), character.ToString()));
            document = document.WithText(text);
            var full = AkburaSyntacticDocument.Parse(text, FileName);
            var span = new TextSpan(0, text.Length);
            var incrementalDiagnostics = workspace.LanguageServices.Diagnostics.GetSyntacticDiagnostics(document, span);
            var fullDiagnostics = workspace.LanguageServices.Diagnostics.GetSyntacticDiagnostics(full, span);

            Assert.True(
                fullDiagnostics.Select(static diagnostic => (diagnostic.Code, diagnostic.Span, diagnostic.Message)).SequenceEqual(
                    incrementalDiagnostics.Select(static diagnostic => (diagnostic.Code, diagnostic.Span, diagnostic.Message))),
                $"Incremental diagnostics differ after typing '{text}': " +
                string.Join("; ", incrementalDiagnostics.Select(static diagnostic => diagnostic.ToString())));
        }

        Assert.Equal(Source, text.ToString());
        Assert.Empty(workspace.LanguageServices.Diagnostics.GetSyntacticDiagnostics(
            document,
            new TextSpan(0, text.Length)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("x.")]
    [InlineData("x.Da")]
    public void DataType_AttributeCompletionOffersDirective(string prefix)
    {
        var source = "<Border " + prefix + "/>";
        var position = "<Border ".Length + prefix.Length;
        using var workspace = CreateWorkspace();
        var context = Open(workspace, source);
        var document = AkburaSyntacticDocument.Create(context.Document);
        var result = workspace.LanguageServices.Completion.GetCompletions(document, context, position);
        var item = Assert.Single(result.Items, static item => item.DisplayText == "x.DataType");

        Assert.Equal("x.DataType=\"\"", item.InsertText);
        Assert.Equal(1, item.CaretOffsetFromEnd);
        Assert.True(item.TriggerCompletionAfterInsert);
        Assert.Equal(prefix, document.Text.ToString(result.ApplicableSpan));
    }

    [Fact]
    public void DataType_ClassificationResolvesGeneratedComponentType()
    {
        using var workspace = CreateWorkspace();
        var context = Open(workspace, Source);
        var position = Source.IndexOf("MyContentableComponent", StringComparison.Ordinal);
        var typeSpan = new TextSpan(position, "MyContentableComponent".Length);
        var classifications = workspace.LanguageServices.Classification.GetClassifications(context, typeSpan);

        Assert.Contains(classifications, classification =>
            classification.Span == typeSpan && classification.Kind == AkburaClassificationKind.ClassName);
        Assert.DoesNotContain(classifications, classification =>
            classification.Span.OverlapsWith(typeSpan) && classification.Kind == AkburaClassificationKind.String);
    }

    [Theory]
    [InlineData("", true, false, '\0')]
    [InlineData("MyCont", true, false, '\0')]
    [InlineData("M", false, false, 'M')]
    [InlineData("MyCont", false, true, 't')]
    public async Task DataType_TypeCompletionOffersGeneratedComponentType(string prefix, bool isExplicit, bool isIncomplete, char character)
    {
        var source = "param Control Content;\r\n\r\n<Border x.DataType=\"" + prefix + "\"/>";
        var position = source.IndexOf('"') + 1 + prefix.Length;
        using var workspace = CreateWorkspace();
        var context = Open(workspace, source);
        var document = AkburaSyntacticDocument.Create(context.Document);
        var result = await workspace.LanguageServices.ProjectedCSharp.GetCompletionsAsync(
            document,
            context,
            position,
            new AkburaProjectedCompletionTrigger(isExplicit, isIncomplete, character));

        Assert.NotNull(result);
        var item = Assert.Single(result.Value.Items, static item => item.DisplayText == "MyContentableComponent");
        var resolution = await workspace.LanguageServices.ProjectedCSharp.ResolveCompletionAsync(
            document,
            context,
            position,
            item.ResolveKey);

        Assert.NotNull(resolution);
        var changed = document.Text.WithChanges(resolution.Change.Changes).ToString();
        Assert.Contains("x.DataType=\"MyContentableComponent\"", changed);
    }

    [Fact]
    public async Task DataType_WorkspaceTypingPreservesTypeClassificationAndCompletion()
    {
        const string prefix = "param Control Content;\r\n\r\n<Border ";
        const string suffix = " bg-red-300 p-4 Child=${Binding Content}/>";
        const string attribute = "x.DataType=\"MyContentableComponent\"";
        using var workspace = CreateWorkspace();
        var text = SourceText.From(prefix + suffix);
        var context = Open(workspace, text);
        var document = AkburaSyntacticDocument.Create(context.Document);
        _ = workspace.LanguageServices.Classification.GetClassifications(context, new TextSpan(0, text.Length));
        var position = prefix.Length;

        foreach (var character in attribute)
        {
            text = text.WithChanges(new TextChange(new TextSpan(position++, 0), character.ToString()));
            document = document.WithText(text);
            context = Open(workspace, text);
        }

        Assert.Equal(Source, context.Document.Text.ToString());
        Assert.Empty(workspace.LanguageServices.Diagnostics.GetSyntacticDiagnostics(
            AkburaSyntacticDocument.Create(context.Document),
            new TextSpan(0, text.Length)));
        var typeStart = Source.IndexOf("MyContentableComponent", StringComparison.Ordinal);
        var typeSpan = new TextSpan(typeStart, "MyContentableComponent".Length);
        var classifications = workspace.LanguageServices.Classification.GetClassifications(context, typeSpan);
        Assert.Contains(classifications, classification =>
            classification.Span == typeSpan && classification.Kind == AkburaClassificationKind.ClassName);

        text = text.WithChanges(new TextChange(TextSpan.FromBounds(typeStart + "MyCont".Length, typeSpan.End), ""));
        document = document.WithText(text);
        context = Open(workspace, text);
        position = typeStart + "MyCont".Length;
        Assert.True(document.TryGetCSharpCompletionContext(position, out var completionContext));
        Assert.Equal(AkburaCSharpCompletionContextKind.Type, completionContext.Kind);
        Assert.Equal("MyCont", text.ToString(completionContext.HostSpan));
        var result = await workspace.LanguageServices.ProjectedCSharp.GetCompletionsAsync(
            document,
            context,
            position,
            new AkburaProjectedCompletionTrigger(IsExplicit: true, IsIncomplete: false, Character: '\0'));

        Assert.NotNull(result);
        Assert.Contains(result.Value.Items, static item => item.DisplayText == "MyContentableComponent");
    }

    private static AkburaDocumentContext Open(AkburaWorkspace workspace, string source)
    {
        return Open(workspace, SourceText.From(source));
    }

    private static AkburaDocumentContext Open(AkburaWorkspace workspace, SourceText text)
    {
        return workspace.OpenOrChangeDocumentContext(
            new Uri(Path.Combine(Environment.CurrentDirectory, "Components", FileName)),
            text);
    }

    private static AkburaWorkspace CreateWorkspace()
    {
        const string csharpSource = """
            global using Avalonia.Controls;

            namespace Avalonia.Controls
            {
                public class Control { }
                public class Border : Control
                {
                    public Control Child { get; set; }
                }
            }

            namespace Akbura
            {
                public class AkburaControl : Avalonia.Controls.Control { }
            }
            """;
        var platformAssemblies = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator) ?? [];
        var compilation = CSharpCompilation.Create(
            nameof(WorkspaceDataTypeTests),
            [CSharpSyntaxTree.ParseText(csharpSource)],
            platformAssemblies.Select(static path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var project = new ProjectContext(
            ProjectId.CreateNewId(),
            projectFilePath: string.Empty,
            projectDirectory: Environment.CurrentDirectory,
            rootNamespace: "PurityUIDashborad",
            compilation,
            ImmutableArray<ProjectReference>.Empty);
        return new AkburaWorkspace(project);
    }
}
