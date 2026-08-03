using Akbura.ComponentTree;
using Akbura.Engine;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using System.Collections;
using System.Globalization;

namespace Akbura.Diagnostics;

internal partial class DiagnosticsRoot : AkburaControl
{
    private readonly Dictionary<TreeViewItem, AkburaControl> _componentByItem = [];
    private AkburaControl? _selectedComponent;
    private bool _isAttached;
    private bool _isRenderingDetails;

    public DiagnosticsRoot()
        : base(AkburaEngine.Empty)
    {
    }

    internal int VisibleComponentCount => _componentByItem.Count;

    internal AkburaControl? SelectedComponent => _selectedComponent;

    internal int DetailRenderVersion { get; private set; }

    internal IInputBuilderProvider InputBuilders { get; set; } =
        new InputBuilderProvider(InputBuilderProvider.CreateDefaultBuilders());

    internal IServiceProvider? InputServices { get; set; }

    protected override void OnInitialized()
    {
        base.OnInitialized();

        if (VisualRoot != null)
        {
            AttachDiagnostics();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (IsInitialized)
        {
            AttachDiagnostics();
        }
    }

    private void AttachDiagnostics()
    {
        if (_isAttached)
        {
            return;
        }

        _isAttached = true;
        componentTree.SelectionChanged += OnTreeSelectionChanged;
        AkburaComponentRegistry.Changed += OnComponentRegistryChanged;
        RefreshTree();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_isAttached)
        {
            _isAttached = false;
            componentTree.SelectionChanged -= OnTreeSelectionChanged;
            AkburaComponentRegistry.Changed -= OnComponentRegistryChanged;
            SelectComponent(null);
        }

        base.OnDetachedFromVisualTree(e);
    }

