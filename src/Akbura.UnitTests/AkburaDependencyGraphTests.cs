using Akbura.Language;
using Akbura.Language.CodeGeneration;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Immutable;
using System.Linq;

namespace Akbura.UnitTests;

public sealed class AkburaDependencyGraphTests
{
    [Fact]
    public void ComponentReference_DependsOnSurfaceButNotUnrelatedDocuments()
    {
        var index = CreateIndex(
            ("Page.akbura", "using Demo.Components; <Card />"),
            ("Components/Card.akbura", "param string Title; <Border />"),
            ("Unrelated.akbura", "<Border />"));
        var graph = AkburaDependencyGraph.Create(index);
        var dependency = Assert.Single(graph.GetDependencies(GetDocument(index, "Page.akbura")));

        Assert.Equal("Components/Card.akbura", dependency.Document.FilePath);
        Assert.False(dependency.IncludesBody);
        Assert.Empty(graph.GetDependencies(GetDocument(index, "Components/Card.akbura")));
    }

    [Fact]
    public void TransitiveComponentCycle_IncludesEveryDependencyOnceWithoutSelf()
    {
        var index = CreateIndex(
            ("First.akbura", "<Second />"),
            ("Second.akbura", "<Third />"),
            ("Third.akbura", "<First />"),
            ("Other.akbura", "<Border />"));
        var dependencies = AkburaDependencyGraph.Create(index).GetDependencies(GetDocument(index, "First.akbura"));

        Assert.Equal(new[] { "Second.akbura", "Third.akbura" }, dependencies.Select(static dependency => dependency.Document.FilePath));
        Assert.All(dependencies, static dependency => Assert.False(dependency.IncludesBody));
    }

    [Fact]
    public void AliasedOrAmbiguousSimpleName_KeepsAllMatchingComponentCandidates()
    {
        var index = CreateIndex(
            ("Page.akbura", "using Alias = Demo.First; <Alias::Widget />"),
            ("First/Widget.akbura", "<Border />"),
            ("Second/Widget.akbura", "<Border />"));
        var dependencies = AkburaDependencyGraph.Create(index).GetDependencies(GetDocument(index, "Page.akbura"));

        Assert.Equal(new[] { "First/Widget.akbura", "Second/Widget.akbura" }, dependencies.Select(static dependency => dependency.Document.FilePath));
        Assert.All(dependencies, static dependency => Assert.False(dependency.IncludesBody));
    }

    [Fact]
    public void CSharpExpressionReference_IsDiscoveredWithoutAMarkupTagReference()
    {
        var index = CreateIndex(
            ("Page.akbura", "state object value = Demo.Model.WidthProperty; <Border />"),
            ("Model.akbura", "param double Width; <Border />"));
        var dependency = Assert.Single(AkburaDependencyGraph.Create(index).GetDependencies(GetDocument(index, "Page.akbura")));

        Assert.Equal("Model.akbura", dependency.Document.FilePath);
        Assert.False(dependency.IncludesBody);
    }

    [Fact]
    public void ImportedApplyChain_DependsOnBodiesAndSourceMappingsTransitively()
    {
        var index = CreateIndex(
            ("Page.akbura", "using Demo.Styles.Top.akcss; <Border class=\"top\" />"),
            ("Styles/Top.akcss", "@using Demo.Styles.Base.akcss; .top { @apply basic; }"),
            ("Styles/Base.akcss", ".basic { Width: 10; }"),
            ("Styles/Other.akcss", ".other { Height: 20; }"));
        var graph = AkburaDependencyGraph.Create(index);
        var dependencies = graph.GetDependencies(GetDocument(index, "Page.akbura"));

        Assert.Equal(new[] { "Styles/Base.akcss", "Styles/Top.akcss" }, dependencies.Select(static dependency => dependency.Document.FilePath));
        Assert.All(dependencies, static dependency => Assert.True(dependency.IncludesBody));
        Assert.Equal("Styles/Base.akcss", Assert.Single(graph.GetDependencies(GetDocument(index, "Styles/Top.akcss"))).Document.FilePath);
    }

    [Fact]
    public void CyclicApplyImports_TerminateAndPreserveBodyDependencies()
    {
        var index = CreateIndex(
            ("Page.akbura", "using Demo.Styles.First.akcss; <Border class=\"first\" />"),
            ("Styles/First.akcss", "@using Demo.Styles.Second.akcss; .first { @apply second; }"),
            ("Styles/Second.akcss", "@using Demo.Styles.First.akcss; .second { @apply first; }"));
        var graph = AkburaDependencyGraph.Create(index);
        var dependencies = graph.GetDependencies(GetDocument(index, "Page.akbura"));

        Assert.Equal(2, dependencies.Length);
        Assert.All(dependencies, static dependency => Assert.True(dependency.IncludesBody));
        Assert.Equal("Styles/Second.akcss", Assert.Single(graph.GetDependencies(GetDocument(index, "Styles/First.akcss"))).Document.FilePath);
    }

    [Fact]
    public void GlobalUsingInOrdinaryComponent_CreatesWholeDocumentDependency()
    {
        var index = CreateIndex(
            ("Page.akbura", "<Border />"),
            ("Imports.akbura", "global using Avalonia.Controls; <Border />"));
        var dependency = Assert.Single(AkburaDependencyGraph.Create(index).GetDependencies(GetDocument(index, "Page.akbura")));

        Assert.Equal("Imports.akbura", dependency.Document.FilePath);
        Assert.True(dependency.IncludesBody);
    }

