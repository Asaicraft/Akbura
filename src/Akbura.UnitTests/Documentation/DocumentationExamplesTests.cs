using Akbura.Language.Syntax;
using Akbura.TestUtilities.Documentation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using System.Collections.Immutable;
using System.Collections.ObjectModel;

namespace Akbura.UnitTests;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class DocumentationExamplesTests
{
    public static IEnumerable<object[]> Examples() => DocumentationExampleCatalog.PositiveModes();

    [Theory]
    [MemberData(nameof(Examples))]
    public async Task DocumentedExample_CompilesEmitsAndRendersExpectedOutput(string id, bool structural)
    {
        var example = DocumentationCompiler.Compile(id, structural);
        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            using var view = new DocumentationView(example);
            AssertInitialOutput(id, view);
        }, CancellationToken.None);
    }

    [Fact]
    public void DocumentedInvalidBorder_ReportsTheDestinationDiagnosticAtTheForeach()
    {
        var example = DocumentationExampleCatalog.Get("foreach.invalid-border");
        var fixture = DocumentationCompiler.CreateCompilation(example);
        var root = fixture.Tree.GetRoot();
        Assert.Empty(root.GetDiagnostics());
        var loop = Assert.Single(root.DescendantNodes().OfType<MarkupForeachStatementSyntax>());
        var diagnostics = fixture.SemanticModel.GetSemanticDiagnostics(root);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == example.ExpectedDiagnostic &&
            ReferenceEquals(diagnostic.Syntax, loop));
        // Do not replace Border with StackPanel or assert an arbitrary compilation failure.
        Assert.Contains("<Border>", fixture.Block.Code, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BasicForeach_HasDirectVisualAndLogicalChildrenAndDoesNotSetItemDataContext(bool structural)
    {
        var example = DocumentationCompiler.Compile("foreach.basic", structural);
        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            var inheritedContext = new object();
            using var view = new DocumentationView(example, inheritedContext);
            var panel = Assert.IsType<StackPanel>(view.Root);
            Assert.Equal(5, panel.Children.Count);
            DocumentationView.AssertDirectParents(panel);
            Assert.All(panel.Children, child => Assert.Same(inheritedContext, child.DataContext));
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ObservableExample_ClicksUpdateTheLoopAndNeighboringIfWithoutManualRender(bool structural)
    {
        var example = DocumentationCompiler.Compile("foreach.observable", structural);
        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            using var view = new DocumentationView(example);
            var panel = Assert.IsType<StackPanel>(view.Root);
            var button = Assert.IsType<Button>(panel.Children[0]);
            var prefix = panel.Children[1];
            var originalItems = panel.Children.Skip(2).Take(5).ToArray();
            var footer = panel.Children[7];
            var source = view.Value<ObservableCollection<int>>("array");

            for (var i = 0; i < 6; i++) view.Click(button);

            Assert.Same(source, view.Value<ObservableCollection<int>>("array"));
            Assert.Equal(11, source.Count);
            Assert.Same(panel, view.Root);
            Assert.Same(button, panel.Children[0]);
            Assert.Same(prefix, panel.Children[1]);
            for (var i = 0; i < originalItems.Length; i++) Assert.Same(originalItems[i], panel.Children[2 + i]);
            Assert.Same(footer, panel.Children[13]);
            Assert.Equal("A lot of items!", Assert.IsType<TextBlock>(panel.Children[14]).Text);
            DocumentationView.AssertDirectText(panel,
                new[] { "Starting loop" }.Concat(Enumerable.Range(1, 11)
                    .Select(item => $"Current item is {item}"))
                    .Concat(new[] { "Ending loop", "A lot of items!" }).ToArray());

            source.RemoveAt(10);
            view.Flush();
            Assert.Same(footer, panel.Children[^1]);
            Assert.DoesNotContain(panel.Children.OfType<TextBlock>(), text => text.Text == "A lot of items!");
            Assert.Equal(13, panel.Children.Count);
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ObservableExample_MoveReplaceResetAndDuplicateOccurrencesKeepCorrectChildren(bool structural)
    {
        var example = DocumentationCompiler.Compile("foreach.observable", structural);
        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            using var view = new DocumentationView(example);
            var panel = Assert.IsType<StackPanel>(view.Root);
            var source = view.Value<ObservableCollection<int>>("array");
            var prefix = panel.Children.Take(2).ToArray();
            var footer = panel.Children[^1];
            var first = panel.Children[2];
            var second = panel.Children[3];

            source.Move(0, 4);
            view.Flush();
            Assert.Same(first, panel.Children[6]);
            Assert.Same(second, panel.Children[2]);
            DocumentationView.AssertDirectText(panel, "Starting loop", "Current item is 2", "Current item is 3",
                "Current item is 4", "Current item is 5", "Current item is 1", "Ending loop");

            source[0] = 9;
            view.Flush();
            Assert.Equal("Current item is 9", Assert.IsType<TextBlock>(panel.Children[2]).Text);
            Assert.Same(first, panel.Children[6]);
            Assert.Same(footer, panel.Children[^1]);

            source.Clear(); // ObservableCollection raises Reset here.
            view.Flush();
            Assert.Equal(3, panel.Children.Count);
            Assert.Same(prefix[0], panel.Children[0]);
            Assert.Same(prefix[1], panel.Children[1]);
            Assert.Same(footer, panel.Children[2]);

            source.Add(7);
            source.Add(7);
            source.Add(7);
            view.Flush();
            var duplicates = panel.Children.Skip(2).Take(3).ToArray();
            Assert.NotSame(duplicates[0], duplicates[1]);
            Assert.NotSame(duplicates[1], duplicates[2]);
            Assert.NotSame(duplicates[0], duplicates[2]);
            source.Move(0, 2);
            view.Flush();
            Assert.Same(duplicates[1], panel.Children[2]);
            Assert.Same(duplicates[2], panel.Children[3]);
            Assert.Same(duplicates[0], panel.Children[4]);
            Assert.Same(footer, panel.Children[5]);
            DocumentationView.AssertDirectParents(panel);
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ObservableExample_UsesReplacementSourceAndIgnoresChangesToTheOldSource(bool structural)
    {
        var example = DocumentationCompiler.Compile("foreach.observable", structural);
        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            using var view = new DocumentationView(example);
            var panel = Assert.IsType<StackPanel>(view.Root);
            var previous = view.Value<ObservableCollection<int>>("array");
            var replacement = new ObservableCollection<int> { 8, 9 };
            view.SetValue("array", replacement);
            DocumentationView.AssertDirectText(panel, "Starting loop", "Current item is 8", "Current item is 9", "Ending loop");
            var retained = panel.Children.ToArray();

            previous.Add(100);
            view.Flush();
            Assert.Equal(retained.Length, panel.Children.Count);
            for (var i = 0; i < retained.Length; i++) Assert.Same(retained[i], panel.Children[i]);

            replacement.Add(10);
            view.Flush();
            DocumentationView.AssertDirectText(panel, "Starting loop", "Current item is 8", "Current item is 9",
                "Current item is 10", "Ending loop");
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ImmutableExample_ClickAssignsNewStateAndPreservesExistingKeyedControls(bool structural)
    {
        var example = DocumentationCompiler.Compile("foreach.immutable", structural);
        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            using var view = new DocumentationView(example);
            var panel = Assert.IsType<StackPanel>(view.Root);
            var originals = panel.Children.ToArray();
            var previous = view.Value<ImmutableArray<int>>("array");
            view.Click(Assert.IsType<Button>(panel.Children[0]));
            var updated = view.Value<ImmutableArray<int>>("array");
            Assert.False(previous.Equals(updated));
            Assert.Equal(new[] { 1, 2, 3, 4 }, updated.ToArray());
            for (var i = 0; i < originals.Length; i++) Assert.Same(originals[i], panel.Children[i]);
            DocumentationView.AssertDirectText(panel, "Item 1", "Item 2", "Item 3", "Item 4");
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData("foreach.keys", false)]
    [InlineData("foreach.keys", true)]
    [InlineData("foreach.x-id", false)]
    [InlineData("foreach.x-id", true)]
    public async Task KeyExamples_ReorderingImmutableStateRetainsEachIteration(string id, bool structural)
    {
        var example = DocumentationCompiler.Compile(id, structural);
        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            using var view = new DocumentationView(example);
            var panel = Assert.IsType<StackPanel>(view.Root);
            var previous = panel.Children.ToArray();
            var width = id == "foreach.keys" ? 2 : 1;
            view.SetValue("array", ImmutableArray.Create(3, 1, 2));
            var order = new[] { 2, 0, 1 };
            Assert.Equal(previous.Length, panel.Children.Count);
            for (var item = 0; item < order.Length; item++)
            {
                for (var child = 0; child < width; child++)
                {
                    Assert.Same(previous[order[item] * width + child], panel.Children[item * width + child]);
                }
            }
            if (width == 2)
                DocumentationView.AssertDirectText(panel, "Item 3", "Square: 9", "Item 1", "Square: 1", "Item 2", "Square: 4");
            else
                DocumentationView.AssertDirectText(panel, "Item 3", "Item 1", "Item 2");
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GuardExample_RemovingTheBreakingItemReachesTheSuffixAndPreservesSiblings(bool structural)
    {
        var example = DocumentationCompiler.Compile("foreach.guards", structural);
        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            using var view = new DocumentationView(example);
            var panel = Assert.IsType<StackPanel>(view.Root);
            var previous = panel.Children.ToArray();
            var source = view.Value<ObservableCollection<int>>("array");
            Assert.True(source.Remove(3));
            view.Flush();
            DocumentationView.AssertDirectText(panel, "Starting loop", "Item 1, doubled 2, index 0",
                "Item 5, doubled 10, index 3", "Ending loop");
            Assert.Same(previous[0], panel.Children[0]);
            Assert.Same(previous[1], panel.Children[1]);
            Assert.Same(previous[2], panel.Children[3]);
            var five = panel.Children[2];

            source.Move(3, 0);
            view.Flush();
            DocumentationView.AssertDirectText(panel, "Starting loop", "Item 5, doubled 10, index 0",
                "Item 1, doubled 2, index 1", "Ending loop");
            Assert.Same(five, panel.Children[1]);
            Assert.Same(previous[1], panel.Children[2]);
            Assert.Same(previous[2], panel.Children[3]);
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConditionalExample_TogglePreservesTheButtonButUnmountsTheOldAlternative(bool structural)
    {
        var example = DocumentationCompiler.Compile("conditional.toggle", structural);
        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            using var view = new DocumentationView(example);
            var panel = Assert.IsType<StackPanel>(view.Root);
            var button = Assert.IsType<Button>(panel.Children[0]);
            var originalAlternative = panel.Children[1];
            view.Click(button);
            DocumentationView.AssertDirectText(panel, "Details are visible.");
            Assert.Same(button, panel.Children[0]);
            Assert.NotSame(originalAlternative, panel.Children[1]);
            view.Click(button);
            DocumentationView.AssertDirectText(panel, "Open the details to continue.");
            Assert.Same(button, panel.Children[0]);
            Assert.NotSame(originalAlternative, panel.Children[1]);
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ScalarConditionalExample_SwitchesTheNativeChild(bool structural)
    {
        var example = DocumentationCompiler.Compile("conditional.scalar", structural);
        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            using var view = new DocumentationView(example);
            var border = Assert.IsType<Border>(view.Root);
            Assert.IsType<Button>(border.Child);
            view.SetValue("expanded", true);
            Assert.Same(border, view.Root);
            Assert.Equal("Expanded", Assert.IsType<TextBlock>(border.Child).Text);
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RootTemplateExample_SelectsTextButtonAndEmptyContentInItsNativeHost(bool structural)
    {
        var example = DocumentationCompiler.Compile("conditional.root-template", structural);
        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            using var view = new DocumentationView(example);
            var host = Assert.IsType<ContentControl>(view.Root);
            Assert.Contains(host.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Ada");
            view.SetObjectValue("model", view.CreatePerson("Grace", selected: false, canEdit: true));
            Assert.Same(host, view.Root);
            Assert.Equal("Edit", Assert.Single(host.GetVisualDescendants().OfType<Button>()).Content);
            Assert.DoesNotContain(host.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Ada");

            view.SetObjectValue("model", view.CreatePerson("Nobody", selected: false, canEdit: false));
            Assert.Empty(host.GetVisualDescendants().OfType<Button>());
            Assert.DoesNotContain(host.GetVisualDescendants().OfType<TextBlock>(), text =>
                text.Text is "Ada" or "Grace" or "Nobody" or "Edit");
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task QuickStartCounter_ClickUsesTheRealStateAndUpdatesTheExistingTextBlock(bool structural)
    {
        var example = DocumentationCompiler.Compile("main.counter", structural);
        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            using var view = new DocumentationView(example);
            var panel = Assert.IsType<StackPanel>(view.Root);
            var text = Assert.IsType<TextBlock>(panel.Children[0]);
            var button = Assert.IsType<Button>(panel.Children[1]);
            view.Click(button);
            view.Click(button);
            Assert.Equal(2, view.Value<int>("count"));
            Assert.Same(text, panel.Children[0]);
            Assert.Equal("Count: 2", text.Text);
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResourceExample_MovesTheNumberedBrushWithoutRemovingForeignEntries(bool structural)
    {
        var example = DocumentationCompiler.Compile("main.resources", structural);
        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            using var view = new DocumentationView(example);
            var panel = Assert.IsType<StackPanel>(view.Root);
            var accent = panel.Resources["AccentBrush"];
            var numbered = panel.Resources[1];
            var foreign = new SolidColorBrush(Colors.Black);
            panel.Resources.Add("foreign", foreign);
            view.Click(Assert.IsType<Button>(Assert.Single(panel.Children)));
            Assert.False(panel.Resources.ContainsKey(1));
            Assert.Same(numbered, panel.Resources[2]);
            Assert.Same(accent, panel.Resources["AccentBrush"]);
            Assert.Same(foreign, panel.Resources["foreign"]);
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StyleExample_ClickChangesAppliedBackgroundAndPreservesTheButton(bool structural)
    {
        var example = DocumentationCompiler.Compile("main.styles", structural);
        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            using var view = new DocumentationView(example);
            var panel = Assert.IsType<StackPanel>(view.Root);
            var button = Assert.IsType<Button>(Assert.Single(panel.Children));
            Assert.Equal(Colors.Blue, Assert.IsAssignableFrom<ISolidColorBrush>(button.Background).Color);
            view.Click(button);
            Assert.Same(button, Assert.Single(panel.Children));
            Assert.Equal(Colors.Red, Assert.IsAssignableFrom<ISolidColorBrush>(button.Background).Color);
            view.Click(button);
            Assert.Equal(Colors.Blue, Assert.IsAssignableFrom<ISolidColorBrush>(button.Background).Color);
        }, CancellationToken.None);
    }

    private static void AssertInitialOutput(string id, DocumentationView view)
    {
        if (id.StartsWith("grid.", StringComparison.Ordinal))
        {
            DocumentationGridExpectations.AssertDefinitions(id, Assert.IsType<Grid>(view.Root));
            return;
        }

        switch (id)
        {
            case "foreach.basic":
                var basic = Assert.IsType<StackPanel>(view.Root);
                Assert.Equal(8d, basic.Spacing);
                DocumentationView.AssertDirectText(basic, Enumerable.Range(1, 5)
                    .Select(item => $"Current item is {item}").ToArray());
                Assert.Equal(5, basic.Children.Count);
                DocumentationView.AssertDirectParents(basic);
                break;
            case "foreach.property-element":
                AssertOnlyText(view,
                    "Before the loop", "Item 1", "Item 2", "Item 3", "After the loop");
                break;
            case "foreach.observable":
                var observable = Assert.IsType<StackPanel>(view.Root);
                Assert.Equal("Add an item", Assert.IsType<Button>(observable.Children[0]).Content);
                DocumentationView.AssertDirectText(observable, "Starting loop", "Current item is 1",
                    "Current item is 2", "Current item is 3", "Current item is 4", "Current item is 5", "Ending loop");
                Assert.Equal(8, observable.Children.Count);
                break;
            case "foreach.immutable":
                var immutable = Assert.IsType<StackPanel>(view.Root);
                Assert.Equal("Add an item", Assert.IsType<Button>(immutable.Children[0]).Content);
                Assert.Equal(4, immutable.Children.Count);
                DocumentationView.AssertDirectText(immutable, "Item 1", "Item 2", "Item 3");
                break;
            case "foreach.keys":
                AssertOnlyText(view,
                    "Item 1", "Square: 1", "Item 2", "Square: 4", "Item 3", "Square: 9");
                break;
            case "foreach.x-id":
                AssertOnlyText(view, "Item 1", "Item 2", "Item 3");
                break;
            case "foreach.index":
                AssertOnlyText(view,
                    "Item 1, source index 0", "Item 3, source index 2", "Item 5, source index 4");
                break;
            case "foreach.guards":
                AssertOnlyText(view,
                    "Starting loop", "Item 1, doubled 2, index 0", "Ending loop");
                break;
            case "foreach.nested":
                var nested = Assert.IsType<StackPanel>(view.Root);
                DocumentationView.AssertDirectText(nested,
                    "Row 1 (0), column 10 (0)", "Row 1 (0), column 20 (1)",
                    "Row 2 (1), column 10 (0)", "Row 2 (1), column 20 (1)");
                Assert.Equal(4, nested.Children.Count);
                DocumentationView.AssertDirectParents(nested);
                break;
            case "conditional.toggle":
                var conditional = Assert.IsType<StackPanel>(view.Root);
                Assert.Equal("Toggle details", Assert.IsType<Button>(conditional.Children[0]).Content);
                Assert.Equal(2, conditional.Children.Count);
                DocumentationView.AssertDirectText(conditional, "Open the details to continue.");
                break;
            case "conditional.scalar":
                Assert.Equal("Expand", Assert.IsType<Button>(Assert.IsType<Border>(view.Root).Child).Content);
                break;
            case "conditional.item-template":
                var items = Assert.IsType<ItemsControl>(view.Root);
                Assert.NotNull(items.ItemTemplate);
                Assert.Single(items.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Ada");
                Assert.DoesNotContain(items.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Grace");
                break;
            case "conditional.root-template":
                var host = Assert.IsType<ContentControl>(view.Root);
                Assert.NotNull(host.ContentTemplate);
                Assert.Single(host.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Ada");
                Assert.Empty(host.GetVisualDescendants().OfType<Button>());
                break;
            case "conditional.pattern":
                AssertOnlyText(view, "Ada");
                break;
            case "main.counter":
                var counter = Assert.IsType<StackPanel>(view.Root);
                Assert.Equal(12d, counter.Spacing);
                Assert.Equal(2, counter.Children.Count);
                DocumentationView.AssertDirectText(counter, "Count: 0");
                Assert.Equal("Increment", Assert.IsType<Button>(counter.Children[1]).Content);
                break;
            case "main.component":
                var component = Assert.IsType<StackPanel>(view.Root);
                Assert.Equal(3, component.Children.Count);
                DocumentationView.AssertDirectText(component, "Dashboard");
                var border = Assert.IsType<Border>(component.Children[2]);
                Assert.False(border.IsVisible);
                view.Click(Assert.IsType<Button>(component.Children[1]));
                Assert.True(border.IsVisible);
                break;
            case "main.resources":
                var resources = Assert.IsType<StackPanel>(view.Root).Resources;
                Assert.Equal(2, resources.Count);
                Assert.Equal(Colors.Red, Assert.IsType<SolidColorBrush>(resources["AccentBrush"]).Color);
                Assert.Equal(Colors.Blue, Assert.IsType<SolidColorBrush>(resources[1]).Color);
                break;
            case "main.styles":
                var styledPanel = Assert.IsType<StackPanel>(view.Root);
                var style = Assert.IsType<Style>(Assert.Single(styledPanel.Styles));
                Assert.Single(style.Setters);
                Assert.Single(style.Children);
                var styledButton = Assert.IsType<Button>(Assert.Single(styledPanel.Children));
                Assert.Equal(Colors.Blue, Assert.IsAssignableFrom<ISolidColorBrush>(styledButton.Background).Color);
                break;
            default:
                throw new InvalidOperationException($"No initial runtime assertion has been registered for '{id}'.");
        }
    }

    private static void AssertOnlyText(DocumentationView view, params string[] expected)
    {
        var panel = Assert.IsType<StackPanel>(view.Root);
        Assert.Equal(expected.Length, panel.Children.Count);
        Assert.All(panel.Children, child => Assert.IsType<TextBlock>(child));
        DocumentationView.AssertDirectText(panel, expected);
        DocumentationView.AssertDirectParents(panel);
    }

}
