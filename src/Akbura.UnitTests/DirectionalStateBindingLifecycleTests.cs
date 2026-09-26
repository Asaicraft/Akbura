using Akbura.ComponentTree;
using Akbura.Engine;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Data.Core;
using Avalonia.Markup.Xaml.MarkupExtensions.CompiledBindings;
using System.ComponentModel;
using System.Collections.Immutable;

namespace Akbura.UnitTests;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class DirectionalStateBindingLifecycleTests
{
    private static readonly CompiledBindingPath s_countingVmNamePath =
        CreatePropertyPath<CountingVm, string>(
            nameof(CountingVm.Name),
            static source => source.Name);

    private static readonly CompiledBindingPath s_observableVmStreamPath =
        CreatePropertyPath<ObservableVm, TestSubject<string>>(
            nameof(ObservableVm.Stream),
            static source => source.Stream);

    private static readonly CompiledBindingPath s_switchingRootNamePath =
        CreateSwitchingRootNamePath();

    [Fact]
    public void BindSuspendsResumesAndDoesNotDuplicateSubscriptions()
    {
        var vm = new CountingVm { Name = "Initial" };
        var state = StateBindings.CreateBind(
            static value => (string)value!,
            value => vm.Name = value,
            () => vm,
            static () => s_countingVmNamePath,
            pathLength: 1,
            requiresNonNullSource: true);

        Assert.Equal("Initial", state.Value);
        Assert.Equal(1, vm.NameReadCount);
        Assert.Equal(1, vm.PropertyChangedSubscriberCount);

        state.Value = "  Local  ";
        Assert.Equal("Local", vm.Name);
        Assert.Equal("Local", state.Value);

        state.SuspendResources();
        Assert.Equal(0, vm.PropertyChangedSubscriberCount);
        vm.Name = "Detached";
        Assert.Equal("Local", state.Value);

        state.ResumeResources();
        Assert.Equal("Detached", state.Value);
        Assert.Equal(1, vm.PropertyChangedSubscriberCount);

        state.ResumeResources();
        Assert.Equal(1, vm.PropertyChangedSubscriberCount);
    }

    [Fact]
    public void ReplacingRootDisconnectsOldOwnerAndWritesToCurrentOwner()
    {
        var first = new CountingVm { Name = "First" };
        var second = new CountingVm { Name = "Second" };
        var root = new State<CountingVm>(first);
        var state = StateBindings.CreateBind(
            static value => (string)value!,
            value => root.Value.Name = value,
            () => root.Value,
            static () => s_countingVmNamePath,
            pathLength: 1,
            requiresNonNullSource: true,
            observeRoot: changed => root.Subscribe(_ => changed()));

        Assert.Equal(1, first.PropertyChangedSubscriberCount);
        root.Value = second;

        Assert.Equal("Second", state.Value);
        Assert.Equal(0, first.PropertyChangedSubscriberCount);
        Assert.Equal(1, second.PropertyChangedSubscriberCount);

        first.Name = "Stale";
        Assert.Equal("Second", state.Value);

        state.Value = "Current";
        Assert.Equal("Stale", first.Name);
        Assert.Equal("Current", second.Name);
    }

    [Fact]
    public void OutReadsValueFromTheGraphObservedByCompiledPath()
    {
        var first = new CountingVm { Name = "First" };
        var second = new CountingVm { Name = "Second" };
        var root = new SwitchingRoot(first, second);
        var state = StateBindings.CreateOut(
            static value => (string)value!,
            () => root,
            static () => s_switchingRootNamePath,
            pathLength: 2,
            requiresNonNullSource: true);

        Assert.Equal("First", state.Value);
        Assert.Equal(1, root.CurrentReadCount);
        Assert.Equal(1, first.PropertyChangedSubscriberCount);
        Assert.Equal(0, second.PropertyChangedSubscriberCount);

        first.Name = "Updated first";

        Assert.Equal("Updated first", state.Value);
        Assert.Equal(1, root.CurrentReadCount);

        second.Name = "Stale second";

        Assert.Equal("Updated first", state.Value);
    }

    [Fact]
    public void ObservableRebindCompletionAndErrorDisposeOnlyOwnedSubscription()
    {
        var first = new ObservableVm();
        var second = new ObservableVm();
        var third = new ObservableVm();
        var root = new State<ObservableVm>(first);
        var state = StateBindings.CreateOutObservable(
            () => root.Value.Stream,
            () => root.Value,
            static () => s_observableVmStreamPath,
            pathLength: 1,
            requiresNonNullSource: true,
            observeRoot: changed => root.Subscribe(_ => changed()));

        Assert.Null(state.Value);
        Assert.Equal(1, first.Stream.SubscriberCount);
        first.Stream.Next("First value");
        Assert.Equal("First value", state.Value);

        root.Value = second;
        Assert.Equal(0, first.Stream.SubscriberCount);
        Assert.Equal(1, second.Stream.SubscriberCount);
        first.Stream.Next("Stale value");
        first.Stream.NextStale("Stale callback");
        Assert.Equal("First value", state.Value);
        second.Stream.Next("Second value");
        Assert.Equal("Second value", state.Value);

        second.Stream.Complete();
        Assert.Equal(0, second.Stream.SubscriberCount);
        second.Stream.NextStale("After completion");
        Assert.Equal("Second value", state.Value);

        root.Value = third;
        Assert.Equal(1, third.Stream.SubscriberCount);
        var error = new InvalidOperationException("Observable failure");
        var thrown = Assert.Throws<InvalidOperationException>(() => third.Stream.Error(error));
        Assert.Same(error, thrown);
        Assert.Equal(0, third.Stream.SubscriberCount);
        third.Stream.NextStale("After error");
        Assert.Equal("Second value", state.Value);
    }

