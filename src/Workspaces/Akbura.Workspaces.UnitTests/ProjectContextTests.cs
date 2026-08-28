using Akbura.Language;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;

namespace Akbura.Workspaces.UnitTests;

public sealed class ProjectContextTests
{
    [Fact]
    public void Constructor_RemovesCurrentAssemblyMetadataReference()
    {
        const string assemblyName = "CurrentProject";
        var platformReference = MetadataReference.CreateFromFile(
            typeof(object).Assembly.Location);
        var selfReference = CreateReference(
            assemblyName,
            "1.0.0.0",
            filePath: "metadata-cache.bin");
        var compilation = CreateCompilation(
            assemblyName,
            "1.0.0.0",
            platformReference,
            selfReference);

        var context = CreateContext(compilation);

        Assert.DoesNotContain(
            selfReference,
            context.CSharpCompilation.References);
        Assert.Contains(
            platformReference,
            context.CSharpCompilation.References);
        Assert.Contains(
            selfReference,
            compilation.References);
    }

    [Fact]
    public void Constructor_RemovesStaleVersionAndPreservesOtherAssembly()
    {
        const string assemblyName = "CurrentProject";
        var staleReference = CreateReference(
            assemblyName,
            "2.0.0.0");
        var otherReference = CreateReference(
            "OtherProject",
            "1.0.0.0");
        var compilation = CreateCompilation(
            assemblyName,
            "1.0.0.0",
            staleReference,
            otherReference);

        var context = CreateContext(compilation);

        Assert.DoesNotContain(
            staleReference,
            context.CSharpCompilation.References);
        Assert.Contains(
            otherReference,
            context.CSharpCompilation.References);
    }

    [Fact]
    public void Constructor_RemovesUnboundCurrentAssemblyMetadataReference()
    {
        const string assemblyName = "CurrentProject";
        var selfReference = CreateReference(
                assemblyName,
                "1.0.0.0",
                filePath: "metadata-cache.bin")
            .WithProperties(
                new MetadataReferenceProperties(
                    MetadataImageKind.Module));
        var compilation = CreateCompilation(
            assemblyName,
            "1.0.0.0",
            selfReference);

        var context = CreateContext(compilation);

        Assert.DoesNotContain(
            selfReference,
            context.CSharpCompilation.References);
    }

