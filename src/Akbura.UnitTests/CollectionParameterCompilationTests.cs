using Akbura.Language;
using Akbura.Language.CodeGeneration;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.TestUtilities.Documentation;
using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using System.Collections;
using System.Collections.ObjectModel;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Akbura.UnitTests;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class CollectionParameterCompilationTests
{
    private const string Page = "akbura/collection-parameters.md";
    private static string Doc(string section, int ordinal = 0) =>
        MarkdownDocumentation.Get(Page, section, ordinal).Code;

    [Fact]
    public void DocumentationCatalog_CoversEveryAkburaFence()
    {
        var sections = MarkdownDocumentation.Read(Page).Where(b => b.Language == "akbura")
            .Select(b => (b.Section, b.Ordinal)).ToArray();
        Assert.Equal(new[]
        {
            ("Declaring a collection parameter", 0), ("Optional parameter", 0),
            ("Observable state source", 0), ("Static resource source", 0),
            ("DataContext binding", 0), ("Non-generic and concrete observable declarations", 0),
            ("Non-generic and concrete observable declarations", 1)
        }, sections);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OptionalDocumentedParameter_HasDistinctEmptyBackings_AndWritableDescriptor(bool structural)
    {
        var assembly = Compile(Doc("Optional parameter"), Doc("Declaring a collection parameter"), structural);
        await OnUi(() =>
        {
            var parent = New(assembly, "PlannerView");
            var window = new Window { Content = parent };
            try
            {
                window.Show(); Drain();
                var child = Assert.IsAssignableFrom<AkburaControl>(parent.Child);
                var data = Assert.IsAssignableFrom<ObservableCollection<int>>(Get(child, "Data"));
                Assert.Empty(data);
                Assert.NotSame(data, Get(New(assembly, "ItemsPanel"), "Data"));
                var descriptor = Assert.IsAssignableFrom<Akbura.ComponentTree.Parameter>(
                    child.GetType().GetField("DataProperty")!.GetValue(null));
                Assert.False(descriptor.AvaloniaProperty.IsReadOnly);
                Assert.True(descriptor.IsSet(child));
                Assert.Equal("Count: 0", Assert.IsType<TextBlock>(Assert.IsType<StackPanel>(child.Child).Children[0]).Text);
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DocumentedStateSource_UpdatesForeach_AndCountOutsideIt(bool structural)
    {
        var assembly = Compile(Doc("Observable state source"), Doc("Declaring a collection parameter"), structural);
        await OnUi(() =>
        {
            var parent = New(assembly, "PlannerView");
            var window = new Window { Content = parent };
            try
            {
                window.Show(); Drain();
                var child = Assert.IsAssignableFrom<AkburaControl>(parent.Child);
                var owned = Assert.IsAssignableFrom<IList<int>>(Get(child, "Data"));
                var source = Assert.IsType<ObservableCollection<int>>(Get(parent, "items"));
                Assert.NotSame(source, owned);
                AssertRows(child, [1, 2, 3]);
                source.Add(4); Drain();
                AssertRows(child, [1, 2, 3, 4]);
                owned.RemoveAt(1); Drain();
                Assert.Equal(new[] { 1, 3, 4 }, source);
                AssertRows(child, [1, 3, 4]);
                var replacement = new ObservableCollection<int>([9, 8]);
                Set(parent, "items", replacement); Drain();
                Assert.Same(owned, Get(child, "Data"));
                AssertRows(child, [9, 8]);
                source.Add(100); Drain();
                AssertRows(child, [9, 8]);
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DocumentedStaticResource_IsObservedAfterLookup(bool structural)
    {
        var assembly = Compile(Doc("Static resource source"), Doc("Declaring a collection parameter"), structural);
        await OnUi(() =>
        {
            var source = new ObservableCollection<int>([2, 4]);
            var app = Application.Current!;
            var hadValue = app.Resources.TryGetValue("Numbers", out var old);
            app.Resources["Numbers"] = source;
            var parent = New(assembly, "PlannerView");
            var window = new Window { Content = parent };
            try
            {
                window.Show(); Drain();
                var child = Assert.IsAssignableFrom<AkburaControl>(parent.Child);
                AssertRows(child, [2, 4]);
                source.Add(6); Drain();
                AssertRows(child, [2, 4, 6]);
            }
            finally
            {
                window.Close();
                if (hadValue) app.Resources["Numbers"] = old;
                else app.Resources.Remove("Numbers");
            }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DocumentedDataContextBinding_ReconnectsWithoutOverwritingViewModelReference(bool structural)
    {
        var assembly = Compile(Doc("DataContext binding"), Doc("Declaring a collection parameter"), structural);
        await OnUi(() =>
        {
            var model = new CollectionParameterTestViewModel();
            var first = new ObservableCollection<int>([1, 2]);
            model.Items = first;
            var parent = New(assembly, "PlannerView");
            parent.DataContext = model;
            var window = new Window { Content = parent };
            try
            {
                window.Show(); Drain();
                var child = Assert.IsAssignableFrom<AkburaControl>(parent.Child);
                var owned = Assert.IsAssignableFrom<IList<int>>(Get(child, "Data"));
                AssertRows(child, [1, 2]);
                owned.Add(3); Drain();
                Assert.Same(first, model.Items);
                Assert.Equal(new[] { 1, 2, 3 }, first);
                var next = new ObservableCollection<int>([5]);
                model.Items = next; Drain();
                Assert.Same(owned, Get(child, "Data"));
                Assert.Same(next, model.Items);
                AssertRows(child, [5]);
                first.Add(9); Drain();
                AssertRows(child, [5]);
                next.Add(6); Drain();
                AssertRows(child, [5, 6]);
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    public async Task DocumentedUntypedAndConcreteDeclarations_AcceptSourceSetter(bool structural, int ordinal)
    {
        var assembly = Compile("<ItemsPanel/>", Doc("Non-generic and concrete observable declarations", ordinal), structural);
        await OnUi(() =>
        {
            var child = New(assembly, "ItemsPanel");
            var before = Get(child, "Data");
            var source = ordinal == 0
                ? (object)new ObservableCollection<object>([1, "two"])
                : new ObservableCollection<int>([1, 2]);
            Set(child, "Data", source);
            var list = Assert.IsAssignableFrom<IList>(Get(child, "Data"));
            Assert.Same(before, list);
            Assert.Equal(2, list.Count);
            Assert.Equal(ordinal == 0 ? typeof(ObservableCollection<object>) : typeof(ObservableCollection<int>), list.GetType());
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CollectionInitializer_IsPerInstance_AndNullClearsWithoutReplacing(bool structural)
    {
        const string childSource = "using System.Collections.Generic; using Avalonia.Controls; param IList<int> Data = [4, 5]; <Border/>";
        var assembly = Compile("<ItemsPanel/>", childSource, structural);
        await OnUi(() =>
        {
            var first = New(assembly, "ItemsPanel");
            var second = New(assembly, "ItemsPanel");
            var data = Assert.IsAssignableFrom<IList<int>>(Get(first, "Data"));
            var other = Assert.IsAssignableFrom<IList<int>>(Get(second, "Data"));
            Assert.Equal(new[] { 4, 5 }, data);
            Assert.Equal(new[] { 4, 5 }, other);
            Assert.NotSame(data, other);
            Set(first, "Data", null);
            Assert.Same(data, Get(first, "Data"));
            Assert.Empty(data);
            Assert.Equal(new[] { 4, 5 }, other);
        });
    }

    [Fact]
    public async Task CompatibleDescriptorRecreation_PreservesPropertyAndSourceConnection()
    {
        var assembly = Compile("<ItemsPanel/>", Doc("Declaring a collection parameter"), structural: true);
        await OnUi(() =>
        {
            var owner = New(assembly, "ItemsPanel");
            var source = new ObservableCollection<int>([1]);
            Set(owner, "Data", source);
            var backing = Get(owner, "Data");
            var descriptor = Assert.IsAssignableFrom<Akbura.ComponentTree.Parameter>(
                owner.GetType().GetField("DataProperty")!.GetValue(null));
            var factory = Assert.Single(owner.GetType().GetMethods(BindingFlags.Static | BindingFlags.NonPublic),
                method => method.Name.StartsWith("__AkburaCreateParameter_Data_", StringComparison.Ordinal));
            var recreated = Assert.IsAssignableFrom<Akbura.ComponentTree.Parameter>(
                factory.Invoke(null, [descriptor.AvaloniaProperty]));
            Assert.Same(descriptor.AvaloniaProperty, recreated.AvaloniaProperty);
            Assert.Same(backing, Get(owner, "Data"));
            source.Add(2);
            Assert.Equal(new[] { 1, 2 }, Assert.IsAssignableFrom<IList<int>>(backing));
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ContentClear_RemovesLogicalChildren(bool structural)
    {
        const string child = "using System.Collections.Generic; using Avalonia.Controls; param IList<Control> Content; <Border/>";
        var assembly = Compile("<ItemsPanel/>", child, structural);
        await OnUi(() =>
        {
            var owner = New(assembly, "ItemsPanel");
            var first = new Border();
            var source = new ObservableCollection<Control>([first]);
            Set(owner, "Content", source);
            var owned = Assert.IsAssignableFrom<IList<Control>>(Get(owner, "Content"));
            Assert.Contains(first, owner.GetLogicalChildren());
            owned.Clear();
            Assert.Empty(source);
            Assert.DoesNotContain(first, owner.GetLogicalChildren());
        });
    }

    [Fact]
    public void OrdinaryRequiredParameter_IsStillDiagnosed()
    {
        var fixture = AkcssActivatorPlannerTests.CreateFixture("<ItemsPanel/>", Owners);
        var tree = AkburaSyntaxTree.ParseText("using System.Collections.Generic; param IList<int> Data; param string Required;", "ItemsPanel.akbura");
        var compilation = new AkburaCompilation(fixture.CSharpCompilation, [fixture.ComponentTree, tree], rootNamespace: "Demo");
        var errors = compilation.GetSemanticModel(fixture.ComponentTree).GetSemanticDiagnostics(fixture.ComponentTree.GetRoot());
        var required = Assert.Single(errors.Where(e => e.Code == ErrorCodes.AKBURA_SEMANTIC_MarkupRequiredParameterNotSet));
        Assert.Equal("Required", required.Parameters[0]);
    }

    private static Assembly Compile(string parentSource, string childSource, bool structural)
    {
        var fixture = AkcssActivatorPlannerTests.CreateFixture(parentSource, Owners);
        var child = AkburaSyntaxTree.ParseText(childSource, "ItemsPanel.akbura");
        var documents = new[] { fixture.ComponentTree, child };
        var compilation = new AkburaCompilation(fixture.CSharpCompilation, documents, rootNamespace: "Demo");
        var csharp = fixture.CSharpCompilation.WithAssemblyName("CollectionParams_" + Guid.NewGuid().ToString("N"));
        var options = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        if (structural) options = options.WithPreprocessorSymbols("DEBUG");
        foreach (var document in documents)
        {
            var root = document.GetRoot();
            Assert.Empty(root.GetDiagnostics());
            var model = compilation.GetSemanticModel(document);
            var symbol = Assert.IsAssignableFrom<IAkburaComponentSymbol>(model.GetSymbolInfo(root).Symbol);
            var errors = model.GetSemanticDiagnostics(root).Where(d => d.Severity == AkburaDiagnosticSeverity.Error).ToArray();
            Assert.True(errors.Length == 0, string.Join(Environment.NewLine, errors.Select(d => d.Code + ": " + d.Message)));
            var generated = ComponentDocumentWriter.Generate(symbol, model, document.FilePath,
                new Dictionary<AkburaSyntax, string>(),
                mode: structural ? ComponentGenerationMode.DebugStructural : ComponentGenerationMode.ReleaseDirect);
            csharp = csharp.AddSyntaxTrees(CSharpSyntaxTree.ParseText(generated, options, document.FilePath + ".g.cs"));
        }
        using var output = new MemoryStream();
        var result = csharp.Emit(output);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        return Assembly.Load(output.ToArray());
    }

    private static AkburaControl New(Assembly assembly, string name) =>
        Assert.IsAssignableFrom<AkburaControl>(Activator.CreateInstance(assembly.GetType("Demo." + name)!));
    private static PropertyInfo Property(object owner, string name) => owner.GetType().GetProperty(name,
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
    private static object? Get(object owner, string name) => Property(owner, name).GetValue(owner);
    private static void Set(object owner, string name, object? value) => Property(owner, name).SetValue(owner, value);
    private static void Drain() => Dispatcher.UIThread.RunJobs();
    private static void AssertRows(AkburaControl owner, int[] values)
    {
        var panel = Assert.IsType<StackPanel>(owner.Child);
        Assert.Equal(values.Length + 1, panel.Children.Count);
        Assert.Equal("Count: " + values.Length, Assert.IsType<TextBlock>(panel.Children[0]).Text);
        for (var i = 0; i < values.Length; i++)
            Assert.Equal($"Item: {values[i]}, index: {i}", Assert.IsType<TextBlock>(panel.Children[i + 1]).Text);
    }
    private static async Task OnUi(Action action)
    {
        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            action();
            return true;
        }, CancellationToken.None);
    }

    private const string Owners = """
        using Akbura;
        using Akbura.Engine;
        namespace Demo;
        public partial class PlannerView : AkburaControl
        {
            public PlannerView() : base(AkburaEngine.Empty) { }
        }
        public partial class ItemsPanel : AkburaControl
        {
            public ItemsPanel() : base(AkburaEngine.Empty) { }
        }
        """;
}

// Public so the native reflection-binding path has the same accessibility as a ViewModel.
public sealed class CollectionParameterTestViewModel : System.ComponentModel.INotifyPropertyChanged
{
    private IList<int> _items = new ObservableCollection<int>();
    public IList<int> Items
    {
        get => _items;
        set { _items = value; PropertyChanged?.Invoke(this, new(nameof(Items))); }
    }
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}
