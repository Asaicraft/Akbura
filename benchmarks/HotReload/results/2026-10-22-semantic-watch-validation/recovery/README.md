# Live semantic watch validation after an oracle correction

Status: **completed and reviewed; semantic reuse works in live watch, but its production latency benefit is inconclusive**. Experimental defaults remain disabled.

The [initial investigation](../README.md) documents the workload, causal attribution and production methodology. Its source manifest, frozen binaries and completed correctness runs are preserved. This continuation changes only three harness files: `Program.SemanticWatch.cs`, `SupportedWatchFixture/Program.cs` and `Verify-SemanticWatch.ps1`. Compiler, cache ownership, runtime and unit-test implementation are unchanged.

## Corrected diagnostic contract

Introducing `global::Demo.WatchPerson.Current.MissingSemanticWatchMember` produces the expected diagnostic. Existing Hot Reload behavior removes this invalid binding but retains the label's last value. Baseline and combined both reproduce this behavior. The old oracle wrongly waited for that invalid label to react to a subsequent model change.

The corrected oracle waits for application of the diagnostic fallback. It then requires the original controls, state 42, user input, process and single external subscription to remain intact. Both reactive state labels and the unaffected direct member label must still react. The invalid label must retain its precise pre-error value and is explicitly reported as **not reactive**, not as a successful valid binding. After repair, all four name labels must react again; a further supported Margin update is required. Failed-generation telemetry must show errors without publishing the failed version into the semantic store. A missing or inconsistent acknowledgment still fails verification.

A short combined reproduction passed this complete sequence before launching the continuation. This preparation result is not a production timing sample. The final controller then passed the diagnostic sequence with baseline and with the combined 100-update stress session.

## Resume and evidence

```powershell
pwsh -NoProfile -File benchmarks/HotReload/Verify-SemanticWatch.ps1 `
    -OutputDirectory bin/SemanticWatch/gate-recovery `
    -ResultDirectory benchmarks/HotReload/results/2026-10-22-semantic-watch-validation/recovery `
    -ReuseCompilerEvidence bin/SemanticWatch/gate