    [Fact]
    public void SemanticDiagnostics_DoNotBindAgainstCurrentProjectOutput()
    {
        const string assemblyName = "CurrentProject";
        const string componentSource = """
            using Avalonia.Controls;

            param int Value;

            bool IsDirty() => Value != 0;

            <TextBlock Text={IsDirty()}/>
            """;
        var platformReference = MetadataReference.CreateFromFile(
            typeof(object).Assembly.Location);
        var selfReference = CreateReference(
            assemblyName,
            "1.0.0.0",
            """
            namespace CurrentProject
            {
                public class DiagnosticInput
                {
                    public int Value { get; set; }

                    public bool IsDirty() => false;
                }
            }
            """);
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText("""
                [assembly: System.Reflection.AssemblyVersion("1.0.0.0")]

                namespace Akbura
                {
                    public class AkburaControl : Avalonia.Controls.Control
                    {
                    }
                }

                namespace Avalonia.Controls
                {
                    public class Control
                    {
                    }

                    public sealed class TextBlock : Control
                    {
                        public object? Text { get; set; }
                    }
                }
                """)],
            [platformReference, selfReference],
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary));
        using var workspace = new AkburaWorkspace(
            new ProjectContext(
                ProjectId.CreateNewId(),
                projectFilePath: string.Empty,
                projectDirectory: Environment.CurrentDirectory,
                rootNamespace: "CurrentProject",
                compilation,
                ImmutableArray<ProjectReference>.Empty));
        var text = SourceText.From(componentSource);
        var context = workspace.OpenOrChangeDocumentContext(
            new Uri(Path.GetFullPath("DiagnosticInput.akbura")),
            text);

        var diagnostics = workspace.LanguageServices.Diagnostics
            .GetDiagnostics(
                context,
                new TextSpan(0, text.Length));

        Assert.DoesNotContain(
            diagnostics,
            static diagnostic => diagnostic.Message.Contains(
                "ambiguous",
                StringComparison.OrdinalIgnoreCase) ||
                diagnostic.Message.Contains(
                    "Неоднознач",
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SemanticDiagnostics_DoNotDuplicateGeneratedComponentParameters()
    {
        const string routerSource = """
            namespace CurrentProject;

            param bind string Url = "";

            <TextBlock/>
            """;
        const string linkSource = """
            namespace CurrentProject;

            param Router Router;

            state string currentUrl = Router.Url;

            <TextBlock Text={currentUrl}/>
            """;
        var compilation = CSharpCompilation.Create(
            "CurrentProject",
            [CSharpSyntaxTree.ParseText("""
                namespace Akbura.ComponentTree
                {
                    public sealed class Parameter<TOwner, TValue>
                    {
                    }
                }

                namespace Akbura
                {
                    public class AkburaControl : Avalonia.Controls.Control
                    {
                    }
                }

                namespace Avalonia.Controls
                {
                    public class Control
                    {
                    }

                    public sealed class TextBlock : Control
                    {
                        public object? Text { get; set; }
                    }
                }

                namespace CurrentProject
                {
                    public partial class Router : Akbura.AkburaControl
                    {
                        public static readonly
                            Akbura.ComponentTree.Parameter<Router, string>
                            UrlProperty = new();

                        public string Url { get; set; } = "";
                    }
                }
                """)],
            [MetadataReference.CreateFromFile(
                typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary));
        using var workspace = new AkburaWorkspace(
            CreateContext(compilation));
        workspace.OpenOrChangeDocumentContext(
            new Uri(Path.GetFullPath("Router.akbura")),
            SourceText.From(routerSource));
        var text = SourceText.From(linkSource);
        var context = workspace.OpenOrChangeDocumentContext(
            new Uri(Path.GetFullPath("Link.akbura")),
            text);

        var diagnostics = workspace.LanguageServices.Diagnostics
            .GetDiagnostics(
                context,
                new TextSpan(0, text.Length));

        Assert.DoesNotContain(
            diagnostics,
            static diagnostic => diagnostic.Message.Contains(
                "ambiguous",
                StringComparison.OrdinalIgnoreCase) ||
                diagnostic.Message.Contains(
                    "Неоднознач",
                    StringComparison.OrdinalIgnoreCase));
    }

    private static ProjectContext CreateContext(
        CSharpCompilation compilation)
    {
        return new ProjectContext(
            ProjectId.CreateNewId(),
            projectFilePath: string.Empty,
            projectDirectory: Environment.CurrentDirectory,
            rootNamespace: string.Empty,
            compilation,
            ImmutableArray<ProjectReference>.Empty);
    }

    private static CSharpCompilation CreateCompilation(
        string assemblyName,
        string assemblyVersion,
        params MetadataReference[] references)
    {
        return CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(
                $"[assembly: System.Reflection.AssemblyVersion(\"{assemblyVersion}\")]")],
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary));
    }

    private static PortableExecutableReference CreateReference(
        string assemblyName,
        string assemblyVersion,
        string source = "",
        string? filePath = null)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(
                $"[assembly: System.Reflection.AssemblyVersion(\"{assemblyVersion}\")]\n" +
                source)],
            [MetadataReference.CreateFromFile(
                typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        Assert.True(
            result.Success,
            string.Join(
                Environment.NewLine,
                result.Diagnostics));

        stream.Position = 0;
        return MetadataReference.CreateFromStream(
            stream,
            filePath: filePath ?? assemblyName + ".dll");
    }
}
