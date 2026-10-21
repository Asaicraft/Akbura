using Avalonia.Controls;
using Avalonia.Media;
using System.Collections;

namespace Akbura.UnitTests;

public sealed partial class DocumentationExamplesTests
{
    private static readonly Color Slate = Color.Parse("#334155");
    private static readonly Color Blue = Color.Parse("#3B82F6");
    private static readonly Color Red = Color.Parse("#EF4444");

    private static DocumentationView CreateDocumentationView(CompiledDocumentationExample example)
    {
        if (example.Example.Id != "expression-binding.injected") return new DocumentationView(example);
        var model = Companion(example, "ViewModel");
        // Injected properties are public component inputs. Supply the service
        // before attachment; do not inspect generated fields or invoke Update().
        return new DocumentationView(example, configure: owner => ((dynamic)owner).Vm = (dynamic)model);
    }

    private static object Companion(CompiledDocumentationExample example, string type, params object[] args) =>
        Activator.CreateInstance(example.Type.Assembly.GetType("Demo." + type, throwOnError: true)!, args)!;

    private static TextBlock FirstText(DocumentationView view) => view.Root is TextBlock text ? text :
        Assert.IsType<StackPanel>(view.Root).Children.OfType<TextBlock>().First();

    private static Button Button(DocumentationView view, string content) => Assert.Single(
        Assert.IsType<StackPanel>(view.Root).Children.OfType<Button>(), button => Equals(button.Content, content));

    private static Border[] Borders(DocumentationView view) => view.Root is Border border ? [border] :
        Assert.IsType<StackPanel>(view.Root).Children.OfType<Border>().ToArray();

    private static void Background(Border border, Color expected) =>
        Assert.Equal(expected, Assert.IsAssignableFrom<ISolidColorBrush>(border.Background).Color);

