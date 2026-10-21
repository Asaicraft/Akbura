---
title: Expression Bindings
summary: Reactive C# property values, Avalonia Binding, and the three ways to conditionally apply AKCSS utilities.
---

# Expression bindings

Use `Text=${x.bind expression}` to assign the result of a strongly typed C#
expression to a property and track its supported observable inputs. Names come
from the component's C#/Akbura scope. Roslyn checks their types and the conversion
to the target property; this is not an Avalonia binding-path string.

`x.bind` is **one-way**. When an input notifies, Akbura reevaluates the expression
and updates the target without requiring a component render. It complements,
rather than replaces, Avalonia `Binding`.

::: warning Version compatibility
This page describes the current repository implementation. Use matching compiler,
runtime and editor versions containing expression bindings and reactive utility
conditions. An older published package may not contain these features.
:::

## Start with state

```akbura
using Avalonia.Controls;

state int count = 0;

<StackPanel>
    <TextBlock Text=${x.bind $"Count: {count}"}/>
    <TextBlock Text=${x.bind $"Double: {count * 2}"}/>
    <Button Click={count++}>Increase</Button>
</StackPanel>
```

Both results follow `count`. State assignments can independently schedule the
component's normal render, too: this example does not prove that state changes
avoid rendering. The expression binding's notification-driven reevaluation and
the state's render scheduling are separate mechanisms.

Normal C# evaluation rules apply to supported arithmetic, interpolation, logical
operators, `?:`, `??`, conditional access, nested properties and indexers. Keep
mutations in handlers, not inside binding expressions.

## A notifying model

Save this companion C# file as `ViewModel.cs`. The examples below import `Demo`
and use these actual types. The setters raise `INotifyPropertyChanged`; ordinary
auto-properties would not announce changes.

```csharp
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Demo;

public abstract class NotifyingObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

public sealed class Person(string name) : NotifyingObject
{
    private string _name = name;
    public string Name { get => _name; set => Set(ref _name, value); }
}

public sealed class ViewModel : NotifyingObject
{
    private string _name = "Ada";
    private Person? _profile = new("Ada's profile");
    private int _selectedIndex;
    private bool _usePrimaryName = true;
    private string _primaryName = "Primary";
    private string _fallbackName = "Fallback";
    private bool _isSelected;
    private bool _isDisabled;
    private bool _hasError;

    public string Name { get => _name; set => Set(ref _name, value); }
    public Person? Profile { get => _profile; set => Set(ref _profile, value); }
    public ObservableCollection<Person> People { get; } = [new("Ada"), new("Grace")];
    public int SelectedIndex { get => _selectedIndex; set => Set(ref _selectedIndex, value); }
    public bool UsePrimaryName { get => _usePrimaryName; set => Set(ref _usePrimaryName, value); }
    public string PrimaryName { get => _primaryName; set => Set(ref _primaryName, value); }
    public string FallbackName { get => _fallbackName; set => Set(ref _fallbackName, value); }
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
    public bool IsDisabled { get => _isDisabled; set => Set(ref _isDisabled, value); }
    public bool HasError { get => _hasError; set => Set(ref _hasError, value); }
}

public sealed class Holder : NotifyingObject
{
    private ViewModel _vm = new();
    public ViewModel Vm { get => _vm; set => Set(ref _vm, value); }
}
```

## Ordinary C# property expressions

```akbura
using Avalonia.Controls;
using Demo;

state ViewModel Vm = new();
state int refresh = 0;

<StackPanel DataContext={Vm}>
    <TextBlock Text={Vm.Name}/>
    <TextBlock Text={$"Frame: {refresh}"}/>
    <Button Click={refresh++}>Refresh</Button>
</StackPanel>
```

`{Vm.Name}` is evaluated by the normal component render/update path. A change to
`Vm.Name` does not independently subscribe this expression to INPC. The explicit
state change in **Refresh** requests a normal update and reads the latest name.
Ordinary expressions are not permanently static; they use a different refresh
contract.

## Avalonia Binding

```akbura
using Avalonia.Controls;
using Avalonia.Data;
using Demo;

state ViewModel Vm = new();

<TextBlock DataContext={Vm} Text=${Binding Name}/>
```

`Name` is an Avalonia binding path. By default it starts at the target's effective
`DataContext`, here `Vm`. Avalonia's binding engine observes supported source
notifications without a component render. `Binding` has its own source selection,
binding modes, converters, `StringFormat`, fallback values and other features.

