---
title: Conditional Markup
summary: Select markup content with C# conditions and branch-local scopes.
---

# Conditional Markup

`$if`, `$else if`, and `$else` describe alternative markup content without
adding a visual wrapper. These compiler and editor changes are being integrated;
they do not imply support in an already published alpha package. Use matching
compiler and runtime versions when the feature is released.

## Syntax

```akbura
using Avalonia.Controls;

state bool expanded = false;

<StackPanel>
    <Button Click={expanded = !expanded}>Toggle details</Button>

    $if (expanded)
    {
        <TextBlock Text="Details are visible." />
    }
    $else
    {
        <TextBlock Text="Open the details to continue." />
    }
</StackPanel>
```

Conditions are ordinary C# boolean expressions, evaluated in order until the
first successful branch. Nullable booleans need an explicit check such as
`enabled == true`; values are not treated as JavaScript-like truthy values.

A chain can have several `$else if (condition)` clauses and one final `$else`.
`$else if` contains one `$`, not `$else $if`. Formatting whitespace can separate
clauses, but another element or meaningful text ends the chain.

Blocks may be empty or nested. Without a matching condition and without an
`$else`, the statement contributes no child. The contents of a block are markup,
not arbitrary C# statements or component declarations.

## Content destinations

Statements belong inside an element body or a property-element body:

```akbura
<Border>
    <Border.Child>
        $if (expanded)
        {
            <TextBlock Text="Expanded" />
        }
        $else
        {
            <Button>Expand</Button>
        }
    </Border.Child>
</Border>
```

For a scalar property such as `Border.Child`, each possible active branch must
contribute at most one compatible child. Two alternative children are not two
simultaneous children. A branch containing two controls is still invalid; no
hidden `StackPanel` is inserted to make it fit.

Top-level conditionals that replace the component's root are outside this
syntax contract. Add-only destinations also need an explicit reversible
lifecycle contract before reactive switching can be supported.

## Templates

Conditional content can appear beneath a stable root:

```akbura
<ItemsControl.ItemTemplate x.DataType="Person" x.ItemName="person">
    <Border>
        $if (person is { Selected: true })
        {
            <TextBlock Text={person.Name} />
        }
    </Border>
</ItemsControl.ItemTemplate>
```

Each built instance owns separate branch storage. Parent updates refresh its
active content without sharing controls or branch selection between instances.
Removing an instance releases its conditional resources. In this example the
stable root is the `Border` written in the template, not an extra wrapper
inserted by the compiler.

A typed `IDataTemplate` destination, native `DataTemplate.Content`, or native
`ControlTemplate.Content` can also select the entire returned control:

```akbura
<ContentControl.ContentTemplate x.DataType="Person" x.ItemName="person">
    $if (person is { Selected: true })
    {
        <TextBlock Text={person.Name} />
    }
    $else if (person is { CanEdit: true })
    {
        <Button>Edit</Button>
    }
</ContentControl.ContentTemplate>
```

An inactive chain can return no control. Each possible active branch still
contributes at most one compatible control. Switching the selected root updates
the native `ContentPresenter` or templated-control host; a nonvisual per-instance
coordinator stores its lifetime, and no hidden visual wrapper is added. Native
data-template matching and the per-instance name scope remain in use.
Native generic data-template matching can accept `null` for a reference data
type. Guard nullable data in the condition rather than relying on the adapter
to change that matching contract.

Whole-root factories require their actual native host. A standalone manual
`IDataTemplate.Build` call without that host does not create a provisional
control. Custom template contracts and hosts that do not provide this bridge
are not supported; unsupported deferred destinations are diagnosed rather than
silently dropping their content.

Render locals used by a retained conditional template are read from the current
parent render, including nullable values and later assignments in that render.
Pattern and `out` variables from an enclosing conditional are captured from
the currently reached branch; separate template instances retain separate
item contexts.
Capturing anonymous, ref-like, or pointer types into this retained storage is
currently unsupported and diagnosed. Ordinary parent expressions do not acquire
this additional capture restriction.

Conditional assignment of complete `DataTemplate` objects to a template
property is a different operation and remains allowed. Such an assignment is
evaluated outside the template build: its conditions, assignment values, and
template headers cannot read an item alias declared only on that same
destination. An enclosing template's item alias
remains available in its normal scope.

## C# and branch-local scope

Parameters, reactive state, imports, and existing item/data contexts remain
available to conditions and active content. Pattern variables belong to their
C# branch scope:

```akbura
<StackPanel>
    $if (model is Person person)
    {
        <TextBlock Text={person.Name} />
    }
</StackPanel>
```

The editor uses the same connected C# projection for condition checks and branch
expressions. It does not invent a second set of binding rules. Changing an
ancestor condition refreshes completion for its descendants, even when their
text is unchanged.

An `x.Name` declared in a conditional branch is branch-local, including access
from nested branches. Native element-name bindings and template `PART` lookup
use that branch's name scope; separate template instances do not share names.
Inner names shadow enclosing names and do not become visible in the enclosing
scope. Do not assume a non-null, unconditional accessor outside that branch.
Parent hooks must be called unconditionally outside conditional
expressions; branch switching does not reset the parent's hook frame.

## Lifetime contract

Only the active branch creates controls, bindings, event handlers, and markup
extension results. While that branch remains active, its dynamic properties
continue updating and compatible controls are retained. Inactive alternatives
are not eagerly constructed.

The default exit policy is unmount, not keep-alive: switching `A → B → A`
creates a new A. Parent state is retained, but control-local input from the
previous A is not promised after leaving it. Hot Reload reconciliation of a
still-active branch is a separate operation from an ordinary condition toggle.

## Editor support

Directive completion is available in markup content. `$else if` and `$else` are
offered only after an eligible chain. Conditions and branch expressions use C#
completion, symbol information, and source-mapped navigation. Braces and
parentheses participate in pairing, indentation, and folding.

`${Binding ...}` remains a markup extension; `"$if"` in an attribute remains a
string. To render literal directive-looking text in content, use an existing
inline string expression such as `{"$if"}`.

See [Components and Markup](/#components-and-markup),
[Parameters](/#parameters), and [Binding](/#binding) for the surrounding language
rules.
