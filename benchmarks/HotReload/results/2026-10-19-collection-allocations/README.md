# Semantic dependency collection allocation investigation

Status: verification and measurements completed on October 19, 2026 at 07:23 UTC. The results support a local Detach allocation/time improvement and bounded temporary-set retention, not an end-to-end generator speedup or a reduction in total process allocations. Relevant DebugStats, SDK-host and Release suites passed, including the existing 100/500-revision checks. Starting revision: `c46e177cfa93ef375ef71667eef83a01346b5540`, branch `feature/improve-hot-reload`; implementation changes remain uncommitted. This work only changes temporary collection construction, not dependency completeness, invalidation or compiler ownership.

## Utility inventory and intended boundaries

At inventory time the existing Collections/Pools/ArrayExtensions implementations matched historical revision `9c3c84b27316fe0eb7ad7726b18d0d3498f62e84`; no historical implementation was copied into this branch. The existing utilities were extended in place.

| Hot path | Existing utility / proposed use | Ordering and ownership proof |
| --- | --- | --- |
| Collector's five Detach sequences | Add an internal known-count sorted materializer to the existing `EnumerableExtensions` partial | One fresh result array, sorted before immutable transfer. String sets are ordinal-unique; source-type pairs are unique by both fields; dictionary lookups have unique keys. Equal keys cannot represent distinct output facts at these call sites. |
| Explicit and whole-receiver candidates | Compare existing `ArrayBuilder.SortAndRemoveDuplicates` with an ordinal `PooledHashSet` rental | Exact ordinal sorted set, with local `try/finally`; never pool collector fields. |
| Input identifier/member inventories | Compare existing builders plus a local ordinal set with LINQ Distinct | Preserve first occurrence, not sorted order. Apply only if measurements justify it. |
| Probe diagnostic keys | Compare a lazy composite-key pooled set with the existing HashSet | Default tuple equality, original order, no rental for an empty/no-relevant-error sequence. |
| Projection of a known-size collection | Existing `SelectAsArray(IReadOnlyCollection, selector)` | Already creates one owned array; no replacement abstraction needed. |

`ImmutableArrayBuilder` copies into independent immutable storage, but rents a writer and has no sorting operation. `ArrayBuilder.ToImmutable` also copies; `ToImmutableAndFree` must not be combined with another unconditional Free. `ImmutableSetWithInsertionOrder` maintains persistent maps and is not a cheap temporary distinct buffer. `ConcurrentCache` is a fixed-size cache rather than a rental/ownership boundary. `ArraySortHelper` serves segmented arrays; ordinary owned result arrays should use `Array.Sort`. Existing unsafe array wrapping is not needed: new code uses `ImmutableCollectionsMarshal.AsImmutableArray`.

The generator targets netstandard2.0, whose HashSet API exposes neither Capacity nor EnsureCapacity. A Count-only return limit cannot detect a set that grew and then lost elements. The retention policy must account for that case using supported APIs, and its trimming cost must be measured rather than hidden.

## Baseline

The unchanged existing benchmark tool was verified against its archived assembly hashes before freezing. A new baseline ran the semantic-signature and ordinary markup workloads for both projects and both optimization modes, with initial generation, two warm-ups, eight measured revisions and fresh source/diagnostic oracles. Its raw output is `bin/CollectionAllocations/before`; portable results are in [before](before/README.md), with the corresponding new implementation in [after](after/README.md). Earlier archives were not overwritten. Timing groups must not overlap other builds/tests.

Microbenchmarks compare original and alternative algorithms within the same process, across sizes 0/1/2/4/8/16/32/64/128/256/1024 and high-duplicate inputs. Data creation, delegates, validation and reporting stay outside measured loops. End-to-end findings are separate from microbenchmark savings.

