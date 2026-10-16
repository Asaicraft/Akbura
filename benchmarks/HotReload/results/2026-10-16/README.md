# Hot Reload timing measurements

Date: 16 October 2026. These measurements describe the current implementation, not its development history. Only elapsed time was measured; memory metrics were not collected.

Main result: repeated property edits through real watch had median latencies of 107 ms on the small fixture, 1.249 s on NeedHelp, and 2.016 s on Sidebar. The first Purity update was substantially slower: 12.8–13.9 s. A successful minute-long CLR update was not reproduced; a full FeatureGallery rebuild took 70.8 s.

## Environment and methodology

Windows 11, build 26200; Intel Core i5-11400H, 12 logical processors. .NET SDK 10.0.401, runtime 10.0.12, SDK Roslyn 5.9.0.0, Avalonia 12.0.4. Akbura and BlackSilence were built in Debug without STATS. The three library copies loaded by the harness matched the current repository build outputs byte-for-byte.

Scenarios ran sequentially, without concurrent builds or tests launched by the harness. OS background activity was not controlled; these are local observations, not universal performance guarantees.

- CLR: 19 scenarios, each with 2 warm-up and 8 measured updates — 190 real updates to loaded assemblies in total.
- Generator: an initial pass, 2 warm-ups, and 8 measured passes. The final result is compared with a fresh GeneratorDriver.
- Watch: the first edit is recorded separately, followed by 8 repeated edits. Timing starts immediately before writing the file and ends when the new value is observed on the live control on the UI thread.
- Builds: 3 no-op repetitions per project; other conditions have one observation each.

For an even sample count, the median is the average of the two central values. p95 uses nearest rank; with n=8, it equals the maximum and does not reliably estimate rare production delays. Initial and warm-up observations remain in the CSV but are excluded from the warm-sample median.

## Real dotnet watch: file write → UI observation

All values are in seconds. Startup is measured separately, from launching watch until the application is ready; it is not included in edit latency.

<!-- BEGIN GENERATED: watch -->
| Scenario | Startup (s) | First recorded edit (s) | Measured n | Repeat median (s) | Repeat range (s) | Repeat p95 (s) |
| --- | --- | --- | --- | --- | --- | --- |
| Fixture | 8.320 | 1.760 | 8 | 0.107 | 0.078–0.184 | 0.184 |
| NeedHelp | 21.424 | 12.810 | 8 | 1.249 | 0.909–1.936 | 1.936 |
| Sidebar | 9.731 | 13.888 | 8 | 2.016 | 1.643–4.542 | 4.542 |
<!-- END GENERATED: watch -->

All 27 edits completed without restarting the application or replacing the target control. Fixture also checks user input and state; Sidebar checks the named control, foreground/state preservation, and ToggleSidebar execution followed by restoring the state. Sidebar's checks are included in its path to the observation log line. The hosts use Avalonia.Headless and 10 ms polling; physical frame presentation is not measured.

Warm-up does not end precisely after the first edit: Sidebar's second edit took 4.542 s, its third took 2.709 s, and its last four took 1.643–2.076 s. Only the first edit is excluded, as specified by the methodology; later slow updates have not been hidden.

Timestamps from **the same run**, for the eight repeated edits:

<!-- BEGIN GENERATED: watch-boundary -->
| Scenario | Matched measured n | Before send message, median (ms) | After message to UI, median (ms) |
| --- | --- | --- | --- |
| Fixture | 8 | 93.30 | 11.61 |
| NeedHelp | 8 | 1240.21 | 8.74 |
| Sidebar | 8 | 1992.08 | 24.41 |

Missing or unmatched send logs are shown as —; no boundary is inferred from independent runs.
<!-- END GENERATED: watch-boundary -->

This is a boundary observed through asynchronous logs, not the exact duration of MetadataUpdater or a handler: output delivery and polling are included. Nevertheless, most latency in these scenarios occurs **before sending** the update to the application. Subtracting the independent generator measurements below from watch latency would not yield an exact EnC duration.

## BlackSilence on real projects

FeatureGallery: 32 additional files, 28 components, 18 C# files, 190 metadata references, and 34 generated sources. The isolated Purity copy: 18 additional files, 16 components, 7 C# files, 191 references, and 17 generated sources. The user's original external project was not edited.

