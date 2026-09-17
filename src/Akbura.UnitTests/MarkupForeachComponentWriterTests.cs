using Avalonia.Controls;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace Akbura.UnitTests;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class MarkupForeachComponentWriterTests
{
    [Fact]
    public void StructuralForeach_UsesMembershipSignalAndKeepsLayoutRecoveryGuard()
    {
        var fixture = AkcssActivatorPlannerTests.CreateFixture(
            "using Avalonia.Controls; using Demo; " +
            """
            <StackPanel>
                $foreach (var item in Items)
                {
                    <TextBlock Text={item.ToString()} />
                }
            </StackPanel>
            """,
            OwnerSource);
        var root = fixture.ComponentTree.GetRoot();
        var component = Assert.IsType<Akbura.Language.Symbols.IAkburaComponentSymbol>(
            fixture.SemanticModel.GetSymbolInfo(root).Symbol, exactMatch: false);
        var generated = Akbura.Language.CodeGeneration.ComponentDocumentWriter.Generate(
            component,
            fixture.SemanticModel,
            "Views/PlannerView.akbura",
            new System.Collections.Generic.Dictionary<Akbura.Language.Syntax.AkburaSyntax, string>(),
            mode: Akbura.Language.CodeGeneration.ComponentGenerationMode.DebugStructural);

        var generatedText = generated.ToString();

        Assert.Contains(
            "__conditionalChanged0 |= __foreachRegion0.ChildrenChanged;",
            generatedText,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "__conditionalChanged0 = true;",
            generatedText,
            StringComparison.Ordinal);

        Assert.Contains(
            ".IsCollectionLayoutCurrent(",
            generatedText,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnchangedForeach_RepairsExternallyInterleavedTargetLayout()
    {
        var type = MarkupConditionalComponentWriterTests.Compile(
            """
            <StackPanel>
                $foreach (var item in Items)
                {
                    <TextBlock Text={item.ToString()} />
                }
            </StackPanel>
            """,
            structural: true,
            ownerSource: OwnerSource);

        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            var owner = Create(type);
            var panel = Assert.IsType<StackPanel>(Invoke(owner, "FirstForTest"));
            var generated = panel.Children.ToArray();
            var foreign = new Border();

            panel.Children.Insert(2, foreign);

            Invoke(owner, "UpdateForTest");

            Assert.Equal(generated.Length + 1, panel.Children.Count);
            for (var i = 0; i < generated.Length; i++)
            {
                Assert.Same(generated[i], panel.Children[i]);
            }

            Assert.Same(foreign, panel.Children[^1]);
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MixedGuards_UseSourceIndexAndPreserveSiblingsAndMovedOccurrences(bool structural)
    {
        var type = MarkupConditionalComponentWriterTests.Compile(
            """
            <StackPanel>
                <TextBlock Text="Starting loop" />
                $foreach (var item in Items)
                {
                    if (item % 2 == 0) { continue; }
                    if (item % 3 == 0) { break; }
                    <CountingTextBlock Text={$"{item}:{@index}"} />
                }
                <TextBlock Text="Ending loop" />
                $if (Items.Count > 10) { <TextBlock Text="Many items" /> }
            </StackPanel>
            """, structural, OwnerSource);

        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            var owner = Create(type);
            var panel = Assert.IsType<StackPanel>(Invoke(owner, "FirstForTest"));
            AssertTexts(panel, "Starting loop", "1:0", "Ending loop");
            var first = panel.Children[1];
            var footer = panel.Children[2];
            Assert.Equal(1, Constructed(type));

            var items = Read<ObservableCollection<int>>(owner, "Items");
            items.Add(7); // Beyond the stopping item: metadata only, no body.
            Invoke(owner, "UpdateForTest");
            Assert.Equal(1, Constructed(type));
            Assert.Same(first, panel.Children[1]);
            Assert.Same(footer, panel.Children[2]);

            items.Remove(3);
            Invoke(owner, "UpdateForTest");
            AssertTexts(panel, "Starting loop", "1:0", "5:3", "7:4", "Ending loop");
            Assert.Same(first, panel.Children[1]);
            Assert.Same(footer, panel.Children[4]);
            var five = panel.Children[2];
            items.Move(3, 0);
            Invoke(owner, "UpdateForTest");
            AssertTexts(panel, "Starting loop", "5:0", "1:1", "7:4", "Ending loop");
            Assert.Same(five, panel.Children[1]);
            Assert.Same(first, panel.Children[2]);
            Assert.Equal(3, Constructed(type));
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MultipleRoots_KeepDuplicateOccurrencesIndependentAndUpdateLocalVariables(bool structural)
    {
        var type = MarkupConditionalComponentWriterTests.Compile(
            """
            <StackPanel>
                $foreach (var item in Items)
                {
                    var label = $"{item}:{index}";
                    <CountingTextBlock Text={label} />
                    <TextBlock Text={$"tail:{item}"} />
                }
                <TextBlock Text="Footer" />
            </StackPanel>
            """, structural, OwnerSource);
        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            var owner = Create(type);
            var items = Read<ObservableCollection<int>>(owner, "Items");
            items.Clear();
            items.Add(1);
            items.Add(1);
            var panel = Assert.IsType<StackPanel>(Invoke(owner, "FirstForTest"));
            AssertTexts(panel, "1:0", "tail:1", "1:1", "tail:1", "Footer");
            var first = panel.Children[0];
            var second = panel.Children[2];
            Assert.NotSame(first, second);
            items.Move(1, 0);
            Invoke(owner, "UpdateForTest");
            Assert.Same(second, panel.Children[0]);
            Assert.Same(first, panel.Children[2]);
            AssertTexts(panel, "1:0", "tail:1", "1:1", "tail:1", "Footer");
            Assert.Equal(2, Constructed(type));
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SnapshotSource_BreakStopsMoveNextAndDisposesEnumerator(bool structural)
    {
        var type = MarkupConditionalComponentWriterTests.Compile(
            """
            <StackPanel>
                $foreach (var item in LazyItems)
                {
                    if (item == 3) { break; }
                    <CountingTextBlock Text={$"{item}"} />
                }
            </StackPanel>
            """, structural, OwnerSource);
        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            var owner = Create(type);
            var panel = Assert.IsType<StackPanel>(Invoke(owner, "FirstForTest"));
            AssertTexts(panel, "1", "2");
            Assert.Equal(3, Read<int>(owner, "Visited"));
            Assert.Equal(1, Read<int>(owner, "Disposed"));
            Assert.Equal(2, Constructed(type));
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplicitKeys_PreserveNodesAcrossSourceReplacement(bool structural)
    {
        var type = MarkupConditionalComponentWriterTests.Compile(
            """
            <StackPanel>
                $foreach (var item in Items; key: item)
                {
                    <CountingTextBlock Text={$"{item}:{index}"} />
                    <TextBlock Text="tail" />
                }
            </StackPanel>
            """, structural, OwnerSource);
        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            var owner = Create(type);
            var panel = Assert.IsType<StackPanel>(Invoke(owner, "FirstForTest"));
            var one = panel.Children[0];
            var five = panel.Children[8];
            type.GetProperty("Items")!.SetValue(owner, new ObservableCollection<int>([5, 1]));
            Invoke(owner, "UpdateForTest");
            AssertTexts(panel, "5:0", "tail", "1:1", "tail");
            Assert.Same(five, panel.Children[0]);
            Assert.Same(one, panel.Children[2]);
            Assert.Equal(5, Constructed(type));
        }, CancellationToken.None);
    }

    private static void AssertTexts(StackPanel panel, params string[] expected) =>
        Assert.Equal(expected, panel.Children.Cast<TextBlock>().Select(static x => x.Text));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NestedLoops_ContributeActualWidthsAndUseIndependentSourceIndices(bool structural)
    {
        var type = MarkupConditionalComponentWriterTests.Compile(
            """
            <StackPanel>
                $foreach (var item in Items)
                {
                    var outerIndex = @index;
                    if (item > 2) { break; }
                    $foreach (var inner in Items)
                    {
                        if (inner > 2) { break; }
                        <TextBlock Text={$"{item}:{outerIndex}/{inner}:{@index}"} />
                    }
                }
                <TextBlock Text="Footer" />
            </StackPanel>
            """, structural, OwnerSource);
        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            var owner = Create(type);
            var panel = Assert.IsType<StackPanel>(Invoke(owner, "FirstForTest"));
            AssertTexts(panel, "1:0/1:0", "1:0/2:1", "2:1/1:0", "2:1/2:1", "Footer");
            var first = panel.Children[0];
            var footer = panel.Children[4];
            Read<ObservableCollection<int>>(owner, "Items").RemoveAt(1);
            Invoke(owner, "UpdateForTest");
            AssertTexts(panel, "1:0/1:0", "Footer");
            Assert.Same(first, panel.Children[0]);
            Assert.Same(footer, panel.Children[1]);
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LoopInsideConditional_UsesActualWidthInsteadOfFiniteReservedSlots(bool structural)
    {
        var type = MarkupConditionalComponentWriterTests.Compile(
            """
            <StackPanel>
                <TextBlock Text="Header" />
                $if (Visible)
                {
                    $foreach (var item in Items) { <TextBlock Text={$"{item}"} /> }
                }
                <TextBlock Text="Footer" />
            </StackPanel>
            """, structural, OwnerSource);
        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            var owner = Create(type);
            var panel = Assert.IsType<StackPanel>(Invoke(owner, "FirstForTest"));
            AssertTexts(panel, "Header", "1", "2", "3", "4", "5", "Footer");
            var footer = panel.Children[^1];
            type.GetProperty("Visible")!.SetValue(owner, false);
            Invoke(owner, "UpdateForTest");
            AssertTexts(panel, "Header", "Footer");
            Assert.Same(footer, panel.Children[1]);
            type.GetProperty("Visible")!.SetValue(owner, true);
            Invoke(owner, "UpdateForTest");
            AssertTexts(panel, "Header", "1", "2", "3", "4", "5", "Footer");
            Assert.Same(footer, panel.Children[^1]);
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeclaredNumericConversion_PreservesObservableSource(bool structural)
    {
        var type = MarkupConditionalComponentWriterTests.Compile(
            """
            <StackPanel>
                $foreach (double item in Items) { <TextBlock Tag={item} /> }
            </StackPanel>
            """, structural, OwnerSource);
        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            var owner = Create(type);
            var panel = Assert.IsType<StackPanel>(Invoke(owner, "FirstForTest"));
            Assert.Equal(new[] { 1d, 2d, 3d, 4d, 5d }, panel.Children.Cast<TextBlock>().Select(static x => (double)x.Tag!));
            var first = panel.Children[0];
            Read<ObservableCollection<int>>(owner, "Items").Add(6);
            Invoke(owner, "UpdateForTest");
            Assert.Same(first, panel.Children[0]);
            Assert.Equal(6d, ((TextBlock)panel.Children[^1]).Tag);
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NonGenericSource_UsesDeclaredForeachCast(bool structural)
    {
        var type = MarkupConditionalComponentWriterTests.Compile(
            """
            <StackPanel>
                $foreach (int item in Untyped) { <TextBlock Tag={item} /> }
            </StackPanel>
            """, structural, OwnerSource);
        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            var owner = Create(type);
            var panel = Assert.IsType<StackPanel>(Invoke(owner, "FirstForTest"));
            Assert.Equal(new[] { 1, 2 }, panel.Children.Cast<TextBlock>().Select(static x => (int)x.Tag!));
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SourceItemType_IsIndependentOfNonControlDestinationItemType(bool structural)
    {
        var type = MarkupConditionalComponentWriterTests.Compile(
            """
            <NumberListOwner>
                $foreach (var item in Words) { {item.Length} }
            </NumberListOwner>
            """, structural, OwnerSource);
        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            var owner = Create(type);
            var root = Invoke(owner, "FirstForTest")!;
            Assert.Equal(new[] { 1, 2 }, Read<IList<int>>(root, "Values"));
        }, CancellationToken.None);
    }

    private static AkburaControl Create(Type type) => (AkburaControl)Activator.CreateInstance(type)!;
    private static T Read<T>(object owner, string name) => (T)owner.GetType().GetProperty(name)!.GetValue(owner)!;
    private static int Constructed(Type type) => (int)type.Assembly.GetType("Demo.CountingTextBlock")!.GetField("Constructed")!.GetValue(null)!;

    private static object? Invoke(object owner, string name)
    {
        try { return owner.GetType().GetMethod(name)!.Invoke(owner, null); }
        catch (TargetInvocationException exception) when (exception.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private const string OwnerSource =
        """
        namespace Demo;
        public partial class PlannerView : Akbura.AkburaControl
        {
            public PlannerView() : base(Akbura.Engine.AkburaEngine.Empty) { }
            public System.Collections.ObjectModel.ObservableCollection<int> Items { get; set; } = [1, 2, 3, 4, 5];
            public System.Collections.ObjectModel.ObservableCollection<string> Words { get; set; } = ["a", "bb"];
            public System.Collections.IEnumerable Untyped { get; } = new System.Collections.ArrayList { 1, 2 };
            public bool Visible { get; set; } = true;
            public int Visited { get; private set; }
            public int Disposed { get; private set; }
            public System.Collections.Generic.IEnumerable<int> LazyItems => Enumerate();
            private System.Collections.Generic.IEnumerable<int> Enumerate()
            {
                try
                {
                    for (var i = 1; i <= 5; i++) { Visited++; yield return i; }
                }
                finally { Disposed++; }
            }
            public Avalonia.Controls.Control FirstForTest() => FirstUpdate();
            public Avalonia.Controls.Control UpdateForTest() => Update();
        }
        public sealed class CountingTextBlock : Avalonia.Controls.TextBlock
        {
            public static int Constructed;
            public CountingTextBlock() { Constructed++; }
        }
        public sealed class NumberListOwner : Avalonia.Controls.Control
        {
            [Avalonia.Metadata.Content]
            public System.Collections.Generic.IList<int> Values { get; } = new System.Collections.Generic.List<int>();
        }
        """;
}
