using Akbura.Workspaces.Projects;
using Akbura.Workspaces.Resources;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Akbura.Workspaces.UnitTests;

public sealed class ResourceDocumentLifecycleTests
{
    [Fact]
    public void Synchronize_ReusesUnchangedFactsAndRemovesDeletedDocuments()
    {
        using var workspace = new AkburaWorkspace();
        var projectId = workspace.DefaultProjectId;
        var assembly = ResourceAssemblyIdentity.Create(
            workspace.CurrentSolution
                .GetRequiredProject(projectId)
                .CSharpCompilation
                .Assembly
                .Identity);
        var first = Input(
            assembly,
            "App.axaml",
            Application("<x:String x:Key=\"First\">A</x:String>"));
        var second = Input(
            assembly,
            "Resources/Theme.axaml",
            Dictionary("<x:String x:Key=\"Second\">B</x:String>"));

        var original = workspace.SynchronizeProjectResourceDocuments(
            projectId,
            [first, second]);
        var originalFirst =
            original.ResourceDocuments[first.DictionaryIdentity];
        var originalSecond =
            original.ResourceDocuments[second.DictionaryIdentity];

        var unchanged = workspace.SynchronizeProjectResourceDocuments(
            projectId,
            [first, second]);

        Assert.Same(original, unchanged);

        var changedFirst = Input(
            assembly,
            "App.axaml",
            Application("<x:String x:Key=\"Changed\">C</x:String>"));
        var changed = workspace.SynchronizeProjectResourceDocuments(
            projectId,
            [changedFirst, second]);
        var currentFirst =
            changed.ResourceDocuments[changedFirst.DictionaryIdentity];
        var currentSecond =
            changed.ResourceDocuments[second.DictionaryIdentity];

        Assert.NotSame(originalFirst.SyntaxTree, currentFirst.SyntaxTree);
        Assert.NotSame(originalFirst.Facts, currentFirst.Facts);
        Assert.Same(originalSecond, currentSecond);
        Assert.Contains(
            currentFirst.Facts.Declarations,
            static declaration => declaration.Key == "Changed");
        Assert.DoesNotContain(
            currentFirst.Facts.Declarations,
            static declaration => declaration.Key == "First");

        var afterDelete = workspace.SynchronizeProjectResourceDocuments(
            projectId,
            [changedFirst]);

        Assert.Single(afterDelete.ResourceDocuments);
        Assert.DoesNotContain(
            second.DictionaryIdentity,
            afterDelete.ResourceDocuments.Keys);
    }

    [Fact]
    public void OpenChange_RenameAndRemoveUpdateResourceIdentity()
    {
        using var workspace = new AkburaWorkspace();
        var projectId = workspace.DefaultProjectId;
        var assembly = ResourceAssemblyIdentity.Create(
            workspace.CurrentSolution
                .GetRequiredProject(projectId)
                .CSharpCompilation
                .Assembly
                .Identity);
        var original = Input(
            assembly,
            "Resources/Old.axaml",
            Dictionary("<x:String x:Key=\"Old\">A</x:String>"));
        workspace.OpenOrChangeResourceDocument(
            projectId,
            original);

        var renamed = new ResourceDocumentInput(
            original.Uri,
            original.PhysicalPath,
            "Resources/New.axaml",
            assembly,
            SourceText.From(
                Dictionary(
                    "<x:String x:Key=\"New\">B</x:String>")),
            VersionStamp.Create(),
            "project",
            "net10.0");
        workspace.OpenOrChangeResourceDocument(
            projectId,
            renamed);

        var project = workspace.CurrentSolution.GetRequiredProject(
            projectId);
        Assert.DoesNotContain(
            original.DictionaryIdentity,
            project.ResourceDocuments.Keys);
        Assert.Contains(
            renamed.DictionaryIdentity,
            project.ResourceDocuments.Keys);

        workspace.RemoveResourceDocument(projectId, renamed.Uri);

        Assert.Empty(
            workspace.CurrentSolution
                .GetRequiredProject(projectId)
                .ResourceDocuments);
    }

