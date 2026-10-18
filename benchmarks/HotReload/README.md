# Hot Reload timing benchmark

This harness primarily measures elapsed time. The opt-in C# invalidation suite also records allocation and lifetime observations. It separates three different paths:

- `dotnet watch`: immediately before writing an edit to observing its value on the live UI thread. The headless host polls every 10 ms. The same process and target control must survive; the fixture also checks component state and user input.
- BlackSilence: `RunGeneratorsAndUpdateCompilation` over real evaluated project inputs. Generated-text changes and incremental step reasons are logged. These runs do not include MSBuild or Roslyn Edit and Continue.
- CLR integration: generation, compiler diagnostic validation, semantic edit discovery plus delta emission/application, and the real generated metadata handlers. This uses the repository's CLR test helper, not the SDK's complete Edit and Continue pipeline. It is not a substitute for `dotnet watch` latency.

The build suite measures CLI wall time and reads actual task intervals from MSBuild binlogs. It includes rebuilds, no-op builds, a new MSBuild node, component/C# edits, and recovery after a damaged manifest. Rebuild is **not** a cold filesystem-cache measurement. `-nr:false` disables MSBuild node reuse, not every compiler or OS cache.

The generator-only compilation does not run other projects' source generators (for example Avalonia's `InitializeComponent` generator). Existing compiler-error keys are logged and must remain unchanged; BlackSilence itself must report no errors. The last incremental output and compiler-error set must also match a fresh BlackSilence driver. This is output parity, not a claim that the isolated compilation replaces the full successful MSBuild build.

## Run

Requirements: Windows, PowerShell, Git, the repository's .NET 10 SDK and package restore access. Commands below run from the repository root. No IDE automation is used.

```powershell
git submodule update --init -- benchmarks/HotReload/Akbura-PurityUI-Tutorial
powershell -NoProfile -ExecutionPolicy Bypass -File benchmarks/HotReload/Run.ps1 -Prepare
powershell -NoProfile -ExecutionPolicy Bypass -File benchmarks/HotReload/Summarize.ps1
```

`-Prepare` builds the current Debug runtime, diagnostics, build tasks, BlackSilence and this executable. Debug is intentional: structural Hot Reload requires the generated debug contract. The benchmark loads the SDK's Roslyn assemblies, so record the SDK version when comparing machines. Run suites sequentially, without concurrent builds or tests. The fixed scenario order shares process/JIT state inside the CLR suite; this is a latency probe, not a statistically isolated BenchmarkDotNet comparison.

Use `-Suite runtime`, `-Suite generator`, `-Suite graph`, `-Suite build`, or `-Suite watch` to run a subset. The graph suite separately inspects the generation request's recompute/reuse decisions for a scalar edit; it is not an instrumentation-based count of executed methods. Outputs default to ignored `bin/HotReloadBenchmarks`; `-OutputDirectory` overrides that location. The executable references built repository assemblies, so rerun `-Prepare` after changing implementation code. The runner copies the watch fixture into its output directory and restores temporary edits byte-for-byte in `finally` blocks. Do not terminate it forcibly while an edit is in progress.

## Purity tutorial submodule

