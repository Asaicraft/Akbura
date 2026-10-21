# C# invalidation measurements

See the [engineering investigation](../../../../../../../For%20Contributors/csharp-invalidation-investigation.md) for methodology, environment, interpretation and limitations.

<!-- BEGIN GENERATED: invalidation -->
Recorded 44 driver runs; 4 workload/mode groups; 44 fresh-driver parity checks. Each group has two warm-ups and eight measured samples.

| Project / file | Scenario | Binary / profiling | Initial ms | Median ms | Range ms | p95 ms | Components generated / reused | Diagnostics evaluated | Changed texts |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Akbura.FeatureGallery / Counter.akbura | localized-literal | production / off / enabled | 13702.79 | 206.20 | 197.15 - 250.11 | 250.11 | not measured / not measured | not measured | 1 |
| Akbura.FeatureGallery / DataTemplatesPage.akbura | localized-literal | production / off / enabled | 12973.80 | 3520.23 | 3502.29 - 3536.11 | 3536.11 | not measured / not measured | not measured | 1 |
| PurityUIDashborad / NeedHelp.akbura | localized-literal | production / off / enabled | 5474.11 | 153.68 | 143.16 - 230.32 | 230.32 | not measured / not measured | not measured | 1 |
| PurityUIDashborad / Sidebar.akbura | localized-literal | production / off / enabled | 5697.27 | 575.11 | 498.44 - 780.90 | 780.90 | not measured / not measured | not measured | 1 |

All stage durations and allocations are inclusive. Do not sum nested or parallel stages. With n=8, nearest-rank p95 equals the maximum.
<!-- END GENERATED: invalidation -->