    [Fact]
    public void GuardedRestore_DoesNotOverwriteNewerOpenBufferText()
    {
        using var workspace = new AkburaWorkspace();
        var projectId = workspace.DefaultProjectId;
        var assembly = ResourceAssemblyIdentity.Create(
            workspace.CurrentSolution
                .GetRequiredProject(projectId)
                .CSharpCompilation
                .Assembly
                .Identity);
        var persisted = Input(
            assembly,
            "App.axaml",
            Application(
                "<x:String x:Key=\"Disk\">A</x:String>"));
        var stale = persisted.WithText(
            SourceText.From(Application(
                "<x:String x:Key=\"Stale\">B</x:String>")),
            VersionStamp.Create());
        var current = persisted.WithText(
            SourceText.From(Application(
                "<x:String x:Key=\"Current\">C</x:String>")),
            VersionStamp.Create());

        workspace.OpenOrChangeResourceDocument(projectId, stale);
        workspace.OpenOrChangeResourceDocument(projectId, current);

        Assert.False(workspace.TryRestoreResourceDocument(
            projectId,
            persisted.Uri,
            stale.Text,
            persisted));
        Assert.Contains(
            Assert.Single(workspace.CurrentSolution
                .GetRequiredProject(projectId)
                .ResourceDocuments.Values)
                .Facts.Declarations,
            static declaration => declaration.Key == "Current");

        Assert.True(workspace.TryRestoreResourceDocument(
            projectId,
            persisted.Uri,
            current.Text,
            persisted));
        Assert.Contains(
            Assert.Single(workspace.CurrentSolution
                .GetRequiredProject(projectId)
                .ResourceDocuments.Values)
                .Facts.Declarations,
            static declaration => declaration.Key == "Disk");
    }

    [Fact]
    public async Task RoslynLoader_UsesOnlyAxamlAdditionalDocumentsAndLinkPath()
    {
        using var roslynWorkspace = new AdhocWorkspace();
        var projectDirectory = Path.Combine(
            Path.GetTempPath(),
            nameof(ResourceDocumentLifecycleTests),
            Guid.NewGuid().ToString("N"));
        var projectPath = Path.Combine(projectDirectory, "App.csproj");
        var project = roslynWorkspace.AddProject(
            ProjectInfo.Create(
                ProjectId.CreateNewId(),
                VersionStamp.Create(),
                "App",
                "App",
                LanguageNames.CSharp,
                filePath: projectPath));
        var linkedPath = Path.Combine(
            projectDirectory,
            "..",
            "Shared",
            "Theme.axaml");
        project = project
            .AddAdditionalDocument(
                "Theme.axaml",
                SourceText.From(Dictionary(
                    "<x:String x:Key=\"Disk\">A</x:String>")),
                folders: ["Linked"],
                filePath: linkedPath)
            .Project;
        project = project
            .AddAdditionalDocument(
                "Notes.txt",
                SourceText.From("not a resource"),
                filePath: Path.Combine(projectDirectory, "Notes.txt"))
            .Project;
        var compilation = CSharpCompilation.Create("App");
        var openText = SourceText.From(Dictionary(
            "<x:String x:Key=\"OpenBuffer\">B</x:String>"));

        var resources = await new RoslynResourceDocumentLoader()
            .LoadAsync(
                project,
                compilation,
                uri => string.Equals(
                        uri.LocalPath,
                        Path.GetFullPath(linkedPath),
                        StringComparison.OrdinalIgnoreCase)
                    ? openText
                    : null,
                CancellationToken.None);

        var resource = Assert.Single(resources);
        Assert.Equal("Linked/Theme.axaml", resource.LogicalPath.Value);
        Assert.Same(openText, resource.Text);
    }

