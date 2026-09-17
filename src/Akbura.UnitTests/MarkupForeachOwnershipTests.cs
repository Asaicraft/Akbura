using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using System.Collections;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace Akbura.UnitTests;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class MarkupForeachOwnershipTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ForwardElementNameAcrossRoots_UsesEachIterationScopeAndReleasesRemovedBindings(bool structural)
    {
        var type = Compile(
            """
            <StackPanel>
                <TextBlock Text="prefix" />
                $foreach (var item in Items; key: item.Id)
                {
                    <TextBlock Text=${ReflectionBinding Path=Text, ElementName={SourceName}} />
                    <TextBox x.Name="source" Text={item.Name} />
                }
                <TextBlock Text="suffix" />
            </StackPanel>
            """, structural);
        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            var owner = Create(type);
            var window = new Window { Content = owner };
            try
            {
                window.Show();
                var panel = Assert.IsType<StackPanel>(owner.Child);
                var prefix = panel.Children[0];
                var suffix = panel.Children[5];
                var firstTarget = Assert.IsType<TextBlock>(panel.Children[1]);
                var firstSource = Assert.IsType<TextBox>(panel.Children[2]);
                var secondTarget = Assert.IsType<TextBlock>(panel.Children[3]);
                var secondSource = Assert.IsType<TextBox>(panel.Children[4]);
                Assert.Equal("one", firstTarget.Text);
                Assert.Equal("two", secondTarget.Text);

                firstSource.Text = "first live";
                Assert.Equal("first live", firstTarget.Text);
                Assert.Equal("two", secondTarget.Text);
                secondSource.Text = "second live";
                Assert.Equal("second live", secondTarget.Text);
                Assert.Equal("first live", firstTarget.Text);

                Invoke(Read<object>(owner, "Items"), "Move", 1, 0);
                owner.InvalidState();
                Assert.Same(secondTarget, panel.Children[1]);
                Assert.Same(secondSource, panel.Children[2]);
                Assert.Same(firstTarget, panel.Children[3]);
                Assert.Same(firstSource, panel.Children[4]);
                Assert.Equal("two", secondTarget.Text);
                Assert.Equal("one", firstTarget.Text);
                secondSource.Text = "moved live";
                Assert.Equal("moved live", secondTarget.Text);
                Assert.Equal("one", firstTarget.Text);

                Read<IList>(owner, "Items").RemoveAt(1);
                owner.InvalidState();
                Assert.Equal(4, panel.Children.Count);
                Assert.Same(prefix, panel.Children[0]);
                Assert.Same(suffix, panel.Children[3]);
                Assert.Same(secondTarget, panel.Children[1]);
                Assert.Null(firstSource.GetVisualParent());
                var inactiveText = firstTarget.Text;
                firstSource.Text = "removed source";
                Assert.Equal(inactiveText, firstTarget.Text);
                secondSource.Text = "remaining live";
                Assert.Equal("remaining live", secondTarget.Text);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task KeyedMoveAndReplace_RefreshItemIndexCapturesWithoutDuplicateOrDetachedHandlers(bool structural)
    {
        var type = Compile(
            """
            <StackPanel>
                <TextBlock Text="prefix" />
                $foreach (var item in Items; key: item.Id)
                {
                    <CountingButton Content={$"{item.Name}:{@index}"} Click={() => Record(item, @index)} />
                }
                <TextBlock Text="suffix" />
            </StackPanel>
            """, structural);
        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            var owner = Create(type);
            var panel = Assert.IsType<StackPanel>(Invoke(owner, "FirstForTest"));
            var items = Read<IList>(owner, "Items");
            var original = items[0];
            var first = Assert.IsAssignableFrom<Button>(panel.Children[1]);
            var second = Assert.IsAssignableFrom<Button>(panel.Children[2]);
            Click(first);
            Assert.Equal(1, Read<int>(owner, "Calls"));
            Assert.Same(original, Read<object>(owner, "LastItem"));
            Assert.Equal(0, Read<int>(owner, "LastIndex"));

            Invoke(items, "Move", 0, 1);
            Invoke(owner, "UpdateForTest");
            Assert.Same(second, panel.Children[1]);
            Assert.Same(first, panel.Children[2]);
            Assert.Equal("one:1", first.Content);
            Click(first);
            Assert.Equal(2, Read<int>(owner, "Calls"));
            Assert.Same(original, Read<object>(owner, "LastItem"));
            Assert.Equal(1, Read<int>(owner, "LastIndex"));

            var replacement = NewItem(type, 1, "replacement");
            items[1] = replacement;
            Invoke(owner, "UpdateForTest");
            Assert.Same(first, panel.Children[2]);
            Assert.Equal("replacement:1", first.Content);
            Click(first);
            Assert.Equal(3, Read<int>(owner, "Calls"));
            Assert.Same(replacement, Read<object>(owner, "LastItem"));
            Assert.Equal(1, Read<int>(owner, "LastIndex"));
            Assert.Equal(2, Constructed(type));

            for (var i = 0; i < 3; i++)
            {
                Invoke(owner, "UpdateForTest");
            }

            Click(first);
            Assert.Equal(4, Read<int>(owner, "Calls"));
            items.RemoveAt(1);
            Invoke(owner, "UpdateForTest");
            Assert.Equal(3, panel.Children.Count);
            Assert.Same(second, panel.Children[1]);
            Click(first);
            Assert.Equal(4, Read<int>(owner, "Calls"));
            Click(second);
            Assert.Equal(5, Read<int>(owner, "Calls"));
            Assert.Same(items[0], Read<object>(owner, "LastItem"));
            Assert.Equal(0, Read<int>(owner, "LastIndex"));
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    public async Task InvalidDestination_IsRejectedBeforeSourceEvaluationAndChildConstruction(bool structural, int mode)
    {
        var type = Compile(
            """
            <IndexedOwner>
                $foreach (var item in GetItems())
                {
                    <CountingButton Content={Format(item)} />
                }
            </IndexedOwner>
            """, structural);
        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            type.Assembly.GetType("Demo.IndexedOwner")!.GetProperty("ModeForTest")!.SetValue(null, mode);
            var owner = Create(type);
            if (mode == 2)
            {
                Assert.Throws<ArgumentNullException>(() => Invoke(owner, "FirstForTest"));
            }
            else
            {
                Assert.Throws<InvalidOperationException>(() => Invoke(owner, "FirstForTest"));
            }

            Assert.Equal(0, Read<int>(owner, "SourceEvaluations"));
            Assert.Equal(0, Read<int>(owner, "BodyEvaluations"));
            Assert.Equal(0, Constructed(type));
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedSourceReplacement_RetainsAppliedRootsAndRetriesTheNewSource(bool structural)
    {
        var type = Compile(
            """
            <StackPanel>
                <TextBlock Text="prefix" />
                $foreach (var item in Items; key: item.Id)
                {
                    <CountingButton Content={Format(item)} Click={() => Record(item, @index)} />
                }
                <TextBlock Text="suffix" />
            </StackPanel>
            """, structural);
        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            var owner = Create(type);
            var panel = Assert.IsType<StackPanel>(Invoke(owner, "FirstForTest"));
            var appliedRoots = panel.Children.ToArray();
            var oldItems = Read<IList>(owner, "Items");
            Invoke(owner, "ReplaceSourceForTest");
            Set(owner, "FailForTest", true);
            Assert.Throws<InvalidOperationException>(() => Invoke(owner, "UpdateForTest"));
            Assert.Equal(appliedRoots.Length, panel.Children.Count);
            for (var i = 0; i < appliedRoots.Length; i++)
            {
                Assert.Same(appliedRoots[i], panel.Children[i]);
            }

            Assert.Equal(3, Read<IList>(owner, "Items").Count);
            Set(owner, "FailForTest", false);
            Assert.Same(panel, Invoke(owner, "UpdateForTest"));
            Assert.Equal(5, panel.Children.Count);
            Assert.Same(appliedRoots[0], panel.Children[0]);
            Assert.Same(appliedRoots[2], panel.Children[1]);
            Assert.Same(appliedRoots[1], panel.Children[2]);
            Assert.Same(appliedRoots[3], panel.Children[4]);
            Assert.Equal(new[] { "two replaced", "one replaced", "three" },
                panel.Children.OfType<Button>().Select(static button => button.Content));
            Assert.Equal(3, Constructed(type));
            Click(Assert.IsAssignableFrom<Button>(panel.Children[2]));
            Assert.Same(Read<IList>(owner, "Items")[1], Read<object>(owner, "LastItem"));
            Assert.Equal(1, Read<int>(owner, "LastIndex"));

            oldItems.Clear();
            Invoke(owner, "UpdateForTest");
            Assert.Equal(5, panel.Children.Count);
            Assert.Same(appliedRoots[2], panel.Children[1]);
            Assert.Same(appliedRoots[1], panel.Children[2]);
            Assert.Equal(3, Constructed(type));
        }, CancellationToken.None);
    }

    private static Type Compile(string markup, bool structural) =>
        MarkupConditionalComponentWriterTests.Compile(markup, structural, OwnerSource);

    private static AkburaControl Create(Type type) =>
        Assert.IsAssignableFrom<AkburaControl>(Activator.CreateInstance(type));

    private static object NewItem(Type type, int id, string name) =>
        Activator.CreateInstance(type.Assembly.GetType("Demo.Item")!, id, name)!;

    private static int Constructed(Type type) =>
        (int)type.Assembly.GetType("Demo.CountingButton")!.GetField("Constructed")!.GetValue(null)!;

    private static T Read<T>(object target, string name) =>
        (T)target.GetType().GetProperty(name)!.GetValue(target)!;

    private static void Set(object target, string name, object value) =>
        target.GetType().GetProperty(name)!.SetValue(target, value);

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static object? Invoke(object target, string name, params object[] arguments)
    {
        try
        {
            return target.GetType().GetMethod(name)!.Invoke(target, arguments);
        }
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
            public System.Collections.ObjectModel.ObservableCollection<Item> Items { get; set; } =
                [new Item(1, "one"), new Item(2, "two")];
            public string SourceName => "source";
            public int Calls { get; private set; }
            public Item? LastItem { get; private set; }
            public int LastIndex { get; private set; } = -1;
            public int SourceEvaluations { get; private set; }
            public int BodyEvaluations { get; private set; }
            public bool FailForTest { get; set; }
            public void Record(Item item, int index) { Calls++; LastItem = item; LastIndex = index; }
            public System.Collections.Generic.IEnumerable<Item> GetItems() { SourceEvaluations++; return Items; }
            public string Format(Item item)
            {
                BodyEvaluations++;
                if (FailForTest && item.Id == 1) { throw new System.InvalidOperationException("body failure"); }
                return item.Name;
            }
            public void ReplaceSourceForTest() => Items =
                [new Item(2, "two replaced"), new Item(1, "one replaced"), new Item(3, "three")];
            public Avalonia.Controls.Control FirstForTest() => FirstUpdate();
            public Avalonia.Controls.Control UpdateForTest() => Update();
        }
        public sealed class Item
        {
            public Item(int id, string name) { Id = id; Name = name; }
            public int Id { get; }
            public string Name { get; }
        }
        public sealed class CountingButton : Avalonia.Controls.Button
        {
            public static int Constructed;
            public CountingButton() { Constructed++; }
        }
        public sealed class IndexedOwner : Avalonia.Controls.Control
        {
            public static int ModeForTest { get; set; }
            [Avalonia.Metadata.Content]
            public System.Collections.Generic.IList<Avalonia.Controls.Control> Values { get; } =
                ModeForTest == 0
                    ? new System.Collections.ObjectModel.ReadOnlyCollection<Avalonia.Controls.Control>([])
                    : ModeForTest == 1 ? new Avalonia.Controls.Control[1] : null!;
        }
        """;
}
