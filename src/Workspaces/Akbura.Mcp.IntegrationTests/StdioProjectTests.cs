using Akbura.Workspaces;
using Akbura.Workspaces.MSBuild;
using Akbura.Workspaces.Utilities;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace Akbura.Mcp.IntegrationTests;

/// <summary>
/// Exercises the MCP tools over stdio with an evaluated project and saved source files.
/// </summary>
public sealed partial class StdioProjectTests
{
    private const string ViewSource = "using Akbura;\r\nusing Avalonia.Controls;\r\nusing Demo.Helpers.akcss;\r\n<Row />";

    [Fact]
    public async Task ClientDiscoversFourToolsAndObservesSavedChangesOverStdio()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var cancellationToken = timeout.Token;
        using var fixture = new ProjectFixture();
        await fixture.BuildAsync(cancellationToken);

        var startInfo = CreateProcess(fixture.DirectoryPath);
        startInfo.RedirectStandardInput = true;
        foreach (var argument in new[] { fixture.ServerPath, "--project", fixture.ProjectPath, "--configuration", "Debug", "--target-framework", "net10.0" })
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var server = Process.Start(startInfo)!;
        var stderr = server.StandardError.ReadToEndAsync(cancellationToken);
        var protocol = new Pipe();
        var stdout = ForwardProtocolAsync(server.StandardOutput, protocol.Writer, cancellationToken);
        try
        {
            await using var client = await McpClient.CreateAsync(
                new StreamClientTransport(server.StandardInput.BaseStream, protocol.Reader.AsStream()),
                cancellationToken: cancellationToken);
            var tools = await client.ListToolsAsync(cancellationToken: cancellationToken);
            Assert.Equal(["akbura_analyze_document", "akbura_find_utilities", "akbura_get_layout_recipe", "akbura_project_info"],
                tools.Select(tool => tool.Name).Order());
            Assert.All(tools, tool => Assert.NotNull(tool.ProtocolTool.OutputSchema));
            var find = Assert.Single(tools, tool => tool.Name == "akbura_find_utilities");
            var properties = find.ProtocolTool.InputSchema.GetProperty("properties");
            Assert.True(properties.TryGetProperty("documentPath", out _));
            Assert.True(properties.TryGetProperty("includeImportable", out _));

            var info = Content(await CallAsync("akbura_project_info"), "ready");
            Assert.Equal("Debug", info.GetProperty("configuration").GetString());
            Assert.Equal("net10.0", info.GetProperty("targetFramework").GetString());
            Assert.Equal(fixture.ProjectPath, info.GetProperty("projectPath").GetString());
            Assert.Equal(1, info.GetProperty("documents").GetProperty("akburaCount").GetInt32());
            Assert.Equal(1, info.GetProperty("documents").GetProperty("akcssCount").GetInt32());
            Assert.True(info.GetProperty("standardLibrary").GetProperty("isAvailable").GetBoolean());
            Assert.Equal("using Akbura.Markup;", info.GetProperty("standardLibrary").GetProperty("variantImport").GetString());
            Assert.Contains(info.GetProperty("packageVersions").EnumerateArray(), dependency =>
                dependency.GetProperty("name").GetString() == "Avalonia" &&
                dependency.GetProperty("version").GetString() == fixture.AvaloniaVersion);
            Assert.Contains(info.GetProperty("assemblyVersions").EnumerateArray(), dependency =>
                dependency.GetProperty("name").GetString() == "Akbura");
            Assert.False(string.IsNullOrWhiteSpace(info.GetProperty("serverVersion").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(info.GetProperty("compilerVersion").GetString()));

            var gap = Content(await SearchAsync("Akbura.Row", "gap"), "success");
            Assert.Equal(info.GetProperty("snapshotId").GetString(), gap.GetProperty("snapshotId").GetString());
            var rowUtility = Assert.Single(gap.GetProperty("utilities").EnumerateArray(),
                utility => utility.GetProperty("name").GetString() == "gap");
            Assert.Equal("requires_import", rowUtility.GetProperty("availability").GetString());
            Assert.Equal("using Akbura.Styles.akcss;", rowUtility.GetProperty("requiredImports")[0].GetString());
            Assert.Equal("double", rowUtility.GetProperty("parameters")[0].GetProperty("type").GetString());
            Assert.Equal(ViewSource, await File.ReadAllTextAsync(fixture.ViewPath, cancellationToken));
            var direct = await FindDirectAsync(fixture, cancellationToken);
            Assert.Equal(direct.Utilities.Select(utility => utility.Id), gap.GetProperty("utilities").EnumerateArray()
                .Select(utility => utility.GetProperty("id").GetString()));

            var unknown = await CallAsync("akbura_find_utilities", new Dictionary<string, object?>
            {
                ["documentPath"] = "../Outside.akbura",
                ["targetType"] = "Akbura.Row",
            });
            Content(unknown, "unknown_document", isError: true);
            await VerifyLayoutWorkflowAsync(client, fixture, info, cancellationToken);

            await File.WriteAllTextAsync(fixture.ViewPath, "using Akbura.Styles.akcss;\r\n" + ViewSource, cancellationToken);
            var available = Content(await SearchAsync("Akbura.Row", "gap"), "success");
            Assert.NotEqual(gap.GetProperty("snapshotId").GetString(), available.GetProperty("snapshotId").GetString());
            Assert.All(available.GetProperty("utilities").EnumerateArray(), utility =>
                Assert.Equal("available", utility.GetProperty("availability").GetString()));

            var oldLocal = Content(await SearchAsync("Demo.CustomPanel", "local-gap"), "success");
            Assert.Equal("double", Assert.Single(oldLocal.GetProperty("utilities").EnumerateArray()).GetProperty("parameters")[0].GetProperty("type").GetString());
            await File.WriteAllTextAsync(fixture.StylesPath, StylesWithParameter("int"), cancellationToken);
            var newLocal = Content(await SearchAsync("Demo.CustomPanel", "local-gap"), "success");
            Assert.Equal("int", Assert.Single(newLocal.GetProperty("utilities").EnumerateArray()).GetProperty("parameters")[0].GetProperty("type").GetString());

            await File.WriteAllTextAsync(fixture.CodePath,
                "namespace Demo; public class CustomPanel : Avalonia.Controls.Border { }", cancellationToken);
            var changedType = Content(await SearchAsync("Demo.CustomPanel", "local-gap"), "success");
            Assert.Empty(changedType.GetProperty("utilities").EnumerateArray());
            Assert.NotEqual(newLocal.GetProperty("snapshotId").GetString(), changedType.GetProperty("snapshotId").GetString());

            await File.AppendAllTextAsync(fixture.ProjectPath, "\r\n<!-- project configuration changed -->", cancellationToken);
            var reload = Content(await CallAsync("akbura_project_info"), "reload_required");
            Assert.False(reload.GetProperty("isComplete").GetBoolean());
            Content(await SearchAsync("Akbura.Row", "gap"), "reload_required", isError: true);

            server.StandardInput.Close();
            await server.WaitForExitAsync(cancellationToken);
            await stdout;
            Assert.Equal(0, server.ExitCode);

            ValueTask<CallToolResult> CallAsync(string name, IReadOnlyDictionary<string, object?>? arguments = null) =>
                client.CallToolAsync(name, arguments, cancellationToken: cancellationToken);

            ValueTask<CallToolResult> SearchAsync(string target, string query) =>
                CallAsync("akbura_find_utilities", new Dictionary<string, object?>
                {
                    ["documentPath"] = "View.akbura",
                    ["targetType"] = target,
                    ["query"] = query,
                });
        }
        catch (Exception exception)
        {
            if (!server.HasExited) server.Kill(entireProcessTree: true);
            await server.WaitForExitAsync(CancellationToken.None);
            var errors = stderr.IsCanceled ? string.Empty : await stderr;
            throw new InvalidOperationException($"Stdio integration failed.{Environment.NewLine}{errors}", exception);
        }
    }