    [Fact]
    public async Task RoslynLoader_UsesRootLevelLinkForExternalResource()
    {
        using var roslynWorkspace = new AdhocWorkspace();
        var projectDirectory = Path.Combine(
            Path.GetTempPath(),
            nameof(ResourceDocumentLifecycleTests),
            Guid.NewGuid().ToString("N"),
            "Project");
        var project = roslynWorkspace.AddProject(
            ProjectInfo.Create(
                ProjectId.CreateNewId(),
                VersionStamp.Create(),
                "App",
                "App",
                LanguageNames.CSharp,
                filePath: Path.Combine(projectDirectory, "App.csproj")));
        var linkedPath = Path.Combine(
            Path.GetDirectoryName(projectDirectory)!,
            "Shared",
            "Theme.axaml");
        project = project
            .AddAdditionalDocument(
                "Theme.axaml",
                SourceText.From(Dictionary(
                    "<x:String x:Key=\"Linked\">A</x:String>")),
                folders: [],
                filePath: linkedPath)
            .Project;

        var resources = await new RoslynResourceDocumentLoader()
            .LoadAsync(
                project,
                CSharpCompilation.Create("App"),
                openTextProvider: null,
                CancellationToken.None);

        var resource = Assert.Single(resources);
        Assert.Equal("Theme.axaml", resource.LogicalPath.Value);
        Assert.Equal(
            Path.GetFullPath(linkedPath),
            resource.PhysicalPath);
    }