The baseline tool closure was frozen before any production edits. Its initial preflight was interrupted by unrelated tests in another worktree; that partial run is not an accepted measurement. The final controller resumed the original frozen baseline rather than rebuilding it from optimized sources. Functional tests in separate worktrees are valid parallel work; only clean timing groups need a quiet interval. The wrapper automatically waits and retries timing interference, without stopping anyone else's processes.

The completed real-project matrix comprises eight groups per binary, 16 groups overall, and 88 revisions per binary. All 176 fresh source/diagnostic comparisons passed. Separately, all 88 matching before/after revision pairs had identical generated-source digests and generator diagnostic values: [176 source/diagnostic comparison records](comparison-parity.json). Initial and warm-up revisions are included in parity but excluded from timing medians. Preflight ownership/fork checks are additional observations, not part of these timing totals. `before/run.json`, `after/run.json` and their compressed traces retain frozen binary and input provenance; inherited preparation-time environment metadata is not final-source provenance.

## Measured selection and rejected alternatives

The [selection run](selection/README.md) contains 1,936 timed batches across 242 complete cells, plus bounded/unbounded retention controls. It is explicitly a selection run, not final-source compiler verification. Among production algorithms, the candidate strategy changed afterwards; subsequent harness changes added faster log draining, provenance and before/after comparisons. The separate [final microbenchmark run](micro/README.md) also completed all 242 cells and records [loaded assembly hashes](micro/collections-provenance.json).

| Location | Before | Selected implementation | Selection allocation / timing observations |
| --- | --- | --- | --- |
| Collector Detach | Five LINQ ordering pipelines | Direct known-size copy, cached ordinal comparers, owned immutable transfer | At eight facts per populated field: 2,496 -> 672 bytes and 1,731.65 -> 1,641.38 ns. The control excludes the collector lock. Large cases save allocations but may take longer; see below. |
| Explicit member candidates | Concat/Select/Distinct/OrderBy | Ordinal pooled set, direct sorted immutable materialization, finally Free | Eight precomputed identities: 960 -> 152 bytes and 519.67 -> 392.65 ns. Symbol identity computation is excluded from these micro numbers. |
| Whole receiver candidates | Temporary immutable builder, Distinct/OrderBy | Same local ordinal pooled-set strategy | The shared sorted-set kernel is measured above, not the whole Record method. ArrayBuilder was rejected for this path after measurement. |
| DetachedCSharpInputs identifier/member inventories | First-occurrence LINQ Distinct | Left unchanged | At 128 unique strings, a pooled-set/ArrayBuilder prototype saved only 664 bytes (3,880 -> 3,216) while taking 1,711.33 -> 2,419.53 ns. It also copies/grows a builder that cannot remain pooled at that size. No token/source-path ordering is changed. |
| Probe diagnostics | Lazy composite-key HashSet | Left unchanged | Pooling saves bytes but the two-key control slows from 212.72 to 253.85 ns; empty/no-error calls get no benefit. Larger artificial cases improve, but no real diagnostic-heavy workload has established an overall benefit, so this separate migration is deferred. |

For eight unique candidates, the ArrayBuilder prototype used 152 bytes but took 589.78 ns versus the original 519.67 ns. At 128 it used 3,280 bytes / 9,509.38 ns versus the pooled set's 1,112 bytes / 6,925.39 ns. The existing builder drops buffers at capacity 128, making it a poor general choice for these variable-size candidate sets. The pooled set is selected for both candidate call sites; no unmeasured threshold heuristic is introduced.

The direct Detach result is an allocation trade-off, not a universal CPU win. At 128 facts, it uses 4,512 versus 16,896 bytes but takes 36,225.20 versus 30,083.98 ns; at 1,024, 33,184 versus 124,416 bytes and 531,053.52 versus 423,837.50 ns. These deliberately large fixtures are not representative per-document sizes inferred from real-project traces. Real generator stage and total measurements determine the practical effect. No result here should be advertised as allocation-free: the immutable owned result arrays still allocate.

