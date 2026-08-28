using Akbura.ComponentTree;
using Akbura.Engine;
using Avalonia;
using Avalonia.Controls;
using System.Collections.Immutable;

namespace Akbura.UnitTests;

public sealed class StateTests
{
    [Fact]
    public void StateFactory_RunsAfterServicesAndParameters_ForEachComponent()
    {
        var first = new FactoryComponent(CreateEngine(new OffsetService(4)));
        first.Seed = 3;

        Assert.Equal(0, first.FactoryCallCount);
        Assert.Equal(0, first.UpdateCount);

        first.InitializeForTest();

        Assert.False(first.StateWasCreatedDuringFirstUpdate);
        Assert.Equal(1, first.FactoryCallCount);
        Assert.Equal(7, first.Total.InitialValue);
        Assert.Equal(7, first.Total.Value);
        Assert.True(first.Total.IsAttached);
        Assert.Equal("total", first.Total.Info!.Name);
        Assert.Equal(typeof(int), first.Total.Info.ValueType);
        Assert.Equal(1, first.UpdateCount);

        var second = new FactoryComponent(CreateEngine(new OffsetService(2)));
        second.Seed = 10;
        second.InitializeForTest();

        Assert.Equal(12, second.Total.Value);
        Assert.NotSame(first.Total, second.Total);
        Assert.Same(first.Total.Info, second.Total.Info);
    }

    [Fact]
    public void StateValue_NotifiesBeforeUpdate_AndIgnoresEqualValues()
    {
        var control = new ReactiveComponent();
        control.InitializeForTest();
        control.Events.Clear();

        var subscription = control.First.Subscribe(
            value => control.Events.Add($"subscriber:{value}"));

        Assert.Empty(control.Events);
        Assert.Equal(0, control.First.InitialValue);
        Assert.Same(control.First.Info, control.FirstStateInfo);
        Assert.Same(control.Second.Info, control.SecondStateInfo);

        control.First.Value = 1;

        Assert.Equal(
            ["subscriber:1", "update:1:0:0"],
            control.Events);
        Assert.Equal(2, control.UpdateCount);

        control.First.Value = 1;

        Assert.Equal(2, control.UpdateCount);
        Assert.Equal(2, control.Events.Count);

        subscription.Dispose();
        subscription.Dispose();
        control.Events.Clear();
        control.First.Value = 2;

        Assert.Equal(["update:2:0:0"], control.Events);
        Assert.Equal(3, control.UpdateCount);
    }

    [Fact]
    public void ParameterChange_UpdatesOnlyAfterInitialization()
    {
        var control = new ReactiveComponent();

        control.ParameterValue = 3;

        Assert.Equal(0, control.UpdateCount);

        control.InitializeForTest();

        Assert.Equal(1, control.FirstUpdateCount);
        Assert.Equal(1, control.UpdateCount);

        control.ParameterValue = 4;

        Assert.Equal(2, control.UpdateCount);

        control.ParameterValue = 4;

        Assert.Equal(2, control.UpdateCount);

        control.InvalidState();

        Assert.Equal(3, control.UpdateCount);
    }

    [Fact]
    public void SuppressUpdates_GroupsStateAndParameterChanges()
    {
        var control = new ReactiveComponent();
        control.InitializeForTest();

        using (control.BeginUpdateSuppression())
        {
            control.First.Value = 1;
            control.ParameterValue = 2;

            using (control.BeginUpdateSuppression())
            {
                control.Second.Value = 3;
            }

            Assert.Equal(1, control.UpdateCount);
        }

        Assert.Equal(2, control.UpdateCount);
        Assert.Equal(1, control.First.Value);
        Assert.Equal(3, control.Second.Value);
        Assert.Equal(2, control.ParameterValue);

        using (control.BeginUpdateSuppression())
        {
        }

        Assert.Equal(2, control.UpdateCount);

        Assert.Throws<ExpectedTestException>(
            control.ChangeStateInsideThrowingSuppression);

        Assert.Equal(3, control.UpdateCount);
        Assert.Equal(4, control.First.Value);
    }