    [Fact]
    public async Task RoslynLoader_DoesNotScanUnlistedAxamlFiles()
    {
        var projectDirectory = Path.Combine(
            Path.GetTempPath(),
            nameof(ResourceDocumentLifecycleTests),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(projectDirectory);

        try
        {
            var excludedPath = Path.Combine(
                projectDirectory,
                "Excluded.axaml");
            File.WriteAllText(
                excludedPath,
                Dictionary(
                    "<x:String x:Key=\"Excluded\">B</x:String>"));
            using var roslynWorkspace = new AdhocWorkspace();
            var project = roslynWorkspace.AddProject(
                ProjectInfo.Create(
                    ProjectId.CreateNewId(),
                    VersionStamp.Create(),
                    "App",
                    "App",
                    LanguageNames.CSharp,
                    filePath: Path.Combine(
                        projectDirectory,
                        "App.csproj")));
            project = project
                .AddAdditionalDocument(
                    "Included.axaml",
                    SourceText.From(Dictionary(
                        "<x:String x:Key=\"Included\">A</x:String>")),
                    filePath: Path.Combine(
                        projectDirectory,
                        "Included.axaml"))
                .Project;

            var resources = await new RoslynResourceDocumentLoader()
                .LoadAsync(
                    project,
                    CSharpCompilation.Create("App"),
                    openTextProvider: null,
                    CancellationToken.None);

            Assert.True(File.Exists(excludedPath));
            var resource = Assert.Single(resources);
            Assert.Equal("Included.axaml", resource.LogicalPath.Value);
            Assert.DoesNotContain(
                resources,
                static candidate =>
                    candidate.LogicalPath.Value == "Excluded.axaml");
        }
        finally
        {
            Directory.Delete(projectDirectory, recursive: true);
        }
    }

    [Fact]
    public void SamePhysicalResource_RemainsIsolatedPerProjectAndTargetFramework()
    {
        using var workspace = new AkburaWorkspace();
        var uri = new Uri(Path.Combine(
            Path.GetTempPath(),
            nameof(ResourceDocumentLifecycleTests),
            Guid.NewGuid().ToString("N"),
            "Shared.axaml"));
        var first = AddProject(workspace, "First", "net9.0");
        var second = AddProject(workspace, "Second", "net10.0");
        var firstInput = new ResourceDocumentInput(
            uri,
            uri.LocalPath,
            "Shared.axaml",
            ResourceAssemblyIdentity.Create(
                first.CSharpCompilation.Assembly.Identity),
            SourceText.From(Dictionary(
                "<x:String x:Key=\"FirstKey\">A</x:String>")),
            VersionStamp.Create(),
            first.Id.Value.ToString("N"),
            "net9.0");
        var secondInput = new ResourceDocumentInput(
            uri,
            uri.LocalPath,
            "Shared.axaml",
            ResourceAssemblyIdentity.Create(
                second.CSharpCompilation.Assembly.Identity),
            SourceText.From(Dictionary(
                "<x:String x:Key=\"SecondKey\">B</x:String>")),
            VersionStamp.Create(),
            second.Id.Value.ToString("N"),
            "net10.0");

        workspace.SynchronizeProjectResourceDocuments(
            first.Id,
            [firstInput]);
        workspace.SynchronizeProjectResourceDocuments(
            second.Id,
            [secondInput]);

        var solution = workspace.CurrentSolution;
        var firstResource = Assert.Single(
            solution.GetRequiredProject(first.Id).ResourceDocuments.Values);
        var secondResource = Assert.Single(
            solution.GetRequiredProject(second.Id).ResourceDocuments.Values);
        Assert.Equal("net9.0", firstResource.Input.TargetFramework);
        Assert.Equal("net10.0", secondResource.Input.TargetFramework);
        Assert.Contains(
            firstResource.Facts.Declarations,
            static declaration => declaration.Key == "FirstKey");
        Assert.DoesNotContain(
            firstResource.Facts.Declarations,
            static declaration => declaration.Key == "SecondKey");
        Assert.Contains(
            secondResource.Facts.Declarations,
            static declaration => declaration.Key == "SecondKey");
        Assert.DoesNotContain(
            secondResource.Facts.Declarations,
            static declaration => declaration.Key == "FirstKey");

        workspace.RemoveResourceDocument(first.Id, uri);

        Assert.Empty(workspace.CurrentSolution
            .GetRequiredProject(first.Id)
            .ResourceDocuments);
        Assert.Single(workspace.CurrentSolution
            .GetRequiredProject(second.Id)
            .ResourceDocuments);
    }

    private static ResourceDocumentInput Input(ResourceAssemblyIdentity assembly, string logicalPath, string text)
    {
        var physicalPath = Path.Combine(
            Path.GetTempPath(),
            "AkburaResourceTests",
            logicalPath.Replace(
                '/',
                Path.DirectorySeparatorChar));
        return new ResourceDocumentInput(
            new Uri(physicalPath),
            physicalPath,
            logicalPath,
            assembly,
            SourceText.From(text),
            VersionStamp.Create(),
            "project",
            "net10.0");
    }

    private static AkburaProjectSnapshot AddProject(AkburaWorkspace workspace, string assemblyName, string targetFramework)
    {
        var roslynProjectId = ProjectId.CreateNewId(assemblyName);
        var projectDirectory = Path.Combine(
            Path.GetTempPath(),
            nameof(ResourceDocumentLifecycleTests),
            assemblyName,
            targetFramework);
        var context = new ProjectContext(
            roslynProjectId,
            Path.Combine(projectDirectory, assemblyName + ".csproj"),
            projectDirectory,
            assemblyName,
            CSharpCompilation.Create(
                assemblyName,
                options: new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary)),
            []);
        return workspace.AddOrUpdateProject(context);
    }

    private static string Application(string resources)
    {
        return "<Application " +
            "xmlns=\"https://github.com/avaloniaui\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
            "<Application.Resources><ResourceDictionary>" +
            resources +
            "</ResourceDictionary></Application.Resources>" +
            "</Application>";
    }

    private static string Dictionary(string resources)
    {
        return "<ResourceDictionary " +
            "xmlns=\"https://github.com/avaloniaui\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
            resources +
            "</ResourceDictionary>";
    }
}