The final run confirms the selection at eight precomputed candidates: LINQ uses 960 bytes / 547.19 ns, ArrayBuilder 152 bytes / 619.43 ns and the selected pooled set 152 bytes / 383.80 ns. At 128 candidates, the pooled set uses 1,112 bytes / 6,936.13 ns versus LINQ's 6,720 bytes / 8,032.23 ns. Final repeated Detach at eight facts is 2,496 -> 672 bytes and 1,750.92 -> 1,643.05 ns; its large 128-fact case still trades CPU for allocation, at 16,896 -> 4,512 bytes and 30,644.73 -> 35,170.12 ns. These are kernel/fixture results, not complete Record or whole-project timings.

## Real-project findings and regressions

The final [comparison CSV](comparison.csv) contains all 72 paired timing, allocation, retained-heap and stage comparisons. Full ranges, initial costs and p95 are in the before/after reports; with eight samples, nearest-rank p95 is the maximum rather than a robust production tail estimate. Groups were sequential, not randomized crossover trials. Small timing differences do not establish causality.

The measured Detach improvements are substantial within that small stage. The following rows all have cross-compilation reuse enabled; allocation is inclusive current-thread allocation attributed to that stage, not total process allocation.

| Project / edit | Detach calls | Median ms, before -> after | Stage bytes, before -> after | Time reduction | Allocation reduction |
| --- | --- | --- | --- | --- | --- |
| Gallery / signature | 95 | 0.34145 -> 0.21270 | 77,032 -> 15,040 | 37.71% | 80.48% |
| Purity / signature | 45 | 0.18185 -> 0.10785 | 40,512 -> 8,256 | 40.69% | 79.62% |
| Gallery / markup | 5 | 0.02010 -> 0.01200 | 4,480 -> 936 | 40.30% | 79.11% |
| Purity / markup | 8 | 0.04330 -> 0.02040 | 7,480 -> 1,608 | 52.89% | 78.50% |

This does not translate into a whole-generator win. Enabled-mode totals are:

| Project / edit | Generator median ms, before -> after | Elapsed change | Process bytes, before -> after | Allocation change |
| --- | --- | --- | --- | --- |
| Gallery / signature | 8,634.75 -> 8,681.11 | +0.54% | 3,047,348,808 -> 3,049,567,432 | +0.073% |
| Purity / signature | 2,120.49 -> 2,221.14 | +4.75% | 968,356,340 -> 969,312,188 | +0.099% |
| Gallery / markup | 230.63 -> 234.94 | +1.87% | 53,386,684 -> 54,610,616 | +2.293% |
| Purity / markup | 681.55 -> 681.98 | +0.06% | 139,470,004 -> 139,795,988 | +0.234% |

The disabled controls changed from -5.31% to +5.02% in elapsed time; notably Purity's signature control was +5.02% while enabled was +4.75%. This variability prevents attributing the entire timing increase to this patch. It does not make the increases disappear: no net end-to-end speedup or process-allocation reduction was demonstrated. Process CPU for enabled signature edits was 9,484.38 -> 9,445.31 ms in Gallery and 3,179.69 -> 3,273.44 ms in Purity. No combined overall improvement percentage is claimed.

Capture and validation remain small compared with binding. For signature edits, `SemanticDependencyCapture` was 1.10390 -> 1.20090 ms in Gallery and 0.37000 -> 0.38075 ms in Purity; `DocumentDependencyValidation` was 0.02385 -> 0.02495 ms and 0.02070 -> 0.02030 ms. Current-thread allocation attributed to the capture stage was zero before and after these incremental updates. Many original documents already have incomplete proofs and exit capture early; the complete independent fixture consumers reuse their artifacts. These real workloads therefore do not demonstrate candidate-materialization allocation savings during incremental capture; those savings are supported by the isolated kernels and exact-fact tests, not by extrapolating the micro percentages to the whole project.

