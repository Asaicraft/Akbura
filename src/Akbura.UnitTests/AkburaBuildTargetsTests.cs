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
        AssertUtf16LittleEndianBom(sourceStream);
        using var reader = new StreamReader(sourceStream);
        Assert.Contains("@utilities", reader.ReadToEnd(), StringComparison.Ordinal);
    }
    private static void AssertUtf16LittleEndianBom(Stream stream)
    {
        var position = stream.Position;
        Assert.Equal(0xff, stream.ReadByte());
        Assert.Equal(0xfe, stream.ReadByte());
        stream.Position = position;
    }

}
