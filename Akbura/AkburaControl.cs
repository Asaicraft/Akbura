using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akbura;

public abstract class AkburaControl : Control
{
    public static readonly DirectProperty<AkburaControl, Control?> ChildProperty =
        AvaloniaProperty.RegisterDirect<AkburaControl, Control?>(nameof(Child), getter: x => x.Child);

    public static readonly StyledProperty<Thickness> PaddingProperty =
            Decorator.PaddingProperty.AddOwner<TemplatedControl>();

    /// <summary>
    /// Initializes static members of the <see cref="Decorator"/> class.
    /// </summary>
    static AkburaControl()
    {
        AffectsMeasure<AkburaControl>(ChildProperty, PaddingProperty);
        ChildProperty.Changed.AddClassHandler<AkburaControl>((x, e) => x.ChildChanged(e));
    }

    public Control? Child
    {
        get; private set => SetAndRaise(ChildProperty, ref field, value);
    }

    /// <summary>
    /// Gets or sets the padding placed between the border of the control and its content.
    /// </summary>
    public Thickness Padding
    {
        get => GetValue(PaddingProperty);
        set => SetValue(PaddingProperty, value);
    }


    public void InvalidState()
    {
        Child = Update();
    }

    protected abstract Control Update();

    /// <inheritdoc/>
    protected override Size MeasureOverride(Size availableSize)
    {
        return LayoutHelper.MeasureChild(Child, availableSize, Padding);
    }

    /// <inheritdoc/>
    protected override Size ArrangeOverride(Size finalSize)
    {
        return LayoutHelper.ArrangeChild(Child, finalSize, Padding);
    }

    /// <summary>
    /// Called when the <see cref="Child"/> property changes.
    /// </summary>
    /// <param name="e">The event args.</param>
    private void ChildChanged(AvaloniaPropertyChangedEventArgs e)
    {
        var oldChild = (Control?)e.OldValue;
        var newChild = (Control?)e.NewValue;

        if (oldChild != null)
        {
            ((ISetLogicalParent)oldChild).SetParent(null);
            LogicalChildren.Clear();
            VisualChildren.Remove(oldChild);
        }

        if (newChild != null)
        {
            ((ISetLogicalParent)newChild).SetParent(this);
            VisualChildren.Add(newChild);
            LogicalChildren.Add(newChild);
        }
    }
}