For the same enabled signature cells, inclusive `CSharpProbeBinding` was 2,211.80 -> 2,271.18 ms in Gallery and 1,216.33 -> 1,294.32 ms in Purity; `CSharpProbeDiagnostics` was 1,167.29 -> 1,211.76 ms and 432.71 -> 494.60 ms. Probe/capture callback counts also varied, for example Gallery binding calls 3,064 -> 3,070, while all source and diagnostic outputs matched. Stage durations are inclusive and can overlap or run in parallel, so they must not be summed or compared as exclusive partitions of elapsed time.

The retained heap with drivers/workload owners still alive rose slightly in all four enabled cells, by about 0.13-0.33%; for signature edits it was 473,271,968 -> 474,817,128 bytes in Gallery and 210,446,200 -> 210,899,092 bytes in Purity. These are whole managed heaps, not direct cache sizes or a leak diagnosis. The bounded pool policy is retained for its explicit capacity limit and demonstrated grow/shrink safety, not advertised as a process-wide allocation optimization. Its trim/drop cost and the observed total-allocation regressions are part of this result. The changes retained for allocation performance are the locally measured Detach materialization and candidate kernels; the unhelpful builder, identifier and diagnostic migrations were rejected. Further broad pooling or semantic/runtime changes are not justified by this experiment.

## Pool retention and ownership

New rentals introduced here are only short-lived candidate sets. Each rental is local to one operation and returned in finally, including failures in symbol identity construction and immutable materialization. The collector's persistent sets, symbol-visited set and dictionary remain ordinary current-model-owned collections. Detach does not free or dispose the collector and can be called repeatedly. Compiler, semantic-state and snapshot publication lifetimes are unchanged. The return-policy change in generic PooledHashSet also applies to its existing users, not just the new candidate rentals.

The shared state is a pool of disjoint rentals, never a shared mutable set. The string pool uses ordinal equality and 32 slots. The generic pool keeps its default 128-slot setting unless a smaller size is supplied. Free clears all elements; above 1,024 current elements it drops the rental with `ForgetTrackedObject`. Smaller rentals use TrimExcess before Clear, so a formerly huge set that lost elements cannot retain its old capacity. This uses only the public netstandard2.0 API. No trimming replacement array is exposed through an immutable result.

The selected runtime retained capacity 1,103 rather than 1,931 for the 1,024-element control. Eight repeated 65,536-element rentals retained capacity zero versus 75,431 under the previous Clear-only policy, but allocated 23,279,528 versus 2,911,856 bytes. That is an explicit bounded-memory trade-off: repeatedly huge rentals are expensive and deliberately not retained indefinitely. At 16 elements, both policies retained capacity 17 with approximately equal allocation. Whole-heap measurements include runtime/harness ownership; retained capacity is the more direct pool observation. A separate regression tests grow-then-clear-then-add-one and old-element collectibility.

The final run reproduced these capacity endpoints; its large bounded case allocated 23,279,576 bytes versus the same 2,911,856-byte Clear-only control. The 1,024-element bounded case allocated 98,296 versus 76,176 bytes over eight rentals, retaining capacity 1,103 versus 1,931. Trimming is not cost-free. Neither the one-slot retention fixture nor the scalar results are measurements of all production pool slots being populated.

Each immutable array is freshly allocated for the exact result count, sorted before being wrapped through ImmutableCollectionsMarshal, and never written afterwards. No rented array or mutable collection is published. Ordinal string duplicates are the same scalar fact; source-type keys consist of both record fields, and lookup keys are dictionary-unique. Stability for distinct equal-key objects is not promised by the helper and is not required at the migrated call sites. Schema, completeness flags, identity strings and candidate universes are unchanged.

## Executed verification

The 32 focused tests cover empty/default behavior, duplicates, ordinal case/Unicode/separators, source/result independence, equal-key preservation, repeated Detach and exact serialized facts, incomplete reasons/flags, cancellation, throwing CopyTo/comparers/symbol identities, concurrent independent rentals, bounded capacity and element collection. They passed again after the final candidate strategy choice (`bin/CollectionAllocations/quick/collections-current.trx`), but do not replace the extended compiler/lifetime suites. The final micro run and C# brace audit also completed. A synthetic before/after comparison smoke check matched 16 source/diagnostic records and produced all nine requested metric/stage comparisons.

