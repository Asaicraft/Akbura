---
title: Markup Extensions
summary: Use static and dynamic Avalonia resources from AKCSS expressions.
---

::: warning
Akbura does not currently support general Avalonia `MarkupExtension` types. Custom markup extensions cannot yet be instantiated and evaluated from Akbura markup or AKCSS.
:::

AKCSS currently provides two compiler-supported resource extensions:

```csharp
Amx.StaticResource<T>(object? key);
Amx.DynamicResource<T>(object? key);
```

They provide typed access to resources from AKCSS expressions.

| Method | Behavior |
| --- | --- |
| `Amx.StaticResource<T>(key)` | Resolves the resource as `T` without creating a dynamic resource binding. |
| `Amx.DynamicResource<T>(key)` | Observes the resource and updates the target property when its value changes. |

## Compiler-intercepted methods

The methods are declared by the `Amx` class:

```csharp
/// <summary>
/// All methods in this class shoud be intercepted
/// </summary>
public static class Amx
{
    public static object? Extend<T>(params object[] args)
    {
        throw new InvalidOperationException("This method shoud be intercepted!");
    }

    public static T StaticResource<T>(object? key)
    {
        throw new InvalidOperationException("This method shoud be intercepted!");
    }

    public static T DynamicResource<T>(object? key)
    {
        throw new InvalidOperationException("This method shoud be intercepted!");
    }
}
```

These methods must not execute at runtime. The AKCSS compiler recognizes their invocations and replaces them with generated Avalonia resource lookup or binding code. Calling them directly from ordinary C# code throws `InvalidOperationException`.

`Amx.Extend<T>` is an interception placeholder for general markup extensions. It is not currently supported for use in AKCSS; only `StaticResource<T>` and `DynamicResource<T>` are available.

## Static resources

Use `StaticResource<T>` when the resource does not need to update the target after it has been resolved:

```akcss
@using Avalonia.Media;

Button.brand {
    Background: Amx.StaticResource<IBrush>("BrandBrush");
}
```

The generic type argument tells the compiler which type the resource value must have.

## Dynamic resources

Use `DynamicResource<T>` when changing the resource should update the styled property:

```akcss
Control.w-(double width) {
    Width: width * Amx.DynamicResource<double>("--spacing");
}
```

```akbura
using Akbura.Styles.akcss;

<Button w-10>
    Continue
</Button>
```

With the default `--spacing` value of `4`, `w-10` produces a width of `40`. If `--spacing` changes, the `Width` property is recalculated automatically.

The resource invocation may be part of a larger expression. In this example, the generated converter captures the `width` utility parameter and multiplies it by every new resource value.

## Generated code

The `w-` utility above generates code equivalent to:

```csharp
[global::Akbura.CompilerAnotations.StyleNameAttribute("w")] 
private sealed class Style_0 : global::Akbura.Akcss.AkcssUtility<double>
{
    public override void Update(object __target, double width)
    {
        global::System.ArgumentNullException.ThrowIfNull(__target);
        if (__target is global::Avalonia.Layout.Layoutable && __target is global::Avalonia.Controls.IResourceHost)
        {
            #line 11 "D:\\Repos\\Akbura\\Akbura\\Styles.akcss"
            TrackSubscription(__target, ((global::Avalonia.AvaloniaObject)__target).Bind(global::Avalonia.Layout.Layoutable.WidthProperty, global::Avalonia.Controls.ResourceNodeExtensions.GetResourceObservable((global::Avalonia.Controls.IResourceHost)__target, "--spacing", converter: __resourceValue => global::System.Object.ReferenceEquals(__resourceValue, global::Avalonia.AvaloniaProperty.UnsetValue) ? global::Avalonia.AvaloniaProperty.UnsetValue : (object?)(width * (double)__resourceValue!))));
            #line default
        }
    }

    public override void Reset(object __target)
    {
        global::System.ArgumentNullException.ThrowIfNull(__target);
        base.Reset(__target);
        if (__target is global::Avalonia.AvaloniaObject && __target is global::Avalonia.Layout.Layoutable)
        {
            ((global::Avalonia.AvaloniaObject)__target).ClearValue(global::Avalonia.Layout.Layoutable.WidthProperty);
        }
    }
}
```

The generated utility:

1. Checks that the target supports the property and implements `IResourceHost`.
2. Obtains an observable for the `--spacing` resource.
3. Converts every resource value using the original AKCSS expression.
4. Binds the result to `Width` and tracks the subscription.
5. Disposes the tracked binding and clears the property when the utility is reset.

## Current limitations

- Custom Avalonia `MarkupExtension` implementations are not supported yet.
- `Amx.Extend<T>` is not available for application code.
- Resource methods are supported only where the Akbura compiler can intercept the expression.
- The target must be an Avalonia resource host for dynamic resource observation.
