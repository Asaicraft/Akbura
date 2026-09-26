using Akbura.ComponentTree;
using Akbura.Language;
using Akbura.Language.CodeGeneration;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Reflection;

namespace Akbura.UnitTests;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class DirectionalStateBindingRuntimeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OutStatesTrackInstanceTailFromStaticRoot(bool structural)
    {
        var assembly = CompileStaticRoot(structural);

        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            var component = Assert.IsAssignableFrom<AkburaControl>(
                Activator.CreateInstance(assembly.GetType("Demo.StaticRootDemo")!));
            var componentType = component.GetType();
            componentType.GetMethod("InitializeForTest")!.Invoke(component, null);

            var globalsType = assembly.GetType("Demo.Globals")!;
            var current = globalsType.GetProperty("Current")!.GetValue(null)!;
            var currentType = current.GetType();
            var text = Assert.IsType<TextBlock>(component.Child);

            Assert.Same(
                current,
                componentType.GetProperty("CurrentStaticState")!.GetValue(component));
            Assert.Equal("Initial static", text.Text);

            currentType.GetProperty("Name")!.SetValue(current, "Updated static");
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("Updated static", text.Text);
            return true;
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OutStateReadsFromTheGraphObservedByCompiledPath(bool structural)
    {
        var assembly = CompileSwitchingGraph(structural);

        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            var component = Assert.IsAssignableFrom<AkburaControl>(
                Activator.CreateInstance(assembly.GetType("Demo.SwitchingGraphDemo")!));
            var componentType = component.GetType();
            componentType.GetMethod("InitializeForTest")!.Invoke(component, null);

            var text = Assert.IsType<TextBlock>(component.Child);
            var first = componentType.GetProperty("First")!.GetValue(component)!;
            var second = componentType.GetProperty("Second")!.GetValue(component)!;
            var vmType = first.GetType();

            Assert.Equal("First", text.Text);
            Assert.Equal(
                42L,
                componentType.GetProperty("CurrentNumberState")!.GetValue(component));
            Assert.Equal(1, componentType.GetProperty("NextVmReadCount")!.GetValue(component));
            Assert.Equal(1, vmType.GetProperty("PropertyChangedSubscriberCount")!.GetValue(first));
            Assert.Equal(0, vmType.GetProperty("PropertyChangedSubscriberCount")!.GetValue(second));

            vmType.GetProperty("Name")!.SetValue(first, "Updated first");
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("Updated first", text.Text);
            Assert.Equal(1, componentType.GetProperty("NextVmReadCount")!.GetValue(component));
            return true;
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OutStatesTrackParameterRootAndAvaloniaPropertyReplacement(bool structural)
    {
        var assembly = CompileParameterRoot(structural);

        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            var component = Assert.IsAssignableFrom<AkburaControl>(
                Activator.CreateInstance(assembly.GetType("Demo.ParameterRootDemo")!));
            var componentType = component.GetType();
            var vmType = assembly.GetType("Demo.DemoVm")!;
            var parameter = Assert.IsAssignableFrom<Parameter>(
                componentType.GetField("VmProperty")!.GetValue(null));
            var initial = Activator.CreateInstance(vmType)!;
            vmType.GetProperty("Name")!.SetValue(initial, "Initial parameter");

            component.SetValue(parameter.AvaloniaProperty, initial);
            componentType.GetMethod("InitializeForTest")!.Invoke(component, null);

            var text = Assert.IsType<TextBlock>(component.Child);
            Assert.Equal("Initial parameter", text.Text);
            Assert.Same(
                initial,
                componentType.GetProperty("CurrentParameterState")!.GetValue(component));
            Assert.True(Assert.IsType<int>(
                vmType.GetProperty("NameReadCount")!.GetValue(initial)) > 0);

            vmType.GetProperty("Name")!.SetValue(initial, "Updated parameter");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Updated parameter", text.Text);

            var replacement = Activator.CreateInstance(vmType)!;
            vmType.GetProperty("Name")!.SetValue(replacement, "Replacement parameter");
            component.SetValue(parameter.AvaloniaProperty, replacement);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("Replacement parameter", text.Text);
            Assert.Same(
                replacement,
                componentType.GetProperty("CurrentParameterState")!.GetValue(component));

            vmType.GetProperty("Name")!.SetValue(initial, "Stale parameter");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Replacement parameter", text.Text);
            return true;
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DynamicIndexerRebindsWhenParameterChanges(bool structural)
    {
        var assembly = CompileDynamicParameterIndex(structural);

        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            var component = Assert.IsAssignableFrom<AkburaControl>(
                Activator.CreateInstance(assembly.GetType("Demo.DynamicIndexDemo")!));
            var componentType = component.GetType();
            var vmType = assembly.GetType("Demo.DemoVm")!;
            var itemType = assembly.GetType("Demo.DemoItem")!;
            var vmParameter = Assert.IsAssignableFrom<Parameter>(
                componentType.GetField("VmProperty")!.GetValue(null));
            var indexParameter = Assert.IsAssignableFrom<Parameter>(
                componentType.GetField("IndexProperty")!.GetValue(null));
            var vm = vmType.GetMethod("Create")!.Invoke(null, null)!;

            component.SetValue(vmParameter.AvaloniaProperty, vm);
            component.SetValue(indexParameter.AvaloniaProperty, 0);
            componentType.GetMethod("InitializeForTest")!.Invoke(component, null);

            var root = Assert.IsType<StackPanel>(component.Child);
            var parameterText = Assert.IsType<TextBlock>(root.Children[0]);
            var componentPropertyText = Assert.IsType<TextBlock>(root.Children[1]);
            Assert.Equal("First", parameterText.Text);
            Assert.Equal("First", componentPropertyText.Text);

            component.SetValue(indexParameter.AvaloniaProperty, 1);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Second", parameterText.Text);
            Assert.Equal("First", componentPropertyText.Text);

            componentType.GetProperty("SelectedIndex")!.SetValue(component, 1);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Second", componentPropertyText.Text);

            var items = Assert.IsAssignableFrom<System.Collections.IList>(
                vmType.GetProperty("Items")!.GetValue(vm));
            itemType.GetProperty("Name")!.SetValue(items[0], "Stale first");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Second", parameterText.Text);
            Assert.Equal("Second", componentPropertyText.Text);

            itemType.GetProperty("Name")!.SetValue(items[1], "Updated second");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Updated second", parameterText.Text);
            Assert.Equal("Updated second", componentPropertyText.Text);

            component.SetValue(indexParameter.AvaloniaProperty, 0);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Stale first", parameterText.Text);
            Assert.Equal("Updated second", componentPropertyText.Text);

            componentType.GetProperty("SelectedIndex")!.SetValue(component, 0);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Stale first", componentPropertyText.Text);
            return true;
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DirectionalStatesSynchronizeAndRebind(bool structural)
    {
        var assembly = Compile(structural);

        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            var component = Assert.IsAssignableFrom<AkburaControl>(
                Activator.CreateInstance(assembly.GetType("Demo.StateDemo")!));
            var componentType = component.GetType();
            componentType.GetMethod("InitializeForTest")!.Invoke(component, null);

            var root = Assert.IsType<StackPanel>(component.Child);
            var nameText = Assert.IsType<TextBlock>(root.Children[0]);
            var fullNameText = Assert.IsType<TextBlock>(root.Children[1]);
            var surnameText = Assert.IsType<TextBlock>(root.Children[2]);
            var nestedText = Assert.IsType<TextBlock>(root.Children[3]);
            var indexedText = Assert.IsType<TextBlock>(root.Children[4]);
            var signalText = Assert.IsType<TextBlock>(root.Children[5]);
            var writeOnlyText = Assert.IsType<TextBlock>(root.Children[6]);
            var setName = Assert.IsType<Button>(root.Children[7]);
            var setRemoteName = Assert.IsType<Button>(root.Children[8]);
            var originalVm = componentType.GetProperty("CurrentVm")!.GetValue(component)!;
            var vmType = originalVm.GetType();

            Assert.Equal("Initial", nameText.Text);
            Assert.Equal("Initial Person", fullNameText.Text);
            Assert.Equal("Seed", surnameText.Text);
            Assert.Equal("Child", nestedText.Text);
            Assert.Equal("First", indexedText.Text);
            Assert.Null(signalText.Text);
            Assert.Null(writeOnlyText.Text);
            Assert.Equal(0, vmType.GetProperty("WriteOnlySetCount")!.GetValue(originalVm));

            var initialSubscriberCount = Assert.IsType<int>(
                vmType.GetProperty("PropertyChangedSubscriberCount")!.GetValue(originalVm));
            Assert.True(initialSubscriberCount > 0);
            var firstWindow = new Window { Content = component };
            firstWindow.Show();
            Dispatcher.UIThread.RunJobs();
            firstWindow.Content = null;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, vmType.GetProperty("PropertyChangedSubscriberCount")!.GetValue(originalVm));
            firstWindow.Close();
            vmType.GetProperty("Name")!.SetValue(originalVm, "Detached");
            Assert.Equal("Initial", nameText.Text);

            var currentWindow = new Window { Content = component };
            currentWindow.Show();
            Dispatcher.UIThread.RunJobs();
            root = Assert.IsType<StackPanel>(component.Child);
            nameText = Assert.IsType<TextBlock>(root.Children[0]);
            fullNameText = Assert.IsType<TextBlock>(root.Children[1]);
            surnameText = Assert.IsType<TextBlock>(root.Children[2]);
            nestedText = Assert.IsType<TextBlock>(root.Children[3]);
            indexedText = Assert.IsType<TextBlock>(root.Children[4]);
            signalText = Assert.IsType<TextBlock>(root.Children[5]);
            writeOnlyText = Assert.IsType<TextBlock>(root.Children[6]);
            setName = Assert.IsType<Button>(root.Children[7]);
            setRemoteName = Assert.IsType<Button>(root.Children[8]);
            Assert.Equal(initialSubscriberCount,
                vmType.GetProperty("PropertyChangedSubscriberCount")!.GetValue(originalVm));
            Assert.Equal("Detached", nameText.Text);
            Assert.Equal("Detached Person", fullNameText.Text);

            setName.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Event", vmType.GetProperty("Name")!.GetValue(originalVm));
            Assert.Equal("Event", nameText.Text);

            setRemoteName.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Remote event", nameText.Text);
            Assert.Equal("Remote event Person", fullNameText.Text);

            componentType.GetMethod("SetWriteOnlyState")!.Invoke(component, ["First write"]);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("First write", vmType.GetProperty("WrittenValue")!.GetValue(originalVm));
            Assert.Equal(1, vmType.GetProperty("WriteOnlySetCount")!.GetValue(originalVm));
            Assert.Equal("First write", writeOnlyText.Text);

            componentType.GetMethod("SetNameState")!.Invoke(component, ["  Local  "]);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("Local", vmType.GetProperty("Name")!.GetValue(originalVm));
            Assert.Equal("Local", nameText.Text);
            Assert.Equal("Local Person", fullNameText.Text);

            vmType.GetProperty("Name")!.SetValue(originalVm, "Remote");
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("Remote", nameText.Text);
            Assert.Equal("Remote Person", fullNameText.Text);

            componentType.GetMethod("SetSurnameState")!.Invoke(component, ["Local surname"]);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Local surname", vmType.GetProperty("Surname")!.GetValue(originalVm));

            vmType.GetProperty("Surname")!.SetValue(originalVm, "Remote surname");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Local surname", surnameText.Text);

            var originalChild = vmType.GetProperty("Child")!.GetValue(originalVm)!;
            vmType.GetProperty("Name")!.SetValue(originalChild, "Child update");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Child update", nestedText.Text);

            var replacementChild = Activator.CreateInstance(vmType)!;
            vmType.GetProperty("Name")!.SetValue(replacementChild, "New child");
            componentType.GetMethod("SetChild")!.Invoke(component, [replacementChild]);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("New child", nestedText.Text);
            vmType.GetProperty("Name")!.SetValue(originalChild, "Stale child");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("New child", nestedText.Text);

            componentType.GetMethod("ClearChild")!.Invoke(component, null);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("New child", nestedText.Text);
            var restoredChild = Activator.CreateInstance(vmType)!;
            vmType.GetProperty("Name")!.SetValue(restoredChild, "Restored child");
            componentType.GetMethod("SetChild")!.Invoke(component, [restoredChild]);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Restored child", nestedText.Text);

            var replacementItem = Activator.CreateInstance(vmType)!;
            vmType.GetProperty("Name")!.SetValue(replacementItem, "Replacement item");
            componentType.GetMethod("ReplaceFirstItem")!.Invoke(component, [replacementItem]);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Replacement item", indexedText.Text);

            componentType.GetMethod("SetSelectedIndex")!.Invoke(component, [1]);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Second", indexedText.Text);

            componentType.GetMethod("EmitSignal")!.Invoke(component, ["Signal value"]);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Signal value", signalText.Text);

            var replacement = vmType.GetMethod("CreateRoot")!.Invoke(null, null)!;
            vmType.GetProperty("Name")!.SetValue(replacement, "Replacement");
            vmType.GetProperty("Surname")!.SetValue(replacement, "Replacement surname");
            componentType.GetMethod("ReplaceVm")!.Invoke(component, [replacement]);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("Replacement", nameText.Text);
            Assert.Equal("Replacement Person", fullNameText.Text);
            Assert.Equal("Local surname", surnameText.Text);

            vmType.GetProperty("Name")!.SetValue(originalVm, "Stale");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Replacement", nameText.Text);

            componentType.GetMethod("SetSurnameState")!.Invoke(component, ["Current target"]);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Current target", vmType.GetProperty("Surname")!.GetValue(replacement));
            currentWindow.Close();
            return true;
        }, CancellationToken.None);
    }

    private static Assembly Compile(bool structural)
    {
        const string componentSource =
            """
            using Avalonia.Controls;
            namespace Demo;

            state DemoVm vm = DemoVm.CreateRoot();
            state string name = bind vm.Name;
            state string fullName = out vm.FullName;
            state string surname = in vm.Surname;
            state string nested = bind vm.Child.Name;
            state int selectedIndex = 0;
            state string indexed = bind vm.Items[selectedIndex].Name;
            state string signal = out vm.Signal;
            state string writeOnly = in vm.WriteOnly;

            <StackPanel>
                <TextBlock Text={name} />
                <TextBlock Text={fullName} />
                <TextBlock Text={surname} />
                <TextBlock Text={nested} />
                <TextBlock Text={indexed} />
                <TextBlock Text={signal} />
                <TextBlock Text={writeOnly} />
                <Button Click={() => { name = "  Event  "; }}>Set state</Button>
                <Button Click={() => { vm.Name = "Remote event"; }}>Set source</Button>
            </StackPanel>
            """;
        const string hostSource =
            """
            using System;
            using System.ComponentModel;
            using System.Collections.ObjectModel;
            using System.Runtime.CompilerServices;
            using System.Threading;

            namespace Demo;

            public sealed class DemoVm : INotifyPropertyChanged
            {
                private string _name = "Initial";
                private string _surname = "Seed";
                private DemoVm _child = null!;
                private string _writtenValue = "Untouched";

                public DemoVm()
                {
                    Signal = new TestSubject<string>();
                }

                public static DemoVm CreateRoot()
                {
                    var value = new DemoVm();
                    value.Child = new DemoVm { Name = "Child" };
                    value.Items.Add(new DemoVm { Name = "First" });
                    value.Items.Add(new DemoVm { Name = "Second" });
                    return value;
                }

                private PropertyChangedEventHandler? _propertyChanged;

                public event PropertyChangedEventHandler? PropertyChanged
                {
                    add
                    {
                        _propertyChanged += value;
                        PropertyChangedSubscriberCount++;
                    }
                    remove
                    {
                        _propertyChanged -= value;
                        PropertyChangedSubscriberCount--;
                    }
                }

                public int PropertyChangedSubscriberCount { get; private set; }

                public string Name
                {
                    get => _name;
                    set
                    {
                        var normalized = value.Trim();
                        if (_name == normalized)
                        {
                            return;
                        }

                        _name = normalized;
                        OnPropertyChanged();
                        OnPropertyChanged(nameof(FullName));
                    }
                }

                public string FullName => Name + " Person";

                public DemoVm Child
                {
                    get => _child;
                    set
                    {
                        if (ReferenceEquals(_child, value))
                        {
                            return;
                        }

                        _child = value;
                        OnPropertyChanged();
                    }
                }

                public ObservableCollection<DemoVm> Items { get; } = new();

                public TestSubject<string> Signal { get; }

                public string WriteOnly
                {
                    set
                    {
                        _writtenValue = value;
                        WriteOnlySetCount++;
                    }
                }

                public string WrittenValue => _writtenValue;

                public int WriteOnlySetCount { get; private set; }

                public string Surname
                {
                    get => _surname;
                    set
                    {
                        if (_surname == value)
                        {
                            return;
                        }

                        _surname = value;
                        OnPropertyChanged();
                    }
                }

                private void OnPropertyChanged([CallerMemberName] string? name = null) =>
                    _propertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            }

            public sealed class TestSubject<T> : IObservable<T>
            {
                private event Action<T>? Next;

                public IDisposable Subscribe(IObserver<T> observer)
                {
                    void Forward(T value) => observer.OnNext(value);
                    Next += Forward;
                    return new Subscription(() => Next -= Forward);
                }

                public void OnNext(T value) => Next?.Invoke(value);

                private sealed class Subscription(Action dispose) : IDisposable
                {
                    private Action? _dispose = dispose;

                    public void Dispose()
                    {
                        var action = Interlocked.Exchange(ref _dispose, null);
                        action?.Invoke();
                    }
                }
            }

            public partial class StateDemo : Akbura.AkburaControl
            {
                public StateDemo() : base(Akbura.Engine.AkburaEngine.Empty)
                {
                }

                public DemoVm CurrentVm => vm;

                public void SetNameState(string value) => name = value;

                public void SetSurnameState(string value) => surname = value;

                public void ReplaceVm(DemoVm value) => vm = value;

                public void SetChild(DemoVm value) => vm.Child = value;

                public void ClearChild() => vm.Child = null!;

                public void ReplaceFirstItem(DemoVm value) => vm.Items[0] = value;

                public void SetSelectedIndex(int value) => selectedIndex = value;

                public void EmitSignal(string value) => vm.Signal.OnNext(value);

                public void SetWriteOnlyState(string value) => writeOnly = value;

                public void InitializeForTest() => base.OnInitialized();
            }
            """;

        return Compile(
            componentSource,
            hostSource,
            structural,
            "DirectionalStateBindingRuntime",
            "StateDemo.akbura");
    }

    private static Assembly CompileParameterRoot(bool structural)
    {
        const string componentSource =
            """
            using Avalonia.Controls;
            namespace Demo;

            param DemoVm Vm;
            state DemoVm current = out Vm;
            state string name = out Vm.Name;

            <TextBlock Text={name} />
            """;
        const string hostSource =
            """
            using System.ComponentModel;
            using System.Runtime.CompilerServices;

            namespace Demo;

            public sealed class DemoVm : INotifyPropertyChanged
            {
                private string _name = string.Empty;
                private PropertyChangedEventHandler? _propertyChanged;

                public event PropertyChangedEventHandler? PropertyChanged
                {
                    add
                    {
                        _propertyChanged += value;
                        PropertyChangedSubscriberCount++;
                    }
                    remove
                    {
                        _propertyChanged -= value;
                        PropertyChangedSubscriberCount--;
                    }
                }

                public int PropertyChangedSubscriberCount { get; private set; }

                public int NameReadCount { get; private set; }

                public string Name
                {
                    get
                    {
                        NameReadCount++;
                        return _name;
                    }
                    set
                    {
                        if (_name == value)
                        {
                            return;
                        }

                        _name = value;
                        _propertyChanged?.Invoke(
                            this,
                            new PropertyChangedEventArgs(nameof(Name)));
                    }
                }
            }

            public partial class ParameterRootDemo : Akbura.AkburaControl
            {
                public ParameterRootDemo() : base(Akbura.Engine.AkburaEngine.Empty)
                {
                }

                public DemoVm CurrentParameterState => current;

                public void InitializeForTest() => base.OnInitialized();
            }
            """;

        return Compile(
            componentSource,
            hostSource,
            structural,
            "DirectionalParameterRootRuntime",
            "ParameterRootDemo.akbura");
    }

    private static Assembly CompileDynamicParameterIndex(bool structural)
    {
        const string componentSource =
            """
            using Avalonia.Controls;
            namespace Demo;

            param DemoVm Vm;
            param int Index;
            state string parameterName = out Vm.Items[Index].Name;
            state string componentPropertyName = out Vm.Items[SelectedIndex].Name;

            <StackPanel>
                <TextBlock Text={parameterName} />
                <TextBlock Text={componentPropertyName} />
            </StackPanel>
            """;
        const string hostSource =
            """
            using System.Collections.ObjectModel;
            using System.ComponentModel;
            using System.Runtime.CompilerServices;

            namespace Demo;

            public sealed class DemoVm
            {
                public ObservableCollection<DemoItem> Items { get; } = new();

                public static DemoVm Create()
                {
                    var value = new DemoVm();
                    value.Items.Add(new DemoItem { Name = "First" });
                    value.Items.Add(new DemoItem { Name = "Second" });
                    return value;
                }
            }

            public sealed class DemoItem : INotifyPropertyChanged
            {
                private string _name = string.Empty;

                public event PropertyChangedEventHandler? PropertyChanged;

                public string Name
                {
                    get => _name;
                    set
                    {
                        if (_name == value)
                        {
                            return;
                        }

                        _name = value;
                        PropertyChanged?.Invoke(
                            this,
                            new PropertyChangedEventArgs(nameof(Name)));
                    }
                }
            }

            public partial class DynamicIndexDemo : Akbura.AkburaControl
            {
                public static readonly Avalonia.StyledProperty<int> SelectedIndexProperty =
                    Avalonia.AvaloniaProperty.Register<DynamicIndexDemo, int>(
                        nameof(SelectedIndex));

                public DynamicIndexDemo() : base(Akbura.Engine.AkburaEngine.Empty)
                {
                }

                public int SelectedIndex
                {
                    get => GetValue(SelectedIndexProperty);
                    set => SetValue(SelectedIndexProperty, value);
                }

                public void InitializeForTest() => base.OnInitialized();
            }
            """;

        return Compile(
            componentSource,
            hostSource,
            structural,
            "DirectionalDynamicParameterIndexRuntime",
            "DynamicIndexDemo.akbura");
    }

    private static Assembly CompileStaticRoot(bool structural)
    {
        const string componentSource =
            """
            using Avalonia.Controls;
            namespace Demo;

            state DemoVm current = out Globals.Current;
            state string name = out Globals.Current.Name;

            <TextBlock Text={name} />
            """;
        const string hostSource =
            """
            using System.ComponentModel;
            using System.Runtime.CompilerServices;

            namespace Demo;

            public static class Globals
            {
                public static DemoVm Current { get; } = new()
                {
                    Name = "Initial static",
                };
            }

            public sealed class DemoVm : INotifyPropertyChanged
            {
                private string _name = string.Empty;

                public event PropertyChangedEventHandler? PropertyChanged;

                public string Name
                {
                    get => _name;
                    set
                    {
                        if (_name == value)
                        {
                            return;
                        }

                        _name = value;
                        PropertyChanged?.Invoke(
                            this,
                            new PropertyChangedEventArgs(nameof(Name)));
                    }
                }
            }

            public partial class StaticRootDemo : Akbura.AkburaControl
            {
                public StaticRootDemo() : base(Akbura.Engine.AkburaEngine.Empty)
                {
                }

                public DemoVm CurrentStaticState => current;

                public void InitializeForTest() => base.OnInitialized();
            }
            """;

        return Compile(
            componentSource,
            hostSource,
            structural,
            "DirectionalStaticRootRuntime",
            "StaticRootDemo.akbura");
    }

    private static Assembly CompileSwitchingGraph(bool structural)
    {
        const string componentSource =
            """
            using Avalonia.Controls;
            namespace Demo;

            state string name = out NextVm.Name;
            state long number = out NumberVm.Number;

            <TextBlock Text={name} />
            """;
        const string hostSource =
            """
            using System.ComponentModel;

            namespace Demo;

            public sealed class DemoVm : INotifyPropertyChanged
            {
                private PropertyChangedEventHandler? _propertyChanged;
                private string _name;
                private int _number;

                public DemoVm(string name)
                {
                    _name = name;
                }

                public event PropertyChangedEventHandler? PropertyChanged
                {
                    add
                    {
                        _propertyChanged += value;
                        PropertyChangedSubscriberCount++;
                    }
                    remove
                    {
                        _propertyChanged -= value;
                        PropertyChangedSubscriberCount--;
                    }
                }

                public int PropertyChangedSubscriberCount { get; private set; }

                public int Number
                {
                    get => _number;
                    set
                    {
                        if (_number == value)
                        {
                            return;
                        }

                        _number = value;
                        _propertyChanged?.Invoke(
                            this,
                            new PropertyChangedEventArgs(nameof(Number)));
                    }
                }

                public string Name
                {
                    get => _name;
                    set
                    {
                        if (_name == value)
                        {
                            return;
                        }

                        _name = value;
                        _propertyChanged?.Invoke(
                            this,
                            new PropertyChangedEventArgs(nameof(Name)));
                    }
                }
            }

            public partial class SwitchingGraphDemo : Akbura.AkburaControl
            {
                public SwitchingGraphDemo() : base(Akbura.Engine.AkburaEngine.Empty)
                {
                }

                public DemoVm First { get; } = new("First");

                public DemoVm Second { get; } = new("Second");

                public DemoVm NumberVm { get; } = new("Number")
                {
                    Number = 42,
                };

                public int NextVmReadCount { get; private set; }

                public long CurrentNumberState => number;

                public DemoVm NextVm => ++NextVmReadCount == 1
                    ? First
                    : Second;

                public void InitializeForTest() => base.OnInitialized();
            }
            """;

        return Compile(
            componentSource,
            hostSource,
            structural,
            "DirectionalSwitchingGraphRuntime",
            "SwitchingGraphDemo.akbura");
    }

    private static Assembly Compile(string componentSource, string hostSource, bool structural, string assemblyName, string componentFilePath)
    {

        var options = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        if (structural)
        {
            options = options.WithPreprocessorSymbols("DEBUG");
        }

        var csharpCompilation = CSharpCompilation.Create(
            assemblyName + "_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(hostSource, options)],
            SymbolTests.CreateAvaloniaReferences(),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
        var syntaxTree = AkburaSyntaxTree.ParseText(componentSource, componentFilePath);
        var compilation = new AkburaCompilation(csharpCompilation, [syntaxTree], rootNamespace: "Demo");
        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        var diagnostics = semanticModel.GetSemanticDiagnostics(syntaxTree.GetRoot())
            .Where(static diagnostic => diagnostic.Severity == AkburaDiagnosticSeverity.Error)
            .ToArray();
        Assert.True(
            diagnostics.Length == 0,
            string.Join(Environment.NewLine, diagnostics.Select(static diagnostic =>
                diagnostic.Code + ": " + diagnostic.Message)));
        var symbol = Assert.IsAssignableFrom<IAkburaComponentSymbol>(
            semanticModel.GetSymbolInfo(syntaxTree.GetRoot()).Symbol);
        var generated = ComponentDocumentWriter.Generate(
            symbol,
            semanticModel,
            syntaxTree.FilePath,
            new Dictionary<AkburaSyntax, string>(),
            mode: structural
                ? ComponentGenerationMode.DebugStructural
                : ComponentGenerationMode.ReleaseDirect)
            .ToString();
        var emittedCompilation = csharpCompilation.AddSyntaxTrees(
            CSharpSyntaxTree.ParseText(generated, options));
        var compilationDiagnostics = emittedCompilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error)
            .ToArray();
        Assert.True(
            compilationDiagnostics.Length == 0,
            string.Join(Environment.NewLine, compilationDiagnostics.AsEnumerable()) +
                Environment.NewLine + generated);
        using var output = new MemoryStream();
        var result = emittedCompilation.Emit(output);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.AsEnumerable()));
        return Assembly.Load(output.ToArray());
    }
}
