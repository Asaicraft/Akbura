using Akbura.ComponentTree;
using Akbura.Language;
using Akbura.Language.CodeGeneration;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Avalonia.Controls;
using Avalonia.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Reflection;

namespace Akbura.UnitTests;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed partial class MarkupExpressionBindingRuntimeTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task GeneratedBindings_EvaluateAndObserveWithoutRepeatingGetters(int mode)
    {
        var compiled = Compile("<TextBlock Text=${x.bind Vm.Name} />", mode);
        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            dynamic component = Activator.CreateInstance(compiled.Assembly.GetType("Demo.ExpressionDemo")!)!;
            component.InitializeForTest();
            dynamic vm = component.ModelForTest;
            var text = Assert.IsType<TextBlock>(((AkburaControl)component).Child);
            Assert.Equal("Initial", text.Text);
            Assert.Equal(1, (int)vm.NameReads);
            Assert.Equal(1, (int)vm.Adds);
            Assert.True(((AkburaControl)component).IsInitialized);
            component.InitializeForTest();
            Assert.Same(text, ((AkburaControl)component).Child);
            Assert.Equal(1, (int)vm.NameReads);
            Assert.Equal(1, (int)vm.Adds);
            vm.Unrelated();
            Assert.Equal(1, (int)vm.NameReads);
            vm.Name = "Updated";
            Assert.Equal("Updated", text.Text);
            Assert.Equal(2, (int)vm.NameReads);
            component.RenderAgain();
            Assert.Equal(1, (int)vm.Adds);
            Assert.Equal(0, (int)vm.Removes);
            vm.Raise(null);
            Assert.Equal("Updated", text.Text);
            vm.Raise("");
            Assert.Equal("Updated", text.Text);
            return true;
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData("$\"{Vm.FirstName} {Vm.LastName}\"")]
    [InlineData("Vm.Profile?.Name ?? \"Unknown\"")]
    [InlineData("Vm.Profile?.Profile.Name ?? \"Unknown\"")]
    [InlineData("Vm.Enabled ? Vm.FirstName : Vm.LastName")]
    [InlineData("Vm.Items[Vm.SelectedIndex].Name")]
    [InlineData("Vm.Count.ToString()")]
    [InlineData("Vm.Price.ToString(\"F2\")")]
    [InlineData("Vm.Count switch { 0 => Vm.FirstName, _ => Vm.LastName }")]
    [InlineData("$\"{Vm.Name} ({Vm.Name.Length})\"")]
    public void GeneratedExpressions_CompileInEveryMode(string expression)
    {
        for (var mode = 0; mode < 3; mode++) Compile("<TextBlock Text=${x.bind " + expression + "} />", mode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task GeneratedInterpolation_RepeatedReadsAndPartialMethodsRemainReactive(int mode)
    {
        var compiled = Compile("<StackPanel><TextBlock Text=${x.bind $\"{Vm.FirstName} {Vm.LastName}\"} /><TextBlock Text=${x.bind $\"{Vm.Name} ({Vm.Name.Length})\"} /><TextBlock Text=${x.bind Vm.Format(Vm.FirstName, Vm.LastName)} /></StackPanel>", mode);
        _output.WriteLine("Actual generated expression-binding calls:\n" + string.Join("\n", compiled.Source.Split('\n')
            .Where(line => line.Contains(".__AkburaBindExpression<", StringComparison.Ordinal) ||
                line.Contains(".ReconcileExpressionBinding<", StringComparison.Ordinal))));
        await OnUi(() =>
        {
            dynamic component = Create(compiled);
            var panel = Assert.IsType<StackPanel>(((AkburaControl)component).Child);
            dynamic vm = component.ModelForTest;
            Assert.Equal("First Last", ((TextBlock)panel.Children[0]).Text);
            Assert.Equal("Initial (7)", ((TextBlock)panel.Children[1]).Text);
            Assert.Equal(2, (int)vm.NameReads);
            Assert.Equal(1, (int)vm.Calls);
            vm.FirstName = "Changed";
            Assert.Equal("Changed Last", ((TextBlock)panel.Children[0]).Text);
            Assert.Equal("ChangedLast", ((TextBlock)panel.Children[2]).Text);
            Assert.Equal(2, (int)vm.Calls);
            vm.LastName = "Again";
            Assert.Equal("Changed Again", ((TextBlock)panel.Children[0]).Text);
            Assert.Equal(3, (int)vm.Calls);
            return true;
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task GeneratedNestedNullTransitions_ReleaseObsoleteReceivers(int mode)
    {
        var compiled = Compile("<TextBlock Text=${x.bind Vm.Profile?.Profile?.Name ?? \"Unknown\"} />", mode);
        await OnUi(() =>
        {
            dynamic component = Create(compiled);
            dynamic vm = component.ModelForTest;
            var text = Assert.IsType<TextBlock>(((AkburaControl)component).Child);
            Assert.Equal("Unknown", text.Text);
            dynamic profile = vm.Create();
            dynamic address = vm.Create();
            address.Name = "First city";
            profile.Profile = address;
            vm.Profile = profile;
            Assert.Equal("First city", text.Text);
            address.Name = "Updated city";
            Assert.Equal("Updated city", text.Text);
            dynamic nextAddress = vm.Create();
            nextAddress.Name = "Second city";
            profile.Profile = nextAddress;
            Assert.Equal("Second city", text.Text);
            Assert.Equal((int)address.Adds, (int)address.Removes);
            address.Name = "Retired";
            Assert.Equal("Second city", text.Text);
            vm.Profile = null;
            Assert.Equal("Unknown", text.Text);
            Assert.Equal((int)profile.Adds, (int)profile.Removes);
            Assert.Equal((int)nextAddress.Adds, (int)nextAddress.Removes);
            vm.Profile = profile;
            Assert.Equal("Second city", text.Text);
            return true;
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task GeneratedConditionalAndSwitch_ObserveOnlyExecutedBranches(int mode)
    {
        var compiled = Compile("<StackPanel><TextBlock Text=${x.bind Vm.Enabled ? Vm.Name : Vm.Profile!.Name} /><TextBlock Text=${x.bind Vm.Count switch { 0 when Vm.Enabled => Vm.Name, _ => Vm.Profile!.Name }} /></StackPanel>", mode);
        await OnUi(() =>
        {
            dynamic component = Activator.CreateInstance(compiled.Assembly.GetType("Demo.ExpressionDemo")!)!;
            dynamic vm = component.ModelForTest;
            dynamic inactive = vm.Create();
            inactive.Name = "Guest";
            vm.Profile = inactive;
            vm.Enabled = true;
            component.InitializeForTest();
            var panel = Assert.IsType<StackPanel>(((AkburaControl)component).Child);
            Assert.Equal(0, (int)inactive.NameReads);
            Assert.Equal(0, (int)inactive.Adds);
            vm.Enabled = false;
            Assert.Equal("Guest", ((TextBlock)panel.Children[0]).Text);
            Assert.Equal("Guest", ((TextBlock)panel.Children[1]).Text);
            var inactiveReads = (int)vm.NameReads;
            vm.Name = "Admin";
            Assert.Equal(inactiveReads, (int)vm.NameReads);
            vm.Enabled = true;
            Assert.Equal("Admin", ((TextBlock)panel.Children[0]).Text);
            Assert.Equal((int)inactive.Adds, (int)inactive.Removes);
            return true;
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task GeneratedIndexer_ReconcilesSelectionAndCollectionMutations(int mode)
    {
        var compiled = Compile("<TextBlock Text=${x.bind Vm.Items[Vm.SelectedIndex].Name} />", mode);
        await OnUi(() =>
        {
            dynamic component = Activator.CreateInstance(compiled.Assembly.GetType("Demo.ExpressionDemo")!)!;
            dynamic vm = component.ModelForTest;
            dynamic first = vm.Create(), second = vm.Create();
            first.Name = "First"; second.Name = "Second";
            vm.Items.Add(first); vm.Items.Add(second);
            component.InitializeForTest();
            var text = Assert.IsType<TextBlock>(((AkburaControl)component).Child);
            Assert.Equal("First", text.Text);
            vm.SelectedIndex = 1;
            Assert.Equal("Second", text.Text);
            Assert.Equal((int)first.Adds, (int)first.Removes);
            vm.Items.Insert(0, first);
            Assert.Equal("First", text.Text);
            vm.Items[1] = second;
            Assert.Equal("Second", text.Text);
            vm.Items.RemoveAt(0);
            Assert.Equal("Second", text.Text);
            dynamic replacement = vm.Create();
            replacement.Name = "Replacement";
            vm.Items[1] = replacement;
            Assert.Equal("Replacement", text.Text);
            second.Name = "Retired selected";
            Assert.Equal("Replacement", text.Text);
            return true;
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task GeneratedParameterReplacementAndDetach_ReleaseCallbacks(int mode)
    {
        var compiled = Compile("<TextBlock Text=${x.bind Vm.Name} />", mode, "param Model Vm;");
        await OnUi(() =>
        {
            dynamic component = Activator.CreateInstance(compiled.Assembly.GetType("Demo.ExpressionDemo")!)!;
            var type = ((object)component).GetType();
            var descriptor = Assert.IsAssignableFrom<Parameter>(type.GetField("VmProperty")!.GetValue(null));
            dynamic first = Activator.CreateInstance(compiled.Assembly.GetType("Demo.Model")!)!;
            ((AkburaControl)component).SetValue(descriptor.AvaloniaProperty, (object)first);
            component.InitializeForTest();
            Assert.True(((AkburaControl)component).IsInitialized);
            var window = new Window { Content = (AkburaControl)component };
            try
            {
                window.Show();
                var text = Assert.IsType<TextBlock>(((AkburaControl)component).Child);
                Assert.True((int)first.Adds - (int)first.Removes == 1, (string)first.ActiveHandlers);
                dynamic second = first.Create();
                second.Name = "Second";
                ((AkburaControl)component).SetValue(descriptor.AvaloniaProperty, (object)second);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("Second", text.Text);
                Assert.Equal((int)first.Adds, (int)first.Removes);
                Assert.True((int)second.Adds - (int)second.Removes == 1, (string)second.ActiveHandlers);
                for (var cycle = 0; cycle < 3; cycle++)
                {
                    window.Content = null;
                    Dispatcher.UIThread.RunJobs();
                    Assert.Equal((int)second.Adds, (int)second.Removes);
                    var detached = "Detached " + cycle;
                    var reads = (int)second.NameReads;
                    second.Name = detached;
                    Assert.NotEqual(detached, text.Text);
                    Assert.Equal(reads, (int)second.NameReads);
                    window.Content = (AkburaControl)component;
                    Dispatcher.UIThread.RunJobs();
                    Assert.Same(text, ((AkburaControl)component).Child);
                    Assert.Equal(detached, text.Text);
                    Assert.Equal(reads + 1, (int)second.NameReads);
                    Assert.True((int)second.Adds - (int)second.Removes == 1, (string)second.ActiveHandlers);
                }
                window.Content = null;
                dynamic third = second.Create();
                third.Name = "Replacement while detached";
                ((AkburaControl)component).SetValue(descriptor.AvaloniaProperty, (object)third);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal((int)second.Adds, (int)second.Removes);
                Assert.Equal(0, (int)third.Adds);
                Assert.Equal(0, (int)third.NameReads);
                third.Name = "Changed before reattachment";
                Assert.NotEqual("Changed before reattachment", text.Text);
                window.Content = (AkburaControl)component;
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("Changed before reattachment", text.Text);
                Assert.Equal(1, (int)third.NameReads);
                Assert.Equal(1, (int)third.Adds - (int)third.Removes);
            }
            finally { window.Close(); }
            return true;
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task GeneratedSharedSource_OnlyDetachedOwnerIsSuspended(int mode)
    {
        var compiled = Compile("<TextBlock Text=${x.bind Vm.Name} />", mode, "param Model Vm;");
        await OnUi(() =>
        {
            var type = compiled.Assembly.GetType("Demo.ExpressionDemo")!;
            var descriptor = Assert.IsAssignableFrom<Parameter>(type.GetField("VmProperty")!.GetValue(null));
            dynamic vm = Activator.CreateInstance(compiled.Assembly.GetType("Demo.Model")!)!;
            AkburaControl CreateOwner()
            {
                dynamic owner = Activator.CreateInstance(type)!;
                ((AkburaControl)owner).SetValue(descriptor.AvaloniaProperty, (object)vm);
                owner.InitializeForTest();
                return (AkburaControl)owner;
            }
            var first = CreateOwner();
            var second = CreateOwner();
            var panel = new StackPanel { Children = { first, second } };
            var window = new Window { Content = panel };
            try
            {
                window.Show();
                var firstText = Assert.IsType<TextBlock>(first.Child);
                var secondText = Assert.IsType<TextBlock>(second.Child);
                Assert.Equal(2, (int)vm.Adds - (int)vm.Removes);
                panel.Children.Remove(first);
                Assert.Equal(1, (int)vm.Adds - (int)vm.Removes);
                var reads = (int)vm.NameReads;
                vm.Name = "Only attached owner";
                Assert.Equal("Initial", firstText.Text);
                Assert.Equal("Only attached owner", secondText.Text);
                Assert.Equal(reads + 1, (int)vm.NameReads);
                panel.Children.Add(first);
                Assert.Equal("Only attached owner", firstText.Text);
                Assert.Equal(2, (int)vm.Adds - (int)vm.Removes);
            }
            finally { window.Close(); }
            Assert.Equal((int)vm.Adds, (int)vm.Removes);
            return true;
        });
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task GeneratedStructuralOwner_DisposalCannotBeUndoneByReattachment(int mode)
    {
        var compiled = Compile("<TextBlock Text=${x.bind Vm.Name} />", mode);
        await OnUi(() =>
        {
            dynamic component = Create(compiled);
            dynamic vm = component.ModelForTest;
            var control = (AkburaControl)component;
            var window = new Window { Content = control };
            try
            {
                window.Show();
                var text = Assert.IsType<TextBlock>(control.Child);
                var renderState = Assert.IsType<Akbura.HotReload.AkburaRenderState>(
                    ((object)component).GetType().GetField("__akburaRenderState", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(component));
                renderState.Dispose();
                Assert.Equal((int)vm.Adds, (int)vm.Removes);
                var restored = text.Text;
                var reads = (int)vm.NameReads;
                window.Content = null;
                window.Content = control;
                Dispatcher.UIThread.RunJobs();
                vm.Name = "Retired";
                Assert.Equal(restored, text.Text);
                Assert.Equal(reads, (int)vm.NameReads);
                Assert.Equal((int)vm.Adds, (int)vm.Removes);
                renderState.Dispose();
            }
            finally { window.Close(); }
            return true;
        });
    }

    private static dynamic Create(CompiledComponent compiled)
    {
        dynamic component = Activator.CreateInstance(compiled.Assembly.GetType("Demo.ExpressionDemo")!)!;
        component.InitializeForTest();
        Assert.True(((AkburaControl)component).IsInitialized);
        return component;
    }

    private static async Task OnUi(Func<bool> action) =>
        await AvaloniaHeadlessTestSession.GetSession().Dispatch(action, CancellationToken.None);

    internal static CompiledComponent Compile(string markup, int mode, string declarations = "state Model Vm = new();", string? additionalHost = null)
    {
        var options = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        if (mode == 1) options = options.WithPreprocessorSymbols("DEBUG");
        var host = Host + (additionalHost ?? "");
        var csharp = CSharpCompilation.Create("ExpressionBinding_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(host, options)], SymbolTests.CreateAvaloniaReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        var tree = AkburaSyntaxTree.ParseText("using Avalonia.Controls; using Demo; " + declarations + markup, "ExpressionDemo.akbura");
        var compilation = new AkburaCompilation(csharp, [tree], rootNamespace: "Demo");
        var model = compilation.GetSemanticModel(tree);
        var errors = model.GetSemanticDiagnostics(tree.GetRoot()).Where(d => d.Severity == AkburaDiagnosticSeverity.Error).ToArray();
        Assert.True(errors.Length == 0, string.Join("\n", errors.Select(d => d.Code + ": " + d.Message)));
        var component = Assert.IsAssignableFrom<IAkburaComponentSymbol>(model.GetSymbolInfo(tree.GetRoot()).Symbol);
        var source = ComponentDocumentWriter.Generate(component, model, tree.FilePath,
            new Dictionary<AkburaSyntax, string>(), mode: (ComponentGenerationMode)mode).ToString();
        var result = csharp.AddSyntaxTrees(CSharpSyntaxTree.ParseText(source, options));
        using var output = new MemoryStream();
        var emitted = result.Emit(output);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics) + "\n" + source);
        return new CompiledComponent(Assembly.Load(output.ToArray()), source);
    }

    internal sealed record CompiledComponent(Assembly Assembly, string Source);

    internal const string Host = """
        using System;
        using System.ComponentModel;
        using System.Collections.ObjectModel;
        using Akbura;
        namespace Demo;
        public sealed class Model : INotifyPropertyChanged
        {
            private PropertyChangedEventHandler? _changed;
            public int Adds, Removes, NameReads, CountReads, Calls;
            public bool NotifyDuringSubscribe, ThrowOnRemove;
            public event PropertyChangedEventHandler? PropertyChanged
            {
                add
                {
                    Adds++;
                    _changed += value;
                    if (NotifyDuringSubscribe) value?.Invoke(this, new(nameof(Name)));
                }
                remove
                {
                    Removes++;
                    _changed -= value;
                    if (ThrowOnRemove) throw new InvalidOperationException("Subscription removal failed.");
                }
            }
            public string ActiveHandlers => string.Join("; ", Array.ConvertAll(
                _changed?.GetInvocationList() ?? Array.Empty<Delegate>(),
                handler => handler.Method.DeclaringType?.FullName + "." + handler.Method.Name));
            private string _name = "Initial", _first = "First", _last = "Last";
            private Model? _profile;
            private bool _enabled;
            private int _selectedIndex, _count;
            public string Name { get { NameReads++; return _name; } set { _name = value; Raise(nameof(Name)); } }
            public string FirstName { get => _first; set { _first = value; Raise(nameof(FirstName)); } }
            public string LastName { get => _last; set { _last = value; Raise(nameof(LastName)); } }
            public Model? Profile { get => _profile; set { _profile = value; Raise(nameof(Profile)); } }
            public bool Enabled { get => _enabled; set { _enabled = value; Raise(nameof(Enabled)); } }
            public int SelectedIndex { get => _selectedIndex; set { _selectedIndex = value; Raise(nameof(SelectedIndex)); } }
            public int Count { get { CountReads++; return _count; } set { _count = value; Raise(nameof(Count)); } }
            public decimal Price { get; set; } = 1.5m;
            private ObservableCollection<Model> _items = new();
            public ObservableCollection<Model> Items { get => _items; set { _items = value; Raise(nameof(Items)); } }
            public Avalonia.Controls.TextBox Input { get; } = new();
            public void ReplaceItems() => Items = new();
            public string Format(string first, string last) { Calls++; return first + last; }
            public Model Create() => new();
            public void Raise(string? name) => _changed?.Invoke(this, new(name));
            public void Unrelated() => Raise("Other");
        }
        public partial class ExpressionDemo : AkburaControl
        {
            public ExpressionDemo() : base(Akbura.Engine.AkburaEngine.Empty) { }
            public Model ModelForTest => Vm;
            public void InitializeForTest() => InitializeIfNeeded();
            public void RenderAgain() => Update();
        }
        """;
}