## C# expression binding

```akbura
using Avalonia.Controls;
using Demo;

state ViewModel Vm = new();

<TextBlock DataContext={Vm} Text=${x.bind $"Hello, {Vm.Name}"}/>
```

`Vm` is a real component symbol. `x.bind` does not look for a `Vm` property in
`DataContext`; the `DataContext` assignment above is not needed by this binding.
Akbura tracks the supported observable reads made by the C# expression and writes
the calculated string one-way. The runtime uses `ExpressionBinding<T>`, not an
Avalonia BindingPath or a CLR markup-extension class named `x.bind`.

| Property | `${x.bind ...}` | `${Binding ...}` |
| --- | --- | --- |
| Input language | Typed C# expression | Avalonia BindingPath |
| Name resolution | Component's C#/Akbura scope | DataContext or explicitly selected source |
| Type analysis | Roslyn C# semantic analysis | Avalonia's binding mechanism, depending on binding type |
| Reactive engine | Akbura expression binding | Avalonia binding engine |
| Calculations | Supported C# operators and formatting | Binding paths, converters, StringFormat; not arbitrary C# |
| Direction | One-way | Determined by binding mode |
| Observable source update needs a component render | No | No |

Use either system where its source selection and expression model fit. There is
no claim here that `x.bind` is universally faster or preferable.

## Nested and nullable receivers

```akbura
using Avalonia.Controls;
using Demo;

state ViewModel model = new();

<StackPanel DataContext={model}>
    <TextBlock Text=${x.bind model.Profile?.Name ?? "Unknown"}/>
    <Button Click={model.Profile = new Person("Grace's profile")}>Replace profile</Button>
    <Button Click={model.Profile = null}>Clear profile</Button>
</StackPanel>
```

The binding observes the current `Profile` and its `Name`. Replacing `Profile`
disconnects the old receiver and observes the replacement. `null` selects the
fallback; no object is created automatically. If the result stays equal while a
receiver changes, the active observations still change.

## Indexed collections

```akbura
using Avalonia.Controls;
using Demo;

state ViewModel model = new();

<StackPanel DataContext={model}>
    <TextBlock Text=${x.bind model.People[model.SelectedIndex].Name}/>
    <Button Click={model.SelectedIndex = (model.SelectedIndex + 1) % model.People.Count}>Next person</Button>
    <Button Click={model.People[model.SelectedIndex].Name = "Renamed"}>Rename selected</Button>
</StackPanel>
```

Here `ObservableCollection<Person>` announces item replacement and collection
changes, `SelectedIndex` announces selection changes, and each `Person` announces
`Name` changes. The active item follows the current index. Changing an unselected
person must not replace the selected display.

Keep the collection nonempty and the index valid before evaluation; this example
never removes items. Indexing an array or `List<T>` does not invent collection
notifications. An observable item's properties may notify even when its container
cannot announce replacement.

## Dynamic conditional branches

```akbura
using Avalonia.Controls;
using Demo;

state ViewModel model = new();

<StackPanel DataContext={model}>
    <TextBlock Text=${x.bind model.UsePrimaryName ? model.PrimaryName : model.FallbackName}/>
    <Button Click={model.UsePrimaryName = !model.UsePrimaryName}>Switch branch</Button>
</StackPanel>
```

Only the selected branch is read. Editing the inactive name does not change the
result; switching branches uses that branch's latest value. Likewise, `&&`, `||`,
`??` and conditional access preserve C# short-circuit evaluation. Observations
of branches that are no longer read are released.

## Avalonia property sources

```akbura
using Avalonia.Controls;

state Slider source = new() { Value = 25 };

<StackPanel DataContext={source}>
    <TextBlock Text=${x.bind $"Value: {source.Value:0}"}/>
    <Button Click={source.Value += 5}>Change value</Button>
</StackPanel>
```

Supported registered Avalonia properties, such as `Slider.Value`, provide a
notification channel without INPC. Merely deriving from `AvaloniaObject` does not
make every arbitrary CLR property observable.

## Parameters and contextual conversions

```akbura
using Avalonia.Controls;

param int Count = 2;
state int offset = 10;

<StackPanel>
    <Border Width=${x.bind Count + offset}/>
    <Button Click={offset++}>Increase offset</Button>
</StackPanel>
```

