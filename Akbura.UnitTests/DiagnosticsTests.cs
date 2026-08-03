using Akbura.ComponentTree;
using Akbura.Diagnostics;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
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
        { "c56a4180-65aa-42ec-a945-5fd21dec0538", typeof(Guid), new Guid("c56a4180-65aa-42ec-a945-5fd21dec0538") },
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
    public async Task DiagnosticsWindow_DoesNotRegisterItsOwnComponentSubtree()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(AvaloniaTestAppBuilder));
        await session.Dispatch(
            () =>
            {
                var component = new InspectableComponent();
                var applicationWindow = new Window { Content = component };
                applicationWindow.Show();

                var diagnosticsWindow = new DiagnosticsWindow();
                diagnosticsWindow.Show(applicationWindow);

                var diagnostics = Assert.IsType<DiagnosticsRoot>(
                    diagnosticsWindow.Content);
                Assert.Equal(1, diagnostics.VisibleComponentCount);

                var registeredComponents =
                    AkburaComponentRegistry.GetAttachedComponents();
                Assert.Contains(component, registeredComponents);
                Assert.DoesNotContain(
                    registeredComponents,
                    candidate =>
                        TopLevel.GetTopLevel(candidate) is DiagnosticsWindow);

                diagnosticsWindow.Close();
                applicationWindow.Close();
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task AkburaControl_WithNoGeneratedChild_DoesNotCrashLayout()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(AvaloniaTestAppBuilder));
        await session.Dispatch(
            () =>
            {
                var component = new EmptyInspectableComponent();
                var window = new Window { Content = component };

                window.Show();
                component.Measure(new Avalonia.Size(320, 200));
                component.Arrange(new Avalonia.Rect(0, 0, 320, 200));

                Assert.Null(component.Child);
                window.Close();
            },
            CancellationToken.None);
    }

    [Fact]
    public void StateValueConverter_RoundTripsArbitraryObjectAsJson()
    {
        var expected = new EditableModel("Akbura", 3);
        var text = StateValueConverter.FormatForEditor(
            expected,
            typeof(EditableModel));

        Assert.Contains("\"Name\"", text, StringComparison.Ordinal);
        Assert.True(StateValueConverter.TryParse(
            text,
            typeof(EditableModel),
            out var value,
            out var error));
        Assert.Equal(string.Empty, error);
        Assert.Equal(expected, Assert.IsType<EditableModel>(value));
    }

    [Fact]
    public void StateValueConverter_ParsesObjectWithoutLosingNaturalClrValues()
    {
        Assert.True(StateValueConverter.TryParse(
            "plain text",
            typeof(object),
            out var text,
            out _));
        Assert.Equal("plain text", text);

        Assert.True(StateValueConverter.TryParse(
            "{\"count\": 3, \"enabled\": true}",
            typeof(object),
            out var value,
            out _));
        var dictionary = Assert.IsType<Dictionary<string, object?>>(value);
        Assert.Equal(3L, dictionary["count"]);
        Assert.Equal(true, dictionary["enabled"]);
    }

    [Fact]
    public void DiagnosticsOptions_PrioritizeCustomInputBuilderAndKeepFallback()
    {
        var custom = new EditableModelInputBuilder();
        var options = new AkburaDiagnosticsOptions
        {
            ToggleGesture = new KeyGesture(
                Key.D,
                KeyModifiers.Control | KeyModifiers.Shift),
        };
        options.InputBuilders.Insert(0, custom);

        var configuration = options.CreateConfiguration();
        var request = new InputRequest
        {
            RequestedType = typeof(EditableModel),
            ComponentType = typeof(InspectableComponent),
            Variation = DataVariation.State,
        };

        Assert.Same(custom, configuration.InputBuilders.Provide(request));
        Assert.IsType<UniversalInputBuilder>(
            configuration.InputBuilders[^1]);
        Assert.True(AkburaDiagnosticsExtensions.HasSameToggleGesture(
            options.ToggleGesture,
            configuration.ToggleGesture));
    }

    [Fact]
    public void InputBuilderProvider_PreservesRuntimeTypeForObjectValues()
    {
        var configuration = new AkburaDiagnosticsOptions()
            .CreateConfiguration();
        var integerRequest = new InputRequest
        {
            RequestedType = typeof(object),
            ExistingValue = 42,
            ComponentType = typeof(InspectableComponent),
            Variation = DataVariation.Parameter,
        };
        var nullRequest = integerRequest with { ExistingValue = null };

        Assert.IsType<NumericInputBuilder<int>>(
            configuration.InputBuilders.Provide(integerRequest));
        Assert.IsType<UniversalInputBuilder>(
            configuration.InputBuilders.Provide(nullRequest));
    }

    [Fact]
    public void InputBuilderProvider_PrefersCollectionInputOverUniversalInput()
    {
        var configuration = new AkburaDiagnosticsOptions()
            .CreateConfiguration();
        var request = new InputRequest
        {
            RequestedType = typeof(IList<int>),
            ExistingValue = new List<int> { 1, 2, 3 },
            ComponentType = typeof(InspectableComponent),
            Variation = DataVariation.Parameter,
        };
        var builders = configuration.InputBuilders
            .Provides(request)
            .ToArray();

        Assert.IsType<CollectionInputBuilder>(builders[0]);
        Assert.IsType<UniversalInputBuilder>(builders[^1]);
    }

    [Fact]
    public async Task InputBuilderBinding_SynchronizesValuesInBothDirections()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(AvaloniaTestAppBuilder));
        await session.Dispatch(
            () =>
            {
                var source = new Border();
                var input = new TextBox();
                InputBuilder.SetInputValue(source, "source");

                using var binding = InputBuilder.BindInputValue(input, source);

                Assert.Equal("source", InputBuilder.GetInputValue(input));

                InputBuilder.SetInputValue(input, "edited");
                Assert.Equal("edited", InputBuilder.GetInputValue(source));

                InputBuilder.SetInputValue(source, "external");
                Assert.Equal("external", InputBuilder.GetInputValue(input));
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task InputBuilders_LoadTheirIconsFromDiagnosticsResources()
    {
        using var session = HeadlessUnitTestSession.StartNew(
            typeof(AvaloniaTestAppBuilder));

        await session.Dispatch(
            () =>
            {
                var stringIcon = new StringInputBuilder().Icon;
                var collectionIcon = new CollectionInputBuilder().Icon;
                var universalIcon = new UniversalInputBuilder().Icon;

                Assert.NotSame(stringIcon, universalIcon);
                Assert.NotSame(collectionIcon, universalIcon);
                Assert.True(stringIcon.Bounds.Width > 0d);
                Assert.True(stringIcon.Bounds.Height > 0d);
                Assert.True(collectionIcon.Bounds.Width > 0d);
                Assert.True(collectionIcon.Bounds.Height > 0d);
                Assert.True(universalIcon.Bounds.Width > 0d);
                Assert.True(universalIcon.Bounds.Height > 0d);
                Assert.True(
                    universalIcon.Bounds.Height /
                    universalIcon.Bounds.Width >= 0.75d);
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task CollectionInput_UsesNestedDiagnosticInputsForItems()
    {
        using var session = HeadlessUnitTestSession.StartNew(
            typeof(AvaloniaTestAppBuilder));

        await session.Dispatch(
            () =>
            {
                var original =
                    new System.Collections.ObjectModel.ObservableCollection<int>
                    {
                        1,
                        2,
                        3,
                    };
                var request = new InputRequest
                {
                    RequestedType = typeof(IList<int>),
                    ExistingValue = original,
                    ComponentType = typeof(InspectableComponent),
                    Variation = DataVariation.Parameter,
                };
                var input = Assert.IsType<CollectionInput>(
                    new CollectionInputBuilder().Build(
                        request,
                        request.ExistingValue));
                var window = new DiagnosticsWindow
                {
                    Content = input,
                };

                window.Show();
                window.UpdateLayout();

                var expander = Assert.IsType<Expander>(input.Child);
                Assert.False(expander.IsExpanded);

                expander.IsExpanded = true;
                window.UpdateLayout();

                var itemsControl = input.GetVisualDescendants()
                    .OfType<ItemsControl>()
                    .Single();
                var itemModels = Assert.IsAssignableFrom<
                    IEnumerable<CollectionInputItem>>(
                        itemsControl.ItemsSource);
                Assert.Equal(3, itemModels.Count());

                var itemTemplate = Assert.IsAssignableFrom<
                    Avalonia.Controls.Templates.IDataTemplate>(
                        itemsControl.ItemTemplate);
                var itemHost = new StackPanel();
                foreach (var item in itemModels)
                {
                    itemHost.Children.Add(
                        Assert.IsType<Grid>(
                            itemTemplate.Build(item)));
                }

                var itemWindow = new Window
                {
                    Content = itemHost,
                };
                itemWindow.Show();
                itemWindow.UpdateLayout();

                var editors = itemHost.GetVisualDescendants()
                    .OfType<DiagnosticInput>()
                    .OrderBy(static editor => editor.Request.MemberName)
                    .ToArray();
                Assert.Equal(3, editors.Length);
                Assert.All(
                    editors,
                    static editor =>
                        Assert.Equal(
                            typeof(int),
                            editor.Request.RequestedType));

                ApplyEditorValue(editors[1], 8);

                var updated = Assert.IsType<
                    System.Collections.ObjectModel.ObservableCollection<int>>(
                        InputBuilder.GetInputValue(input));
                Assert.NotSame(original, updated);
                Assert.Equal([1, 8, 3], updated);
                Assert.Equal([1, 2, 3], original);

                expander.IsExpanded = false;
                Assert.False(expander.IsExpanded);

                itemWindow.Close();
                window.Close();
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task DiagnosticsDetails_ConstrainLongErrorsToAvailableWidth()
    {
        using var session = HeadlessUnitTestSession.StartNew(
            typeof(AvaloniaTestAppBuilder));

        await session.Dispatch(
            () =>
            {
                var diagnosticsWindow = new DiagnosticsWindow
                {
                    Width = 760d,
                    Height = 560d,
                };
                diagnosticsWindow.Show();

                var diagnosticsRoot = Assert.IsType<DiagnosticsRoot>(
                    diagnosticsWindow.Content);
                var detailsScroll = diagnosticsRoot.Child!
                    .GetVisualDescendants()
                    .OfType<ScrollViewer>()
                    .Single(scroll =>
                        scroll.Content is StackPanel);

                Assert.Equal(
                    ScrollBarVisibility.Disabled,
                    detailsScroll.HorizontalScrollBarVisibility);
                Assert.Equal(
                    Avalonia.Layout.HorizontalAlignment.Stretch,
                    detailsScroll.HorizontalContentAlignment);

                var editor = new DiagnosticInput
                {
                    InputBuilders = new AkburaDiagnosticsOptions()
                        .CreateConfiguration()
                        .InputBuilders,
                    Request = new InputRequest
                    {
                        RequestedType = typeof(string),
                        ComponentType = typeof(InspectableComponent),
                        Variation = DataVariation.Parameter,
                        ExistingValue = "value",
                    },
                    Value = "value",
                    CommitValue = static _ =>
                        throw new FormatException(new string('x', 2_000)),
                };
                var host = new ScrollViewer
                {
                    Width = 480d,
                    HorizontalScrollBarVisibility =
                        ScrollBarVisibility.Disabled,
                    Content = editor,
                };
                diagnosticsWindow.Content = host;
                diagnosticsWindow.UpdateLayout();
                InputBuilder.SetInputValue(editor, "changed");
                diagnosticsWindow.UpdateLayout();
                ApplyEditorValue(editor, "changed");
                diagnosticsWindow.UpdateLayout();

                var error = editor.GetVisualDescendants()
                    .OfType<TextBlock>()
                    .Single(text =>
                        text.Text?.Length == 2_000);
                Assert.True(error.IsVisible);
                Assert.True(
                    error.Bounds.Width <= editor.Bounds.Width,
                    $"Error width {error.Bounds.Width} exceeded editor width {editor.Bounds.Width}.");
                Assert.True(
                    host.Extent.Width <= host.Viewport.Width + 0.5d,
                    $"Extent {host.Extent.Width} exceeded viewport {host.Viewport.Width}.");

                diagnosticsWindow.Close();
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task DiagnosticInput_ActionButtonsContainPathIcons()
    {
        using var session = HeadlessUnitTestSession.StartNew(
            typeof(AvaloniaTestAppBuilder));

        await session.Dispatch(
            () =>
            {
                var configuration = new AkburaDiagnosticsOptions()
                    .CreateConfiguration();
                var editor = new DiagnosticInput
                {
                    InputBuilders = configuration.InputBuilders,
                    Request = new InputRequest
                    {
                        RequestedType = typeof(string),
                        ComponentType = typeof(InspectableComponent),
                        Variation = DataVariation.Parameter,
                        ExistingValue = "value",
                    },
                    Value = "value",
                    CommitValue = static _ => { },
                };
                var window = new DiagnosticsWindow
                {
                    Content = editor,
                };

                window.Show();

                var buttons = editor.GetVisualDescendants()
                    .OfType<Button>()
                    .ToArray();
                Assert.Equal(2, buttons.Length);
                Assert.All(
                    buttons,
                    static button =>
                    {
                        var icon = Assert.IsType<Avalonia.Controls.Shapes.Path>(
                            button.Content);
                        Assert.NotNull(icon.Fill);
                        Assert.Equal(16d, icon.Width);
                        Assert.Equal(16d, icon.Height);
                        Assert.Equal(
                            Avalonia.Layout.HorizontalAlignment.Center,
                            icon.HorizontalAlignment);
                        Assert.Equal(
                            Avalonia.Layout.VerticalAlignment.Center,
                            icon.VerticalAlignment);
                        Assert.Equal(
                            new Avalonia.Thickness(0d),
                            button.Padding);
                    });

                window.Close();
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task DiagnosticInput_ItemTemplateAcceptsTransientNullItem()
    {
        using var session = HeadlessUnitTestSession.StartNew(
            typeof(AvaloniaTestAppBuilder));

        await session.Dispatch(
            () =>
            {
                var configuration = new AkburaDiagnosticsOptions()
                    .CreateConfiguration();
                var editor = new DiagnosticInput
                {
                    InputBuilders = configuration.InputBuilders,
                    Request = new InputRequest
                    {
                        RequestedType = typeof(string),
                        ComponentType = typeof(InspectableComponent),
                        Variation = DataVariation.Parameter,
                        ExistingValue = "value",
                    },
                    Value = "value",
                    CommitValue = static _ => { },
                };
                var window = new DiagnosticsWindow
                {
                    Content = editor,
                };

                window.Show();

                var selector = editor.GetVisualDescendants()
                    .OfType<ComboBox>()
                    .Single();
                var template = Assert.IsAssignableFrom<
                    Avalonia.Controls.Templates.IDataTemplate>(
                        selector.ItemTemplate);

                Assert.NotNull(template.Build(null));

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

    [Fact]
    public async Task DiagnosticsWindow_EditsParametersAndStates()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(AvaloniaTestAppBuilder));
        await session.Dispatch(
            () =>
            {
                var diagnosticsWindow = new DiagnosticsWindow();
                diagnosticsWindow.Show();

                var component = new InspectableComponent();
                var applicationWindow = new Window { Content = component };
                applicationWindow.Show();

                var parameterEditor = FindEditor(
                    diagnosticsWindow,
                    DataVariation.Parameter,
                    nameof(InspectableComponent.Title));
                ApplyEditorValue(parameterEditor, "edited title");
                Assert.Equal("edited title", component.Title);

                var stateEditor = FindEditor(
                    diagnosticsWindow,
                    DataVariation.State,
                    "counter");
                ApplyEditorValue(stateEditor, 12);
                Assert.Equal(12, component.Counter.Value);

                var collectionEditor = FindEditor(
                    diagnosticsWindow,
                    DataVariation.Parameter,
                    nameof(InspectableComponent.Items));
                ApplyEditorValue(collectionEditor, new List<int> { 4, 8, 15 });
                Assert.Equal([4, 8, 15], component.Items);

                applicationWindow.Close();
                diagnosticsWindow.Close();
            },
            CancellationToken.None);
    }

    private static DiagnosticInput FindEditor(
        DiagnosticsWindow window,
        DataVariation variation,
        string memberName)
    {
        return window.GetVisualDescendants()
            .OfType<DiagnosticInput>()
            .Single(editor =>
                editor.Request.Variation == variation &&
                editor.Request.MemberName == memberName);
    }

    private static void ApplyEditorValue(
        DiagnosticInput editor,
        object? value)
    {
        InputBuilder.SetInputValue(editor, value);

        var apply = editor.GetVisualDescendants()
            .OfType<Button>()
            .Single(button =>
                ReferenceEquals(
                    button.FindAncestorOfType<DiagnosticInput>(),
                    editor) &&
                string.Equals(
                    ToolTip.GetTip(button) as string,
                    "Apply changes",
                    StringComparison.Ordinal));
        apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    private sealed class ThrowingValue
    {
        public override string ToString()
        {
            throw new InvalidOperationException();
        }
    }

    public sealed record EditableModel(string Name, int Count);

    private sealed class EditableModelInputBuilder : InputBuilder
    {
        public override Type OutputType => typeof(EditableModel);

        protected override Control BuildCore(InputRequest request)
        {
            return new TextBox();
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
        private static readonly ReadOnlyParameter<InspectableComponent, IList<int>> s_items =
            Parameter.CreateReadOnly<InspectableComponent, IList<int>>(
                nameof(Items),
                static owner => owner.Items);
        private static readonly ImmutableArray<Parameter> s_parameters =
            [s_title, s_items];
        private static readonly ImmutableArray<Avalonia.AvaloniaProperty<IAkburaCommand>> s_commands = [];
        private static readonly ImmutableArray<InjectService> s_services = [];
        private readonly Border _root = new();
        private readonly List<int> _items = [1, 2, 3];
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

        public IList<int> Items => _items;

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

    private sealed class EmptyInspectableComponent : AkburaControl
    {
        private static readonly ImmutableArray<Parameter> s_parameters = [];
        private static readonly ImmutableArray<Avalonia.AvaloniaProperty<IAkburaCommand>> s_commands = [];
        private static readonly ImmutableArray<InjectService> s_services = [];
        private static readonly ImmutableArray<State> s_states = [];

        public EmptyInspectableComponent()
            : base(Akbura.Engine.AkburaEngine.Empty)
        {
        }

        protected override Control Update() => null!;

        protected override Control FirstUpdate() => null!;

        protected override ImmutableArray<Parameter> GetParameters() => s_parameters;

        protected override ImmutableArray<Avalonia.AvaloniaProperty<IAkburaCommand>> GetCommands() => s_commands;

        protected override ImmutableArray<InjectService> GetServices() => s_services;

        protected override ImmutableArray<State> GetStates() => s_states;
    }
}
