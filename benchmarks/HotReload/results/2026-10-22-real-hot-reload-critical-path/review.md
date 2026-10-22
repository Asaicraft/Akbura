# Real Sidebar critical-path review

## Decision

The benchmark-only investigation is complete. It proves that compilation-bound semantic reuse removes actual Roslyn work on the real Sidebar, but does not establish the preregistered end-to-end benefit required for default activation. Production behavior, expression eligibility, ownership and defaults remain unchanged. No optimization, additional memory experiment or extended timing matrix was launched.

| Area | Verdict | Reason |
| --- | --- | --- |
| Binding-only | Restricted opt-in; performance inconclusive | Supported gain estimate 2.68%, interval crosses zero and point estimate is below 5%; fallback non-inferiority is not established. |
| Projection-only | Restricted | No demonstrated supported gain; fallback non-inferiority is not established. |
| Combined | Restricted opt-in; performance inconclusive | Supported interval crosses zero; additional gain over Binding is not demonstrated. |
| Watch memory | Inconclusive in this archive | No new 100/500-revision retained-heap comparison or root analysis was run. Historical attribution is separate evidence, not a new result. |

## Accepted production population

All 32 primary sessions completed without detected foreign-build contamination, process restarts or failed runtime invariants. Supported sessions contain 5 independent watch processes per variant, 30 measured edits each; fallback sessions contain 3 per variant, 20 edits each. Three warmups per session are excluded. This gives 840 measured updates plus 96 excluded warmups. Cold build/startup is outside the latency boundary.

Every acknowledgment verifies the expected visible value, original Sidebar owner/root, named control and nested component identities, state, foreground and two working ToggleSidebar operations. Primary sessions use frozen non-STATS compiler binaries without EventPipe or forced GC. The archive records the source fingerprint, tutorial revision, runtime, analyzer hashes and per-session provenance.

| Production metric | Baseline | Projection | Binding | Combined |
| --- | ---: | ---: | ---: | ---: |
| Supported median of session medians, ms | 1498.844 | 1498.777 | 1483.257 | 1415.240 |
| Supported pooled-edit p95, ms | 2256.012 | 2268.761 | 2119.155 | 2119.412 |
| Fallback median of session medians, ms | 1519.761 | 1570.671 | 1509.033 | 1531.948 |
| Fallback pooled-edit p95, ms | 2138.112 | 2162.381 | 2196.392 | 2257.649 |

These medians and p95 values are descriptive. Inference uses the geometric mean of the **paired per-round session-median ratios**, with equal round weights and 10,000 deterministic paired-round session-cluster bootstrap resamples. Consequently, the estimated improvement is not obtained by dividing the displayed medians of session medians. Individual edits within one process are not independent bootstrap units.

Positive values below mean faster than the reference; negative values mean slower.

| Comparison | Supported improvement, % [95% interval] | Fallback improvement, % [95% interval] |
| --- | ---: | ---: |
| Projection / Baseline | -1.99 [-9.75, +4.83] | -3.73 [-8.32, +1.01] |
| Binding / Baseline | +2.68 [-0.57, +6.54] | -0.84 [-5.98, +2.56] |
| Combined / Baseline | +3.29 [-1.53, +7.50] | -1.65 [-2.71, -0.73] |
| Combined / Binding | +0.63 [-1.45, +3.14] | -0.81 [-4.20, +3.08] |

Binding fails the >=5% supported point-estimate gate and the positive-interval gate. Its fallback interval also fails to exclude a regression worse than 5%. Combined fails both supported gates and the >=2% positive-interval comparison against Binding. Combined's fallback result is a small observed slowdown within the preregistered 2% point/5% interval budget, not an improvement. Projection fails the fallback point-estimate and interval limits. Only three fallback clusters are available; these intervals must not be treated as high-powered proof of equivalence.

The 600 supported measured latencies span 1325.134-3288.400 ms; the 240 fallback latencies span 1342.260-2489.837 ms. Excluded warmups span 2912.031-17510.328 ms. This workload did **not** reproduce a minute-long warmed update and therefore does not explain every intermittent delay seen in an IDE or another machine state.

