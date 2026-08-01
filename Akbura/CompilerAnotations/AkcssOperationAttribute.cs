using System;
using System.ComponentModel;

namespace Akbura.CompilerAnotations;

/// <summary>
/// Describes a single flattened operation generated from an AKCSS style
/// or utility declaration.
/// </summary>
/// <remarks>
/// <para>
/// This attribute is part of the compiler metadata contract between generated
/// AKCSS code, the Akbura compiler, and the AKCSS runtime.
/// </para>
/// <para>
/// IMPORTANT: Do not rename, remove, reorder, or change the semantics of this
/// type or its members without making the corresponding changes in every
/// compiler and runtime component that consumes AKCSS metadata.
/// </para>
/// <para>
/// AKCSS operations are emitted as a flattened ordered sequence. Nested
/// operations, such as declarations inside an <c>@if</c> block, are connected
/// to their containing operation through <see cref="ParentOrder"/>.
/// </para>
/// <para>
/// The attribute can describe the four operation kinds currently represented
/// by the AKCSS semantic model:
/// </para>
/// <list type="bullet">
/// <item>
/// <description>
/// A property assignment represented by
/// <see cref="AkcssOperationKind.Set"/>.
/// </description>
/// </item>
/// <item>
/// <description>
/// A conditional block represented by
/// <see cref="AkcssOperationKind.If"/>.
/// </description>
/// </item>
/// <item>
/// <description>
/// A style or utility composition represented by
/// <see cref="AkcssOperationKind.Apply"/>.
/// </description>
/// </item>
/// <item>
/// <description>
/// A C# interceptor declaration represented by
/// <see cref="AkcssOperationKind.Intercept"/>.
/// </description>
/// </item>
/// </list>
/// <para>
/// C# expressions stored in <see cref="Expression"/> must be independent of
/// source-level <c>using</c> directives. Every referenced type must therefore
/// use its fully qualified name with the <c>global::</c> prefix.
/// </para>
/// </remarks>
/// <example>
/// The following AKCSS declaration:
/// <code>
/// @using System;
/// @using Avalonia;
///
/// Border.card {
///     Padding: Math.Pow(2, 3) * new Thickness(3);
///
///     @if(IsPointerOver) {
///         Padding: new Thickness(16);
///     }
/// }
/// </code>
///
/// Can be represented as:
/// <code>
/// [AkcssOperation(
///     Order = 1,
///     Kind = AkcssOperationKind.Set,
///     TargetType = typeof(global::Avalonia.Controls.Border),
///     PropertyAccessKind = AkcssPropertyAccessKind.AvaloniaProperty,
///     Property = "Padding",
///     AvaloniaProperty = "PaddingProperty",
///     PropertyOwnerType = typeof(global::Avalonia.Controls.Decorator),
///     PropertyType = typeof(global::Avalonia.Thickness),
///     ValueKind = AkcssPropertyValueKind.CSharpExpression,
///     ExpressionType = typeof(global::Avalonia.Thickness),
///     Expression =
///         "global::System.Math.Pow(2, 3) * " +
///         "new global::Avalonia.Thickness(3)",
///     Priority = AkcssOperationPriority.Style)]
///
/// [AkcssOperation(
///     Order = 2,
///     Kind = AkcssOperationKind.If,
///     TargetType = typeof(global::Avalonia.Controls.Border),
///     ExpressionType = typeof(bool),
///     Expression =
///         "((global::Avalonia.Input.InputElement)__target).IsPointerOver",
///     IfStartOrder = 3,
///     IfEndOrder = 3)]
///
/// [AkcssOperation(
///     Order = 3,
///     ParentOrder = 2,
///     Depth = 1,
///     Kind = AkcssOperationKind.Set,
///     TargetType = typeof(global::Avalonia.Controls.Border),
///     PropertyAccessKind = AkcssPropertyAccessKind.AvaloniaProperty,
///     Property = "Padding",
///     AvaloniaProperty = "PaddingProperty",
///     PropertyOwnerType = typeof(global::Avalonia.Controls.Decorator),
///     PropertyType = typeof(global::Avalonia.Thickness),
///     ValueKind = AkcssPropertyValueKind.CSharpExpression,
///     ExpressionType = typeof(global::Avalonia.Thickness),
///     Expression = "new global::Avalonia.Thickness(16)",
///     Priority = AkcssOperationPriority.StyleTrigger)]
/// </code>
/// </example>
[AttributeUsage(
    AttributeTargets.Class,
    AllowMultiple = true,
    Inherited = false)]
