# Real Purity Sidebar Hot Reload critical path

Status: **measurement matrix complete and reviewed; default-performance gates not met**. Completed at `2026-10-22T12:31:52Z`. Starting revision: `15d850ae1f0fef104aa77be3743cbc6f2ba0b2cd`. This investigation changes benchmark/STATS observability only, not production semantic behavior, cache eligibility, cache ownership or defaults.

All 32 independent primary sessions completed: 600 supported and 240 fallback updates. Binding improved supported latency by an estimated 2.68% (95% session-cluster interval -0.57% to +6.54%); Combined by 3.29% (-1.53% to +7.50%). Neither demonstrates the preregistered improvement. Combined does not demonstrate superiority over Binding. Both remain opt-in.

The diagnostic trace now directly measures the correlated Roslyn GeneratorDriver interval: approximately 0.75-0.85 seconds per warmed supported edit, versus approximately 2.1 seconds to verified UI acknowledgment in the separate instrumented sessions. Within Akbura, semantic binding dominates; planning takes 11-18 ms, emission about 5 ms, dependency capture below 1 ms and binding lookup below 0.5 ms. These data do not justify prioritizing the proposed planner/using-scope/lookup optimizations for end-to-end latency. The remaining watch time is not a direct EnC measurement.

See the [review and four-variant decision table](review.md), [generated production measurements](measured-summary.md), [session summaries](session-summary.csv), [confidence intervals](comparison.csv) and [per-revision diagnostic observations](stage-observations.csv). Unmeasured allocation, full parity, direct EnC and new memory gates remain explicitly unavailable.

## Run and progress

```powershell
pwsh -NoProfile -File benchmarks/HotReload/Verify-PurityCriticalPath.ps1
Get-Content bin/PurityCriticalPath/gate/journal.log -Tail 20 -Wait
Get-Content bin/PurityCriticalPath/gate/status.json
```

The hidden controller writes flushed UTC heartbeats, current owned child PID, per-step logs, input/closure hashes and success markers. Run the same command to resume identical inputs; completed groups are reused, partial groups get fresh directories. Preserve old gates if sources or protocol change. Do not edit benchmark/compiler sources during measurement. The controller never kills foreign tests/builds; primary sessions wait for them and reject contaminated timing samples. A test failure or incomplete trace stops the gate instead of silently accepting it. See [commands](commands.md).

The current source passed eight focused DebugStats tests (two new telemetry fresh-source/diagnostic-parity cases and six existing statistics tests), plus synthetic trace and session-cluster statistical guards. These are targeted executions, not the full correctness/default acceptance suite. Functional pilots under `preflight` passed real supported, changed-expression and relevant C# updates; they are explicitly excluded from the primary performance sample set. In the supported Combined pilot, warmed updates recorded 15 actual first bindings/probe compilations/models skipped with matching contribution imports. The C# compilation-transition pilot recorded no semantic hits. Those small pilots are not performance claims.

## Preregistered experiment and acceptance thresholds

Four variants: Baseline, Projection, Binding and Combined. Primary sessions use the original frozen non-STATS Release compiler binaries; the common benchmark parent/UI verifier is rebuilt and separately hashed. Diagnostic sessions use freshly built STATS binaries with the same compiler algorithms plus gated observability. They are not production timing samples.

- Supported root-Margin edit: five independent sessions per variant, 30 measured updates and three warmups, for 600 measured supported updates.
- Unsupported literal fallback: three independent sessions per variant, 20 measured updates and three warmups, for 240 measured fallback updates.
- Separate instrumented controls: supported, literal fallback, changed expression and changed C# declaration contract for each variant. Five measured edits and three warmups per session. These validate workload causality, not the production confidence interval.
- Variant order rotates/reverses across rounds. Contaminated timing sessions remain preserved and are not accepted. Foreign test/build processes are never terminated.
- Primary statistic: equal-weight geometric ratio of independent session medians of completed-file-write-to-verified-UI-acknowledgment latency. Use 10,000 deterministic session-cluster bootstrap draws and 95% intervals. Never treat 150 edits as 150 independent watch processes.

Thresholds are fixed before the primary run: Binding must improve supported latency by at least 5%, with its 95% improvement interval entirely above zero. Fallback point regression must be at most 2%, with the interval excluding regression worse than 5%. Combined must additionally improve over Binding by at least 2%, with its interval entirely favoring Combined. These are proposed decision gates, not a statement that any variant passes. Fresh source/diagnostic/dependency parity, affected suite/SDK/concurrency/cancellation gates, real GeneratorDriver regression limits and memory ownership validation remain separate prerequisites before any default decision.