    private void OnComponentRegistryChanged(object? sender, EventArgs e)
    {
        if (_isRenderingDetails)
        {
            return;
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            RefreshTree();
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (_isAttached)
            {
                RefreshTree();
            }
        });
    }

    private void RefreshTree()
    {
        if (!_isAttached)
        {
            return;
        }

        var components = AkburaComponentRegistry.GetAttachedComponents()
            .Where(IsApplicationComponent)
            .ToArray();
        var componentSet = new HashSet<AkburaControl>(components, ReferenceEqualityComparer.Instance);
        var roots = components
            .Where(component =>
                ((IComponentTree)component).ComponentParent is not AkburaControl parent ||
                !componentSet.Contains(parent))
            .ToArray();

        var selected = _selectedComponent != null && componentSet.Contains(_selectedComponent)
            ? _selectedComponent
            : roots.FirstOrDefault() ?? components.FirstOrDefault();

        _componentByItem.Clear();
        var selectedItem = default(TreeViewItem);
        var items = new TreeViewItem[roots.Length];
        for (var index = 0; index < roots.Length; index++)
        {
            items[index] = CreateTreeItem(roots[index], componentSet, selected, ref selectedItem);
        }

        componentTree.ItemsSource = items;
        SelectComponent(selected);
        if (selectedItem != null)
        {
            selectedItem.IsSelected = true;
        }
    }

    private bool IsApplicationComponent(AkburaControl component)
    {
        return !ReferenceEquals(component, this) &&
            TopLevel.GetTopLevel(component) is not DiagnosticsWindow;
    }

    private TreeViewItem CreateTreeItem(
        AkburaControl component,
        HashSet<AkburaControl> componentSet,
        AkburaControl? selected,
        ref TreeViewItem? selectedItem)
    {
        var item = new TreeViewItem
        {
            Header = GetComponentDisplayName(component),
            IsExpanded = true,
        };
        _componentByItem.Add(item, component);

        if (ReferenceEquals(component, selected))
        {
            selectedItem = item;
        }

        var children = ((IComponentTree)component).ComponentChildren
            .OfType<AkburaControl>()
            .Where(componentSet.Contains)
            .ToArray();
        if (children.Length != 0)
        {
            var childItems = new TreeViewItem[children.Length];
            for (var index = 0; index < children.Length; index++)
            {
                childItems[index] = CreateTreeItem(children[index], componentSet, selected, ref selectedItem);
            }

            item.ItemsSource = childItems;
        }

        return item;
    }

    private static string GetComponentDisplayName(AkburaControl component)
    {
        var typeName = component.GetType().Name;
        return string.IsNullOrWhiteSpace(component.Name)
            ? typeName
            : $"{typeName}  #{component.Name}";
    }

    private void OnTreeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (componentTree.SelectedItem is TreeViewItem item &&
            _componentByItem.TryGetValue(item, out var component))
        {
            SelectComponent(component);
        }
    }

    private void SelectComponent(AkburaControl? component)
    {
        if (ReferenceEquals(_selectedComponent, component))
        {
            RenderDetails();
            return;
        }

        if (_selectedComponent != null)
        {
            _selectedComponent.PropertyChanged -= OnSelectedPropertyChanged;
            foreach (var state in _selectedComponent.GetDiagnosticStates())
            {
                state.ValueChanged -= OnSelectedStateChanged;
            }
        }

        _selectedComponent = component;
        if (_selectedComponent != null)
        {
            _selectedComponent.PropertyChanged += OnSelectedPropertyChanged;
            foreach (var state in _selectedComponent.GetDiagnosticStates())
            {
                state.ValueChanged += OnSelectedStateChanged;
            }
        }

        RenderDetails();
    }

    private void OnSelectedPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        RenderDetails();
    }

    private void OnSelectedStateChanged(State state)
    {
        RenderDetails();
    }

    private void RenderDetails()
    {
        if (_isRenderingDetails)
        {
            return;
        }

        _isRenderingDetails = true;
        try
        {
            DetailRenderVersion++;
            detailsPanel.Children.Clear();
            var component = _selectedComponent;
            if (component == null)
            {
                selectionTitle.Text = "No component selected";
                selectionType.Text = string.Empty;
                detailsPanel.Children.Add(new TextBlock
                {
                    Text = "Attach an Akbura component to the visual tree to inspect it.",
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.7,
                });
                return;
            }

            selectionTitle.Text = GetComponentDisplayName(component);
            selectionType.Text = component.GetType().FullName ?? component.GetType().Name;
            AppendServices(component);
            AppendStates(component);
            AppendParameters(component);
        }
        finally
        {
            _isRenderingDetails = false;
        }
    }

    private void AppendServices(AkburaControl component)
    {
        var section = CreateSection("Services");
        var services = component.GetDiagnosticServices();
        if (services.IsEmpty)
        {
            section.Children.Add(CreateEmptyValue());
        }
        else
        {
            foreach (var service in services)
            {
                var value = component.GetValue(service.AvaloniaProperty);
                section.Children.Add(CreateReadOnlyRow(
                    service.Name,
                    service.ServiceType,
                    service.IsOptional ? "optional" : "required",
                    service.IsInjected(component) ? DebugString.Format(value) : "not injected"));
            }
        }

        detailsPanel.Children.Add(section);
    }

    private void AppendStates(AkburaControl component)
    {
        var section = CreateSection("States");
        var states = component.GetDiagnosticStates();
        if (states.IsEmpty)
        {
            section.Children.Add(CreateEmptyValue());
        }
        else
        {
            foreach (var state in states)
            {
                section.Children.Add(CreateStateRow(component, state));
            }
        }

        detailsPanel.Children.Add(section);
    }

    private void AppendParameters(AkburaControl component)
    {
        var section = CreateSection("Parameters");
        var parameters = component.GetDiagnosticParameters();
        if (parameters.IsEmpty)
        {
            section.Children.Add(CreateEmptyValue());
        }
        else
        {
            foreach (var parameter in parameters)
            {
                section.Children.Add(CreateParameterRow(component, parameter));
            }
        }

        detailsPanel.Children.Add(section);
    }

    private static StackPanel CreateSection(string title)
    {
        var section = new StackPanel();
        section.Classes.Add("diagnostic-section");

        var heading = new TextBlock
        {
            Text = title,
        };
        heading.Classes.Add("diagnostic-section-title");
        section.Children.Add(heading);
        return section;
    }

    private static Control CreateEmptyValue()
    {
        var value = new TextBlock
        {
            Text = "None",
        };
        value.Classes.Add("diagnostic-empty");
        return value;
    }

    private static Control CreateReadOnlyRow(
        string name,
        Type type,
        string detail,
        string value)
    {
        var row = CreateValueRow();
        row.Children.Add(CreateIdentity(name, type, detail));

        var valueText = new TextBlock
        {
            Text = value,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        };
        valueText.Classes.Add("diagnostic-value");
        Grid.SetColumn(valueText, 1);
        row.Children.Add(valueText);
        return row;
    }

    private Control CreateStateRow(
        AkburaControl component,
        State state)
    {
        return CreateEditableRow(
            component,
            state.Info?.Name ?? "State",
            state.ValueType,
            $"initial {DebugString.Format(state.BoxedInitialValue)}",
            DataVariation.State,
            state.BoxedValue,
            value => state.BoxedValue = value);
    }

    private Control CreateParameterRow(
        AkburaControl component,
        Parameter parameter)
    {
        var property = parameter.AvaloniaProperty;
        var currentValue = component.GetValue(property);
        if (property.IsReadOnly &&
            currentValue is not IList &&
            currentValue is not IDictionary)
        {
            return CreateReadOnlyRow(
                parameter.Name,
                property.PropertyType,
                $"{parameter.Binding} | readonly",
                DebugString.Format(currentValue));
        }

        return CreateEditableRow(
            component,
            parameter.Name,
            property.PropertyType,
            parameter.Binding.ToString(),
            DataVariation.Parameter,
            currentValue,
            value => ApplyParameterValue(component, parameter, value));
    }

    private Control CreateEditableRow(
        AkburaControl component,
        string name,
        Type type,
        string detail,
        DataVariation variation,
        object? value,
        Action<object> commitValue)
    {
        var row = CreateValueRow();
        row.Children.Add(CreateIdentity(name, type, detail));

        var editor = new DiagnosticInput
        {
            InputBuilders = InputBuilders,
            Request = new InputRequest
            {
                RequestedType = type,
                ComponentType = component.GetType(),
                Variation = variation,
                MemberName = name,
                ComponentInstance = component,
                Services = InputServices,
                ExistingValue = value,
            },
            Value = value!,
            CommitValue = commitValue,
        };
        Grid.SetColumn(editor, 1);
        row.Children.Add(editor);
        return row;
    }

    private static StackPanel CreateIdentity(
        string name,
        Type type,
        string detail)
    {
        var identity = new StackPanel();
        identity.Classes.Add("diagnostic-identity");

        var nameText = new TextBlock { Text = name };
        nameText.Classes.Add("diagnostic-name");
        identity.Children.Add(nameText);

        var detailText = new TextBlock
        {
            Text = $"{GetTypeDisplayName(type)} | {detail}",
            TextWrapping = TextWrapping.Wrap,
        };
        detailText.Classes.Add("diagnostic-detail");
        identity.Children.Add(detailText);
        return identity;
    }

    private static void ApplyParameterValue(
        AkburaControl component,
        Parameter parameter,
        object? value)
    {
        var property = parameter.AvaloniaProperty;
        if (!property.IsReadOnly)
        {
            component.SetCurrentValue(property, value);
            return;
        }

        var currentValue = component.GetValue(property);
        if (TryReplaceCollection(currentValue, value))
        {
            component.OnParameterChanged();
            return;
        }

        throw new InvalidOperationException(
            $"Parameter '{parameter.Name}' is readonly and its current value " +
            "is not a mutable collection.");
    }

    private static bool TryReplaceCollection(
        object? currentValue,
        object? replacement)
    {
        if (ReferenceEquals(currentValue, replacement))
        {
            return true;
        }

        if (currentValue is IDictionary targetDictionary &&
            replacement is IDictionary sourceDictionary &&
            !targetDictionary.IsReadOnly)
        {
            var entries = sourceDictionary
                .Cast<DictionaryEntry>()
                .ToArray();
            targetDictionary.Clear();
            foreach (var entry in entries)
            {
                targetDictionary.Add(entry.Key, entry.Value);
            }

            return true;
        }

        if (currentValue is IList targetList &&
            replacement is IEnumerable sourceItems &&
            !targetList.IsReadOnly)
        {
            var items = sourceItems.Cast<object?>().ToArray();
            targetList.Clear();
            foreach (var item in items)
            {
                targetList.Add(item);
            }

            return true;
        }

        return false;
    }

    private static Grid CreateValueRow()
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("200,*"),
        };
        row.Classes.Add("diagnostic-value-row");
        return row;
    }

    private static string GetTypeDisplayName(Type type)
    {
        var nullableType = Nullable.GetUnderlyingType(type);
        if (nullableType != null)
        {
            return GetTypeDisplayName(nullableType) + "?";
        }

        return type.Name;
    }
}

internal static class DebugString
{
    public static string Format(object? value)
    {
        if (value == null)
        {
            return "null";
        }

        return value switch
        {
            string text => text,
            char character => character.ToString(),
            bool boolean => boolean ? "true" : "false",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
            _ => TryFormatObject(value),
        };
    }

    private static string TryFormatObject(object value)
    {
        try
        {
            return value.ToString() ?? value.GetType().FullName ?? value.GetType().Name;
        }
        catch (Exception exception)
        {
            return $"<{value.GetType().Name}: {exception.GetType().Name}>";
        }
    }
}
