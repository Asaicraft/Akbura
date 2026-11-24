using Akbura.Diagnostics;
using Akbura.Workspaces.Projects;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using System.Reflection;

namespace Akbura.Workspaces.UnitTests;

public sealed class ProjectConfigurationTests
{
    private static readonly Lazy<AkburaReferenceSet> Catalog = new(() => AkburaReferenceSet.FromFiles(
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)));

    [Fact]
    public void FacadeKeepsIdentityAndReadsExternalUpdates()
    {
        using var workspace = new AkburaWorkspace();
        var project = workspace.DefaultProject;
        Assert.Same(project, workspace.GetProject(project.Id));
        var original = project.Context;
        project.AddSource("First.cs", "public class First {}");
        Assert.Equal(original.RoslynProjectId, project.Context.RoslynProjectId);
        Assert.Equal(original.CSharpCompilation.AssemblyName, project.Context.CSharpCompilation.AssemblyName);
        var external = new ProjectContext(original.RoslynProjectId, "Project.csproj", "Project", "Changed",
            project.Context.CSharpCompilation, original.ProjectReferences, AkburaDiagnosticPublisher.Workspace);
        workspace.AddOrUpdateProject(external);
        Assert.Same(external, project.Context);
        project.AddSource("Second.cs", "public class Second {}");
        Assert.Equal("Changed", project.Context.RootNamespace);
        workspace.RemoveProject(project.Id);
        Assert.Throws<InvalidOperationException>(() => project.AddSource("Third.cs", ""));
        workspace.AddOrUpdateProject(original);
        Assert.NotSame(project, workspace.GetProject(project.Id));
        Assert.Throws<InvalidOperationException>(() => _ = project.Context);
        var replacement = workspace.DefaultProject;
        workspace.Dispose();
        Assert.Throws<ObjectDisposedException>(() => replacement.AddAssemblyOf<int>());
        Assert.Throws<ObjectDisposedException>(() => workspace.GetProject(project.Id));
    }

    [Fact]
    public void SourcesNormalizePathsPreserveOptionsAndSupportPartialTypes()
    {
        var options = new CSharpParseOptions(LanguageVersion.CSharp12, preprocessorSymbols: ["FEATURE"]);
        var first = CSharpSyntaxTree.ParseText("public partial class Person {}", options, "Existing.cs");
        var compilation = CSharpCompilation.Create("Preserved", [first],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, nullableContextOptions: NullableContextOptions.Enable));
        var original = new ProjectContext(ProjectId.CreateNewId(), "App.csproj", "Root", "Demo", compilation, [], AkburaDiagnosticPublisher.Both);
        using var workspace = new AkburaWorkspace(original);
        var project = workspace.DefaultProject;
        var uri = new Uri("memory:///View.akbura");
        workspace.OpenOrChangeDocument(uri, SourceText.From("param Person Value;"));
        project.AddSource("Models/Other/../Person.cs", "#if FEATURE\npublic partial class Person { public Address Address { get; set; } }\n#endif");
        project.AddSource("Models\\Address.cs", "public class Address { public string Street { get; set; } }");
        Assert.Equal(2, project.Context.CSharpCompilation.GetTypeByMetadataName("Person")!.DeclaringSyntaxReferences.Length);
        Assert.NotEmpty(project.Context.CSharpCompilation.GetTypeByMetadataName("Person")!.GetMembers("Address"));
        Assert.Same(first, project.Context.CSharpCompilation.SyntaxTrees.First());
        var person = project.Context.CSharpCompilation.SyntaxTrees.Single(tree => tree.FilePath == "Models/Person.cs");
        Assert.Equal(options, person.Options);
        Assert.Throws<InvalidOperationException>(() => project.AddSource("Models/./Person.cs", ""));
        Assert.Throws<KeyNotFoundException>(() => project.UpdateSource("Missing.cs", ""));
        var before = workspace.CurrentSolution;
        project.UpdateSource("Models\\Person.cs", person.GetText().ToString());
        Assert.Same(before, workspace.CurrentSolution);
        project.UpdateSource("Models/Person.cs", "public partial class Person { public int Incomplete =>");
        Assert.True(project.Context.CSharpCompilation.SyntaxTrees.Single(tree => tree.FilePath == "Models/Person.cs").GetDiagnostics().Any());
        Assert.Same(first, project.Context.CSharpCompilation.SyntaxTrees.First());
        Assert.True(project.RemoveSource("Models/./Address.cs"));
        Assert.False(project.RemoveSource("Models/Address.cs"));
        Assert.Same(compilation.Options, project.Context.CSharpCompilation.Options);
        Assert.Equal(original.ProjectFilePath, project.Context.ProjectFilePath);
        Assert.Equal(original.ProjectDirectory, project.Context.ProjectDirectory);
        Assert.Equal(original.RootNamespace, project.Context.RootNamespace);
        Assert.Equal(original.DiagnosticPublisher, project.Context.DiagnosticPublisher);
        Assert.Equal("param Person Value;", workspace.CurrentSolution.GetRequiredProject(project.Id).Documents.Values.Single().Text.ToString());
    }

    [Theory]
    [InlineData("../Outside.cs")]
    [InlineData("Models/../../Outside.cs")]
    [InlineData("/Root.cs")]
    [InlineData("C:\\Root.cs")]
    [InlineData("memory:///Root.cs")]
    [InlineData("View.akbura")]
    [InlineData("")]
    public void InvalidVirtualPathsFailWithoutPublication(string path)
    {
        using var workspace = new AkburaWorkspace();
        var before = workspace.CurrentSolution;
        Assert.Throws<ArgumentException>(() => workspace.DefaultProject.AddSource(path, ""));
        Assert.Same(before, workspace.CurrentSolution);
    }

    [Fact]
    public void TreeLessProjectsUseTheDocumentedParsePolicy()
    {
        using var workspace = new AkburaWorkspace();
        workspace.DefaultProject.AddSource("New.cs", "public class New {}");
        var options = (CSharpParseOptions)workspace.DefaultProject.Context.CSharpCompilation.SyntaxTrees.Single().Options;
        Assert.Equal(LanguageVersion.CSharp14, options.LanguageVersion);
        Assert.Empty(options.PreprocessorSymbolNames);
    }

    [Fact]
    public void EquivalentAssemblyConveniencesAreIdempotentAndAllowValueAndInterfaceTypes()
    {
        using var workspace = new AkburaWorkspace();
        var project = workspace.DefaultProject;
        project.ReferenceResolver = Catalog.Value;
        project.AddAssemblyOf<int>();
        var before = workspace.CurrentSolution;
        project.AddAssembly(typeof(int).Assembly);
        project.AddAssemblyOf<IEnumerable<int>>();
        Assert.Same(before, workspace.CurrentSolution);
        Assert.NotEqual(TypeKind.Error, project.Context.CSharpCompilation.GetSpecialType(SpecialType.System_String).TypeKind);
        var sentinelImage = Emit("Initializer_" + Guid.NewGuid().ToString("N"),
            "public sealed class NeverInitialize { static NeverInitialize() { throw new System.Exception(\"Must not execute\"); } }");
        var sentinelAssembly = Assembly.Load(sentinelImage);
        project.Update(update =>
        {
            update.ReferenceResolver = AkburaReferenceSet.FromImages([sentinelImage]);
            update.AddAssembly(sentinelAssembly);
        });
        Assert.NotNull(project.Context.CSharpCompilation.GetTypeByMetadataName("NeverInitialize"));
    }

    [Fact]
    public void ImagesAreCopiedAndNeverLoadedAsExecutableAssemblies()
    {
        var name = "Image_" + Guid.NewGuid().ToString("N");
        var image = Emit(name, "public class FromImage { public int Value { get; set; } }");
        using var workspace = CoreWorkspace();
        workspace.DefaultProject.AddAssemblyImage(image);
        Array.Clear(image);
        Assert.NotNull(workspace.DefaultProject.Context.CSharpCompilation.GetTypeByMetadataName("FromImage"));
        Assert.DoesNotContain(AppDomain.CurrentDomain.GetAssemblies(), assembly => assembly.GetName().Name == name);
        var snapshot = workspace.CurrentSolution;
        workspace.Dispose();
        Assert.NotNull(snapshot.GetRequiredProject(workspace.DefaultProjectId).CSharpCompilation.GetTypeByMetadataName("FromImage"));
    }

    [Fact]
    public void PreparedImagesResolveALocationlessAssemblyAndAreImmutable()
    {
        var image = Emit("Locationless_" + Guid.NewGuid().ToString("N"), "public class InMemory {}");
        var loaded = Assembly.Load(image);
        Assert.Empty(loaded.Location);
        var catalog = AkburaReferenceSet.FromImages([image, File.ReadAllBytes(typeof(object).Assembly.Location)]);
        Array.Clear(image);
        using var workspace = new AkburaWorkspace();
        workspace.DefaultProject.Update(update =>
        {
            update.ReferenceResolver = catalog;
            update.AddAssembly(loaded);
        });
        Assert.NotNull(workspace.DefaultProject.Context.CSharpCompilation.GetTypeByMetadataName("InMemory"));
    }

    [Fact]
    public void MissingDependenciesAndInvalidImagesHaveActionableErrorsAndRollBack()
    {
        var dependency = Emit("NeededDependency", "public class Needed {}");
        var library = Emit("RequestingLibrary", "public class Consumer : Needed {}", MetadataReference.CreateFromImage(dependency));
        using var workspace = CoreWorkspace();
        var before = workspace.CurrentSolution;
        var missing = Assert.Throws<InvalidOperationException>(() => workspace.DefaultProject.AddAssemblyImage(library));
        Assert.Contains("NeededDependency", missing.Message);
        Assert.Contains("RequestingLibrary", missing.Message);
        Assert.Same(before, workspace.CurrentSolution);
        Assert.Throws<ArgumentException>(() => workspace.DefaultProject.AddAssemblyImage([1, 2, 3]));
        Assert.Same(before, workspace.CurrentSolution);
    }

    [Fact]
    public void BatchesPublishOnceResolveDependenciesInAnyOrderAndRollBackOnFailure()
    {
        var dependency = Emit("BatchDependency", "public class Needed {}");
        var library = Emit("BatchLibrary", "public class Consumer : Needed {}", MetadataReference.CreateFromImage(dependency));
        using var workspace = new AkburaWorkspace();
        var project = workspace.DefaultProject;
        var publications = 0;
        workspace.Changed += (_, _) => publications++;
        project.Update(update =>
        {
            update.AddAssemblyImage(library);
            update.AddSource("ViewModel.cs", "public class ViewModel : Consumer {}");
            update.AddAssemblyImage(dependency);
            update.AddAssemblyOf<int>();
        });
        Assert.Equal(1, publications);
        Assert.DoesNotContain(project.Context.CSharpCompilation.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var before = workspace.CurrentSolution;
        Assert.Throws<InvalidOperationException>(() => project.Update(update =>
        {
            update.AddSource("Discarded.cs", "public class Discarded {}");
            throw new InvalidOperationException("Callback failed");
        }));
        Assert.Same(before, workspace.CurrentSolution);
        Assert.Throws<InvalidOperationException>(() => project.Update(update =>
        {
            update.AddSource("Discarded.cs", "public class Discarded {}");
            project.AddSource("Nested.cs", "");
        }));
        Assert.Same(before, workspace.CurrentSolution);
        AkburaProjectUpdate? escaped = null;
        project.Update(update => escaped = update);
        Assert.Throws<InvalidOperationException>(() => escaped!.AddSource("Late.cs", ""));
    }

    [Fact]
    public void DuplicatesKeepAliasesButVersionsImagesAndSelfReferencesConflict()
    {
        var image = Emit("AliasedLibrary", "public class First {}");
        var otherImage = Emit("AliasedLibrary", "public class Second {}");
        var otherVersion = Emit("AliasedLibrary", "[assembly:System.Reflection.AssemblyVersion(\"2.0.0.0\")] public class First {}");
        using var workspace = CoreWorkspace();
        var project = workspace.DefaultProject;
        var first = MetadataReference.CreateFromImage(image).WithAliases(["FirstAlias"]);
        project.AddReference(first);
        var before = workspace.CurrentSolution;
        project.AddReference(MetadataReference.CreateFromImage(image).WithAliases(["FirstAlias"]));
        Assert.Same(before, workspace.CurrentSolution);
        project.AddReference(first.WithAliases(["SecondAlias"]));
        Assert.Contains(project.Context.CSharpCompilation.References, reference => reference.Properties.Aliases.Contains("FirstAlias"));
        Assert.Contains(project.Context.CSharpCompilation.References, reference => reference.Properties.Aliases.Contains("SecondAlias"));
        before = workspace.CurrentSolution;
        Assert.Throws<InvalidOperationException>(() => project.AddAssemblyImage(otherImage));
        Assert.Throws<InvalidOperationException>(() => project.AddAssemblyImage(otherVersion));
        Assert.Same(before, workspace.CurrentSolution);
        var self = Emit(project.Context.CSharpCompilation.AssemblyName!, "public class Self {}");
        Assert.Contains("self-reference", Assert.Throws<InvalidOperationException>(() => project.AddAssemblyImage(self)).Message);
        Assert.Same(before, workspace.CurrentSolution);
        Assert.Throws<InvalidOperationException>(() => AkburaReferenceSet.FromImages([image, otherImage]));
    }

    [Fact]
    public void CallerMetadataIsNotDisposedByWorkspaceOperations()
    {
        var image = Emit("CallerOwned", "public class Owned {}");
        using var metadata = AssemblyMetadata.CreateFromImage(image);
        using var workspace = CoreWorkspace();
        workspace.DefaultProject.AddReference(metadata.GetReference());
        workspace.Dispose();
        Assert.True(metadata.GetModules()[0].GetMetadataReader().IsAssembly);
    }

    [Fact]
    public async Task StagedConfigurationPreservesConcurrentTextAndProjectChanges()
    {
        using var workspace = new AkburaWorkspace();
        var project = workspace.DefaultProject;
        var uri = new Uri("memory:///Live.akbura");
        workspace.OpenOrChangeDocument(uri, SourceText.From("state int Count = 1;"));
        using var staged = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var configuration = Task.Run(() => project.Update(update =>
        {
            update.AddSource("Staged.cs", "public class Staged {}");
            staged.Set();
            Assert.True(release.Wait(TimeSpan.FromSeconds(30)));
        }));
        Assert.True(staged.Wait(TimeSpan.FromSeconds(30)));
        workspace.OpenOrChangeDocument(uri, SourceText.From("state int Count = 999;"));
        project.AddSource("Concurrent.cs", "public class Concurrent {}");
        release.Set();
        await configuration;
        Assert.Equal(2, project.Context.CSharpCompilation.SyntaxTrees.Count());
        Assert.Equal("state int Count = 999;", workspace.CurrentSolution.GetRequiredProject(project.Id).Documents.Values.Single().Text.ToString());
    }

    private static AkburaWorkspace CoreWorkspace()
    {
        var workspace = new AkburaWorkspace();
        workspace.DefaultProject.AddAssemblyOf<int>();
        return workspace;
    }

    [Fact]
    public void MultipleSourcesKeepInheritanceInterfacesAndProjectLinks()
    {
        using var workspace = new AkburaWorkspace();
        var project = workspace.DefaultProject;
        var other = ProjectContext.CreateSyntaxOnly("OtherProject");
        workspace.AddOrUpdateProject(other);
        var original = project.Context;
        var links = ImmutableArray.Create(new ProjectReference(other.RoslynProjectId));
        workspace.AddOrUpdateProject(new ProjectContext(original.RoslynProjectId, "App.csproj", "Root", "Demo",
            original.CSharpCompilation, links, AkburaDiagnosticPublisher.None));
        project.Update(update =>
        {
            update.AddAssemblyOf<int>();
            update.AddSource("IName.cs", "public interface IName { string Name { get; } }");
            update.AddSource("Base.cs", "public class Base { public int Inherited { get; set; } }");
            update.AddSource("Model.cs", "public partial class Model : Base, IName { public string Name => \"\"; }");
            update.AddSource("OtherPart.cs", "public partial class Model { public int Read() => Inherited; }");
        });
        Assert.Equal(links, project.Context.ProjectReferences);
        Assert.Equal(AkburaDiagnosticPublisher.None, project.Context.DiagnosticPublisher);
        var model = project.Context.CSharpCompilation.GetTypeByMetadataName("Model")!;
        Assert.Equal("Base", model.BaseType!.Name);
        Assert.Equal("IName", Assert.Single(model.Interfaces).Name);
        Assert.Equal(2, model.DeclaringSyntaxReferences.Length);
        Assert.DoesNotContain(project.Context.CSharpCompilation.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ValidationFailureRollsBackSourcesAndAssemblyRegistration()
    {
        using var workspace = CoreWorkspace();
        var project = workspace.DefaultProject;
        project.AddAssemblyImage(Emit("ExistingLibrary", "public class Existing {}"));
        var before = workspace.CurrentSolution;
        Assert.Throws<InvalidOperationException>(() => project.Update(update =>
        {
            update.AddSource("Discarded.cs", "public class Discarded {}");
            update.AddAssemblyImage(Emit("ExistingLibrary", "public class Different {}"));
        }));
        Assert.Same(before, workspace.CurrentSolution);
        Assert.Empty(project.Context.CSharpCompilation.SyntaxTrees);
    }

    [Fact]
    public void AddingALoadedAssemblyDoesNotTreatAliasOnlyReferencesAsGlobal()
    {
        var image = Emit("AliasRoot_" + Guid.NewGuid().ToString("N"), "public class AliasType {}");
        var loaded = Assembly.Load(image);
        using var workspace = CoreWorkspace();
        var project = workspace.DefaultProject;
        project.AddReference(MetadataReference.CreateFromImage(image).WithAliases(["OnlyAlias"]));
        project.AddAssembly(loaded);
        Assert.NotNull(project.Context.CSharpCompilation.GetTypeByMetadataName("AliasType"));
        Assert.Contains(project.Context.CSharpCompilation.References, reference => reference.Properties.Aliases.Contains("OnlyAlias"));
        var before = workspace.CurrentSolution;
        project.AddAssembly(loaded);
        Assert.Same(before, workspace.CurrentSolution);
    }

    private static byte[] Emit(string name, string source, params MetadataReference[] references)
    {
        var compilation = CSharpCompilation.Create(name, [CSharpSyntaxTree.ParseText(source)],
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) }.Concat(references),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        return stream.ToArray();
    }

}
