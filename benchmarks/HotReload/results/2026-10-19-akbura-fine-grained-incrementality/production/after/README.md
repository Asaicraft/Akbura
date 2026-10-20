# C# invalidation measurements

See the [engineering investigation](../../../../../../For%20Contributors/csharp-invalidation-investigation.md) for methodology, environment, interpretation and limitations.

<!-- BEGIN GENERATED: invalidation -->
Recorded 66 driver runs; 6 workload/mode groups; 66 fresh-driver parity checks. Each group has two warm-ups and eight measured samples.

| Project / file | Scenario | Binary / profiling | Initial ms | Median ms | Range ms | p95 ms | Components generated / reused | Diagnostics evaluated | Changed texts |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Akbura.FeatureGallery / Counter.akbura | localized-binding-sibling | production / off / enabled | 13908.49 | 226.00 | 208.52 - 319.57 | 319.57 | not measured / not measured | not measured | 1 |
| Akbura.FeatureGallery / Counter.akbura | localized-diagnostic | production / off / enabled | 14027.52 | 252.39 | 235.29 - 282.22 | 282.22 | not measured / not measured | not measured | 1 |
| Akbura.FeatureGallery / Counter.akbura | localized-literal | production / off / enabled | 15449.87 | 249.04 | 220.23 - 288.25 | 288.25 | not measured / not measured | not measured | 1 |
| PurityUIDashborad / NavButton.akbura | localized-binding-sibling | production / off / enabled | 6011.82 | 178.09 | 127.44 - 229.25 | 229.25 | not measured / not measured | not measured | 1 |
| PurityUIDashborad / Sidebar.akbura | localized-diagnostic | production / off / enabled | 6701.49 | 640.87 | 514.06 - 1143.99 | 1143.99 | not measured / not measured | not measured | 1 |
| PurityUIDashborad / Sidebar.akbura | localized-literal | production / off / enabled | 7457.52 | 634.38 | 508.69 - 806.87 | 806.87 | not measured / not measured | not measured | 1 |

All stage durations and allocations are inclusive. Do not sum nested or parallel stages. With n=8, nearest-rank p95 equals the maximum.
<!-- END GENERATED: invalidation -->
