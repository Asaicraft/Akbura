---
title: Editor Completion
summary: Type-aware completion for x.DataType, binding paths, and Avalonia resource keys.
---

Akbura's Visual Studio and VS Code extensions share the same project-aware
completion model. The model uses the current C# compilation, `.akbura` syntax,
and the Avalonia resource files included in the project.

## `x.DataType`

Inside an `x.DataType` value, completion suggests C# types rather than ordinary
string values. The editor understands the component's namespace, `using` and
`global using` directives, aliases, qualified names, and the type forms accepted
by the Akbura compiler. The completed value is also classified as a type.

```akbura
<ItemsControl.ItemTemplate x.DataType="MyApp.Models.TaskItem">
    <TextBlock Text=${CompiledBinding Title} />
</ItemsControl.ItemTemplate>
```

An explicit `x.DataType` supplies the receiver type for bindings inside the
template. Where Akbura can infer a template item type, binding completion uses
that inferred type instead.

## Binding paths

Path completion is available in `Binding`, `CompiledBinding`, and
`ReflectionBinding`, for both the first positional argument and `Path=`:

```akbura
<TextBlock Text=${Binding Customer.DisplayName} />
<TextBlock Text=${CompiledBinding Path=Customer.DisplayName} />
```

Suggestions follow the type at the cursor, including inherited public
properties and the element type after supported indexers. Akbura also uses its
existing binding semantics for contexts such as `$self`, `$parent`, named
elements, casts, and template item scopes. Completion replaces only the path
segment being edited, so completing in the middle of a path does not duplicate
its prefix or suffix.

## Resource keys

`StaticResource` and `DynamicResource` offer the same set of resource keys:

```akbura
<Border Background=${StaticResource CardBackgroundBrush} />
<Border BorderBrush=${DynamicResource AccentBrush} />
```

The editor combines resource declarations that are provably available from:

- the current `.akbura` resource scopes;
- local `.axaml` files and their literal resource dictionary imports;
- explicitly exported dictionaries in referenced assemblies; and
- a small catalog of known Avalonia built-in resource hints.

Keys remain case-sensitive and are inserted exactly as declared. Quoted keys,
spaces, dots, and hyphens are preserved. Local `.axaml` edits are read from the
open editor buffer, so adding or removing a resource or import can update
completion before the file is saved or the project is rebuilt.

Only `.axaml` files included by the evaluated project are indexed. A file that
merely exists beside the project, or under `bin` or `obj`, is not treated as an
application resource.

## Exporting library resources

A compiled library has no portable public API for enumerating its embedded
Avalonia resources. A library can opt in to Akbura completion by adding assembly
attributes for the keys it intentionally publishes:

```csharp
using Akbura.CompilerAnotations;
using Avalonia.Media;

[assembly: ExportResourceForAkburaCompletion(
    "Styles.axaml",
    "CardBackgroundBrush",
    typeof(SolidColorBrush))]

[assembly: ExportResourceForAkburaCompletion(
    "Styles.axaml",
    "AccentColor",
    typeof(Color))]

[assembly: ExportResourceForAkburaCompletion(
    "Themes/Compact.axaml",
    "CompactSpacing",
    typeof(double))]
```

`dictionaryPath` is relative to the resource root of the assembly carrying the
attribute. It is an assembly resource path, not a path relative to the solution,
consumer project, or source file. Use the exact published dictionary path, such
as `Styles.axaml` or `Themes/Compact.axaml`.

`resourceType` describes the actual resource value, or a correct public contract
for it. For example, export a `SolidColorBrush` resource as
`typeof(SolidColorBrush)` or `typeof(IBrush)`, not as the type of a property that
might consume the brush. Do not infer a type from the spelling of the key.

Apply the attribute once for every exported key. A library may export multiple
keys from one dictionary and separate sets from multiple dictionaries.

### Imports activate exact exports

Export metadata does not make every referenced library key globally visible. A
consumer activates only the assembly and dictionary path that it imports:

```xml
<ResourceInclude Source="avares://Acme.Theme/Styles.axaml" />
```

This import activates the `Styles.axaml` exports on `Acme.Theme`. It does not
activate `Themes/Compact.axaml`, nor a `Styles.axaml` exported by another
assembly. Removing the last reachable import removes those suggestions.

If a package exposes a wrapper dictionary that imports dictionaries from other
assemblies, the wrapper assembly must re-export the public keys under the
wrapper's own entry-point path. Akbura does not guess transitive exports hidden
behind a compiled wrapper:

```csharp
[assembly: ExportResourceForAkburaCompletion(
    "ThemePack.axaml",
    "CardBackgroundBrush",
    typeof(SolidColorBrush))]
```

The attribute must be present in the assembly used by the consumer's C#
compilation. NuGet packages that ship a compile/reference assembly under `ref/`
must preserve these assembly attributes there; putting them only in the runtime
assembly is not enough. Akbura reads symbols from the existing Roslyn
compilation and does not load the runtime assembly or inspect Avalonia's private
resource bundle format.

The attribute has no runtime behavior. It does not load a dictionary, create a
resource, validate that a key exists, or change Avalonia lookup. Consumers must
still import the real dictionary at runtime.

## Known limitations

- Built-in entries are editor hints, not proof that a key is present in the
  selected Avalonia theme at runtime.
- Only completed literal resource imports are followed. Bindings, markup
  extensions, values assembled in code, and other computed `Source` values are
  not executed by the editor.
- When editing a shared library, Akbura does not guess which consuming
  application's `App` should supply global resources.
- Complex selector-dependent scopes and custom resource containers are analyzed
  conservatively. A runtime-valid key may therefore be absent from completion.
- Conversely, normal runtime lookup rules still decide whether a suggested key
  is available at the point where the application executes.

Completion is assistance rather than a closed-world resource validator. An
unknown key may still be supplied at runtime by code, a host application, a
computed import, or another mechanism the editor intentionally does not run.
