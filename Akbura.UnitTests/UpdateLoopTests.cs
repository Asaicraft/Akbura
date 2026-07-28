using Akbura.ComponentTree;
using Akbura.Engine;
using Avalonia;
using Avalonia.Controls;
using System.Collections.Immutable;

namespace Akbura.UnitTests;

public sealed class UpdateLoopTests
{
    [Fact]
    public void Engine_UsesDefaultUpdateLimit()
    {
        var engine = new AkburaEngineExtensions.AkburaEngineBuilder().Build();

        Assert.Equal(
            AkburaEngine.DefaultMaxUpdatesPerBatch,
            engine.MaxUpdatesPerBatch);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Engine_RejectsNonPositiveUpdateLimit(int value)
    {
        var engine = new AkburaEngineExtensions.AkburaEngineBuilder().Build();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => engine.MaxUpdatesPerBatch = value);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new AkburaEngineExtensions.AkburaEngineBuilder()
                .WithMaxUpdatesPerBatch(value));
    }

    [Fact]
    public void UpdateLoop_AllowsConfiguredNumberOfPasses()
    {
        var engine = new AkburaEngineExtensions.AkburaEngineBuilder()
            .WithMaxUpdatesPerBatch(3)
            .Build();
        var control = new LoopingComponent(
            engine,
            invalidationsBeforeCompletion: 2);

        control.InitializeForTest();

        Assert.Equal(1, control.FirstUpdateCount);
        Assert.Equal(3, control.UpdateCount);
    }

    [Fact]
    public void UpdateLoop_ThrowsWhenConfiguredLimitIsExceeded()
    {
        var engine = new AkburaEngineExtensions.AkburaEngineBuilder()
            .WithMaxUpdatesPerBatch(3)
            .Build();
        var control = new LoopingComponent(
            engine,
            invalidationsBeforeCompletion: -1);

        var exception = Assert.Throws<AkburaUpdateLimitExceededException>(
            control.InitializeForTest);

        Assert.Same(control, exception.AkburaControl);
        Assert.Equal(3, exception.MaxUpdatesPerBatch);
        Assert.Equal(1, control.FirstUpdateCount);
        Assert.Equal(3, control.UpdateCount);
        Assert.Contains(
            typeof(LoopingComponent).FullName!,
            exception.Message);
        Assert.Contains("Update()", exception.Message);
    }

    private sealed class LoopingComponent : AkburaControl
    {
        private static readonly ImmutableArray<Parameter> s_parameters = [];
        private static readonly ImmutableArray<InjectService> s_services = [];
        private static readonly ImmutableArray<AvaloniaProperty<IAkburaCommand>> s_commands = [];
        private static readonly ImmutableArray<State> s_states = [];

        private readonly Border _root = new();
        private int _invalidationsBeforeCompletion;

        public LoopingComponent(
            AkburaEngine engine,
            int invalidationsBeforeCompletion)
            : base(engine)
        {
            _invalidationsBeforeCompletion = invalidationsBeforeCompletion;
        }

        public int FirstUpdateCount { get; private set; }

        public int UpdateCount { get; private set; }

        public void InitializeForTest()
        {
            base.OnInitialized();
        }

        protected override Control FirstUpdate()
        {
            FirstUpdateCount++;
            return _root;
        }

        protected override Control Update()
        {
            UpdateCount++;
            if (_invalidationsBeforeCompletion < 0)
            {
                InvalidState();
            }
            else if (_invalidationsBeforeCompletion > 0)
            {
                _invalidationsBeforeCompletion--;
                InvalidState();
            }

            return _root;
        }

        protected override ImmutableArray<Parameter> GetParameters()
        {
            return s_parameters;
        }

        protected override ImmutableArray<InjectService> GetServices()
        {
            return s_services;
        }

        protected override ImmutableArray<AvaloniaProperty<IAkburaCommand>> GetCommands()
        {
            return s_commands;
        }

        protected override ImmutableArray<State> GetStates()
        {
            return s_states;
        }
    }
}
