using Akbura.Language;
using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Microsoft.CodeAnalysis.CSharp;

namespace Akbura.UnitTests;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class BuiltInStylesTests
{
    [Fact]
    public async Task StylesResources_ProvideTailwindThemeValues()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(AvaloniaTestAppBuilder));
        var resources = await session.Dispatch(
            () => Assert.IsType<ResourceDictionary>(AvaloniaXamlLoader.Load(
                new Uri("avares://Akbura/Styles.axaml"),
                baseUri: null)),
            CancellationToken.None);

        Assert.Equal(4d, resources["--spacing"]);
        Assert.Equal(16d, resources["--text-base"]);
        Assert.Equal(FontWeight.Bold, resources["--font-weight-bold"]);
        Assert.Equal(new CornerRadius(6), resources["--radius-md"]);

        var shadow = Assert.IsType<BoxShadows>(resources["--shadow-md"]);
        Assert.Equal(2, shadow.Count);

        var blue = Assert.IsType<SolidColorBrush>(resources["--color-blue-500"]);
        Assert.Equal(Color.Parse("#3B82F6"), blue.Color);
        var mauve = Assert.IsType<SolidColorBrush>(resources["--color-mauve-500"]);
        Assert.Equal(Color.Parse("#79697B"), mauve.Color);

        string[] colorNames =
        [
            "red", "orange", "amber", "yellow", "lime", "green", "emerald",
            "teal", "cyan", "sky", "blue", "indigo", "violet", "purple",
            "fuchsia", "pink", "rose", "slate", "gray", "zinc", "neutral",
            "stone", "mauve", "olive", "mist", "taupe",
        ];
        int[] shades = [50, 100, 200, 300, 400, 500, 600, 700, 800, 900, 950];
        foreach (var colorName in colorNames)
        {
            foreach (var shade in shades)
            {
                Assert.True(resources.ContainsKey($"--color-{colorName}-{shade}"));
            }
        }
    }

    [Fact]
    public void StylesAkcss_BindsBuiltInUtilitiesAndRepresentativeAttributes()
    {
        var stylesPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "Akbura",
            "Styles.akcss"));
        var akcssTree = AkcssSyntaxTree.ParseText(
            File.ReadAllText(stylesPath),
            stylesPath,
            "Akbura.Styles.akcss");
        var componentTree = AkburaSyntaxTree.ParseText(
            """
            using Avalonia.Controls;
            using Akbura.Styles.akcss;

            <StackPanel w-4 h-8 h-auto min-w-2 max-h-20 m-4 gap-2 opacity-75
                        overflow-hidden pointer-events-auto self-center bg-slate-100>
                <Border p-4 border-1 border-slate-300 rounded-md shadow-lg
                        col-1 row-2 col-span-2>
                    <TextBlock text-center font-bold whitespace-nowrap />
                </Border>
                <Button px-4 py-2 bg-blue-500 text-white rounded-full />
                <Grid gap-x-2 />
            </StackPanel>
            """,
            "TailwindSample.akbura");
        var compilation = new AkburaCompilation(
            CreateCSharpCompilation(),
            [componentTree],
            [akcssTree]);
        var semanticModel = compilation.GetSemanticModel(componentTree);
        var utilitiesSection = Assert.Single(
            akcssTree.GetRoot().Members.OfType<AkcssUtilitiesSectionSyntax>());

        Assert.True(utilitiesSection.Utilities.Count >= 40);
        foreach (var utility in utilitiesSection.Utilities)
        {
            var symbol = Assert.IsAssignableFrom<ITailwindUtilitySymbol>(
                semanticModel.GetDeclaredSymbol(utility));
            Assert.All(symbol.Operations, operation => Assert.False(
                operation.HasErrors,
                $"Utility '{symbol.MetadataName}' contains an invalid operation."));
        }

        var attributes = componentTree.GetRoot()
            .DescendantNodes()
            .OfType<TailwindAttributeSyntax>()
            .ToArray();

        Assert.NotEmpty(attributes);
        foreach (var attribute in attributes)
        {
            var operation = Assert.IsAssignableFrom<ITailwindUtilityAttributeOperation>(
                semanticModel.GetOperation(attribute));
            Assert.False(operation.HasErrors, attribute.ToFullString());
            Assert.NotNull(operation.Utility);
            Assert.Empty(semanticModel.GetSemanticDiagnostics(attribute));
        }

        var operations = attributes.ToDictionary(
            static attribute => attribute.ToFullString().Trim(),
            attribute => Assert.IsAssignableFrom<ITailwindUtilityAttributeOperation>(
                semanticModel.GetOperation(attribute)));

        Assert.Equal("w", operations["w-4"].UtilityName);
        Assert.Equal("4", Assert.Single(operations["w-4"].Arguments).Text);
        Assert.Equal("h", operations["h-8"].UtilityName);
        Assert.Equal("8", Assert.Single(operations["h-8"].Arguments).Text);

        AssertStaticUtility(operations["h-auto"], "h-auto");
        AssertStaticUtility(operations["self-center"], "self-center");
        AssertStaticUtility(operations["text-center"], "text-center");
        AssertStaticUtility(operations["font-bold"], "font-bold");
        AssertStaticUtility(operations["rounded-md"], "rounded-md");
        AssertStaticUtility(operations["rounded-full"], "rounded-full");
    }

    private static void AssertStaticUtility(
        ITailwindUtilityAttributeOperation operation,
        string utilityName)
    {
        Assert.Equal(utilityName, operation.UtilityName);
        Assert.Equal(utilityName, operation.Utility?.Name);
        Assert.True(operation.Arguments.IsEmpty);
    }

    private static CSharpCompilation CreateCSharpCompilation()
    {
        return CSharpCompilation.Create(
            "BuiltInStylesTests",
            references: SymbolTests.CreateAvaloniaReferences());
    }
}
