using Akbura.Diagnostics;
using Akbura.Language;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using System.Text;

namespace Akbura.UnitTests;

public sealed class AkburaDiagnosticReferenceTests
{
    private const string Source = "@using Avalonia.Controls;\r\n// 😀 библиотека\r\nBorder.shared { Missing: 1; }\r\n";

    [Fact]
    public void ReferencedProjectDiagnostic_ResolvesTransitiveOwnerWithoutUsingConsumerText()
    {
        var external = AkcssSyntaxTree.ParseText(SourceText.From(Source), "Library/Styles/Theme.akcss", "Library.Styles.Theme.akcss");
        var library = new AkburaCompilation(CreateCompilation("Library"), [], [external], rootNamespace: "Library");
        var reference = library.ToReference();
        var intermediate = new AkburaCompilation(CreateCompilation("Intermediate").AddReferences(reference.CSharpReference),
            [], [], compilationReferences: [reference]);
        var intermediateReference = intermediate.ToReference();
        var consumer = ComponentSyntaxTree.ParseText(SourceText.From("<object />"), "Consumer.akbura");
        var compilation = new AkburaCompilation(CreateCompilation("Consumer").AddReferences(intermediateReference.CSharpReference),
            [consumer], [], compilationReferences: [intermediateReference]);
        var diagnostic = Assert.Single(library.GetSemanticModel(external).GetSemanticDiagnostics(external.GetRoot()),
            static diagnostic => diagnostic.Code == ErrorCodes.AKBURA_SEMANTIC_AkcssPropertyNotFound);

        var actual = AkburaDiagnosticEngine.CreateSemanticRecord(consumer, compilation.GetSemanticModel(consumer), diagnostic);

        Assert.Equal(external.FilePath, actual.FilePath);
        Assert.Equal(diagnostic.Message, actual.Message);
        Assert.Equal(diagnostic.Span, actual.Span);
        Assert.Equal(external.Text.Lines.GetLinePositionSpan(diagnostic.Span), actual.LineSpan);
        Assert.Equal(2, actual.LineSpan.Start.Line);
    }

