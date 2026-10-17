# C# invalidation measurements

See the [engineering investigation](../../../../../For%20Contributors/csharp-invalidation-investigation.md) for methodology, environment, interpretation and limitations.

Controls use the same unrelated-body edit and evaluated inputs as the main matrix. Each project runs production/off → stats/off → stats-only → full trace, in separate processes. [Order/timestamps](control-order.json) also include the preceding NeedHelp subset. Binaries and input hashes are in [provenance](invalidation-provenance.json); missing off-mode counters are not zero work.

All modes pass full source/diagnostic parity and produce zero changed generated texts. Production itself takes 10,015.58 ms in Gallery and 2,555.09 ms in Purity, so the multi-second cost does not originate in tracing. The overlapping ranges and ordered-run drift do not support a precise causal overhead percentage. Full trace has a 3.59% higher Gallery median than stats-only, but lower medians than production in both projects; these are observations, not speedup claims. Process-allocation differences are also observations rather than isolated per-event costs.

[Summary](invalidation-summary.csv), [samples](invalidation-samples.csv), [inclusive stages](invalidation-stages.csv), [compact transitions](invalidation-transitions.csv), [all tracked steps](invalidation-steps.csv), and [compressed raw records](invalidation-traces.jsonl.gz) preserve the measured data. Initial passes are not cold filesystem/OS measurements. No tests/builds overlapped these timing runs.

<!-- BEGIN GENERATED: invalidation -->
Recorded 88 driver runs; 8 workload/mode groups; 16 fresh-driver parity checks. Each group has two warm-ups and eight measured samples.

| Project / file | Scenario | Binary / profiling | Initial ms | Median ms | Range ms | p95 ms | Components generated / reused | Diagnostics evaluated | Changed texts |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Akbura.FeatureGallery / Counter.akbura | unrelated-body | production / off | 23737.62 | 10015.58 | 9884.38 - 11002.64 | 11002.64 | not measured / not measured | not measured | 0 |
| Akbura.FeatureGallery / Counter.akbura | unrelated-body | stats / off | 21193.66 | 9405.26 | 8688.30 - 10097.02 | 10097.02 | not measured / not measured | not measured | 0 |
| Akbura.FeatureGallery / Counter.akbura | unrelated-body | stats / stats | 15288.92 | 8911.58 | 8492.63 - 9923.83 | 9923.83 | 28 / 0 | 32 | 0 |
| Akbura.FeatureGallery / Counter.akbura | unrelated-body | stats / trace | 15791.97 | 9231.64 | 8874.20 - 9445.58 | 9445.58 | 28 / 0 | 32 | 0 |
| PurityUIDashborad / Sidebar.akbura | unrelated-body | production / off | 9650.61 | 2555.09 | 2283.11 - 3664.37 | 3664.37 | not measured / not measured | not measured | 0 |
| PurityUIDashborad / Sidebar.akbura | unrelated-body | stats / off | 7082.16 | 2321.20 | 2103.25 - 3257.12 | 3257.12 | not measured / not measured | not measured | 0 |
| PurityUIDashborad / Sidebar.akbura | unrelated-body | stats / stats | 6219.09 | 2438.18 | 2178.34 - 3394.86 | 3394.86 | 15 / 0 | 17 | 0 |
| PurityUIDashborad / Sidebar.akbura | unrelated-body | stats / trace | 7820.38 | 2346.04 | 2152.15 - 3625.66 | 3625.66 | 15 / 0 | 17 | 0 |

All stage durations and allocations are inclusive. Do not sum nested or parallel stages. With n=8, nearest-rank p95 equals the maximum.
<!-- END GENERATED: invalidation -->
