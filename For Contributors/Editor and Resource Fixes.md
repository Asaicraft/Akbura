# Editor and resource fixes

Implementation baseline: `0326c9cc05f7c0d773279ebbbd85bc17ae41847e`.

## Native brace completion

`AkburaSyntacticDocument.NativePairing` normalizes a native point on an existing
structural opening brace or immediately after it. It then calls the same
`ShouldAutoCloseCurlyBrace` token/owner check as Workspaces. Literal/comment
braces are not structural. The Visual Studio provider uses the normalized point
for the session and does not run a second simulated insertion for that case.
Before-insertion points still use `GetAutomaticPairDecision`. The original
40 ms syntax budget is unchanged; a budget timeout is logged, not silently
converted into an unconditional brace pair.

The session rechecks an adjacent closer before insertion and refuses to publish
tracking points after an editor-cancelled insertion. Native Tab, overtype,
Backspace and Undo must also be exercised in an experimental VS instance.

## Tag delimiter classifications

`<`, `>`, `</`, `/>` are `Punctuation` only when owned by a markup start/end tag.
The same characters inside embedded C# comparisons remain `Operator`. The VS
classification-to-theme map is unchanged; this is not a hard-coded color.

## Resource hooks

```akbura
using Akbura.Hooks;
using Avalonia.Controls;
using Avalonia.Media;

state IBrush? activeColor =
    useDynamicResource<IBrush?>("--color-teal-300", Brushes.Transparent);
state IBrush? inactiveColor =
    useStaticResource<IBrush?>("--color-gray-500", Brushes.Transparent);

<StackPanel>
    <TextBlock Text="Active" Foreground={activeColor}/>
    <TextBlock Text="Inactive" Foreground={inactiveColor}/>
</StackPanel>
```

Both functions return `State<T>` and accept either `(key, fallback)` or
`(IResourceHost host, key, fallback)`. They use normal composable hook slots and
post-commit effects, not name-based compiler special cases. The first render
sees `fallback`; the successful effect publishes the resource value and uses the
normal state invalidation path. Missing, null and incompatible values produce
fallback, with no implicit type conversion.

Static lookup is cached for the same host, key and fallback. It ignores later
resource and theme changes. A first miss on an unattached logical host is retried
once on attachment; a miss on an already attached host stays cached. Reattaching
a previously resolved static host does not silently replace the cached value.
Changing the key, host or fallback creates a new lookup contract.

Dynamic lookup creates `GetResourceObservable(key)` inside the effect and owns
its subscription. It observes Avalonia resource/theme changes, re-subscribes
when the contract changes, and uses cancellation-aware UI dispatch to ignore
stale notifications. Owner detachment cleans up effects; reattachment uses the
existing hook runtime to reestablish them. Do not call hooks conditionally.

## Rename

Rename semantics remain implemented in `Akbura.Workspaces` and are shared
with the language server.

The native Visual Studio Rename adapter is intentionally not implemented yet.
A future Visual Studio integration should use an inline editor experience
rather than a modal dialog.

## Ordinary C# executable scope

The probe builder restores containing executable headers around a target instead
of hoisting nested declarations above them. An `out var` or a pattern variable
is declared by its actual condition. Preceding locals stay inside that lexical
block. A dedicated annotation identifies the target statement after wrapping;
`BindStatement` must not bind the last outer `if` as if it were the original
assignment. Existing component-method binding and markup-flow wrapping remain.

This is a correction to the existing split header/body representation, not a
new grammar for unsupported C# constructs. The helper handles the represented
if/while/for/foreach/using/lock/fixed/checked/unsafe body owners; it does not claim
a general Roslyn control-flow reconstruction for arbitrary new syntax.

`TryFindResource` still returns `object?`. Correct usage includes a type check:

```akbura
if (activeColor == null)
{
    if (this.TryFindResource("--color-teal-300", this.ActualThemeVariant, out var found))
    {
        activeColor = found as IBrush;
    }
}
```

## Verification

```powershell
pwsh -NoProfile -File ./eng/Verify-AkburaEditorResourceFixes.ps1 -Full -BuildVisualStudio
```

No package versions, Marketplace identities or release tags are changed here.
The patch author did not execute the C# tests or native VS code in the preparation
environment. A successful pure Workspaces test is not native VS coverage.

In a fresh experimental Visual Studio instance check:

1. Type `{` after same-line and multiline `$foreach` headers, nested loops and
   `$if`. Verify one closer, overtype `}`, Backspace, Tab and Undo. Check strings,
   comments and preexisting `{}`. Inspect timeout logs on a cold parse.
2. Verify all four tag delimiters have one classification and C# comparisons
   still use operator classification.
3. Rename a state and a foreach local; references update but unrelated strings
   do not. Escape cancels; invalid/conflicting names do not change buffers;
   `@index` and external definitions are refused.
4. Rename a symbol referenced by an initially closed second `.akbura` file.
   Verify both buffers, preservation of prior unsaved edits, single linked Undo
   and Redo, and Undo after closing/reopening a participating file.
5. Make one target read-only, or edit another open file before its semantic
   publication. Verify the entire operation refuses stale/incomplete edits.
6. Exercise static/dynamic resource fallback, theme changes, resource replacement,
   component detach/reattach and an aborted render. Test both generation modes
   for nested `out var` and a pattern variable; wrong object-to-brush assignment
   must still produce a real type diagnostic.

## API references

- [RenameCommandArgs](https://learn.microsoft.com/dotnet/api/microsoft.visualstudio.text.editor.commanding.commands.renamecommandargs?view=visualstudiosdk-2022)
- [ICommandHandler](https://learn.microsoft.com/dotnet/api/microsoft.visualstudio.commanding.icommandhandler-1?view=visualstudiosdk-2022)
- [Linked Undo](https://learn.microsoft.com/dotnet/api/microsoft.visualstudio.textmanager.interop.ivslinkedundotransactionmanager?view=visualstudiosdk-2022)
- [Avalonia 12.0.4 ResourceNodeExtensions](https://github.com/AvaloniaUI/Avalonia/blob/12.0.4/src/Avalonia.Base/Controls/ResourceNodeExtensions.cs)
