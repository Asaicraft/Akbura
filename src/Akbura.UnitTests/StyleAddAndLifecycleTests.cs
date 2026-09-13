using Akbura.Language;
using Akbura.Language.CodeGeneration;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.ComponentTree;
using Avalonia.Data;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Reflection;

namespace Akbura.UnitTests;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class StyleAddAndLifecycleTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeStyle_SelectsTypedAddMethodForEachChildAndCompiles(bool debugStructural)
    {
        const string source =
            """
            using Avalonia.Controls;
            using Avalonia.Styling;
            <Border>
                <Border.Styles>
                    <Style Selector="Button">
                        <Setter Property="Opacity" Value="0.5" />
                        <Style Selector="^:pointerover">
                            <Setter Property="Opacity" Value="0.8" />
                        </Style>
                    </Style>
                </Border.Styles>
                <Button />
            </Border>
            """;
        var fixture = AkcssActivatorPlannerTests.CreateFixture(source, OwnerSource);
        var styles = fixture.GetChildElements().Single(element => element.StartTag!.Name.ToString() == "Border.Styles");
        var style = styles.Body.OfType<MarkupElementContentSyntax>().Single().Element;
        var symbol = fixture.GetElementSymbol(style);

        Assert.Equal(MarkupContentKind.AddMethods, symbol.ContentModel.Kind);
        Assert.Equal(["SetterBase", "IStyle"], symbol.Children.Select(child =>
            child.InsertionMethod!.Parameters.Single().Type.Name));
        Assert.All(symbol.Children, child => Assert.Equal("Add", child.InsertionMethod!.Name));
        Assert.All(symbol.Children, child => Assert.Equal("StyleBase", child.InsertionMethod!.ContainingType.Name));
        _ = Compile(fixture, debugStructural);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReactiveStyle_ReplacesOwnedSubtreeAndPreservesControlAndForeignStyles(bool debugStructural)
    {
        const string source =
            """
            using Avalonia.Controls;
            using Avalonia.Styling;
            state double opacity = 0.25;
            <Border>
                <Border.Styles>
                    <Style Selector="Button">
                        <Setter Property="Opacity" Value={opacity} />
                        <Style Selector="^.active">
                            <Setter Property="Width" Value="81" />
                        </Style>
                    </Style>
                </Border.Styles>
                <Button />
            </Border>
            """;
        var ownerType = Compile(AkcssActivatorPlannerTests.CreateFixture(source, OwnerSource), debugStructural);
        using var session = HeadlessUnitTestSession.StartNew(typeof(AvaloniaTestAppBuilder));
        await session.Dispatch(() =>
        {
            var owner = Assert.IsAssignableFrom<AkburaControl>(Activator.CreateInstance(ownerType));
            var window = new Window { Content = owner };
            try
            {
                window.Show();
                var border = Assert.IsType<Border>(owner.Child);
                var button = Assert.IsType<Button>(border.Child);
                Assert.Equal(0.25, button.Opacity);
                var initialStyle = Assert.IsType<Style>(Assert.Single(border.Styles));
                Assert.Single(initialStyle.Setters);
                Assert.Single(initialStyle.Children);
                var foreign = new Style(selector => selector.OfType<Button>())
                {
                    Setters = { new Setter(Control.HeightProperty, 73d) },
                };
                border.Styles.Add(foreign);

                Assert.IsType<State<double>>(Assert.Single(owner.GetDiagnosticStates())).Value = 0.6;

                Assert.Same(border, owner.Child);
                Assert.Same(button, border.Child);
                Assert.Equal(0.6, button.Opacity);
                Assert.Equal(73d, button.Height);
                Assert.Equal(2, border.Styles.Count);
                Assert.Contains(foreign, border.Styles);
                var updated = Assert.IsType<Style>(border.Styles.Single(style => !ReferenceEquals(style, foreign)));
                Assert.NotSame(initialStyle, updated);
                Assert.Single(updated.Setters);
                Assert.Single(updated.Children);

                Assert.IsType<State<double>>(Assert.Single(owner.GetDiagnosticStates())).Value = 0.9;

                Assert.Equal(0.9, button.Opacity);
                Assert.Equal(2, border.Styles.Count);
                Assert.Contains(foreign, border.Styles);
                Assert.Same(button, border.Child);
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
    public async Task SetterBinding_AllAssignmentRoutesRetainBindingObjectsAndApplyToStyledControls(bool debugStructural)
    {
        const string source =
            """
            using Avalonia.Controls;
            using Avalonia.Data;
            using Avalonia.Styling;
            using Demo;
            <StackPanel>
                <StackPanel.Styles>
                    <Style Selector="Button#attribute">
                        <Setter Value=${Binding AccentBrush} Property="Background" />
                    </Style>
                    <Style Selector="Button#content">
                        <Setter Property="Background"><Binding Path="AccentBrush" /></Setter>
                    </Style>
                    <Style Selector="Button#property">
                        <Setter Property="Background">
                            <Setter.Value><Binding Path="AccentBrush" /></Setter.Value>
                        </Setter>
                    </Style>
                    <Style Selector="Button#extension">
                        <Setter Value=${ObjectBinding} Property="Background" />
                    </Style>
                </StackPanel.Styles>
                <Button Name="attribute" />
                <Button Name="content" />
                <Button Name="property" />
                <Button Name="extension" />
            </StackPanel>
            """;
        const string custom =
            """
            namespace Demo
            {
                public partial class PlannerView
                {
                    public Avalonia.Media.IBrush AccentBrush { get; } = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Colors.Blue);
                }
                public sealed class ObjectBindingExtension
                {
                    public static int Calls;
                    public object ProvideValue(System.IServiceProvider services)
                    {
                        var target = (Avalonia.Markup.Xaml.IProvideValueTarget?)services.GetService(typeof(Avalonia.Markup.Xaml.IProvideValueTarget));
                        if (target?.TargetObject is not Avalonia.Styling.Setter setter || setter.Property != Avalonia.Controls.Button.BackgroundProperty)
                            throw new System.InvalidOperationException("ProvideValue must run after the Setter's property is initialized.");
                        Calls++;
                        return new Avalonia.Data.Binding("AccentBrush");
                    }
                }
            }
            """;
        var ownerType = Compile(AkcssActivatorPlannerTests.CreateFixture(source, OwnerSource + "\r\n" + custom), debugStructural);
        using var session = HeadlessUnitTestSession.StartNew(typeof(AvaloniaTestAppBuilder));
        await session.Dispatch(() =>
        {
            var owner = Assert.IsAssignableFrom<AkburaControl>(Activator.CreateInstance(ownerType));
            owner.DataContext = owner;
            var window = new Window { Content = owner };
            try
            {
                window.Show();
                var panel = Assert.IsType<StackPanel>(owner.Child);
                var expected = Assert.IsAssignableFrom<IBrush>(ownerType.GetProperty("AccentBrush")!.GetValue(owner));
                Assert.Equal(4, panel.Styles.Count);
                Assert.All(panel.Styles, style =>
                {
                    var setter = Assert.IsType<Setter>(Assert.Single(Assert.IsType<Style>(style).Setters));
                    Assert.Same(Button.BackgroundProperty, setter.Property);
                    Assert.IsAssignableFrom<BindingBase>(setter.Value);
                });
                Assert.All(panel.Children, child => Assert.Same(expected, Assert.IsType<Button>(child).Background));
                var extension = ownerType.Assembly.GetType("Demo.ObjectBindingExtension")!;
                Assert.True(Assert.IsType<int>(extension.GetField("Calls")!.GetValue(null)) > 0);
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
    public async Task SetterResources_ResolveBeforeInsertionAndKeepDynamicResourcesLive(bool debugStructural)
    {
        const string source =
            """
            using Avalonia.Controls;
            using Avalonia.Media;
            using Avalonia.Styling;
            using Avalonia.Markup.Xaml.MarkupExtensions;
            <Border>
                <Border.Resources>
                    <SolidColorBrush x.key="StaticBrush" Color="Red" />
                    <SolidColorBrush x.key="DynamicBrush" Color="Blue" />
                </Border.Resources>
                <Border.Styles>
                    <Style Selector="Button#static">
                        <Setter Value=${StaticResource StaticBrush} Property="Background" />
                    </Style>
                    <Style Selector="Button#dynamic">
                        <Setter Value=${DynamicResource DynamicBrush} Property="Background" />
                    </Style>
                </Border.Styles>
                <StackPanel>
                    <Button Name="static" />
                    <Button Name="dynamic" />
                </StackPanel>
            </Border>
            """;
        var ownerType = Compile(AkcssActivatorPlannerTests.CreateFixture(source, OwnerSource), debugStructural);
        using var session = HeadlessUnitTestSession.StartNew(typeof(AvaloniaTestAppBuilder));
        await session.Dispatch(() =>
        {
            var owner = Assert.IsAssignableFrom<AkburaControl>(Activator.CreateInstance(ownerType));
            var window = new Window { Content = owner };
            try
            {
                window.Show();
                var border = Assert.IsType<Border>(owner.Child);
                var panel = Assert.IsType<StackPanel>(border.Child);
                var staticButton = Assert.IsType<Button>(panel.Children[0]);
                var dynamicButton = Assert.IsType<Button>(panel.Children[1]);
                var staticBrush = Assert.IsType<SolidColorBrush>(border.Resources["StaticBrush"]);
                var dynamicBrush = Assert.IsType<SolidColorBrush>(border.Resources["DynamicBrush"]);
                var staticSetter = Assert.IsType<Setter>(Assert.Single(Assert.IsType<Style>(border.Styles[0]).Setters));
                var dynamicSetter = Assert.IsType<Setter>(Assert.Single(Assert.IsType<Style>(border.Styles[1]).Setters));
                Assert.Same(staticBrush, staticSetter.Value);
                Assert.IsAssignableFrom<BindingBase>(dynamicSetter.Value);
                Assert.Same(staticBrush, staticButton.Background);
                Assert.Same(dynamicBrush, dynamicButton.Background);

                var replacement = new SolidColorBrush(Colors.Green);
                border.Resources["DynamicBrush"] = replacement;

                Assert.Same(replacement, dynamicButton.Background);
                Assert.Same(staticBrush, staticButton.Background);
                Assert.Same(dynamicSetter, Assert.Single(Assert.IsType<Style>(border.Styles[1]).Setters));
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
    public async Task RenamedAssignBindingPayload_StoresObjectResultAfterProvideValue(bool debugStructural)
    {
        const string source =
            """
            using Avalonia.Controls;
            using Demo;
            <ContentControl>
                <OrderedAssignment Payload=${ObjectBinding} Target="Button.Background" />
            </ContentControl>
            """;
        const string extension =
            """
            namespace Demo
            {
                public sealed class ObjectBindingExtension
                {
                    public object ProvideValue(System.IServiceProvider services)
                    {
                        var target = (Avalonia.Markup.Xaml.IProvideValueTarget?)services.GetService(typeof(Avalonia.Markup.Xaml.IProvideValueTarget));
                        if (target?.TargetObject is not OrderedAssignment holder || holder.Target != Avalonia.Controls.Button.BackgroundProperty)
                            throw new System.InvalidOperationException("The metadata dependency must be initialized before ProvideValue.");
                        return new Avalonia.Data.Binding("AccentBrush");
                    }
                }
            }
            """;
        var ownerType = Compile(AkcssActivatorPlannerTests.CreateFixture(source,
            OwnerSource + "\r\n" + OrderedAssignmentSource + "\r\n" + extension), debugStructural);
        using var session = HeadlessUnitTestSession.StartNew(typeof(AvaloniaTestAppBuilder));
        await session.Dispatch(() =>
        {
            var owner = Assert.IsAssignableFrom<AkburaControl>(Activator.CreateInstance(ownerType));
            var window = new Window { Content = owner };
            try
            {
                window.Show();
                var holder = Assert.IsType<ContentControl>(owner.Child).Content!;
                var payload = Assert.IsType<Binding>(holder.GetType().GetProperty("Payload")!.GetValue(holder));
                Assert.Equal("AccentBrush", payload.Path);
                Assert.Same(Button.BackgroundProperty, holder.GetType().GetProperty("Target")!.GetValue(holder));
                Assert.Equal(["Target", "Payload"], Assert.IsAssignableFrom<IEnumerable<string>>(
                    holder.GetType().GetProperty("Order")!.GetValue(holder)));
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
    public async Task CustomAdd_AppliesUserDefinedConversionAndSpecificOverload(bool debugStructural)
    {
        const string source = "using Avalonia.Controls; using Demo; <ContentControl><AddHost><Child /></AddHost></ContentControl>";
        const string custom =
            """
            namespace Demo
            {
                public sealed class Child
                {
                    public static implicit operator Target(Child child) => new Target { Value = 7 };
                }
                public sealed class Target { public int Value { get; set; } }
                public sealed class AddHost
                {
                    public System.Collections.Generic.List<int> Values { get; } = new();
                    public void Add(object child) => Values.Add(-1);
                    public void Add(Target child) => Values.Add(child.Value);
                }
            }
            """;
        var fixture = AkcssActivatorPlannerTests.CreateFixture(source, OwnerSource + "\r\n" + custom);
        var container = fixture.GetChildElements().Single();
        var symbol = fixture.GetElementSymbol(container);
        Assert.Equal("Target", Assert.Single(symbol.Children).InsertionMethod!.Parameters.Single().Type.Name);
        var ownerType = Compile(fixture, debugStructural);
        using var session = HeadlessUnitTestSession.StartNew(typeof(AvaloniaTestAppBuilder));
        await session.Dispatch(() =>
        {
            var owner = Assert.IsAssignableFrom<AkburaControl>(Activator.CreateInstance(ownerType));
            var window = new Window { Content = owner };
            try
            {
                window.Show();
                var root = Assert.IsType<ContentControl>(owner.Child);
                var containerValue = Assert.IsAssignableFrom<object>(root.Content);
                var values = Assert.IsAssignableFrom<IEnumerable<int>>(
                    containerValue.GetType().GetProperty("Values")!.GetValue(containerValue));
                Assert.Equal(7, Assert.Single(values));
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public void CustomAdd_AmbiguousInterfacesProduceSemanticDiagnostic()
    {
        const string source = "using Avalonia.Controls; using Demo; <ContentControl><AddHost><Child /></AddHost></ContentControl>";
        const string custom =
            """
            namespace Demo
            {
                public interface ILeft { }
                public interface IRight { }
                public sealed class Child : ILeft, IRight { }
                public sealed class AddHost
                {
                    public void Add(ILeft child) { }
                    public void Add(IRight child) { }
                }
            }
            """;
        var fixture = AkcssActivatorPlannerTests.CreateFixture(source, custom);

        var diagnostics = fixture.SemanticModel.GetSemanticDiagnostics(fixture.ComponentTree.GetRoot());

        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.Code == "AKBURA_SEMANTIC_MarkupContentAddMethodAmbiguous");
    }

    [Theory]
    [InlineData("Payload=\"Red\" Target=\"Button.Background\"", false)]
    [InlineData("Payload=\"Red\"><OrderedAssignment.Target>{Button.BackgroundProperty}</OrderedAssignment.Target></OrderedAssignment", true)]
    [InlineData("Target=\"Button.Background\">Red</OrderedAssignment", true)]
    [InlineData("Target=\"Button.Background\"><OrderedAssignment.Payload>Red</OrderedAssignment.Payload></OrderedAssignment", true)]
    public void Dependencies_UnifyAttributePropertyElementAndImplicitContentOrder(string markup, bool openElement)
    {
        var source = "using Avalonia.Controls; using Demo; <ContentControl><OrderedAssignment " +
            markup + (openElement ? "></ContentControl>" : " /></ContentControl>");
        var fixture = AkcssActivatorPlannerTests.CreateFixture(source, OrderedAssignmentSource);
        var assignment = fixture.GetChildElements().Single();

        var order = fixture.SemanticModel.GetMarkupAssignmentOrder(assignment, out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.Equal(2, order.Length);
        Assert.Contains("Target", order[0].ToString(), StringComparison.Ordinal);
        Assert.True(ReferenceEquals(order[1], assignment) || order[1].ToString().Contains("Payload", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Dependencies_MixedRoutesInitializeHolderBeforePayload(bool debugStructural)
    {
        const string source =
            """
            using Avalonia.Controls;
            using Demo;
            <StackPanel>
                <ContentControl><OrderedAssignment Payload="Red" Target="Button.Background" /></ContentControl>
                <ContentControl>
                    <OrderedAssignment Payload="Blue">
                        <OrderedAssignment.Target>{Button.BackgroundProperty}</OrderedAssignment.Target>
                    </OrderedAssignment>
                </ContentControl>
                <ContentControl><OrderedAssignment Target="Button.Background">Green</OrderedAssignment></ContentControl>
                <ContentControl>
                    <OrderedAssignment Target="Button.Background">
                        <OrderedAssignment.Payload>Orange</OrderedAssignment.Payload>
                    </OrderedAssignment>
                </ContentControl>
            </StackPanel>
            """;
        var ownerType = Compile(AkcssActivatorPlannerTests.CreateFixture(source,
            OwnerSource + "\r\n" + OrderedAssignmentSource), debugStructural);
        using var session = HeadlessUnitTestSession.StartNew(typeof(AvaloniaTestAppBuilder));
        await session.Dispatch(() =>
        {
            var owner = Assert.IsAssignableFrom<AkburaControl>(Activator.CreateInstance(ownerType));
            var window = new Window { Content = owner };
            try
            {
                window.Show();
                var root = Assert.IsType<StackPanel>(owner.Child);
                Assert.Equal(4, root.Children.Count);
                foreach (var child in root.Children)
                {
                    var holder = Assert.IsType<ContentControl>(child).Content!;
                    var values = Assert.IsAssignableFrom<IEnumerable<string>>(holder.GetType().GetProperty("Order")!.GetValue(holder));
                    Assert.Equal(["Target", "Payload"], values);
                    Assert.IsAssignableFrom<Avalonia.Media.IBrush>(holder.GetType().GetProperty("Payload")!.GetValue(holder));
                }
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public void DependencyCycle_ReportsClosedCycleWithoutBlockedDependent()
    {
        const string source = "using Avalonia.Controls; using Demo; <ContentControl><CycleHost C=\"3\" A=\"1\" B=\"2\" /></ContentControl>";
        const string custom =
            """
            namespace Demo
            {
                public sealed class CycleHost
                {
                    [Avalonia.Metadata.DependsOn(nameof(B))] public int A { get; set; }
                    [Avalonia.Metadata.DependsOn(nameof(A))] public int B { get; set; }
                    [Avalonia.Metadata.DependsOn(nameof(A))] public int C { get; set; }
                }
            }
            """;
        var fixture = AkcssActivatorPlannerTests.CreateFixture(source, custom);

        _ = fixture.SemanticModel.GetMarkupAssignmentOrder(fixture.GetChildElements().Single(), out var diagnostics);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("AKBURA_SEMANTIC_MarkupPropertyDependencyCycle", diagnostic.Code);
        Assert.Contains("A -> B -> A", diagnostic.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("C ->", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DependencySelfCycle_ReportsClosedMemberPath()
    {
        var fixture = AkcssActivatorPlannerTests.CreateFixture(
            "using Avalonia.Controls; using Demo; <ContentControl><CycleHost A=\"1\" /></ContentControl>",
            "namespace Demo { public sealed class CycleHost { [Avalonia.Metadata.DependsOn(nameof(A))] public int A { get; set; } } }");

        _ = fixture.SemanticModel.GetMarkupAssignmentOrder(fixture.GetChildElements().Single(), out var diagnostics);

        Assert.Contains("A -> A", Assert.Single(diagnostics).Message, StringComparison.Ordinal);
    }

    private static Type Compile(AkcssActivatorPlannerTests.PlannerFixture fixture, bool debugStructural)
    {
        var component = Assert.IsAssignableFrom<IAkburaComponentSymbol>(
            fixture.SemanticModel.GetSymbolInfo(fixture.ComponentTree.GetRoot()).Symbol);
        var semanticErrors = fixture.SemanticModel.GetSemanticDiagnostics(fixture.ComponentTree.GetRoot())
            .Where(diagnostic => diagnostic.Severity == AkburaDiagnosticSeverity.Error).ToArray();
        Assert.True(semanticErrors.Length == 0, string.Join(Environment.NewLine,
            semanticErrors.Select(diagnostic => diagnostic.Code + ": " + diagnostic.Message)));
        var generated = ComponentDocumentWriter.Generate(component, fixture.SemanticModel,
            "PlannerView.akbura", new Dictionary<AkburaSyntax, string>(), mode: debugStructural
                ? ComponentGenerationMode.DebugStructural : ComponentGenerationMode.ReleaseDirect);
        var options = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        if (debugStructural) options = options.WithPreprocessorSymbols("DEBUG");
        var compilation = fixture.CSharpCompilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(generated, options))
            .WithAssemblyName("StyleLifecycle_" + Guid.NewGuid().ToString("N"));
        var errors = compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity is
            DiagnosticSeverity.Warning or DiagnosticSeverity.Error).ToArray();
        Assert.True(errors.Length == 0, string.Join(Environment.NewLine, errors.Select(error => error.ToString())) +
            Environment.NewLine + generated);
        using var output = new MemoryStream();
        var emitted = compilation.Emit(output);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        return Assert.IsAssignableFrom<Type>(Assembly.Load(output.ToArray()).GetType("Demo.PlannerView"));
    }

    private const string OwnerSource =
        """
        namespace Demo
        {
            public partial class PlannerView : Akbura.AkburaControl
            {
                public PlannerView() : base(Akbura.Engine.AkburaEngine.Empty) { }
            }
        }
        """;

    private const string OrderedAssignmentSource =
        """
        namespace Demo
        {
            public sealed class OrderedAssignment
            {
                private Avalonia.AvaloniaProperty? _target;
                private object? _payload;
                public System.Collections.Generic.List<string> Order { get; } = new();
                public Avalonia.AvaloniaProperty? Target
                {
                    get => _target;
                    set { _target = value; Order.Add("Target"); }
                }
                [Avalonia.Metadata.Content, Avalonia.Data.AssignBinding, Avalonia.Metadata.DependsOn(nameof(Target))]
                public object? Payload
                {
                    get => _payload;
                    set
                    {
                        if (_target == null) throw new System.InvalidOperationException("Payload was initialized before Target.");
                        _payload = value;
                        Order.Add("Payload");
                    }
                }
            }
        }
        """;
}
