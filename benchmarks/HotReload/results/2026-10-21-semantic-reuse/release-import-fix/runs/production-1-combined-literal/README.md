# C# invalidation measurements

See the [engineering investigation](../../../../../../../For%20Contributors/csharp-invalidation-investigation.md) for methodology, environment, interpretation and limitations.

<!-- BEGIN GENERATED: invalidation -->
Recorded 44 driver runs; 4 workload/mode groups; 44 fresh-driver parity checks. Each group has two warm-ups and eight measured samples.

| Project / file | Scenario | Binary / profiling | Initial ms | Median ms | Range ms | p95 ms | Components generated / reused | Diagnostics evaluated | Changed texts |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Akbura.FeatureGallery / Counter.akbura | localized-literal | production / off / enabled | 13246.14 | 207.48 | 202.57 - 213.45 | 213.45 | not measured / not measured | not measured | 1 |
| Akbura.FeatureGallery / DataTemplatesPage.akbura | localized-literal | production / off / enabled | 12719.20 | 3450.72 | 3399.69 - 3681.65 | 3681.65 | not measured / not measured | not measured | 1 |
| PurityUIDashborad / NeedHelp.akbura | localized-literal | production / off / enabled | 5740.28 | 154.62 | 143.13 - 242.89 | 242.89 | not measured / not measured | not measured | 1 |
| PurityUIDashborad / Sidebar.akbura | localized-literal | production / off / enabled | 5549.31 | 550.76 | 473.68 - 805.92 | 805.92 | not measured / not measured | not measured | 1 |

All stage durations and allocations are inclusive. Do not sum nested or parallel stages. With n=8, nearest-rank p95 equals the maximum.
<!-- END GENERATED: invalidation -->
