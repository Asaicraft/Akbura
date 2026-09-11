using Akbura.Language;
using Akbura.Language.Symbols;
using Microsoft.CodeAnalysis;
using System.Diagnostics;
using System.Reflection;
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
        Assert.NotNull(module.AkcssModule);
        var generatedTypeName = module.AkcssModule!.TypeName;
        Assert.StartsWith("global::", generatedTypeName, StringComparison.Ordinal);
        var generatedType = assembly.GetType(generatedTypeName["global::".Length..]);
        Assert.NotNull(generatedType);
        Assert.True(generatedType.IsPublic);
        var generatedSourcePath = generatedType.GetField(
            "SourcePath",
            BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(generatedSourcePath);
        Assert.Equal(source.SourceCodePath, generatedSourcePath.GetRawConstantValue());
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
        using var embeddedSource = new MemoryStream();
        sourceStream.CopyTo(embeddedSource);

        var repositoryRoot = FindRepositoryRoot();
        var sourcePath = Path.Combine(repositoryRoot, "src", "Akbura", "Styles.akcss");
        Assert.Equal(File.ReadAllBytes(sourcePath), embeddedSource.ToArray());
    }

    [Fact]
    public void AkburaBuild_EmbedsManifestWithoutCreatingAkburaIntermediateDirectory()
    {
        var repositoryRoot = FindRepositoryRoot();
        var configuration = typeof(AkburaControl).Assembly
            .GetCustomAttribute<AssemblyConfigurationAttribute>()!
            .Configuration;
        var intermediateOutputPath = Path.Combine(
            repositoryRoot,
            "src",
            "Akbura",
            "obj",
            configuration,
            "net10.0");

        Assert.False(Directory.Exists(Path.Combine(intermediateOutputPath, "Akbura")));
        Assert.Contains(
            AkburaModuleManifest.ResourceName,
            typeof(AkburaControl).Assembly.GetManifestResourceNames());
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null &&
               !File.Exists(Path.Combine(directory.FullName, "Akbura.slnx")))
        {
            directory = directory.Parent;
        }

        return Assert.IsType<DirectoryInfo>(directory).FullName;
    }
}
