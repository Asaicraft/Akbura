using Avalonia;
using Akbura.Akcss;
using Akbura.ComponentTree;
using Akbura.Engine;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.VisualTree;
using System;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace Akbura;

public abstract class AkburaControl : Control, IComponentTree
{
	private readonly AvaloniaList<IComponentTree> _componentChildren = [];
	private IComponentTree? _componentParent;

	public static readonly DirectProperty<AkburaControl, Control?> ChildProperty =
		AvaloniaProperty.RegisterDirect<AkburaControl, Control?>(nameof(Child), getter: x => x.Child);

	public static readonly StyledProperty<Thickness> PaddingProperty =
		Decorator.PaddingProperty.AddOwner<AkburaControl>();

	public static readonly AttachedProperty<ImmutableArray<TailwindUtilityActivator>> TailwindUtilitiesProperty =
		AvaloniaProperty.RegisterAttached<AkburaControl, Control, ImmutableArray<TailwindUtilityActivator>>(
			"TailwindUtilities",
			coerce: CoerceTailwindUtilities);

	/// <summary>
	/// Initializes static members of the <see cref="Decorator"/> class.
	/// </summary>
	static AkburaControl()
	{
		AffectsMeasure<AkburaControl>(ChildProperty, PaddingProperty);
		ChildProperty.Changed.AddClassHandler<AkburaControl>((x, e) => x.ChildChanged(e));
		InitializeExplicitThickness();
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

	public static ImmutableArray<TailwindUtilityActivator> GetTailwindUtilities(Control control)
	{
		ArgumentNullException.ThrowIfNull(control);

		var utilities = control.GetValue(TailwindUtilitiesProperty);
		return utilities.IsDefault ? ImmutableArray<TailwindUtilityActivator>.Empty : utilities;
	}

	public static void SetTailwindUtilities(
		Control control,
		ImmutableArray<TailwindUtilityActivator> utilities)
	{
		ArgumentNullException.ThrowIfNull(control);

		if (control.IsSet(TailwindUtilitiesProperty))
		{
			throw new InvalidOperationException("TailwindUtilities has already been set.");
		}

		control.SetValue(
			TailwindUtilitiesProperty,
			utilities.IsDefault ? [] : utilities);
	}

	public static void ExecuteTailwindUtilities(Control control)
	{
		foreach (var utility in GetTailwindUtilities(control))
		{
			if (utility.Condition)
			{
				utility.Execute(control);
			}
		}
	}

	private static ImmutableArray<TailwindUtilityActivator> CoerceTailwindUtilities(
		AvaloniaObject sender,
		ImmutableArray<TailwindUtilityActivator> utilities)
	{
		if (TailwindUtilitiesProperty != null &&
			sender.IsSet(TailwindUtilitiesProperty))
		{
			throw new InvalidOperationException("TailwindUtilities has already been set.");
		}

		return utilities.IsDefault ? ImmutableArray<TailwindUtilityActivator>.Empty : utilities;
	}

	private readonly AkburaEngine _engine;

	public AkburaControl(): this(AkburaEngine.Singletone)
	{

	}

	public AkburaControl(AkburaEngine akburaEngine)
	{
		_engine = akburaEngine;
	}

	IComponentTree? IComponentTree.ComponentParent
	{
		get => _componentParent;
	}

	IAvaloniaReadOnlyList<IComponentTree> IComponentTree.ComponentChildren =>
		_componentChildren;

	public void InvalidState()
	{
		Child = Update();
	}

	protected override void OnInitialized()
	{
		base.OnInitialized();

		Child = FirstUpdate();

		var parameters = GetParameters();
		for (var index = 0; index < parameters.Length; index++)
		{
			var parameter = parameters[index];
			if (!parameter.IsSet(this))
			{
				throw new AkburaParameterNotSettedException(this, parameter);
			}
		}

		var validatedParameters = GetParameters();
		if (parameters != validatedParameters)
		{
			throw new AkburaParametersArrayChangedException(this);
		}

		Child = Update();
	}

	protected abstract Control Update();

	protected abstract Control FirstUpdate();

	/// <summary>
	/// Gets the parameters declared by this component.
	/// </summary>
	/// <remarks>
	/// Implementations must cache and return the same immutable array instance on every call.
	/// </remarks>
	/// <returns>The component parameter descriptors.</returns>
	protected abstract ImmutableArray<Parameter> GetParameters();

	protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
	{
		base.OnAttachedToVisualTree(e);
		SetComponentParent(FindComponentParent());
	}

	protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
	{
		base.OnDetachedFromVisualTree(e);
		SetComponentParent(null);
	}

	private IComponentTree? FindComponentParent()
	{
		for (var parent = this.GetVisualParent(); parent != null; parent = parent.GetVisualParent())
		{
			if (parent is IComponentTree componentParent)
			{
				return componentParent;
			}
		}

		return null;
	}

	private void SetComponentParent(IComponentTree? componentParent)
	{
		if (ReferenceEquals(_componentParent, componentParent))
		{
			return;
		}

		if (_componentParent is AkburaControl oldParent)
		{
			oldParent._componentChildren.Remove(this);
		}

		_componentParent = componentParent;

		if (componentParent is AkburaControl newParent &&
			!newParent._componentChildren.Contains(this))
		{
			newParent._componentChildren.Add(this);
		}
	}

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
			VisualChildren.Remove(oldChild);
		}