Raw inputs: [samples](samples.csv), [independent session summaries](session-summary.csv), [comparisons](comparison.csv), [machine-readable summary](statistics.json). The [generated summary](measured-summary.md) can be regenerated without rerunning watch; this review contains the manual interpretation.

## Direct diagnostic measurements

These are a **separate population**, not the primary production timings: one STATS/EventPipe session per variant and scenario, five measured updates after three warmups. All 16 sessions passed runtime and causal checks. All 16 traces drained with zero lost events. Completed same-process GeneratorDriver spans are matched to revisions by an enclosed verified source-hash/revision marker, not cross-process timestamp subtraction.

The table reports medians of five supported diagnostic revisions. For one metric appearing in both Request and Generation records, the records are totaled within a revision first. Different named stages are inclusive and may overlap; their medians must not be added as an exclusive budget.

| Supported diagnostic metric per update | Baseline | Projection | Binding | Combined |
| --- | ---: | ---: | ---: | ---: |
| File-write completion to verified UI acknowledgment, ms | 2129.39 | 2083.36 | 2166.98 | 2115.02 |
| Direct correlated GeneratorDriver, ms | 809.731 | 854.200 | 779.351 | 751.478 |
| Direct BlackSilence single-generator interval, ms | 806.688 | 851.056 | 776.426 | 748.528 |
| Akbura Request + Generation inclusive scopes, ms | 754.983 | 811.447 | 743.689 | 711.432 |
| Generated-batch core excluding output hashing, ms | 747.780 | 804.973 | 737.526 | 704.021 |
| Semantic binding, ms | 728.399 | 783.824 | 712.211 | 685.837 |
| Expression projection construction, ms | 660.780 | 16.563 | 625.834 | 14.374 |
| Probe-scope construction, ms | 615.200 | 3.730 | 589.587 | 3.369 |
| Cross-revision binding lookup, ms | 0 | 0 | 0.436 | 0.156 |
| Semantic dependency capture, ms | 0.149 | 0.152 | 0.628 | 0.622 |
| Dependency detachment, ms | 0.019 | 0.020 | 0.056 | 0.054 |
| Component planning, ms | 10.911 | 11.902 | 18.034 | 14.430 |
| C# emission, ms | 4.708 | 4.643 | 5.020 | 4.902 |
| SourceText creation, ms | 0.729 | 0.722 | 0.697 | 0.727 |
| Expression projections constructed | 248 | 120 | 248 | 120 |
| Cross-revision projection constructions skipped | 0 | 128 | 0 | 128 |
| Markup expression factory bindings | 181 | 181 | 166 | 166 |
| Cross-revision first bindings skipped | 0 | 0 | 15 | 15 |
| Probe compilations created | 295 | 295 | 280 | 280 |
| Probe SemanticModels acquired | 295 | 295 | 280 | 280 |
| Semantic contributions imported | 0 | 0 | 15 | 15 |
| Component plans created | 1 | 1 | 1 | 1 |
| Generated Akbura source hints | 16 | 16 | 16 | 16 |
| Changed generated Akbura hints | 1 | 1 | 1 | 1 |

The generated Akbura batch contains approximately 2.43 million characters; the cold-build inventory contains 23 generated C# files from all generators. Exact character counts are available in [stage observations](stage-observations.csv); fresh prepared project paths differ between sessions, so these totals are not a cross-variant source-parity assertion. Only one Akbura hint changes per warmed supported edit, not all 16 hints. A changed-hint count does not measure Roslyn's downstream method/delta workload or prove that the entire project output was rewritten.

For every supported measured Binding/Combined revision, all five causal counters agree at 15: BindingHit, FirstBindingSkipped, ProbeCompilationSkipped, ProbeSemanticModelSkipped and SemanticContributionImported. Actual created probe compilations and models fall from 295 to 280. This is removed work, not merely a cache lookup hit. Projection skips construction of 128 projections but does not reduce created probe compilations/models.

