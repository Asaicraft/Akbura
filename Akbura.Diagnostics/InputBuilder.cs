using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Markup.Xaml.MarkupExtensions.CompiledBindings;
using Avalonia.Media;

namespace Akbura.Diagnostics;

/// <summary>
/// Creates an Avalonia control that edits a typed diagnostics value.
/// </summary>
public abstract class InputBuilder : AvaloniaObject
{
    /// <summary>
    /// Stores the typed value exchanged between an input control and diagnostics.
    /// </summary>
    public static readonly AttachedProperty<object?> InputValueProperty =
        AvaloniaProperty.RegisterAttached<InputBuilder, Control, object?>(
            "InputValue",
            defaultBindingMode: BindingMode.TwoWay);

    /// <summary>
    /// Reads the typed value exposed by an input control.
    /// </summary>
    public static object? GetInputValue(Control control)
    {
        return control.GetValue(InputValueProperty);
    }

    /// <summary>
    /// Writes the typed value exposed by an input control.
    /// </summary>
    public static void SetInputValue(Control control, object? value)
    {
        control.SetValue(InputValueProperty, value);
    }

    /// <summary>
    /// Creates a two-way binding between the input value of an editor and the
    /// input value exposed by another control.
    /// </summary>
    /// <param name="input">The generated editor control.</param>
    /// <param name="source">The control that owns the source input value.</param>
    /// <returns>A subscription that removes the binding when disposed.</returns>
    public static IDisposable BindInputValue(Control input, Control source)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(source);

        var binding = new CompiledBinding(InputValueBindingPath)
        {
            Source = source,
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
        };

        return input.Bind(InputValueProperty, binding);
    }

    protected static CompiledBindingPath InputValueBindingPath
    {
        get
        {
            return field ??= new CompiledBindingPathBuilder()
                .Property(
                    InputValueProperty,
                    PropertyInfoAccessorFactory.CreateAvaloniaPropertyAccessor)
                .Build();
        }
    }

    /// <summary>
    /// Gets the value type produced by this editor.
    /// </summary>
    public abstract Type OutputType { get; }

    /// <summary>
    /// Creates a control and initializes its
    /// <see cref="InputValueProperty"/> with the specified value.
    /// </summary>
    /// <remarks>
    /// The returned control must use <see cref="InputValueProperty"/> as its
    /// value source and keep it synchronized with the editable control property.
    /// </remarks>
    public virtual Control Build(InputRequest request, object? existingValue)
    {
        ArgumentNullException.ThrowIfNull(request);

        var input = BuildCore(request, existingValue);

        SetInputValue(input, existingValue);

        return input;
    }

    /// <summary>
    /// Creates and configures a control for editing the requested value.
    /// </summary>
    /// <remarks>
    /// The returned control must use <see cref="InputValueProperty"/> as its
    /// value source.
    ///
    /// Changes made by the user must update <see cref="InputValueProperty"/>,
    /// and changes to <see cref="InputValueProperty"/> must update the control.
    /// </remarks>
    protected virtual Control BuildCore(
        InputRequest request,
        object? existingValue)
    {
        return BuildCore(request);
    }

    /// <summary>
    /// Creates and configures a control for editing the requested value.
    /// </summary>
    protected abstract Control BuildCore(InputRequest request);

    /// <summary>
    /// Determines whether this builder can edit the requested value.
    /// </summary>
    public virtual bool CanProvide(InputRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var editorType = request.EditorType;
        return editorType == typeof(object)
            ? OutputType == typeof(object)
            : editorType.IsAssignableFrom(OutputType);
    }
}