		if (newChild != null)
		{
			VisualChildren.Add(newChild);
		}
	}

	#region Explicit Thickness

	private static readonly ConditionalWeakTable<Control, ExplicitThicknessState> s_paddingStates = new();
	private static readonly ConditionalWeakTable<Control, ExplicitThicknessState> s_marginStates = new();
	private static readonly ConditionalWeakTable<Control, ExplicitThicknessState> s_borderThicknessStates = new();

	public static readonly AttachedProperty<double?> ExplicitLeftPaddingProperty =
		AvaloniaProperty.RegisterAttached<AkburaControl, Control, double?>(
			"ExplicitLeftPadding",
			defaultValue: null);

	public static readonly AttachedProperty<double?> ExplicitTopPaddingProperty =
		AvaloniaProperty.RegisterAttached<AkburaControl, Control, double?>(
			"ExplicitTopPadding",
			defaultValue: null);

	public static readonly AttachedProperty<double?> ExplicitRightPaddingProperty =
		AvaloniaProperty.RegisterAttached<AkburaControl, Control, double?>(
			"ExplicitRightPadding",
			defaultValue: null);

	public static readonly AttachedProperty<double?> ExplicitBottomPaddingProperty =
		AvaloniaProperty.RegisterAttached<AkburaControl, Control, double?>(
			"ExplicitBottomPadding",
			defaultValue: null);

	public static readonly AttachedProperty<double?> ExplicitLeftMarginProperty =
		AvaloniaProperty.RegisterAttached<AkburaControl, Control, double?>(
			"ExplicitLeftMargin",
			defaultValue: null);

	public static readonly AttachedProperty<double?> ExplicitTopMarginProperty =
		AvaloniaProperty.RegisterAttached<AkburaControl, Control, double?>(
			"ExplicitTopMargin",
			defaultValue: null);

	public static readonly AttachedProperty<double?> ExplicitRightMarginProperty =
		AvaloniaProperty.RegisterAttached<AkburaControl, Control, double?>(
			"ExplicitRightMargin",
			defaultValue: null);

	public static readonly AttachedProperty<double?> ExplicitBottomMarginProperty =
		AvaloniaProperty.RegisterAttached<AkburaControl, Control, double?>(
			"ExplicitBottomMargin",
			defaultValue: null);

	public static readonly AttachedProperty<double?> ExplicitLeftBorderThicknessProperty =
		AvaloniaProperty.RegisterAttached<AkburaControl, Control, double?>(
			"ExplicitLeftBorderThickness",
			defaultValue: null);

	public static readonly AttachedProperty<double?> ExplicitTopBorderThicknessProperty =
		AvaloniaProperty.RegisterAttached<AkburaControl, Control, double?>(
			"ExplicitTopBorderThickness",
			defaultValue: null);

	public static readonly AttachedProperty<double?> ExplicitRightBorderThicknessProperty =
		AvaloniaProperty.RegisterAttached<AkburaControl, Control, double?>(
			"ExplicitRightBorderThickness",
			defaultValue: null);

	public static readonly AttachedProperty<double?> ExplicitBottomBorderThicknessProperty =
		AvaloniaProperty.RegisterAttached<AkburaControl, Control, double?>(
			"ExplicitBottomBorderThickness",
			defaultValue: null);

	public static double? GetExplicitLeftPadding(Control control) =>
		GetExplicitThicknessSide(control, ExplicitLeftPaddingProperty);

	public static void SetExplicitLeftPadding(Control control, double? value) =>
		SetExplicitThicknessSide(control, ExplicitLeftPaddingProperty, value);

	public static double? GetExplicitTopPadding(Control control) =>
		GetExplicitThicknessSide(control, ExplicitTopPaddingProperty);

	public static void SetExplicitTopPadding(Control control, double? value) =>
		SetExplicitThicknessSide(control, ExplicitTopPaddingProperty, value);

	public static double? GetExplicitRightPadding(Control control) =>
		GetExplicitThicknessSide(control, ExplicitRightPaddingProperty);

	public static void SetExplicitRightPadding(Control control, double? value) =>
		SetExplicitThicknessSide(control, ExplicitRightPaddingProperty, value);

	public static double? GetExplicitBottomPadding(Control control) =>
		GetExplicitThicknessSide(control, ExplicitBottomPaddingProperty);

	public static void SetExplicitBottomPadding(Control control, double? value) =>
		SetExplicitThicknessSide(control, ExplicitBottomPaddingProperty, value);

	public static double? GetExplicitLeftMargin(Control control) =>
		GetExplicitThicknessSide(control, ExplicitLeftMarginProperty);

	public static void SetExplicitLeftMargin(Control control, double? value) =>
		SetExplicitThicknessSide(control, ExplicitLeftMarginProperty, value);

	public static double? GetExplicitTopMargin(Control control) =>
		GetExplicitThicknessSide(control, ExplicitTopMarginProperty);

	public static void SetExplicitTopMargin(Control control, double? value) =>
		SetExplicitThicknessSide(control, ExplicitTopMarginProperty, value);

	public static double? GetExplicitRightMargin(Control control) =>
		GetExplicitThicknessSide(control, ExplicitRightMarginProperty);

	public static void SetExplicitRightMargin(Control control, double? value) =>
		SetExplicitThicknessSide(control, ExplicitRightMarginProperty, value);

	public static double? GetExplicitBottomMargin(Control control) =>
		GetExplicitThicknessSide(control, ExplicitBottomMarginProperty);

	public static void SetExplicitBottomMargin(Control control, double? value) =>
		SetExplicitThicknessSide(control, ExplicitBottomMarginProperty, value);

	public static double? GetExplicitLeftBorderThickness(Control control) =>
		GetExplicitThicknessSide(control, ExplicitLeftBorderThicknessProperty);

	public static void SetExplicitLeftBorderThickness(Control control, double? value) =>
		SetExplicitThicknessSide(control, ExplicitLeftBorderThicknessProperty, value);

	public static double? GetExplicitTopBorderThickness(Control control) =>
		GetExplicitThicknessSide(control, ExplicitTopBorderThicknessProperty);

	public static void SetExplicitTopBorderThickness(Control control, double? value) =>
		SetExplicitThicknessSide(control, ExplicitTopBorderThicknessProperty, value);

	public static double? GetExplicitRightBorderThickness(Control control) =>
		GetExplicitThicknessSide(control, ExplicitRightBorderThicknessProperty);

	public static void SetExplicitRightBorderThickness(Control control, double? value) =>
		SetExplicitThicknessSide(control, ExplicitRightBorderThicknessProperty, value);

	public static double? GetExplicitBottomBorderThickness(Control control) =>
		GetExplicitThicknessSide(control, ExplicitBottomBorderThicknessProperty);

	public static void SetExplicitBottomBorderThickness(Control control, double? value) =>
		SetExplicitThicknessSide(control, ExplicitBottomBorderThicknessProperty, value);

	private static void InitializeExplicitThickness()
	{
		AddExplicitThicknessHandlers(
			OnExplicitPaddingChanged,
			ExplicitLeftPaddingProperty,
			ExplicitTopPaddingProperty,
			ExplicitRightPaddingProperty,
			ExplicitBottomPaddingProperty);
		AddExplicitThicknessHandlers(
			OnExplicitMarginChanged,
			ExplicitLeftMarginProperty,
			ExplicitTopMarginProperty,
			ExplicitRightMarginProperty,
			ExplicitBottomMarginProperty);
		AddExplicitThicknessHandlers(
			OnExplicitBorderThicknessChanged,
			ExplicitLeftBorderThicknessProperty,
			ExplicitTopBorderThicknessProperty,
			ExplicitRightBorderThicknessProperty,
			ExplicitBottomBorderThicknessProperty);

		PaddingProperty.Changed.AddClassHandler<AkburaControl>(OnPaddingChanged);
		Decorator.PaddingProperty.Changed.AddClassHandler<Decorator>(OnPaddingChanged);
		TemplatedControl.PaddingProperty.Changed.AddClassHandler<TemplatedControl>(OnPaddingChanged);
		Layoutable.MarginProperty.Changed.AddClassHandler<Control>(OnMarginChanged);
		Border.BorderThicknessProperty.Changed.AddClassHandler<Border>(OnBorderThicknessChanged);
		TemplatedControl.BorderThicknessProperty.Changed.AddClassHandler<TemplatedControl>(OnBorderThicknessChanged);
	}

	private static void AddExplicitThicknessHandlers(
		Action<Control, AvaloniaPropertyChangedEventArgs> handler,
		params AttachedProperty<double?>[] properties)
	{
		foreach (var property in properties)
		{
			property.Changed.AddClassHandler<Control>(handler);
		}
	}

	private static double? GetExplicitThicknessSide(
		Control control,
		AttachedProperty<double?> property)
	{
		ArgumentNullException.ThrowIfNull(control);
		return control.GetValue(property);
	}

	private static void SetExplicitThicknessSide(
		Control control,
		AttachedProperty<double?> property,
		double? value)
	{
		ArgumentNullException.ThrowIfNull(control);
		control.SetValue(property, value);
	}

	private static void OnExplicitPaddingChanged(
		Control control,
		AvaloniaPropertyChangedEventArgs args)
	{
		var property = GetPaddingProperty(control);
		if (property != null)
		{
			OnExplicitThicknessChanged(
				control,
				property,
				s_paddingStates,
				ExplicitLeftPaddingProperty,
				ExplicitTopPaddingProperty,
				ExplicitRightPaddingProperty,
				ExplicitBottomPaddingProperty);
		}
	}

	private static void OnExplicitMarginChanged(
		Control control,
		AvaloniaPropertyChangedEventArgs args)
	{
		OnExplicitThicknessChanged(
			control,
			Layoutable.MarginProperty,
			s_marginStates,
			ExplicitLeftMarginProperty,
			ExplicitTopMarginProperty,
			ExplicitRightMarginProperty,
			ExplicitBottomMarginProperty);
	}

	private static void OnExplicitBorderThicknessChanged(
		Control control,
		AvaloniaPropertyChangedEventArgs args)
	{
		var property = GetBorderThicknessProperty(control);
		if (property != null)
		{
			OnExplicitThicknessChanged(
				control,
				property,
				s_borderThicknessStates,
				ExplicitLeftBorderThicknessProperty,
				ExplicitTopBorderThicknessProperty,
				ExplicitRightBorderThicknessProperty,
				ExplicitBottomBorderThicknessProperty);
		}
	}

	private static void OnPaddingChanged(
		Control control,
		AvaloniaPropertyChangedEventArgs args)
	{
		var property = GetPaddingProperty(control);
		if (property != null)
		{
			OnBaseThicknessChanged(
				control,
				property,
				s_paddingStates,
				args,
				ExplicitLeftPaddingProperty,
				ExplicitTopPaddingProperty,
				ExplicitRightPaddingProperty,
				ExplicitBottomPaddingProperty);
		}
	}

	private static void OnMarginChanged(
		Control control,
		AvaloniaPropertyChangedEventArgs args)
	{
		OnBaseThicknessChanged(
			control,
			Layoutable.MarginProperty,
			s_marginStates,
			args,
			ExplicitLeftMarginProperty,
			ExplicitTopMarginProperty,
			ExplicitRightMarginProperty,
			ExplicitBottomMarginProperty);
	}

	private static void OnBorderThicknessChanged(
		Control control,
		AvaloniaPropertyChangedEventArgs args)
	{
		var property = GetBorderThicknessProperty(control);
		if (property != null)
		{
			OnBaseThicknessChanged(
				control,
				property,
				s_borderThicknessStates,
				args,
				ExplicitLeftBorderThicknessProperty,
				ExplicitTopBorderThicknessProperty,
				ExplicitRightBorderThicknessProperty,
				ExplicitBottomBorderThicknessProperty);
		}
	}

	private static void OnExplicitThicknessChanged(
		Control control,
		StyledProperty<Thickness> property,
		ConditionalWeakTable<Control, ExplicitThicknessState> states,
		AttachedProperty<double?> leftProperty,
		AttachedProperty<double?> topProperty,
		AttachedProperty<double?> rightProperty,
		AttachedProperty<double?> bottomProperty)
	{
		if (!HasExplicitThickness(
				control,
				leftProperty,
				topProperty,
				rightProperty,
				bottomProperty))
		{
			RestoreBaseThickness(control, property, states);
			return;
		}

		var state = states.GetValue(
			control,
			key => new ExplicitThicknessState(key.GetValue(property)));
		ApplyExplicitThickness(
			control,
			property,
			state,
			leftProperty,
			topProperty,
			rightProperty,
			bottomProperty);
	}

	private static void OnBaseThicknessChanged(
		Control control,
		StyledProperty<Thickness> property,
		ConditionalWeakTable<Control, ExplicitThicknessState> states,
		AvaloniaPropertyChangedEventArgs args,
		AttachedProperty<double?> leftProperty,
		AttachedProperty<double?> topProperty,
		AttachedProperty<double?> rightProperty,
		AttachedProperty<double?> bottomProperty)
	{
		if (!states.TryGetValue(control, out var state) || state.IsApplying)
		{
			return;
		}

		state.BaseValue = (Thickness)args.NewValue!;
		ApplyExplicitThickness(
			control,
			property,
			state,
			leftProperty,
			topProperty,
			rightProperty,
			bottomProperty);
	}

	private static void ApplyExplicitThickness(
		Control control,
		StyledProperty<Thickness> property,
		ExplicitThicknessState state,
		AttachedProperty<double?> leftProperty,
		AttachedProperty<double?> topProperty,
		AttachedProperty<double?> rightProperty,
		AttachedProperty<double?> bottomProperty)
	{
		var baseValue = state.BaseValue;
		SetThickness(
			control,
			property,
			state,
			new Thickness(
				control.GetValue(leftProperty) ?? baseValue.Left,
				control.GetValue(topProperty) ?? baseValue.Top,
				control.GetValue(rightProperty) ?? baseValue.Right,
				control.GetValue(bottomProperty) ?? baseValue.Bottom));
	}

	private static void RestoreBaseThickness(
		Control control,
		StyledProperty<Thickness> property,
		ConditionalWeakTable<Control, ExplicitThicknessState> states)
	{
		if (!states.TryGetValue(control, out var state))
		{
			return;
		}

		SetThickness(control, property, state, state.BaseValue);
		states.Remove(control);
	}

	private static void SetThickness(
		Control control,
		StyledProperty<Thickness> property,
		ExplicitThicknessState state,
		Thickness value)
	{
		if (control.GetValue(property) == value)
		{
			return;
		}

		state.IsApplying = true;
		try
		{
			control.SetCurrentValue(property, value);
		}
		finally
		{
			state.IsApplying = false;
		}
	}

	private static bool HasExplicitThickness(
		Control control,
		AttachedProperty<double?> leftProperty,
		AttachedProperty<double?> topProperty,
		AttachedProperty<double?> rightProperty,
		AttachedProperty<double?> bottomProperty)
	{
		return control.GetValue(leftProperty) != null ||
			control.GetValue(topProperty) != null ||
			control.GetValue(rightProperty) != null ||
			control.GetValue(bottomProperty) != null;
	}

	private static StyledProperty<Thickness>? GetPaddingProperty(Control control)
	{
		return control switch
		{
			AkburaControl => PaddingProperty,
			TemplatedControl => TemplatedControl.PaddingProperty,
			Decorator => Decorator.PaddingProperty,
			_ => null,
		};
	}

	private static StyledProperty<Thickness>? GetBorderThicknessProperty(Control control)
	{
		return control switch
		{
			Border => Border.BorderThicknessProperty,
			TemplatedControl => TemplatedControl.BorderThicknessProperty,
			_ => null,
		};
	}

	private sealed class ExplicitThicknessState
	{
		public ExplicitThicknessState(Thickness baseValue)
		{
			BaseValue = baseValue;
		}

		public Thickness BaseValue { get; set; }

		public bool IsApplying { get; set; }
	}

	#endregion
}