    [Fact]
    public void GlobalAkcssImport_CarriesImportedModuleDependencyToComponents()
    {
        var index = CreateIndex(
            ("Page.akbura", "<Border class=\"shared\" />"),
            ("GlobalUsings.akcss", "@using Demo.Styles.Shared.akcss;"),
            ("Styles/Shared.akcss", ".shared { Width: 10; }"));
        var dependencies = AkburaDependencyGraph.Create(index).GetDependencies(GetDocument(index, "Page.akbura"));

        Assert.Equal(new[] { "GlobalUsings.akcss", "Styles/Shared.akcss" }, dependencies.Select(static dependency => dependency.Document.FilePath));
        Assert.All(dependencies, static dependency => Assert.True(dependency.IncludesBody));
    }

    [Fact]
    public void InlineModule_SharesOwnerBodyAndDiscoversItsImports()
    {
        var index = CreateIndex(
            ("Owner.akbura", "@akcss { @using Demo.Styles.Shared.akcss; .local { @apply shared; } } <Border class=\"local\" />"),
            ("Styles/Shared.akcss", ".shared { Width: 10; }"));
        var graph = AkburaDependencyGraph.Create(index);
        var owner = GetDocument(index, "Owner.akbura");
        var inline = Assert.Single(index.InlineAkcssDescriptors);

        Assert.Same(owner, inline.DocumentVersion);
        Assert.Equal("Styles/Shared.akcss", Assert.Single(graph.GetDependencies(owner)).Document.FilePath);

        var dependencies = graph.GetDependencies(inline);
        Assert.Equal(2, dependencies.Length);
        Assert.Contains(dependencies, dependency => ReferenceEquals(dependency.Document, owner) && dependency.IncludesBody);
        Assert.All(dependencies, static dependency => Assert.True(dependency.IncludesBody));
    }

    [Fact]
    public void RecoverySyntax_ConservativelyDependsOnEveryOtherDocumentBody()
    {
        var index = CreateIndex(
            ("Broken.akbura", "}"),
            ("Other.akbura", "<Border />"),
            ("Styles/Shared.akcss", ".shared { Width: 10; }"));
        var dependencies = AkburaDependencyGraph.Create(index).GetDependencies(GetDocument(index, "Broken.akbura"));

        Assert.Equal(2, dependencies.Length);
        Assert.All(dependencies, static dependency => Assert.True(dependency.IncludesBody));
        Assert.DoesNotContain(dependencies, static dependency => dependency.Document.FilePath == "Broken.akbura");
    }

    [Fact]
    public void UnresolvedCandidate_IsRetainedAndResolvesWhenTheDeclarationIsAdded()
    {
        var initial = CreateIndex(("Page.akbura", "<Future />"));
        var initialDocument = GetDocument(initial, "Page.akbura");

        Assert.Contains(new DocumentDependencyName(DocumentDependencyKind.Component, "Future"), initialDocument.DependencyNames);
        Assert.Empty(AkburaDependencyGraph.Create(initial).GetDependencies(initialDocument));

        var updated = CreateIndex(("Page.akbura", "<Future />"), ("Future.akbura", "<Border />"));
        var dependency = Assert.Single(AkburaDependencyGraph.Create(updated).GetDependencies(GetDocument(updated, "Page.akbura")));

        Assert.Equal("Future.akbura", dependency.Document.FilePath);
        Assert.False(dependency.IncludesBody);
    }

    [Fact]
    public void InputReordering_DoesNotChangeDependencyOrderOrIdentities()
    {
        var files = new[]
        {
            ("Page.akbura", "<First><Second /></First>"),
            ("First.akbura", "<Border />"),
            ("Second.akbura", "<Border />"),
        };
        var initial = CreateIndex(files);
        Array.Reverse(files);
        var reordered = CreateIndex(files);
        var before = AkburaDependencyGraph.Create(initial).GetDependencies(GetDocument(initial, "Page.akbura"));
        var after = AkburaDependencyGraph.Create(reordered).GetDependencies(GetDocument(reordered, "Page.akbura"));

        Assert.Equal(before.Select(static dependency => dependency.Identity), after.Select(static dependency => dependency.Identity));
        Assert.Equal(before.Select(static dependency => dependency.IncludesBody), after.Select(static dependency => dependency.IncludesBody));
    }

    [Fact]
    public void AppliedModuleLineChange_InvalidatesBodyVersionEvenWithUnchangedSurface()
    {
        const string page = "using Demo.Styles.Shared.akcss; <Border class=\"shared\" />";
        const string module = ".shared { Width: 10; }";
        var initial = CreateIndex(("Page.akbura", page), ("Styles/Shared.akcss", module));
        var updated = CreateIndex(("Page.akbura", page), ("Styles/Shared.akcss", "\r\n" + module));
        var before = Assert.Single(AkburaDependencyGraph.Create(initial).GetDependencies(GetDocument(initial, "Page.akbura")));
        var after = Assert.Single(AkburaDependencyGraph.Create(updated).GetDependencies(GetDocument(updated, "Page.akbura")));

        Assert.True(before.IncludesBody);
        Assert.True(before.Document.HasSameSurface(after.Document));
        Assert.False(before.Document.HasSameGenerationShape(after.Document));
    }

    private static AkburaProjectIndex CreateIndex(params (string Path, string Source)[] files)
    {
        var trees = files.Select(static file => file.Path.EndsWith(".akcss", StringComparison.Ordinal)
            ? (AkburaSyntaxTree)AkcssSyntaxTree.ParseText(file.Source, file.Path, AkcssGeneratedModuleNames.GetMetadataName("Demo", file.Path))
            : ComponentSyntaxTree.ParseText(file.Source, file.Path)).ToImmutableArray();

        return AkburaProjectIndex.Create(CSharpCompilation.Create("DependencyGraphTests"), trees, "Demo", string.Empty);
    }

    private static DocumentSyntaxVersion GetDocument(AkburaProjectIndex index, string path)
    {
        return Assert.Single(index.Documents, document => document.FilePath == path);
    }
}