The [Akbura-PurityUI-Tutorial](https://github.com/Asaicraft/Akbura-PurityUI-Tutorial) repository is a Git submodule at `Akbura-PurityUI-Tutorial/`. This repository tracks its URL and a specific commit, not a vendored copy of its files. Clone with `git clone --recurse-submodules` or initialize it with the command above. Use `git submodule update --init` to check out the recorded revision; the runner does not fetch or follow upstream automatically.

Generator, graph and watch suites include Purity by default. `Prepare-Purity.ps1` exports the tutorial's checked-out committed revision with `git archive` into a uniquely named directory under the ignored output directory. Only that disposable snapshot is adapted: Akbura NuGet references are replaced with the current local Debug runtime/diagnostics/analyzers/build targets, and the tracked `PurityHost` adapter is added. The host is restored and built before measurement. Its output validates live `NeedHelp` and `Sidebar` updates. The submodule checkout is never edited or built, including during watch file edits.

Only the shared dashboard, ViewModels and headless host are built; mobile/browser workloads are not required. Use a fresh `-OutputDirectory` for a self-contained archived run: reusing an output directory retains logs from suites that were not rerun.

The selected source revision and prepared path are recorded in `purity-source.json`. `Summarize.ps1` includes the repository URL and revision in its portable results, without the machine-specific prepared path. Snapshots and archives remain in the output directory for inspection. Modified tracked files in the source checkout cause an explicit error rather than silently benchmarking different content. Untracked source files are not exported.

Use `-SkipPurity` to run only the built-in fixture and FeatureGallery, without initializing the submodule. `-PurityRoot <path>` optionally selects another clean tutorial Git checkout; it now accepts source repositories, not manually adapted copies. Both the default and override go through the same automatic preparation.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File benchmarks/HotReload/Run.ps1 -Suite watch -Prepare
powershell -NoProfile -ExecutionPolicy Bypass -File benchmarks/HotReload/Run.ps1 -Suite watch -SkipPurity
powershell -NoProfile -ExecutionPolicy Bypass -File benchmarks/HotReload/Summarize.ps1 -OutputDirectory benchmarks/HotReload/results/local
```

`WatchFixture.csproj` can also be run directly. The runner puts the absolute repository root into its isolated copy so custom output directories resolve the same runtime and build tasks.

## C# invalidation investigation

`-Suite invalidation -Prepare` builds local `DebugStats` analyzers and runs the C# edit matrix over FeatureGallery and a prepared Purity snapshot. It preserves the older suites and their `csharp` scenario. It records actual statistics, structured cache-availability/invalidation observations, public Roslyn tracked steps, full diagnostic/source parity, caller/process allocations and independently timed driver wall time. See the [investigation](../../For%20Contributors/csharp-invalidation-investigation.md) for boundaries and correctness limitations.

```powershell
pwsh -NoProfile -File benchmarks/HotReload/Run.ps1 -Suite invalidation -Prepare -OutputDirectory bin/CSharpInvalidationInvestigation/matrix
pwsh -NoProfile -File benchmarks/HotReload/Summarize-Invalidation.ps1 -InputDirectory bin/CSharpInvalidationInvestigation/matrix -ResultDirectory benchmarks/HotReload/results/local-invalidation
```

Use `-InvalidationScenario unrelated-body,akbura,unchanged` for a subset, `-InvalidationProfiling off,stats,trace` for profiler-overhead modes, `-InvalidationPurityFile NeedHelp.akbura,Sidebar.akbura` to select Purity targets, or `-SkipPurity` for Gallery only. `off` means no session in the same stats-enabled binary, not a non-STATS production build. Timing runs must not overlap builds/tests. Do not overwrite the historical results directory.

Pass array options by invoking the script inside PowerShell 7, for example `& ./benchmarks/HotReload/Run.ps1 -Suite invalidation -Prepare -InvalidationScenario @('unrelated-body', 'unchanged') -InvalidationProfiling @('off', 'stats', 'trace')`. Do not forward comma-separated array values through the native `pwsh -File` argument boundary.

The separate summarizer exports compressed raw JSONL, samples, stage timings/allocations, tracked-step transitions, causes, provenance and medians/range/nearest-rank p95. It validates two warm-ups/eight measured samples and two final fresh-driver parity checks per group. Its README update replaces only its own marked block, preserving manual notes. Stage timings/allocations are inclusive and must not be summed.

`Verify-SdkRoslyn.ps1 -Prepare` runs the instrumentation/statistics checks in an isolated copy of the existing test output with the active SDK's Roslyn assemblies. It leaves the original test output and repository package versions unchanged and records assembly hashes, versions, host-resolution evidence and TRX results. This complements the ordinary package-based unit-test host; run it after benchmarks, not concurrently with timing samples. The verification and invalidation-report scripts require PowerShell 7.2 or later.

## Cross-compilation artifact reuse

The `artifacts` suite runs the existing C# invalidation matrix with `AkburaCrossCompilationReuse=false` and `true` on identical evaluated inputs. Modes are paired per scenario. It uses the same `DebugStats` binary for both: `off` disables measurement collection, not instrumentation call sites. Use `stats` for counters without full trace events or `trace` for reasons. The optimization is a conservative isolated-leaf proof, not a general method-body shortcut; see [the mechanism](../../For%20Contributors/hot-reload.md#reusing-artifacts-after-a-c-edit).

```powershell
& ./benchmarks/HotReload/Run.ps1 -Suite artifacts -Prepare -OutputDirectory bin/CrossCompilationReuse/matrix -InvalidationProfiling stats
& ./benchmarks/HotReload/Summarize-Invalidation.ps1 -InputDirectory bin/CrossCompilationReuse/matrix -ResultDirectory benchmarks/HotReload/results/local-artifact-reuse
& ./benchmarks/HotReload/Run.ps1 -Suite retention -RetentionRevisions 100 -OutputDirectory bin/CrossCompilationReuse/lifetime
```

The default matrix includes unchanged inputs, unrelated/referenced bodies, whitespace/comments, signatures, overloads, constants, attributes, global usings, references, files added/removed, unresolved/repaired code, repeated/reverted edits, options and markup. `-ArtifactOptimization disabled,enabled` selects modes (pass arrays from within PowerShell, as above). `retention` also forks two concurrent real-project driver branches. This opt-in suite is not part of the default timing run.

Every artifact-matrix update, including initial/warm-up runs, is compared with a cache-disabled fresh driver. The oracle compares all generated hint names and characters, compiler/generator diagnostics, descriptor fields, mapped/primary/additional locations and every diagnostic property except the transient `akbura.document-version` stamp. Stamps are independently checked as positive and not newer than the current request; a cached publication can legitimately retain an older publication stamp. JSON serialization makes property/message comparisons unambiguous. Fresh generation is verification only, never the runtime reuse algorithm, and runs outside the recorded interval.

The raw rows/CSVs include wall time, actual semantic preparation/model/probe/query/emission/text counts, validated hits/fallbacks, validation/capture timings, allocations and post-GC heap/working set. Stage measurements are inclusive; never add nested times to estimate a wall-time saving. Forced GC and verification also run outside timed intervals, but affect subsequent heap/JIT state equally in both modes. The summary requires all 11 fresh checks and all 11 post-GC observations per artifact group; legacy invalidation runs keep their two-final-check protocol. Report generation preserves manual prose.

`artifact-memory <inputs.json> <file> <revisions> <enabled|disabled>` is the executable counterpart of `-Suite retention`. It performs a fresh oracle on every revision, records heap after forced full GC, then releases every driver/compiler inside a non-inlined helper while deliberately retaining the newest artifact owner. Weak observations cover C# and Akbura compilations, indexes, bound/detached snapshots, created semantic models and probe contexts. The final row asserts that no compilation-bound target remains and at most one distinct detached generation survives. Active-driver heap samples and this isolated-owner proof answer different questions. Working set includes the GC allocator/native runtime and is not evidence of a retained compiler graph by itself.

The unit tests independently cover 100/500 revisions, canceled attempts, stale/concurrent publications, removed artifacts and request lifetime. To repeat them under SDK Roslyn without changing repository packages:

```powershell
& ./benchmarks/HotReload/Verify-SdkRoslyn.ps1 -Prepare -TestFilter 'FullyQualifiedName~InvalidationTrace_|FullyQualifiedName~BlackSilenceGenerationStatisticsTests|FullyQualifiedName~DetachedArtifacts_' -ExpectedTestCount 61
```

## Real C# member contracts and autonomous runs

The real-member scenarios insert a multi-member `UserViewModel`, a separate service, fields/properties and several methods into the real evaluated Gallery/Purity inputs. Markup directly calls the instance through its current ViewModel/service. `real-method`, `real-referenced-method`, `real-service`, `real-property`, `real-multiple`, `real-mixed`, `real-signature` and `real-overload` distinguish accepted bodies, simultaneous markup changes and mandatory fallback. This is not another isolated static-leaf fixture.

The [recorded real-member results](results/2026-10-17-member-contracts/README.md) include paired timings/counters, repeated concurrent forks, isolated-owner GC observations, 100/500-revision test evidence and a real C# `dotnet watch` transcript. Generator-only savings and live application latency are reported separately.

Prepare once, then start the detached controller from PowerShell 7:

```powershell
& ./benchmarks/HotReload/Run.ps1 -Suite artifacts -Prepare -FreshCompiler -ArtifactOptimization @() -OutputDirectory bin/MemberContracts/inputs
& ./benchmarks/HotReload/Start-ContractBenchmarks.ps1 -InputDirectory bin/MemberContracts/inputs -OutputDirectory bin/MemberContracts/run
Get-Content -LiteralPath ./bin/MemberContracts/run/journal.log -Tail 20 -Wait
```

The launcher returns immediately. The worker survives closing the chat, freezes its tool closure, hashes evaluated sources/references, writes raw stdout with immediate flush, saves stderr separately and updates `status.json` atomically. The journal includes checked revisions and a 15-second process/CPU heartbeat, not only scenario boundaries. Other CLI build/test activity pauses startup; activity detected during a group invalidates that partial group. Do not build, test or edit measured project inputs while it runs. Closing the log viewer with Ctrl+C does not stop the worker. The computer must remain awake.

Use the same `-OutputDirectory ... -Resume` to continue after a failure. Complete groups are checked before reuse, partial logs are preserved under `interrupted/`, and changed frozen inputs require a new run rather than silently mixing implementations. A second controller cannot acquire the run lock. Default reports go to the run's `report/`; `-ResultDirectory` selects a separate portable archive. `status.json` reports `Complete` or `Failed` with the current job and failure text. Send that directory back for analysis; there is no reason to keep a chat polling for hours.

Before the main matrix, a separate Gallery preflight checks a referenced real-member edit, three concurrent fork pairs, fresh-generation parity and owner-only collection. Its logs stay under `preflight/` and are excluded from reported timing groups. Scenario names are restricted to the supported matrix, and output cannot be placed inside the tool directory being frozen.

The default queue pairs 11 scenarios in two projects/two modes (44 primary groups), then four real-member lifetime groups: Gallery 100 revisions per mode, Purity one revision per mode, each followed by eight repeated two-branch concurrent observations. `-RetentionRevisions` and `-ConcurrentSamples` adjust those counts; concurrency requires at least three observations. Summary exports include declaration construction/validation stages and `artifact-concurrent-summary.csv` medians/ranges/counts. Inclusive stages must not be summed. Inputs must stay unchanged between preparation and resumed groups.

After generator measurements, the worker runs `Verify-MemberWatch.ps1` in an ignored disposable fixture with the local analyzers. It edits `MemberViewModel.GetName()` nine times under real `dotnet watch` and checks the original process/control, updated method result/displayed value, user input and unrelated state. A 10 ms application tick drives ordinary rendering; this does not extend runtime Hot Reload type selection to arbitrary services. Raw native detection logs and post-delta metadata callbacks are timestamped. Exact EnC generator-processing and delta-submission intervals are unavailable and remain null, not inferred from generator-only timings. `-FreshCompiler` only affects preparation builds, not measured generator-driver calls.

## Partially generated reports

`Summarize.ps1` writes the CSV artifacts and then calls `Update-Report.ps1` to create or update `README.md` in the results directory. Watch timings and send boundaries, generator/CLR/build tables, graph decisions, observation/parity counts, and available tutorial provenance are generated from recorded artifacts. Missing suites are shown as unmeasured, not as zero-cost results. Variable graph counts are displayed as ranges.

New reports start with an English scaffold. Machine-specific environment notes, methodology, interpretations and limitations remain manual. Existing reports are updated only between paired HTML comments such as `<!-- BEGIN GENERATED: watch -->` and `<!-- END GENERATED: watch -->`; all text outside marked blocks is preserved. Report-specific selections may omit blocks. Unknown, duplicate, nested or unpaired markers fail without replacing the report. An existing unmarked README is never overwritten automatically.

Archived CSV results can be rendered without rerunning any benchmark:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File benchmarks/HotReload/Update-Report.ps1 -ResultDirectory benchmarks/HotReload/results/2026-10-16
powershell -NoProfile -ExecutionPolicy Bypass -File benchmarks/HotReload/Test-Report.ps1
```

The update is deterministic, uses invariant numeric formatting and preserves CRLF. Always review manual conclusions after changing the data: generated tables do not update prose or prove that the full benchmark matrix completed. The regression checks exercise the archived measurements, manual-text preservation, idempotence, incomplete datasets, graph ranges, Markdown escaping, malformed markers and the automatic summarizer integration.

## Sampling and boundaries

The CLR suite performs two warm-up updates and eight measured updates per scenario. Generation uses one initial pass, two warm-up passes/edits and eight measured passes. Watch records the first edit separately and then eight measured edits. Raw CSV retains warm-ups. Summary median uses the middle pair for even sample counts; p95 uses nearest rank, so with eight observations it equals the maximum. Do not interpret it as a reliable production tail-latency estimate.

The `cold` generator scenario creates a fresh `GeneratorDriver` each time **inside one process**. Roslyn/JIT and process-wide parse caches can already be warm. Its index-zero sample is the first driver run in that process; neither number represents a freshly booted machine or a cold disk cache.

CLR scenarios cover scalar properties and AKCSS tokens at 1/100/500 child controls, insertion/removal, reordering, control-type replacement, bindings, event closures, conditionals, foreach, state initializers and state addition/removal, AKCSS definitions/order, and 1/10/100 live owners. Tree-size cases change one property on the common root; they do not change 500 independent properties. Other structural cases use 20 children. Verification runs after the measured interval; compiler diagnostics are timed explicitly inside it.

This finite matrix does not claim to enumerate every program, unsupported edit, binding priority, template, resource, operating system, or IDE. Physical screen presentation, Visual Studio/Rider, production Release code, concurrent IDE editing, network/restore latency and cold disk caches are not measured. The retention suite's concurrent generator-driver forks are not a measurement of IDE scheduling. Unsupported/rude edits and failures must not be reported as successful timing samples. Functional regression tests remain separate from this latency harness.

Checked-in runs:

- [2026-10-16 results](results/2026-10-16/README.md)
- [C# invalidation investigation and profiling controls](results/2026-10-16-csharp-invalidation/README.md)
- [Bounded cross-compilation artifact reuse](results/2026-10-17-artifact-reuse/README.md)