Times are in milliseconds. The initial pass is index=0 in a separate process for each series; median/max cover eight warm passes. The last column counts generated sources whose **text changed**, not backend operations executed again.

<!-- BEGIN GENERATED: generator -->
| Project / scenario | Initial pass (ms) | Measured n | Median (ms) | p95 (ms) | Text changed / total |
| --- | --- | --- | --- | --- | --- |
| Gallery / Counter.akbura:cold | 15119.57 | 8 | 8664.80 | 9193.91 | 0/34 |
| Gallery / Counter.akbura:csharp | 14339.04 | 8 | 8410.99 | 8626.89 | 0/34 |
| Gallery / Counter.akbura:diagnostics-off | 14411.67 | 8 | 290.12 | 369.49 | 1/34 |
| Gallery / Counter.akbura:noop | 14270.23 | 8 | 12.67 | 14.95 | 0/34 |
| Gallery / Counter.akbura:scalar | 14630.86 | 8 | 284.99 | 380.46 | 1/34 |
| Gallery / Counter.akbura:whitespace | 14031.84 | 8 | 14.61 | 15.46 | 0/34 |
| Purity / NeedHelp.akbura:csharp | 5612.73 | 8 | 2238.12 | 3224.96 | 0/17 |
| Purity / NeedHelp.akbura:diagnostics-off | 5685.31 | 8 | 343.56 | 350.46 | 1/17 |
| Purity / NeedHelp.akbura:noop | 5617.30 | 8 | 2.96 | 3.01 | 0/17 |
| Purity / NeedHelp.akbura:scalar | 6393.49 | 8 | 383.90 | 390.14 | 1/17 |
| Purity / NeedHelp.akbura:whitespace | 5966.79 | 8 | 6.17 | 9.81 | 0/17 |
| Purity / Sidebar.akbura:csharp | 5536.70 | 8 | 2191.59 | 3345.99 | 0/17 |
| Purity / Sidebar.akbura:diagnostics-off | 5627.18 | 8 | 1229.12 | 1278.28 | 1/17 |
| Purity / Sidebar.akbura:noop | 5707.28 | 8 | 2.83 | 3.00 | 0/17 |
| Purity / Sidebar.akbura:scalar | 5664.40 | 8 | 1237.91 | 1357.48 | 1/17 |
| Purity / Sidebar.akbura:whitespace | 5655.82 | 8 | 8.38 | 11.05 | 0/17 |
<!-- END GENERATED: generator -->

In the fresh-driver series, every pass starts the incremental graph again, but the process, Roslyn/JIT, and static parse cache may already be warm. A zero text diff does not mean no generation occurred: the same source set was generated again.

The C# series adds an unrelated file, `BenchmarkUnrelated.cs`, to the original compilation, with a method returning the iteration number. The method body changes between measured iterations; each compilation is derived from the original compilation rather than an IDE workspace. This is a reproducible C# environment invalidation workload, not an exact model of all internal watch/Visual Studio caches.

An ordinary property edit changes one generated source. A C# edit leaves all generated text unchanged but increases generation time to 8.411 s on Gallery and several seconds on Purity. This path needs separate profiling; its cost cannot be inferred from the number of changed `.g.cs` files. Disabling diagnostics is not a general improvement: for Counter, it did not accelerate generation within the observed variation.

A separate run reads generation-request decisions rather than inferring work from text diffs:

<!-- BEGIN GENERATED: graph -->
| Scenario | Measured n | Components | Recomputed | Reused | Diagnostic documents recomputed | AKCSS modules recomputed |
| --- | --- | --- | --- | --- | --- | --- |
| Counter.akbura:scalar | 8 | 28 | 1 | 27 | 1 | 0 |
| NeedHelp.akbura:scalar | 8 | 16 | 1 | 15 | 1 | 0 |
| Sidebar.akbura:scalar | 8 | 16 | 1 | 15 | 1 | 0 |
<!-- END GENERATED: graph -->

These counts are identical across all eight repeated edits in each series. They represent logical graph decisions, not an execution profiler. The tested ordinary edits do not broadly recompute dependent components; this finding must not be generalized to every C# environment or declaration change.

