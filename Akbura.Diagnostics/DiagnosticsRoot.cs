using Akbura.ComponentTree;
using Akbura.Engine;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using System.Globalization;

namespace Akbura.Diagnostics;

internal partial class DiagnosticsRoot : AkburaControl
{
    private readonly Dictionary<TreeViewItem, AkburaControl> _componentByItem = [];
    private AkburaControl? _selectedComponent;
    private bool _isAttached;

    public DiagnosticsRoot()
        : base(AkburaEngine.Empty)
    {
    }

    internal int VisibleComponentCount => _componentByItem.Count;

    internal AkburaControl? SelectedComponent => _selectedComponent;

    internal int DetailRenderVersion { get; private set; }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

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
                section.Children.Add(CreateStateRow(state));
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
                section.Children.Add(CreateReadOnlyRow(
                    parameter.Name,
                    parameter.AvaloniaProperty.PropertyType,
                    parameter.Binding.ToString(),
                    DebugString.Format(component.GetValue(parameter.AvaloniaProperty))));
            }
        }

        detailsPanel.Children.Add(section);
    }

    private static StackPanel CreateSection(string title)
    {
        var section = new StackPanel
        {
            Spacing = 8,
        };
        section.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
        });
        return section;
    }

    private static Control CreateEmptyValue()
    {
        return new TextBlock
        {
            Text = "None",
            Opacity = 0.55,
        };
    }

    private static Control CreateReadOnlyRow(
        string name,
        Type type,
        string detail,
        string value)
    {
        var row = CreateValueRow();
        var identity = new StackPanel
        {
            Spacing = 2,
        };
        identity.Children.Add(new TextBlock
        {
            Text = name,
            FontWeight = FontWeight.Medium,
        });
        identity.Children.Add(new TextBlock
        {
            Text = $"{GetTypeDisplayName(type)} | {detail}",
            FontSize = 12,
            Opacity = 0.6,
        });
        row.Children.Add(identity);

        var valueText = new TextBlock
        {
            Text = value,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(valueText, 1);
        row.Children.Add(valueText);
        return row;
    }

    private static Control CreateStateRow(State state)
    {
        var container = new StackPanel
        {
            Spacing = 7,
        };
        var row = CreateValueRow();
        var identity = new StackPanel
        {
            Spacing = 2,
        };
        identity.Children.Add(new TextBlock
        {
            Text = state.Info?.Name ?? "State",
            FontWeight = FontWeight.Medium,
        });
        identity.Children.Add(new TextBlock
        {
            Text = $"{GetTypeDisplayName(state.ValueType)} | initial {DebugString.Format(state.BoxedInitialValue)}",
            FontSize = 12,
            Opacity = 0.6,
        });
        row.Children.Add(identity);

        if (!StateValueConverter.CanEdit(state.ValueType))
        {
            var value = new TextBlock
            {
                Text = DebugString.Format(state.BoxedValue),
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(value, 1);
            row.Children.Add(value);
            container.Children.Add(row);
            return container;
        }

        var editor = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 8,
        };
        var input = new TextBox
        {
            Text = StateValueConverter.FormatForEditor(state.BoxedValue, state.ValueType),
            MinWidth = 180,
        };
        var apply = new Button
        {
            Content = "Apply",
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        Grid.SetColumn(apply, 1);
        editor.Children.Add(input);
        editor.Children.Add(apply);
        Grid.SetColumn(editor, 1);
        row.Children.Add(editor);
        container.Children.Add(row);

        var error = new TextBlock
        {
            Foreground = Brushes.OrangeRed,
            FontSize = 12,
            IsVisible = false,
            TextWrapping = TextWrapping.Wrap,
        };
        container.Children.Add(error);

        void ApplyValue()
        {
            if (!StateValueConverter.TryParse(input.Text, state.ValueType, out var value, out var message))
            {
                error.Text = message;
                error.IsVisible = true;
                return;
            }

            try
            {
                state.BoxedValue = value;
                error.IsVisible = false;
            }
            catch (Exception exception)
            {
                error.Text = exception.Message;
                error.IsVisible = true;
            }
        }

        apply.Click += (_, _) => ApplyValue();
        input.KeyDown += (_, eventArgs) =>
        {
            if (eventArgs.Key == Key.Enter)
            {
                eventArgs.Handled = true;
                ApplyValue();
            }
        };

        return container;
    }

    private static Grid CreateValueRow()
    {
        return new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("220,*"),
            ColumnSpacing = 16,
            MinHeight = 38,
        };
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
