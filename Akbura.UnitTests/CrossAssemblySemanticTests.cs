using Akbura.Language;
using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Immutable;
using System.Text;

namespace Akbura.UnitTests;

public sealed class CrossAssemblySemanticTests
{
    [Fact]
    public void SemanticModel_UsesComponentsAndAkcssFromReferencedAssembly()
    {
        const string componentSource =
            """
            namespace Library.Components;

            param string Title;
            inject System.IServiceProvider Services;
            """;
        const string unusedComponentSource =
            """
            namespace Library.Components;
            param string Ignored;
            """;
        const string akcssSource =
            """
            @using Avalonia.Controls;

            Control.libraryCard {
                Width: 321;
            }

            @utilities {
                Control.libraryPadded {
                    Padding: 9;
                }
            }
            """;
        const string libraryCSharpSource =
            """
            namespace Library.Components;

            public sealed partial class LibraryCard : Akbura.AkburaControl
            {
                private static readonly System.Collections.Immutable.ImmutableArray<
                    Akbura.ComponentTree.Parameter> s_parameters = [];

                protected override Avalonia.Controls.Control Update() =>
                    new Avalonia.Controls.Border();

                protected override Avalonia.Controls.Control FirstUpdate() =>
                    new Avalonia.Controls.Border();

                protected override System.Collections.Immutable.ImmutableArray<
                    Akbura.ComponentTree.Parameter> GetParameters() => s_parameters;
            }
            """;
        const string consumerSource =
            """
            using Avalonia.Controls;
            using Library.Components;
            using Library.Styles.Theme.akcss;

            <LibraryCard Title="From DLL" class="libraryCard" libraryPadded />
            """;

        var directory = Path.Combine(
            Path.GetTempPath(),
            nameof(CrossAssemblySemanticTests),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var assemblyPath = Path.Combine(directory, "Library.dll");

        try
        {
            var references = CreateReferences();
            var libraryCompilation = CSharpCompilation.Create(
                "Library",
                [CSharpSyntaxTree.ParseText(libraryCSharpSource)],
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var manifest = AkburaModuleManifestBuilder.Build(
                "Library",
                "Library",
                [
                    new AkburaModuleSourceText(
                        "Components/LibraryCard.akbura",
                        componentSource),
                    new AkburaModuleSourceText(
                        "Components/Unused.akbura",
                        unusedComponentSource),
                    new AkburaModuleSourceText(
                        "Styles/Theme.akcss",
                        akcssSource),
                ],
                libraryCompilation);
            var manifestBytes = SerializeManifest(manifest);
            var resources = new[]
            {
                CreateResource(AkburaModuleManifest.ResourceName, manifestBytes),
                CreateEmbeddedSourceResource("Components/LibraryCard.akbura", componentSource),
                CreateEmbeddedSourceResource("Components/Unused.akbura", unusedComponentSource),
                CreateEmbeddedSourceResource("Styles/Theme.akcss", akcssSource),
            };

            var emitResult = libraryCompilation.Emit(
                assemblyPath,
                manifestResources: resources);
            Assert.True(
                emitResult.Success,
                string.Join(Environment.NewLine, emitResult.Diagnostics));

            var libraryReference = MetadataReference.CreateFromFile(assemblyPath);
            var consumerCSharpCompilation = CSharpCompilation.Create(
                "Consumer",
                syntaxTrees: null,
                references: references.Append(libraryReference),
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var consumerTree = AkburaSyntaxTree.ParseText(
                consumerSource,
                "Views/Consumer.akbura");
            var compilation = new AkburaCompilation(
                consumerCSharpCompilation,
                [consumerTree],
                rootNamespace: "Consumer");
            var semanticModel = compilation.GetSemanticModel(consumerTree);

            Assert.Single(compilation.SyntaxTrees);
            Assert.Empty(compilation.AkcssSyntaxTrees);
            Assert.Single(compilation.DeclarationTable.Components);
            Assert.Empty(compilation.DeclarationTable.AkcssModules);
            var referencedModule = Assert.Single(
                compilation.ReferencedModules,
                static module => module.Manifest.AssemblyName == "Library");
            Assert.False(referencedModule.IsSyntaxTreeMaterialized("Components/LibraryCard.akbura"));
            Assert.False(referencedModule.IsSyntaxTreeMaterialized("Components/Unused.akbura"));
            Assert.False(referencedModule.IsSyntaxTreeMaterialized("Styles/Theme.akcss"));
            Assert.Equal(Akbura.Language.LocationKind.MetadataFile, referencedModule.Location.Kind);
            Assert.Same(referencedModule, referencedModule.Location.MetadataModule);
            Assert.Equal(referencedModule.Location, new MetadataLocation(referencedModule));
            var referencedComponentSource = Assert.Single(
                referencedModule.Manifest.Sources,
                static source => source.SourceCodePath == "Components/LibraryCard.akbura");
            var referencedComponentDeclaration = Assert.Single(referencedComponentSource.Declarations);
            var referencedAkcssSource = Assert.Single(
                referencedModule.Manifest.Sources,
                static source => source.SourceCodePath == "Styles/Theme.akcss");
            Assert.Equal(
                "Library.Styles.Theme.akcss",
                Assert.Single(referencedAkcssSource.Declarations).MetadataName);

            var updatedCSharpCompilation = consumerCSharpCompilation.AddSyntaxTrees(
                CSharpSyntaxTree.ParseText("internal sealed class AddedType { }"));
            var updatedCompilation = compilation.WithCSharpCompilation(updatedCSharpCompilation);
            var reusedModule = Assert.Single(
                updatedCompilation.ReferencedModules,
                static module => module.Manifest.AssemblyName == "Library");
            Assert.Same(referencedModule, reusedModule);

            var markupRoot = Assert.IsType<MarkupRootSyntax>(
                Assert.Single(
                    consumerTree.GetRoot().Members,
                    static member => member is MarkupRootSyntax));
            var element = markupRoot.Element;
            var component = Assert.IsAssignableFrom<IMarkupComponentSymbol>(
                semanticModel.GetSymbolInfo(element).Symbol);
            Assert.Equal("Library.Components.LibraryCard", component.MetadataName);
            Assert.NotNull(component.AkburaComponent);
            var titleParameter = Assert.Single(component.AkburaComponent!.Parameters);
            Assert.Equal("Title", titleParameter.Name);
            Assert.Equal(
                SpecialType.System_String,
                Assert.IsAssignableFrom<ITypeSymbol>(titleParameter.Type.Symbol).SpecialType);
            var injectedService = Assert.Single(component.AkburaComponent.InjectedServices);
            Assert.Equal("Services", injectedService.Name);
            Assert.Equal("IServiceProvider", injectedService.Type.Name);
            Assert.False(referencedModule.IsSyntaxTreeMaterialized("Components/LibraryCard.akbura"));

            var attributes = element.StartTag!.Attributes;
            Assert.Equal(3, attributes.Count);

            var titleOperation = Assert.IsAssignableFrom<IMarkupPropertySetterOperation>(
                semanticModel.GetOperation(attributes[0]));
            Assert.Same(titleParameter, titleOperation.Property?.Parameter);
            Assert.Equal("From DLL", titleOperation.LiteralValue);

            var classOperation = Assert.IsAssignableFrom<IMarkupPropertySetterOperation>(
                semanticModel.GetOperation(attributes[1]));
            var referencedStyle = Assert.Single(classOperation.AppliedAkcssSymbols);
            Assert.Equal("libraryCard", referencedStyle.Name);
            Assert.Equal("Control", referencedStyle.TargetType.Name);

            var utilityOperation = Assert.IsAssignableFrom<ITailwindUtilityAttributeOperation>(
                semanticModel.GetOperation(attributes[2]));
            var referencedUtility = Assert.Single(utilityOperation.Utilities);
            Assert.Equal("libraryPadded", referencedUtility.Name);
            Assert.Equal("Control", referencedUtility.TargetType.Name);
            Assert.NotEmpty(referencedUtility.Operations);

            Assert.True(semanticModel.GetSemanticDiagnostics(element).IsEmpty);
            foreach (var attribute in attributes)
            {
                Assert.True(semanticModel.GetSemanticDiagnostics(attribute).IsEmpty);
            }

            Assert.False(referencedModule.IsSyntaxTreeMaterialized("Components/LibraryCard.akbura"));
            Assert.True(referencedModule.IsSyntaxTreeMaterialized("Styles/Theme.akcss"));
            Assert.False(referencedModule.IsSyntaxTreeMaterialized("Components/Unused.akbura"));

            var titleSyntax = titleParameter.DeclarationSyntax;
            var injectSyntax = injectedService.DeclarationSyntax;
            var componentSyntax = component.AkburaComponent.DeclarationSyntax;
            Assert.Same(titleSyntax, titleParameter.DeclarationSyntax);
            Assert.Same(injectSyntax, injectedService.DeclarationSyntax);
            Assert.Same(componentSyntax, component.AkburaComponent.DeclarationSyntax);
            Assert.Same(componentSyntax, titleSyntax.Root);
            Assert.Same(componentSyntax, injectSyntax.Root);
            Assert.Equal(
                Assert.Single(referencedComponentDeclaration.Component!.Parameters).SourceStart,
                titleSyntax.FullSpan.Start);
            Assert.Equal(
                Assert.Single(referencedComponentDeclaration.Component.InjectedServices).SourceStart,
                injectSyntax.FullSpan.Start);
            Assert.True(referencedModule.IsSyntaxTreeMaterialized("Components/LibraryCard.akbura"));
            Assert.False(referencedModule.IsSyntaxTreeMaterialized("Components/Unused.akbura"));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static MetadataReference[] CreateReferences()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var trustedPlatformAssemblies =
            AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string
            ?? throw new InvalidOperationException("Trusted platform assemblies are unavailable.");
        foreach (var path in trustedPlatformAssemblies.Split(Path.PathSeparator))
        {
            paths.Add(path);
        }

        foreach (var reference in SymbolTests.CreateAvaloniaReferences())
        {
            if (reference is PortableExecutableReference { FilePath: { Length: > 0 } path })
            {
                paths.Add(path);
            }
        }

        return paths
            .Select(static path => MetadataReference.CreateFromFile(path))
            .ToArray();
    }

    private static byte[] SerializeManifest(AkburaModuleManifest manifest)
    {
        using var stream = new MemoryStream();
        AkburaModuleManifestSerializer.Write(stream, manifest);
        return stream.ToArray();
    }

    private static ResourceDescription CreateEmbeddedSourceResource(string name, string content)
    {
        var preamble = Encoding.Unicode.GetPreamble();
        var text = Encoding.Unicode.GetBytes(content);
        var bytes = new byte[preamble.Length + text.Length];
        preamble.CopyTo(bytes, 0);
        text.CopyTo(bytes, preamble.Length);
        return CreateResource(name, bytes);
    }

    private static ResourceDescription CreateResource(string name, byte[] content)
    {
        return new ResourceDescription(
            name,
            () => new MemoryStream(content, writable: false),
            isPublic: true);
    }
}
