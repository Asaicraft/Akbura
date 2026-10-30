using Akbura.Language;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace Akbura.UnitTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ComponentMetadataNameCacheCollection
{
    public const string Name = "Component metadata name cache";
}

[Collection(ComponentMetadataNameCacheCollection.Name)]
public sealed class ComponentMetadataNameCacheTests : IDisposable
{
    private readonly List<string> _referenceFiles = [];

    public void Dispose()
    {
        foreach (var file in _referenceFiles) File.Delete(file);
    }

    [Fact]
    public void RepeatedRequests_ComputeOnceAndReturnStoredInstanceWithoutPerHitAllocations()
    {
        var tree = new CountingTree("Views/Card.akbura");
        var model = Model();
        var expected = model.GetAkburaComponentMetadataName(tree);
        Assert.Equal("App.Views.Card", expected);
        for (var index = 0; index < 100; index++) Assert.Same(expected, model.GetAkburaComponentMetadataName(tree));
        Assert.Equal(1, tree.NameReads);
        Assert.Single(Cache(model.Compilation));
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 10_000; index++) model.GetAkburaComponentMetadataName(tree);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.InRange(allocated, 0, 64);
        Assert.Equal(1, tree.NameReads);
    }

    [Fact]
    public void NonComponentAndEmptyNames_AreCachedAsEmptyResults()
    {
        var model = Model();
        var styles = AkcssSyntaxTree.ParseText("", "Styles.akcss", "Styles");
        var empty = new CountingTree("");
        Assert.Empty(model.GetAkburaComponentMetadataName(styles));
        Assert.Empty(model.GetAkburaComponentMetadataName(empty));
        Assert.Empty(model.GetAkburaComponentMetadataName(empty));
        Assert.Equal(1, empty.NameReads);
        Assert.Equal(2, Cache(model.Compilation).Count);
    }

    [Fact]
    public void EqualTreesAndSameFilePaths_AreStillDifferentIdentityKeys()
    {
        var first = new CountingTree("Views/Card.akbura", "namespace First;");
        var second = new CountingTree("Views/Card.akbura", "namespace Second;");
        var third = new CountingTree("Views/Other.akbura");
        Assert.True(first.Equals(second));
        var model = Model();
        Assert.Equal("First.Card", model.GetAkburaComponentMetadataName(first));
        Assert.Equal("Second.Card", model.GetAkburaComponentMetadataName(second));
        Assert.Equal("App.Views.Other", model.GetAkburaComponentMetadataName(third));
        Assert.Equal(3, Cache(model.Compilation).Count);
    }

    [Theory]
    [InlineData("", "App", "Views/Card.akbura", "App.Views.Card")]
    [InlineData("namespace Explicit;", "App", "Views/Card.akbura", "Explicit.Card")]
    [InlineData("namespace Explicit.Nested;", "App", "Views/Card.akbura", "Explicit.Nested.Card")]
    [InlineData("", "", "Views/Card.akbura", "Views.Card")]
    [InlineData("", "App.. Root ", "Views/Card.akbura", "App.Root.Views.Card")]
    [InlineData("", "App", "Card.akbura", "App.Card")]
    [InlineData("", "App", "", "")]
    [InlineData("namespace Explicit;", "App", "", "")]
    public void NamespaceRules_AgreeWithOriginalCalculation(string source, string root, string path, string expected)
    {
        var tree = AkburaSyntaxTree.ParseText(source, path);
        var model = Model(root);
        Assert.Equal(expected, Original(model, tree));
        Assert.Equal(expected, model.GetAkburaComponentMetadataName(tree));
        Assert.Equal(expected, model.GetAkburaComponentMetadataName(tree));
    }

    [Fact]
    public void PathRules_PreserveInsideOutsideRootSeparatorsAndCase()
    {
        var project = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "MetadataNames", "Project"));
        var cases = new[]
        {
            (Path.Combine(project, "Views", "Card.akbura"), project, "App.Views.Card"),
            (Path.Combine(project, "Card.akbura"), project, "App.Card"),
            (Path.Combine(project, "..", "Other", "Card.akbura"), project, "App.Card"),
            (Path.Combine(project, "Views", "Card.akbura"), "", "App.Card"),
            (Path.Combine(project, "Views", "Card.akbura"), Path.GetPathRoot(project)!,
                "App." + string.Join('.', Path.GetDirectoryName(project)![Path.GetPathRoot(project)!.Length..]
                    .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries).Append(Path.GetFileName(project)).Append("Views")) + ".Card"),
        };
        foreach (var (path, directory, expected) in cases)
        {
            var tree = AkburaSyntaxTree.ParseText("", path);
            var model = Model("App", directory);
            Assert.Equal(expected, Original(model, tree));
            Assert.Equal(expected, model.GetAkburaComponentMetadataName(tree));
        }
        if (OperatingSystem.IsWindows())
        {
            foreach (var directory in new[] { project.ToUpperInvariant(), project.Replace('\\', '/') })
            {
                var tree = AkburaSyntaxTree.ParseText("", Path.Combine(project, "Views", "Card.akbura").Replace('\\', '/'));
                Assert.Equal("App.Views.Card", Model("App", directory).GetAkburaComponentMetadataName(tree));
            }
        }
    }

    [Fact]
    public void RelativeProjectDirectory_RecomputesAfterCurrentDirectoryChanges()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var first = Directory.CreateDirectory(Path.Combine(directory, "First")).FullName;
        var second = Directory.CreateDirectory(Path.Combine(directory, "Second")).FullName;
        var previous = Environment.CurrentDirectory;
        try
        {
            var tree = new CountingTree(Path.Combine(first, "Project", "Views", "Card.akbura"));
            var model = Model("App", "Project");
            Environment.CurrentDirectory = first;
            Assert.Equal("App.Views.Card", model.GetAkburaComponentMetadataName(tree));
            Assert.Equal(Original(model, tree), model.GetAkburaComponentMetadataName(tree));
            Environment.CurrentDirectory = second;
            Assert.Equal("App.Card", model.GetAkburaComponentMetadataName(tree));
            Assert.Equal(Original(model, tree), model.GetAkburaComponentMetadataName(tree));
            Assert.Empty(Cache(model.Compilation));
            var relativeTree = new CountingTree("Views/Card.akbura");
            var cached = model.GetAkburaComponentMetadataName(relativeTree);
            Environment.CurrentDirectory = first;
            Assert.Same(cached, model.GetAkburaComponentMetadataName(relativeTree));
            Assert.Equal(1, relativeTree.NameReads);
        }
        finally
        {
            Environment.CurrentDirectory = previous;
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("C:Views/Card.akbura", "C:\\Project")]
    [InlineData("\\Views\\Card.akbura", "C:\\Project")]
    [InlineData("C:\\Project\\Views\\Card.akbura", "\\Project")]
    public void WindowsPartiallyQualifiedPaths_AreNotCached(string path, string project)
    {
        if (!OperatingSystem.IsWindows()) return;
        var tree = new CountingTree(path);
        var model = Model("App", project);
        Assert.Equal(Original(model, tree), model.GetAkburaComponentMetadataName(tree));
        Assert.Empty(Cache(model.Compilation));
    }

    [Fact]
    public void SameTree_DifferentCompilationContextsHaveIndependentCaches()
    {
        var project = Path.Combine(Path.GetTempPath(), "MetadataNames", "Project");
        var tree = AkburaSyntaxTree.ParseText("", Path.Combine(project, "Views", "Card.akbura"));
        var first = Model("One", project);
        var second = Model("Two", Path.GetDirectoryName(project)!);
        Assert.Equal("One.Views.Card", first.GetAkburaComponentMetadataName(tree));
        Assert.Equal("Two.Project.Views.Card", second.GetAkburaComponentMetadataName(tree));
        Assert.NotSame(Cache(first.Compilation), Cache(second.Compilation));
    }

    [Fact]
    public void NamespaceText_StillHonorsTheArbitraryRootArgument()
    {
        var tree = AkburaSyntaxTree.ParseText("namespace Old;", "Card.akbura");
        var model = Model();
        Assert.Equal("Old.Card", model.GetAkburaComponentMetadataName(tree));
        Assert.Equal("Other", model.GetAkburaNamespaceText(AkburaSyntaxTree.ParseText("namespace Other;").GetRoot(), tree));
        Assert.Equal("App", model.GetAkburaNamespaceText(AkburaSyntaxTree.ParseText("").GetRoot(), tree));
        Assert.Equal("Old.Card", model.GetAkburaComponentMetadataName(tree));
    }

    [Fact]
    public void RevisionsAndReusableState_DoNotTransferCachedNames()
    {
        var tree = AkburaSyntaxTree.ParseText("namespace Old;", "Card.akbura");
        var original = new AkburaCompilation(CSharp(), [tree], "App");
        Assert.Equal("Old.Card", original.GetSemanticModel(tree).GetAkburaComponentMetadataName(tree));
        var changed = tree.WithChangedText(SourceText.From("namespace New;"));
        var other = AkburaSyntaxTree.ParseText("", "Other.akbura");
        var reference = new AkburaCompilation(CSharp(), [other], "Library");
        var revisions = new[]
        {
            original.WithSyntaxTrees([tree, other]), original.ReplaceSyntaxTree(tree, changed),
            original.AddSyntaxTrees([other]), original.RemoveSyntaxTrees([tree]),
            original.WithCompilationReferences([reference.ToReference()]),
            original.WithCSharpCompilation(original.CSharpCompilation.WithAssemblyName("Updated")),
            original.WithAkcssSyntaxTrees([AkcssSyntaxTree.ParseText("", "Theme.akcss", "Theme")]),
            new AkburaCompilation(CSharp(), [tree], [], "NewRoot", reuseFrom: original),
        };
        foreach (var revision in revisions)
        {
            Assert.NotSame(original, revision);
            Assert.Empty(Cache(revision));
            Assert.NotSame(Cache(original), Cache(revision));
            foreach (var current in revision.SyntaxTrees)
            {
                var model = revision.GetSemanticModel(current);
                Assert.Equal(Original(model, current), model.GetAkburaComponentMetadataName(current));
            }
        }
        Assert.Equal("New.Card", revisions[1].GetSemanticModel(changed).GetAkburaComponentMetadataName(changed));
        Assert.Equal("Old.Card", original.GetSemanticModel(tree).GetAkburaComponentMetadataName(tree));
    }

    [Theory]
    [InlineData("global::Library.Override", "Library.Override")]
    [InlineData("Library.Override", "Library.Override")]
    [InlineData("global::", "")]
    [InlineData(null, "App.Embedded.Card")]
    [InlineData("", "App.Embedded.Card")]
    public void ReferencedDeclarations_PreserveOverridesAndPrefixHandling(string? metadataName, string expected)
    {
        var reference = Reference(metadataName);
        var host = AkburaSyntaxTree.ParseText("", "Host.akbura");
        var compilation = new AkburaCompilation(CSharp().AddReferences(reference), [host], "App");
        var module = Assert.Single(compilation.ReferencedModules);
        var tree = ModuleTree(module);
        var model = compilation.GetSemanticModel(host);
        Assert.Equal(expected, Original(model, tree));
        Assert.Equal(expected, model.GetAkburaComponentMetadataName(tree));
        Assert.Equal(expected, model.GetAkburaComponentMetadataName(tree));
        var replacement = compilation.WithCSharpCompilation(CSharp().AddReferences(Reference("Other.Override")));
        Assert.Empty(Cache(replacement));
        Assert.Equal("App.Embedded.Card", replacement.GetSemanticModel(host).GetAkburaComponentMetadataName(tree));
        var newTree = ModuleTree(Assert.Single(replacement.ReferencedModules));
        Assert.Equal("Other.Override", replacement.GetSemanticModel(host).GetAkburaComponentMetadataName(newTree));
        Assert.Equal(expected, model.GetAkburaComponentMetadataName(tree));
    }

    [Fact]
    public void ParallelRequests_ConvergeOnOneEntryPerIdentity()
    {
        var model = Model();
        var trees = Enumerable.Range(0, 8).Select(index => new CountingTree($"Views/Card{index}.akbura")).ToArray();
        Parallel.For(0, 16_000, index =>
        {
            var tree = trees[index % trees.Length];
            Assert.Equal($"App.Views.Card{index % trees.Length}", model.GetAkburaComponentMetadataName(tree));
        });
        Assert.Equal(trees.Length, Cache(model.Compilation).Count);
        foreach (var tree in trees)
        {
            var reads = tree.NameReads;
            Assert.InRange(reads, 1, 16_000);
            var result = model.GetAkburaComponentMetadataName(tree);
            Assert.Same(result, model.GetAkburaComponentMetadataName(tree));
            Assert.Equal(reads, tree.NameReads);
        }
    }

    [Fact]
    public void Cache_DoesNotKeepDiscardedCompilationAlive()
    {
        var weak = PopulateTemporaryCompilation();
        for (var index = 0; index < 10 && weak.IsAlive; index++)
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        }
        Assert.False(weak.IsAlive);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference PopulateTemporaryCompilation()
    {
        var model = Model();
        Assert.Equal("App.Views.Card", model.GetAkburaComponentMetadataName(new CountingTree("Views/Card.akbura")));
        return new WeakReference(model.Compilation);
    }

    private static AkburaSemanticModel Model(string root = "App", string directory = "")
    {
        var host = AkburaSyntaxTree.ParseText("", "Host.akbura");
        return new AkburaCompilation(CSharp(), [host], root, directory).GetSemanticModel(host);
    }

    private static CSharpCompilation CSharp() => CSharpCompilation.Create("CacheTests",
        references: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
        options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    private static ConcurrentDictionary<AkburaSyntaxTree, string> Cache(AkburaCompilation compilation) =>
        (ConcurrentDictionary<AkburaSyntaxTree, string>)typeof(AkburaCompilation)
            .GetField("_componentMetadataNames", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(compilation)!;

    private static string Original(AkburaSemanticModel model, AkburaSyntaxTree tree) =>
        (string)typeof(AkburaSemanticModel).GetMethod("ComputeAkburaComponentMetadataName",
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(model, [tree])!;

    private MetadataReference Reference(string? metadataName)
    {
        const string path = "Embedded/Card.akbura";
        var declaration = new AkburaModuleDeclaration(DeclarationKind.Component, "Card", metadataName, 0, 0, [],
            component: new AkburaModuleComponent("System.Object", [], []));
        var manifest = new AkburaModuleManifest(AkburaModuleManifest.CurrentFormatVersion, "Library",
            [new AkburaModuleSource(path, AkburaModuleSourceKind.Component, [declaration])]);
        using var data = new MemoryStream();
        AkburaModuleManifestSerializer.Write(data, manifest);
        var bytes = data.ToArray();
        var text = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("param string Title;")).ToArray();
        using var image = new MemoryStream();
        var result = CSharp().Emit(image, manifestResources:
        [
            new ResourceDescription(AkburaModuleManifest.ResourceName, () => new MemoryStream(bytes), true),
            new ResourceDescription(path, () => new MemoryStream(text), true),
        ]);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".dll");
        _referenceFiles.Add(file);
        File.WriteAllBytes(file, image.ToArray());
        return MetadataReference.CreateFromFile(file);
    }

    private static AkburaSyntaxTree ModuleTree(AkburaReferencedModule module)
    {
        var lazy = typeof(AkburaReferencedModule).GetField("_lazyEmbeddedData", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(module)!;
        var data = lazy.GetType().GetProperty("Value")!.GetValue(lazy)!;
        var sources = (IEnumerable<AkburaReferencedSource>)data.GetType().GetProperty("Sources")!.GetValue(data)!;
        return Assert.Single(sources).GetSyntaxTree();
    }

    private sealed class CountingTree(string path, string source = "") : AkburaSyntaxTree(SourceText.From(source), path)
    {
        private readonly ComponentSyntaxTree _tree = AkburaSyntaxTree.ParseText(source, path);
        private int _nameReads;
        public int NameReads => Volatile.Read(ref _nameReads);
        public override SyntaxTreeKind Kind => SyntaxTreeKind.Component;
        public override string ComponentName { get { Interlocked.Increment(ref _nameReads); return _tree.ComponentName; } }
        public override AkburaDocumentSyntax GetRoot() => _tree.GetRoot();
        public override AkburaSyntax GetRootSyntax() => _tree.GetRootSyntax();
        public override bool Equals(object? obj) => obj is CountingTree;
        public override int GetHashCode() => 0;
    }
}
