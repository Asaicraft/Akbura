using Akbura.ComponentTree;
using Akbura.Diagnostics;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using System.Collections.Immutable;

namespace Akbura.UnitTests;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class DiagnosticsTests
{
    public enum SampleMode
    {
        First,
        Second,
    }

    public static TheoryData<string, Type, object?> EditableValues => new()
    {
        { "hello", typeof(string), "hello" },
        { "true", typeof(bool), true },
        { "-12", typeof(int), -12 },
        { "42", typeof(uint), 42u },
        { "9223372036854775807", typeof(long), long.MaxValue },
        { "1.25", typeof(float), 1.25f },
        { "2.5", typeof(double), 2.5d },
        { "10.125", typeof(decimal), 10.125m },
        { "second", typeof(SampleMode), SampleMode.Second },
        { "", typeof(int?), null },
        { "7", typeof(int?), 7 },
    };

    [Theory]
    [MemberData(nameof(EditableValues))]
    public void StateValueConverter_ParsesSupportedValues(
        string text,
        Type type,
        object? expected)
    {
        Assert.True(StateValueConverter.CanEdit(type));
        Assert.True(StateValueConverter.TryParse(text, type, out var value, out var error));
        Assert.Equal(string.Empty, error);
        Assert.Equal(expected, value);
    }

    [Fact]
    public void StateValueConverter_RejectsInvalidValueWithoutChangingState()
    {
        var state = new State<int>(4);

        Assert.False(StateValueConverter.TryParse("not a number", typeof(int), out _, out var error));
        Assert.Contains("not a valid Int32", error, StringComparison.Ordinal);
        Assert.Equal(4, state.Value);
    }

    [Fact]
    public void DebugString_FormatsSimpleValuesAndContainsFailingToString()
    {
        Assert.Equal("null", DebugString.Format(null));
        Assert.Equal("true", DebugString.Format(true));
        Assert.Equal("1.5", DebugString.Format(1.5d));
        Assert.Equal("<ThrowingValue: InvalidOperationException>", DebugString.Format(new ThrowingValue()));
    }

    [Fact]
    public void ToggleGesture_RequiresExactKeyboardModifiers()
    {
        var expected = new KeyGesture(Key.D, KeyModifiers.Control | KeyModifiers.Shift);

        Assert.True(AkburaDiagnosticsExtensions.HasSameToggleGesture(
            expected,
            new KeyGesture(Key.D, KeyModifiers.Control | KeyModifiers.Shift)));
        Assert.False(AkburaDiagnosticsExtensions.HasSameToggleGesture(
            expected,
            new KeyGesture(Key.D, KeyModifiers.Control)));
        Assert.False(AkburaDiagnosticsExtensions.HasSameToggleGesture(
            expected,
            new KeyGesture(Key.D, KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt)));
        Assert.False(AkburaDiagnosticsExtensions.HasSameToggleGesture(
            expected,
            new KeyGesture(Key.F12, KeyModifiers.Control | KeyModifiers.Shift)));
    }

    [Fact]
    public void ToggleGesture_AutoRepeatDoesNotToggleTwice()
    {
        var latch = new AkburaDiagnosticsExtensions.KeyGestureLatch(
            new KeyGesture(Key.F12, KeyModifiers.Control));

        Assert.True(latch.Press(Key.F12, KeyModifiers.Control, out var handled));
        Assert.True(handled);
        Assert.False(latch.Press(Key.F12, KeyModifiers.Control, out handled));
        Assert.True(handled);
        Assert.True(latch.Release(Key.F12));
        Assert.True(latch.Press(Key.F12, KeyModifiers.Control, out handled));

        Assert.True(latch.Release(Key.F12));
        Assert.False(latch.Press(Key.F12, KeyModifiers.None, out handled));
        Assert.False(handled);
        Assert.False(latch.Press(Key.F12, KeyModifiers.Control, out handled));
        Assert.False(handled);
        Assert.False(latch.Release(Key.F12));
    }

    [Fact]
    public void DiagnosticsRoot_IsGeneratedAsAkburaComponent()
    {
        Assert.True(typeof(AkburaControl).IsAssignableFrom(typeof(DiagnosticsRoot)));
    }

    [Fact]
    public async Task DiagnosticsWindow_LoadsGeneratedAkburaComponent()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(AvaloniaTestAppBuilder));
        await session.Dispatch(
            () =>
            {
                var window = new DiagnosticsWindow();
                window.Show();

                var root = Assert.IsType<DiagnosticsRoot>(window.Content);
                Assert.IsType<Grid>(root.Child);

                window.Close();
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task DiagnosticsWindow_TracksExternalComponentAndStateChanges()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(AvaloniaTestAppBuilder));
        await session.Dispatch(
            () =>
            {
                var diagnosticsWindow = new DiagnosticsWindow();
                diagnosticsWindow.Show();
                var diagnostics = Assert.IsType<DiagnosticsRoot>(diagnosticsWindow.Content);
                Assert.Equal(0, diagnostics.VisibleComponentCount);

                var component = new InspectableComponent();
                var applicationWindow = new Window { Content = component };
                applicationWindow.Show();

                Assert.Equal(1, diagnostics.VisibleComponentCount);
                Assert.Same(component, diagnostics.SelectedComponent);
                var renderedVersion = diagnostics.DetailRenderVersion;

                component.Counter.Value = 2;

                Assert.True(diagnostics.DetailRenderVersion > renderedVersion);

                applicationWindow.Content = null;
                Assert.Equal(0, diagnostics.VisibleComponentCount);
                Assert.Null(diagnostics.SelectedComponent);

                applicationWindow.Close();
                diagnosticsWindow.Close();
            },
            CancellationToken.None);
    }

    private sealed class ThrowingValue
    {
        public override string ToString()
        {
            throw new InvalidOperationException();
        }
    }

    private sealed class InspectableComponent : AkburaControl
    {
        private static readonly StateInfo<int> s_counterInfo =
            new("counter", static _ => 1);
        private static readonly Parameter<InspectableComponent, string> s_title =
            Parameter.Create<InspectableComponent, string>(
                nameof(Title),
                defaultValue: "sample");
        private static readonly ImmutableArray<Parameter> s_parameters = [s_title];
        private static readonly ImmutableArray<Avalonia.AvaloniaProperty<IAkburaCommand>> s_commands = [];
        private static readonly ImmutableArray<InjectService> s_services = [];
        private readonly Border _root = new();
        private ImmutableArray<State> _states;
        private State<int> _counter = null!;

        public InspectableComponent()
            : base(Akbura.Engine.AkburaEngine.Empty)
        {
        }

        public string Title
        {
            get => GetValue(s_title.AvaloniaProperty);
            set => SetValue(s_title.AvaloniaProperty, value);
        }

        public State<int> Counter => _counter;

        protected override Control Update() => _root;

        protected override Control FirstUpdate() => _root;

        protected override ImmutableArray<Parameter> GetParameters() => s_parameters;

        protected override ImmutableArray<Avalonia.AvaloniaProperty<IAkburaCommand>> GetCommands() => s_commands;

        protected override ImmutableArray<InjectService> GetServices() => s_services;

        protected override ImmutableArray<State> GetStates()
        {
            if (_states.IsDefault)
            {
                _counter = CreateState(s_counterInfo);
                _states = [_counter];
            }

            return _states;
        }
    }
}