    [Fact]
    public void SubscriberStateChanges_AreRenderedInOnePass()
    {
        var control = new ReactiveComponent();
        control.InitializeForTest();

        using var subscription = control.First.Subscribe(
            value => control.Second.Value = value * 2);

        control.First.Value = 5;

        Assert.Equal(5, control.First.Value);
        Assert.Equal(10, control.Second.Value);
        Assert.Equal(2, control.UpdateCount);
        Assert.Equal("update:5:10:0", control.Events[^1]);
    }

    [Fact]
    public void StateChangeDuringUpdate_QueuesNonRecursivePass()
    {
        var control = new ReactiveComponent();
        control.InitializeForTest();
        control.MutateStateDuringNextUpdate = true;

        control.First.Value = 1;

        Assert.Equal(2, control.First.Value);
        Assert.Equal(3, control.UpdateCount);
        Assert.Equal(1, control.FirstUpdateCount);
        Assert.Equal(1, control.MaxUpdateDepth);
    }

    [Fact]
    public void OnInitialized_ThrowsWhenGetStatesReturnsDifferentArray()
    {
        var control = new ReactiveComponent(returnNewStateArray: true);

        var exception = Assert.Throws<AkburaStatesArrayChangedException>(
            control.InitializeForTest);

        Assert.Same(control, exception.AkburaControl);
        Assert.Contains("GetStates()", exception.Message);
        Assert.Contains("same immutable array instance", exception.Message);
        Assert.Equal(0, control.UpdateCount);
        Assert.Equal(1, control.FirstUpdateCount);
    }

    private static AkburaEngine CreateEngine(IOffsetService service)
    {
        return new AkburaEngine(new SingleServiceProvider(service));
    }

    private sealed class ReactiveComponent : AkburaControl
    {
        private static readonly StateInfo<int> s_firstStateInfo =
            new("first", static _ => 0);

        private static readonly StateInfo<int> s_secondStateInfo =
            StateInfo<int>.FromState(
                "second",
                static _ => new State<int>(0));

        private static readonly Parameter<ReactiveComponent, int> s_parameter =
            Parameter.Create<ReactiveComponent, int>(
                nameof(ParameterValue),
                defaultValue: 0);

        private static readonly ImmutableArray<Parameter> s_parameters = [s_parameter];
        private static readonly ImmutableArray<AvaloniaProperty<IAkburaCommand>> s_commands = [];
        private static readonly ImmutableArray<InjectService> s_services = [];

        private readonly bool _returnNewStateArray;
        private ImmutableArray<State> _states;
        private State<int> _first = null!;
        private State<int> _second = null!;
        private int _updateDepth;

        public ReactiveComponent(bool returnNewStateArray = false)
            : base(AkburaEngine.Empty)
        {
            _returnNewStateArray = returnNewStateArray;
        }

        public State<int> First => _first;

        public State<int> Second => _second;

        public StateInfo<int> FirstStateInfo => s_firstStateInfo;

        public StateInfo<int> SecondStateInfo => s_secondStateInfo;

        public int ParameterValue
        {
            get => GetValue(s_parameter.AvaloniaProperty);
            set => SetValue(s_parameter.AvaloniaProperty, value);
        }

        public int UpdateCount
        {
            get; private set;
        }

        public int FirstUpdateCount
        {
            get; private set;
        }

        public int MaxUpdateDepth
        {
            get; private set;
        }

        public bool MutateStateDuringNextUpdate
        {
            get; set;
        }

        public List<string> Events
        {
            get;
        } = [];

        public void InitializeForTest()
        {
            base.OnInitialized();
        }

        public IDisposable BeginUpdateSuppression()
        {
            return SuppressUpdates();
        }

        public void ChangeStateInsideThrowingSuppression()
        {
            using (SuppressUpdates())
            {
                First.Value = 4;
                throw new ExpectedTestException();
            }
        }

        protected override Control Update()
        {
            _updateDepth++;
            MaxUpdateDepth = Math.Max(MaxUpdateDepth, _updateDepth);
            try
            {
                UpdateCount++;
                if (MutateStateDuringNextUpdate)
                {
                    MutateStateDuringNextUpdate = false;
                    First.Value++;
                }

                Events.Add(
                    $"update:{First.Value}:{Second.Value}:{ParameterValue}");
                return new Border();
            }
            finally
            {
                _updateDepth--;
            }
        }

