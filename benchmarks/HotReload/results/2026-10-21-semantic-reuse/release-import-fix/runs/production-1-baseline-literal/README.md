# C# invalidation measurements

See the [engineering investigation](../../../../../../../For%20Contributors/csharp-invalidation-investigation.md) for methodology, environment, interpretation and limitations.

<!-- BEGIN GENERATED: invalidation -->
Recorded 44 driver runs; 4 workload/mode groups; 44 fresh-driver parity checks. Each group has two warm-ups and eight measured samples.

| Project / file | Scenario | Binary / profiling | Initial ms | Median ms | Range ms | p95 ms | Components generated / reused | Diagnostics evaluated | Changed texts |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Akbura.FeatureGallery / Counter.akbura | localized-literal | production / off / enabled | 12986.94 | 210.29 | 202.44 - 215.92 | 215.92 | not measured / not measured | not measured | 1 |
| Akbura.FeatureGallery / DataTemplatesPage.akbura | localized-literal | production / off / enabled | 13093.38 | 3498.41 | 3450.85 - 3584.15 | 3584.15 | not measured / not measured | not measured | 1 |
| PurityUIDashborad / NeedHelp.akbura | localized-literal | production / off / enabled | 5529.49 | 180.36 | 147.26 - 243.91 | 243.91 | not measured / not measured | not measured | 1 |
| PurityUIDashborad / Sidebar.akbura | localized-literal | production / off / enabled | 5442.51 | 571.30 | 497.26 - 799.69 | 799.69 | not measured / not measured | not measured | 1 |

All stage durations and allocations are inclusive. Do not sum nested or parallel stages. With n=8, nearest-rank p95 equals the maximum.
<!-- END GENERATED: invalidation -->
