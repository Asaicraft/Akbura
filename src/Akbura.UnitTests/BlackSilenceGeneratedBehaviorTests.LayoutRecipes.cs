using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System.Reflection;

namespace Akbura.UnitTests;

public sealed partial class BlackSilenceGeneratedBehaviorTests
{
    private const double RecipeSpacingResource = 3d;

    [Fact]
    public Task LayoutRecipe_ResponsiveCardGridWrapsAtBothBreakpoints()
    {
        return RunRecipeAsync("ResponsiveCardGrid", (window, component) =>
        {
            var row = Assert.IsType<Row>(component.Child);
            Assert.Equal(12, row.Columns);
            Assert.Equal(4, row.Children.Count);
            Assert.Equal(3d * RecipeSpacingResource, row.RowSpacing);
            Assert.Equal(3d * RecipeSpacingResource, row.ColumnSpacing);

            foreach (var (width, span) in new[] { (767d, 12), (768d, 6), (1023d, 6), (1024d, 4), (1023d, 6), (767d, 12) })
            {
                ResizeRecipeWindow(window, width);
                Assert.True(row.Bounds.Width > 0d);
                var perRow = row.Columns / span;
                for (var index = 0; index < row.Children.Count; index++)
                {
                    var child = row.Children[index];
                    Assert.Equal(span, Grid.GetColumnSpan(child));
                    Assert.True(child.Bounds.Width > 0d);
                    var first = row.Children[index - index % perRow];
                    Assert.Equal(first.Bounds.Y, child.Bounds.Y);
                    if (index % perRow == 0)
                    {
                        Assert.Equal(0d, child.Bounds.X);
                        if (index != 0)
                        {
                            var previous = row.Children[index - 1];
                            Assert.Equal(previous.Bounds.Bottom + row.RowSpacing, child.Bounds.Y);
                        }
                    }
                    else
                    {
                        Assert.True(child.Bounds.X > row.Children[index - 1].Bounds.X);
                    }
                }
            }
        });
    }

    [Fact]
    public Task LayoutRecipe_ResponsiveSpacingUsesTheResourceAndRestoresNarrowerPadding()
    {
        return RunRecipeAsync("ResponsiveSpacing", (window, component) =>
        {
            var border = Assert.IsType<Border>(component.Child);
            foreach (var (width, scale) in new[] { (767d, 2d), (768d, 4d), (1023d, 4d), (1024d, 6d), (1023d, 4d), (767d, 2d) })
            {
                ResizeRecipeWindow(window, width);
                Assert.Equal(new Thickness(scale * RecipeSpacingResource), border.Padding);
            }
        });
    }

    [Fact]
    public Task LayoutRecipe_SecondaryContentLeavesNoGapOrReservedSpaceWhenHidden()
    {
        return RunRecipeAsync("ResponsiveSecondaryContent", (window, component) =>
        {
            var stack = Assert.IsType<StackPanel>(component.Child);
            var primary = Assert.IsType<Border>(stack.Children[0]);
            var secondary = Assert.IsType<Border>(stack.Children[1]);
            foreach (var (width, expanded) in new[] { (1023d, false), (1024d, true), (1023d, false), (1024d, true) })
            {
                ResizeRecipeWindow(window, width);
                Assert.True(primary.IsVisible);
                Assert.Equal(expanded, secondary.IsVisible);
                Assert.Equal(stack.Bounds.Width, primary.Bounds.Width);
                var gap = expanded ? 4d * RecipeSpacingResource : 0d;
                Assert.Equal(gap, stack.Spacing);
                Assert.Equal(primary.Height + (expanded ? secondary.Height + gap : 0d), stack.DesiredSize.Height);
                if (expanded)
                {
                    Assert.Equal(primary.Bounds.Bottom + gap, secondary.Bounds.Y);
                }
            }
        });
    }

    private static async Task RunRecipeAsync(string filename, Action<Window, AkburaControl> verify)
    {
        using var sourceStream = typeof(BlackSilenceGeneratedBehaviorTests).Assembly.GetManifestResourceStream(
            "Akbura.UnitTests.LayoutRecipes." + filename + ".akbura");
        Assert.NotNull(sourceStream);
        using var reader = new StreamReader(sourceStream);
        var source = SourceText.From(reader.ReadToEnd());
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        var constructor = $$"""
            public partial class {{filename}}
            {
                public {{filename}}() : base(global::Akbura.Engine.AkburaEngine.Empty) { }
            }
            """;
        var compilation = CSharpCompilation.Create("AkburaLayoutRecipe_" + filename,
            [CSharpSyntaxTree.ParseText(constructor, parseOptions)], SymbolTests.CreateAvaloniaReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var path = Path.Combine(Environment.CurrentDirectory, filename + ".akbura");
        var driver = CreateDriver([new TestAdditionalText(path, source)], parseOptions);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var generated, out var diagnostics);
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        using var image = new MemoryStream();
        var emitted = generated.Emit(image);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        var componentType = Assembly.Load(image.ToArray()).GetType(filename, throwOnError: true)!;

        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            var component = Assert.IsAssignableFrom<AkburaControl>(Activator.CreateInstance(componentType));
            var window = new Window { Width = 767d, Height = 480d, Content = component };
            window.Resources["--spacing"] = RecipeSpacingResource;
            window.Show();
            try
            {
                ResizeRecipeWindow(window, 767d);
                verify(window, component);
            }
            finally
            {
                window.Close();
                Dispatcher.UIThread.RunJobs();
            }
        }, CancellationToken.None);
    }

    private static void ResizeRecipeWindow(Window window, double width)
    {
        window.Width = width;
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(width, window.ClientSize.Width);
    }
}
