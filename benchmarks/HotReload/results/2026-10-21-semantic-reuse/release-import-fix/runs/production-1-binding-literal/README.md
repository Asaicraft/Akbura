# C# invalidation measurements

See the [engineering investigation](../../../../../../../For%20Contributors/csharp-invalidation-investigation.md) for methodology, environment, interpretation and limitations.

<!-- BEGIN GENERATED: invalidation -->
Recorded 44 driver runs; 4 workload/mode groups; 44 fresh-driver parity checks. Each group has two warm-ups and eight measured samples.

| Project / file | Scenario | Binary / profiling | Initial ms | Median ms | Range ms | p95 ms | Components generated / reused | Diagnostics evaluated | Changed texts |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Akbura.FeatureGallery / Counter.akbura | localized-literal | production / off / enabled | 13065.99 | 207.13 | 197.71 - 219.26 | 219.26 | not measured / not measured | not measured | 1 |
| Akbura.FeatureGallery / DataTemplatesPage.akbura | localized-literal | production / off / enabled | 13143.23 | 3458.19 | 3432.19 - 3577.77 | 3577.77 | not measured / not measured | not measured | 1 |
| PurityUIDashborad / NeedHelp.akbura | localized-literal | production / off / enabled | 5647.26 | 163.51 | 141.27 - 308.07 | 308.07 | not measured / not measured | not measured | 1 |
| PurityUIDashborad / Sidebar.akbura | localized-literal | production / off / enabled | 5440.84 | 562.13 | 502.88 - 801.90 | 801.90 | not measured / not measured | not measured | 1 |

All stage durations and allocations are inclusive. Do not sum nested or parallel stages. With n=8, nearest-rank p95 equals the maximum.
<!-- END GENERATED: invalidation -->