The `int` result is implicitly converted to the `double` expected by `Width`.
Parameter changes and existing state notifications can be observed; their setters
may also request normal component updates. Incompatible conversions are diagnosed.

`x.bind` does not add a reverse write channel. Use the existing
[directional parameter attributes](/akbura/parameters) or
[`state bind/out/in`](/akbura/state#directional-connections) when that connection
is required. Do not combine `bind:` or `out:` with `x.bind` on the same assignment.

## Injected services

```akbura
using Avalonia.Controls;
using Demo;

inject ViewModel Vm;

<TextBlock DataContext={Vm} Text=${x.bind Vm.Name}/>
```

Configure a provider for `ViewModel` as explained in
[Dependency Injection](/akbura/dependency-injection). `Vm` is an injected component
symbol, not an implicitly created `DataContext.Vm` path. The explicit DataContext
assignment is useful if other controls use `${Binding Name}`; `x.bind Vm.Name`
itself does not require it. The injected model still needs to notify about changes
to its properties.

## Three ways to conditionally apply AKCSS utilities

All three forms below decide whether a utility participates in the **same AKCSS
cascade**. They differ in how the Boolean is obtained and refreshed. Import
`Akbura.Styles.akcss` and load the normal AKCSS resources in your application;
see [built-in utility resources](/akcss/utilities#spacing-scale).

### Ordinary C# condition

```akbura
using Avalonia.Controls;
using Akbura.Styles.akcss;
using Demo;

state ViewModel Vm = new();
state int refresh = 0;

<StackPanel DataContext={Vm}>
    <Border bg-slate-700 {Vm.IsSelected}:bg-blue-500 MinHeight="32"/>
    <TextBlock Text={$"Frame: {refresh}"}/>
    <Button Click={Vm.IsSelected = !Vm.IsSelected}>Toggle selection</Button>
    <Button Click={refresh++}>Refresh</Button>
</StackPanel>
```

`{Vm.IsSelected}:bg-blue-500` is an ordinary C# Boolean expression, evaluated
through the component's normal render/AKCSS refresh path. It does not install
expression-binding observations. Toggling this INPC property alone does not
request a component render; **Refresh** changes state and makes the ordinary
condition catch up. Conditions using state can naturally update during the
render requested by a state assignment.

### Avalonia Binding condition

```akbura
using Avalonia.Controls;
using Avalonia.Data;
using Akbura.Styles.akcss;
using Demo;

state ViewModel Vm = new();

<Border DataContext={Vm} bg-slate-700
        ${Binding IsSelected}:bg-blue-500 MinHeight="32"/>
```

`${Binding IsSelected}` is an ordinary Avalonia markup extension. The path starts
at this Border's effective DataContext, which is `Vm`. Avalonia Binding observes
the notifying `IsSelected` property and its Boolean result activates or
deactivates the AKCSS candidate without a component render. False restores the
slate utility in this example.

### Reactive C# expression condition

```akbura
using Avalonia.Controls;
using Akbura.Styles.akcss;
using Demo;

state ViewModel Vm = new();

<Border DataContext={Vm} bg-slate-700
        ${x.bind Vm.IsSelected && !Vm.IsDisabled}:bg-blue-500 MinHeight="32"/>
```

`Vm` resolves in component scope. Roslyn checks the C# condition, which must
convert to `bool` (`bool?` needs an explicit null fallback). Akbura observes
supported reads through `ExpressionBinding<bool>` and updates the utility
candidate directly. No DataContext path is required here.

This is useful for derived reactive conditions involving multiple properties.
When `IsSelected` is false in this example, the right-hand `IsDisabled` read is skipped
and need not remain observed. Hidden reads inside arbitrary methods are not
discovered automatically.

### A nested DataContext path

```akbura
using Avalonia.Controls;
using Avalonia.Data;
using Akbura.Styles.akcss;
using Demo;

state Holder holder = new();

<Border DataContext={holder} bg-slate-700
        ${Binding Vm.IsSelected}:bg-blue-500 MinHeight="32"/>
```

This works because `holder` actually exposes a notifying `Vm` property whose
value exposes `IsSelected`. Replacing `holder.Vm` lets Avalonia follow the new
source. An `inject ViewModel Vm;` declaration by itself does **not** establish
this DataContext path. Set `DataContext={Vm}` and bind `IsSelected`, or select an
explicit Avalonia binding source instead.

### Compare all three against one model

```akbura
using Avalonia.Controls;
using Avalonia.Data;
using Akbura.Styles.akcss;
using Demo;

state ViewModel Vm = new();
state int refresh = 0;

<StackPanel DataContext={Vm}>
    <Border Name="binding" bg-slate-700 ${Binding IsSelected}:bg-blue-500 MinHeight="32"/>
    <Border Name="expression" bg-slate-700 ${x.bind Vm.IsSelected}:bg-blue-500 MinHeight="32"/>
    <Border Name="frame" bg-slate-700 {Vm.IsSelected}:bg-blue-500 MinHeight="32"/>
    <TextBlock Text={$"Frame: {refresh}"}/>
    <Button Click={Vm.IsSelected = !Vm.IsSelected}>Toggle selection</Button>
    <Button Click={refresh++}>Refresh ordinary condition</Button>
</StackPanel>
```

Both the Binding and x.bind cards follow selection immediately. The ordinary
card catches up when you request a refresh. Toggling an INPC model property in
a button handler does not itself request an Akbura component render.

| Behavior | `{condition}:utility` | `${Binding Path}:utility` | `${x.bind expression}:utility` |
| --- | --- | --- | --- |
| Input | C# Boolean expression | Avalonia BindingPath | C# Boolean expression |
| Evaluation | Normal component/AKCSS refresh | Avalonia binding engine | Akbura expression observer |
| Names | Component scope | DataContext/selected source | Component scope |
| Observable update without component render | Not independently | Yes, with notifying source | Yes, with supported observable inputs |
| Arbitrary supported C# logic | Yes | Not directly | Yes |
| Conflict handling | AKCSS cascade | AKCSS cascade | AKCSS cascade |
| Use when | The condition belongs to normal rendering | Existing Avalonia source selection/binding features fit | You need derived reactive C# logic |

### Shared cascade and restoration

```akbura
using Avalonia.Controls;
using Avalonia.Data;
using Akbura.Styles.akcss;
using Demo;

state ViewModel Vm = new();

<StackPanel DataContext={Vm}>
    <Border Name="binding" bg-slate-700
            ${Binding IsSelected}:bg-blue-500 ${Binding HasError}:bg-red-500 p-3/>
    <Border Name="expression" bg-slate-700
            ${x.bind Vm.IsSelected}:bg-blue-500 ${x.bind Vm.HasError}:bg-red-500 p-3/>
</StackPanel>
```

For both cards: neither condition gives slate; selected gives blue; error gives
red. If both are true, red wins because it appears later with otherwise equal
precedence. Clearing error restores blue; clearing both restores slate. Padding
is unrelated and remains `12` with the default spacing resource. Neither
observation engine requires a separate styling system or a different cascade.
See [Utility Variants](/akcss/utility-variants) for ordering metadata and
property-level conflict resolution.

## Observation boundaries and lifetime

- Notifications come from existing Akbura state/parameters/services,
  `INotifyPropertyChanged`, supported registered Avalonia properties and
  `INotifyCollectionChanged` on indexed collections. Plain CLR members and
  ordinary collections do not acquire notification channels.
- The actual receivers and branches read by the expression are observed.
  Replacing a nested receiver or changing the index reconnects observations.
- Arbitrary method bodies and user-defined operations can hide dependencies.
  Partial-observation warnings mean automatic reactivity is not fully guaranteed;
  an explicit supported call is not a promise of method-body inspection.
- Render locals capture values from that render. A local initialized from
  `model.Profile` is not automatically an observation of that initializer.
- `x.bind` requires a writable, supported scalar property target. It is not an
  event handler, command declaration or collection assignment. Mutations,
  construction, deferred lambdas, `await`, dynamic operations and other
  unsupported forms are diagnosed inside binding expressions.
- Detaching suspends owned observations; reattaching reads current inputs.
  Disposing the owning component releases subscriptions. Hot Reload uses the
  normal component lifetime and reconciliation mechanisms, not a polling loop.

For editing assistance, see [Editor Completion](/akbura/editor-completion).
Try the live examples in [FeatureGallery](https://asaicraft.github.io/Akbura/Gallery/)
and choose **Expression bindings**, or run the repository's desktop gallery.
The hosted gallery may lag behind this branch.
