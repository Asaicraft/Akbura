# C# invalidation investigation — 2026-10-16–17

See the [engineering investigation](../../../../For%20Contributors/csharp-invalidation-investigation.md) for methodology, environment, interpretation and limitations.

These are generator-driver measurements, not IDE/save-to-render latency. The measured baseline is `7e3967f82c3da21c8755a0323ad66e36617b7418` plus the accompanying opt-in instrumentation. The historical `../2026-10-16` archive is unchanged. The principal result is a reuse boundary: a retained C# edit creates a new project-state owner with no previous backend snapshot. Semantic preparation then runs for every component even when every output compares equal.

Gallery has 28 components, five AKCSS outputs and one project-service output. The pinned tutorial snapshot has 15 components and one project-service output. Main matrix timings use `stats / trace`; compare them with the separate [profiling controls](overhead/README.md), not as production timings or a claimed speedup. Cases run sequentially on a developer desktop and exhibit drift.

Artifacts:

- [Summary](invalidation-summary.csv), [individual samples](invalidation-samples.csv), and [inclusive stage times/allocations](invalidation-stages.csv).
- [Compact per-scenario callback transitions](invalidation-transitions.csv), [all tracked-step observations](invalidation-steps.csv), and [environment causes](invalidation-causes.csv).
- [Compressed raw JSONL](invalidation-traces.jsonl.gz), including scalar events, complete diagnostic validation and fresh-driver source digests.
- [Measured binary/input provenance](invalidation-provenance.json), [workstation/source manifest](invalidation-environment.json), [tutorial revision](purity-source.json), and [harness correction provenance](harness-update.json).
- [Pinned Roslyn source comparison](roslyn-source-manifest.csv), [verification evidence](verification/README.md), and the separately recorded [unsupported global-alias case](global-alias/README.md).

The namespace `global-using` matrix case is valid and passes fresh-driver parity. The alias variant exposed duplicate global aliases (`CS1537`) and is excluded from successful timing summaries. Its production fresh-driver reproduction is kept separately; generator behavior is not changed to hide that limitation.

Each group's initial pass is a fresh driver inside a newly started benchmark process, but not a cold OS/filesystem measurement. Subsequent fresh-driver checks also share process-wide caches. `off` counters are unmeasured, not zero. Stage times/allocations overlap and must not be added. With eight measured samples, p95 is simply the maximum, not an estimate of a stable production tail.

<!-- BEGIN GENERATED: invalidation -->
Recorded 627 driver runs; 57 workload/mode groups; 114 fresh-driver parity checks. Each group has two warm-ups and eight measured samples.

