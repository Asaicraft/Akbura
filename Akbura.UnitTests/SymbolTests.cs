using Akbura.Language.Symbols;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using AkburaSymbolKind = Akbura.Language.Symbols.SymbolKind;

namespace Akbura.UnitTests;

public class SymbolTests
{
    [Fact]
    public void MarkupComponentSymbol_StoresResolvedCSharpType()
    {
        var compilation = CSharpCompilation.Create(
            assemblyName: "TestAssembly",
            references: CreateAvaloniaReferences());

        var buttonType = compilation.GetTypeByMetadataName("Avalonia.Controls.Button");
        Assert.NotNull(buttonType);

        var symbol = new MarkupComponentSymbol(
            name: "Button",
            csharpDefinition: new CSharpSymbolDefinition(buttonType!));

        Assert.Equal(AkburaSymbolKind.MarkupComponent, symbol.Kind);
        Assert.IsAssignableFrom<IMarkupComponentSymbol>(symbol);
        Assert.Equal(SymbolLanguage.Markup, symbol.Language);
        Assert.Equal("Button", symbol.Name);
        Assert.Equal("Button", symbol.MetadataName);
        Assert.True(symbol.CanBeReferencedByName);
        Assert.True(symbol.IsDefinition);
        Assert.False(symbol.IsImplicitlyDeclared);
        Assert.Same(buttonType, symbol.ComponentType);
        Assert.True(SymbolEqualityComparer.Default.Equals(buttonType, symbol.CSharpDefinition.Symbol));
        Assert.Equal("Button -> global::Avalonia.Controls.Button", symbol.ToDisplayString());
    }

    [Fact]
    public void MarkupComponentSymbol_RequiresCSharpDefinition()
    {
        Assert.Throws<ArgumentException>(() => new MarkupComponentSymbol("Button", default));
    }

    internal static MetadataReference[] CreateAvaloniaReferences()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var trustedPlatformAssemblies =
            AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string
            ?? throw new InvalidOperationException(
                "Trusted platform assemblies are unavailable.");
        foreach (var path in trustedPlatformAssemblies.Split(Path.PathSeparator))
        {
            paths.Add(path);
        }

        var avaloniaControlsAssembly = typeof(Akbura.AkburaControl).BaseType!.Assembly;
        paths.Add(typeof(Avalonia.AvaloniaObject).Assembly.Location);
        paths.Add(avaloniaControlsAssembly.Location);
        paths.Add(typeof(Akbura.AkburaControl).Assembly.Location);

        return paths
            .Select(static path => MetadataReference.CreateFromFile(path))
            .ToArray();
    }
}