[Browsable(false)]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class AkcssOperationAttribute : Attribute
{
    /// <summary>
    /// Gets or sets the unique sequential order of the operation.
    /// </summary>
    /// <value>
    /// The flattened operation index within the generated AKCSS style
    /// or utility.
    /// </value>
    /// <remarks>
    /// Operations must be processed in ascending order. The value must be
    /// unique within the generated type containing the attributes.
    /// </remarks>
    public int Order { get; set; }

    /// <summary>
    /// Gets or sets the kind of operation represented by this attribute.
    /// </summary>
    public AkcssOperationKind Kind { get; set; }

    /// <summary>
    /// Gets or sets how this operation was introduced into the flattened
    /// operation sequence.
    /// </summary>
    /// <remarks>
    /// This distinguishes source-declared operations from operations produced
    /// by <c>@apply</c> expansion or synthesized by the compiler.
    /// </remarks>
    public AkcssOperationOriginKind Origin { get; set; }

    /// <summary>
    /// Gets or sets the order of the immediate containing operation.
    /// </summary>
    /// <value>
    /// The <see cref="Order"/> of the containing
    /// <see cref="AkcssOperationKind.If"/> operation, or <c>-1</c> when the
    /// operation is not nested.
    /// </value>
    /// <remarks>
    /// Unlike <see cref="IfStartOrder"/> and <see cref="IfEndOrder"/>, this
    /// property supports arbitrarily nested conditional blocks.
    /// </remarks>
    public int ParentOrder { get; set; } = -1;

    /// <summary>
    /// Gets or sets the nesting depth of the operation.
    /// </summary>
    /// <value>
    /// <c>0</c> for a top-level operation, <c>1</c> for an operation directly
    /// inside an <c>@if</c>, and so on.
    /// </value>
    public int Depth { get; set; }

    /// <summary>
    /// Gets or sets the effective styling priority used by a property
    /// assignment.
    /// </summary>
    /// <remarks>
    /// This property is meaningful for <see cref="AkcssOperationKind.Set"/>
    /// operations. Normal declarations use
    /// <see cref="AkcssOperationPriority.Style"/>, while declarations inside
    /// an active <c>@if</c> use
    /// <see cref="AkcssOperationPriority.StyleTrigger"/>.
    /// </remarks>
    public AkcssOperationPriority Priority { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the compiler detected an error
    /// while binding the operation.
    /// </summary>
    /// <remarks>
    /// Runtime code should normally ignore operations for which this property
    /// is <see langword="true"/>. Production generators should generally avoid
    /// emitting invalid operations unless the metadata is required by tooling.
    /// </remarks>
    public bool HasErrors { get; set; }

    /// <summary>
    /// Gets or sets the fully qualified name of the AKCSS symbol that originally
    /// declared this operation.
    /// </summary>
    /// <value>
    /// A style, utility, or generated metadata name.
    /// </value>
    /// <remarks>
    /// This is particularly useful for operations introduced by
    /// <c>@apply</c>.
    /// </remarks>
    public string? DeclaringSymbol { get; set; }

    /// <summary>
    /// Gets or sets the type of object to which the containing style or utility
    /// can be applied.
    /// </summary>
    /// <remarks>
    /// This is the selector target type, not necessarily the same type as
    /// <see cref="PropertyOwnerType"/>.
    /// </remarks>
    public Type? TargetType { get; set; }

    /// <summary>
    /// Gets or sets the mechanism used to read or write the target property.
    /// </summary>
    public AkcssPropertyAccessKind PropertyAccessKind { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the operation targets a regular
    /// Avalonia property.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This property is retained for compatibility with older generated
    /// metadata. New generators should set
    /// <see cref="PropertyAccessKind"/> directly.
    /// </para>
    /// <para>
    /// Attached Avalonia properties must use
    /// <see cref="AkcssPropertyAccessKind.AttachedAccessor"/> instead.
    /// </para>
    /// </remarks>
    public bool IsAvaloniaProperty
    {
        get =>
            PropertyAccessKind ==
            AkcssPropertyAccessKind.AvaloniaProperty;

        set
        {
            if (value)
            {
                PropertyAccessKind =
                    AkcssPropertyAccessKind.AvaloniaProperty;
            }
            else if (
                PropertyAccessKind ==
                AkcssPropertyAccessKind.AvaloniaProperty)
            {
                PropertyAccessKind =
                    AkcssPropertyAccessKind.None;
            }
        }
    }

    /// <summary>
    /// Gets or sets the source-level property name.
    /// </summary>
    /// <value>
    /// A property name such as <c>Padding</c>, <c>Width</c>,
    /// <c>Grid.Row</c>, or <c>Age</c>.
    /// </value>
    public string? Property { get; set; }

    /// <summary>
    /// Gets or sets the name of the static registered Avalonia property field.
    /// </summary>
    /// <value>
    /// A field name such as <c>PaddingProperty</c>.
    /// </value>
    /// <remarks>
    /// This property is used for regular and attached Avalonia properties.
    /// </remarks>
    public string? AvaloniaProperty { get; set; }

    /// <summary>
    /// Gets or sets the name of the static attached-property getter.
    /// </summary>
    /// <value>
    /// A method name such as <c>GetRow</c>, or <see langword="null"/> when the
    /// property is not accessed through an attached-property getter.
    /// </value>
    public string? AttachedGetter { get; set; }

    /// <summary>
    /// Gets or sets the name of the static attached-property setter.
    /// </summary>
    /// <value>
    /// A method name such as <c>SetRow</c>, or <see langword="null"/> when the
    /// property is not accessed through an attached-property setter.
    /// </value>
    public string? AttachedSetter { get; set; }

    /// <summary>
    /// Gets or sets the type that declares or owns the property.
    /// </summary>
    /// <remarks>
    /// For an Avalonia property, this is the type that exposes the static
    /// property field. For a CLR property, this is the declaring CLR type.
    /// </remarks>
    public Type? PropertyOwnerType { get; set; }

    /// <summary>
    /// Gets or sets the value type of the target property.
    /// </summary>
    public Type? PropertyType { get; set; }

    /// <summary>
    /// Gets or sets the type accepted by an attached-property accessor.
    /// </summary>
    /// <remarks>
    /// This property is meaningful when <see cref="PropertyAccessKind"/> is
    /// <see cref="AkcssPropertyAccessKind.AttachedAccessor"/>.
    /// </remarks>
    public Type? AttachedTargetType { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the property can be read.
    /// </summary>
    /// <remarks>
    /// Read access is required when the property is referenced by a reactive
    /// expression or condition.
    /// </remarks>
    public bool CanRead { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the property can be written.
    /// </summary>
    /// <remarks>
    /// A <see cref="AkcssOperationKind.Set"/> operation normally requires this
    /// property to be <see langword="true"/>.
    /// </remarks>
    public bool CanWrite { get; set; } = true;

    /// <summary>
    /// Gets or sets the syntactic or semantic category of the assigned value.
    /// </summary>
    /// <remarks>
    /// This allows the runtime or tooling to distinguish ordinary C#
    /// expressions from AKCSS-specific shorthand such as color literals,
    /// thickness tuples, and <c>Amx</c> invocations.
    /// </remarks>
    public AkcssPropertyValueKind ValueKind { get; set; }

    /// <summary>
    /// Gets or sets the compiled C# expression associated with the operation.
    /// </summary>
    /// <value>
    /// For <see cref="AkcssOperationKind.Set"/>, the expression that calculates
    /// the assigned value. For <see cref="AkcssOperationKind.If"/>, the Boolean
    /// condition. The value is normally <see langword="null"/> for
    /// <see cref="AkcssOperationKind.Apply"/> and
    /// <see cref="AkcssOperationKind.Intercept"/>.
    /// </value>
    /// <remarks>
    /// <para>
    /// Every referenced type must use its fully qualified name with the
    /// <c>global::</c> prefix.
    /// </para>
    /// <para>
    /// The expression must not depend on AKCSS or C# <c>using</c> directives,
    /// aliases, or the namespace containing the generated code.
    /// </para>
    /// <para>
    /// Correct examples:
    /// </para>
    /// <code>
    /// global::System.Math.Pow(2, 3)
    ///
    /// new global::Avalonia.Thickness(3)
    ///
    /// global::Akbura.Amx.DynamicResource&lt;
    ///     global::Avalonia.Media.IBrush&gt;("--color-blue-500")
    /// </code>
    /// </remarks>
    public string? Expression { get; set; }

    /// <summary>
    /// Gets or sets the compile-time result type of
    /// <see cref="Expression"/>.
    /// </summary>
    /// <remarks>
    /// For an <see cref="AkcssOperationKind.If"/> operation, this must normally
    /// be <see cref="Boolean"/>.
    /// </remarks>
    public Type? ExpressionType { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the generated value must be
    /// wrapped in an Avalonia <c>SolidColorBrush</c>.
    /// </summary>
    /// <remarks>
    /// This supports AKCSS color expressions assigned to brush-valued
    /// properties.
    /// </remarks>
    public bool RequiresBrushConversion { get; set; }

    /// <summary>
    /// Gets or sets the normalized textual representation of a compile-time
    /// constant value.
    /// </summary>
    /// <remarks>
    /// Arbitrary objects cannot be embedded directly into .NET attribute
    /// metadata. Therefore, constant values that are not valid attribute
    /// arguments must be stored as normalized text.
    /// </remarks>
    public string? ConstantValue { get; set; }

    /// <summary>
    /// Gets or sets the runtime type represented by
    /// <see cref="ConstantValue"/>.
    /// </summary>
    public Type? ConstantValueType { get; set; }

    /// <summary>
    /// Gets or sets the order of the first operation controlled by an
    /// <see cref="AkcssOperationKind.If"/> operation.
    /// </summary>
    /// <value>
    /// The inclusive order of the first operation, or <c>-1</c> when the
    /// operation is not an <c>@if</c> operation or the block is empty.
    /// </value>
    public int IfStartOrder { get; set; } = -1;

    /// <summary>
    /// Gets or sets the order of the last operation controlled by an
    /// <see cref="AkcssOperationKind.If"/> operation.
    /// </summary>
    /// <value>
    /// The inclusive order of the last operation, or <c>-1</c> when the
    /// operation is not an <c>@if</c> operation or the block is empty.
    /// </value>
    /// <remarks>
    /// Nested operations must additionally use <see cref="ParentOrder"/>;
    /// a start/end range alone cannot represent arbitrary nesting reliably.
    /// </remarks>
    public int IfEndOrder { get; set; } = -1;

    /// <summary>
    /// Gets or sets the source-level names referenced by an <c>@apply</c>
    /// operation.
    /// </summary>
    /// <value>
    /// The original style and utility names in source order.
    /// </value>
    /// <example>
    /// <code>
    /// new[] { "surface", "p-4", "rounded-lg" }
    /// </code>
    /// </example>
    public string[] ApplyItems { get; set; } = [];

    /// <summary>
    /// Gets or sets the fully qualified metadata names of the symbols resolved
    /// by an <c>@apply</c> operation.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="ApplyItems"/>, these values identify the actual
    /// resolved AKCSS symbols and are suitable for diagnostics, incremental
    /// compilation, and source mapping.
    /// </remarks>
    public string[] AppliedSymbols { get; set; } = [];

    /// <summary>
    /// Gets or sets the order of the first operation produced by expanding an
    /// <c>@apply</c> operation.
    /// </summary>
    /// <value>
    /// The inclusive first expanded operation, or <c>-1</c> when the
    /// composition is not materialized in the metadata sequence.
    /// </value>
    public int ExpansionStartOrder { get; set; } = -1;

    /// <summary>
    /// Gets or sets the order of the last operation produced by expanding an
    /// <c>@apply</c> operation.
    /// </summary>
    /// <value>
    /// The inclusive last expanded operation, or <c>-1</c> when the
    /// composition is not materialized in the metadata sequence.
    /// </value>
    public int ExpansionEndOrder { get; set; } = -1;

    /// <summary>
    /// Gets or sets the order of the <c>@apply</c> operation that produced the
    /// current expanded operation.
    /// </summary>
    /// <value>
    /// The originating operation order, or <c>-1</c> when the current
    /// operation was not produced by <c>@apply</c>.
    /// </value>
    public int ExpandedFromOrder { get; set; } = -1;

    /// <summary>
    /// Gets or sets the C# type that replaces the generated AKCSS style or
    /// utility implementation.
    /// </summary>
    /// <remarks>
    /// This property is meaningful only when <see cref="Kind"/> is
    /// <see cref="AkcssOperationKind.Intercept"/>.
    /// </remarks>
    public Type? InterceptType { get; set; }

    /// <summary>
    /// Gets or sets the normalized source path containing the operation.
    /// </summary>
    /// <remarks>
    /// The value is used for diagnostics, source mapping, generated
    /// <c>#line</c> directives, and tooling.
    /// </remarks>
    public string? SourcePath { get; set; }

    /// <summary>
    /// Gets or sets the zero-based source-text position at which the operation
    /// begins.
    /// </summary>
    /// <value>
    /// The source position, or <c>-1</c> when source mapping is unavailable.
    /// </value>
    public int SourceStart { get; set; } = -1;

    /// <summary>
    /// Gets or sets the source-text length of the operation.
    /// </summary>
    public int SourceLength { get; set; }
}

/// <summary>
/// Specifies the kind of flattened AKCSS operation.
/// </summary>
/// <remarks>
/// IMPORTANT: This enumeration is part of the compiler metadata contract.
/// Do not rename, remove, reorder, or change its members without updating all
/// metadata producers and consumers.
/// </remarks>
[Browsable(false)]
[EditorBrowsable(EditorBrowsableState.Never)]
public enum AkcssOperationKind
{
    /// <summary>
    /// Assigns a value to an Avalonia, attached, CLR, parameter, or command
    /// property abstraction.
    /// </summary>
    Set,

    /// <summary>
    /// Evaluates a Boolean expression and conditionally executes a nested
    /// operation range.
    /// </summary>
    If,

    /// <summary>
    /// Composes one or more styles or utilities through <c>@apply</c>.
    /// </summary>
    Apply,

    /// <summary>
    /// Replaces the generated style or utility with a custom C#
    /// implementation.
    /// </summary>
    Intercept,
}

/// <summary>
/// Specifies how an operation entered the flattened metadata sequence.
/// </summary>
[Browsable(false)]
[EditorBrowsable(EditorBrowsableState.Never)]
public enum AkcssOperationOriginKind
{
    /// <summary>
    /// The operation was declared directly in AKCSS source.
    /// </summary>
    Direct,

    /// <summary>
    /// The operation was introduced by expanding an <c>@apply</c>
    /// declaration.
    /// </summary>
    ApplyExpansion,

    /// <summary>
    /// The operation was synthesized by the compiler.
    /// </summary>
    Synthesized,
}

/// <summary>
/// Specifies how the compiler accesses a property represented by an AKCSS
/// operation.
/// </summary>
/// <remarks>
/// The values mirror the property-access categories used by the Akbura
/// semantic model.
/// </remarks>
[Browsable(false)]
[EditorBrowsable(EditorBrowsableState.Never)]
public enum AkcssPropertyAccessKind
{
    /// <summary>
    /// No property access mechanism is associated with the operation.
    /// </summary>
    None,

    /// <summary>
    /// The property is accessed through a normal CLR property.
    /// </summary>
    ClrProperty,

    /// <summary>
    /// The property is accessed through a registered Avalonia property field.
    /// </summary>
    AvaloniaProperty,

    /// <summary>
    /// The property is accessed through static attached-property accessors.
    /// </summary>
    AttachedAccessor,

    /// <summary>
    /// The property represents an Akbura component parameter.
    /// </summary>
    Parameter,

    /// <summary>
    /// The property represents an Akbura command facade.
    /// </summary>
    Command,
}

/// <summary>
/// Specifies how an AKCSS property value was expressed in source.
/// </summary>
/// <remarks>
/// IMPORTANT: These values correspond to the value categories recognized by
/// the AKCSS binder.
/// </remarks>
[Browsable(false)]
[EditorBrowsable(EditorBrowsableState.Never)]
public enum AkcssPropertyValueKind
{
    /// <summary>
    /// No value category was resolved.
    /// </summary>
    None,

    /// <summary>
    /// The value is represented by a regular C# expression.
    /// </summary>
    CSharpExpression,

    /// <summary>
    /// The value was written using an AKCSS color literal.
    /// </summary>
    ColorLiteral,

    /// <summary>
    /// The value was written using AKCSS thickness tuple syntax.
    /// </summary>
    ThicknessTuple,

    /// <summary>
    /// The value is produced by an <c>Amx</c> invocation, such as a dynamic
    /// resource lookup.
    /// </summary>
    AmxInvocation,

    /// <summary>
    /// The compiler could not bind the value successfully.
    /// </summary>
    Error,
}

/// <summary>
/// Specifies the effective styling priority of a generated AKCSS property
/// assignment.
/// </summary>
[Browsable(false)]
[EditorBrowsable(EditorBrowsableState.Never)]
public enum AkcssOperationPriority
{
    /// <summary>
    /// Represents a normal AKCSS style declaration.
    /// </summary>
    Style,

    /// <summary>
    /// Represents a declaration inside an active AKCSS <c>@if</c> block.
    /// </summary>
    StyleTrigger,
}