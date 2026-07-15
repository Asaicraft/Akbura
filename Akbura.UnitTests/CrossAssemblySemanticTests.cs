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
                protected override Avalonia.Controls.Control Update() => this;
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
                        "Styles/Theme.akcss",
                        akcssSource),
                ],
                libraryCompilation);
            var manifestBytes = SerializeManifest(manifest);
            var resources = new[]
            {
                CreateResource(AkburaModuleManifest.ResourceName, manifestBytes),
                CreateResource("Components/LibraryCard.akbura", Encoding.UTF8.GetBytes(componentSource)),
                CreateResource("Styles/Theme.akcss", Encoding.UTF8.GetBytes(akcssSource)),
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
            var referencedModule = Assert.Single(
                compilation.ReferencedModules,
                static module => module.Manifest.AssemblyName == "Library");
            Assert.Single(referencedModule.ComponentSyntaxTrees);
            Assert.Equal(Akbura.Language.LocationKind.MetadataFile, referencedModule.Location.Kind);
            Assert.Same(referencedModule, referencedModule.Location.MetadataModule);
            Assert.Equal(referencedModule.Location, new MetadataLocation(referencedModule));
            Assert.Single(referencedModule.AkcssSyntaxTrees);
            Assert.Equal(
                "Library.Styles.Theme.akcss",
                referencedModule.AkcssSyntaxTrees[0].LogicalName);

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

    private static ResourceDescription CreateResource(string name, byte[] content)
    {
        return new ResourceDescription(
            name,
            () => new MemoryStream(content, writable: false),
            isPublic: true);
    }
}