        protected override Control FirstUpdate()
        {
            FirstUpdateCount++;
            return new Border();
        }

        protected override ImmutableArray<Parameter> GetParameters()
        {
            return s_parameters;
        }

        protected override ImmutableArray<AvaloniaProperty<IAkburaCommand>> GetCommands()
        {
            return s_commands;
        }

        protected override ImmutableArray<InjectService> GetServices()
        {
            return s_services;
        }

        protected override ImmutableArray<State> GetStates()
        {
            if (_states.IsDefault)
            {
                _first = CreateState(s_firstStateInfo);
                _second = CreateState(s_secondStateInfo);
                _states = [_first, _second];
            }

            return _returnNewStateArray
                ? ImmutableArray.Create<State>(_states[0], _states[1])
                : _states;
        }
    }

    private sealed class FactoryComponent : AkburaControl
    {
        private static readonly StateInfo<int> s_totalStateInfo =
            new("total", CreateInitialValue);

        private static readonly Parameter<FactoryComponent, int> s_seedParameter =
            Parameter.Create<FactoryComponent, int>(nameof(Seed));

        private static readonly InjectService<FactoryComponent, IOffsetService> s_serviceInjection =
            InjectService.Create<FactoryComponent, IOffsetService>(
                nameof(Service),
                static control => control.Service,
                static (control, value) => control.Service = value);

        private static readonly ImmutableArray<Parameter> s_parameters = [s_seedParameter];
        private static readonly ImmutableArray<AvaloniaProperty<IAkburaCommand>> s_commands = [];
        private static readonly ImmutableArray<InjectService> s_services = [s_serviceInjection];

        private ImmutableArray<State> _states;
        private State<int> _total = null!;
        private IOffsetService? _service;

        public FactoryComponent(AkburaEngine engine)
            : base(engine)
        {
        }

        public int Seed
        {
            get => GetValue(s_seedParameter.AvaloniaProperty);
            set => SetValue(s_seedParameter.AvaloniaProperty, value);
        }

        public IOffsetService? Service
        {
            get => _service;
            set => SetAndRaise(
                s_serviceInjection.AvaloniaProperty,
                ref _service,
                value);
        }

        public State<int> Total => _total;

        public int FactoryCallCount
        {
            get; private set;
        }

        public int UpdateCount
        {
            get; private set;
        }

        public bool StateWasCreatedDuringFirstUpdate
        {
            get; private set;
        }

        public void InitializeForTest()
        {
            base.OnInitialized();
        }

        protected override Control Update()
        {
            UpdateCount++;
            return new Border();
        }

        protected override Control FirstUpdate()
        {
            StateWasCreatedDuringFirstUpdate = !_states.IsDefault;
            return new Border();
        }

        protected override ImmutableArray<Parameter> GetParameters()
        {
            return s_parameters;
        }

        protected override ImmutableArray<AvaloniaProperty<IAkburaCommand>> GetCommands()
        {
            return s_commands;
        }

        protected override ImmutableArray<InjectService> GetServices()
        {
            return s_services;
        }

        protected override ImmutableArray<State> GetStates()
        {
            if (_states.IsDefault)
            {
                _total = CreateState(s_totalStateInfo);
                _states = [_total];
            }

            return _states;
        }

        private static int CreateInitialValue(AkburaControl control)
        {
            var component = (FactoryComponent)control;
            component.FactoryCallCount++;
            return component.Seed + component.Service!.Offset;
        }
    }

    private interface IOffsetService
    {
        int Offset { get; }
    }

    private sealed class OffsetService(int offset) : IOffsetService
    {
        public int Offset { get; } = offset;
    }

    private sealed class SingleServiceProvider(IOffsetService service) : IAkburaServiceProvider
    {
        public object? GetService(ref readonly InjectionInfo injectionInfo)
        {
            return injectionInfo.RequestedService == typeof(IOffsetService)
                ? service
                : null;
        }
    }

    private sealed class ExpectedTestException : Exception
    {
    }
}
