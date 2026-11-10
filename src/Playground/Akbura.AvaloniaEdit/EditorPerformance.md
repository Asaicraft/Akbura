# Editor presentation and request architecture

## Reference and boundaries

Reference: [RoslynPad 21.1](https://github.com/roslynpad/roslynpad/tree/21.1).
The reviewed files were `RoslynSemanticHighlighter`, `RoslynHighlightingColorizer`,
`AvalonEditTextContainer`, `CodeTextEditor`, `RoslynCodeEditor`,
`RoslynCodeEditorCompletionProvider`, `RoslynCompletionData`, and `DiagnosticsUpdater`.

The useful principles are asynchronous preparation with provisional presentation,
separate diagnostic updates, stable completion windows, and deferred descriptions.
This is not a port of its per-line classification requests, Reactive Extensions,
uncanceled asynchronous callbacks, stale completion-offset adjustment, or warm-up.
No RoslynPad dependency was introduced. Workspaces and compiler algorithms are unchanged.
The later collection refactoring adds only three explicitly authorized InternalsVisibleTo
attributes in Akbura.Generator/AssemblyInfo.cs so the editor, its tests and smoke host
can reuse the existing internal builders directly.

## Presentation

`WorkspaceColorizer` stores classified segments in AvaloniaEdit's augmented
`TextSegmentCollection`. Line lookup is interval-aware, including spans that start
before the requested line. Returned intersections retain provider precedence.
Rendering visits intersections rather than the whole document and never calls a
language service. A reusable ArrayElement<IImmutableBrush?> array is cleared before
each line-rendering pass. Polymorphic references use ArrayElement to avoid covariant
array stores. Rendering consumes a pooled segment builder directly, without creating
an intermediate classification array for each line.
Resource, theme and foreground changes schedule a coalesced full redraw.

The segment tree is disconnected from document notifications. `TrackChange` alone
updates it, once per change. Only intersected segments receive custom provisional
fragment/boundary handling; the tree shifts unaffected offsets. TextDocument already
invalidates edited visual lines, so this path does not request a full-view redraw.
Fresh publications compare presentation and invalidate the changed range. Full
authoritative arrays remain available through `Classifications`; the internal
inspection snapshot is materialized lazily, not during rendering or edit tracking.

Retained segments are provisional. Current semantic results replace them, including
valid empty results. Cancellation and service failure do not publish an empty result.
Buffer replacement and disposal release the old presentation.

## Syntax and services

`EditorSyntaxCache` follows the local GreenNodeCache pattern: a fixed direct-mapped
array of immutable entries, hash/mask lookup and a complete key check. It has two
slots because full document syntax retains much more memory than a green node.
Collisions replace an entry and cause a cache miss, never a result for another key.
Keys use buffer and SourceText reference identity, revision and ordinal file path.
The most recently accepted entry is also the incremental parsing base.

All cache reads, publication, cancellation and clearing belong to the editor UI
thread. DispatcherVerifyAccessDebug asserts this in Debug through Conditional("DEBUG");
the compiler omits its calls in Release. The cache needs no lock. Worker
construction returns immutable syntax to an explicit UI-thread completion callback;
it does not rely on an ambient SynchronizationContext at request time. Only the host
scheduler controls where parsing executes. A shared completion task is installed
before invoking a possibly synchronous or reentrant scheduler.

Same-revision features share accepted syntax or one pending computation. New text
uses public WithText, with change ranges derived from the accepted syntax's text,
including coalesced edits. Project-only changes preserve syntax but invalidate
semantic results. Feature cancellation ends its wait, without canceling shared work.
Supersession cancels owned work; buffer replacement and disposal clear all entries.
Only a still-owned request for a current snapshot may add an entry. Late results
cannot overwrite a newer entry, even if the scheduler ignores cancellation. Failed
results are not cached; their task exceptions are observed even if no feature waits.

`EditorServiceQueue` serializes service execution and keeps the latest pending waiter
per stream, with interactive work ahead of pending classification and diagnostics.
Its lease covers execution rather than host-scheduler result delivery. This also
permits deterministic cancellation-resistant scheduler tests without deadlocks.
Workspace synchronization runs under the same queue lease, so the redundant
per-session SemaphoreSlim was removed. The queue retains synchronization for its
pending list, which is accessed by scheduler threads and cancellation callbacks.
Automatic completion is posted once per dispatcher turn and coalesces burst input.
Explicit requests supersede that pending callback. No service is run in `BufferChanged`.

Classifications publish before diagnostics, with a dispatcher opportunity between
stages. Initial `Ready`, explicit `RefreshAsync` and `AnalysisUpdated` still cover the
complete refresh. Concurrent equivalent refreshes share pending work. Diagnostics
use the canonical logical comparer, including locations, severity, message and
properties; unchanged effective diagnostics do not rebuild markers or raise events.
Edits invalidate diagnostic offsets immediately.

Classification remains a complete-document request. Rendering optimization does not
justify multiplying expensive semantic preparation into independent line requests,
and viewport-only results would change the public complete-result contract.

## Completion

Temporary variable-length results use the existing Akbura.Pools ImmutableArrayBuilder
and ArrayBuilder, with using or finally/Free ownership. The completed immutable arrays
own their storage and remain valid after pool reuse. Completion sorting includes the
original provider index as a tie-breaker so replacing stable LINQ OrderBy does not
reorder candidates with equal SortText. Single-match and minimum searches use direct
loops; existing collection helpers handle scalar predicates and projections.

Compatible prefix editing retains the completion and description windows. Fresh
provider results update data in place; a uniquely matched selected candidate and its
scroll position are retained where still applicable. Context keys and applicable
starts are checked using the shared context APIs, including AKCSS and embedded C#.
The provider is still consulted for changed snapshots: completeness alone is not a
promise that insertion text or resolve keys remain valid after more typing.

Retaining a row never legitimizes its previous offsets. Acceptance after additional
typing refreshes and uniquely matches provider fields, then uses that current item's
canonical change and identity. Ambiguous matching is rejected. A typed delimiter
against a stale row remains normal input. Existing canonical edit validation,
additional edits, caret placement, follow-up completion and undo grouping remain.
Escape invalidates posted and running requests so late results cannot reopen UI.

Descriptions are cached lazily per item and snapshot, including in-flight work, with
a 64-item bound. Selection cancellation stops only that selection's wait. A snapshot
change or session end clears ownership and references. Sequence and selection checks
reject late descriptions. No eager provider warm-up was added.

## Measurement protocol

Measurements are now compiled **only when DEBUG is defined**. The SDK's normal
Debug configuration and this repository's DebugStats configuration define that
symbol; Release and ReleaseStats do not. This follows Visual Studio's documented
[conditional debugging model](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.debug?view=net-10.0).
The compiler removes the measurement types, options, timer scopes, counters and
test gates from Release, including their call sites and arguments. There is no
runtime debugger-attached check or disabled timer in the Release paths.

The browser panel has separate `Debugging/SmokePerformanceView.akbura` layout. Its
markup and code-behind are excluded from Release compilation and embedded module
sources using DefaultItemExcludes. The normal editor smoke test runs in both builds.
Only Debug loads the profiling panel after the smoke test succeeds.

`EditorPerformance` is a Debug-only, opt-in internal hook, absent from ordinary host options.
It aggregates counters and bounded duration samples; it never logs per token.
The shared `EditorPerformanceScenario` runs in both the desktop headless test and
published browser host. Base documents have approximately 100, 1,000 and 5,000 lines,
markup, inline AKCSS, a state declaration, Unicode and C# expressions every 20 lines.
The long-line cases contain 400, 4,000 and 20,000 characters.

The workload separates cold initialization, warmed token input, insertion at
the beginning, scrolling, multiline paste/undo/redo, completion input, C# project
updates, companion stylesheet updates and a long line. Ordinary presentation phases
disable automatic provider calls and defer analysis to isolate edit/render costs;
provider latency and explicit full refresh are measured in separate phases. This is
a benchmark configuration, not a longer production debounce. Regression and smoke
tests exercise actual automatic input separately.

There are 20 samples per ordinary phase. C# and stylesheet refreshes use five samples
at 100/1,000 lines and one sample at 5,000 lines; 5,000-line completion uses five.
The report uses median/range for five samples and a single duration for one sample,
without presenting their maximum as a stable p95. Other p95 values use nearest rank;
the median averages the two central samples when the sample count is even.
Cold binding is one sample per size, measured from Bind through Ready and rendering.
Host metadata preparation precedes it. Later sizes share the already running process;
browser binding follows the smoke test. These are not cold application startup times.

Desktop render measurements use an actual headless rendered-frame capture at
1280 x 900. Browser measurements use two TopLevel animation callbacks so the previous
frame can consume updated visual lines. This is an input-to-render opportunity,
not a physical display/GPU scan-out measurement. Completion input-to-frame and
provider completion latency are recorded separately. The browser is single-threaded
WASM without AOT/trimming, using the inline scheduler; yielding does not make an
indivisible synchronous language-service call interruptible or move it to a worker.

Current opt-in desktop command (Debug only):

```powershell
$env:AKBURA_PERFORMANCE_OUTPUT = "$PWD/artifacts/editor-performance/desktop-debug.json"
dotnet test src/Playground/Akbura.AvaloniaEdit.UnitTests/Akbura.AvaloniaEdit.UnitTests.csproj -c Debug --filter FullyQualifiedName~OptInDebugWorkload
```

In Debug, the browser's size selector and **Measure performance** button expose the same raw JSON, also written
to the browser console with the `AKBURA_PERFORMANCE` prefix. Build/publish success
must be distinguished from executing this button in a real browser.

## Debug-only verification

Verified on 2026-11-09 after restricting instrumentation to DEBUG:

- Debug tests: **134 passed**. Release tests: **124 passed**, including the subsequent
  syntax-cache refactoring. The ten Debug-only
  cases use measurement counters, explicit measurement gates or the profiling fixture.
  Functional completion, canonical edits, undo, lifecycle, diagnostic and appearance
  tests continue to execute in Release.
- `BuildConfigurationTests.MeasurementsAreCompiledOnlyWithDebug` checks compiled
  measurement types and properties in both builds. It also verifies that Release
  has no Stopwatch type reference.
- Editor and BrowserSmoke builds passed with warnings as errors in both Debug and
  Release using `-p:BuildProjectReferences=false` after dependencies were built.
  Full dependency-graph builds with warnings as errors report an existing CS8602 in
  `src/Akbura.Generator/Language/Blender.Reader.cs:393`; that read-only source was not
  changed. Both test suites still executed and passed in those initial commands.
- Metadata inspection of the actual DLLs found EditorPerformance and Measurement
  only in Debug editor output, and EditorPerformanceScenario and SmokePerformanceView
  only in Debug BrowserSmoke output. The debug layout resource is absent in Release.
- Evaluated MSBuild source items include the debug markup/code-behind in Debug and
  DebugStats, and exclude both in Release and ReleaseStats.
- The syntax-cache refactoring removes all six cache locks and the redundant
  workspace synchronization semaphore. Six added cases run in both configurations:
  colliding entries rebuild correct syntax, cached reads preserve current pending
  requests or cancel superseded ones, cancellation-resistant old revisions cannot
  publish, an inline scheduler can reenter a shared request, and worker completion
  publishes on the UI without an ambient SynchronizationContext. Existing shared
  cancellation, disposal, incremental parsing and complete editor regressions pass.
- This configuration change was verified by builds, compiled metadata and tests.
  No new browser execution or latency measurement is claimed for this change.

The Debug-only panel is defined in `Debugging/SmokePerformanceView.akbura` and its
code-behind, attached by `SmokeView.Performance.cs` after the smoke succeeds.
The opt-in test is now named `OptInDebugWorkload`. Historical Release measurements
below retain their original configuration label.

## Background Debug rerun

The full editor project audit also replaced the remaining LINQ pipelines in
CompletionEdits and AkburaProjectExtensions. Canonical edits retain start/length
ordering and overlap validation. Desktop reference paths preserve first-seen ordinal
uniqueness; polymorphic Assembly slots use ArrayElement. Mutable collections that
belong to a long-lived session remain ordinary owned collections rather than rented
builders held for the application's lifetime.

The opt-in native Debug benchmark now appends phase progress to the result path plus
`.progress.log`, while retaining the existing last-phase `.progress` file. Results
are written only after all sizes finish. The active background run directory is in
`artifacts/editor-performance/active-debug-run.txt`; its progress log, runner.log,
status.txt and results.json can be inspected independently of this chat. Its wrapper
appends COMPLETE or FAILED when the process ends.

### Completed Debug run, 2026-11-09

Run directory: `artifacts/editor-performance/debug-background-af12cd4b/`.
The detached test completed successfully: one opt-in benchmark passed, all three
sizes completed, and the wrapper recorded COMPLETE. The result was saved at elapsed
00:01:50.95. Runtime: Windows headless .NET 10.0.12, Debug, inline scheduler,
1280 × 900. No browser measurement was executed in this rerun.

This is a current-build characterization, not a measured speedup from the latest
collection changes: there is no matching Debug baseline. Historical Release timings
below cannot serve as its before-run. Sizes ran sequentially in one process, so
JIT/tiering and warmed services also prevent interpreting the smaller warmed input
timings at 5,000 lines as an advantage from document size.

All durations below are milliseconds. Twenty-sample cells use median / nearest-rank
p95; five-sample cells use median [minimum–maximum]; one-sample cells show the single
observation. Full refresh at 5,000 lines has only one observation per update type.

| Lines | Presentation workload (20 samples) | Input to rendered frame, ms |
| --- | --- | --- |
| 100 | token-input | 4.03 / 8.29 |
| 100 | beginning-edit | 3.88 / 4.20 |
| 100 | scroll | 14.44 / 24.55 |
| 100 | paste-undo-redo | 5.50 / 7.16 |
| 100 | long-line | 0.86 / 3.00 |
| 1000 | token-input | 0.84 / 2.08 |
| 1000 | beginning-edit | 0.77 / 1.66 |
| 1000 | scroll | 2.56 / 5.94 |
| 1000 | paste-undo-redo | 1.25 / 3.12 |
| 1000 | long-line | 0.91 / 1.50 |
| 5000 | token-input | 0.70 / 1.74 |
| 5000 | beginning-edit | 0.73 / 0.84 |
| 5000 | scroll | 2.43 / 5.94 |
| 5000 | paste-undo-redo | 1.19 / 2.90 |
| 5000 | long-line | 4.15 / 4.76 |

Token-input remapped 20 intersected spans and visited 120 render spans for 20 edits
at every document size. It requested no full redraw and performed no full
classification during that phase. The TextChanged handler median / p95 was
0.03 / 0.08 ms at 100 lines; 0.04 / 0.09 ms at 1000 lines; 0.01 / 0.02 ms at 5000 lines.
The 5,000-line token-input maximum was 11.14 ms, above its 1.74 ms p95.

| Lines | Samples | Completion input to frame, ms | Fresh completion latency, ms | Adapter adaptation, ms |
| --- | --- | --- | --- | --- |
| 100 | 20 | 5.48 / 8.97 | 26.55 / 33.70 | 4.25 / 7.82 |
| 1000 | 20 | 1.51 / 5.79 | 43.18 / 48.38 | 2.07 / 3.37 |
| 5000 | 5 | 1.86 [1.31–34.93] | 177.17 [161.82–204.11] | 1.69 [1.29–3.35] |

The completion window was reused for all 20 / 20 / 5 refreshes respectively; no
popup disposal was counted within the completion phase. At 5,000 lines, median
workspace synchronization was 48.69 ms, syntax 60.54 ms and provider work 57.05 ms,
compared with 1.69 ms in adaptation. These independently aggregated medians are not
an exact additive decomposition of the median end-to-end latency. Input-to-frame
maximums were 107.75 / 74.35 / 34.93 ms for 100 / 1,000 / 5,000 lines respectively;
retaining the popup does not eliminate every input stall.

| Lines | Full refresh | Samples | Total, ms | Classification, ms | Diagnostics, ms |
| --- | --- | --- | --- | --- | --- |
| 100 | context-refresh | 5 | 152.51 [143.73–191.11] | 147.11 [132.85–149.85] | 4.92 [4.53–10.78] |
| 100 | stylesheet-refresh | 5 | 135.31 [133.64–141.22] | 128.29 [122.01–135.79] | 4.63 [4.42–10.66] |
| 1000 | context-refresh | 5 | 1273.03 [1180.59–1577.43] | 994.58 [917.26–1292.77] | 254.06 [248.69–277.64] |
| 1000 | stylesheet-refresh | 5 | 1183.80 [1128.70–1235.72] | 926.80 [879.70–986.15] | 256.09 [248.17–268.17] |
| 5000 | context-refresh | 1 | 20764.04 | 14576.61 | 6158.52 |
| 5000 | stylesheet-refresh | 1 | 21133.99 | 14857.42 | 6274.77 |

Syntax was reused for every C# and stylesheet update (one cache hit per refresh).
The 5,000-line observations still spend 14.58–14.86 seconds in the complete-document
classification service and 6.16–6.27 seconds in diagnostics. These timers surround
the existing workspace service calls; they do not establish which compiler
sub-operation is responsible. Those read-only service algorithms were not changed.

| Lines | Bind through Ready and rendered frame, ms (one observation) |
| --- | --- |
| 100 | 1765.68 |
| 1000 | 2459.04 |
| 5000 | 21774.09 |

The remaining large-document cost is full semantic refresh and initial binding.
The sub-millisecond warmed input figures isolate presentation with automatic
completion disabled and analysis deferred. They are not end-to-end typing latency
with automatic semantic analysis enabled. The inline scheduler used here executes
service work on the headless UI thread; the normal desktop default uses the
thread-pool scheduler, while single-threaded WASM requires its own runtime evidence.
No Release or WASM latency claim follows from this Debug run.

## Historical Release profiling results

The following data was collected during the original optimization pass, before
profiling was restricted to Debug. It is retained as historical evidence. The
current Release build contains no measurement code or profiling UI. New Debug
measurements are labelled Debug and must not be compared directly with these
Release timings as a before/after pair.

Executed on Windows on 2026-11-09, against baseline commit `59ef1b74` and the
uncommitted adapter changes. Both native runs report **.NET 10.0.12**, Release,
inline scheduler, and the same 1280 × 900 headless host. Native measurements use an
inline scheduler to expose adapter/service cost consistently; the normal desktop
default remains the thread-pool scheduler. The browser pair ran in the actual in-app
browser at a fixed **1280 × 720**, net10.0-browser, inline scheduler, without AOT,
trimming or a worker runtime. The same shared scenario and metadata preparation were
used on both sides of each pair. Browser and native timings are separate.

Only the final valid fixture is included: it uses `Text={Count.ToString()}`, verified
to have no diagnostics before measurement. Earlier invalid expression experiments,
an interrupted test-host launch and an earlier browser run with a different viewport
were excluded. Raw successful runs are in `artifacts/editor-performance/`:
`desktop-before.json`, `desktop-after.json`, `browser-before.json`, and
`browser-after.json`. The baseline used opt-in counters and the shared workload on
the original adapter; those temporary source substitutions were restored before the
final tests and publication.

These are single before/after runs on a working development machine, without process
or hardware isolation. Timing improvements are observations for this workload, not
CI limits or universal speedup guarantees. Work-count assertions are deterministic.
Values below are milliseconds. For 20 samples, cells show **median / p95**; five
samples show **median [minimum–maximum]**; one sample shows its duration only.

### Native presentation

| Lines | Workload (20 samples) | Before | After |
| --- | --- | --- | --- |
| 100 | token-input | 25.80 / 30.82 | 5.06 / 18.28 |
| 100 | beginning-edit | 20.96 / 24.26 | 4.68 / 5.56 |
| 100 | scroll | 19.39 / 21.88 | 16.10 / 20.10 |
| 100 | paste-undo-redo | 17.88 / 22.62 | 6.10 / 15.13 |
| 100 | long-line | 2.16 / 4.39 | 2.17 / 4.36 |
| 1000 | token-input | 11.34 / 12.78 | 4.98 / 6.10 |
| 1000 | beginning-edit | 11.15 / 12.87 | 4.36 / 6.12 |
| 1000 | scroll | 9.25 / 11.83 | 13.68 / 30.74 |
| 1000 | paste-undo-redo | 10.88 / 12.71 | 5.77 / 8.25 |
| 1000 | long-line | 1.03 / 1.46 | 1.01 / 1.65 |
| 5000 | token-input | 43.04 / 44.66 | 0.72 / 2.06 |
| 5000 | beginning-edit | 42.75 / 43.68 | 0.73 / 1.64 |
| 5000 | scroll | 38.83 / 44.44 | 2.50 / 5.67 |
| 5000 | paste-undo-redo | 46.89 / 48.18 | 1.15 / 3.31 |
| 5000 | long-line | 4.33 / 10.44 | 4.64 / 6.05 |

The long-line rows use 400 / 4,000 / 20,000 characters respectively. Paste phases
perform multiline insertion, undo and redo in each sample. Token and beginning
phases defer semantic work to isolate presentation; production AnalysisDelay is
unchanged. The opt-in benchmark configuration is recorded in every JSON file.

| Lines | Remapped spans | Render span visits | Full redraw requests | Text-change handler, ms |
| --- | --- | --- | --- | --- |
| 100 | 12500 → 20 | 562500 → 120 | 20 → 0 | 0.46 / 0.75 → 0.08 / 0.16 |
| 1000 | 128600 → 20 | 5787000 → 120 | 20 → 0 | 0.14 / 0.31 → 0.07 / 0.11 |
| 5000 | 644600 → 20 | 29007000 → 120 | 20 → 0 | 0.29 / 0.39 → 0.01 / 0.04 |

Counters cover all 20 token edits. Beginning edits remap zero intersected spans in
the new tree, while the old array remapped every span. The measured renderer visits
only intersections. Full fresh publication still has document-sized preparation;
that cost is outside the synchronous input and line-rendering paths.

### Native completion

| Lines | Metric | Samples | Before | After |
| --- | --- | --- | --- | --- |
| 100 | InputHandling | 20 | 24.76 / 35.69 | 5.78 / 10.89 |
| 100 | CompletionInputToRender | 20 | 24.80 / 35.74 | 5.87 / 10.98 |
| 100 | CompletionLatency | 20 | 52.91 / 68.54 | 28.15 / 44.83 |
| 1000 | InputHandling | 20 | 15.93 / 22.84 | 5.40 / 15.93 |
| 1000 | CompletionInputToRender | 20 | 15.96 / 22.88 | 5.47 / 16.06 |
| 1000 | CompletionLatency | 20 | 48.17 / 62.96 | 35.25 / 48.07 |
| 5000 | InputHandling | 5 | 66.81 [62.45–69.07] | 1.88 [1.68–30.07] |
| 5000 | CompletionInputToRender | 5 | 66.84 [62.48–69.09] | 1.90 [1.70–30.10] |
| 5000 | CompletionLatency | 5 | 110.83 [105.03–115.87] | 57.10 [45.62–89.84] |

InputHandling is the entire input API call; headless KeyTextInput also pumps UI jobs,
so it is not identical to the TextChanged handler measurement. CompletionLatency is
from input through render opportunity and the explicit provider result, including UI
adaptation. This provides a comparable end-to-end measure despite stage separation.
The legacy CompletionProvider timer included Parse; the new Syntax timer measures
WithText separately. Their individual provider timer values are not a like-for-like
syntax-free provider speedup.

The completion phase deliberately obtains fresh candidates for each changed snapshot.
Provider call counts stay 20 / 20 / 5. Full parses fall from 20 / 20 / 5 to zero,
replaced by 20 / 20 / 5 WithText calls. Popup creation/disposal during those phases
falls from 20 / 20 / 5 to zero; the already open popup is reused 20 / 20 / 5 times.
Local filtering is not used as a claim that old canonical edits remain valid.

### Native initialization and semantic context changes

| Lines | Workload | Samples | Before | After |
| --- | --- | --- | --- | --- |
| 100 | cold binding | 1 | 1955.27 | 1988.55 |
| 100 | context-refresh | 5 | 269.42 [195.59–285.35] | 180.52 [175.00–222.93] |
| 100 | stylesheet-refresh | 5 | 178.45 [164.83–193.20] | 181.32 [171.67–198.19] |
| 1000 | cold binding | 1 | 2021.75 | 3163.81 |
| 1000 | context-refresh | 5 | 1045.76 [933.40–1185.17] | 909.47 [801.63–1066.42] |
| 1000 | stylesheet-refresh | 5 | 947.69 [860.31–998.41] | 786.76 [774.84–900.84] |
| 5000 | cold binding | 1 | 12707.85 | 13772.94 |
| 5000 | context-refresh | 1 | 11374.13 | 13175.25 |
| 5000 | stylesheet-refresh | 1 | 11532.49 | 12948.39 |

Each 100/1,000-line context phase issues five current classifications and five
diagnostics after the change, versus ten of each before. At 5,000 lines those counts
are one versus two. The requested classification character counts halve accordingly;
requests remain complete-document requests. Syntax is reused for these project-only
changes. In the C# phase the new presentation rebuilds once with a targeted redraw;
subsequent identical colors and all stylesheet-phase identical colors need no
presentation rebuild or redraw.

There is **no measured cold-binding improvement**. The 1,000-line scrolling sample
is slower after the change, and the single 5,000-line C#/stylesheet refresh samples
are also slower. Fewer adapter calls do not imply proportionally faster shared
semantic preparation: the old second request already benefited from workspace
caches. In the 5,000-line C# refresh, the current classification alone took
12193.25 ms and diagnostics 958.44 ms.
Colors now publish between these calls, while Refresh still awaits both. The
compiler and workspace algorithms are unchanged.

### Executed browser results

| 100-line workload / metric | Samples | Before | After |
| --- | --- | --- | --- |
| token-input | 20 | 85.30 / 104.40 | 28.85 / 33.70 |
| beginning-edit | 20 | 69.70 / 76.70 | 25.90 / 30.90 |
| scroll | 20 | 58.35 / 66.00 | 63.05 / 94.80 |
| paste-undo-redo | 20 | 50.05 / 76.20 | 28.20 / 34.40 |
| long-line | 20 | 16.50 / 24.60 | 14.65 / 17.80 |
| InputHandling | 20 | 5.65 / 8.00 | 3.65 / 9.70 |
| CompletionInputToRender | 20 | 44.85 / 53.80 | 22.55 / 36.10 |
| CompletionLatency | 20 | 157.05 / 180.80 | 132.95 / 170.10 |
| context-refresh | 5 | 973.10 [905.90–1016.70] | 844.00 [813.20–928.80] |
| stylesheet-refresh | 5 | 908.00 [841.10–914.20] | 795.20 [738.50–874.20] |

Browser cold binding was 1153.80 ms before and 1331.60 ms after (one sample each).
Twenty token edits remapped 12500 → 20 spans, visited
350000 → 120 rendered spans, and requested 20 → 0 full redraws.
The token-change handler itself measured 1.75 / 3.30 → 0.30 / 0.60 ms.
During 20 completion edits, Parse 20 → 0, WithText 0 → 20, popup creations
20 → 0, and popup reuse 0 → 20; provider calls remain 20. Each five-sample context
phase reduces classification/diagnostic calls from ten each to five each.

Browser behavior was **executed and observed**, including the extended smoke PASS
for real metadata, utilities, styles/@apply, AKCSS input, live source changes,
completion, hover and undo. After measurement, real input changed square-48 to
square-480 with the candidate and its adjacent description still visible. Proof is
saved as `artifacts/editor-performance/browser-after-completion.jpg`. Stable object
identity and canonical acceptance after further input are asserted in headless tests;
a screenshot alone does not establish object identity.

Browser coverage is the 100-line workload. Valid 1,000/5,000-line browser profiles
were not executed, so the native large-document latency is not a WASM claim. The
selector exposes those sizes for a subsequent browser profile.

The browser is **still blocked by synchronous services**. After optimization,
100-line C# context classification took a median 768.80 ms,
range 741.40–793.30 ms;
diagnostics took 31.60 ms median.
Completion-provider calls were 72.15 ms median,
maximum 78.30 ms,
in addition to separate syntax and adaptation work. Yielding permits input between
stages, not during an indivisible call. Moving such work off the UI thread remains
a separate verified worker/threading task outside this pass.

The browser scrolling median/p95 regressed in this pair despite fewer span visits.
Completion also had one large after-run outlier: input API maximum
296.30 ms,
input-to-render maximum 387.30 ms and completion maximum
566.90 ms, versus
193.20 ms maximum completion before.
No cause is assigned to that outlier without a runtime trace; the lower median/p95
does not mean that browser input stalls have been eliminated.

### Validation

- Release test suite with warnings as errors: **127 passed, zero failed**.
- Separate opt-in Release benchmark: **1 passed**, all three native document sizes
  completed with matching workloads and no service failures.
- Release BrowserSmoke publish with warnings as errors completed, followed by the
  executed browser checks and measurements above.
- Four structural checks run against the instrumented original adapter failed as
  expected: full line scan, LF and CRLF global remapping, and popup recreation.
  The final suite asserts bounded intersections, no full redraw/rebuild on local
  edits, UTF-16 offsets, multiline precedence and resource changes.
- Explicit gates cover syntax sharing, cancellation-resistant late syntax after
  replacement/disposal, diagnostic hold with early current colors, coalesced project
  notifications and refresh contracts, queue priority, burst automatic completion,
  Escape, stable popup identities, current filtered rows, canonical Tab acceptance
  and one-step undo. Description tests cover A/B/A reuse, pending work, stale
  selection/close and retry after null/faulted resolution.
- Existing LF/CRLF continuity, quote/comment delimiter propagation, document
  replacement/rebinding, theme, project/reference/source/import/stylesheet
  invalidation, AKCSS punctuation, diagnostics and hover tests remain passing.
- Workspaces and Akbura.Generator production sources have no diff. Changed text files
  use CRLF and a final newline. No commit, push or pull request was created.

Commands used in the original profiling pass (not current measurement commands):

```powershell
dotnet test src/Playground/Akbura.AvaloniaEdit.UnitTests/Akbura.AvaloniaEdit.UnitTests.csproj -c Release --no-restore -warnaserror
$env:AKBURA_PERFORMANCE_OUTPUT = "$PWD/artifacts/editor-performance/desktop-after.json"
dotnet test src/Playground/Akbura.AvaloniaEdit.UnitTests/Akbura.AvaloniaEdit.UnitTests.csproj -c Release --no-build --no-restore -m:1 -nodeReuse:false --filter FullyQualifiedName~OptInReleaseWorkload
dotnet publish src/Playground/Akbura.AvaloniaEdit.BrowserSmoke/Akbura.AvaloniaEdit.BrowserSmoke.csproj -c Release --no-restore -warnaserror -m:1 -nodeReuse:false -o artifacts/editor-performance/browser-after
git diff --check
git diff --exit-code -- src/Workspaces src/Akbura.Generator
```

The successful benchmark additionally enabled a VSTest diagnostic log to diagnose
an earlier host-launch issue. The browser button and console expose its raw result;
no page-startup timing is substituted for those measurements.

### Changed files

The Playground paths below are relative to `src/Playground/`. There are 30 changed
or new files including the Debug-only instrumentation and collection refactorings;
the additional file outside Playground is the authorized generator AssemblyInfo.cs.

| Project | Files | Purpose |
| --- | --- | --- |
| Akbura.AvaloniaEdit | AkburaEditorSession.cs; AkburaEditorOptions.cs | Split publication, refresh coalescing, appearance/lifecycle invalidation, opt-in instrumentation |
| Akbura.AvaloniaEdit | Infrastructure/EditorPerformance.cs; EditorServiceQueue.cs; EditorSyntaxCache.cs | Bounded counters, serial priority/coalescing, owned incremental syntax |
| Akbura.AvaloniaEdit | Highlighting/WorkspaceColorizer.cs; Diagnostics/DiagnosticRenderer.cs | Indexed tracked presentation, targeted redraw, unchanged diagnostics fast path |
| Akbura.AvaloniaEdit | Completion/AkburaEditorSession.Completion.cs; CompletionDescriptionPopup.cs; CompletionEdits.cs; QuickInfo/AkburaEditorSession.Hover.cs | Stable UI, current canonical changes, deferred descriptions, shared syntax |
| Akbura.AvaloniaEdit | AkburaProjectExtensions.cs; Akbura.AvaloniaEdit.csproj; EditorPerformance.md | Pooled reference preparation, smoke-host internals access and this report |
| Akbura.AvaloniaEdit.UnitTests | EditorArchitectureTests.cs; EditorPerformanceBenchmarks.cs; CompletionDescriptionTests.cs | Structural/race/lifecycle regressions and native workload |
| Akbura.AvaloniaEdit.UnitTests | AkcssCompletionIntegrationTests.cs; Akbura.AvaloniaEdit.UnitTests.csproj | Await coalesced input, module-dot regression, link the shared scenario |
| Akbura.AvaloniaEdit.UnitTests | BuildConfigurationTests.cs | Assert that Release contains no measurement types, properties or Stopwatch reference |
| Akbura.AvaloniaEdit.UnitTests | EditorSyntaxCacheTests.cs | Hash collisions, cached/pending identity, late revisions, inline reentrancy and UI publication without SynchronizationContext |
| Akbura.AvaloniaEdit.BrowserSmoke | EditorPerformanceScenario.cs; SmokeView.Performance.cs; SmokeView.akbura | Shared workload, frame measurement, size/button/output layout |
| Akbura.AvaloniaEdit.BrowserSmoke | SmokeView.akbura.cs; SmokeView.Akcss.cs | Enable profiling after PASS and await deferred automatic completion |
| Akbura.AvaloniaEdit.BrowserSmoke | Debugging/SmokePerformanceView.akbura; Debugging/SmokePerformanceView.akbura.cs; Akbura.AvaloniaEdit.BrowserSmoke.csproj | Separate Debug-only panel and exclude its code and embedded source from Release |
| Akbura.Generator (outside Playground) | src/Akbura.Generator/AssemblyInfo.cs | Authorized friend access to existing builders; no helper implementations copied or changed |