The proposed future 500-revision memory budget is Combined-minus-Baseline forced-GC retained-growth overhead of at most +5 MiB, with bounded semantic graphs and collectible old compilation-bound owners. No 500-revision session is started by this benchmark-only controller. Likewise, an inconclusive first matrix does not automatically enable a feature or launch another optimization; a separately recorded eight-session/40-update extension may be justified with the same frozen compiler set.

## Workload fidelity

Use the committed `Akbura-PurityUI-Tutorial` submodule without modifying its checkout. Each independent session exports a clean revision into a fresh directory. The supported and fallback preparations add only `Margin="10"` to the real Sidebar root Panel. Fallback changes its existing Dashboard text literal. The expression control uses a root Margin expression, recorded explicitly. The C# control uses one prepared C# getter referenced by the root Margin; edits change the getter value and add a const declaration, forcing a semantic declaration-contract transition without changing an existing CLR signature. Those two controls are not mislabeled as an unchanged original Sidebar.

The host verifies the visible expected value, original owner/root, named Dashboard control, nested component identities, state, foreground and two operable ToggleSidebar calls on every acknowledgment. Validation cost is included and separately recorded. Prepared source hashes, generated source inventory, runtime/task hashes and analyzer closures are saved per session. Cold build/startup and warmups are not measured samples. No full GC, heap dump, counter collector or EventPipe session runs in primary production sessions.

## Clocks and observed stages

The parent uses one monotonic clock for source-write completion, received watch log markers and received verified UI acknowledgment. Log-marker intervals include transport/logging delay and are explicitly not exact CLR or EnC boundaries. Application validation duration is measured in the application; its allocation/heap counters are never substituted for compiler allocation/retained-size measurements.

Separate diagnostic sessions attach EventPipe to the actual owned compiler/watch process. The installed SDK's `Microsoft-CodeAnalysis-General` manifest exposes paired GeneratorDriver and single-generator events with IDs. An `Akbura-CriticalPath` marker, emitted inside the actual STATS generator callback, carries the revision and SHA-256 of the verified compilation input. A completed Roslyn span is assigned to a revision only if it encloses exactly one unique matching revision/hash marker in that **same trace-local clock and PID**. Unmatched, foreign, ambiguous or truncated spans are not guessed from callback delivery time. The collector drains before watch exits.

The observed Akbura request/generated-batch scopes export all existing stage counters/timings, including projection/binding lookup, probe derivation/model acquisition, semantic dependency capture/detachment, planning, C# emission and SourceText creation. Stage durations are inclusive and may overlap; adding them is not a valid exclusive critical-path partition. Batch-core elapsed time excludes the new generated-output hashing work. Full GeneratorDriver time is measured from Roslyn events, not relabeled Akbura scope time. Direct EnC/CLR delta-production intervals remain unavailable unless their events are actually observed; unexplained time is not automatically assigned to EnC.

Generated-output telemetry retains latest-only scalar hashes/length totals, not generated syntax or semantic objects. Hint/character counts describe the Akbura batch; the cold-build file inventory includes all generators. A changed-hint count does not claim that every generated file changed. Compiler allocation totals are currently unavailable and must stay unavailable.

The event names/schema are verified against the installed binaries rather than assumed from upstream. Roslyn's [EventSource implementation](https://source.dot.net/Microsoft.CodeAnalysis.Workspaces/Log/RoslynEventSource.cs.html) is supplementary context, not a substitute for the installed manifest or captured events.

## Scope and remaining gates

The first delivery is the benchmark-only matrix. Using-scope caching, assignment pooling, fused lookups, broader expression support and CodeWriter changes are not implemented. Production defaults remain disabled even if this first performance matrix looks favorable, until the separate correctness/GeneratorDriver/memory gates are satisfied. Historical 100-edit memory attribution and old feasibility results are not new executions in this archive.

The [completed review](review.md) distinguishes Binding-only, Projection-only, Combined and watch-memory verdicts. Missing 500-edit memory, production allocation or direct EnC values stay explicitly unavailable rather than being filled from unrelated experiments. No further sessions or production optimizations were launched after this matrix.