    [Fact]
    public void HotReloadReconciliationSuspendsOnlyReplacedStateResources()
    {
        var previousVm = new CountingVm { Name = "Previous" };
        var currentVm = new CountingVm { Name = "Current" };
        var previous = StateBindings.CreateOut(
            static value => (string)value!,
            () => previousVm,
            static () => s_countingVmNamePath,
            pathLength: 1,
            requiresNonNullSource: true);
        var current = StateBindings.CreateOut(
            static value => (string)value!,
            () => currentVm,
            static () => s_countingVmNamePath,
            pathLength: 1,
            requiresNonNullSource: true);
        var component = new ReconciliationComponent();

        component.Reconcile([previous], [current]);

        Assert.Equal(0, previousVm.PropertyChangedSubscriberCount);
        Assert.Equal(1, currentVm.PropertyChangedSubscriberCount);

        component.Reconcile([current], [current]);

        Assert.Equal(1, currentVm.PropertyChangedSubscriberCount);
    }

    private static CompiledBindingPath CreatePropertyPath<TSource, TValue>(string name, Func<TSource, TValue> getter)
        where TSource : class
    {
        var property = new ClrPropertyInfo(
            name,
            source => getter((TSource)source),
            setter: null,
            typeof(TValue));

        return new CompiledBindingPathBuilder()
            .Property(
                property,
                PropertyInfoAccessorFactory.CreateInpcPropertyAccessor)
            .Build();
    }

    private static CompiledBindingPath CreateSwitchingRootNamePath()
    {
        var current = new ClrPropertyInfo(
            nameof(SwitchingRoot.Current),
            static source => ((SwitchingRoot)source).Current,
            setter: null,
            typeof(CountingVm));
        var name = new ClrPropertyInfo(
            nameof(CountingVm.Name),
            static source => ((CountingVm)source).Name,
            setter: null,
            typeof(string));

        return new CompiledBindingPathBuilder()
            .Property(
                current,
                PropertyInfoAccessorFactory.CreateInpcPropertyAccessor)
            .Property(
                name,
                PropertyInfoAccessorFactory.CreateInpcPropertyAccessor)
            .Build();
    }

    private sealed class CountingVm : INotifyPropertyChanged
    {
        private PropertyChangedEventHandler? _propertyChanged;
        private string _name = string.Empty;

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
                var normalized = value.Trim();
                if (_name == normalized)
                {
                    return;
                }

                _name = normalized;
                _propertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
            }
        }
    }

    private sealed class ObservableVm
    {
        public TestSubject<string> Stream { get; } = new();
    }

    private sealed class SwitchingRoot : INotifyPropertyChanged
    {
        private readonly CountingVm _first;
        private readonly CountingVm _second;

        public SwitchingRoot(CountingVm first, CountingVm second)
        {
            _first = first;
            _second = second;
        }

        public event PropertyChangedEventHandler? PropertyChanged
        {
            add { }
            remove { }
        }

        public int CurrentReadCount { get; private set; }

        public CountingVm Current => ++CurrentReadCount == 1
            ? _first
            : _second;
    }

    private sealed class ReconciliationComponent : AkburaControl
    {
        private static readonly ImmutableArray<Parameter> s_parameters = [];
        private static readonly ImmutableArray<AvaloniaProperty<IAkburaCommand>> s_commands = [];
        private static readonly ImmutableArray<InjectService> s_services = [];
        private static readonly ImmutableArray<State> s_states = [];
        private readonly Border _root = new();

        public ReconciliationComponent() : base(AkburaEngine.Empty)
        {
        }

        public void Reconcile(ImmutableArray<State> previous, ImmutableArray<State> current)
        {
            ReconcileStateResourcesForHotReload(previous, current);
        }

        protected override Control FirstUpdate() => _root;

        protected override Control Update() => _root;

        protected override ImmutableArray<Parameter> GetParameters() => s_parameters;

        protected override ImmutableArray<AvaloniaProperty<IAkburaCommand>> GetCommands() => s_commands;

        protected override ImmutableArray<InjectService> GetServices() => s_services;

        protected override ImmutableArray<State> GetStates() => s_states;
    }

    private sealed class TestSubject<T> : IObservable<T>
    {
        private readonly List<IObserver<T>> _observers = [];
        private readonly List<IObserver<T>> _observerHistory = [];

        public int SubscriberCount => _observers.Count;

        public IDisposable Subscribe(IObserver<T> observer)
        {
            _observers.Add(observer);
            _observerHistory.Add(observer);
            return new Subscription(_observers, observer);
        }

        public void Next(T value)
        {
            foreach (var observer in _observers.ToArray())
            {
                observer.OnNext(value);
            }
        }

        public void NextStale(T value)
        {
            _observerHistory[0].OnNext(value);
        }

        public void Complete()
        {
            foreach (var observer in _observers.ToArray())
            {
                observer.OnCompleted();
            }
        }

        public void Error(Exception error)
        {
            foreach (var observer in _observers.ToArray())
            {
                observer.OnError(error);
            }
        }

        private sealed class Subscription(List<IObserver<T>> observers, IObserver<T> observer)
            : IDisposable
        {
            private List<IObserver<T>>? _observers = observers;
            private IObserver<T>? _observer = observer;

            public void Dispose()
            {
                var currentObservers = Interlocked.Exchange(ref _observers, null);
                var currentObserver = Interlocked.Exchange(ref _observer, null);
                if (currentObservers != null && currentObserver != null)
                {
                    currentObservers.Remove(currentObserver);
                }
            }
        }
    }
}
