# Bounded cross-compilation artifact reuse

This archive compares the detached-artifact optimization enabled and disabled in the same `DebugStats` binary. It is a generator-only measurement, not `dotnet watch`, Visual Studio, full build or source-to-screen latency. The [canonical mechanism](../../../../For%20Contributors/hot-reload.md#reusing-artifacts-after-a-c-edit) defines the narrow supported subset; the [ownership audit](../../../../For%20Contributors/csharp-invalidation-investigation.md#detached-artifact-ownership-and-validation) explains its safety boundary.

The final frozen run completed on October 17, 2026 at 12:56 UTC (18:56 in Bishkek). All 64 primary workload/mode groups and four lifetime/concurrency groups completed: 704 primary fresh-driver comparisons and 214 lifetime/fork comparisons passed. The result is a large reduction in binding work for the proven isolated-leaf subset, not general acceleration of all C# edits. No obsolete compilation-bound target survived the isolated newest-owner GC checks. The current detached artifacts intentionally remain alive and have a measurable memory cost.

## Memory architecture

The stable project/options incremental node owns one `BoundedArtifactStore`. A successful publication atomically replaces its entire current artifact/weak-identity pair; monotonically reserved versions prevent an older successful attempt from overwriting a newer one. Validation and generation run outside the publication lock. Each branch validates the actual candidate against its own current inputs, so publication order is only a cache-hit opportunity, never a correctness condition.

```text
Roslyn current driver/state tables
  +-- current compilation-specific state --> compilation, index, models, probes, bound snapshot
  +-- current request ---------------------> weak previous bound snapshot
  +-- current bound snapshot --------------> its current detached values (no reverse edge)
  +-- stable project/options store
        +-- one latest detached snapshot --> scalar input/dependency facts, source characters, diagnostic values
        +-- current weak identity handles -> no strong compiler/reference/option ownership
```

There are no previous/parent links or historical identity dictionaries in the detached owner. Removed entries are absent from newly built maps. Generated text is explicitly converted to Roslyn's known string-backed `SourceText`; arbitrary text implementations, semantic objects, syntax/version objects and capturing delegates are excluded by construction and the recursive graph test. A caller deliberately holding an old driver can still legitimately retain that driver's compiler graph; the isolated-owner experiment releases those external roots.

Active readers or branches can temporarily retain their own current detached values. The store itself retains only its latest publication; it never records those branch generations as history. Sharing immutable current text records does not link their producing snapshots.

## Semantic correctness

Reuse requires exact project/document/schema facts, ordered reference/options/parse/encoding identity and exact C# input text except for the explicitly allowed isolated leaf. That leaf is a standalone internal static class with one parameterless static `int` method returning an integer literal, without attributes, imports, directives, generics, base types or other members. Its declaration tokens must remain identical; its normalized name must be outside other C# inputs, generated declaration names and the document/global lookup universe. Unknown lookup syntax or compiler services means fallback, not guessed equivalence.

Signatures, overloads, constants, aliases, references/options, added/removed trees and unresolved/invalid declarations therefore use current-compilation binding. Source and diagnostic eligibility are independent: diagnostic availability and primary/additional source mappings have their own checks. Detached source reuse never imports a semantic model or bypasses `SemanticModelState.IsCompatibleWith`. Full fresh-driver source/diagnostic equality is a test oracle only, not the production proof.

The [verification archive](verification/README.md) records the 274-case regression run, 61-case SDK-host run, 39-case Release run, adversarial edits, 100/500-revision GC tests, cancellation and publication-order checks.

## Performance method

The starting repository revision is `ad27592712009e2959fe47eb0913c7ce5b16981f` on `feature/improve-hot-reload`. The working implementation, input and analyzer hashes are recorded in the provenance files; HEAD alone does not identify these binaries. Both historical October 16 result archives are unchanged.

The historical unrelated-body medians were 12,698.99 ms for FeatureGallery and 2,295.80 ms for Purity Sidebar, with 33 and 15 semantic source inputs prepared respectively. Those were trace-instrumented investigation runs, not the paired `stats` controls below. They provide context, not the denominator for a claimed optimization percentage; the current disabled mode is the like-for-like control.

The real evaluated FeatureGallery and pinned Purity tutorial (`f752e29d7dc791c7fcdac6b740aad7a52552738e`, Sidebar target) use identical inputs within each enabled/disabled pair. Purity is exported and adapted in an ignored disposable directory; the submodule is not edited. Each group runs one initial generation, two warm-ups and eight measured updates. The modes are paired per scenario, disabled first; this is not a randomized statistically isolated experiment.

The final queue measures Gallery markup first as a regression guard, then continues the remaining scenario/project pairs on the same frozen binaries. `control-order.json` records this fixed order and the guard's input/tool provenance. The gate changes execution order, not the samples or their inclusion.

Earlier exploratory measurements exposed avoidable markup-edit overhead from repeated detachment. They are preserved in ignored scratch storage and excluded from this archive. The final implementation memoizes only unchanged scalar facts and already detached characters, and the complete comparison must use its final frozen binaries.

Every update, including initial and warm-up passes, is checked against a cache-disabled fresh generator. Full source identities/content, mappings and compiler/generator diagnostic values are compared outside the timed interval. The transient diagnostic publication stamp is checked independently as positive and not newer than the current request; all other properties and provenance remain part of equality. Existing isolated-harness compiler errors (other generators such as Avalonia are not run here) must remain stable and match the fresh oracle. Unsupported/incomplete-input cases are explicitly recorded, never silently counted as successful clean compilation.

Timing is `RunGeneratorsAndUpdateCompilation` only. Inclusive stage durations must not be added together. Forced full GC and fresh verification run outside that interval but affect the following sample's heap/cache state in both modes. Allocations are process-wide managed allocated bytes during the timed generator call; post-GC heap is retained managed memory with the current driver and baseline inputs alive, not allocation volume. Working set also includes native/runtime/GC allocator state. Nearest-rank p95 with eight samples is the maximum, not a production tail-latency estimate.

`GeneratedSourceTextCreated` and `CSharpEmission` count component/AKCSS-writer work, not every Roslyn text allocation or emission. The project-level Hot Reload service is still rebuilt once per executed document batch in both modes. `DetachedSourceTextCreated` separately counts actual new string-backed text wrappers in `ArtifactCapture`; unchanged detached characters can be shared in fresh current maps without retaining an older snapshot. Its work and allocations are already included in capture and driver totals. Zero component/AKCSS-writer text constructions must not be interpreted as zero cache-capture allocations or zero project-service work.

The machine was an Intel Core i5-11400H (12 logical processors), approximately 15.73 GiB RAM, Windows 11 Pro build 26200. The preparation-time available-memory observation was taken while correctness tests were finishing; the separately recorded timing-start observation was approximately 3.82 GiB. No benchmark samples overlapped those tests. This was a developer desktop, not an isolated performance lab; background activity, memory pressure and process/JIT history can affect observations. Exact SDK/analyzer/input/source provenance is in [the environment](invalidation-environment.json), [per-group provenance](invalidation-provenance.json) and [execution order](control-order.json).

## Performance interpretation

All numbers below use the paired current disabled control, not the historical trace-instrumented results. Change is `(enabled / disabled - 1) * 100`; negative values mean less elapsed time. Each primary median contains eight measured updates.

| Workload | Edit | Disabled median ms | Enabled median ms | Change |
| --- | --- | ---: | ---: | ---: |
| Gallery | Isolated unrelated body | 9,015.43 | 23.04 | -99.74% |
| Gallery | Isolated C# whitespace | 8,834.86 | 22.95 | -99.74% |
| Gallery | Isolated C# comments | 8,551.33 | 22.73 | -99.73% |
| Gallery | Isolated body reversion | 8,389.61 | 22.62 | -99.73% |
| Gallery | Ordinary markup | 243.25 | 240.72 | -1.04% |
| Gallery | Referenced body, fallback | 8,532.95 | 8,556.16 | +0.27% |
| Purity Sidebar | Isolated unrelated body | 1,951.83 | 10.59 | -99.46% |
| Purity Sidebar | Isolated C# whitespace | 1,918.47 | 10.45 | -99.46% |
| Purity Sidebar | Isolated C# comments | 1,950.64 | 10.58 | -99.46% |
| Purity Sidebar | Isolated body reversion | 2,129.41 | 11.78 | -99.45% |
| Purity Sidebar | Ordinary markup | 610.82 | 627.30 | +2.70% |
| Purity Sidebar | Referenced body, fallback | 2,076.25 | 1,947.10 | -6.22% |

The unrelated-body reduction comes from avoiding actual semantic work, not merely suppressing identical output:

| Per-update median, unrelated body | Gallery disabled | Gallery enabled | Purity disabled | Purity enabled |
| --- | ---: | ---: | ---: | ---: |
| Semantic source inputs prepared | 33 | 0 | 15 | 0 |
| Semantic models created | 31 | 0 | 15 | 0 |
| Probe compilations | 2,892 | 0 | 869 | 0 |
| Probe semantic-model acquisitions | 2,892 | 0 | 869 | 0 |
| Probe diagnostic queries | 2,911 | 0 | 872 | 0 |
| Components generated | 28 | 0 | 15 | 0 |
| AKCSS sources generated | 5 | 0 | 0 | 0 |
| Diagnostic documents evaluated | 32 | 0 | 17 | 0 |
| Component/AKCSS emissions | 33 | 0 | 15 | 0 |
| Component/AKCSS source texts created | 33 | 0 | 15 | 0 |
| New detached text wrappers | 0 | 0 | 0 | 0 |
| Validated source artifact hits | 0 | 33 | 0 | 15 |
| Detached diagnostic entries reused | 0 | 32 | 0 | 17 |
| Conservative source fallbacks | 0 | 0 | 0 | 0 |
| Validation ms, inclusive | 0 | 11.77 | 0 | 5.88 |
| Capture ms, inclusive | 0 | 0.04 | 0 | 0.04 |
| Process-wide allocated MiB | 2,859.91 | 7.32 | 906.14 | 4.12 |
| Post-GC active-driver heap MiB | 408.65 | 201.66 | 190.95 | 84.88 |

Managed allocation volume fell by 99.74% for Gallery and 99.55% for Purity in this case. These are allocations within the timed generator call, not the fresh oracle's allocations and not the detached owner's retained size. Validation remains a substantial part of the fast path and is included in driver time.

Referenced bodies/signatures, overloads, constants, global usings, reference replacement, file addition/removal and unresolved/repaired inputs all performed current-compilation work: 33 source fallbacks per Gallery update and 15 per Purity update. Their paired median changes ranged from -6.45% to +1.88% for Gallery and -6.22% to +3.31% for Purity. These variations are not evidence that fallback analysis was eliminated. Enabled fallback validation took 12.50-15.18 ms for Gallery and 5.02-5.97 ms for Purity, with capture separately taking 3.03-3.23 ms and 0.58-0.62 ms. Every update still matched the fresh oracle.

Ordinary markup retained its selective binding behavior: one component regenerated in both modes, with three Gallery or six Purity semantic models and unchanged probe/query counts. Validation/capture medians were 0.14/0.06 ms for Gallery and 0.51/0.11 ms for Purity. Allocation increases were 0.33% and 0.65%; one new detached wrapper was created per update rather than copying every unchanged generated text. The final Gallery median did not regress; Purity's median increased by 16.47 ms (2.70%). This run does not establish statistical equivalence or a tail-latency improvement: Gallery's observed maximum increased from 263.02 to 334.14 ms, while Purity's decreased from 934.14 to 875.83 ms. The small sample and fixed order preclude treating either maximum as a production p95 guarantee.

Unchanged and repeated-identical inputs stayed on the existing cached path without artifact validation/capture: Gallery medians remained approximately 5.3-5.5 ms and Purity approximately 2.0-2.1 ms. No cross-compilation hit is needed for that path. The complete matrix, including initial times and ranges, follows.

## Latency matrix

<!-- BEGIN GENERATED: invalidation -->
Recorded 704 driver runs; 64 workload/mode groups; 704 fresh-driver parity checks. Each group has two warm-ups and eight measured samples.

| Project / file | Scenario | Binary / profiling | Initial ms | Median ms | Range ms | p95 ms | Components generated / reused | Diagnostics evaluated | Changed texts |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Akbura.FeatureGallery / Counter.akbura | add-tree | stats / stats / disabled | 14908.80 | 8140.44 | 8082.59 - 8757.05 | 8757.05 | 28 / 0 | 32 | 0 |
| Akbura.FeatureGallery / Counter.akbura | add-tree | stats / stats / enabled | 14158.34 | 8207.17 | 8099.72 - 8775.58 | 8775.58 | 28 / 0 | 32 | 0 |
| Akbura.FeatureGallery / Counter.akbura | akbura | stats / stats / disabled | 15399.46 | 243.25 | 240.66 - 263.02 | 263.02 | 1 / 27 | 1 | 1 |
| Akbura.FeatureGallery / Counter.akbura | akbura | stats / stats / enabled | 15038.62 | 240.72 | 228.75 - 334.14 | 334.14 | 1 / 27 | 1 | 1 |
| Akbura.FeatureGallery / Counter.akbura | const | stats / stats / disabled | 14905.87 | 8633.27 | 8478.81 - 9006.93 | 9006.93 | 28 / 0 | 32 | 0 |
| Akbura.FeatureGallery / Counter.akbura | const | stats / stats / enabled | 14958.14 | 8412.87 | 8027.92 - 8960.80 | 8960.80 | 28 / 0 | 32 | 0 |
| Akbura.FeatureGallery / Counter.akbura | csharp-trivia | stats / stats / disabled | 14760.59 | 8551.33 | 8431.35 - 9072.17 | 9072.17 | 28 / 0 | 32 | 0 |
| Akbura.FeatureGallery / Counter.akbura | csharp-trivia | stats / stats / enabled | 15394.69 | 22.73 | 20.68 - 25.78 | 25.78 | 0 / 28 | 0 | 0 |
| Akbura.FeatureGallery / Counter.akbura | csharp-whitespace | stats / stats / disabled | 15259.83 | 8834.86 | 8395.76 - 8987.32 | 8987.32 | 28 / 0 | 32 | 0 |
| Akbura.FeatureGallery / Counter.akbura | csharp-whitespace | stats / stats / enabled | 14648.19 | 22.95 | 20.91 - 28.05 | 28.05 | 0 / 28 | 0 | 0 |
| Akbura.FeatureGallery / Counter.akbura | global-using | stats / stats / disabled | 15030.80 | 8316.98 | 8168.10 - 8824.12 | 8824.12 | 28 / 0 | 32 | 33 |
| Akbura.FeatureGallery / Counter.akbura | global-using | stats / stats / enabled | 14063.76 | 8297.47 | 8221.03 - 8917.17 | 8917.17 | 28 / 0 | 32 | 33 |
| Akbura.FeatureGallery / Counter.akbura | overload | stats / stats / disabled | 15822.72 | 8552.82 | 8409.39 - 9071.18 | 9071.18 | 28 / 0 | 32 | 0 |
| Akbura.FeatureGallery / Counter.akbura | overload | stats / stats / enabled | 15573.64 | 8583.23 | 8485.84 - 8921.06 | 8921.06 | 28 / 0 | 32 | 0 |
| Akbura.FeatureGallery / Counter.akbura | reference-compatible | stats / stats / disabled | 13523.90 | 8189.63 | 8094.42 - 8798.27 | 8798.27 | 28 / 0 | 32 | 0 |
| Akbura.FeatureGallery / Counter.akbura | reference-compatible | stats / stats / enabled | 14664.76 | 8202.21 | 8137.18 - 8771.36 | 8771.36 | 28 / 0 | 32 | 0 |
| Akbura.FeatureGallery / Counter.akbura | referenced-body | stats / stats / disabled | 16409.95 | 8532.95 | 8484.01 - 8607.10 | 8607.10 | 28 / 0 | 32 | 0 |
| Akbura.FeatureGallery / Counter.akbura | referenced-body | stats / stats / enabled | 15503.48 | 8556.16 | 8395.09 - 9021.16 | 9021.16 | 28 / 0 | 32 | 0 |
| Akbura.FeatureGallery / Counter.akbura | referenced-signature | stats / stats / disabled | 15222.75 | 8794.25 | 8446.73 - 9279.52 | 9279.52 | 28 / 0 | 32 | 0 |
| Akbura.FeatureGallery / Counter.akbura | referenced-signature | stats / stats / enabled | 14972.56 | 8959.27 | 8649.12 - 9408.93 | 9408.93 | 28 / 0 | 32 | 0 |
| Akbura.FeatureGallery / Counter.akbura | remove-tree | stats / stats / disabled | 14723.88 | 8277.95 | 8173.91 - 8837.71 | 8837.71 | 28 / 0 | 32 | 0 |
| Akbura.FeatureGallery / Counter.akbura | remove-tree | stats / stats / enabled | 14896.82 | 8179.33 | 8157.78 - 8619.21 | 8619.21 | 28 / 0 | 32 | 0 |
| Akbura.FeatureGallery / Counter.akbura | repeat | stats / stats / disabled | 14693.40 | 5.26 | 4.95 - 5.58 | 5.58 | 0 / 0 | 0 | 0 |
| Akbura.FeatureGallery / Counter.akbura | repeat | stats / stats / enabled | 14023.01 | 5.39 | 4.84 - 5.53 | 5.53 | 0 / 0 | 0 | 0 |
| Akbura.FeatureGallery / Counter.akbura | revert | stats / stats / disabled | 14264.08 | 8389.61 | 8190.73 - 8919.71 | 8919.71 | 28 / 0 | 32 | 0 |
| Akbura.FeatureGallery / Counter.akbura | revert | stats / stats / enabled | 14724.06 | 22.62 | 20.24 - 28.55 | 28.55 | 0 / 28 | 0 | 0 |
| Akbura.FeatureGallery / Counter.akbura | unchanged | stats / stats / disabled | 15180.28 | 5.31 | 4.81 - 6.09 | 6.09 | 0 / 0 | 0 | 0 |
| Akbura.FeatureGallery / Counter.akbura | unchanged | stats / stats / enabled | 15304.29 | 5.47 | 5.14 - 5.99 | 5.99 | 0 / 0 | 0 | 0 |
| Akbura.FeatureGallery / Counter.akbura | unrelated-body | stats / stats / disabled | 15263.30 | 9015.43 | 8716.29 - 9276.74 | 9276.74 | 28 / 0 | 32 | 0 |
| Akbura.FeatureGallery / Counter.akbura | unrelated-body | stats / stats / enabled | 15264.20 | 23.04 | 21.44 - 26.19 | 26.19 | 0 / 28 | 0 | 0 |
| Akbura.FeatureGallery / Counter.akbura | unresolved | stats / stats / disabled | 15082.15 | 8788.22 | 8182.07 - 8871.83 | 8871.83 | 28 / 0 | 32 | 1 |
| Akbura.FeatureGallery / Counter.akbura | unresolved | stats / stats / enabled | 14866.79 | 8221.09 | 8151.46 - 8777.24 | 8777.24 | 28 / 0 | 32 | 1 |
| PurityUIDashborad / Sidebar.akbura | add-tree | stats / stats / disabled | 5512.77 | 1992.92 | 1962.90 - 2125.54 | 2125.54 | 15 / 0 | 17 | 0 |
| PurityUIDashborad / Sidebar.akbura | add-tree | stats / stats / enabled | 5955.68 | 1906.24 | 1807.40 - 2078.83 | 2078.83 | 15 / 0 | 17 | 0 |
| PurityUIDashborad / Sidebar.akbura | akbura | stats / stats / disabled | 5496.88 | 610.82 | 587.47 - 934.14 | 934.14 | 1 / 14 | 1 | 1 |
| PurityUIDashborad / Sidebar.akbura | akbura | stats / stats / enabled | 5654.40 | 627.30 | 586.76 - 875.83 | 875.83 | 1 / 14 | 1 | 1 |
| PurityUIDashborad / Sidebar.akbura | const | stats / stats / disabled | 5444.69 | 2015.90 | 1939.06 - 2148.55 | 2148.55 | 15 / 0 | 17 | 0 |
| PurityUIDashborad / Sidebar.akbura | const | stats / stats / enabled | 6064.02 | 1931.86 | 1874.18 - 2190.24 | 2190.24 | 15 / 0 | 17 | 0 |
| PurityUIDashborad / Sidebar.akbura | csharp-trivia | stats / stats / disabled | 5503.08 | 1950.64 | 1846.59 - 2090.46 | 2090.46 | 15 / 0 | 17 | 0 |
| PurityUIDashborad / Sidebar.akbura | csharp-trivia | stats / stats / enabled | 5862.54 | 10.58 | 9.66 - 15.78 | 15.78 | 0 / 15 | 0 | 0 |
| PurityUIDashborad / Sidebar.akbura | csharp-whitespace | stats / stats / disabled | 5476.37 | 1918.47 | 1824.99 - 2023.27 | 2023.27 | 15 / 0 | 17 | 0 |
| PurityUIDashborad / Sidebar.akbura | csharp-whitespace | stats / stats / enabled | 5601.42 | 10.45 | 9.62 - 14.75 | 14.75 | 0 / 15 | 0 | 0 |
| PurityUIDashborad / Sidebar.akbura | global-using | stats / stats / disabled | 5494.58 | 2002.21 | 1954.73 - 2094.84 | 2094.84 | 15 / 0 | 17 | 15 |
| PurityUIDashborad / Sidebar.akbura | global-using | stats / stats / enabled | 5924.09 | 1928.01 | 1841.80 - 2142.55 | 2142.55 | 15 / 0 | 17 | 15 |
| PurityUIDashborad / Sidebar.akbura | overload | stats / stats / disabled | 5438.47 | 1935.15 | 1847.50 - 2162.29 | 2162.29 | 15 / 0 | 17 | 0 |
| PurityUIDashborad / Sidebar.akbura | overload | stats / stats / enabled | 6062.31 | 1952.18 | 1868.99 - 2091.02 | 2091.02 | 15 / 0 | 17 | 0 |
| PurityUIDashborad / Sidebar.akbura | reference-compatible | stats / stats / disabled | 5515.24 | 1978.29 | 1917.16 - 2168.50 | 2168.50 | 15 / 0 | 17 | 0 |
| PurityUIDashborad / Sidebar.akbura | reference-compatible | stats / stats / enabled | 6060.72 | 1969.46 | 1856.36 - 2220.75 | 2220.75 | 15 / 0 | 17 | 0 |
| PurityUIDashborad / Sidebar.akbura | referenced-body | stats / stats / disabled | 5761.73 | 2076.25 | 1867.91 - 2351.10 | 2351.10 | 15 / 0 | 17 | 0 |
| PurityUIDashborad / Sidebar.akbura | referenced-body | stats / stats / enabled | 5554.75 | 1947.10 | 1870.34 - 2121.80 | 2121.80 | 15 / 0 | 17 | 0 |
| PurityUIDashborad / Sidebar.akbura | referenced-signature | stats / stats / disabled | 5639.96 | 1940.85 | 1855.89 - 2193.92 | 2193.92 | 15 / 0 | 17 | 0 |
| PurityUIDashborad / Sidebar.akbura | referenced-signature | stats / stats / enabled | 5630.55 | 1941.00 | 1851.93 - 2297.06 | 2297.06 | 15 / 0 | 17 | 0 |
| PurityUIDashborad / Sidebar.akbura | remove-tree | stats / stats / disabled | 5546.46 | 1958.25 | 1840.86 - 2763.59 | 2763.59 | 15 / 0 | 17 | 0 |
| PurityUIDashborad / Sidebar.akbura | remove-tree | stats / stats / enabled | 5728.11 | 2023.09 | 1921.58 - 2214.04 | 2214.04 | 15 / 0 | 17 | 0 |
| PurityUIDashborad / Sidebar.akbura | repeat | stats / stats / disabled | 5581.27 | 2.04 | 1.89 - 3.21 | 3.21 | 0 / 0 | 0 | 0 |
| PurityUIDashborad / Sidebar.akbura | repeat | stats / stats / enabled | 5612.08 | 2.13 | 1.93 - 2.85 | 2.85 | 0 / 0 | 0 | 0 |
| PurityUIDashborad / Sidebar.akbura | revert | stats / stats / disabled | 5621.40 | 2129.41 | 1880.76 - 2727.48 | 2727.48 | 15 / 0 | 17 | 0 |
| PurityUIDashborad / Sidebar.akbura | revert | stats / stats / enabled | 5795.12 | 11.78 | 10.69 - 13.29 | 13.29 | 0 / 15 | 0 | 0 |
| PurityUIDashborad / Sidebar.akbura | unchanged | stats / stats / disabled | 5617.73 | 2.11 | 1.81 - 3.72 | 3.72 | 0 / 0 | 0 | 0 |
| PurityUIDashborad / Sidebar.akbura | unchanged | stats / stats / enabled | 5598.87 | 2.09 | 1.85 - 2.98 | 2.98 | 0 / 0 | 0 | 0 |
| PurityUIDashborad / Sidebar.akbura | unrelated-body | stats / stats / disabled | 5517.81 | 1951.83 | 1841.51 - 2308.72 | 2308.72 | 15 / 0 | 17 | 0 |
| PurityUIDashborad / Sidebar.akbura | unrelated-body | stats / stats / enabled | 5656.20 | 10.59 | 9.85 - 15.41 | 15.41 | 0 / 15 | 0 | 0 |
| PurityUIDashborad / Sidebar.akbura | unresolved | stats / stats / disabled | 5869.33 | 1963.97 | 1882.37 - 2224.94 | 2224.94 | 15 / 0 | 17 | 1 |
| PurityUIDashborad / Sidebar.akbura | unresolved | stats / stats / enabled | 5638.11 | 1943.17 | 1890.72 - 2107.65 | 2107.65 | 15 / 0 | 17 | 1 |

All stage durations and allocations are inclusive. Do not sum nested or parallel stages. With n=8, nearest-rank p95 equals the maximum.
<!-- END GENERATED: invalidation -->

## Lifetime and concurrency

The separate large-project sequence keeps one current driver while replacing a C# leaf tree. Each revision has a fresh oracle, allocation and forced-GC heap observation. Two real-project driver branches are then generated concurrently; both timed generations finish before either fresh oracle starts. Finally, a non-inlined helper releases every driver/compiler and returns only the newest detached owner plus weak observations. Repeated full collections must collect every compilation-bound target while retaining at most one distinct detached generation. The raw lifetime result asserts these conditions rather than inferring them from top-level entry counts.

Small-project unit tests separately cover 100 and 500 revisions, including markup changes, cancellation at three stages, stale/concurrent publication, removed documents and retained current requests. [Verification commands and results](verification/README.md) are kept independently from timing runs.

### Active-session heap and work

There are 206 sequential heap observations: 101 per Gallery mode (initial plus 100 revisions), and two per Purity mode (initial plus one revision). Every observation has a fresh oracle; the eight concurrent branches add eight more, giving 214 lifetime/fork comparisons. MiB means 1,048,576 bytes. Values below come from [sequential samples](artifact-memory-samples.csv); allocation medians exclude the initial pass.

| Workload / mode | Revisions | Allocated MiB per update, median | Initial heap MiB | First edited heap MiB | Final edited heap MiB | Edited heap range MiB | Active owners after each edit | Entries / weak identity slots |
| --- | ---: | ---: | ---: | ---: | ---: | --- | ---: | --- |
| Gallery disabled | 100 | 2,855.60 | 244.23 | 358.07 | 380.00 | 358.07-380.42 | 2 | 0 / 0 |
| Gallery enabled | 100 | 7.32 | 265.22 | 266.35 | 173.94 | 170.70-266.35 | 2 | 33 / 193 |
| Purity disabled | 1 | 904.13 | 117.70 | 174.88 | 174.88 | Single edit | 2 | 0 / 0 |
| Purity enabled | 1 | 4.11 | 121.35 | 123.39 | 123.39 | Single edit | 2 | 15 / 194 |

The active driver legitimately retains current/adjacent incremental evaluation state. Both modes had one observed owner initially and exactly two after every edit, not a growing owner count. Gallery's enabled heap was 175.23 MiB at revision 10, 175.03 at 25, 173.63 at 50, 173.17 at 75 and 173.94 at 100; disabled was 378.91, 378.44, 379.43, 379.30 and 380.00 MiB respectively. There was no revision-proportional growth in this finite sequence. The owner-only check below distinguishes those legitimate active-driver roots from retention by the artifact cache itself.

Gallery's edited working-set ranges were 927.95-1,195.50 MiB disabled and 445.56-965.10 MiB enabled; the final values were 977.26 and 530.83 MiB. Purity's single edited values were 454.86 and 394.18 MiB. Working set includes allocator/native/runtime state and need not drop with the live managed heap. GC counts are preserved per generation in the samples and include oracle/forced collections: Gallery's cumulative final Gen0/Gen1/Gen2 counts were 121,419/16,925/519 disabled and 74,253/14,366/1,084 enabled. The higher enabled Gen2 count is not evidence of a cache leak or a production GC-frequency estimate; this harness deliberately forces collections around full verification work.

### Isolated newest-owner result

After the session helper returned, only the newest artifact store and weak observations remained. Six repeated full-GC attempts ran before the [final assertions](artifact-memory-results.json):

| Workload / mode | C# compilations observed / alive | Akbura compilations observed / alive | Bound snapshots observed / alive | Models and each probe category observed / alive | Distinct detached generations alive | Final managed heap MiB | Working set MiB |
| --- | --- | --- | --- | --- | ---: | ---: | ---: |
| Gallery disabled | 103 / 0 | 103 / 0 | 103 / 0 | 3,193 / 0 | 0 | 32.49 | 1,690.29 |
| Gallery enabled | 103 / 0 | 103 / 0 | 103 / 0 | 93 / 0 | 1 | 51.34 | 886.34 |
| Purity disabled | 4 / 0 | 4 / 0 | 4 / 0 | 60 / 0 | 0 | 27.96 | 529.02 |
| Purity enabled | 4 / 0 | 4 / 0 | 4 / 0 | 60 / 0 | 1 | 29.25 | 444.15 |

Indexes and compilation-specific project states also had zero live targets in every group. Observation counts describe weak observations, not a claim that all observed targets were distinct. All 103 Gallery detached-snapshot observations resolved to at most one distinct live generation enabled, none disabled. The enabled owner still held 33 Gallery entries/193 weak slots or 15 Purity entries/194 weak slots. No historical chain or compilation-bound target was retained by that deliberately live owner. All lifetime assertions passed; no external surviving root prevented the isolated proof.

Keeping current artifacts is not free: the enabled owner-only process heap exceeded the disabled observation by 18.85 MiB for Gallery and 1.29 MiB for Purity. These are separate-process whole-heap observations, not an exact retained-size attribution to the cache. They demonstrate the trade-off without confusing lower update allocations with zero retained cache cost. The lower active-driver heap and the slightly larger isolated-owner heap answer different questions.

### Concurrent branch observations

Both branches changed markup as well as the C# leaf, so they intentionally failed the detached-input proof and bound independently. Both branches completed before their fresh oracles started. The [concurrent rows](artifact-concurrent.csv) contain one two-branch observation per project/mode, not eight-sample medians:

| Workload | Disabled aggregate ms | Enabled aggregate ms | Change | Models, disabled / enabled | Enabled source hits / fallbacks | Allocated MiB, disabled / enabled |
| --- | ---: | ---: | ---: | --- | --- | --- |
| Gallery, two branches | 21,964.63 | 22,352.66 | +1.77% | 62 / 62 | 0 / 66 | 6,216.06 / 6,167.61 |
| Purity, two branches | 5,059.17 | 6,704.60 | +32.52% | 30 / 30 | 0 / 30 | 1,856.68 / 1,855.12 |

There is no demonstrated concurrent speedup. In particular, the Purity enabled observation was slower and must not be hidden or described as neutral overhead. One fixed-order pair cannot establish a stable regression magnitude or isolate its cause; a dedicated repeated concurrency benchmark would be needed before drawing that conclusion. Correct outputs, complete publication and collection of abandoned branch contexts were verified independently of timing. These forks are not a Visual Studio/IDE scheduling measurement.

### Lifetime acceptance summary

| Scenario | Revisions / attempts / branches | Old compilation-bound objects collectible | Historical snapshot chain |
| --- | --- | --- | --- |
| Sequential unit updates | 100, package and SDK hosts | Verified, newest store kept alive | None |
| Long unit session | 500, package and SDK hosts | Verified, newest store kept alive | None |
| Canceled revisions and successful retry | Five attempts at each of three stages | Verified; no canceled publication | None |
| Concurrent unit forks | Four branches | Verified after branch release | None |
| Large Gallery session | 100 revisions plus two forks, each mode | Verified, 103 compilation observations per mode | None |
| Purity real-project forks | One revision plus two forks, each mode | Verified, four compilation observations per mode | None |

This is finite stress-test evidence plus a transitive field/reference audit, not a claim that an arbitrary external host cannot retain old drivers. The complete unit commands, TRX counts, exclusions and the intentionally failing pre-fix test are in [verification](verification/README.md).

## Reproduce

Run sequentially from the repository root, without concurrent builds/tests:

```powershell
& ./benchmarks/HotReload/Run.ps1 -Suite artifacts -Prepare -OutputDirectory bin/CrossCompilationReuse/matrix -InvalidationProfiling stats -InvalidationScenario @('akbura', 'unchanged', 'unrelated-body', 'referenced-body', 'csharp-whitespace', 'csharp-trivia', 'referenced-signature', 'overload', 'const', 'global-using', 'reference-compatible', 'add-tree', 'remove-tree', 'unresolved', 'repeat', 'revert')
& ./benchmarks/HotReload/Run.ps1 -Suite retention -SkipPurity -RetentionRevisions 100 -OutputDirectory bin/CrossCompilationReuse/matrix
foreach ($mode in @('disabled', 'enabled')) {
    & dotnet ./benchmarks/HotReload/bin/Debug/net10.0/HotReloadBenchmarks.dll artifact-memory ./bin/CrossCompilationReuse/matrix/purity-inputs.json Sidebar.akbura 1 $mode |
        Set-Content -LiteralPath "./bin/CrossCompilationReuse/matrix/artifact-memory-purity-$mode.jsonl" -Encoding UTF8
    if ($LASTEXITCODE -ne 0) {
        throw "Purity retention failed in $mode mode."
    }
}
& ./benchmarks/HotReload/Summarize-Invalidation.ps1 -InputDirectory bin/CrossCompilationReuse/matrix -ResultDirectory benchmarks/HotReload/results/2026-10-17-artifact-reuse
```

## Limits

The optimized leaf is synthetic but inserted into real Gallery/Purity inputs. It proves isolated numeric-return body/trivia reuse, not that arbitrary ViewModel edits or IDE compiler configurations are fast. General method bodies, declarations, external references/options, unknown dependency syntax and custom compiler providers/resolvers fall back to current-compilation binding. Narrowing this fallback requires another complete semantic dependency proof, not an output-text comparison. No old semantic plan/model/symbol is imported across compilations. GC evidence is finite stress testing plus a field/indirect-reference audit, not a formal proof of every possible host retention path. Holding old drivers externally legitimately keeps their compiler graphs alive.