The diagnostic changed-expression and literal-fallback scenarios also reuse other unchanged expressions: 15 bindings per measured revision for Binding/Combined. They are not zero-hit controls for the whole document. The changed constructor expression is outside the admitted member-chain subset; its modification must still bind normally. The relevant C# declaration-change scenario records zero semantic hits in every variant, confirming the compilation-owner boundary. The separate [member-chain controls](control) pass positive skip gates for Binding/Combined and negative gates for Baseline/Projection. None of these controls expands eligibility.

## What is and is not the critical path

The directly observed BlackSilence interval is nearly the full **correlated** GeneratorDriver span. Within Akbura, semantic binding is much larger than planning, emission, dependency capture or cache lookup. Projection's much smaller projection-construction timer does not translate into a comparable reduction in total semantic binding or UI latency: nested/lazy work and changes in when it is materialized make subtraction of those inclusive stages invalid. The session does not isolate the exact reason for that redistribution.

The median per-revision ratio of correlated GeneratorDriver duration to UI latency is 42.6% for Baseline and about 38% for the other three variants. This compares elapsed durations, not timestamps from different processes. It is diagnostic context, not a production improvement estimate. A substantial portion of the live-update path therefore lies outside the one correlated GeneratorDriver interval. It is **not** yet partitioned into watch scheduling, other project/generator work, C# compilation/diagnostics and EnC delta production.

Received watch-log markers place update-batch submission shortly before UI acknowledgment: diagnostic supported medians are 2099.48/2052.71/2137.96/2084.00 ms for Baseline/Projection/Binding/Combined respectively. The corresponding received RuntimeApplying-to-RuntimeApplied log gaps have medians 2.18/2.58/2.39/2.28 ms. UI validation itself has medians about 1 ms. These log intervals include transport/logging effects and are **not** direct CLR runtime-apply durations. They support investigating the pre-application path rather than claiming runtime UI application is responsible for the seconds of delay.

The proposed cached using-scope and fused lookup changes target sub-millisecond measured scopes. Whole component planning takes only 11-18 ms here, with CreateAssignments being just a subset. They are not justified as the next major end-to-end optimization by this evidence. No claim is made about their possible allocation benefit because compiler allocation attribution was not collected.

The next useful investigation is finer attribution of the unpartitioned watch/compiler path and remaining semantic binding, with direct compilation/EnC evidence where available. Keep expression support and ownership unchanged. Any later local optimization still needs an isolated microbenchmark and source/diagnostic/dependency parity before changing production code. Do not expand semantic-cache eligibility simply to increase benchmark hits.

## Unmeasured and remaining acceptance gates

| Required evidence | Baseline | Projection | Binding | Combined |
| --- | --- | --- | --- | --- |
| Compiler allocated bytes per update | Not measured | Not measured | Not measured | Not measured |
| Direct EnC delta-production duration | Not measured | Not measured | Not measured | Not measured |
| New forced-GC heap growth at 100 revisions | Not measured | Not measured | Not measured | Not measured |
| New forced-GC heap growth at 500 revisions | Not measured | Not measured | Not measured | Not measured |
| Live semantic-store entry counts / root paths | Not newly measured | Not newly measured | Not newly measured | Not newly measured |
| Fresh four-way generated-source parity suite | Not run | Not run | Not run | Not run |
| Fresh four-way diagnostic/dependency parity suite | Not run | Not run | Not run | Not run |
| Standalone real-component GeneratorDriver regression matrix | Not run | Not run | Not run | Not run |

The [focused TRX](tests/critical-observation.trx) contains eight passed DebugStats tests: two new observer source/diagnostic parity cases and six existing statistics cases. Synthetic trace-correlation and session-cluster summary guards also passed. This is not a substitute for the full affected Release/DebugStats/SDK suites, cancellation/concurrency/failure-repair checks or isolated compilation-transition GC tests required for default acceptance.

The current matrix already fails the supported performance decision gates, so defaults stay disabled independently of those missing gates. An eight-session/40-update extension with the same frozen binaries could improve precision, but was not launched automatically. New memory experiments and production changes remain outside this completed benchmark-only run.