The full verifier completed the relevant DebugStats suites, an isolated SDK Roslyn host and Release cases. Earlier archives are untouched. [Verification metadata](verification/results.json) and [the source manifest](verification/source-manifest.json) identify fingerprint `73EAA82574DFC527FE1675F0B844A0D1383E50659813AA39275FC7DFACF0BFA0`. That fingerprint covers the implementation and harness, not later edits to this report. Its `BenchmarksRequested=false` refers to the nested quality verifier; the outer controller separately completed both benchmark matrices and the before/after comparison.

| Executed checks | Passed / failed | Evidence |
| --- | --- | --- |
| Relevant DebugStats suites, package Roslyn 4.11 | 421 / 0 | [TRX](verification/debugstats/tests.trx) |
| Relevant isolated SDK-host suites, Roslyn 5.9 | 208 / 0 | [TRX](verification/sdk/sdk-invalidation.trx), [actual host identities](verification/sdk/sdk-verification.json) |
| Relevant Release cases | 163 / 0 | [TRX](verification/release/tests.trx) |
| Report regressions | 17 / 0 | [Output](verification/Test-Report/output.log) |
| Invalidation report regressions | 23 / 0 | [Output](verification/Test-InvalidationReport/output.log) |
| Frozen contract controller regressions | 12 / 0 | [Output](verification/Test-ContractRunner/output.log) |

The suites overlap and must not be added as unique tests. They are relevant compiler/generator/pool suites, not every repository test project. All 32 new collection cases passed in the final hosts. Release excludes duplicate long sessions and STATS-only cancellation cases; those ran in both package and SDK-host DebugStats configurations. Exact quality commands and filters are archived in [verification/commands.json](verification/commands.json).

The existing current-owner and mixed semantic-dependency sessions passed at 100 and 500 revisions in both compiler hosts. The mixed semantic package-host cases took 1 min 30.50 s / 8 min 36.36 s; SDK-host cases took 1 min 32.94 s / 7 min 50.74 s. Recursive detached-object audits, current-only bounded fact checks, weak-reference collectibility and concurrent signature branches passed. All 13 semantic-dependency cancellation-boundary cases passed in each STATS host. Keeping only the latest detached owner did not retain obsolete compiler objects or snapshot history. These are functional/ownership test observations, not new real-project 500-revision timing claims.

No Visual Studio, Rider, runtime reconciliation or whole-watch latency was measured in this allocation-only change. No public semantic contract, declaration equivalence rule, invalidation decision or compiler lifetime was redesigned. There is no claim that this work fixes minute-long IDE Hot Reload delays.

## Reproduction and completion

Run from the repository root:

```powershell
pwsh -NoProfile -File benchmarks/HotReload/Verify-CollectionAllocations.ps1
Get-Content bin/CollectionAllocations/final/journal.log -Tail 20 -Wait
```

The first command starts a hidden background controller and returns its PID. All times in the journal are UTC. The completed run's status is in `bin/CollectionAllocations/final/status.json`; child output/stderr and failed attempts have separate timestamped directories. For a new experiment choose a new output and result directory rather than replacing this archive. Sources are fingerprinted; after source edits use a new output directory. The pre-change `bin/CollectionAllocations/before` frozen tool is a required baseline prerequisite in this checkout and must not be silently reconstructed from new code. On a new checkout, freeze and measure the unchanged starting revision first using the existing contract runner.

Evidence collection and the results review are complete for this narrowly scoped allocation change. The accepted result is lower local materialization cost with unchanged facts and ownership, plus bounded temporary-set retention. Whole-project latency and process-allocation improvements were not achieved; the regressions and trade-offs above remain explicit. No next Hot Reload architecture work, commit, merge or push was performed.
