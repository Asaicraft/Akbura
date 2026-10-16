# Hot Reload timing benchmark

This harness measures elapsed time, not memory. It separates three different paths:

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

This finite matrix does not claim to enumerate every program, unsupported edit, binding priority, template, resource, operating system, or IDE. Physical screen presentation, Visual Studio/Rider, production Release code, concurrent editing, network/restore latency and cold disk caches are not measured. Unsupported/rude edits and failures must not be reported as successful timing samples. Functional regression tests remain separate from this latency harness.

Checked-in runs:

- [2026-10-16 results](results/2026-10-16/README.md)