    [Fact]
    public void EmbeddedModuleDiagnostic_UsesMaterializedSourceWithoutParsingUnrelatedFiles()
    {
        var directory = Path.Combine(Path.GetTempPath(), nameof(AkburaDiagnosticReferenceTests), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var assemblyPath = Path.Combine(directory, "Library.dll");
            var library = CreateCompilation("Library");
            var manifest = AkburaModuleManifestBuilder.Build("Library", "Library",
            [
                new AkburaModuleSourceText("Styles/Theme.akcss", Source),
                new AkburaModuleSourceText("Styles/Unused.akcss", ".unused { }\r\n"),
            ], library);
            using var manifestStream = new MemoryStream();
            AkburaModuleManifestSerializer.Write(manifestStream, manifest);
            var manifestBytes = manifestStream.ToArray();
            var emit = library.Emit(assemblyPath, manifestResources:
            [
                new ResourceDescription(AkburaModuleManifest.ResourceName,
                    () => new MemoryStream(manifestBytes, writable: false), isPublic: true),
                CreateSourceResource("Styles/Theme.akcss", Source),
                CreateSourceResource("Styles/Unused.akcss", ".unused { }\r\n"),
            ]);
            Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
            var consumer = ComponentSyntaxTree.ParseText(SourceText.From("<object />"), "Consumer.akbura");
            var compilation = new AkburaCompilation(CreateCompilation("Consumer")
                .AddReferences(MetadataReference.CreateFromFile(assemblyPath)), [consumer]);
            var module = Assert.Single(compilation.ReferencedModules,
                static module => module.Manifest.AssemblyName == "Library");
            var external = Assert.Single(module.GetAkcssSyntaxTreesByLogicalName("Library.Styles.Theme.akcss"));
            Assert.False(module.IsSyntaxTreeMaterialized("Styles/Unused.akcss"));
            var diagnostic = Assert.Single(compilation.GetSemanticModel(external).GetSemanticDiagnostics(external.GetRoot()),
                static diagnostic => diagnostic.Code == ErrorCodes.AKBURA_SEMANTIC_AkcssPropertyNotFound);

            var actual = AkburaDiagnosticEngine.CreateSemanticRecord(consumer, compilation.GetSemanticModel(consumer), diagnostic);

            Assert.Equal("Styles/Theme.akcss", actual.FilePath);
            Assert.Equal(diagnostic.Message, actual.Message);
            Assert.Equal(diagnostic.Span, actual.Span);
            Assert.Equal(external.Text.Lines.GetLinePositionSpan(diagnostic.Span), actual.LineSpan);
            Assert.StartsWith("Library, Version=", actual.Properties["akbura.module-identity"], StringComparison.Ordinal);
            var direct = Assert.Single(AkburaDiagnosticEngine.Collect(external, compilation.GetSemanticModel(external)),
                static diagnostic => diagnostic.Id == ErrorCodes.AKBURA_SEMANTIC_AkcssPropertyNotFound);
            Assert.Equal(actual.LogicalId, direct.LogicalId);
            Assert.False(module.IsSyntaxTreeMaterialized("Styles/Unused.akcss"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void DifferentEmbeddedModules_WithIdenticalSourcePathAndDiagnostic_AreNotDeduplicated()
    {
        var directory = Path.Combine(Path.GetTempPath(), nameof(AkburaDiagnosticReferenceTests), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var references = new List<MetadataReference>();
            foreach (var name in new[] { "FirstLibrary", "SecondLibrary" })
            {
                var library = CreateCompilation(name);
                var manifest = AkburaModuleManifestBuilder.Build(name, name,
                    [new AkburaModuleSourceText("Styles/Theme.akcss", Source)], library);
                using var manifestStream = new MemoryStream();
                AkburaModuleManifestSerializer.Write(manifestStream, manifest);
                var manifestBytes = manifestStream.ToArray();
                var path = Path.Combine(directory, name + ".dll");
                var emit = library.Emit(path, manifestResources:
                [
                    new ResourceDescription(AkburaModuleManifest.ResourceName,
                        () => new MemoryStream(manifestBytes, writable: false), isPublic: true),
                    CreateSourceResource("Styles/Theme.akcss", Source),
                ]);
                Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
                references.Add(MetadataReference.CreateFromFile(path));
            }

            var consumer = ComponentSyntaxTree.ParseText(SourceText.From("<object />"), "Consumer.akbura");
            var compilation = new AkburaCompilation(CreateCompilation("Consumer").AddReferences(references), [consumer]);
            var model = compilation.GetSemanticModel(consumer);
            var diagnostics = new List<AkburaDiagnosticRecord>();
            foreach (var module in compilation.ReferencedModules.Where(static module =>
                module.Manifest.AssemblyName is "FirstLibrary" or "SecondLibrary"))
            {
                var tree = Assert.Single(module.GetAkcssSyntaxTreesByLogicalName(module.Manifest.AssemblyName + ".Styles.Theme.akcss"));
                var diagnostic = Assert.Single(compilation.GetSemanticModel(tree).GetSemanticDiagnostics(tree.GetRoot()),
                    static diagnostic => diagnostic.Code == ErrorCodes.AKBURA_SEMANTIC_AkcssPropertyNotFound);
                diagnostics.Add(AkburaDiagnosticEngine.CreateSemanticRecord(consumer, model, diagnostic));
            }

            Assert.Equal(2, diagnostics.Count);
            Assert.Equal(diagnostics[0].FilePath, diagnostics[1].FilePath);
            Assert.Equal(diagnostics[0].Span, diagnostics[1].Span);
            Assert.Equal(diagnostics[0].Message, diagnostics[1].Message);
            Assert.NotEqual(diagnostics[0].LogicalId, diagnostics[1].LogicalId);
            Assert.Equal(2, AkburaDiagnosticCollection.Deduplicate(diagnostics).Length);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static ResourceDescription CreateSourceResource(string path, string source)
    {
        var bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(source)).ToArray();
        return new ResourceDescription(path, () => new MemoryStream(bytes, writable: false), isPublic: true);
    }

    private static CSharpCompilation CreateCompilation(string name) => CSharpCompilation.Create(
        name,
        references: SymbolTests.CreateAvaloniaReferences(),
        options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
}
