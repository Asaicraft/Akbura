# BlackSilence diagnostics

Diagnostics are owned by the shared `Akbura.Diagnostics` namespace in
`Akbura.Generator`, below both BlackSilence and Akbura.Workspaces. BlackSilence
does not reference Workspaces, and Workspaces does not reference BlackSilence.
The existing Furioso generator is unchanged.

## Rollout

`AkburaBlackSilenceDiagnostics` has three values:

- `Off`: no BlackSilence diagnostic collection or semantic publication.
- `Shadow` (default): collect and cache diagnostics without publishing them.
- `Publish`: publish the canonical diagnostic batch through Roslyn, subject to
  the publisher policy below.

An unexpected generator failure is always reported as
`AKBURA_GENERATOR_FAILURE`, including in Off, Shadow, or delegated modes.
Cancellation propagates and does not publish a failure or a partial snapshot.

To opt in on a project already using BlackSilence:

```xml
<PropertyGroup>
    <AkburaBlackSilenceDiagnostics>Publish</AkburaBlackSilenceDiagnostics>
    <AkburaDiagnosticPublisher>Auto</AkburaDiagnosticPublisher>
</PropertyGroup>
```

These properties do not replace Furioso with BlackSilence. Production generator
selection remains a separate rollout decision.

## Publisher ownership

| AkburaDiagnosticPublisher | Real build | Editor generator | Workspace presentation |
| --- | --- | --- | --- |
| Auto (default) | Enabled | Enabled unless host confirms workspace ownership | Enabled |
| Generator | Enabled | Enabled | Disabled |
| Workspace | Enabled | Disabled | Enabled |
| Both | Enabled | Enabled | Enabled |
| None | Disabled | Disabled | Disabled |

The generator column additionally requires `Publish`. `DesignTimeBuild=true`
distinguishes the editor from a real build. In Auto, suppression requires an
explicit `AkburaWorkspaceDiagnosticsActive=true` handshake. A missing handshake
keeps the generator fallback. Workspace services always compute canonical
diagnostics; ownership controls presentation, not their query API.

The Akbura LSP sets both handshake properties only in its isolated
MSBuildWorkspace. It does not modify the user project or global editorconfig.
The Visual Studio adapter does not modify the live project or remove foreign
Error List rows. A host opting into Auto + Publish must supply the handshake
for its editor-only compilation. `Both` intentionally exposes both channels.

Transport properties are not a universal UI deduplication protocol: arbitrary
C# extensions and Visual Studio producers are not guaranteed to consume them.
Do not suppress diagnostics by code and line alone. Without complete canonical
identity and version information, merging a foreign Error List entry is unsafe.
Shadow remains the default until actual host acceptance is complete.

## Incremental and identity contracts

Each additional source file, including global-using files without generated C#,
has a separate `DocumentDiagnosticVersion` and immutable diagnostic entry.
Inline AKCSS is collected through its containing component once. Dependency
surfaces are conservative and include transitive dependencies, declarations,
global usings, C# environment and diagnostic schema. Meaningful source positions
remain part of the version; only proven-safe EOF whitespace is reusable.
When the previous tree contains parser diagnostics or skipped text, an edit
reparses that document in full. This recovery fallback prevents stale lexer
context from misreading markup-extension utility attributes after an EOF comment
is fixed. It does not reparse other documents or disable valid-document EOF reuse.

Source-dirty and diagnostic-dirty documents use the same compilation and its
semantic-model cache. Shared binding is prepared sequentially before parallel
source emission. Sources, diagnostics and safe semantic seeds are published as
one versioned snapshot. Failed, canceled and older work cannot overwrite a newer
successful snapshot. Shadow/Publish and ownership changes affect publication
only, not source generation or diagnostic evaluation.

Canonical records contain materialized messages and locations, never symbols,
syntax trees, semantic models or message-argument objects. Equality checks code,
severity, kind, message, OS-aware path, text/line span, additional locations and
semantic properties. Hashes are bucket selectors, not equality proofs. Only
delivery provenance/version and their derived transport fields are excluded.
Exact duplicates merge provenance; distinct spans or messages remain distinct.

Roslyn descriptors are cached by code and severity, with a generic `{0}` message
format. Per-instance messages and locations remain separate. The output is one
immutable batch, so removed diagnostics disappear on the next publication.
LSP ranges use UTF-16 positions from the current SourceText and carry the LSP
document version. Pull requests recheck their captured document and project
identity after computation and before returning a report. A changed snapshot
produces `ContentModified`, never a fallback to an older cached report. Canceled
pull requests do not return cached results; cancellation observed before storing
a newly computed result leaves the previous complete cache entry intact.

Push delivery is not a transaction with editor state or the external UI. The
server validates the snapshot before storing and sending a complete notification,
but another edit can occur after that check. Notifications carry a document
version so clients can discard obsolete results. The state check, cache update,
and asynchronous transport are not protected by one shared commit lock, and
cancellation after commit cannot recall an already-sent notification. These
boundaries do not guarantee that every client hides an obsolete notification
or merges diagnostics from another producer.

The public Workspace `AkburaDiagnosticSpan` retains its original four-field
equality for existing consumers. That equality is not canonical deduplication:
canonical merging and Visual Studio row matching additionally use the full
canonical comparer, including related locations and semantic properties, while
ignoring delivery provenance and version.

Semantic and global-using diagnostics preserve Furioso's metadata and messages.
Parser diagnostics intentionally extend Furioso, which did not publish them;
their expected locations are independently checked against the prior Workspace
traversal. Imported diagnostics use their actual source owner, not the importer.
Embedded PE sources also carry `akbura.module-identity` (or an observed reference
path fallback), a semantic property which keeps same-named sources in different
assemblies distinct. Such records are not projected onto a local editor document
merely because their paths happen to match.

## Measurement

DebugStats and ReleaseStats expose diagnostic batch, evaluated/reused document,
diagnostic-only semantic-model, canonical record, Roslyn record, descriptor,
deduplication, workspace-collision and publication counters. Inclusive timings
cover collection, semantic evaluation and publication; they are not additive CPU
times. Zero reused counters on a cached Roslyn callback mean the callback was
skipped, not that the cache missed.

Performance acceptance compares Off and Shadow/Publish on the same binary and
input set, with warmed repeated operations for sub-millisecond paths. Keep
allocation/semantic-model counts alongside elapsed time. Do not enable Publish
by default based solely on unit-level metadata parity; host UI acceptance and
the documented performance thresholds are separate checks.

Run the diagnostic scenario matrix with:

```shell
dotnet run --project src/Akbura.Benchmarks -c ReleaseStats -- --black-silence-diagnostic-parity
```

For paired fast-path measurements without another parity run:

```shell
dotnet run --project src/Akbura.Benchmarks -c Release -- --black-silence-diagnostic-parity --measure-only --operations 768
```

Use `--scenario ColdDiagnostics --measure-only --operations 3` for the cold
allocation comparison. The general `--black-silence-verify-parity` command also
compares compiler diagnostics, rather than merely recording them. Its existing
cross-generator differences are not suppressed: generated C# currently has 109
CS8603 warnings in BlackSilence, and CS8019 locations differ between the two
generators. These remain a separate source-emission parity gate; they are not
new canonical Akbura diagnostics. Publish stays opt-in.
