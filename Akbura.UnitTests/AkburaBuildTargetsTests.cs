using Akbura.Language;
using Akbura.Language.Symbols;
using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Akbura.UnitTests;

public sealed class AkburaBuildTargetsTests
{
    [Fact]
    public void AkburaAssembly_EmbedsItsOwnAkburaModuleManifest()
    {
        var assembly = typeof(AkburaControl).Assembly;
        var resourceNames = assembly.GetManifestResourceNames();

        Assert.Contains("Styles.akcss", resourceNames);
        Assert.Contains(AkburaModuleManifest.ResourceName, resourceNames);
        Assert.True(AkburaModuleManifestSerializer.TryRead(assembly, out var manifest));

        Assert.NotNull(manifest);
        Assert.Equal(AkburaModuleManifest.CurrentFormatVersion, manifest.FormatVersion);
        Assert.Equal("Akbura", manifest.AssemblyName);

        var source = Assert.Single(
            manifest.Sources,
            static source => source.SourceCodePath == "Styles.akcss");
        Assert.Equal(AkburaModuleSourceKind.Akcss, source.Kind);

        var module = Assert.Single(source.Declarations);
        Assert.Equal(DeclarationKind.AkcssModule, module.Kind);
        Assert.Equal("Akbura.Styles.akcss", module.MetadataName);
        var widthUtility = Assert.Single(
            module.Children,
            static declaration => declaration.Kind == DeclarationKind.AkcssUtility &&
                                  declaration.Name == "w");
        Assert.NotNull(widthUtility.AkcssUtility);
        var utilitySignature = widthUtility.AkcssUtility!;
        Assert.Equal("Control", utilitySignature.TargetTypeName);
        Assert.Equal(1, utilitySignature.ParameterCount);
        var widthParameter = Assert.Single(utilitySignature.Parameters);
        Assert.Equal(0, widthParameter.Ordinal);
        Assert.Equal("width", widthParameter.Name);
        Assert.Equal("double", widthParameter.TypeName);
        Assert.True(widthParameter.SourceLength > 0);

        using var sourceStream = AkburaModuleManifestSerializer.OpenSource(assembly, source);
        using var reader = new StreamReader(sourceStream);
        Assert.Contains("@utilities", reader.ReadToEnd(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildTargets_EmbedAkburaSourcesWithProjectRelativePaths()
    {
        var repositoryPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            ".."));
        var sourceTargetsPath = Path.Combine(
            repositoryPath,
            "Akbura",
            "buildTransitive",
            "Akbura.targets");
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name ?? "Debug";
        var buildTasksOutputPath = Path.Combine(
            repositoryPath,
            "Akbura.Build.Tasks",
            "bin",
            configuration,
            "net10.0");
        var projectPath = Path.Combine(
            Path.GetTempPath(),
            nameof(AkburaBuildTargetsTests),
            Guid.NewGuid().ToString("N"));

        try
        {
            var packageTargetsPath = Path.Combine(projectPath, "package", "buildTransitive");
            var packageToolsPath = Path.Combine(projectPath, "package", "tools", "net10.0");
            Directory.CreateDirectory(packageTargetsPath);
            Directory.CreateDirectory(packageToolsPath);
            File.Copy(sourceTargetsPath, Path.Combine(packageTargetsPath, "Akbura.targets"));
            foreach (var filePath in Directory.EnumerateFiles(buildTasksOutputPath)
                         .Where(static path => Path.GetExtension(path) is ".dll" or ".json"))
            {
                File.Copy(filePath, Path.Combine(packageToolsPath, Path.GetFileName(filePath)));
            }

            var targetsPath = Path.Combine(packageTargetsPath, "Akbura.targets");
            Directory.CreateDirectory(Path.Combine(projectPath, "Views"));
            Directory.CreateDirectory(Path.Combine(projectPath, "Styles"));
            Directory.CreateDirectory(Path.Combine(projectPath, "obj"));

            await File.WriteAllTextAsync(
                Path.Combine(projectPath, "AkburaTargetConsumer.csproj"),
                $$"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                  <Import Project="{{targetsPath}}" />
                </Project>
                """);
            await File.WriteAllTextAsync(
                Path.Combine(projectPath, "ComponentTypes.cs"),
                """
                namespace Akbura
                {
                    public class AkburaControl
                    {
                    }
                }

                namespace Views
                {
                    public interface IClock
                    {
                    }

                    public class CustomControl : Akbura.AkburaControl
                    {
                    }

                    public partial class Counter : CustomControl
                    {
                    }

                    public partial class PlainCounter
                    {
                        private int value;
                    }
                }
                """);
            await File.WriteAllTextAsync(
                Path.Combine(projectPath, "Views", "Counter.akbura"),
                """
                namespace Views;

                inject IClock Clock;
                param bind int UserId = 1;
                state int count = 0;
                command void Refresh(int value);

                <TextBlock Text={count} />
                """);
            await File.WriteAllTextAsync(
                Path.Combine(projectPath, "Views", "DefaultCounter.akbura"),
                """
                namespace Views;

                param string Label = "Default";
                """);
            await File.WriteAllTextAsync(
                Path.Combine(projectPath, "Views", "PlainCounter.akbura"),
                """
                namespace Views;

                param int Value = 1;
                """);
            await File.WriteAllTextAsync(
                Path.Combine(projectPath, "Styles", "Theme.akcss"),
                """
                .counter { Width: 100; }

                @utilities {
                    Control.w-(double value) { Width: value; }
                }
                """);
            await File.WriteAllTextAsync(
                Path.Combine(projectPath, "obj", "Ignored.akbura"),
                "state ignored = true;");

            var result = await BuildProjectAsync(projectPath);
            Assert.True(result.ExitCode == 0, result.Output);

            var assemblyPath = Path.Combine(
                projectPath,
                "bin",
                "Debug",
                "net10.0",
                "AkburaTargetConsumer.dll");
            using var stream = File.OpenRead(assemblyPath);
            using var peReader = new PEReader(stream);
            var metadataReader = peReader.GetMetadataReader();
            var resourceNames = metadataReader.ManifestResources
                .Select(handle => metadataReader.GetString(
                    metadataReader.GetManifestResource(handle).Name))
                .ToArray();

            Assert.Contains("Views/Counter.akbura", resourceNames);
            Assert.Contains("Views/DefaultCounter.akbura", resourceNames);
            Assert.Contains("Views/PlainCounter.akbura", resourceNames);
            Assert.Contains("Styles/Theme.akcss", resourceNames);
            Assert.Contains(AkburaModuleManifest.ResourceName, resourceNames);
            Assert.DoesNotContain("obj/Ignored.akbura", resourceNames);

            var manifestPath = Path.Combine(
                projectPath,
                "obj",
                "Debug",
                "net10.0",
                "Akbura",
                "Akbura.module.xml");
            using var manifestStream = File.OpenRead(manifestPath);
            var manifest = AkburaModuleManifestSerializer.Read(manifestStream);

            Assert.Equal(AkburaModuleManifest.CurrentFormatVersion, manifest.FormatVersion);
            Assert.Equal("AkburaTargetConsumer", manifest.AssemblyName);
            Assert.DoesNotContain(
                manifest.Sources,
                static source => source.SourceCodePath == "obj/Ignored.akbura");

            var componentSource = Assert.Single(
                manifest.Sources,
                static source => source.SourceCodePath == "Views/Counter.akbura");
            Assert.Equal(AkburaModuleSourceKind.Component, componentSource.Kind);

            var component = Assert.Single(componentSource.Declarations);
            Assert.Equal(DeclarationKind.Component, component.Kind);
            Assert.Equal("global::Views.Counter", component.MetadataName);
            Assert.Empty(component.Children);
            Assert.NotNull(component.Component);
            var componentSignature = component.Component!;
            Assert.Equal("global::Views.CustomControl", componentSignature.BaseTypeName);
            Assert.Equal(1, componentSignature.ParameterCount);
            var userId = Assert.Single(componentSignature.Parameters);
            Assert.Equal(0, userId.Ordinal);
            Assert.Equal("UserId", userId.Name);
            Assert.Equal("global::System.Int32", userId.TypeName);
            Assert.Equal(ParamBindingKind.Bind, userId.BindingKind);
            Assert.True(userId.HasDefaultValue);
            Assert.True(userId.SourceLength > 0);
            Assert.Equal(1, componentSignature.InjectedServiceCount);
            var clock = Assert.Single(componentSignature.InjectedServices);
            Assert.Equal(0, clock.Ordinal);
            Assert.Equal("Clock", clock.Name);
            Assert.Equal("global::Views.IClock", clock.TypeName);
            Assert.True(clock.SourceLength > 0);

            var defaultComponentSource = Assert.Single(
                manifest.Sources,
                static source => source.SourceCodePath == "Views/DefaultCounter.akbura");
            var defaultComponent = Assert.Single(defaultComponentSource.Declarations);
            Assert.NotNull(defaultComponent.Component);
            Assert.Equal(
                "global::Akbura.AkburaControl",
                defaultComponent.Component!.BaseTypeName);

            var plainComponentSource = Assert.Single(
                manifest.Sources,
                static source => source.SourceCodePath == "Views/PlainCounter.akbura");
            var plainComponent = Assert.Single(plainComponentSource.Declarations);
            Assert.NotNull(plainComponent.Component);
            Assert.Equal(
                "global::Akbura.AkburaControl",
                plainComponent.Component!.BaseTypeName);

            var akcssSource = Assert.Single(
                manifest.Sources,
                static source => source.SourceCodePath == "Styles/Theme.akcss");
            var akcssModule = Assert.Single(akcssSource.Declarations);
            Assert.Contains(
                akcssModule.Children,
                static declaration => declaration.Kind == DeclarationKind.AkcssStyle &&
                                      declaration.Name == ".counter");
            var widthUtility = Assert.Single(
                akcssModule.Children,
                static declaration => declaration.Kind == DeclarationKind.AkcssUtility &&
                                      declaration.Name == "w");
            Assert.NotNull(widthUtility.AkcssUtility);
            var utilitySignature = widthUtility.AkcssUtility!;
            Assert.Equal("Control", utilitySignature.TargetTypeName);
            Assert.Equal(1, utilitySignature.ParameterCount);
            var valueParameter = Assert.Single(utilitySignature.Parameters);
            Assert.Equal(0, valueParameter.Ordinal);
            Assert.Equal("value", valueParameter.Name);
            Assert.Equal("double", valueParameter.TypeName);
            Assert.True(valueParameter.SourceLength > 0);
        }
        finally
        {
            if (Directory.Exists(projectPath))
            {
                Directory.Delete(projectPath, recursive: true);
            }
        }
    }

    private static async Task<(int ExitCode, string Output)> BuildProjectAsync(string projectPath)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = projectPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("build");
        startInfo.ArgumentList.Add("--nologo");
        startInfo.ArgumentList.Add("--verbosity");
        startInfo.ArgumentList.Add("quiet");
        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start dotnet build.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();
        return (
            process.ExitCode,
            await standardOutput + Environment.NewLine + await standardError);
    }
}
