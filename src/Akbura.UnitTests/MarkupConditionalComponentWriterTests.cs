using Akbura.Language;
using Akbura.Language.CodeGeneration;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace Akbura.UnitTests;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class MarkupConditionalComponentWriterTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TopLevelOutVariable_IsVisibleToMarkupConditionInBothRenderMethods(bool structural)
    {
        var type = Compile(
            """
            TryGet(out var current);
            <Border>$if (current.Length > 0) { <TextBlock Text={current} /> }</Border>
            """, structural, OwnerSource +
            """

            public partial class PlannerView
            {
                public int Fetches { get; private set; }
                public bool TryGet(out string value) { Fetches++; value = Text; return true; }
            }
            """);
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(() =>
        {
            var owner = Create(type);
            var root = Assert.IsType<Border>(Invoke(owner, "FirstForTest"));
            var text = Assert.IsType<TextBlock>(root.Child);
            Assert.Equal("first", text.Text);
            Assert.Equal(1, Read<int>(owner, "Fetches"));

            Set(owner, "Text", "second");
            Assert.Same(root, Invoke(owner, "UpdateForTest"));
            Assert.Same(text, root.Child);
            Assert.Equal("second", text.Text);
            Assert.Equal(2, Read<int>(owner, "Fetches"));
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConditionalTemplate_CapturesTopLevelOutVariableFromTheRealInitialHostRender(bool structural)
    {
        var type = Compile(
            """
            TryGet(out var current);
            <ItemsControl>
                <ItemsControl.ItemTemplate>
                    <Border>
                        $if (Inner) { <TextBlock Text={current} /> }
                        $else { <Button /> }
                    </Border>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
            """, structural, OwnerSource +
            """

            public partial class PlannerView
            {
                public bool TryGet(out string value)
                {
                    value = Text + "-captured";
                    return true;
                }
            }
            """);
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(() =>
        {
            var owner = Create(type);
            var panel = new StackPanel();
            panel.Children.Add(owner);
            var window = new Window { Content = panel };
            try
            {
                window.Show();
                var root = Assert.IsType<ItemsControl>(owner.Child);
                var template = Assert.IsAssignableFrom<Avalonia.Controls.Templates.IDataTemplate>(root.ItemTemplate);
                var instance = Assert.IsType<Border>(template.Build(new object()));
                panel.Children.Add(instance);
                var text = Assert.IsType<TextBlock>(instance.Child);
                Assert.Equal("first-captured", text.Text);

                Set(owner, "Text", "second");
                owner.InvalidState();
                Assert.Same(root, owner.Child);
                Assert.Same(template, root.ItemTemplate);
                Assert.Same(text, instance.Child);
                Assert.Equal("second-captured", text.Text);
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
    public async Task HostInitialRender_EvaluatesOnlyReachedConditionsOnceAfterParentStatements(bool structural)
    {
        var type = Compile(
            """
            Text = "settled";
            <Border>
                $if (EvaluateFirst()) { <CountingTextBlock Text={Text} /> }
                $else if (EvaluateSecond()) { <TextBlock Text={Text} /> }
            </Border>
            """, structural);
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(() =>
        {
            var owner = Create(type);
            Set(owner, "Choice", 1);
            var window = new Window { Content = owner };
            try
            {
                window.Show();
                var root = Assert.IsType<Border>(owner.Child);
                Assert.Equal("settled", Assert.IsType<TextBlock>(root.Child).Text);
                Assert.Equal(2, Read<int>(owner, "ConditionEvaluations"));
                Assert.Equal(0, Constructed(type));
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
    public async Task ParentHookState_SurvivesBranchRemountWithoutChangingHookOrder(bool structural)
    {
        var type = Compile(
            """
            using Akbura.Hooks;
            state int count = useState(7);
            state int doubled = useSelect(count, value => value * 2);
            state string id = useId("conditional-parent");
            <Border>
                $if (Choice == 0) { <TextBlock Text={doubled.ToString()} /> }
                $else { <Button Content={doubled.ToString()} /> }
            </Border>
            """, structural, OwnerSource +
            """

            public partial class PlannerView
            {
                public int CountForTest => count;
                public string IdForTest => id;
                public void IncreaseForTest() => count++;
            }
            """);
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(() =>
        {
            var owner = Create(type);
            var window = new Window { Content = owner };
            try
            {
                window.Show();
                var root = Assert.IsType<Border>(owner.Child);
                var original = Assert.IsType<TextBlock>(root.Child);
                var states = owner.GetDiagnosticStates();
                var id = Read<string>(owner, "IdForTest");
                Assert.Equal("14", original.Text);

                Invoke(owner, "IncreaseForTest");
                Assert.Equal(8, Read<int>(owner, "CountForTest"));
                Assert.Equal("16", original.Text);
                Set(owner, "Choice", 1);
                Assert.Same(root, Invoke(owner, "UpdateForTest"));
                Assert.Equal("16", Assert.IsType<Button>(root.Child).Content);

                Set(owner, "Choice", 0);
                Assert.Same(root, Invoke(owner, "UpdateForTest"));
                var remounted = Assert.IsType<TextBlock>(root.Child);
                Assert.NotSame(original, remounted);
                Assert.Equal("16", remounted.Text);
                Assert.Equal(8, Read<int>(owner, "CountForTest"));
                Assert.Equal(id, Read<string>(owner, "IdForTest"));
                Assert.True(states == owner.GetDiagnosticStates());
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
    public async Task PropertySubscription_RefreshesPatternCaptureAndReleasesUnmountedObserver(bool structural)
    {
        var type = Compile(
            """
            <StackPanel>
                $if (Model is Person person) { <TextBox bind:Text={person.Name} /> }
            </StackPanel>
            """, structural);
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(() =>
        {
            var owner = Create(type);
            var original = Read<object>(owner, "Model");
            var root = Assert.IsType<StackPanel>(Invoke(owner, "FirstForTest"));
            var input = Assert.IsType<TextBox>(Assert.Single(root.Children));
            Assert.Equal("first", input.Text);
            var changed = Activator.CreateInstance(type.Assembly.GetType("Demo.Person")!, "second")!;
            Set(owner, "Model", changed);
            Assert.Same(root, Invoke(owner, "UpdateForTest"));
            Assert.Same(input, Assert.Single(root.Children));
            Assert.Equal("second", input.Text);
            input.Text = "manual";
            Assert.Equal("manual", Read<string>(changed, "Name"));
            Assert.Equal("first", Read<string>(original, "Name"));

            Set(owner, "Model", null);
            Assert.Same(root, Invoke(owner, "UpdateForTest"));
            Assert.Empty(root.Children);
            input.Text = "detached";
            Assert.Equal("manual", Read<string>(changed, "Name"));
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NestedConditionals_KeepTwoSlotsStaticNeighborsAndForeignItems(bool structural)
    {
        var type = Compile(
            """
            <MultiListOwner>
                <MultiListOwner.First>
                    <TextBox Text="prefix" />
                    $if (Choice == 0)
                    {
                        <Button />
                        $if (Inner) { <TextBlock Text={Text} /> }
                    }
                    $else { <CheckBox /> }
                    <TextBox Text="suffix" />
                </MultiListOwner.First>
                <MultiListOwner.Second>
                    $if (Choice == 0) { <TextBlock Text="second" /> }
                </MultiListOwner.Second>
            </MultiListOwner>
            """, structural);
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(() =>
        {
            var owner = Create(type);
            var root = Assert.IsAssignableFrom<Control>(Invoke(owner, "FirstForTest"));
            var first = Read<IList<Control>>(root, "First");
            var second = Read<IList<Control>>(root, "Second");
            Assert.Equal(4, first.Count);
            var prefix = first[0];
            var button = Assert.IsType<Button>(first[1]);
            var text = Assert.IsType<TextBlock>(first[2]);
            var suffix = first[3];
            var otherSlot = Assert.Single(second);
            var foreign = new TextBox { Text = "foreign" };
            first.Insert(1, foreign);

            Set(owner, "Text", "changed");
            Assert.Same(root, Invoke(owner, "UpdateForTest"));
            Assert.Equal("changed", text.Text);
            Assert.Contains(foreign, first);
            Assert.Equal(5, first.Count);
            Assert.Equal([prefix, button, text, suffix], first.Where(item => !ReferenceEquals(item, foreign)));
            Assert.Single(first, item => ReferenceEquals(item, foreign));
            Assert.Same(otherSlot, Assert.Single(second));

            Set(owner, "Inner", false);
            Assert.Same(root, Invoke(owner, "UpdateForTest"));
            Assert.DoesNotContain(text, first);
            Assert.Contains(button, first);
            Assert.Contains(foreign, first);
            Assert.Equal(4, first.Count);
            Assert.Equal([prefix, button, suffix], first.Where(item => !ReferenceEquals(item, foreign)));
            Assert.Single(first, item => ReferenceEquals(item, foreign));
            Assert.Same(otherSlot, Assert.Single(second));

            Set(owner, "Choice", 1);
            Assert.Same(root, Invoke(owner, "UpdateForTest"));
            Assert.DoesNotContain(button, first);
            var checkBox = Assert.Single(first.OfType<CheckBox>());
            Assert.Contains(foreign, first);
            Assert.Empty(second);
            Assert.Equal(4, first.Count);
            Assert.Equal([prefix, checkBox, suffix], first.Where(item => !ReferenceEquals(item, foreign)));
            Assert.Single(first, item => ReferenceEquals(item, foreign));
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ScalarPropertyElement_SwitchesAndClearsWithoutReplacingOwner(bool structural)
    {
        var type = Compile(
            """
            <Border>
                <Border.Child>
                    $if (Choice == 0) { <TextBlock Text={Text} /> }
                    $else if (Choice == 1) { <Button /> }
                </Border.Child>
            </Border>
            """, structural);
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(() =>
        {
            var owner = Create(type);
            var root = Assert.IsType<Border>(Invoke(owner, "FirstForTest"));
            var text = Assert.IsType<TextBlock>(root.Child);
            Set(owner, "Text", "changed");
            Assert.Same(root, Invoke(owner, "UpdateForTest"));
            Assert.Same(text, root.Child);
            Assert.Equal("changed", text.Text);
            Set(owner, "Choice", 1);
            Assert.Same(root, Invoke(owner, "UpdateForTest"));
            Assert.IsType<Button>(root.Child);
            Set(owner, "Choice", -1);
            Assert.Same(root, Invoke(owner, "UpdateForTest"));
            Assert.Null(root.Child);
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Conditions_PreserveNativeImplicitBooleanAndOperatorTrue(bool structural, bool implicitConversion)
    {
        var conditionType = implicitConversion ? "ImplicitTruth" : "ExplicitTruth";
        var type = Compile("<Border>$if (new " + conditionType +
            "(Choice == 0)) { <Button /> }</Border>", structural);
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(() =>
        {
            var owner = Create(type);
            var root = Assert.IsType<Border>(Invoke(owner, "FirstForTest"));
            Assert.IsType<Button>(root.Child);

            Set(owner, "Choice", 1);
            Assert.Same(root, Invoke(owner, "UpdateForTest"));
            Assert.Null(root.Child);
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Conditions_ShortCircuitAndNeverConstructInactiveBranches(bool structural)
    {
        var type = Compile(
            """
            <StackPanel>
                <TextBox />
                $if (EvaluateFirst()) { <Button Content="first" /> }
                $else if (EvaluateSecond()) { <CountingTextBlock Text="second" /> }
            </StackPanel>
            """, structural);
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(() =>
        {
            var owner = Create(type);
            var root = Assert.IsType<StackPanel>(Invoke(owner, "FirstForTest"));
            var prefix = root.Children[0];

            Assert.Equal(1, Read<int>(owner, "ConditionEvaluations"));
            Assert.Equal(0, Constructed(type));
            Assert.IsType<Button>(root.Children[1]);

            Set(owner, "Choice", 1);
            Invoke(owner, "UpdateForTest");
            Assert.Equal(3, Read<int>(owner, "ConditionEvaluations"));
            Assert.Equal(1, Constructed(type));
            Assert.Same(prefix, root.Children[0]);
            Assert.IsAssignableFrom<TextBlock>(root.Children[1]);

            Set(owner, "Choice", -1);
            Invoke(owner, "UpdateForTest");
            Assert.Equal(5, Read<int>(owner, "ConditionEvaluations"));
            Assert.Equal(1, Constructed(type));
            Assert.Same(prefix, Assert.Single(root.Children));
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ScalarConditional_AlternativesClearAndRemountWithoutReplacingOwner(bool structural)
    {
        var type = Compile(
            """
            <Border>
                $if (Choice == 0) { <CountingTextBlock Text={Text} /> }
                $else if (Choice == 1) { <Button Content="button" /> }
            </Border>
            """, structural);
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(() =>
        {
            var owner = Create(type);
            var root = Assert.IsType<Border>(Invoke(owner, "FirstForTest"));
            var first = Assert.IsAssignableFrom<TextBlock>(root.Child);
            Assert.Equal("first", first.Text);
            Assert.Equal(1, Constructed(type));

            Set(owner, "Text", "updated");
            Assert.Same(root, Invoke(owner, "UpdateForTest"));
            Assert.Same(first, root.Child);
            Assert.Equal("updated", first.Text);
            Assert.Equal(1, Constructed(type));

            Set(owner, "Choice", 1);
            Assert.Same(root, Invoke(owner, "UpdateForTest"));
            var button = Assert.IsType<Button>(root.Child);
            Assert.Equal("button", button.Content);

            Set(owner, "Choice", -1);
            Assert.Same(root, Invoke(owner, "UpdateForTest"));
            Assert.Null(root.Child);

            Set(owner, "Choice", 0);
            Assert.Same(root, Invoke(owner, "UpdateForTest"));
            var remounted = Assert.IsAssignableFrom<TextBlock>(root.Child);
            Assert.NotSame(first, remounted);
            Assert.Equal("updated", remounted.Text);
            Assert.Equal(2, Constructed(type));
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ScalarMixedText_UpdatesOneSynthesizedValueWithinRetainedBranch(bool structural)
    {
        var type = Compile(
            """
            <Button>
                $if (Choice == 0) { Hello {Text}! }
                $else { Goodbye }
            </Button>
            """, structural);
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(() =>
        {
            var owner = Create(type);
            var button = Assert.IsType<Button>(Invoke(owner, "FirstForTest"));
            Assert.Equal("Hello first!", button.Content);

            Set(owner, "Text", "Akbura");
            Assert.Same(button, Invoke(owner, "UpdateForTest"));
            Assert.Equal("Hello Akbura!", button.Content);

            Set(owner, "Choice", 1);
            Assert.Same(button, Invoke(owner, "UpdateForTest"));
            Assert.Equal("Goodbye", button.Content);
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PatternEventCapture_RefreshesRetainedRegistrationAndCleansUnmountedButton(bool structural)
    {
        var type = Compile(
            """
            void Accept(Person person) { LastPerson = person; }
            <StackPanel>
                <TextBox Text="static" />
                $if (Model is Person person)
                {
                    <Button Click={() => Accept(person)} Content={person.Name} />
                }
            </StackPanel>
            """, structural);
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(() =>
        {
            var owner = Create(type);
            var root = Assert.IsType<StackPanel>(Invoke(owner, "FirstForTest"));
            var prefix = Assert.IsType<TextBox>(root.Children[0]);
            var button = Assert.IsType<Button>(root.Children[1]);
            var original = Read<object>(owner, "Model");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Same(original, Read<object>(owner, "LastPerson"));

            var changed = Activator.CreateInstance(type.Assembly.GetType("Demo.Person")!, "second")!;
            Set(owner, "Model", changed);
            Assert.Same(root, Invoke(owner, "UpdateForTest"));
            Assert.Same(prefix, root.Children[0]);
            Assert.Same(button, root.Children[1]);
            Assert.Equal("second", button.Content);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Same(changed, Read<object>(owner, "LastPerson"));

            Set(owner, "Model", null);
            Assert.Same(root, Invoke(owner, "UpdateForTest"));
            Assert.Same(prefix, Assert.Single(root.Children));
            Set(owner, "LastPerson", null);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Null(Read<object?>(owner, "LastPerson"));
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DictionaryConditional_RetainsDynamicKeysAndForeignEntriesAcrossAlternatives(bool structural)
    {
        var type = Compile(
            """
            using Avalonia.Media;
            string GetKey() { KeyEvaluations++; return Key; }
            <Border>
                <Border.Resources>
                    $if (Choice == 0) { <SolidColorBrush x.key={GetKey()} Color="Red" /> }
                    $else if (Choice == 1) { <SolidColorBrush x.key={GetKey()} Color="Blue" /> }
                </Border.Resources>
            </Border>
            """, structural);
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(() =>
        {
            var owner = Create(type);
            var root = Assert.IsType<Border>(Invoke(owner, "FirstForTest"));
            var first = Assert.IsType<SolidColorBrush>(root.Resources["key"]);
            var foreign = new SolidColorBrush(Colors.Black);
            root.Resources["foreign"] = foreign;
            Assert.Equal(Colors.Red, first.Color);
            Assert.Equal(1, Read<int>(owner, "KeyEvaluations"));

            Set(owner, "Key", "changed");
            Assert.Same(root, Invoke(owner, "UpdateForTest"));
            Assert.False(root.Resources.ContainsKey("key"));
            Assert.Same(first, root.Resources["changed"]);
            Assert.Same(foreign, root.Resources["foreign"]);
            Assert.Equal(2, Read<int>(owner, "KeyEvaluations"));

            Set(owner, "Choice", 1);
            Assert.Same(root, Invoke(owner, "UpdateForTest"));
            var blue = Assert.IsType<SolidColorBrush>(root.Resources["changed"]);
            Assert.NotSame(first, blue);
            Assert.Equal(Colors.Blue, blue.Color);
            Assert.Same(foreign, root.Resources["foreign"]);
            Assert.Equal(3, Read<int>(owner, "KeyEvaluations"));

            Set(owner, "Choice", -1);
            Assert.Same(root, Invoke(owner, "UpdateForTest"));
            Assert.False(root.Resources.ContainsKey("changed"));
            Assert.Same(foreign, root.Resources["foreign"]);
            Assert.Equal(3, Read<int>(owner, "KeyEvaluations"));
            Assert.Single(root.Resources);
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConditionalStyles_SwitchOwnedStylingAndPreserveForeignStyleAndControl(bool structural)
    {
        var type = Compile(
            """
            using Avalonia.Styling;
            <Border>
                <Border.Styles>
                    $if (Choice == 0)
                    {
                        <Style Selector="Button"><Setter Property="Opacity" Value="0.5" /></Style>
                    }
                    $else if (Choice == 1)
                    {
                        <Style Selector="Button"><Setter Property="Opacity" Value="0.25" /></Style>
                    }
                </Border.Styles>
                <Button />
            </Border>
            """, structural);
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(() =>
        {
            var owner = Create(type);
            var window = new Window { Content = owner };
            try
            {
                window.Show();
                var root = Assert.IsType<Border>(owner.Child);
                var button = Assert.IsType<Button>(root.Child);
                var initialStyle = Assert.IsType<Style>(Assert.Single(root.Styles));
                Assert.NotNull(initialStyle.Selector);
                var initialSetter = Assert.IsType<Setter>(Assert.Single(initialStyle.Setters));
                Assert.Same(Avalonia.Visual.OpacityProperty, initialSetter.Property);
                Assert.Equal(0.5, initialSetter.Value);
                Assert.Equal(0.5, button.Opacity);
                var foreign = new Style(selector => selector.OfType<Button>())
                {
                    Setters = { new Setter(Control.HeightProperty, 73d) },
                };
                root.Styles.Add(foreign);

                Set(owner, "Choice", 1);
                Assert.Same(root, Invoke(owner, "UpdateForTest"));
                Assert.Same(button, root.Child);
                Assert.Equal(0.25, button.Opacity);
                Assert.Equal(73d, button.Height);
                Assert.Contains(foreign, root.Styles);
                Assert.DoesNotContain(initialStyle, root.Styles);

                Set(owner, "Choice", -1);
                Assert.Same(root, Invoke(owner, "UpdateForTest"));
                Assert.Same(button, root.Child);
                Assert.Equal(1, button.Opacity);
                Assert.Same(foreign, Assert.Single(root.Styles));
                Assert.Equal(73d, button.Height);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    internal static Type Compile(string source, bool structural, string? ownerSource = null)
    {
        var fixture = AkcssActivatorPlannerTests.CreateFixture("using Avalonia.Controls; using Demo; " + source,
            ownerSource ?? OwnerSource);
        var component = Assert.IsAssignableFrom<IAkburaComponentSymbol>(
            fixture.SemanticModel.GetSymbolInfo(fixture.ComponentTree.GetRoot()).Symbol);
        var semanticDiagnostics = fixture.SemanticModel.GetSemanticDiagnostics(fixture.ComponentTree.GetRoot());
        Assert.True(semanticDiagnostics.IsEmpty, string.Join(Environment.NewLine,
            semanticDiagnostics.Select(diagnostic => diagnostic.Code + ": " + diagnostic.Message)));
        var generated = ComponentDocumentWriter.Generate(component, fixture.SemanticModel,
            "Views/PlannerView.akbura", new Dictionary<AkburaSyntax, string>(),
            mode: structural ? ComponentGenerationMode.DebugStructural : ComponentGenerationMode.ReleaseDirect);
        var options = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        if (structural)
        {
            options = options.WithPreprocessorSymbols("DEBUG");
        }

        var compilation = fixture.CSharpCompilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(generated, options))
            .WithAssemblyName("MarkupConditional_" + Guid.NewGuid().ToString("N"));
        var diagnostics = compilation.GetDiagnostics().Where(diagnostic =>
            diagnostic.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error).ToArray();
        Assert.True(diagnostics.Length == 0, string.Join(Environment.NewLine, diagnostics.AsEnumerable()) +
            Environment.NewLine + generated);
        using var output = new MemoryStream();
        var emitted = compilation.Emit(output);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        return Assert.IsAssignableFrom<Type>(Assembly.Load(output.ToArray()).GetType("Demo.PlannerView"));
    }

    private static AkburaControl Create(Type type) => Assert.IsAssignableFrom<AkburaControl>(Activator.CreateInstance(type));

    private static int Constructed(Type type) => (int)type.Assembly.GetType("Demo.CountingTextBlock")!
        .GetField("Constructed")!.GetValue(null)!;

    private static T Read<T>(object owner, string name) => (T)owner.GetType().GetProperty(name)!.GetValue(owner)!;

    private static void Set(object owner, string name, object? value) => owner.GetType().GetProperty(name)!.SetValue(owner, value);

    private static object? Invoke(object owner, string name)
    {
        try
        {
            return owner.GetType().GetMethod(name)!.Invoke(owner, null);
        }
        catch (TargetInvocationException error) when (error.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(error.InnerException).Throw();
            throw;
        }
    }

    private const string OwnerSource =
        """
        namespace Demo;
        public partial class PlannerView : Akbura.AkburaControl
        {
            public PlannerView() : base(Akbura.Engine.AkburaEngine.Empty) { }
            public int Choice { get; set; }
            public bool Inner { get; set; } = true;
            public string Text { get; set; } = "first";
            public object? Model { get; set; } = new Person("first");
            public object? LastPerson { get; set; }
            public string Key { get; set; } = "key";
            public int KeyEvaluations { get; set; }
            public int ConditionEvaluations { get; set; }
            public bool EvaluateFirst() { ConditionEvaluations++; return Choice == 0; }
            public bool EvaluateSecond() { ConditionEvaluations++; return Choice == 1; }
            public Avalonia.Controls.Control FirstForTest() => FirstUpdate();
            public Avalonia.Controls.Control UpdateForTest() => Update();
        }
        public sealed class Person
        {
            public Person(string name) { Name = name; }
            public string Name { get; set; }
        }
        public sealed class CountingTextBlock : Avalonia.Controls.TextBlock
        {
            public static int Constructed;
            public CountingTextBlock() { Constructed++; }
        }
        public readonly struct ExplicitTruth
        {
            private readonly bool _value;
            public ExplicitTruth(bool value) { _value = value; }
            public static bool operator true(ExplicitTruth value) => value._value;
            public static bool operator false(ExplicitTruth value) => !value._value;
        }
        public readonly struct ImplicitTruth
        {
            private readonly bool _value;
            public ImplicitTruth(bool value) { _value = value; }
            public static implicit operator bool(ImplicitTruth value) => value._value;
        }
        public sealed class MultiListOwner : Avalonia.Controls.Control
        {
            public System.Collections.Generic.IList<Avalonia.Controls.Control> First { get; } =
                new System.Collections.Generic.List<Avalonia.Controls.Control>();
            public System.Collections.Generic.IList<Avalonia.Controls.Control> Second { get; } =
                new System.Collections.Generic.List<Avalonia.Controls.Control>();
        }
        """;
}