    [Fact]
    public void MultiTargetedProjectRequiresADeclaredTargetFramework()
    {
        using var directory = new TemporaryProjectDirectory();
        var project = Path.Combine(directory.FullName, "Multi.csproj");
        File.WriteAllText(project, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFrameworks>net9.0;net10.0</TargetFrameworks></PropertyGroup>
            </Project>
            """);

        var failure = Assert.Throws<InvalidOperationException>(() => MsBuildProjectSettings.Evaluate(project, "Release"));
        Assert.Contains("--target-framework", failure.Message);
        Assert.Throws<InvalidOperationException>(() => MsBuildProjectSettings.Evaluate(project, "Release", "net8.0"));
        var selected = MsBuildProjectSettings.Evaluate(project, "Release", "net10.0");
        Assert.Equal("Release", selected.GlobalProperties["Configuration"]);
        Assert.Equal("net10.0", selected.GlobalProperties["TargetFramework"]);
    }

    private static async Task<AkburaUtilitySearchResult> FindDirectAsync(ProjectFixture fixture, CancellationToken cancellationToken)
    {
        using var provider = new MsBuildProjectContextProvider(workspaceDiagnosticsActive: true);
        var loaded = await provider.LoadProjectAsync(fixture.ProjectPath, cancellationToken);
        using var workspace = new AkburaWorkspace(loaded.Context);
        workspace.SynchronizeProjectDocuments(workspace.DefaultProjectId, loaded.Documents, cancellationToken);
        Assert.True(workspace.CurrentSolution.TryGetDocumentContext(new Uri(fixture.ViewPath), out var context));
        return workspace.LanguageServices.UtilityCatalog.FindUtilities(context,
            new(TargetType: "Akbura.Row", Query: "gap"), cancellationToken);
    }

    private static JsonElement Content(CallToolResult result, string status, bool isError = false)
    {
        Assert.Equal(isError, result.IsError == true);
        Assert.NotNull(result.StructuredContent);
        var data = result.StructuredContent.Value;
        Assert.Equal(status, data.GetProperty("status").GetString());
        return data;
    }

    private static async Task ForwardProtocolAsync(StreamReader stdout, PipeWriter destination, CancellationToken cancellationToken)
    {
        try
        {
            while (await stdout.ReadLineAsync(cancellationToken) is { } line)
            {
                using var message = JsonDocument.Parse(line);
                Assert.Equal("2.0", message.RootElement.GetProperty("jsonrpc").GetString());
                await destination.WriteAsync(Encoding.UTF8.GetBytes(line + "\n"), cancellationToken);
            }
        }
        finally
        {
            await destination.CompleteAsync();
        }
    }

    private static ProcessStartInfo CreateProcess(string workingDirectory) => new("dotnet")
    {
        WorkingDirectory = workingDirectory,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true,
    };

    private static string StylesWithParameter(string type) => $$"""
        @using Avalonia.Controls;
        @utilities {
            StackPanel.local-gap-({{type}} value) { }
        }
        """;

    /// <summary>
    /// Builds the isolated project and supplies the files used by the stdio tests.
    /// </summary>
    private sealed class ProjectFixture : IDisposable
    {
        private readonly TemporaryProjectDirectory _directory = new();

        public ProjectFixture()
        {
            var repository = FindRepositoryRoot();
            var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
            ServerPath = Path.Combine(repository, "src", "Workspaces", "Akbura.Mcp", "bin", configuration, "net10.0", "akbura-mcp.dll");
            var runtime = Path.Combine(repository, "src", "Akbura", "bin", configuration, "net10.0", "Akbura.dll");
            AvaloniaVersion = XDocument.Load(Path.Combine(repository, "Directory.Build.props")).Descendants("AvaloniaVersion").Single().Value;
            File.Copy(Path.Combine(repository, "global.json"), Path.Combine(DirectoryPath, "global.json"));
            new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
                new XElement("PropertyGroup", new XElement("TargetFramework", "net10.0"), new XElement("RootNamespace", "Demo")),
                new XElement("ItemGroup",
                    new XElement("PackageReference", new XAttribute("Include", "Avalonia"), new XAttribute("Version", AvaloniaVersion)),
                    new XElement("Reference", new XAttribute("Include", "Akbura"), new XElement("HintPath", runtime)),
                    new XElement("AdditionalFiles", new XAttribute("Include", "View.akbura;Helpers.akcss"))))
                .Save(ProjectPath);
            File.WriteAllText(ViewPath, ViewSource);
            File.WriteAllText(StylesPath, StylesWithParameter("double"));
            File.WriteAllText(CodePath, "namespace Demo; public class CustomPanel : Avalonia.Controls.StackPanel { }");
        }

        public string DirectoryPath => _directory.FullName;
        public string ProjectPath => Path.Combine(DirectoryPath, "Fixture.csproj");
        public string ViewPath => Path.Combine(DirectoryPath, "View.akbura");
        public string StylesPath => Path.Combine(DirectoryPath, "Helpers.akcss");
        public string CodePath => Path.Combine(DirectoryPath, "CustomPanel.cs");
        public string ServerPath { get; }
        public string AvaloniaVersion { get; }

        public async Task BuildAsync(CancellationToken cancellationToken)
        {
            var startInfo = CreateProcess(DirectoryPath);
            foreach (var value in new[] { "build", ProjectPath, "--nologo", "--verbosity", "quiet" })
            {
                startInfo.ArgumentList.Add(value);
            }
            using var build = Process.Start(startInfo)!;
            var stdout = build.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = build.StandardError.ReadToEndAsync(cancellationToken);
            try
            {
                await build.WaitForExitAsync(cancellationToken);
                Assert.True(build.ExitCode == 0, await stdout + await stderr);
            }
            finally
            {
                if (!build.HasExited) build.Kill(entireProcessTree: true);
            }
        }

        public void Dispose() => _directory.Dispose();

        private static string FindRepositoryRoot()
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Akbura.slnx"))) return directory.FullName;
            }
            throw new DirectoryNotFoundException("Unable to locate the Akbura repository.");
        }
    }

    /// <summary>
    /// Owns a temporary project directory with isolated MSBuild configuration files.
    /// </summary>
    private sealed class TemporaryProjectDirectory : IDisposable
    {
        private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("akbura-mcp-");

        public TemporaryProjectDirectory()
        {
            foreach (var file in new[] { "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props" })
            {
                File.WriteAllText(Path.Combine(_directory.FullName, file), "<Project />");
            }
        }

        public string FullName => _directory.FullName;

        public void Dispose() => _directory.Delete(recursive: true);
    }
}
