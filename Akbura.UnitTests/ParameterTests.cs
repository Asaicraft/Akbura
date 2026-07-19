using Akbura.ComponentTree;
using Akbura.Engine;
using Avalonia.Controls;
using System.Collections.Immutable;

namespace Akbura.UnitTests;

public sealed class ParameterTests
{
    [Fact]
    public void OnInitialized_ThrowsWhenRequiredParameterIsNotSet()
    {
        var control = new TestComponent(useDefaultParameter: false, returnNewArray: false);

        var exception = Assert.Throws<AkburaParameterNotSettedException>(
            control.InitializeForTest);

        Assert.Same(control, exception.AkburaControl);
        Assert.Same(TestComponent.RequiredParameter, exception.Parameter);
        Assert.Contains("RequiredValue", exception.Message);
        Assert.Contains(typeof(TestComponent).FullName!, exception.Message);
    }

    [Fact]
    public void OnInitialized_AcceptsExplicitlySetRequiredParameter()
    {
        var control = new TestComponent(useDefaultParameter: false, returnNewArray: false);
        control.SetValue(TestComponent.RequiredParameter.AvaloniaProperty, 0);

        control.InitializeForTest();

        Assert.Equal(0, control.RequiredValue);
        Assert.NotNull(control.Child);
    }

    [Fact]
    public void OnInitialized_ThrowsWhenGetParametersReturnsDifferentArray()
    {
        var control = new TestComponent(useDefaultParameter: true, returnNewArray: true);

        var exception = Assert.Throws<AkburaParametersArrayChangedException>(
            control.InitializeForTest);

        Assert.Same(control, exception.AkburaControl);
        Assert.Contains("GetParameters()", exception.Message);
        Assert.Contains("same immutable array instance", exception.Message);
    }

    private sealed class TestComponent : AkburaControl
    {
        private static readonly ImmutableArray<Avalonia.AvaloniaProperty<IAkburaCommand>> s_commands = [];
        private static readonly ImmutableArray<InjectService> s_services = [];

        public static readonly Parameter<TestComponent, int> RequiredParameter =
            Parameter.Create<TestComponent, int>(nameof(RequiredValue));

        private static readonly Parameter<TestComponent, int> s_defaultParameter =
            Parameter.Create<TestComponent, int>(
                "DefaultValue",
                defaultValue: 0);

        private static readonly ImmutableArray<Parameter> s_requiredParameters =
            [RequiredParameter];

        private static readonly ImmutableArray<Parameter> s_defaultParameters =
            [s_defaultParameter];

        private readonly ImmutableArray<Parameter> _parameters;
        private readonly bool _returnNewArray;

        public TestComponent(bool useDefaultParameter, bool returnNewArray)
            : base(new AkburaEngineExtensions.AkburaEngineBuilder().Build())
        {
            _parameters = useDefaultParameter
                ? s_defaultParameters
                : s_requiredParameters;
            _returnNewArray = returnNewArray;
        }

        public int RequiredValue
        {
            get => GetValue(RequiredParameter.AvaloniaProperty);
            set => SetValue(RequiredParameter.AvaloniaProperty, value);
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
            return _returnNewArray
                ? ImmutableArray.Create(_parameters[0])
                : _parameters;
        }

        protected override ImmutableArray<Avalonia.AvaloniaProperty<IAkburaCommand>> GetCommands()
        {
            return s_commands;
        }

        protected override ImmutableArray<InjectService> GetServices()
        {
            return s_services;
        }
    }
}