Get-Content bin/SemanticWatch/gate-recovery/journal.log -Tail 20 -Wait
```

Reuse rejects any source difference outside the three allowlisted harness files, a different starting commit, changed frozen binaries or failed test evidence. Compiler pairs are copied byte-for-byte; the common measurement host is rebuilt. Closure manifests record the old compiler origin and new host hashes. `sources.json` links the original manifest hash and lists changed harness inputs. Reused test evidence records original completion times, result hashes and passed counts. The previous tests passed 155 DebugStats, 229 Release and 47 SDK-host executions for each of binding and combined (862 executions total); this continuation does not claim to rerun those tests.

All causal and negative-control watch sessions were run again with the corrected observer. The combined stress session restarted from revision one to validate the entire sequence in one process; the failed partial session is not spliced into the successful run. Three fresh non-STATS Release rounds produced 480 measured updates across four variants and Margin/Text scenarios. Only completed sessions with no observed timing contamination entered the generated summaries. The 15-second process monitor cannot exclude every short-lived competing process; it recorded no foreign-build wait, contamination or retry in this continuation.

The controller generated [samples.csv](samples.csv), [summary.csv](summary.csv), [stage-observations.csv](stage-observations.csv), [comparisons.csv](comparisons.csv) and the per-round [measured summary](measured-summary.md). The following engineering review is separate from those generated measurements.

## Results and engineering decision

The recovery completed on **2026-10-22 at 02:46:54 UTC** (08:46:54 Asia/Bishkek), using .NET SDK 10.0.401, runtime 10.0.12 and Windows 10.0.26200 x64. The 59 controller steps completed successfully. There are **42 completed live sessions, 795 sequential verified UI acknowledgments and 480 measured production updates**. The remaining acknowledgments cover warmups, initial/memory checks and instrumented scenarios; they are not added to the production sample count.

Post-run verification independently checked all 42 sessions: numbered native acknowledgments match recorded samples; each process remains unchanged within its session; state, controls, input, unaffected bindings and the single external subscriber satisfy their contracts. All fixture hashes and eight frozen closure manifests match their files, and all eight closures contain the same measurement-host hash. Current source files still match measured fingerprint `E22F9CF56D99D40A6A41B8E691FA196B6E179D75D34CB086E1D39F0C92E68778`. The review checked **547 credited causal records** against source hashes, actual loaded analyzer MVIDs and a native `dotnet-watch` host command line. No failed or unpublished generation is credited as a successful update.

### Real watched cache hits and skipped work

Each causal Margin session contains five measured edits and two warmups. These counts come from the actual watched generator, not an isolated GeneratorDriver or application-side counter.

| Metric, seven instrumented Margin edits | Baseline | Projection-only | Binding-only | Combined |
| --- | ---: | ---: | ---: | ---: |
| Verified edited UI acknowledgments | 7 | 7 | 7 | 7 |
| Semantic hits | 0 | 0 | 14 | 14 |
| First C# bindings skipped | 0 | 0 | 14 | 14 |
| Probe compilations skipped | 0 | 0 | 14 | 14 |
| Probe SemanticModels skipped | 0 | 0 | 14 | 14 |
| Dependency contributions imported | 0 | 0 | 14 | 14 |
| Expression-factory invocations | 63 | 63 | 49 | 49 |
| Application restarts / subscription failures | 0 / 0 | 0 / 0 | 0 / 0 | 0 / 0 |

Binding-only and combined skip exactly two first bindings per eligible revision: the unchanged `Current.FirstName` and `Current.LastName` chains. Matching skip counters, imported contributions, `ExactRestrictedContract` and `ValidatedPayloadAndImportedContribution` decisions come from the actual fast-return path. Successful store versions advance with each generation. Other expressions still bind normally; two reused results do not mean the whole component avoids semantic work.

The selected C# compilation, project state and store remain the same objects throughout each supported sequence. For example, both causal binding variants retain diagnostic tokens compilation `1`, state `2` and store `6`. The first edited raw CompilationProvider input is different (`16`), while the existing CSharpEnvironment comparer retains selected owner `1`; later raw inputs are stable. These tokens identify observed references only. They are not semantic equivalence proofs, and no reference-identity rule or comparer was relaxed.

Raw logs: [binding-only](runs/causal-binding-margin/watch.jsonl), [combined](runs/causal-combined-margin/watch.jsonl). Their `analyzer-observations` directories preserve unfiltered native records; the CSV is a convenience projection.

### Non-STATS production latency

Each cell below aggregates three independent sessions with 20 measured edits each, after two warmups. The primary estimator is the geometric mean of the three session medians, giving each round equal weight. Pooled p95 uses all 60 samples and is a different descriptive statistic. Positive reduction means faster than baseline.

| Supported Margin workload | Baseline | Projection-only | Binding-only | Combined |
| --- | ---: | ---: | ---: | ---: |
| Measured updates | 60 | 60 | 60 | 60 |
| Geometric mean of session medians, ms | 163.202 | 157.040 | 154.361 | 151.593 |
| Median reduction vs baseline | Reference | 3.78% | 5.42% | 7.11% |
| Descriptive bootstrap interval, reduction | — | -16.09% to 16.71% | -7.59% to 20.49% | -8.11% to 21.88% |
| Pooled p95, ms | 267.876 | 241.793 | 250.802 | 237.479 |
| Mean application allocated bytes per update interval | 793,458 | 790,402 | 782,800 | 794,230 |
| Application post-GC heap range, bytes | 1,346,624–1,378,456 | 195,776–1,348,048 | 1,342,112–1,379,928 | 1,346,624–1,350,608 |
| Restarts / subscription failures | 0 / 0 | 0 / 0 | 0 / 0 | 0 / 0 |

The allocation row is the mean delta of the application's cumulative allocated-byte counter from the preceding acknowledgment to each measured acknowledgment. It includes the intervening delay and timer activity; it is neither compiler allocation nor an allocation measurement restricted to the latency interval. Heap values are application-side `GC.GetTotalMemory(true)` observations at session end. The low projection-only heap sample is retained as recorded; it is not treated as an optimization benefit. Production compiler managed allocations and post-GC heap are unavailable.

The **pooled** Margin medians are 161.019, 159.921, 157.796 and 157.969 ms, respectively. They differ from the equal-round estimator; mixing these estimators would misstate the reported percentages. All observed production edits across both scenarios finish in 95.995–286.099 ms. The minute-long delay was not reproduced in this small fixture; these measurements do not rule out such delays in real projects or Visual Studio.

| Unsupported Text fallback workload | Baseline | Projection-only | Binding-only | Combined |
| --- | ---: | ---: | ---: | ---: |
| Measured updates | 60 | 60 | 60 | 60 |
| Geometric mean of session medians, ms | 145.761 | 156.088 | 148.775 | 144.840 |
| Median reduction vs baseline | Reference | -7.08% | -2.07% | 0.63% |
| Descriptive bootstrap interval, reduction | — | -24.96% to 11.45% | -24.63% to 14.73% | -19.82% to 13.84% |
| Pooled p95, ms | 253.317 | 250.698 | 253.192 | 252.725 |

Every interval includes zero. Binding-only improves the supported session median in two of three rounds; combined improves it in all three, but the sample still does not resolve the effect from normal variation. The intervals use 2,000 deterministic hierarchical resamples of rounds and then independent observations within the selected sessions, taking the 2.5th and 97.5th percentiles. Three sessions provide limited uncertainty resolution; edits across variants are not paired. Text point estimates warn of possible fallback regressions, especially projection-only, rather than proving either a regression or its absence.

Production uses explicit frozen analyzer paths and recorded file hashes, with the STATS observer absent. Direct loaded-MVID and semantic-skip observations are available only in the separate instrumented sessions. Individual production updates are therefore not claimed to have measured cache hits. Source eligibility and configuration match, but workload-level causal evidence is not substituted for per-update production telemetry. Native log reception is the only observed detection slice; generator/CLR-delta/reconciliation slices remain unavailable rather than being inferred from unsynchronized clocks.

### Negative controls, error recovery and stress

All four variants pass three Text edits, two scope edits and two C# changes: **28 negative-control edits**, with zero semantic hits and zero first-binding/probe/model skips. Text and scope changes reject the document contract; unavailable declaration contracts force normal binding where appropriate. C# edits select new compilations, states and stores with no previous generation, rather than admitting old owners. In the combined compilation control, the selected tokens change from `1/2/6` to `16/17/21` and then `31/32/36` across successive compilations.

The [combined stress session](runs/stress-combined/watch.jsonl) passes 100 measured supported edits, two warmups, six undo/redo edits, Text/scope/C# controls, diagnostic introduction, usable-runtime verification, repair, a subsequent supported edit and final memory verification. Its 116 acknowledgments all use the same application PID. The 102 steady-state supported edits produce **204 actual semantic hits and 204 each of first-binding/probe-compilation/probe-model skips and contribution imports**. These are instrumented stress counts, not production performance samples.

After the compilation-changing control, store `368` publishes generation `112`. Invalid generation `113` reports one error; its diagnostic snapshot can publish, but the successful semantic-store generation remains `112`. Repair publishes `114`, then the subsequent Margin edit publishes `115`. The invalid direct label retains its last valid value while all three unaffected labels react; after repair all four react again. The [baseline diagnostic session](runs/diagnostic-baseline/watch.jsonl) passes the same runtime fallback and repair contract without semantic reuse.

### Retention and its limits

During all 102 supported stress edits, the store holds exactly **two semantic entries, two operation roots, two probe models, two probe compilations and two probe syntax trees**. It remains latest-only; published versions advance from `2` through `103`, with each previous-version token referencing the immediately preceding successful generation. A genuine C# change creates a new store. This directly bounds the current retained payload, not every object owned by the whole compiler/watch process.

Importantly, compiler/watch managed heap **does grow** despite requested collections: from 48,459,440 bytes at generation 2 to 121,253,024 bytes at generation 103 (about 46.21 to 115.64 MiB). Gen2 collection count rises from 12 to 320. The final post-control/repair generation records 129,597,016 bytes. This must not be presented as flat process memory or as proof that the entire watch pipeline is leak-free. Roslyn/EnC history, monitoring overhead and other host state are not separated here, and no matched baseline 100-edit heap series was collected. Attribution of that whole-process growth is **inconclusive**. Working-set changes alone are not a managed-leak oracle.

The application finishes stress with a post-GC heap of 2,672,768 bytes, 67,617,088 cumulatively allocated bytes and one external subscriber. The first and last 25 measured stress edits have diagnostic medians of 347.328 and 283.190 ms; this session shows no worsening latency trend. Forced compiler collections and detailed STATS output make those timings unsuitable for production comparison.

Existing isolated 100/500-revision ownership tests from the [preceding semantic-reuse evidence](../../2026-10-21-semantic-reuse/README.md) remain historical collectibility evidence, not new executions. The new retained-recorder test and relevant lifecycle/cancellation/current-owner/dependency/repair suites passed in this investigation's correctness gate. Together they support bounded cache ownership; they do not erase the unresolved native-host heap observation.

### Correctness executions and provenance

| Exact compiler-source variant | DebugStats | Release | SDK-host Roslyn | Total |
| --- | ---: | ---: | ---: | ---: |
| Binding-only | 155 | 229 | 47 | 431 |
| Combined | 155 | 229 | 47 | 431 |
| Total executions | 310 | 458 | 94 | 862 |

These relevant filtered suites all passed on the original controller invocation. They include the three new observer regressions for actual skipped-work telemetry/ambient-scope restoration, diagnostic publication/repair, and collectibility while the recorder remains alive. The harness-only recovery reused these exact results and compiler binaries; it did not execute another 862 tests. Test-result hashes, original completion times and provenance are in [binding evidence](tests/binding/reused-evidence.json) and [combined evidence](tests/combined/reused-evidence.json). Preparation pilots, this investigation's repeated causal runs and prior-stage tests are not added to these correctness counts. This is not a claim that every repository test was rerun.

The source manifest and eight closure manifests preserve the original compiler origin and rebuilt common host. Raw native analyzer MVIDs verify the loaded compiler pair in instrumented runs. The archived first failed stress session remains [separate partial evidence](../failed-stress/README.md), not an accepted timing sample or a successful portion spliced into the final session.

Source files, scripts, reports and generated CSV/JSON observations use CRLF with a final CRLF. Raw TRX evidence is the exception: its original runner formatting and hashes are preserved rather than changing recorded evidence to normalize line endings. No raw test result was reformatted during review.

### Acceptance gates and configuration decisions

| Gate | Finding |
| --- | --- |
| Supported edit / owner identity | Passed: same-width Margin contract and exact selected compilation owner retained. |
| Real watched hits and skipped work | Passed for binding-only and combined: two actual first-binding/probe/model skips per eligible edit. |
| Live correctness and diagnostics | Passed in all completed scenarios, including the explicitly invalid-expression fallback and repair. |
| Cache payload retention | Bounded in the live store; isolated ownership evidence passes. Whole watch/compiler heap growth remains unattributed. |
| Production speedup | Inconclusive: all six candidate/scenario uncertainty intervals include zero. |
| Generalizability / default activation | Not established: restricted small fixture, no minute-long delay reproduced, no broad production acceptance. |

| Configuration | Decision | Reason |
| --- | --- | --- |
| Baseline | Reference; unchanged default | Correct comparison without experimental reuse. |
| Projection-only | Inconclusive | 3.78% supported point estimate, -7.08% fallback estimate; neither resolves beyond variation. |
| Binding-only | Restricted opt-in | Genuine live skips and correct behavior; 5.42% supported benefit remains uncertain and fallback may regress. |
| Combined | Restricted opt-in | Genuine live skips and correct behavior; 7.11% supported benefit remains uncertain, with native-host heap attribution unresolved. |

**Do not enable these experimental modes by default from this evidence.** Their build-time `ProbeReuseStage` opt-ins remain unchanged. There is no demonstrated general Hot Reload speedup and no semantic-cache redesign, generated-C# delta minimization, commit, push, merge or next optimization stage in this result.
