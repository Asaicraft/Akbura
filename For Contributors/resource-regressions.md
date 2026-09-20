# Quoted resources and executable-scope regressions

Baseline: `d32b6479160e70db526fb6f0ba659d69892bdfad`.

## Two different compiler installations

The native editor and an application's build need not load the same compiler.
The `Akbura` package embeds both `Akbura.BlackSilence.dll` and
`Akbura.Generator.dll` in `analyzers/dotnet/cs`. Building or installing the VSIX
alone does not replace those package assets.

The baseline already contains `CSharpProbeBuilder.ExecutableScopes.cs` and calls
`WrapExecutableBlockScope` for ordinary C# blocks. The source at the
`nuget-v12.0.4-alpha.6` tag does not contain that integration. This patch does
not add it a second time or invent a default-valued `found` local.

`ResourceOutVariableRegressionTests` uses two independent `out var found`
declarations, explicit `(IBrush)found` casts, the real Avalonia
`TryFindResource`, both generator modes, and LF/CRLF. It asserts the generated
component actually sets both brushes. A second test runs the production
BlackSilence generator with diagnostic publication enabled, changes its
AdditionalText, and runs it again against the original C# compilation.

## Incremental argument growth

Markup literal tokens (`AkTextLiteral`) are parser-created aggregates. Reading
individual fresh lexer tokens is already required; that does not prevent the
blender from reusing the aggregate's containing argument or value node.

An edit starting at the aggregate's old right edge can extend it. The boundary
check therefore rejects an owner whose last terminal is `AkTextLiteral`, before
returning early for replacement/deletion edits. It does not disable whole-file
incremental parsing. For example, adding the final quote in:

```akbura
state int count = 0;

<NavIcon Geometries=${StaticResource "Icon.Home"} />
```

must not leave that quote outside a reused positional argument. Full parsing
already accepts this example at the baseline. Assignment compatibility of
`Geometries` is a separate semantic check and is not weakened here.

The parser regression tests invoke `Parser` directly, so a full-document
fallback in `ComponentSyntaxTree.WithChangedText` cannot hide an incorrect
incremental tree. Workspaces tests also exercise `AkburaSyntacticDocument.WithText`.

## Diagnostic format strings

`AkburaDiagnostic.Message` formats resource strings with `string.Format`.
Literal braces in `ERR_LbraceExpected` and `ERR_RbraceExpected` must therefore
be doubled in the `.resx`, not in user markup. Missing braces must still produce
diagnostics; tests assert the readable message rather than suppressing it.

## Verification

Run from the repository root:

```powershell
pwsh -NoProfile -File .\eng\Verify-AkburaResourceRegressions.ps1
pwsh -NoProfile -File .\eng\Verify-AkburaResourceRegressions.ps1 -Full -BuildVisualStudio
```

The tests are supplied as source and have not been executed in the environment
that prepared this patch. Treat their first run as verification, not a presumed
pass.

To build a uniquely versioned local NuGet package after the targeted tests pass:

```powershell
pwsh -NoProfile -File .\eng\Verify-AkburaResourceRegressions.ps1 -PackLocal
```

This only writes a local package and a `last-package.json` manifest. It does not
publish, retag releases, clear global caches, or edit an application project.
Do not overwrite an already restored package version with different compiler
DLLs. Install the unique local version in the consumer and rebuild the editor
extension separately when checking both diagnostic publishers.

## Follow-up: actual failing test run

The reported run has nine parser/Workspaces shape mismatches, two brace-message
format failures, one incorrectly scoped diagnostic assertion, and four resource
runtime assertions made before the normal update lifecycle.

The incremental extension parser now validates the terminator after tentative
reuse of a text argument, its value, or its literal node. If new input continues
the text, it restores the blender, lexer position, and previous-token trivia.
A rejected literal uses the existing full parser's literal scanner, which reads
fresh lexical tokens through the incremental token buffer. This reparses one
literal, not the whole document. Other arguments retain their reuse paths.
The previous `AkTextLiteral` boundary invalidation in `Blender.Reader.cs` is
still required and is retained.

`AkburaSyntax.GetDiagnostics()` returns diagnostics attached directly to that
node. A whole-tree assertion must enumerate descendants (including tokens and
structured trivia); the missing closing brace is attached to its token.

`FirstUpdate()` alone is not a completed component update. The resource tests
now initialize through a headless Window and inspect `owner.Child` after the
normal render/commit. The second render is explicitly requested to test the
ordinary `if` guards; no automatic dynamic-resource invalidation is claimed.

The two brace-resource strings must still be escaped in the compiled `.resx`.
The formatting test reports the embedded string, assembly path, and MVID before
attempting formatting, so source/build mismatches are distinguishable.

These follow-up changes have been reviewed statically but have not been built
or executed with .NET in the preparation environment. Do not treat the new
assertions as a recorded passing run.