| Project / file | Scenario | Binary / profiling | Initial ms | Median ms | Range ms | p95 ms | Components generated / reused | Diagnostics evaluated | Changed texts |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Akbura.FeatureGallery / Counter.akbura | add-tree | stats / trace | 14831.69 | 8817.36 | 8697.39 - 9490.81 | 9490.81 | 28 / 0 | 32 | 0 |
| Akbura.FeatureGallery / Counter.akbura | akbura-whitespace | stats / trace | 22336.69 | 18.83 | 11.72 - 24.34 | 24.34 | 0 / 0 | 0 | 0 |
| Akbura.FeatureGallery / Counter.akbura | akbura | stats / trace | 21692.46 | 498.94 | 379.22 - 596.35 | 596.35 | 1 / 27 | 1 | 1 |
| Akbura.FeatureGallery / Counter.akbura | attribute | stats / trace | 22989.66 | 13567.13 | 13270.23 - 15038.26 | 15038.26 | 28 / 0 | 32 | 0 |
| Akbura.FeatureGallery / Counter.akbura | compilation-options | stats / trace | 15760.27 | 8890.40 | 8661.71 - 9449.79 | 9449.79 | 28 / 0 | 32 | 0 |
| Akbura.FeatureGallery / Counter.akbura | const | stats / trace | 23595.31 | 14550.05 | 13916.39 - 14995.71 | 14995.71 | 28 / 0 | 32 | 0 |
| Akbura.FeatureGallery / Counter.akbura | csharp-trivia | stats / trace | 22830.74 | 13005.01 | 12673.49 - 13721.67 | 13721.67 | 28 / 0 | 32 | 0 |
| Akbura.FeatureGallery / Counter.akbura | csharp-whitespace | stats / trace | 21891.69 | 12875.11 | 12609.22 - 13027.42 | 13027.42 | 28 / 0 | 32 | 0 |
| Akbura.FeatureGallery / Counter.akbura | fresh-driver | stats / trace | 22802.05 | 13064.47 | 12692.72 - 13358.09 | 13358.09 | 28 / 0 | 32 | 0 |
| Akbura.FeatureGallery / Counter.akbura | global-using | stats / trace | 22591.94 | 14568.45 | 13618.42 - 15277.15 | 15277.15 | 28 / 0 | 32 | 33 |
| Akbura.FeatureGallery / Counter.akbura | inert-tree | stats / trace | 21059.20 | 20.85 | 14.15 - 29.24 | 29.24 | 0 / 0 | 0 | 0 |
| Akbura.FeatureGallery / Counter.akbura | language | stats / trace | 17491.36 | 9301.15 | 9116.70 - 9648.85 | 9648.85 | 28 / 0 | 32 | 0 |
| Akbura.FeatureGallery / Counter.akbura | multi-tree | stats / trace | 15282.17 | 8623.52 | 8387.77 - 9168.28 | 9168.28 | 28 / 0 | 32 | 0 |
| Akbura.FeatureGallery / Counter.akbura | nullable | stats / trace | 15647.90 | 8717.12 | 8547.30 - 8983.49 | 8983.49 | 28 / 0 | 32 | 0 |
| Akbura.FeatureGallery / Counter.akbura | original-add-tree | stats / trace | 23102.08 | 13092.43 | 12857.61 - 13406.73 | 13406.73 | 28 / 0 | 32 | 0 |
| Akbura.FeatureGallery / Counter.akbura | overload | stats / trace | 19061.52 | 14427.49 | 13525.35 - 16993.19 | 16993.19 | 28 / 0 | 32 | 0 |
| Akbura.FeatureGallery / Counter.akbura | preprocessor | stats / trace | 18757.58 | 9685.78 | 8941.86 - 9893.50 | 9893.50 | 28 / 0 | 32 | 0 |
| Akbura.FeatureGallery / Counter.akbura | reference-compatible | stats / trace | 27981.16 | 13040.05 | 12843.95 - 13639.52 | 13639.52 | 28 / 0 | 32 | 0 |
| Akbura.FeatureGallery / Counter.akbura | reference-incompatible | stats / trace | 23148.70 | 12729.34 | 12361.53 - 14266.30 | 14266.30 | 28 / 0 | 32 | 0 |
| Akbura.FeatureGallery / Counter.akbura | referenced-body | stats / trace | 21156.82 | 12334.33 | 12035.30 - 17888.05 | 17888.05 | 28 / 0 | 32 | 0 |
| Akbura.FeatureGallery / Counter.akbura | referenced-signature | stats / trace | 27502.83 | 12435.56 | 11150.31 - 16539.63 | 16539.63 | 28 / 0 | 32 | 0 |
| Akbura.FeatureGallery / Counter.akbura | remove-tree | stats / trace | 15458.88 | 8664.38 | 8327.23 - 9254.11 | 9254.11 | 28 / 0 | 32 | 0 |
| Akbura.FeatureGallery / Counter.akbura | repeat | stats / trace | 15277.00 | 13.27 | 8.22 - 14.01 | 14.01 | 0 / 0 | 0 | 0 |
| Akbura.FeatureGallery / Counter.akbura | revert | stats / trace | 16461.16 | 8736.72 | 8605.48 - 8921.92 | 8921.92 | 28 / 0 | 32 | 0 |
| Akbura.FeatureGallery / Counter.akbura | unchanged | stats / trace | 21796.14 | 17.63 | 10.99 - 25.86 | 25.86 | 0 / 0 | 0 | 0 |
| Akbura.FeatureGallery / Counter.akbura | unrelated-body | stats / trace | 21961.40 | 12698.99 | 12583.44 - 13070.97 | 13070.97 | 28 / 0 | 32 | 0 |
| Akbura.FeatureGallery / Counter.akbura | unresolved | stats / trace | 22324.88 | 8958.48 | 8681.77 - 9622.84 | 9622.84 | 28 / 0 | 32 | 1 |
| PurityUIDashborad / NeedHelp.akbura | akbura-whitespace | stats / trace | 9246.96 | 6.80 | 6.46 - 7.44 | 7.44 | 0 / 0 | 0 | 0 |
| PurityUIDashborad / NeedHelp.akbura | akbura | stats / trace | 14243.83 | 411.87 | 379.29 - 480.59 | 480.59 | 1 / 14 | 1 | 1 |
| PurityUIDashborad / NeedHelp.akbura | unrelated-body | stats / trace | 6875.75 | 2781.05 | 2509.84 - 4350.84 | 4350.84 | 15 / 0 | 17 | 0 |
| PurityUIDashborad / Sidebar.akbura | add-tree | stats / trace | 5813.18 | 2152.44 | 1959.71 - 3277.48 | 3277.48 | 15 / 0 | 17 | 0 |
| PurityUIDashborad / Sidebar.akbura | akbura-whitespace | stats / trace | 5949.47 | 7.53 | 7.36 - 20.07 | 20.07 | 0 / 0 | 0 | 0 |
| PurityUIDashborad / Sidebar.akbura | akbura | stats / trace | 5952.88 | 1262.11 | 978.86 - 1413.32 | 1413.32 | 1 / 14 | 1 | 1 |
| PurityUIDashborad / Sidebar.akbura | attribute | stats / trace | 6622.21 | 2257.01 | 2183.13 - 3765.64 | 3765.64 | 15 / 0 | 17 | 0 |
| PurityUIDashborad / Sidebar.akbura | compilation-options | stats / trace | 6314.04 | 2272.72 | 1980.11 - 3451.87 | 3451.87 | 15 / 0 | 17 | 0 |
| PurityUIDashborad / Sidebar.akbura | const | stats / trace | 6503.65 | 2407.16 | 2074.88 - 3833.12 | 3833.12 | 15 / 0 | 17 | 0 |
| PurityUIDashborad / Sidebar.akbura | csharp-trivia | stats / trace | 5848.04 | 2296.95 | 2130.81 - 3436.18 | 3436.18 | 15 / 0 | 17 | 0 |
| PurityUIDashborad / Sidebar.akbura | csharp-whitespace | stats / trace | 6144.64 | 2303.61 | 2116.86 - 3370.45 | 3370.45 | 15 / 0 | 17 | 0 |
| PurityUIDashborad / Sidebar.akbura | fresh-driver | stats / trace | 5915.25 | 2325.45 | 2079.23 - 3443.38 | 3443.38 | 15 / 0 | 17 | 0 |
| PurityUIDashborad / Sidebar.akbura | global-using | stats / trace | 6779.94 | 2198.55 | 1976.55 - 3404.25 | 3404.25 | 15 / 0 | 17 | 15 |
| PurityUIDashborad / Sidebar.akbura | inert-tree | stats / trace | 6695.34 | 4.62 | 4.57 - 11.37 | 11.37 | 0 / 0 | 0 | 0 |
| PurityUIDashborad / Sidebar.akbura | language | stats / trace | 6288.94 | 2470.58 | 2299.20 - 3471.68 | 3471.68 | 15 / 0 | 17 | 0 |
| PurityUIDashborad / Sidebar.akbura | multi-tree | stats / trace | 5660.55 | 2224.59 | 1925.15 - 3303.67 | 3303.67 | 15 / 0 | 17 | 0 |
| PurityUIDashborad / Sidebar.akbura | nullable | stats / trace | 5846.60 | 2278.75 | 2035.23 - 3419.12 | 3419.12 | 15 / 0 | 17 | 0 |
| PurityUIDashborad / Sidebar.akbura | original-add-tree | stats / trace | 6690.81 | 2232.33 | 2101.90 - 3401.83 | 3401.83 | 15 / 0 | 17 | 0 |
| PurityUIDashborad / Sidebar.akbura | overload | stats / trace | 6587.43 | 2333.20 | 2108.42 - 3470.96 | 3470.96 | 15 / 0 | 17 | 0 |
| PurityUIDashborad / Sidebar.akbura | preprocessor | stats / trace | 5647.92 | 2385.05 | 2207.06 - 3342.06 | 3342.06 | 15 / 0 | 17 | 0 |
| PurityUIDashborad / Sidebar.akbura | reference-compatible | stats / trace | 5658.96 | 2281.03 | 1970.62 - 3354.65 | 3354.65 | 15 / 0 | 17 | 0 |
| PurityUIDashborad / Sidebar.akbura | reference-incompatible | stats / trace | 5498.07 | 2359.81 | 2010.80 - 3356.03 | 3356.03 | 15 / 0 | 17 | 0 |
| PurityUIDashborad / Sidebar.akbura | referenced-body | stats / trace | 6005.49 | 2399.70 | 2193.78 - 3839.57 | 3839.57 | 15 / 0 | 17 | 0 |
| PurityUIDashborad / Sidebar.akbura | referenced-signature | stats / trace | 6270.74 | 2496.19 | 2048.22 - 3687.12 | 3687.12 | 15 / 0 | 17 | 0 |
| PurityUIDashborad / Sidebar.akbura | remove-tree | stats / trace | 5615.34 | 2316.29 | 2140.05 - 3325.94 | 3325.94 | 15 / 0 | 17 | 0 |
| PurityUIDashborad / Sidebar.akbura | repeat | stats / trace | 6677.12 | 2.50 | 2.41 - 2.54 | 2.54 | 0 / 0 | 0 | 0 |
| PurityUIDashborad / Sidebar.akbura | revert | stats / trace | 6061.66 | 2156.29 | 1966.45 - 3609.50 | 3609.50 | 15 / 0 | 17 | 0 |
| PurityUIDashborad / Sidebar.akbura | unchanged | stats / trace | 6052.03 | 3.10 | 2.83 - 3.24 | 3.24 | 0 / 0 | 0 | 0 |
| PurityUIDashborad / Sidebar.akbura | unrelated-body | stats / trace | 5919.65 | 2295.80 | 2099.76 - 3544.86 | 3544.86 | 15 / 0 | 17 | 0 |
| PurityUIDashborad / Sidebar.akbura | unresolved | stats / trace | 5810.64 | 2192.10 | 2000.16 - 3347.90 | 3347.90 | 15 / 0 | 17 | 1 |

All stage durations and allocations are inclusive. Do not sum nested or parallel stages. With n=8, nearest-rank p95 equals the maximum.
<!-- END GENERATED: invalidation -->
