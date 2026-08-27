using Akbura.LanguageServer.Projects;

namespace Akbura.LanguageServer.UnitTests;

public sealed class AkburaProjectDiscoveryTests
{
    [Fact]
    public void DiscoverFindsSingleNestedProject()
    {
        using var fixture = new DiscoveryFixture();
        var projectPath = fixture.CreateFile(
            Path.Combine("src", "App", "App.csproj"));

        var result = AkburaProjectDiscovery.Discover(
            fixture.RootUri,
            explicitPath: null,
            out var warning);

        Assert.Equal(projectPath, result);
        Assert.Null(warning);
    }

    [Fact]
    public void DiscoverIgnoresGeneratedAndToolDirectories()
    {
        using var fixture = new DiscoveryFixture();
        fixture.CreateFile(Path.Combine("src", "App", "obj", "Copy.csproj"));
        fixture.CreateFile(Path.Combine("node_modules", "Tool.csproj"));
        fixture.CreateFile(Path.Combine(".git", "Worktree.csproj"));
        var projectPath = fixture.CreateFile(
            Path.Combine("src", "App", "App.csproj"));

        var result = AkburaProjectDiscovery.Discover(
            fixture.RootUri,
            explicitPath: null,
            out var warning);

        Assert.Equal(projectPath, result);
        Assert.Null(warning);
    }

    [Fact]
    public void DiscoverRequiresSelectionForMultipleNestedProjects()
    {
        using var fixture = new DiscoveryFixture();
        fixture.CreateFile(Path.Combine("src", "First", "First.csproj"));
        fixture.CreateFile(Path.Combine("src", "Second", "Second.csproj"));

        var result = AkburaProjectDiscovery.Discover(
            fixture.RootUri,
            explicitPath: null,
            out var warning);

        Assert.Null(result);
        Assert.Contains("Multiple solutions or projects", warning);
        Assert.Contains("under", warning);
    }

    [Fact]
    public void DiscoverPrefersTopLevelSolutionOverNestedProjects()
    {
        using var fixture = new DiscoveryFixture();
        var solutionPath = fixture.CreateFile("Workspace.slnx");
        fixture.CreateFile(Path.Combine("src", "App", "App.csproj"));

        var result = AkburaProjectDiscovery.Discover(
            fixture.RootUri,
            explicitPath: null,
            out var warning);

        Assert.Equal(solutionPath, result);
        Assert.Null(warning);
    }

    private sealed class DiscoveryFixture : IDisposable
    {
        private readonly DirectoryInfo _root =
            Directory.CreateTempSubdirectory("akbura-discovery-tests-");

        public Uri RootUri => new(_root.FullName);

        public string CreateFile(string relativePath)
        {
            var path = Path.GetFullPath(
                Path.Combine(_root.FullName, relativePath));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, string.Empty);
            return path;
        }

        public void Dispose()
        {
            _root.Delete(recursive: true);
        }
    }
}