    private static void AssertExpressionInitialOutput(string id, DocumentationView view)
    {
        switch (id)
        {
            case "expression-binding.state":
                DocumentationView.AssertDirectText(Assert.IsType<StackPanel>(view.Root), "Count: 0", "Double: 0");
                break;
            case "expression-binding.ordinary-property":
                DocumentationView.AssertDirectText(Assert.IsType<StackPanel>(view.Root), "Ada", "Frame: 0");
                break;
            case "expression-binding.avalonia-property":
            case "expression-binding.injected":
            case "expression-binding.indexed":
                Assert.Equal("Ada", FirstText(view).Text);
                break;
            case "expression-binding.property":
                Assert.Equal("Hello, Ada", FirstText(view).Text);
                break;
            case "expression-binding.nested":
                Assert.Equal("Ada's profile", FirstText(view).Text);
                break;
            case "expression-binding.conditional":
                Assert.Equal("Primary", FirstText(view).Text);
                break;
            case "expression-binding.avalonia-source":
                Assert.Equal("Value: 25", FirstText(view).Text);
                break;
            case "expression-binding.conversion":
                Assert.Equal(12d, Assert.Single(Borders(view)).Width);
                break;
            case "expression-binding.utility-frame":
            case "expression-binding.utility-avalonia-binding":
            case "expression-binding.utility-reactive-expression":
            case "expression-binding.utility-binding-nested-path":
                Background(Assert.Single(Borders(view)), Slate);
                break;
            case "expression-binding.utility-comparison":
                Assert.Equal(3, Borders(view).Length);
                Assert.All(Borders(view), border => Background(border, Slate));
                Assert.Equal("Frame: 0", FirstText(view).Text);
                break;
            case "expression-binding.utility-cascade":
                Assert.Equal(2, Borders(view).Length);
                Assert.All(Borders(view), border => { Background(border, Slate); Assert.Equal(12d, border.Padding.Left); });
                break;
            default:
                throw new InvalidOperationException($"Missing expression-binding runtime contract: {id}");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DocumentationStateExpressions_UpdateBothResultsThroughTheDocumentedButton(bool structural)
    {
        var example = DocumentationCompiler.Compile("expression-binding.state", structural);
        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            using var view = new DocumentationView(example);
            view.Click(Button(view, "Increase"));
            view.Click(Button(view, "Increase"));
            DocumentationView.AssertDirectText(Assert.IsType<StackPanel>(view.Root), "Count: 2", "Double: 4");
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData("expression-binding.property", false, "Hello, Grace")]
    [InlineData("expression-binding.property", true, "Hello, Grace")]
    [InlineData("expression-binding.avalonia-property", false, "Grace")]
    [InlineData("expression-binding.avalonia-property", true, "Grace")]
    [InlineData("expression-binding.injected", false, "Grace")]
    [InlineData("expression-binding.injected", true, "Grace")]
    public async Task DocumentationObservableProperty_UpdatesTheRealTargetFromNotifications(string id, bool structural, string expected)
    {
        var example = DocumentationCompiler.Compile(id, structural);
        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            using var view = CreateDocumentationView(example);
            dynamic model = view.Root.DataContext!;
            model.Name = "Grace";
            view.Flush();
            Assert.Equal(expected, FirstText(view).Text);
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DocumentationOrdinaryProperty_WaitsForTheExplicitStateRefresh(bool structural)
    {
        var example = DocumentationCompiler.Compile("expression-binding.ordinary-property", structural);
        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            using var view = new DocumentationView(example);
            dynamic model = view.Root.DataContext!;
            model.Name = "Grace";
            view.Flush();
            DocumentationView.AssertDirectText((StackPanel)view.Root, "Ada", "Frame: 0");
            view.Click(Button(view, "Refresh"));
            DocumentationView.AssertDirectText((StackPanel)view.Root, "Grace", "Frame: 1");
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DocumentationNestedReceiver_ReconnectsAfterReplacementNullAndRestore(bool structural)
    {
        var example = DocumentationCompiler.Compile("expression-binding.nested", structural);
        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            using var view = new DocumentationView(example);
            dynamic model = view.Root.DataContext!;
            dynamic oldProfile = model.Profile;
            oldProfile.Name = "Renamed";
            Assert.Equal("Renamed", FirstText(view).Text);
            view.Click(Button(view, "Replace profile"));
            oldProfile.Name = "Retired";
            view.Flush();
            Assert.Equal("Grace's profile", FirstText(view).Text);
            view.Click(Button(view, "Clear profile"));
            Assert.Equal("Unknown", FirstText(view).Text);
            view.Click(Button(view, "Replace profile"));
            model.Profile.Name = "Restored";
            view.Flush();
            Assert.Equal("Restored", FirstText(view).Text);
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DocumentationIndexer_FollowsSelectionCollectionReplacementAndCurrentItem(bool structural)
    {
        var example = DocumentationCompiler.Compile("expression-binding.indexed", structural);
        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            using var view = new DocumentationView(example);
            dynamic model = view.Root.DataContext!;
            IList people = model.People;
            view.Click(Button(view, "Next person"));
            Assert.Equal("Grace", FirstText(view).Text);
            ((dynamic)people[0]!).Name = "Unselected";
            view.Flush();
            Assert.Equal("Grace", FirstText(view).Text);
            view.Click(Button(view, "Rename selected"));
            Assert.Equal("Renamed", FirstText(view).Text);
            dynamic retired = people[1]!;
            people[1] = Companion(example, "Person", "Replacement");
            view.Flush();
            Assert.Equal("Replacement", FirstText(view).Text);
            retired.Name = "Old item";
            view.Flush();
            Assert.Equal("Replacement", FirstText(view).Text);
            ((dynamic)people[1]!).Name = "Current item";
            view.Flush();
            Assert.Equal("Current item", FirstText(view).Text);
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DocumentationConditionalExpression_UsesTheCurrentBranch(bool structural)
    {
        var example = DocumentationCompiler.Compile("expression-binding.conditional", structural);
        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            using var view = new DocumentationView(example);
            dynamic model = view.Root.DataContext!;
            model.FallbackName = "Latest fallback";
            view.Flush();
            Assert.Equal("Primary", FirstText(view).Text);
            view.Click(Button(view, "Switch branch"));
            Assert.Equal("Latest fallback", FirstText(view).Text);
            model.PrimaryName = "Latest primary";
            view.Flush();
            Assert.Equal("Latest fallback", FirstText(view).Text);
            view.Click(Button(view, "Switch branch"));
            Assert.Equal("Latest primary", FirstText(view).Text);
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DocumentationAvaloniaSourceAndConversions_UseActualPropertyValues(bool structural)
    {
        var avalonia = DocumentationCompiler.Compile("expression-binding.avalonia-source", structural);
        var conversion = DocumentationCompiler.Compile("expression-binding.conversion", structural);
        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            using var source = new DocumentationView(avalonia);
            Assert.IsType<Slider>(source.Root.DataContext).Value = 40;
            source.Flush();
            Assert.Equal("Value: 40", FirstText(source).Text);
            source.Click(Button(source, "Change value"));
            Assert.Equal("Value: 45", FirstText(source).Text);
            using var converted = new DocumentationView(conversion);
            converted.Click(Button(converted, "Increase offset"));
            Assert.Equal(13d, Assert.Single(Borders(converted)).Width);
            ((dynamic)converted.Owner).Count = 5;
            converted.Flush();
            Assert.Equal(16d, Assert.Single(Borders(converted)).Width);
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DocumentationFrameUtility_ChangesOnlyOnTheSupportedRefreshPath(bool structural)
    {
        var example = DocumentationCompiler.Compile("expression-binding.utility-frame", structural);
        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            using var view = new DocumentationView(example);
            var target = Assert.Single(Borders(view));
            view.Click(Button(view, "Toggle selection"));
            Background(target, Slate);
            Assert.Equal("Frame: 0", FirstText(view).Text);
            view.Click(Button(view, "Refresh"));
            Background(target, Blue);
            view.Click(Button(view, "Toggle selection"));
            Background(target, Blue);
            view.Click(Button(view, "Refresh"));
            Background(target, Slate);
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData("expression-binding.utility-avalonia-binding", false)]
    [InlineData("expression-binding.utility-avalonia-binding", true)]
    [InlineData("expression-binding.utility-reactive-expression", false)]
    [InlineData("expression-binding.utility-reactive-expression", true)]
    public async Task DocumentationObservedUtilities_RestoreTheBackgroundWithoutComponentRendering(string id, bool structural)
    {
        var example = DocumentationCompiler.Compile(id, structural);
        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            using var view = new DocumentationView(example);
            dynamic model = view.Root.DataContext!;
            var target = Assert.IsType<Border>(view.Root);
            Background(target, Slate);
            model.IsSelected = true;
            view.Flush();
            Background(target, Blue);
            if (id.EndsWith("reactive-expression", StringComparison.Ordinal))
            {
                model.IsDisabled = true;
                view.Flush();
                Background(target, Slate);
                model.IsDisabled = false;
                view.Flush();
                Background(target, Blue);
            }
            model.IsSelected = false;
            view.Flush();
            Background(target, Slate);
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DocumentationBindingPath_UsesTheRealHolderAndReplacesItsNestedSource(bool structural)
    {
        var example = DocumentationCompiler.Compile("expression-binding.utility-binding-nested-path", structural);
        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            using var view = new DocumentationView(example);
            dynamic holder = view.Root.DataContext!;
            dynamic retired = holder.Vm;
            retired.IsSelected = true;
            view.Flush();
            Background((Border)view.Root, Blue);
            holder.Vm = (dynamic)Companion(example, "ViewModel");
            view.Flush();
            Background((Border)view.Root, Slate);
            retired.IsSelected = false;
            retired.IsSelected = true;
            view.Flush();
            Background((Border)view.Root, Slate);
            holder.Vm.IsSelected = true;
            view.Flush();
            Background((Border)view.Root, Blue);
            holder.Vm.IsSelected = false;
            view.Flush();
            Background((Border)view.Root, Slate);
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DocumentationUtilityComparison_OnlyTheFrameConditionWaitsForRefresh(bool structural)
    {
        var example = DocumentationCompiler.Compile("expression-binding.utility-comparison", structural);
        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            using var view = new DocumentationView(example);
            var targets = Borders(view);
            view.Click(Button(view, "Toggle selection"));
            Background(targets[0], Blue);
            Background(targets[1], Blue);
            Background(targets[2], Slate);
            Assert.Equal("Frame: 0", FirstText(view).Text);
            view.Click(Button(view, "Refresh ordinary condition"));
            Assert.All(targets, target => Background(target, Blue));
            view.Click(Button(view, "Toggle selection"));
            Background(targets[0], Slate);
            Background(targets[1], Slate);
            Background(targets[2], Blue);
            view.Click(Button(view, "Refresh ordinary condition"));
            Assert.All(targets, target => Background(target, Slate));
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DocumentationSharedCascade_RestoresBothMechanismsAndKeepsUnrelatedPadding(bool structural)
    {
        var example = DocumentationCompiler.Compile("expression-binding.utility-cascade", structural);
        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            using var view = new DocumentationView(example);
            dynamic model = view.Root.DataContext!;
            void Check(Color color)
            {
                view.Flush();
                Assert.All(Borders(view), target => { Background(target, color); Assert.Equal(12d, target.Padding.Left); });
            }
            Check(Slate);
            model.IsSelected = true; Check(Blue);
            model.HasError = true; Check(Red);
            model.HasError = false; Check(Blue);
            model.IsSelected = false; Check(Slate);
            model.HasError = true; Check(Red);
            model.HasError = false; Check(Slate);
        }, CancellationToken.None);
    }
}
