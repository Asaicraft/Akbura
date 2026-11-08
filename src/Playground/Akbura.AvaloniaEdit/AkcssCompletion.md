# AKCSS completion integration

The AvaloniaEdit adapter uses the existing workspace completion and projected C# services.
It does not maintain a utility registry or discover styles independently.

## Confirmed integration defects

1. Automatic requests checked only markup and projected C# contexts. Valid standalone and
   inline AKCSS contexts were rejected before calling the native completion service.
   The adapter now also checks the public `GetAkcssCompletionContext` API. The region helper
   used by Visual Studio is internal to Workspaces; no visibility changes are required.
2. `TextEntering` accepted every non-identifier character. With an open popup, typing a
   utility hyphen could insert an unrelated property or utility. Native commit characters
   now follow the existing Visual Studio item-kind policy, including quoted values and
   module imports. Projected C# punctuation handling is retained.
3. The service correctly returned parameterized utilities after an argument was typed,
   but AvaloniaEdit filtered them out again: `square-48` does not match the provider's
   `FilterText` of `square-`. For this case, the popup retains the service's candidates and
   selects the matching utility prefix. Display, filter, insertion and resolve fields are
   unchanged; canonical insertion still preserves the typed argument.
4. Native results were deduplicated by `ResolveKey`. The shared service can return same-name
   `@apply` declarations from different imported modules with the same key but different
   source suffixes and priorities. The adapter now preserves all native results, as the
   Visual Studio adapter does, instead of dropping a valid source distinction.

The focused tests were run before these fixes: automatic AKCSS requests and hyphen input
failed, and a separate assertion reproduced a null popup selection after `square-48`.

## Host configuration

The existing real reference bundle already exposes the `Akbura.Styles.akcss` module.
Making its assembly available does not import the module into a document:

```akbura
using Avalonia.Controls;
using Akbura.Styles.akcss;

<Button p-3 />
```

The host registers in-memory stylesheets using `OpenOrChangeDocument` or
`OpenOrChangeDocumentContext`. BrowserSmoke uses the consistent companion pair
`memory:///Views/Smoke.akbura` and `memory:///Views/Smoke.akcss`, with one document identity
for the stylesheet and a second editor bound to that same document. Updates use normal
workspace notifications. No C# source API, filesystem discovery or automatic module imports
are involved. A renamed non-companion stylesheet requires the corresponding explicit import.

## Existing style-name limitation

The following minimal example still produces no `primary` completion from the shared
native completion service at the marked position:

```akcss
@using Avalonia.Controls;
Button.primary { Padding: 12; }
```

```akbura
using Avalonia.Controls;
<Button class="pri|" />
```

The stylesheet can be registered as the markup document's companion. The adapter exposes
the same result. `AkburaCompletionService.GetAttributeValueItems` handles property literals,
expected values and colors; it does not enumerate AKCSS class names. The inspected Visual
Studio and language-server completion handlers call that same native service, with projected
C# supplementation where applicable. No additional class-value provider was found in those
paths. This is a source-level comparison; the extensions themselves were not executed.
Lowercase `class` is not reinterpreted as Avalonia's uppercase `Classes`.

Existing style suggestions work in AKCSS `@apply` contexts. The integration tests and
BrowserSmoke verify discovery, canonical acceptance and undo for `@apply primary`.
Adding class-value completion requires a separate shared-service change, outside this task.

## Existing property-delimiter limitation

Accepting `Width` before an existing colon has a separate minimal reproducer:

```akcss
@using Avalonia.Controls;
Button.card {
    W|: 12;
}
```

The raw `AkburaCompletionService.GetCompletionChange` produces `Width: : 12;`.
`AkcssCompletionService.GetPropertyItems` supplies `Width: ` as insertion text, while the
applicable span covers only `W`. `ExistingColonReproducerMatchesTheCanonicalSharedEdit`
verifies this directly against the synchronized workspace snapshot, then checks that the
adapter applies exactly that change, preserves its caret position and supports undo.
This records a current limitation; it is not the desired editing behavior.

The language-server handler maps that same canonical change. The inspected Visual Studio
native commit path uses the item's insertion text and applicable span directly; no AKCSS
colon handling was found there. These are source comparisons, not extension runtime tests.
The duplicate colon was also observed during manual input in the published browser host.
No adapter-side parsing or replacement of canonical edits was introduced to hide it.

## Verification

`AkcssCompletionIntegrationTests` uses real resolved compiler assets and actual headless
window text-input events. It compares native workspace items with adapted session items and
checks open popup selection, canonical edits, LF/CRLF and UTF-16 offsets, imports, signatures,
ordinary comments/strings, embedded C#, stylesheet lifecycle, cancellation-resistant requests,
typed characters during resolution, buffer replacement, disposal and retained highlighting.

BrowserSmoke checks imported built-ins, a companion utility, character-by-character input
including hyphens and arguments, style suggestions through `@apply`, standalone and inline
AKCSS, commit/undo, stale edits and stylesheet updates without rebinding. Its two editors
remain usable after the checks. Build/publish results and actual browser execution must be
reported separately.

Validation for this change: all 109 AvaloniaEdit Release tests passed. BrowserSmoke Release
build and publish passed with warnings treated as errors. The published WebAssembly app was
executed in a browser and displayed PASS for the extended checks; manual input additionally
verified continued utility arguments, standalone property suggestions and undo. Visual Studio
and VS Code were not executed. Workspaces and compiler sources remain unchanged.