BlackSilence reported no errors in any series, and the final incremental output matched a fresh driver. The isolated compilation does not run Avalonia generators: its original CS0103 errors for `InitializeComponent` remain (2 in Gallery, 1 in Purity). Their set must stay unchanged and match the fresh pass. This is an explicit generator-only harness limitation, not a successful complete C# build; actual MSBuild builds ran separately and passed.

## CLR: generation → delta → live component

All values are in milliseconds. Stage columns show medians of eight observations. `Total` is measured independently; the sum of stage medians need not equal the median total.

<!-- BEGIN GENERATED: runtime -->
| Scenario | Measured n | Generator (ms) | Validation (ms) | Delta + CLR (ms) | Handlers (ms) | Total (ms) | Total p95 (ms) |
| --- | --- | --- | --- | --- | --- | --- | --- |
| akcss-definition | 8 | 45.57 | 24.31 | 7.01 | 0.86 | 74.84 | 100.43 |
| akcss-order | 8 | 37.71 | 28.13 | 18.71 | 2.24 | 86.22 | 132.63 |
| akcss-token-1 | 8 | 38.53 | 40.37 | 44.04 | 1.68 | 121.35 | 155.28 |
| akcss-token-100 | 8 | 296.45 | 135.19 | 129.85 | 7.81 | 567.48 | 606.31 |
| akcss-token-500 | 8 | 839.46 | 217.70 | 197.77 | 55.01 | 1324.48 | 1463.30 |
| binding | 8 | 30.35 | 15.35 | 4.86 | 1.20 | 54.28 | 64.10 |
| conditional | 8 | 32.56 | 18.66 | 18.65 | 2.40 | 73.65 | 100.79 |
| event | 8 | 30.70 | 13.92 | 12.04 | 1.93 | 65.44 | 95.73 |
| foreach | 8 | 16.48 | 12.00 | 12.26 | 1.68 | 45.88 | 53.60 |
| insert-remove | 8 | 32.66 | 14.62 | 10.45 | 2.06 | 67.81 | 75.18 |
| property-1 | 8 | 19.71 | 30.09 | 12.86 | 1.00 | 69.07 | 87.18 |
| property-100 | 8 | 344.98 | 137.89 | 105.28 | 6.87 | 612.34 | 673.93 |
| property-100-owners | 8 | 33.30 | 12.67 | 10.66 | 7.97 | 69.91 | 73.55 |
| property-10-owners | 8 | 28.54 | 18.58 | 8.33 | 2.42 | 61.94 | 99.62 |
| property-500 | 8 | 859.44 | 239.36 | 154.25 | 44.36 | 1295.20 | 1516.34 |
| reorder | 8 | 35.15 | 18.34 | 9.22 | 2.07 | 71.57 | 77.08 |
| replace-type | 8 | 30.11 | 16.13 | 9.28 | 2.30 | 61.81 | 71.37 |
| state-add-remove | 8 | 28.99 | 18.82 | 9.43 | 1.74 | 60.77 | 68.64 |
| state-initializer | 8 | 27.72 | 18.12 | 3.13 | 0.24 | 48.25 | 57.29 |
<!-- END GENERATED: runtime -->

`property-N` and `akcss-token-N` change one property on the common root of a tree with N TextBlock children, not N independent properties. Other structural scenarios use 20 children. Multi-owner scenarios update 10/100 live instances of one component, each with 20 children.

Checks cover owner/root-control preservation, the retained state value `42`, final property values, child count/order/types, the updated binding expression, and the current Click handler. The CLR helper must produce nonempty real deltas. Verification runs after the timer stops; full compilation diagnostics have their own measured stage.

`Delta + CLR` includes semantic edit discovery by the test helper, EmitDifference, and MetadataUpdater.ApplyUpdate. It is **not** the full IDE/SDK EnC pipeline. Metadata handlers are real, but the application uses Avalonia.Headless; physical screen rendering is not measured. Scenario types run in a fixed order within one process, so their JIT/cache state is not isolated. They must not be treated as independent BenchmarkDotNet comparisons.

In this set, Akbura handler time is substantially smaller than update preparation time. Both costs grow for large flat structures; a small component does not represent the update cost of every large screen.

## Builds and the module manifest

Values here are in seconds, from CLI launch to completion; restore is excluded. `Rebuild` also rebuilds dependencies but does not clear OS filesystem caches. This is not ordinary CLR delta latency.

