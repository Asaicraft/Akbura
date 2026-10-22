# Watch memory attribution and real-workload latency

Status: **matched 100-edit memory matrix complete; [summary and preliminary retention review](channel-recovery/validated/summary/README.md) available**. All 36 independent sessions and 3,600 measured live updates completed. The report repair reused the archived evidence without rerunning measurements. No production implementation or experimental default has changed. Exclusive retention attribution, any selected longer extension and the full heavy-component performance comparison remain separate work.

The [checkpoint-channel recovery](channel-recovery/README.md) preserves the earlier I/O repairs and measurement provenance. The final controller completed every session and failed only in report generation; its old failed journal is intentionally preserved. Use the [successful postprocessing status](channel-recovery/validated/summary/status.json) and [new report journal](channel-recovery/validated/summary/journal.log) for the repaired summary outcome.

Starting reference: `53b4e3d25a3985325ab0446df0111d9a684086ce`. Earlier [semantic reuse](../2026-10-21-semantic-reuse/README.md) and [live-watch validation](../2026-10-22-semantic-watch-validation/recovery/README.md) are historical evidence, not new executions. Their archives and compiler binaries remain unchanged.

## Reproduction and progress

For report-only reproduction from the completed archives, use the commands in the [reviewed summary](channel-recovery/validated/summary/README.md#reproduce-the-report-without-rerunning-measurements). The old measurement controllers are not running. Starting a new measurement gate requires a fresh output directory and source provenance; no new gate is necessary to reproduce this report.

The launcher creates a hidden background controller. It writes flushed UTC journals, atomic status, per-step logs, source fingerprints, binary hashes and success markers. Re-running with identical inputs resumes completed sessions, including restoring their archive. An interrupted partial session is preserved and replaced by a fresh session, never spliced into a success. A live previous owned child prevents duplication. Foreign processes are not stopped. This controller is deliberately a **memory evidence gate**, not an automatic acceptance or next-optimization launcher.

Diagnostic tools are installed under `bin/WatchMemory/tools`, not globally: `dotnet-dump`, `dotnet-gcdump`, `dotnet-counters` and `dotnet-trace`, pinned to `10.0.745401`. Offline graph analysis uses exact NuGet package `Microsoft.Diagnostics.Runtime 4.1.745802`; its assembly version is separately recorded. All four compiler pairs are byte-for-byte copies of the previous frozen evidence; only the common benchmark host is rebuilt. Current compiler/test sources must match the original compiler source manifest before reuse. No earlier test run is counted again.

## Staged experiment

| Compiler variant | Observer | Purpose |
| --- | --- | --- |
| Baseline DebugStats | On | Previously missing matched recorder counterfactual |
| Combined DebugStats | On | Reproduce prior observed growth |
| Baseline Release, non-STATS | Off | Production compiler control |
| Combined Release, non-STATS | Off | Production compiler control |
| Baseline DebugStats | Off | Separate recorder overhead from STATS build effects |
| Combined DebugStats | Off | Same binary as recorder-on, recorder disabled |

Each configuration has three independent 100-edit sessions in **each** of two separate modes: natural GC and checkpoint-forced GC. All 36 sessions and 3,600 measured edits completed, in addition to two warmups per session. Variant order reverses between rounds. Checkpoints are at startup, after warmup, and measured revisions 1, 10, 25, 50, 75 and 100.

The host reuses the supported same-width Margin fixture and its strict UI acknowledgment protocol. It pauses **after** a verified UI update and after causal telemetry processing. The external runner then identifies the actual compiler/watch host and application through owned PID ancestry, executable paths and command lines. It does not mistake the application or the `dotnet watch` launcher for the compiler heap. Only after the checkpoint succeeds does editing resume.

Natural sessions do not explicitly force GC, including their final UI check. Forced sessions induce full GC only at the predetermined checkpoints with external gcdump collection for the compiler/watch host and application. The old stress behavior of requesting collection on every observed generation is not enabled. All these sessions contain diagnostic collectors and checkpoint pauses, so their timing samples are **not primary production latency evidence**.

The first forced session of each configuration additionally captured compiler/watch heap dumps after warmup, revision 50 and revision 100. All 18 full profiles completed; raw dumps and gcdumps remain local in the original session directories under `bin/WatchMemory`, linked by per-session provenance. Full dumps, raw process-memory values and trace images are never copied to Git. No 500-edit session was launched.

## Measurement meanings

Continuous external .NET runtime counters run against the actual compiler/watch host, application and distinct managed launcher. They expose last-GC heap, committed bytes, fragmentation, LOH/POH sizes, assembly counts, allocation rate and GC collection rates. Per-checkpoint process metrics separately record CPU, working set and private bytes. Where a metric is unavailable, it must remain unavailable; rate observations are not mislabeled as exact lifetime totals.

Compiler/watch natural-GC heap is the runtime's **last-collection heap less reported fragmentation**, not a fresh full-GC live-object measurement. A zero last-GC value can mean no collection was observed, not an empty process. Application natural samples also retain the existing native UI acknowledgment's `GC.GetTotalMemory(false)`, cumulative allocated bytes and GC counts. Forced live graph byte/object counts come from gcdump. These different meanings are explicit in `memory-series.csv`; natural and forced measurements have separate plots and are not pooled.

Counter CSV timestamps use OS local wall clock at one-second resolution. Checkpoints use UTC. The summary records the nearest sample's skew and rejects missing or distant samples. This approximate memory-sample join is **not** used to subtract process clocks for latency stages. Existing edit latency remains measured on the orchestrator's single monotonic clock from completed file write to validated acknowledgment; profile pauses are outside that interval.

Offline ClrMD traversal begins at strong and finalizer roots and follows managed references, including dependent handles. It exports root-reachable type counts, shallow byte histograms and representative shortest retaining-type paths. Field names are explicitly unresolved in that traversal. Shallow bytes are not exclusive owner sizes, reachability is not proof of obsolete lifetime, and a path through a legitimate live owner is not automatically a leak. Local address indexes are retained only to allow targeted SOS `gcroot` follow-up; addresses and object contents are not exported. Significant positive type deltas must be examined before attributing growth.

The report generator writes `memory-series.csv`, `memory-summary.csv`, `retained-types.csv` and separate natural/forced SVG plots. Slopes use all checkpoint observations, with early and late windows, rather than inferring a linear leak from two endpoints. Between-session variation and ownership review are still required. Working-set growth alone is never the managed-retention oracle.

## Preparation evidence, not accepted matrix results

- A two-edit Baseline Release natural-GC pilot passed the UI protocol and external counter/process-topology collection.
- A one-edit Combined DebugStats forced-GC pilot passed collection and an offline warmup heap traversal. The traversal inspected approximately 550,000 root-reachable objects in a few seconds and exported sanitized graphs. These pilots are not 100-edit sessions or new production performance results.
- Synthetic report guards validate checkpoint sequences, slope calculation, UTC/local counter joins and rejection of incomplete sessions. Relevant compiler correctness evidence remains the original 862 filtered executions; no compiler code changed in this investigation.
- A copied **real Purity Sidebar** was built and exercised through the existing live host. The tutorial submodule remains clean. Its preparation adds only a root `Margin="10"` before initial compilation, rather than substituting a synthetic component with the same name. Source revision, original/prepared hashes and inventory are recorded under [feasibility](performance/feasibility).

The real Sidebar feasibility run completed nine verified live updates (one initial warmup), preserving the owner, target control, named dashboard, state and operable ToggleSidebar commands. Its legacy-protocol first update took about 20.5 seconds and subsequent updates approximately 2.2–6.6 seconds. This is useful diagnostic feasibility evidence, not a frozen multi-round production comparison, not a completed-write-boundary comparison with the small fixture, and not proof of a particular generator/EnC/runtime bottleneck. The full supported/fallback/C#-change heavy matrix and stage attribution remain pending.

## Pending engineering review

| Question | Current classification |
| --- | --- |
| Is semantic reuse responsible for revision-proportional growth? | Not supported as the primary cause: matched post-GC growth is comparable with reuse disabled. |
| Is recorder/STATS overhead responsible? | Not supported as the primary retained-growth cause; non-STATS and same-binary observer-off controls also grow. |
| Which owner retains the additional memory? | SDK/Roslyn EnC baseline paths retain representative older compilations; exclusive byte attribution remains unresolved. |
| Does the heap plateau? | No plateau observed by 100 edits; longer runs remain untested. |
| What dominates large-component Hot Reload? | Real workload is feasible and slower; stage boundaries are not yet attributed. |
| Which optimization should follow? | Not selected before measurement; no next optimization is implemented. |

After the memory gate, review roots and differences, then choose only justified extensions and collect separate non-STATS heavy/small production timings and stage traces. If an Akbura retention defect is identified, require its actual strong-reference path, failing test, minimal fix and parity/performance revalidation. Do not broaden semantic eligibility, clear live caches, change ownership rules or enable experimental defaults.

## Tool references

Microsoft documents that [gcdump induces a full GC and reconstructs a heap graph](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-gcdump); collection can perturb the target and is therefore outside production timing. [dotnet-dump](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-dump) supports offline heap and root analysis. [Runtime metric definitions](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/built-in-metrics-runtime) distinguish committed bytes, last-collection heap and allocation counters. The actual installed tool help, loaded runtime and binary hashes are recorded rather than assuming current upstream source matches the executing SDK.
