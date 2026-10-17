# C# compilation invalidation in BlackSilence

## Scope and baseline

This document records the original invalidation investigation and the implemented bounded artifact reuse described [below](#detached-artifact-ownership-and-validation). The historical baseline is `feature/improve-hot-reload` at `7e3967f82c3da21c8755a0323ad66e36617b7418`. Historical measurements use that tree with their accompanying instrumentation. Source provenance records identify measured working files; a HEAD hash alone does not identify uncommitted changes. The original result archives are unchanged; new comparisons use same-session cache-disabled controls rather than historical timings as a denominator.

The historical investigation changed no environment/generation-version equivalence or runtime behavior. The current implementation adds a separate detached artifact owner and an explicit conservative proof before binding. It does not weaken semantic-model compatibility, change runtime/AKCSS reconciliation, manifest or NameScope behavior, or use generated-output equality as semantic proof. No old `SemanticModel`, symbol, operation, probe context or compilation is transferred into a new C# environment.

The question is why changing C# that does not change generated text can still be expensive. Equal generated text is an observation **after** generation; it does not prove that semantic inputs or diagnostics were equivalent before generation.

## Roslyn implementation examined

The benchmark's installed SDK is .NET SDK **10.0.401**, runtime **10.0.12**, with `Microsoft.CodeAnalysis` assembly version **5.9.0.0**, file version `5.900.26.42413`, informational version `5.9.0-1.26423.113+e34a38d2ae1fc26406a317517196e55c68ff83ab`. The informational commit belongs to the `dotnet/dotnet` VMR, not to the standalone `dotnet/roslyn` repository. The ordinary unit-test output instead contains the repository's Roslyn **4.11** package (`4.11.0-3.24376.4+5e3a11e2e7f952da93f9d35bd63a2fa181c0608b`). Test-host and SDK-host verification must therefore be reported separately; a 4.11 unit result is not presented as a 5.9 test result.

The source comparison pins Roslyn `main` at **`7ed110c0f961a5f8800139bc2df914ffb0099aca`** and SDK source at VMR **`e34a38d2ae1fc26406a317517196e55c68ff83ab`**, under `src/roslyn`. All six examined files have identical SHA-256 hashes between these pins. `CompilationCache` exists in the actual SDK source; it is not a future-main-only optimization. This observation applies to these files and pins, not to every Roslyn file or future release. The source manifest in the results records hashes and direct source links.

| Concept | Actual implementation and consequence | Main source / corresponding SDK source |
| --- | --- | --- |
| `EntryState` | Internal `Added`, `Removed`, `Modified`, `Cached` bookkeeping. It is not the public tracked-step enum or a promise of semantic equivalence. | [NodeStateTable](https://github.com/dotnet/roslyn/blob/7ed110c0f961a5f8800139bc2df914ffb0099aca/src/Compilers/Core/Portable/SourceGeneration/Nodes/NodeStateTable.cs) / [SDK](https://github.com/dotnet/dotnet/blob/e34a38d2ae1fc26406a317517196e55c68ff83ab/src/roslyn/src/Compilers/Core/Portable/SourceGeneration/Nodes/NodeStateTable.cs) |
| Table builder | `TryUseCachedEntries` copies old entries without evaluating the transform. `TryModifyEntries` compares newly computed outputs, preserving the previous equal item. A modified input can therefore yield an unchanged output. | Same `NodeStateTable` sources above |
| Transform execution | `TransformNode.UpdateStateTable` skips the function on a cached upstream table/entry. A modified entry invokes the function before equality can stop downstream invalidation. | [TransformNode](https://github.com/dotnet/roslyn/blob/7ed110c0f961a5f8800139bc2df914ffb0099aca/src/Compilers/Core/Portable/SourceGeneration/Nodes/TransformNode.cs) / [SDK](https://github.com/dotnet/dotnet/blob/e34a38d2ae1fc26406a317517196e55c68ff83ab/src/roslyn/src/Compilers/Core/Portable/SourceGeneration/Nodes/TransformNode.cs) |
| Lazy graph evaluation | `DriverStateTable.Builder.GetLatestStateTableForNode` memoizes a node's table within the current update and supplies its previous table. Keys are node instances, not a semantic index of arbitrary compilations. | [DriverStateTable](https://github.com/dotnet/roslyn/blob/7ed110c0f961a5f8800139bc2df914ffb0099aca/src/Compilers/Core/Portable/SourceGeneration/Nodes/DriverStateTable.cs) / [SDK](https://github.com/dotnet/dotnet/blob/e34a38d2ae1fc26406a317517196e55c68ff83ab/src/roslyn/src/Compilers/Core/Portable/SourceGeneration/Nodes/DriverStateTable.cs) |
| Immutable publication | `StateTableStore.Builder.ToImmutable` applies `AsCached`: removed inputs are compacted, retained entries become cached, and old step information is dropped. Builder state is not the published state for the next run. | [StateTableStore](https://github.com/dotnet/roslyn/blob/7ed110c0f961a5f8800139bc2df914ffb0099aca/src/Compilers/Core/Portable/SourceGeneration/Nodes/StateTableStore.cs) / [SDK](https://github.com/dotnet/dotnet/blob/e34a38d2ae1fc26406a317517196e55c68ff83ab/src/roslyn/src/Compilers/Core/Portable/SourceGeneration/Nodes/StateTableStore.cs) |
| Driver compilation cache | Reuse requires the same input compilation reference, equivalent post-init tree references, and matching pre-compilation keys (including source-text/parse-options identities). An empty pre-compilation key set follows an explicitly non-reusing path. This does not make different edited compilations semantically interchangeable. | [CompilationCache](https://github.com/dotnet/roslyn/blob/7ed110c0f961a5f8800139bc2df914ffb0099aca/src/Compilers/Core/Portable/SourceGeneration/CompilationCache.cs) / [SDK](https://github.com/dotnet/dotnet/blob/e34a38d2ae1fc26406a317517196e55c68ff83ab/src/roslyn/src/Compilers/Core/Portable/SourceGeneration/CompilationCache.cs) |
| Semantic lifetime | A semantic model belongs to a compilation and syntax tree and can retain substantial binding state. Local-symbol identity is scoped more narrowly than assembly-rooted symbol identity. Symbols and operations obtained from old models are not portable proof objects for a new compilation. | [SemanticModel](https://github.com/dotnet/roslyn/blob/7ed110c0f961a5f8800139bc2df914ffb0099aca/src/Compilers/Core/Portable/Compilation/SemanticModel.cs) / [SDK](https://github.com/dotnet/dotnet/blob/e34a38d2ae1fc26406a317517196e55c68ff83ab/src/roslyn/src/Compilers/Core/Portable/Compilation/SemanticModel.cs) |

Public tracked steps distinguish `Cached` (the transform was skipped), `Unchanged` (executed and compared equal), and `Modified` (executed and changed). These labels should be read alongside the actual tracked node name. A cached component-output node does not mean its expensive upstream generation was skipped. Equality comparers can also run during table reconciliation/compaction for cached values; a comparison event alone does not prove that its transform ran.

Directly reusable design ideas are stable value-shaped graph inputs, exact comparers before expensive downstream transforms, explicit per-update builders, immutable successful publication and bounded previous tables. Roslyn's compilation cache is not a general cross-compilation semantic cache, and a cached semantic model is not a detached artifact suitable for a different compilation. Those are ownership constraints, not optimizations that can simply be copied into BlackSilence.

## BlackSilence's causal chain

The generator constructs the following graph in `AkburaBlackSilenceGenerator.Initialize`:

```text
CompilationProvider + collected documents + project options
  -> CSharpEnvironmentSnapshot.Create / CSharpEnvironmentSnapshotComparer
  -> new BlackSilenceProjectState(environment.Compilation)
  -> BlackSilenceGenerationRequestBuilder.Create
  -> BlackSilenceDocumentBatch.GenerateSafely
  -> GeneratedSourceComparer / diagnostic comparers
  -> source and diagnostic publication
```

`CSharpEnvironmentSnapshot.Create` enumerates C# trees, obtains roots, collects namespaces and identifiers, checks very narrowly defined inert empty internal classes, checks metadata collisions, and retains all other trees. `HasSameEnvironment` includes assembly/options/script identity, ordered reference identity, parse options, namespaces, retained tree ordering/path/options and complete text. It is **not** a public-signature comparison. An unrelated method-body edit and a retained-tree comment edit are consequently conservative invalidations under the current contract.

When that environment compares unequal, `BlackSilence.ProjectState` executes and constructs a new owner. `BlackSilenceProjectState.TryGetSnapshot` consults only that owner's `_snapshot`. It cannot see the old owner's entries, index or reusable semantic states. This is the first **backend-cache availability** loss after the conservative environment boundary, not a failure of Roslyn to preserve equal downstream text.

`BlackSilenceGenerationRequestBuilder.Create` then has `previous == null`. It constructs an `AkburaProjectIndex`/`AkburaCompilation`, a new declaration-environment token, a dependency graph and requests. `FindPrevious` has no generated entries to compare. Diagnostic entries are likewise unavailable. The trace distinguishes this from an entry that exists but fails a generation/diagnostic version check. It does not invent a `DocumentChanged` reason merely because an entry is absent.

With a previous snapshot in the same owner, generation versions compare document shape, options, declaration-environment identity and dependency versions; diagnostics additionally compare C# environment identity. Ordinary Akbura edits can therefore reuse unaffected entries. `BlackSilenceDocumentBatch.Generate` prepares dirty semantic inputs, collects diagnostics, generates dirty sources and publishes only after cancellation checks and successful completion of the batch. Probe contexts remain scoped to their current semantic owner. An updated immutable driver can share an old best-effort owner with another branch, but output correctness cannot depend on winning that cache publication race.

`GeneratedSourceComparer.Equals` runs later. Its `SourceText.ContentEquals` can keep all component outputs unchanged even after their models, plans, C# text and `SourceText` objects have been rebuilt. That comparison prevents downstream output changes; it cannot refund the work already performed. Diagnostics are independently evaluated and compared, including changes that do not alter emitted text.

## Instrumentation contract

`GenerationStatistics.BeginMeasurement(trackInvalidation: true)` extends the existing `AsyncLocal` session. `documentFilter` selects a path substring while preserving project-level events. Counters and timing stages remain the existing statistics mechanism, with added boundaries for environment construction/comparison, request/version checks, owner construction, prior-snapshot lookup, descriptors, dependency graph, declaration/reference infrastructure and output comparison. Existing probe counters and stages are reused.

The structured observations include:

- Environment construction and comparison, with **all observed failure categories**, not just the original fail-fast condition. Tree positions/path/order, text, parse options, namespace introductions, references and compilation options remain distinguishable.
- State/compilation/index/declaration-environment identities and previous generated/diagnostic/semantic-state availability.
- Per-document source, generation-shape, surface and dependency-name fingerprints; per-request dependency fingerprints and actual generate/reuse or evaluate/reuse decisions. When old state is missing, comparisons explicitly say unavailable/unknown.
- Completed snapshot publication, cancellation and output text/reference comparisons.

Fingerprint labels are measurement-only SHA-256 summaries of scalar text/shape inputs, not replacements for exact cache equivalence. The harness separately compares observations across runs, even when the backend cannot access its old owner. It retains scalar observations, not old syntax/semantic graphs. Source fingerprints do not themselves prove semantic equivalence. A missing callback emits no fabricated document event; tracked steps explain why work was skipped.

Trace identities use a `ConditionalWeakTable` whose values contain only numeric IDs. Events contain strings/immutable collections of strings, never compilations, syntax trees, semantic models, symbols, operations or deferred delegates. Independent/nested sessions have separate buffers. Disposal restores the ambient session. Canceled events describe attempted/completed boundaries, never a successful driver result. The disabled path has no per-document event construction or formatting; non-`STATS` call sites are compiled out. The original two-parameter statistics entry point remains available for binary compatibility.

The path filter applies to exported events, not to aggregate counters/timings or to the workload. Backend `GenerationShapeChanged`/`SurfaceChanged` observations describe failed existing equivalence predicates, including reuse eligibility and identity checks; they are not necessarily literal text differences. Raw/version fingerprints and `CanReuse` are recorded separately. `SourceRead` and `DocumentVersion` observations precede canonicalization, so a trailing-whitespace edit remains observable even if downstream callbacks are skipped.

### Timing and allocation boundaries

`GeneratorMs` independently times `RunGeneratorsAndUpdateCompilation` only. Input mutation, helper-library emission, JSON serialization, diagnostic validation and fresh-driver parity checks are outside that interval. `ProcessCpuMs` is process CPU consumption sampled around the run, not wall time. All validation remains enabled.

Stage times are **inclusive**, accumulated across invocations and sometimes parallel workers. `SemanticBinding`, probe binding, diagnostic queries, planning and emission overlap; adding them is invalid. Enclosing `DocumentBatch` is wall time, while accumulated component-generation/emission scopes may exceed wall time. Comparer details collected by the opt-in full scan have their own `CSharpEnvironmentTraceComparison` stage; they do not change the original fail-fast comparer result.

Stage allocations use `GC.GetAllocatedBytesForCurrentThread` on each worker. Cross-thread/decreasing samples are rejected and counted. `CurrentThreadAllocatedBytes` covers the driver caller only. `ProcessAllocatedBytes` uses `GC.GetTotalAllocatedBytes(true)` and includes managed allocations on other workers; it is not retained memory or native memory. Heap, working set and collection counts are observations, not sums of stage allocations. Allocation counters include small measurement-boundary overhead. A standalone `fresh-driver` run is not a cold process, JIT, metadata cache, filesystem or OS measurement.

## Benchmark matrix and reproduction

The existing `benchmarks/HotReload` executable gains an `invalidation` command and the existing `Run.ps1` gains a suite; all earlier suites/scenarios remain available. The old `csharp` scenario remains untouched. Its corresponding new `original-add-tree` scenario explicitly starts with the original compilation and adds a new tree, rather than pretending to simulate an IDE tree replacement.

The new matrix covers unchanged compilation, fresh driver, Akbura scalar/trailing-whitespace edits, unrelated method-body replacement, separate retained-tree comments and whitespace edits, an inert empty-class addition, referenced method body/signature, overload, `const`, relevant diagnostic attribute, global using, compatible/incompatible PE reference replacement, unresolved-to-defined lookup, nullable/preprocessor/language/compilation options, tree addition/removal, repeated identical edit, edit/revert and multiple changed C# trees.

FeatureGallery retains its **28 real components**. Purity uses the pinned tutorial submodule via `Prepare-Purity.ps1`: only a disposable committed-source snapshot is adapted to local runtime/analyzers. The submodule checkout and the user's original external project are neither edited nor built. Two synthetic supporting C# trees are added in memory, without adding a component. Referenced-input cases replace one existing text expression in the selected component for that scenario's baseline. Metadata references are emitted outside the measured interval. Thus these are controlled workloads over real projects, not unmodified end-to-end IDE sessions.

Each workload/mode records one initial pass, two warm-ups and eight measured passes. Nearest-rank p95 with eight samples equals the maximum. Both final alternating states are compared with a fresh driver for source identity/text and complete diagnostic identity: ID, severity, invariant message, original/mapped positions, source span, mapping flag and additional locations. Component, inline/external AKCSS and project sources are included. The pre-existing isolated compiler errors caused by not running Avalonia's `InitializeComponent` generator remain recorded; this is not a replacement for a successful complete MSBuild build. Deliberately unresolved cases may additionally introduce expected diagnostics, which must match a fresh driver rather than be suppressed.

```powershell
git submodule update --init -- benchmarks/HotReload/Akbura-PurityUI-Tutorial
pwsh -NoProfile -File benchmarks/HotReload/Run.ps1 -Suite invalidation -Prepare `
  -OutputDirectory bin/CSharpInvalidationInvestigation/matrix
pwsh -NoProfile -File benchmarks/HotReload/Summarize-Invalidation.ps1 `
  -InputDirectory bin/CSharpInvalidationInvestigation/matrix `
  -ResultDirectory benchmarks/HotReload/results/2026-10-16-csharp-invalidation
```

Use `-InvalidationScenario unrelated-body,akbura,unchanged -InvalidationProfiling off,stats,trace` for a profiling-overhead comparison; keep it in a separate raw directory and summarize that directory separately. `off` uses the same `DebugStats` binaries with no ambient session, `stats` enables timings/counters only, and `trace` also enables structured events. Do not interpret a missing counter in `off` as zero work. This comparison does not quantify the difference between a non-`STATS` production binary and a stats binary.

For array-valued options, invoke the script directly inside PowerShell 7 with `& ./benchmarks/HotReload/Run.ps1 ... -InvalidationScenario @('unrelated-body', 'unchanged') -InvalidationProfiling @('off', 'stats', 'trace')`; the native `pwsh -File` argument boundary does not preserve those PowerShell arrays.

No builds/tests should overlap final timing runs. The reference workstation is Windows 11 build 26200, Intel Core i5-11400H (6 cores/12 logical processors), approximately 16 GiB physical memory. This is a developer desktop, not an isolated performance lab; the archived environment records available memory and source provenance. Cross-run variation and memory pressure must be considered before attributing small differences to the profiler.

The new result directory is separate from the historical `results/2026-10-16` archive. `Summarize-Invalidation.ps1` exports samples, stages, tracked steps, compact per-scenario transition ranges, causes, summaries, provenance and compressed raw JSONL. The transition CSV distinguishes skipped, executed and mixed callbacks using public step reasons rather than output text. It validates sample/parity counts and updates only its marked README block, preserving manual text outside that block.

## Measurements and interpretation

### Primary Gallery result

For an unrelated method-body replacement, the eight measured SDK-host runs have a **12,698.99 ms median**, **12,583.44–13,070.97 ms range**, and **13,070.97 ms nearest-rank p95**. No generated text changes. The original add-tree pattern is measured separately: **13,092.43 ms median**, **12,857.61–13,406.73 ms range**. These are current instrumented observations, not a claimed regression/speedup against the historical 8,410.99 ms run, whose workload pattern and measurement conditions differ.

The replacement changes one retained C# tree; 19 retain their identities. The comparer records `TreeTextChanged`, not changed references, project options, Akbura source shapes or dependency fingerprints. Each measured run creates a new project state, index, Akbura compilation and declaration token. Its accessible previous snapshot/entries/semantic states/diagnostic entries are all absent. It reparses **zero** Akbura files, creates **31** semantic models, regenerates **28** components plus **5** AKCSS outputs, evaluates **32** diagnostic documents, and creates **33** document-writer source texts. Actual source publication is **zero**. Both final states match a fresh driver in source text/identity and complete diagnostics.

The actual public tracked transitions are:

| Tracked node | Observed output reason/count |
| --- | --- |
| `Compilation` | `Modified: 1` |
| `BlackSilence.SourceTexts`, `BlackSilence.SourceFiles`, `BlackSilence.SyntaxTrees`, `BlackSilence.Documents` | `Cached: 32` each |
| `BlackSilence.CSharpEnvironment`, `BlackSilence.ProjectState`, `BlackSilence.GenerationRequests`, `BlackSilence.GeneratedBatch` | `Modified: 1` each |
| `BlackSilence.GeneratedComponents` | `Unchanged: 28` |
| `BlackSilence.GeneratedExternalAkcss` / `BlackSilence.GeneratedInlineAkcss` | `Unchanged: 3` / `Unchanged: 2` |
| `BlackSilence.GeneratedProjectSources` | `Unchanged: 1` |
| `BlackSilence.Diagnostics` | `Cached: 1` |
| `SourceOutput` | `Cached: 35` (34 source slots plus the diagnostic slot, not 35 source files) |

### Measured cost boundaries

Gallery unrelated-body medians below are **inclusive**, not exclusive/additive buckets. Counts are actual invocations from a representative measured run; all individual values remain in the stage CSV.

| Boundary | Invocations | Median ms | Interpretation |
| --- | --- | --- | --- |
| Entire driver | 1 | 12,698.99 | Independent wall clock |
| Document batch | 1 | 12,668.92 | Enclosing backend wall time |
| Semantic input binding/preparation | 33 | 11,647.88 | Dominant enclosing phase; dirty inputs are prepared serially |
| C# probe binding | 3,066 | 3,599.22 | Nested binding work, not all semantic preparation |
| Probe diagnostics requests | 3,512 | 1,920.64 | Includes cache lookups; 2,912 actual diagnostic queries |
| Component generation | 28 | 1,871.40 | Accumulated worker scopes, not parallel-batch wall time |
| C# emission | 33 | 1,330.41 | Repeated despite identical final text; overlaps writer scopes |
| Lazy referenced-module loading | 1 | 745.00 | Cold module work in the new owner, not repeated getter accesses |
| Component planning | 28 | 680.76 | Overlaps generation and some probe work |
| Document `SourceText` construction | 33 | 61.51 | Reconstructed document texts later compare equal |
| Driver time outside document batch | paired difference | 24.42 | Not exclusively Roslyn overhead |
| Request construction | 1 | 10.31 | Includes 9.21 ms of opt-in trace observation work |
| Environment construction | 1 | 1.97 | Not the explanation for the multi-second stall |
| Shared C# probe compilation | 1 | 3.34 | Different from the many per-probe derived compilations |
| Diagnostic collection batch | 1 | 1.81 | After preparation has already populated semantic/diagnostic caches |
| Project index/catalog | 1 | 0.188 | Rebuilt but small in this workload |
| Akbura compilation construction | 1 | 0.008 | Cheap constructor; expensive lazy work occurs later |
| Original environment comparison | 1 | 0.012 | Fail-fast production predicate |
| Trace-only full comparison | 1 | 0.039 | Separate multi-cause observation pass |

The run creates 2,892 probe syntax trees/derived compilations/semantic-model acquisitions. Existing caches still account for 3,841 reused probe contexts, 854 reused binding results and 600 reused diagnostic queries; these are overlapping reuse dimensions, not additive savings. The dominant outer binding duration cannot all be attributed to the narrower probe-binding timer. More granular native symbol/type lookup profiling would be needed to partition the remaining semantic work exactly.

The causal ranking is therefore: conservative retained-C# environment invalidation triggers loss of backend cache availability; that in turn causes expensive semantic preparation and probes across every component. Repeated planning/emission is a second measurable cost even though outputs compare equal. Lazy metadata/module reconstruction is meaningful but its constructor is not the expensive part. Empty diagnostic-entry reuse is lost, yet the cheap final diagnostic-collection batch must not conceal diagnostic queries already done during binding. Environment scanning/comparison, index construction and driver bookkeeping are minor compared with binding. No table of nested scopes is summed to manufacture a total or an expected saving.

### Primary Purity result

The pinned tutorial snapshot contains **15 generated components**, **17 diagnostic documents** and **16 total source outputs** including the project service. It has no local external/inline AKCSS source outputs in this evaluated project; imported utility metadata still participates in binding. This differs from the older external-project snapshot, so historical component counts/timings must not be silently reused for this input.

For the corresponding unrelated-body replacement, the eight measured runs have a **2,295.80 ms median**, **2,099.76–3,544.86 ms range**, and **3,544.86 ms nearest-rank p95**. All 15 components regenerate; 15 semantic models are created; 17 diagnostic documents are evaluated. Akbura parse counts, changed generated texts and `AddSource` calls are all zero. Both final states match a fresh driver. The environment/state/request/batch nodes are modified, while all 15 component outputs are unchanged and their source-output callbacks remain cached.

| Boundary | Invocations | Median ms |
| --- | --- | --- |
| Entire driver | 1 | 2,295.80 |
| Document batch | 1 | 2,286.42 |
| Semantic input preparation | 15 | 2,173.00 |
| C# probe binding | 882 | 1,362.33 |
| Probe diagnostics requests | 1,013 | 513.12 |
| Lazy referenced-module loading | 1 | 573.28 |
| Component generation | 15 | 282.46 |
| Component planning | 15 | 195.63 |
| C# emission | 15 | 81.77 |
| Source-text construction | 15 | 8.85 |
| Outside document batch | paired difference | 9.39 |
| Environment construction | 1 | 1.17 |
| Environment comparison | 1 | 0.009 |

These are again inclusive/overlapping durations. There are 869 actual derived probe compilations/model acquisitions and 872 actual diagnostic queries; caches supply 1,910 reused contexts, 309 reused binding results and 141 reused diagnostic queries. Pure output-artifact reuse has a smaller possible benefit here than in Gallery: semantic preparation, not emission, dominates. The new owner cannot access any previous entries or semantic/diagnostic states despite unchanged Akbura source/dependency fingerprints.

### Matrix coverage and reuse

The complete 27-scenario matrix finished for both Gallery and Purity Sidebar. Every group contains eight measured updates after its initial run and two warm-ups, with exact source/full-diagnostic parity against a fresh driver at both final states. The raw samples distinguish unchanged callbacks from callbacks that executed and produced equal text.

| Workload | Gallery median ms / generated components | Purity Sidebar median ms / generated components |
| --- | --- | --- |
| Unchanged compilation | 17.63 / 0 (backend skipped) | 3.10 / 0 (backend skipped) |
| Akbura scalar edit | 498.94 / 1, 27 reused | 1,262.11 / 1, 14 reused |
| Akbura trailing whitespace | 18.83 / 0 (backend skipped) | 7.53 / 0 (backend skipped) |
| Unrelated C# body | 12,698.99 / 28 | 2,295.80 / 15 |
| Referenced C# body | 12,334.33 / 28 | 2,399.70 / 15 |
| Referenced signature | 12,435.56 / 28 | 2,496.19 / 15 |
| C# comment | 13,005.01 / 28 | 2,296.95 / 15 |
| C# whitespace | 12,875.11 / 28 | 2,303.61 / 15 |
| Inert empty-class addition | 20.85 / 0 (environment equal) | 4.62 / 0 (environment equal) |
| Repeating the same edited compilation | 13.27 / 0 (backend skipped) | 2.50 / 0 (backend skipped) |

For unrelated/referenced-body/signature edits, the backend reuses zero component entries, evaluates 32/17 diagnostic documents, and has no accessible previous snapshot. Generated source texts change in none of these cases. This is evidence of conservative invalidation and lost artifact availability, not permission to skip semantic validation for all method-body/signature changes. The overload case adds a non-applicable string overload to a zero-argument call; it checks conservative invalidation, not every form of overload rebinding.

All cases and individual ranges are in the generated results. Gallery's later full-regeneration cases have approximately 8.6–9.7 s medians rather than the earlier 12–14 s medians despite similar work/allocation counts. They ran sequentially on a developer desktop. This substantial drift prevents treating cross-case latency differences as isolated causal effects or an instrumentation speedup.

An additional Purity `NeedHelp` subset covers a different component without changing the 15-component workload: scalar edit **411.87 ms** (one generated, 14 reused, one diagnostic document evaluated), trailing whitespace **6.80 ms** (backend skipped), and unrelated C# body **2,781.05 ms** (all 15 generated, 17 diagnostic documents evaluated, zero changed texts). All six final fresh-driver comparisons pass. In total the main archive contains **57 groups / 627 driver runs / 114 parity checks**; separate profiler controls add **8 groups / 88 runs / 16 parity checks**. Unsupported alias reproduction is not included in those totals.

### Allocation and lifetime observations

The unrelated-body driver allocates a median **3,002,395,908 managed bytes** in Gallery and **954,068,968 bytes** in Purity across all process threads. These are allocation volumes per update, not retained memory or evidence of a leak. The raw samples also retain heap size, working set and GC collection counts.

Representative Gallery stage medians below use the existing per-thread allocation scopes, accumulated on the threads executing each stage. Scopes are inclusive and overlap; these numbers must not be summed or subtracted to estimate exclusive allocation.

| Stage | Median inclusive allocated bytes | Invalidation/ownership explanation |
| --- | --- | --- |
| Environment construction / comparison | 343,920 / 280 | Retained tree text differs |
| Project-state constructor | 504 | New environment creates a new owner; trace payload included |
| Project descriptors / catalog | 64,744 / 70,504 | Previous index unavailable |
| Akbura compilation constructor | 3,088 | New index; most semantic work is lazy |
| Request construction | 5,328,072 | Previous entries unavailable; includes trace fingerprints |
| Semantic preparation | 2,683,270,796 | Every dirty document is bound in the new compilation |
| Probe binding / diagnostic requests | 415,658,048 / 206,118,004 | Nested current-owner queries and cache lookups |
| Lazy referenced-module loading | 596,572,400 | One actual lazy load, not getter invocation count |
| Final diagnostic collection | 72,028 | Binding already populated diagnostic caches |
| C# emission / source-text construction | 199,954,004 / 22,396,944 | New texts subsequently compare equal |

The weak-reference regression check keeps an exported trace snapshot alive while a synthetically observed compilation becomes collectible. Together with scalar-only events and weak identity keys, this guards against tracing itself rooting an old compiler graph. It is not a heap-dump audit of every generator/IDE cache or a proof of bounded process memory over a long IDE session. Current project snapshots legitimately retain their current compilation-bound state; future cross-compilation reuse must detach artifacts rather than retain old semantic graphs.

### Profiling controls

Separate controls repeat the unrelated-body workload with identical evaluated project inputs and helper edits. They run, in order, non-`STATS` Debug production/off, `DebugStats`/off, stats-only, and full trace. Each mode has its own process, initial pass, two warm-ups, eight samples and two fresh-driver parity checks. Binary flavor and both analyzer hashes are recorded, so production/off cannot be confused with an inactive session in a stats-enabled binary.

| Gallery mode | Median ms | Range ms | Median process allocated bytes |
| --- | --- | --- | --- |
| Production / off | 10,015.58 | 9,884.38–11,002.64 | 2,993,121,700 |
| Stats binary / off | 9,405.26 | 8,688.30–10,097.02 | 2,994,353,216 |
| Stats binary / counters and timers | 8,911.58 | 8,492.63–9,923.83 | 2,998,845,040 |
| Stats binary / full trace | 9,231.64 | 8,874.20–9,445.58 | 3,002,807,572 |

All Gallery modes produce zero changed source texts and pass parity. The trace median is 3.59% above stats-only but 7.83% below production/off in this ordered series. Those contradictory directions, overlapping ranges and drift from the earlier 12,698.99 ms trace series prevent a causal timing-overhead percentage or speedup claim. The observed trace-versus-production allocation difference is 9,685,872 bytes (0.32% of the roughly 3 GB allocation volume); stats-only to trace adds 3,962,532 bytes. These are process observations, not isolated per-event allocation costs.

| Purity Sidebar mode | Median ms | Range ms | Median process allocated bytes |
| --- | --- | --- | --- |
| Production / off | 2,555.09 | 2,283.11–3,664.37 | 950,487,376 |
| Stats binary / off | 2,321.20 | 2,103.25–3,257.12 | 950,935,224 |
| Stats binary / counters and timers | 2,438.18 | 2,178.34–3,394.86 | 954,056,552 |
| Stats binary / full trace | 2,346.04 | 2,152.15–3,625.66 | 953,920,468 |

Purity likewise passes both parity checks with zero changed source texts in every mode. Its trace median is lower than stats-only, and the ranges overlap substantially. The measured trace-versus-production allocation difference is 3,433,092 bytes (0.36%); trace allocates slightly less than stats-only in this process-level observation. No reliable timing-overhead bound can be derived from this single ordered series. A randomized/interleaved repeated-process experiment on an isolated workstation would be needed to estimate a small causal overhead. Both ordinary production controls still reproduce the multi-second stall without trace code running.

Production control reproduction after preparing the same evaluated inputs:

```powershell
dotnet build benchmarks/HotReload/HotReloadBenchmarks.csproj `
  -p:BlackSilenceConfiguration=Debug -o bin/CSharpInvalidationInvestigation/production-tool
dotnet bin/CSharpInvalidationInvestigation/production-tool/HotReloadBenchmarks.dll `
  invalidation bin/CSharpInvalidationInvestigation/matrix/gallery-inputs.json Counter.akbura unrelated-body off
```

Build it outside the stats-tool output directory and do not rebuild either tool during measurements. Stats modes use the tool prepared by `Run.ps1 -Suite invalidation -Prepare`. Their exact order/timestamps are archived in `overhead/control-order.json`. No production Release latency is inferred from these Debug controls.

### Unsupported global-alias case

The first global-using variant added a `global using Alias = System.Text.StringBuilder;`. Generated component files repeated the global alias, producing compiler error `CS1537`. That failed attempt is excluded from successful timing groups. The main matrix instead alternates valid namespace imports (`System.Text` / `System.Threading.Tasks`) and passes source/diagnostic parity; its generated texts legitimately change because imports change.

A separate one-component reproduction using a fresh non-`STATS` production generator has the same `CS1537` error on the initial pass and both final fresh-driver checks. Its evidence and compressed raw records are archived under `global-alias/`, explicitly not summarized as successful latency. Neither diagnostic suppression nor a generator fix is introduced. `harness-update.json` records the benchmark-only correction; the BlackSilence/Generator binaries were unchanged during the main matrix.

### Measurement limitations

These are generator invalidation measurements, not complete IDE/`dotnet watch` save-to-render measurements. Compiler emission, Edit and Continue, CLR delta application, UI handlers and presentation are not timed here. Signature/reference/language changes are useful correctness inputs even when an IDE/CLR would reject them as rude edits; passing generator parity does not establish Hot Reload support for such edits. This investigation alone cannot attribute every minute-long IDE pause.

Probe syntax trees and derived compilations are in-memory work, not thousands of changed project files. `GeneratedSourceTextCreated` retains its existing document-writer coverage; the additional project-level service output explains why its count can be smaller than total source outputs. `SourcePublished` counts actual `AddSource` callbacks across all output categories. Backend reuse counters can be zero when Roslyn skipped the backend entirely; tracked steps distinguish that case.

Diagnostic collection can be cheap after semantic preparation has already performed probe diagnostic queries. A small `DiagnosticBatch` duration is therefore not the total cost of diagnostics. Probe/context/result/diagnostic reuse counters also overlap and must not be added as independent savings. The paired `OutsideDocumentBatchMs` difference contains request building, environment work, comparers, publication and driver bookkeeping; it is not exclusively Roslyn overhead.

The harness uses real evaluated source/reference inputs but controlled compilation options (`DynamicallyLinkedLibrary`, Debug optimization and initially default nullable settings), not the entire project's `Csc` command line. C# parsing/helper-library emission and binary hashing happen before the measured driver interval. Initial samples do not include process startup or represent cold filesystem caches.

## Automated checks

`BlackSilenceIncrementalGenerationTests.Invalidation.cs` adds checks for unchanged callbacks, actual unrelated-edit strategy (without freezing that strategy as a permanent contract), referenced-signature parity, diagnostic changes despite equal generated text, multiple simultaneous invalidation causes, cancellation/no partial publication/retry, concurrent branches and nested-session isolation, disabled event allocation/output parity, document filtering and weak compiler-graph lifetime.

There are 11 new trace tests and one additional statistics-fixture regression. The existing exact parse-count fixture could select two filenames mapping to the same slot in the 32-slot direct-mapped parse cache, even with test-collection isolation. It now selects distinct slots using the real cache hash and explicitly tests a colliding key pair. The production cache, its equivalence policy and the exact parse-count assertions are unchanged.

The disabled-guard allocation check observes zero bytes over 1,000 event guards; it is not a claim that an entire generator run allocates nothing. The lifetime check keeps an exported trace snapshot alive while the observed compilation becomes collectable. The concurrent test runs two branches from the same previous driver, verifies each against a fresh driver and checks distinct compilation identities and separate event buffers.

```powershell
dotnet test src/Akbura.UnitTests/Akbura.UnitTests.csproj -c DebugStats `
  --filter 'FullyQualifiedName~BlackSilence|FullyQualifiedName~AkburaProjectIndex|FullyQualifiedName~DocumentSyntaxVersion|FullyQualifiedName~GenerationStatistics' `
  -- xUnit.MaxParallelThreads=4
```

The final selected run passes **230 tests, zero failures and zero skips** under the ordinary Roslyn 4.11 host. The separate SDK-host run passes **17 tests, zero failures and zero skips** under Roslyn 5.9, including an assembly-version assertion inside the executing host. New invalidation-report checks pass **12/12**, and existing report checks pass **17/17**. Release analyzers and the final benchmark build have zero errors/warnings. Brace/multiline-summary verification passes for all 25 changed C# files.

The DebugStats test build retains pre-existing `CS8602` in Furioso's `ComponentGenerator.cs` and xUnit analyzer warnings in `QuotedResourceRegressionTests.cs`; no warning suppression or unrelated fix is introduced. This investigation does not claim that a full repository-wide test audit was rerun. [Archived verification](../benchmarks/HotReload/results/2026-10-16-csharp-invalidation/verification/README.md) contains the exact commands, TRX results, SDK hashes/version assertion and host-resolution evidence.

For a distinct SDK-host check, `pwsh -NoProfile -File benchmarks/HotReload/Verify-SdkRoslyn.ps1 -Prepare` copies the existing test output into a unique ignored directory and substitutes only the SDK's two Roslyn host assemblies. The original package-based output and repository package references are left unchanged. Assembly versions/hashes, host-resolution logs and TRX counters identify that verification separately; it is not reported as a package upgrade.

### Changed implementation areas

- `src/Akbura.Generator/Language/CodeGeneration/GenerationStatistics.cs` and `GenerationInvalidationEvent.cs`: opt-in scalar trace storage and additional existing-statistics boundaries.
- `src/Akbura.BlackSilence/BlackSilenceInvalidationTrace.cs`, `CSharpEnvironmentSnapshot.Tracing.cs`, environment/version comparers, project state, request construction and output publication: observation call sites, guarded by `STATS`.
- Generator project-index/dependency/declaration/reference-module entry points: timings around actual construction/lazy loading; no semantic cache transfer.
- Both generator assembly-info files: benchmark friend access to existing internal instrumentation.
- `BlackSilenceIncrementalGenerationTests.Invalidation.cs` and `BlackSilenceGenerationStatisticsTests.cs`: instrumentation and fixture regressions.
- Existing Hot Reload program/project/runner and documentation, new invalidation summarizer/report checks/SDK verification, solution items and the separate results archive.

## Architecture alternatives evaluated

Simply retaining the old project owner or copying its snapshot is not a safe fix. `DocumentGenerationVersion` has no separate C# compilation field: the current owner/declaration-environment boundary indirectly supplies broad C# invalidation. Preserving that declaration token without adding explicit semantic validation could incorrectly reuse generated text after a meaningful C# change. `DocumentDiagnosticVersion` already has a separate C# environment identity. Any ownership redesign must preserve both contracts rather than merely make `previous != null`.

### A. Preserve bounded owner-independent artifacts, rebind in the new compilation

Separate a latest-successful, compilation-independent artifact snapshot from the compilation-bound semantic context. Keep document identities/shapes, descriptors, emitted texts, detached diagnostic records and (if designed as an explicitly immutable value) a generation-plan description. Reconstruct/rebind semantic inputs in the current compilation whenever C# invalidates. Compare a freshly validated plan against the old plan before emission; do not accept old generated text as evidence that rebinding can be skipped.

Likely files: `AkburaBlackSilenceGenerator`, `BlackSilenceProjectState`/snapshot, request/version classes, `AkburaProjectIndex`, plan representations and the document batch. Keep publication transactional and versioned across branches; publish nothing from a canceled attempt. A single bounded previous artifact generation avoids an unbounded cache. Old models/probes/symbols/operations must not enter this artifact layer. Detached diagnostics still require recomputation when their environment changes, including negative lookups and mapped positions.

Expected eliminable work: descriptor reconstruction and emission/`SourceText` construction when freshly validated plans are exactly equal. It does **not** automatically eliminate the expensive semantic preparation/probes following a C# edit. The correctness fallback is the existing complete current-compilation path for unsupported plans or missing artifacts. Proof requires exact fresh-driver source/diagnostic parity, branch/cancellation stress and a lifetime test showing that old compiler graphs are released. Fingerprint hashes alone are insufficient equality evidence.

This is the simpler ownership improvement and the recommended first architectural experiment. Its expected benefit must be bounded by measured emission/descriptor costs, not by the full observed regeneration time.

The measurable success condition is fewer emission/source-text invocations for exactly equal, freshly validated plans, with unchanged semantic-validation coverage and full diagnostic parity. Gallery's 1,330 ms accumulated emission and 62 ms source-text scopes identify the target, but overlap/parallelism prevent converting them into an additive wall-time saving or promised percentage. Purity's 82/9 ms scopes suggest a smaller opportunity.

### B. Dependency-tracked, compilation-independent semantic query inputs

Record explicit query descriptions and exact stable dependency facts for generation and diagnostics: qualified and unqualified names, candidate/overload sets, extension scopes/global usings, conversions/nullability, constants, relevant attributes, hook/AKCSS metadata, parse/compilation options, references and unresolved/ambiguous lookups. New compilations must validate/replay those facts; old Roslyn symbols or operations are not reusable query results. A positive-symbol-only dependency graph is unsound when a previously missing declaration or a new overload appears.

Likely files: environment snapshot/comparer, dependency graph, semantic binding and C# probe binder/cache, generation/diagnostic version types, project index and the document batch. Separate diagnostic dependencies from output dependencies. If a query or option cannot be described and validated soundly, fall back to the current broad environment invalidation and recomputation. Keep only bounded scalar/query artifacts; current semantic models and probe contexts remain owned by the current compilation.

Expected eliminable work: semantic preparation and probe work for genuinely unaffected documents, in addition to plan/emission work. This targets a larger possible saving but has substantially greater invalidation complexity. Required proof includes newly defined symbols, overloads/extension methods, relevant attributes, constant changes, metadata replacement, nullable/preprocessor/language modes, source mapping, incomplete syntax, concurrency/cancellation, memory retention and full fresh-driver diagnostic/source parity. Start with a demonstrably safe subset and keep fallback coverage measurable. No broad method-body/signature shortcut should be enabled merely because this finite matrix happened to produce identical text.

The measurable success condition is a reduction from 33/15 prepared semantic inputs to the actually affected subset in these unrelated-edit workloads, accompanied by fewer actual probe compilations/diagnostic queries. Revalidation overhead and fallback rate must be included in the driver measurement. This is the option addressing the primary time cost; there is no measured speedup or justified percentage estimate until it exists and passes the same parity/lifetime matrix.

The alternatives above describe the original investigation's design space. The implementation below chooses a bounded detached layer and a narrow exact dependency proof, not a reusable semantic plan or general query-replay engine. Further expansion requires additional proofs and measurements.

## Detached artifact ownership and validation

The project/options incremental node creates one `BoundedArtifactStore`, independently of `CompilationProvider`. Each new C# environment still creates its own `BlackSilenceProjectState`; it receives that store but does not inherit the old bound snapshot. Independent drivers, including drivers using the same generator adapter, have independent owners. Options changes establish a new owner. There is no static project/driver registry.

### Field and indirect-reference audit

| Owner / value | Actual reachable state | Cross-compilation retention policy |
| --- | --- | --- |
| Roslyn driver tables | Current provider outputs, requests, tracked steps and generated sources | Roslyn/caller-owned; old externally retained drivers can retain old compiler contexts. No cache stores a driver. |
| `BlackSilenceProjectState._snapshot` | Index, documents/ASTs, versioned source/diagnostic entries, semantic-state dictionary and its current detached values | Current same-compilation context only; never placed in the detached store. The detached-value reference has no reverse edge into this bound snapshot. |
| Request `Index`, `Documents`, declaration token | `AkburaCompilation`, source trees, declarations, resolvers and current C# compilation | Compilation-specific. Optional previous bound snapshot is a weak reference, avoiding a full previous-context root in retained request outputs. |
| `AkburaCompilation` / index | Semantic models, syntax/declaration manager, reference manager, lazy probe compilation and resolver closures | Never detached. In particular, reference-manager lazy closures capture the originating C# compilation. |
| `SemanticModelState` | Source tree, C# compilation and symbol/diagnostic caches | Same-compilation import checks unchanged; never copied across C# environments. |
| `ComponentPlan` | Symbols, operations, bound definitions and AST nodes | Short-lived preparation only. No plan is retained in the artifact layer. |
| Generated / diagnostic entries | Version objects with AST/dependency documents and, for diagnostics, C# environment identity | Entries are not detached artifacts. A cache hit constructs a new entry with the **current** request's version. |
| `BoundedArtifactStore._latest` | One `ArtifactPublication`: current detached artifact snapshot and current weak identity handles | Replaced atomically; no previous pointer, historic map, LRU or event/delegate capture. Only two monotonic scalar counters accompany it. |
| `DetachedProjectArtifacts` | Schema/options values, exact C# source strings/token/name arrays, document identities/shapes/lookups, generated characters and diagnostic records | Fresh current maps on every successful publication; removed documents/keys do not survive structural sharing. No compiler-backed descriptors or version objects. |
| Detached source | `SourceText.From(string, UTF8, checksumAlgorithm)` | Explicitly materialized `StringText`, never an arbitrary writer/custom `SourceText`. The audited [Roslyn implementation](https://github.com/dotnet/roslyn/blob/7ed110c0f961a5f8800139bc2df914ffb0099aca/src/Compilers/Core/Portable/Text/StringText.cs) and [SDK implementation](https://github.com/dotnet/dotnet/blob/e34a38d2ae1fc26406a317517196e55c68ff83ab/src/roslyn/src/Compilers/Core/Portable/Text/StringText.cs) contain character/encoding data, not compiler ownership. |
| `AkburaDiagnosticRecord` | String IDs/messages/paths, scalar severity/kind/provenance/version, spans/line spans, immutable location records and string properties | All nested fields are values. Primary and additional mapping dependencies are separately validated. No `Diagnostic`, syntax location or descriptor retaining a compiler graph is cached. |
| Identity validation session | Temporary reference-identity dictionary and a captured publication | Lives only during current capture. Export contains IDs and **weak** handles for currently used inputs only; session is not a pipeline output. |

```text
caller -> current Roslyn driver -> current request -> current state / bound snapshot
                                                   -> index -> AkburaCompilation
                                                               -> models / probes / C# compilation
                            current bound snapshot -> its current detached values
                            \-> options node -> BoundedArtifactStore
                                                  -> latest publication
                                                       -> detached scalar facts / text / diagnostics
                                                       -> current IDs + WEAK option/reference handles

No detached arrow returns to the bound graph. No successful snapshot links backward.
```

The `SourceText` audit includes inherited lazy fields, not just the `StringText` type name: the string factory supplies no custom container, line information refers only to that text and scalar positions, and the default static container refers only to that text and ignores event subscriptions. Checksum/content-hash and embedded-text storage contains bytes, not semantic objects. See the pinned [Roslyn base implementation](https://github.com/dotnet/roslyn/blob/7ed110c0f961a5f8800139bc2df914ffb0099aca/src/Compilers/Core/Portable/Text/SourceText.cs) and [SDK base implementation](https://github.com/dotnet/dotnet/blob/e34a38d2ae1fc26406a317517196e55c68ff83ab/src/roslyn/src/Compilers/Core/Portable/Text/SourceText.cs). An arbitrary `SourceText` subclass or caller-supplied container would not satisfy this ownership argument and is never retained as an artifact.

The identity window replaces all keys each publication. Its size is at most the current reference count plus twice the C# tree count plus one compiler-options slot (shared objects can reduce this). Exact `ReferenceEquals` checks recover IDs from live weak handles; new objects receive never-reused IDs. Runtime identity hashes only select a temporary dictionary bucket, never prove equality. A collected input causes a safe miss. No historical weak-key registry or `ConditionalWeakTable` capacity accumulates with revisions.

Same-compilation markup edits memoize detached capture, not semantic equivalence. The current bound snapshot holds its current detached values. Unchanged document identities can share scalar facts only while every global document identity is unchanged; changing a global import recomputes all consumers' lookup facts. Capture can share an existing detached generated-text record when the current output uses the exact source object from the corresponding bound entry, or the exact detached text already published. Every new artifact snapshot still has fresh current-key maps and no link to the prior snapshot. `DetachedSourceTextCreated` counts actual new wrappers separately from component/AKCSS-writer text creation. This prevents a normal edit from reparsing every lookup name and repeatedly flattening every unchanged generated text.

### Proof boundary and independent diagnostics

See the canonical [Hot Reload mechanism](hot-reload.md#reusing-artifacts-after-a-c-edit) for the exact supported leaf subset. The validator compares complete source text for every unchanged tree, exact declaration token arrays for the changed leaf, current paths/order, options/encoding identities and ordered PE-reference identities. It excludes the changed type from every other C# identifier set, implicit generated component/module names, and each eligible document's full positive/negative lookup-name universe. Identifier `ValueText` normalization handles escaped names. No signature hash or output hash participates in the proof.

This does not claim that arbitrary method bodies, partial classes, extension scopes or positive symbol references alone are a sufficient dependency model. Overloads/extensions, missing/ambiguous names, constants, aliases/global usings, attributes, base/target types, nullability, constraints, hooks, reference identity, parse/compiler options and file membership changes conservatively reject the proof. Global imports from another markup document belong to every consumer's lookup universe; unclassified global dependencies disable reuse for every consumer. Unsupported/custom compiler providers/resolvers also reject it. Unsupported documents stay dirty even when other documents hit. The decision states and scalar reason events distinguish validated equivalence, required rebinding, unsupported dependencies and missing artifacts.

Diagnostics have a separate missing-artifact/mapping check. Reusing source does not suppress a needed diagnostic evaluation. A source-only cached publication followed by an isolated C# edit in publish mode is explicitly tested: source generation hits, diagnostics bind and probe the current compilation. Diagnostic-only C# changes and invalid/repaired inputs are fresh-checked even when emitted source is identical. The oracle compares complete descriptor/diagnostic/location/property values using escaped JSON; only `akbura.document-version` is excluded from equality because it is a transient last-publication revision, independently checked as positive and not in the future. Provenance and all other properties remain in the oracle.

### Publication, failures and forks

All source and diagnostic work completes before detached materialization. Publication holds only a brief per-owner lock, checks cancellation, publishes the bound current snapshot, then replaces the detached publication if its reserved attempt version is newer. Candidate lookup and generation execute outside the lock. An older successful attempt can keep its own correct result but cannot overwrite a newer artifact publication. Attempt versions order starts within an owner, not a global IDE edit chronology. Incompatible forks can miss/replace the cache, but exact validation prevents poisoning another fork's output. Exceptions and cancellation leave the previous complete publication intact; interrupted identity allocation only consumes scalar IDs.

### Verification and measurements

The implementation adds independent exact-output/diagnostic and ownership tests, including 100/500 successive revisions with a fresh oracle after **every** update, cancellation during lookup/validation/preparation, out-of-order/concurrent branches, retained current requests, removed documents, schema changes, independent drivers and adversarial dependency edits. The GC tests retain only the newest store after a non-inlined session helper returns, force repeated full collections and observe obsolete objects through weak references, not full context lists. A recursive detached-value audit rejects compiler/model/syntax/symbol/operation/plan/version/delegate graphs. Cache-entry counts alone are not the proof.

The [separate implementation measurements](../benchmarks/HotReload/results/2026-10-17-artifact-reuse/README.md) record same-binary enabled/disabled latency, counters, retained heap, large Gallery lifetime/concurrency, test commands/counts and limitations. The original archives below remain historical evidence, not a mixed-configuration speedup denominator. The new [harness protocol](../benchmarks/HotReload/README.md#cross-compilation-artifact-reuse) explains the per-update oracle and GC boundaries.

In the completed paired `stats` matrix, the isolated unrelated-body median fell from 9,015.43 to 23.04 ms for Gallery and 1,951.83 to 10.59 ms for Purity. Semantic source preparation fell from 33/15 inputs to zero, along with probe/model/query work. These reductions apply to the explicit isolated-leaf proof, not arbitrary application methods. Ordinary markup medians were 243.25 to 240.72 ms for Gallery and 610.82 to 627.30 ms for Purity; the small fixed-order sample does not prove neutral tail latency. The single Purity concurrent fallback pair was slower with reuse enabled and is explicitly reported, not included in a generalized speedup claim.

All 704 primary and 214 lifetime/fork fresh comparisons passed. The 100-revision Gallery sequence kept exactly two observed compilation owners during active edits, then collected every observed compilation-bound target when only the newest store remained. One detached generation with 33 entries survived intentionally. Independent 100/500-revision package/SDK unit tests passed as well. The enabled owner's whole-process post-GC heap was approximately 51.34 MiB versus 32.49 MiB disabled in the real Gallery check: current reusable artifacts cost memory even though historical compiler graphs are not retained.

## Archived results

The [main results](../benchmarks/HotReload/results/2026-10-16-csharp-invalidation/README.md) contain the complete matrix and NeedHelp subset, compressed raw trace/diagnostic records, samples, inclusive stages, compact per-scenario callback transitions and pinned source/binary/input provenance. Separate directories retain [profiling controls](../benchmarks/HotReload/results/2026-10-16-csharp-invalidation/overhead/README.md), the [unsupported global-alias reproduction](../benchmarks/HotReload/results/2026-10-16-csharp-invalidation/global-alias/README.md) and [verification](../benchmarks/HotReload/results/2026-10-16-csharp-invalidation/verification/README.md). Runs started on October 16 and completed on October 17, 2026; the historical October 16 benchmark archive is untouched.
