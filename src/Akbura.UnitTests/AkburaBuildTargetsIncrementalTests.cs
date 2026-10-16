using Akbura.Language;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.PortableExecutable;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;

namespace Akbura.UnitTests;

public sealed partial class AkburaBuildTargetsTests
{
    [Fact]
    public async Task ManifestBuild_UnchangedInputsSkipGenerationAndStillEmbedResourcesWhenCompiling()
    {
        using var project = new ManifestBuildProject();
        var first = await project.Build();
        Assert.Equal(1, first.Generations);
        project.AssertResources("Card.akbura", "Styles.akcss");
        var bytes = File.ReadAllBytes(project.ManifestPath);
        var timestamp = File.GetLastWriteTimeUtc(project.ManifestPath);

        var second = await project.Build();
        Assert.Equal(0, second.Generations);
        Assert.Equal(0, second.Compilations);
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(project.ManifestPath));
        Assert.Equal(bytes, File.ReadAllBytes(project.ManifestPath));

        File.Delete(project.AssemblyPath);
        var recompile = await project.Build();
        Assert.Equal(0, recompile.Generations);
        Assert.Equal(1, recompile.Compilations);
        project.AssertResources("Card.akbura", "Styles.akcss");
        await project.AssertFreshParity();
    }

    [Fact]
    public async Task ManifestBuild_SourceContentAndCompleteItemSetInvalidateTheManifest()
    {
        using var project = new ManifestBuildProject();
        await project.Build();
        project.Write("Card.akbura", "param System.Int32 Number;\n<object />");
        Assert.Equal(1, (await project.Build()).Generations);
        Assert.Contains("global::System.Int32", File.ReadAllText(project.ManifestPath));

        project.Write("Styles.akcss", "@utilities { object.extra-(int value) { } }");
        Assert.Equal(1, (await project.Build()).Generations);
        Assert.Contains("name=\"extra\"", File.ReadAllText(project.ManifestPath));

        project.Write("Extra.akbura", "<object />");
        Assert.Equal(1, (await project.Build()).Generations);
        project.AssertResources("Card.akbura", "Styles.akcss", "Extra.akbura");
        File.Move(project.PathFor("Extra.akbura"), project.PathFor("Renamed.akbura"));
        Assert.Equal(1, (await project.Build()).Generations);
        project.AssertResources("Card.akbura", "Styles.akcss", "Renamed.akbura");
        File.Delete(project.PathFor("Renamed.akbura"));
        Assert.Equal(1, (await project.Build()).Generations);
        project.AssertResources("Card.akbura", "Styles.akcss");

        project.ExtraItems = "<_AkburaModuleSource Update=\"Card.akbura\" LogicalName=\"Components/Card.akbura\" />";
        Assert.Equal(1, (await project.Build()).Generations);
        project.AssertResources("Components/Card.akbura", "Styles.akcss");
        await project.AssertFreshParity();
        File.Delete(project.PathFor("Card.akbura"));
        File.Delete(project.PathFor("Styles.akcss"));
        Assert.Equal(1, (await project.Build()).Generations);
        project.AssertResources();
        Assert.Equal(0, (await project.Build()).Generations);
    }

    [Fact]
    public async Task ManifestBuild_CSharpContentInvalidatesEvenWithUnchangedLengthAndTimestamp()
    {
        using var project = new ManifestBuildProject();
        project.Write("Types.cs", "global using Payload = System.Int32; public class Marker { }");
        await project.Build();
        var timestamp = File.GetLastWriteTimeUtc(project.PathFor("Types.cs"));
        project.Write("Types.cs", "global using Payload = System.Int64; public class Marker { }");
        File.SetLastWriteTimeUtc(project.PathFor("Types.cs"), timestamp);
        Assert.Equal(1, (await project.Build()).Generations);
        Assert.Contains("global::System.Int64", File.ReadAllText(project.ManifestPath));
        project.Write("Aliases.cs", "global using AdditionalAlias = System.Decimal;");
        Assert.Equal(1, (await project.Build()).Generations);
        File.Delete(project.PathFor("Aliases.cs"));
        Assert.Equal(1, (await project.Build()).Generations);
        await project.AssertFreshParity();
    }

    [Fact]
    public async Task ManifestBuild_ResolvedReferenceContentPathsAndMembershipInvalidate()
    {
        using var project = new ManifestBuildProject();
        project.Write("Card.akbura", "param Contract.Payload Value;\n<object />");
        project.EmitReference("Contract.dll", "namespace Contract; public class Payload { }");
        project.ExtraItems = "<Reference Include=\"Contract\" HintPath=\"Contract.dll\" />";
        await project.Build();
        Assert.Contains("global::Contract.Payload", File.ReadAllText(project.ManifestPath));
        var referencePath = project.PathFor("Contract.dll");
        var timestamp = File.GetLastWriteTimeUtc(referencePath);
        var length = new FileInfo(referencePath).Length;
        project.EmitReference("Contract.dll", "namespace Contract; public class Changed { }");
        Assert.Equal(length, new FileInfo(referencePath).Length);
        File.SetLastWriteTimeUtc(referencePath, timestamp);
        Assert.Equal(1, (await project.Build()).Generations);
        Assert.Contains("global::System.Object", File.ReadAllText(project.ManifestPath));

        File.Move(referencePath, project.PathFor("Moved.dll"));
        project.ExtraItems = "<Reference Include=\"Contract\" HintPath=\"Moved.dll\" />";
        Assert.Equal(1, (await project.Build()).Generations);
        project.ExtraItems = string.Empty;
        Assert.Equal(1, (await project.Build()).Generations);
        project.ExtraItems = "<Reference Include=\"Contract\" HintPath=\"Moved.dll\" />";
        Assert.Equal(1, (await project.Build()).Generations);
        await project.AssertFreshParity();
    }

    [Theory]
    [InlineData("DefineConstants", "FEATURE_A")]
    [InlineData("LangVersion", "preview")]
    [InlineData("Nullable", "annotations")]
    [InlineData("AllowUnsafeBlocks", "true")]
    [InlineData("RootNamespace", "ChangedNamespace")]
    [InlineData("AssemblyName", "ChangedAssembly")]
    public async Task ManifestBuild_CompilerOptionsInvalidate(string name, string value)
    {
        using var project = new ManifestBuildProject();
        await project.Build();
        project.Properties = $"<{name}>{value}</{name}>";
        Assert.Equal(1, (await project.Build()).Generations);
        Assert.Equal(0, (await project.Build()).Generations);
        await project.AssertFreshParity();
    }

    [Fact]
    public async Task ManifestBuild_MissingOrCorruptOutputsAndCacheRecover()
    {
        using var project = new ManifestBuildProject();
        await project.Build();
        File.Delete(project.ManifestPath);
        Assert.Equal(1, (await project.Build()).Generations);
        File.WriteAllText(project.ManifestPath, "<truncated>");
        Assert.Equal(1, (await project.Build()).Generations);
        File.WriteAllText(project.CachePath, "{\"schemaVersion\":");
        Assert.Equal(1, (await project.Build()).Generations);
        File.WriteAllText(project.CachePath, File.ReadAllText(project.CachePath).Replace("\"SchemaVersion\":1", "\"SchemaVersion\":999", StringComparison.Ordinal));
        Assert.Equal(1, (await project.Build()).Generations);
        Assert.Equal(0, (await project.Build()).Generations);
        File.WriteAllText(project.ManifestPath + ".lock", "interrupted owner write");
        Assert.Equal(0, (await project.Build()).Generations);
        await project.AssertFreshParity();
    }

    [Fact]
    public async Task ManifestBuild_FailedGenerationDoesNotPublishSuccessfulState()
    {
        using var project = new ManifestBuildProject();
        await project.Build();
        var oldManifest = File.ReadAllBytes(project.ManifestPath);
        project.ExtraItems = "<_AkburaModuleSource Include=\"Styles.akcss\" LogicalName=\"Card.akbura\" />";
        var failed = await project.Build(expectSuccess: false);
        Assert.NotEqual(0, failed.ExitCode);
        Assert.False(File.Exists(project.CachePath));
        Assert.Equal(oldManifest, File.ReadAllBytes(project.ManifestPath));
        project.ExtraItems = string.Empty;
        Assert.Equal(1, (await project.Build()).Generations);
        project.ExtraItems = "<_AkburaModuleSource Include=\"Missing.akbura\" LogicalName=\"Missing.akbura\" />";
        Assert.NotEqual(0, (await project.Build(expectSuccess: false)).ExitCode);
        Assert.False(File.Exists(project.CachePath));
        project.ExtraItems = string.Empty;
        Assert.Equal(1, (await project.Build()).Generations);
    }

    [Fact]
    public async Task ManifestBuild_CleanAndRebuildPreserveCorrectResources()
    {
        using var project = new ManifestBuildProject();
        await project.Build();
        await project.Run("clean", "--nologo", "-v:q");
        Assert.False(File.Exists(project.ManifestPath));
        Assert.False(File.Exists(project.CachePath));
        Assert.Equal(1, (await project.Build()).Generations);
        Assert.Equal(1, (await project.Build(target: "Rebuild")).Generations);
        project.AssertResources("Card.akbura", "Styles.akcss");
    }

    [Fact]
    public async Task ManifestBuild_ProjectsAndCustomPathsHaveIndependentState()
    {
        using var first = new ManifestBuildProject();
        using var second = new ManifestBuildProject();
        first.Properties = "<BaseIntermediateOutputPath>custom-base/</BaseIntermediateOutputPath><IntermediateOutputPath>custom-obj/</IntermediateOutputPath><AkburaModuleManifestPath>custom-output/module.xml</AkburaModuleManifestPath>";
        first.ManifestPathOverride = first.PathFor("custom-output/module.xml");
        await Task.WhenAll(first.Build(), second.Build());
        Assert.NotEqual(first.CachePath, second.CachePath);
        Assert.Equal(0, (await first.Build()).Generations);
        Assert.Equal(0, (await second.Build()).Generations);
        first.AssertResources("Card.akbura", "Styles.akcss");
        second.AssertResources("Card.akbura", "Styles.akcss");
        first.Properties = first.Properties.Replace("module.xml", "other.xml", StringComparison.Ordinal);
        first.ManifestPathOverride = first.PathFor("custom-output/other.xml");
        Assert.Equal(1, (await first.Build()).Generations);
    }

    [Fact]
    public async Task ManifestBuild_DesignTimeAndOptOutDoNotGenerateOrEmbed()
    {
        using var project = new ManifestBuildProject();
        project.Properties = "<AkburaEmbedSourceFiles>false</AkburaEmbedSourceFiles>";
        Assert.Equal(0, (await project.Build()).Generations);
        Assert.Empty(project.ReadResources());
        project.Properties = "<DesignTimeBuild>true</DesignTimeBuild>";
        Assert.Equal(0, (await project.Build()).Generations);
        Assert.False(File.Exists(project.ManifestPath));
        Assert.Empty(Directory.GetFiles(project.DirectoryPath, "*.cache.json", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task ManifestBuild_MultipleTargetFrameworkContextsHaveIndependentState()
    {
        using var project = new ManifestBuildProject();
        project.FrameworkProperties = "<TargetFrameworks>net10.0;net10.0-windows</TargetFrameworks>";
        project.Write("Types.cs", "#if WINDOWS\nglobal using Payload = System.Int32;\n#else\nglobal using Payload = System.String;\n#endif\npublic class Marker { }");
        await project.Restore();
        Assert.Equal(2, (await project.Build()).Generations);
        Assert.Equal(0, (await project.Build()).Generations);
        var manifests = Directory.GetFiles(project.DirectoryPath, "Akbura.module.xml", SearchOption.AllDirectories);
        Assert.Equal(2, manifests.Length);
        Assert.Equal(2, Directory.GetFiles(project.DirectoryPath, "*.cache.json", SearchOption.AllDirectories).Length);
        project.AssertResources("Card.akbura", "Styles.akcss");
        Assert.Contains("global::System.String", File.ReadAllText(project.ManifestPath));
        var windowsResources = project.ReadResources("net10.0-windows");
        Assert.Equal(new[] { "!Akbura", "Card.akbura", "Styles.akcss" }, windowsResources.Keys.Order());
        var windowsManifestPath = project.PathFor("obj/Debug/net10.0-windows/Akbura.module.xml");
        Assert.Contains("global::System.Int32", File.ReadAllText(windowsManifestPath));
        Assert.Equal(File.ReadAllBytes(windowsManifestPath), windowsResources["!Akbura"]);
        Assert.Equal(File.ReadAllBytes(project.PathFor("Card.akbura")), windowsResources["Card.akbura"]);
        var previous = manifests.ToDictionary(path => path, File.ReadAllBytes);
        foreach (var cachePath in Directory.GetFiles(project.DirectoryPath, "*.cache.json", SearchOption.AllDirectories))
        {
            File.Delete(cachePath);
        }

        Assert.Equal(2, (await project.Build()).Generations);
        foreach (var pair in previous)
        {
            Assert.Equal(pair.Value, File.ReadAllBytes(pair.Key));
        }
    }

    [Fact]
    public async Task ManifestBuild_ProjectReferenceUpdatesUseTheCurrentResolvedAssembly()
    {
        using var project = new ManifestBuildProject();
        project.Write("Contract/Contract.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        project.Write("Contract/Value.cs", "namespace Contract; public class Payload { }");
        project.ExtraItems = "<Compile Remove=\"Contract/**/*.cs\" /><ProjectReference Include=\"Contract/Contract.csproj\" />";
        project.Write("Card.akbura", "param Contract.Payload Value;\n<object />");
        await project.Build();
        Assert.Contains("global::Contract.Payload", File.ReadAllText(project.ManifestPath));
        project.Write("Contract/Value.cs", "namespace Contract; public class Other { }");
        Assert.Equal(1, (await project.Build()).Generations);
        Assert.Contains("global::System.Object", File.ReadAllText(project.ManifestPath));
        Assert.Equal(0, (await project.Build()).Generations);
        await project.AssertFreshParity();
    }

    [Fact]
    public async Task ManifestBuild_SharedOutputOwnershipFailsClearlyInsteadOfOverwriting()
    {
        using var first = new ManifestBuildProject();
        using var second = new ManifestBuildProject();
        var output = first.PathFor("shared/module.xml");
        first.Properties = $"<AkburaModuleManifestPath>{SecurityElement.Escape(output)}</AkburaModuleManifestPath>";
        first.ManifestPathOverride = output;
        second.Properties = first.Properties;
        await first.Build();
        var bytes = File.ReadAllBytes(output);
        var failed = await second.Build(expectSuccess: false);
        Assert.NotEqual(0, failed.ExitCode);
        Assert.Contains("belongs to a different project", failed.Output);
        Assert.Equal(bytes, File.ReadAllBytes(output));
        Assert.Equal(0, (await first.Build()).Generations);
    }

    [Fact]
    public async Task ManifestBuild_LockedOutputAndInvalidReferenceFailWithoutPublishingState()
    {
        using var project = new ManifestBuildProject();
        await project.Build();
        using (var outputLock = new FileStream(project.ManifestPath + ".lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var failed = await project.Build(expectSuccess: false);
            Assert.NotEqual(0, failed.ExitCode);
            Assert.Contains("is in use by another build", failed.Output);
            Assert.False(File.Exists(project.CachePath));
        }

        Assert.Equal(1, (await project.Build()).Generations);
        project.Write("Broken.dll", "not a PE assembly");
        project.ExtraTargets = "<Target Name=\"AddBrokenReference\" AfterTargets=\"FindReferenceAssembliesForReferences\"><ItemGroup><ReferencePathWithRefAssemblies Include=\"Broken.dll\" /></ItemGroup></Target>";
        var failedReference = await project.Build(expectSuccess: false);
        Assert.NotEqual(0, failedReference.ExitCode);
        Assert.Equal(1, failedReference.Generations);
        Assert.False(File.Exists(project.CachePath));
        project.ExtraTargets = string.Empty;
        Assert.Equal(1, (await project.Build()).Generations);
    }

    [Fact]
    public async Task ManifestBuild_ToolDeploymentChangesInvalidateWithoutChangingAssemblyVersions()
    {
        using var project = new ManifestBuildProject();
        var configuration = typeof(AkburaControl).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
        var toolDirectory = Path.Combine(FindRepositoryRoot(), "src", "Akbura.Build.Tasks", "bin", configuration, "net10.0");
        Directory.CreateDirectory(project.PathFor("tools"));
        foreach (var file in Directory.EnumerateFiles(toolDirectory))
        {
            File.Copy(file, project.PathFor("tools/" + Path.GetFileName(file)));
        }

        project.Properties = "<AkburaBuildTasksAssembly>$(MSBuildProjectDirectory)/tools/Akbura.Build.Tasks.dll</AkburaBuildTasksAssembly>";
        await project.Build();
        Assert.Equal(0, (await project.Build()).Generations);
        project.Write("tools/deployment.json", "{\"identity\":1}");
        Assert.Equal(1, (await project.Build()).Generations);
        Assert.Equal(0, (await project.Build()).Generations);
        var timestamp = File.GetLastWriteTimeUtc(project.PathFor("tools/deployment.json"));
        project.Write("tools/deployment.json", "{\"identity\":2}");
        File.SetLastWriteTimeUtc(project.PathFor("tools/deployment.json"), timestamp);
        Assert.Equal(1, (await project.Build()).Generations);
    }

    [Fact]
    public async Task ManifestBuild_PackedPackageWorksInAnIndependentConsumer()
    {
        using var project = new ManifestBuildProject();
        var configuration = typeof(AkburaControl).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
        var version = "12.0.4-manifest-test." + Guid.NewGuid().ToString("N");
        var feed = project.PathFor("feed");
        await project.Run("pack", Path.Combine(FindRepositoryRoot(), "src", "Akbura", "Akbura.csproj"),
            "--no-restore", "--nologo", "-v:q", "-c", configuration, "-o", feed, "-p:PackageVersion=" + version);
        project.PackageVersion = version;
        project.Write("Types.cs", "public class Marker { }");
        project.Write("Card.akbura", "using Avalonia.Controls;\nparam string Value;\n<TextBlock Text={Value} />");
        await project.Restore("--source", feed);
        Assert.Equal(1, (await project.Build()).Generations);
        project.AssertResources("Card.akbura", "Styles.akcss");
        Assert.Equal(0, (await project.Build()).Generations);
        File.Delete(project.AssemblyPath);
        var recompile = await project.Build();
        Assert.Equal(0, recompile.Generations);
        Assert.Equal(1, recompile.Compilations);
        project.AssertResources("Card.akbura", "Styles.akcss");
        await project.AssertFreshParity();
    }

    private sealed class ManifestBuildProject : IDisposable
    {
        private readonly string _repositoryRoot = FindRepositoryRoot();
        private string _lastProject = string.Empty;
        private bool _restored;
        private int _buildNumber;
        private string? _cachePath;

        public ManifestBuildProject()
        {
            DirectoryPath = Path.Combine(Path.GetTempPath(), "AkburaManifestTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DirectoryPath);
            Write("Directory.Build.props", "<Project />");
            Write("Directory.Build.targets", "<Project />");
            Write("Directory.Packages.props", "<Project />");
            Write("Card.akbura", "param Payload Value;\n<object />");
            Write("Styles.akcss", ".example { }");
            Write("Types.cs", "global using Payload = System.String; public class Marker { }");
        }

        public string DirectoryPath { get; }
        public string ExtraItems { get; set; } = string.Empty;
        public string ExtraTargets { get; set; } = string.Empty;
        public string Properties { get; set; } = string.Empty;
        public string FrameworkProperties { get; set; } = "<TargetFramework>net10.0</TargetFramework>";
        public string? ManifestPathOverride { get; set; }
        public string? PackageVersion { get; set; }
        public string ManifestPath => ManifestPathOverride ?? PathFor("obj/Debug/net10.0/Akbura.module.xml");
        public string AssemblyPath => PathFor("obj/Debug/net10.0/ManifestProbe.dll");
        public string CachePath => _cachePath ??= Assert.Single(Directory.GetFiles(DirectoryPath, "*.cache.json", SearchOption.AllDirectories));
        public string PathFor(string path) => Path.Combine(DirectoryPath, path);

        public void Write(string path, string content)
        {
            var fullPath = PathFor(path);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, content, new UTF8Encoding(false));
        }

        public async Task Restore(params string[] arguments)
        {
            SaveProject();
            await Run(["restore", "ManifestProbe.csproj", "--nologo", "-v:q", .. arguments]);
            _restored = true;
        }

        public async Task<ManifestBuildResult> Build(bool expectSuccess = true, string target = "Build")
        {
            SaveProject();
            if (!_restored)
            {
                await Restore();
            }

            var result = await RunCore("build", "ManifestProbe.csproj", "--no-restore", "--nologo", "-v:diag", "-t:" + target);
            Write($"build-{++_buildNumber}.log", result.Output);
            if (expectSuccess)
            {
                Assert.True(result.ExitCode == 0, result.Output[^Math.Min(result.Output.Length, 14000)..]);
                var caches = Directory.GetFiles(DirectoryPath, "*.cache.json", SearchOption.AllDirectories);
                if (caches.Length == 1)
                {
                    _cachePath = caches[0];
                }
            }

            return result;
        }

        public async Task Run(params string[] arguments)
        {
            var result = await RunCore(arguments);
            Assert.True(result.ExitCode == 0, result.Output);
        }

        private async Task<ManifestBuildResult> RunCore(params string[] arguments)
        {
            var start = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = DirectoryPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (var argument in arguments)
            {
                start.ArgumentList.Add(argument);
            }

            start.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en-US";
            start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch
            {
                process.Kill(entireProcessTree: true);
                throw;
            }

            return new ManifestBuildResult(process.ExitCode, await stdout + await stderr);
        }

        private void SaveProject()
        {
            var configuration = typeof(AkburaControl).Assembly.GetCustomAttribute<System.Reflection.AssemblyConfigurationAttribute>()!.Configuration;
            var taskAssembly = Path.Combine(_repositoryRoot, "src", "Akbura.Build.Tasks", "bin", configuration, "net10.0", "Akbura.Build.Tasks.dll");
            var targets = Path.Combine(_repositoryRoot, "src", "Akbura", "buildTransitive", "Akbura.targets");
            var project = $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    {FrameworkProperties}
                    <AssemblyName>ManifestProbe</AssemblyName>
                    <RootNamespace>ManifestProbe</RootNamespace>
                    <Nullable>enable</Nullable>
                    <LangVersion>latest</LangVersion>
                    <EnableDefaultCompileItems>true</EnableDefaultCompileItems>
                    <DefaultItemExcludes>$(DefaultItemExcludes);custom-obj/**;custom-base/**</DefaultItemExcludes>
                    {(PackageVersion == null ? $"<AkburaBuildTasksAssembly>{SecurityElement.Escape(taskAssembly)}</AkburaBuildTasksAssembly>" : string.Empty)}
                    {Properties}
                  </PropertyGroup>
                  {(PackageVersion == null ? $"<Import Project=\"{SecurityElement.Escape(targets)}\" />" : $"<ItemGroup><PackageReference Include=\"Akbura\" Version=\"{PackageVersion}\" /></ItemGroup>")}
                  <ItemGroup>{ExtraItems}</ItemGroup>
                  {ExtraTargets}
                </Project>
                """;
            if (_lastProject != project)
            {
                Write("ManifestProbe.csproj", project);
                _lastProject = project;
            }
        }

        public Dictionary<string, byte[]> ReadResources(string targetFramework = "net10.0")
        {
            var assemblyPath = PathFor($"bin/Debug/{targetFramework}/ManifestProbe.dll");
            if (!File.Exists(assemblyPath))
            {
                assemblyPath = PathFor("bin/Debug/net10.0/ChangedAssembly.dll");
            }

            using var stream = File.OpenRead(assemblyPath);
            using var pe = new PEReader(stream);
            var metadata = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader(pe);
            var block = pe.GetSectionData(pe.PEHeaders.CorHeader!.ResourcesDirectory.RelativeVirtualAddress);
            var resources = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var handle in metadata.ManifestResources)
            {
                var resource = metadata.GetManifestResource(handle);
                var offset = checked((int)resource.Offset);
                var length = BinaryPrimitives.ReadInt32LittleEndian(block.GetContent(offset, sizeof(int)).AsSpan());
                resources.Add(metadata.GetString(resource.Name), block.GetContent(offset + sizeof(int), length).ToArray());
            }

            return resources;
        }

        public void AssertResources(params string[] sourcePaths)
        {
            var resources = ReadResources();
            Assert.Equal(sourcePaths.Append("!Akbura").Order(), resources.Keys.Order());
            Assert.Equal(File.ReadAllBytes(ManifestPath), resources["!Akbura"]);
            using var stream = new MemoryStream(resources["!Akbura"]);
            var manifest = AkburaModuleManifestSerializer.Read(stream);
            Assert.Equal(sourcePaths.Order(), manifest.Sources.Select(source => source.SourceCodePath).Order());
            foreach (var path in sourcePaths)
            {
                var physicalPath = path == "Components/Card.akbura" ? "Card.akbura" : path;
                Assert.Equal(File.ReadAllBytes(PathFor(physicalPath)), resources[path]);
            }
        }

        public async Task AssertFreshParity()
        {
            var previous = File.ReadAllBytes(ManifestPath);
            File.Delete(CachePath);
            Assert.Equal(1, (await Build()).Generations);
            Assert.Equal(previous, File.ReadAllBytes(ManifestPath));
            using var stream = new MemoryStream(previous);
            using var serialized = new MemoryStream();
            AkburaModuleManifestSerializer.Write(serialized, AkburaModuleManifestSerializer.Read(stream));
            Assert.Equal(previous, serialized.ToArray());
        }

        public void EmitReference(string path, string source)
        {
            var compilation = CSharpCompilation.Create("Contract",
                [CSharpSyntaxTree.ParseText(source)],
                [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, deterministic: true));
            using var stream = File.Create(PathFor(path));
            var result = compilation.Emit(stream);
            Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(DirectoryPath, recursive: true);
            }
            catch (IOException)
            {
                // An SDK/compiler/telemetry child may briefly retain its working directory.
                // Do not mask the build assertions with best-effort temporary-file cleanup.
            }
        }
    }

    private sealed record ManifestBuildResult(int ExitCode, string Output)
    {
        public int Generations => Regex.Matches(Output, "Task \"GenerateAkburaModuleManifest\"(?: \\(|\\r?\\n)").Count;
        public int Compilations => Regex.Matches(Output, "Task \"Csc\"(?: \\(|\\r?\\n)").Count;
    }
}
