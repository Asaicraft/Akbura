---
title: Parameters and Directional Bindings
summary: Parent-to-child, two-way, and child-to-parent component parameters and markup attributes.
---

# Parameters and directional bindings

An Akbura component declares its public inputs and outputs with `param`. A
parameter without a default value is required. Its modifier defines the
directions that a parent is allowed to connect.

| Declaration | Direction | Allowed parent attributes |
| --- | --- | --- |
| `param T Value;` | parent → child | `Value={source}` |
| `param bind T Value;` | parent ↔ child | `Value={source}`, `bind:Value={target}`, `out:Value={target}` |
| `param out T Value;` | child → parent | `out:Value={target}` |

`bind` and `out` describe the connection between parent and child. They do not
make an output parameter read-only inside the child: the child writes its output
normally.

## Parent-to-child parameters

Use an unmodified parameter when the parent supplies a value:

```akbura
// Greeting.akbura
using Avalonia.Controls;

namespace Demo;

param string Name;
param string Prefix = "Hello";

<TextBlock Text={$"{Prefix}, {Name}"} />
```

The parent uses ordinary attribute assignment:

```akbura
<Greeting Name={userName} Prefix="Welcome" />
```

## Two-way parameters

Declare `param bind` when both parent and child may update the value:

```akbura
// EditorField.akbura
using Avalonia.Controls;

namespace Demo;

param bind string Text = "";

<TextBox bind:Text={Text} />
```

Connect it to writable parent state with `bind:`:

```akbura
state string text = "Initial";

<EditorField bind:Text={text} />
<TextBlock Text={text} />
```

The initial parent value reaches the child. Later child changes update `text`,
and later parent changes update the child. A `param bind` may also be used as a
one-way input with `Text={text}` or as an output-only connection with
`out:Text={text}`.

## Output parameters

Use `param out` for values owned by the child and published to a writable target
in the parent:

```akbura
// EditorField.akbura
using Avalonia.Controls;

namespace Demo;

param bind string Text = "";
param out string Submitted = "";

<StackPanel>
    <TextBox bind:Text={Text} />
    <Button Click={() => { Submitted = Text; }}>Submit</Button>
</StackPanel>
```

```akbura
// ParentView.akbura
using Avalonia.Controls;

namespace Demo;

state string text = "Initial";
state string submitted = "";

<StackPanel>
    <EditorField bind:Text={text} out:Submitted={submitted} />
    <TextBlock Text={submitted} />
</StackPanel>
```

The output connection starts observing when it is attached and publishes later
child changes. It does not replace the parent's current target value merely by
being attached. Changing `submitted` in the parent also does not write that
value back to `EditorField.Submitted`.

## Directional attributes on controls

The same prefixes apply to ordinary markup properties:

```akbura
<TextBox Text={initialText} />
<TextBox bind:Text={editableText} />
<TextBox out:Text={observedText} />
```

- Normal assignment requires a writable property.
- `bind:` requires a readable and writable property plus the existing supported
  observation contract.
- `out:` requires a readable property plus the existing supported observation
  contract.

A public getter alone does not invent change notifications. If the compiler
cannot observe the property, it reports the existing binding diagnostic instead
of generating a connection that only appears reactive.

## Multiple output receivers

Output readers do not compete with the one setter of a property. These forms are
valid:

```akbura
<TextBox Text={initialText} out:Text={firstObserver} />
<TextBox bind:Text={editableText} out:Text={auditText} />
<TextBox out:Text={firstObserver} out:Text={secondObserver} />
```

Two normal setters, or a normal setter combined with `bind:` for the same
property, conflict:

```akbura
<TextBox Text={first} Text={second} />
<TextBox Text={first} bind:Text={second} />
```

Completion follows the same rules: it keeps valid `out:` candidates available,
does not suggest members lacking the required accessors, and inserts a new
directional target as `bind:Text={}` or `out:Text={}` with the caret inside the
braces.
