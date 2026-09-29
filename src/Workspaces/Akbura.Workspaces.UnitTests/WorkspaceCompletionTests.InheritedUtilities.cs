using Akbura.Language.Symbols;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using System.Xml.Linq;

namespace Akbura.Workspaces.UnitTests;

public sealed partial class WorkspaceCompletionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Completion_TypedUtilityIncludesGeneratedAkburaComponent(bool localStyles)
    {
        using var workspace = CreateRuntimeUtilityWorkspace(CreateRuntimeUtilityReferences());
        workspace.OpenOrChangeDocumentContext(new Uri(Path.GetFullPath("Child.akbura")),
            SourceText.From("namespace Demo;\r\nusing Avalonia.Controls;\r\n<Border />"));
        var source = "using Demo;\r\n" + (localStyles ? """
            @akcss {
                @using Avalonia.Controls;
                @utilities {
                    Control.row-(int row) { Grid.Row: row; }
                    Border.row-border { }
                }
            }
            """ : "using Akbura.Styles.akcss;") + "\r\n<Child row-";
        var path = Path.GetFullPath("Parent.akbura");
        var text = SourceText.From(source);
        var context = workspace.OpenOrChangeDocumentContext(new Uri(path), text);
        var result = workspace.LanguageServices.Completion.GetCompletions(AkburaSyntacticDocument.Parse(text, path), context, source.Length);

        var item = Assert.Single(result.Items, candidate => candidate.DisplayText == "row-(int row)");
        Assert.Equal(AkburaCompletionKind.TailwindUtility, item.Kind);
        Assert.Equal("Control", item.Suffix);
        Assert.DoesNotContain(result.Items, candidate => candidate.DisplayText == "row-border");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Completion_UtilityIncludesBaseTargetForDerivedComponent(bool metadata)
    {
        var references = CreateRuntimeUtilityReferences();
        const string styles = """
            @using Avalonia.Controls;
            @using Avalonia.Controls.Primitives;
            @utilities {
                Border.rounded-lg { }
                Border.rounded-(string radius) { }
                TemplatedControl.rounded-lg { }
                TemplatedControl.rounded-(string radius) { }
                TextBlock.rounded-text-only { }
            }
            """;
        if (metadata)
        {
            const string library = """
                using Akbura.CompilerAnotations;
                using Avalonia.Controls;
                using Avalonia.Controls.Primitives;
                [assembly: AkcssModuleReference(typeof(Library.Styles))]
                namespace Library;
                [AkcssModule("Styles.akcss", MetadataName = "Library.Styles.akcss", FormatVersion = 3)]
                public class Styles
                {
                    [AkcssSymbol(Name = "rounded-lg", MetadataName = "Border.rounded-lg", Kind = AkcssSymbolKind.Utility, TargetType = typeof(Border))]
                    public class BorderRounded { }
                    [AkcssSymbol(Name = "rounded", MetadataName = "Border.rounded", Kind = AkcssSymbolKind.Utility, TargetType = typeof(Border))]
                    [AkcssUtilityParameter(Ordinal = 0, Name = "radius", Type = typeof(string))]
                    public class BorderRadius { }
                    [AkcssSymbol(Name = "rounded-lg", MetadataName = "TemplatedControl.rounded-lg", Kind = AkcssSymbolKind.Utility, TargetType = typeof(TemplatedControl))]
                    public class Rounded { }
                    [AkcssSymbol(Name = "rounded", MetadataName = "TemplatedControl.rounded", Kind = AkcssSymbolKind.Utility, TargetType = typeof(TemplatedControl))]
                    [AkcssUtilityParameter(Ordinal = 0, Name = "radius", Type = typeof(string))]
                    public class Radius { }
                    [AkcssSymbol(Name = "rounded-text-only", MetadataName = "TextBlock.rounded-text-only", Kind = AkcssSymbolKind.Utility, TargetType = typeof(TextBlock))]
                    public class TextOnly { }
                }
                """;
            var libraryCompilation = CSharpCompilation.Create("Library",
                [CSharpSyntaxTree.ParseText(library)], references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            using var stream = new MemoryStream();
            var emit = libraryCompilation.Emit(stream);
            Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
            references = references.Add(MetadataReference.CreateFromImage(stream.ToArray()));
        }

        using var workspace = CreateRuntimeUtilityWorkspace(references);
        if (!metadata)
        {
            workspace.OpenOrChangeDocumentContext(new Uri(Path.GetFullPath("Styles.akcss")), SourceText.From(styles));
        }

        var source = "using Avalonia.Controls;\r\nusing " +
            (metadata ? "Library.Styles.akcss" : "Styles.akcss") + ";\r\n<Button rounded-";
        var path = Path.GetFullPath("InheritedUtilities.akbura");
        var text = SourceText.From(source);
        var context = workspace.OpenOrChangeDocumentContext(new Uri(path), text);
        var document = AkburaSyntacticDocument.Parse(text, path);
        var result = workspace.LanguageServices.Completion.GetCompletions(document, context, source.Length);

        AssertInheritedRoundedUtilities(result);
        Assert.DoesNotContain(result.Items, item => item.DisplayText == "rounded-text-only");
        if (metadata)
        {
            var module = Assert.Single(context.Project.Compilation.GetAkcssModuleSymbolsByLogicalName("Library.Styles.akcss"));
            var utility = Assert.Single(module.AkcssSymbols.OfType<ITailwindUtilitySymbol>(),
                candidate => candidate.Name == "rounded-lg" &&
                    candidate.TargetType.Symbol?.ToDisplayString() == "Avalonia.Controls.Primitives.TemplatedControl");
            Assert.IsType<MetadataTailwindUtilitySymbol>(utility);
        }
    }

    [Fact]
    public void Completion_BuiltInMetadataIncludesInheritedRoundedUtilities()
    {
        using var workspace = CreateRuntimeUtilityWorkspace(CreateRuntimeUtilityReferences());
        const string source = "using Avalonia.Controls;\r\nusing Akbura.Styles.akcss;\r\n<Button rounded-";
        var path = Path.GetFullPath("InheritedUtilities.akbura");
        var text = SourceText.From(source);
        var context = workspace.OpenOrChangeDocumentContext(new Uri(path), text);
        var module = Assert.Single(context.Project.Compilation.GetAkcssModuleSymbolsByLogicalName("Akbura.Styles.akcss"));
        Assert.Contains(module.AkcssSymbols.OfType<ITailwindUtilitySymbol>(), candidate =>
            candidate.Name == "rounded-lg" && candidate.TargetType.Symbol?.ToDisplayString() == "Avalonia.Controls.Primitives.TemplatedControl");
        var result = workspace.LanguageServices.Completion.GetCompletions(
            AkburaSyntacticDocument.Parse(text, path), context, source.Length);
        AssertInheritedRoundedUtilities(result);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Completion_InheritedRoundedUtilitiesSurviveTyping(bool nested, bool globalImport)
    {
        using var workspace = CreateRuntimeUtilityWorkspace(CreateRuntimeUtilityReferences());
        if (globalImport)
        {
            workspace.OpenOrChangeDocumentContext(new Uri(Path.GetFullPath("GlobalUsings.akbura")),
                SourceText.From("global using Akbura.Styles.akcss;"));
        }

        var source = "using Avalonia.Controls;\r\n" +
            (globalImport ? string.Empty : "using Akbura.Styles.akcss;\r\n") +
            (nested ? "<Border>\r\n    <Button |/>\r\n</Border>" : "<Button |/>");
        var position = source.IndexOf('|');
        var text = SourceText.From(source.Remove(position, 1));
        var path = Path.GetFullPath("InheritedUtilities.akbura");
        var uri = new Uri(path);
        var context = workspace.OpenOrChangeDocumentContext(uri, text);
        var document = AkburaSyntacticDocument.Parse(text, path);
        AssertInheritedRoundedUtilities(workspace.LanguageServices.Completion.GetCompletions(document, context, position));

        foreach (var character in "rounded-")
        {
            text = text.WithChanges(new TextChange(new TextSpan(position, 0), character.ToString()));
            position++;
            document = document.WithText(text);
            context = workspace.OpenOrChangeDocumentContext(uri, text);
            var result = workspace.LanguageServices.Completion.GetCompletions(document, context, position);
            AssertInheritedRoundedUtilities(result);
        }
    }

    private static void AssertInheritedRoundedUtilities(AkburaCompletionResult result)
    {
        foreach (var display in new[] { "rounded-lg", "rounded-(string radius)" })
        {
            var item = Assert.Single(result.Items, item => item.DisplayText == display);
            Assert.Equal(AkburaCompletionKind.TailwindUtility, item.Kind);
            Assert.Equal("TemplatedControl", item.Suffix);
        }
    }

    private static AkburaWorkspace CreateRuntimeUtilityWorkspace(ImmutableArray<MetadataReference> references)
    {
        var compilation = CSharpCompilation.Create("InheritedUtilities", references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        return new AkburaWorkspace(new ProjectContext(ProjectId.CreateNewId(), string.Empty,
            Environment.CurrentDirectory, string.Empty, compilation, ImmutableArray<ProjectReference>.Empty));
    }

    private static ImmutableArray<MetadataReference> CreateRuntimeUtilityReferences()
    {
        var output = new DirectoryInfo(AppContext.BaseDirectory);
        var src = output.Parent!.Parent!.Parent!.Parent!.Parent!;
        var version = Assert.Single(XDocument.Load(Path.Combine(src.Parent!.FullName,
            "Directory.Build.props")).Descendants("AvaloniaVersion")).Value;
        var packages = Environment.GetEnvironmentVariable("NUGET_PACKAGES") ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
        var paths = new[]
        {
            Path.Combine(src.FullName, "Akbura", "bin", output.Parent.Name, output.Name, "Akbura.dll"),
            Path.Combine(packages, "avalonia", version, "ref", output.Name, "Avalonia.Base.dll"),
            Path.Combine(packages, "avalonia", version, "ref", output.Name, "Avalonia.Controls.dll"),
        };
        return CreatePlatformReferences().Concat(paths.Select(path => MetadataReference.CreateFromFile(path))).ToImmutableArray();
    }
}
