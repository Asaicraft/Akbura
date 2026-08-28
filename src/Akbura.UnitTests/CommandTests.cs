using Akbura.ComponentTree;
using Akbura.Engine;
using Avalonia;
using Avalonia.Controls;
using System.Collections.Immutable;

namespace Akbura.UnitTests;

public sealed class CommandTests
{
    [Fact]
    public void OnInitialized_AcceptsSameCommandsArray()
    {
        var control = new TestComponent(returnNewArray: false);

        control.InitializeForTest();

        Assert.NotNull(control.Child);
    }

    [Fact]
    public void OnInitialized_ThrowsWhenGetCommandsReturnsDifferentArray()
    {
        var control = new TestComponent(returnNewArray: true);

        var exception = Assert.Throws<AkburaCommandsArrayChangedException>(
            control.InitializeForTest);

        Assert.Same(control, exception.AkburaControl);
        Assert.Contains("GetCommands()", exception.Message);
        Assert.Contains("same immutable array instance", exception.Message);
    }

    private sealed class TestComponent : AkburaControl
    {
        public static readonly StyledProperty<IAkburaCommand> SaveCommandProperty =
            AvaloniaProperty.Register<TestComponent, IAkburaCommand>("SaveCommand");

        private static readonly ImmutableArray<Parameter> s_parameters = [];
        private static readonly ImmutableArray<InjectService> s_services = [];
        private static readonly ImmutableArray<State> s_states = [];

        private static readonly ImmutableArray<AvaloniaProperty<IAkburaCommand>> s_commands =
            [SaveCommandProperty];

        private readonly bool _returnNewArray;

        public TestComponent(bool returnNewArray)
            : base(new AkburaEngineExtensions.AkburaEngineBuilder().Build())
        {
            _returnNewArray = returnNewArray;
        }

        public void InitializeForTest()
        {
            base.OnInitialized();
        }

        protected override Control Update()
        {
            return new Border();
        }

        protected override Control FirstUpdate()
        {
            return new Border();
        }

        protected override ImmutableArray<Parameter> GetParameters()
        {
            return s_parameters;
        }

        protected override ImmutableArray<AvaloniaProperty<IAkburaCommand>> GetCommands()
        {
            return _returnNewArray
                ? ImmutableArray.Create<AvaloniaProperty<IAkburaCommand>>(SaveCommandProperty)
                : s_commands;
        }

        protected override ImmutableArray<InjectService> GetServices()
        {
            return s_services;
        }

        protected override ImmutableArray<State> GetStates()
        {
            return s_states;
        }
    }
}