<!-- BEGIN GENERATED: builds -->
| Condition | n | CLI median (s) | CLI range (s) | Csc task events | Manifest generation task events |
| --- | --- | --- | --- | --- | --- |
| fixture-component-edit | 1 | 4.565 | 4.565–4.565 | 1 | 1 |
| fixture-corrupt-manifest | 1 | 4.770 | 4.770–4.770 | 1 | 1 |
| fixture-csharp-edit | 1 | 4.620 | 4.620–4.620 | 1 | 1 |
| fixture-no-node-reuse | 1 | 1.282 | 1.282–1.282 | 0 | 0 |
| fixture-noop | 3 | 1.281 | 1.279–1.321 | 0 | 0 |
| fixture-rebuild | 1 | 4.811 | 4.811–4.811 | 1 | 1 |
| fixture-recovered-noop | 1 | 1.319 | 1.319–1.319 | 0 | 0 |
| gallery-no-node-reuse | 1 | 4.193 | 4.193–4.193 | 0 | 0 |
| gallery-noop | 3 | 2.528 | 2.494–2.731 | 0 | 0 |
| gallery-rebuild | 1 | 70.789 | 70.789–70.789 | 6 | 3 |

Task event counts span all builds in the row; — means task evidence was not recorded.
<!-- END GENERATED: builds -->

In every no-op case, the binlog contains manifest checks but neither `GenerateAkburaModuleManifest` nor `Csc`. Recovering the corrupted manifest did trigger generation (2.761 s inside the task), and the next no-op skipped it again. The `.cs` edit here deliberately changes only text; C# method-body changes are covered separately by the generator matrix.

During the full FeatureGallery rebuild, GenerateAkburaModuleManifest tasks took 1.043 s for Akbura, 11.697 s for Akbura.Diagnostics, and 10.495 s for FeatureGallery. Their respective Csc tasks took 20.529, 5.069, and 11.665 s. These are task intervals from the binlog, not independent end-to-end measurements; they cannot be arbitrarily added or subtracted during a parallel build. Manifest checks in the third no-op took 332, 360, and 102 ms respectively.

A minute-long full rebuild was reproduced. This alone does not prove that a particular user edit causes a rebuild: establishing that connection requires the log from the affected watch/IDE run.

## Investigating delays of up to a minute

1. Use verbose logs to distinguish actual Hot Reload from a rebuild/restart. Full rebuilding already measured around a minute; the tested CLR edits did not.
2. If the delay precedes `Sending update batch`, profile generation, the C# environment, and EnC. There is a reproducible expensive case here: 8.4 s of Gallery generation after a C# edit, despite unchanged generated output.
3. Account for semantic processing costs in large components: Sidebar requires 1.24 s of generation versus 0.38 s for NeedHelp. Runtime reconciliation also becomes noticeable for large flat trees.

These are investigation directions supported by measurements, not a diagnosis of every minute-long IDE stall. Independent generator-only, CLR-helper, and watch measurements cannot be combined as stages of one trace.

## Artifacts and reproduction

- [Harness instructions and source](../../README.md).
- [All observations, including warm-ups](samples.csv).
- [Summary statistics](summary.csv).
- [MSBuild task intervals](build-tasks.csv).
- [Initial passes and startup](startup.csv).
- [Generator validation](validation.csv).
- [Delta-send and UI-observation boundaries](watch-events.csv).
- [Dependency graph decisions](graph.csv).
- [Environment for each run](environment.json).

Full local logs and binlogs are in the ignored `bin/HotReloadBenchmarks` directory. The repository contains portable CSV files and run conditions rather than absolute links to machine-specific artifacts.

<!-- BEGIN GENERATED: coverage -->
| Series | All observations | Measured observations |
| --- | --- | --- |
| runtime | 190 | 152 |
| generator | 176 | 128 |
| watch | 27 | 24 |
| build-wall | 14 | 14 |

Primary observations: 407. Graph observations: 33. Fresh-driver parity: 19/19 passed. Counts do not imply complete scenario coverage.
<!-- END GENERATED: coverage -->

This is a finite matrix: Visual Studio/Rider, Release, physical GPU/screen presentation, templates and every resource/binding-priority variant, network delays, cold disks, concurrent edits, and rude edits are not covered. Unmeasured conditions are not treated as zero-cost or successfully verified. The full functional test suite was not run: this is a separate timing harness with its own result checks.
