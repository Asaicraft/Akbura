using Akbura.Language;
using Akbura.Language.CodeGeneration;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using System.IO;

namespace Akbura.UnitTests;

public sealed class AkburaGenerationCatalogTests
{
    [Fact]
    public void Create_CollectsComponentExternalAndInlineAkcssInputs()
    {
        var fixture = CreateFixture();
        var catalog = fixture.Catalog;

        var component = Assert.Single(catalog.Components);
        var external = Assert.Single(catalog.ExternalAkcssModules);
        var inline = Assert.Single(catalog.InlineAkcssModules);

        Assert.Equal("Views/PlannerView.akbura", component.SourcePath);
        Assert.Equal("Styles/Shared.akcss", external.SourcePath);
        Assert.Equal("Styles/Shared.akcss", external.ModuleIdentity);
        Assert.Equal("Views/PlannerView.akbura", inline.SourcePath);
        Assert.Equal("Views/PlannerView.akbura.inline.0.akcss", inline.ModuleIdentity);

        var externalSyntax = Assert.IsType<AkburaSyntax>(external.Module.DeclaringSyntax, exactMatch: false);
        var inlineSyntax = Assert.IsType<AkburaSyntax>(inline.Module.DeclaringSyntax, exactMatch: false);

        Assert.Equal(
            AkcssGeneratedModuleNames.GetFullyQualifiedTypeName("Demo", external.ModuleIdentity),
            catalog.AkcssModuleTypeNames[externalSyntax]);

        Assert.Equal(
            AkcssGeneratedModuleNames.GetFullyQualifiedTypeName("Demo", inline.ModuleIdentity),
            catalog.AkcssModuleTypeNames[inlineSyntax]);

        Assert.True(catalog.AkcssSourceMap.TryGetLineDirective(externalSyntax, out _, out var externalPath));
        Assert.Equal(fixture.ExternalAkcssPath, externalPath);

        Assert.True(catalog.AkcssSourceMap.TryGetLineDirective(inlineSyntax, out _, out var inlinePath));
        Assert.Equal(fixture.ComponentPath, inlinePath);
    }

    [Fact]
    public void ModulePlanner_AssignsStableSymbolAndRuntimeIndices()
    {
        var fixture = CreateFixture();
        ref readonly var input = ref fixture.Catalog.ExternalAkcssModules.ItemRef(0);

        var plan = AkcssModulePlanner.Create(input, fixture.Catalog.RootNamespace);

        try
        {
            Assert.Equal("Styles/Shared.akcss", plan.SourcePath);
            Assert.Equal("Styles/Shared.akcss", plan.ModuleIdentity);
            Assert.Equal("Demo.Generated", plan.GeneratedNamespace);

            Assert.Equal(
                AkcssGeneratedModuleNames.GetTypeName("Styles/Shared.akcss"),
                plan.GeneratedTypeName);

            Assert.Equal(input.Module.MetadataName, plan.MetadataName);

            Assert.False(plan.IsInlined);
            Assert.Equal(2, plan.Symbols.Length);
            Assert.Equal(2, plan.RuntimeStyles.Length);

            Assert.Equal(AkcssSymbolGenerationKind.Style, plan.Symbols[0].Kind);
            Assert.Equal(AkcssSymbolGenerationKind.Utility, plan.Symbols[1].Kind);

            Assert.Equal(0, plan.Symbols[0].SymbolIndex);
            Assert.Equal(1, plan.Symbols[1].SymbolIndex);
            Assert.Equal(0, plan.Symbols[0].RuntimeStyleIndex);
            Assert.Equal(1, plan.Symbols[1].RuntimeStyleIndex);
            Assert.False(plan.Symbols[0].HasErrors);
            Assert.False(plan.Symbols[1].HasErrors);

            Assert.Equal(AkcssRuntimeStyleKind.Generated, plan.RuntimeStyles[0].Kind);
            Assert.Equal(AkcssRuntimeStyleKind.Generated, plan.RuntimeStyles[1].Kind);
        }
        finally
        {
            plan.ReturnToPool();
        }
    }

    private static CatalogFixture CreateFixture()
    {
        const string rootNamespace = "Demo";

        const string componentSource =
            """
            using Avalonia.Controls;
            using Demo.Styles.Shared.akcss;

            @akcss {
                @using Avalonia.Controls;

                .local {
                    Width: 10;
                }
            }

            <Border class="local shared" />
            """;

        const string externalAkcssSource =
            """
            @using Avalonia.Controls;

            .shared {
                Height: 20;
            }

            @utilities {
                .spacing-(double value) {
                    Width: value;
                }
            }
            """;

        const string csharpSource =
            """
            namespace Demo;

            public partial class PlannerView
            {
            }
            """;

        var projectDirectory = Path.Combine(Path.GetTempPath(), "AkburaGenerationCatalogTests");
        var componentPath = Path.Combine(projectDirectory, "Views", "PlannerView.akbura");
        var externalAkcssPath = Path.Combine(projectDirectory, "Styles", "Shared.akcss");
        var externalSourcePath = "Styles/Shared.akcss";

        var externalLogicalName = AkcssGeneratedModuleNames.GetMetadataName(
            rootNamespace,
            externalSourcePath);

        var componentTree = ComponentSyntaxTree.ParseText(
            SourceText.From(componentSource),
            componentPath);

        var externalAkcssTree = AkcssSyntaxTree.ParseText(
            SourceText.From(externalAkcssSource),
            externalAkcssPath,
            externalLogicalName);

        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

        var csharpCompilation = CSharpCompilation.Create(
            "AkburaGenerationCatalogTests",
            syntaxTrees: [CSharpSyntaxTree.ParseText(csharpSource, parseOptions)],
            references: SymbolTests.CreateAvaloniaReferences(),
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        var catalog = AkburaGenerationCatalogBuilder.Create(
            csharpCompilation,
            [componentTree, externalAkcssTree],
            rootNamespace,
            projectDirectory);

        return new CatalogFixture(catalog, componentPath, externalAkcssPath);
    }

    private sealed class CatalogFixture
    {
        public CatalogFixture(
            AkburaGenerationCatalog catalog,
            string componentPath,
            string externalAkcssPath)
        {
            Catalog = catalog;
            ComponentPath = componentPath;
            ExternalAkcssPath = externalAkcssPath;
        }

        public AkburaGenerationCatalog Catalog { get; }

        public string ComponentPath { get; }

        public string ExternalAkcssPath { get; }
    }
}
