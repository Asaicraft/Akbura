# C# invalidation measurements

See the [engineering investigation](../../../../../../../For%20Contributors/csharp-invalidation-investigation.md) for methodology, environment, interpretation and limitations.

<!-- BEGIN GENERATED: invalidation -->
Recorded 44 driver runs; 4 workload/mode groups; 44 fresh-driver parity checks. Each group has two warm-ups and eight measured samples.

| Project / file | Scenario | Binary / profiling | Initial ms | Median ms | Range ms | p95 ms | Components generated / reused | Diagnostics evaluated | Changed texts |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Akbura.FeatureGallery / Counter.akbura | localized-literal | stats / stats / enabled | 13546.63 | 229.11 | 219.70 - 230.46 | 230.46 | 1 / 27 | 1 | 1 |
| Akbura.FeatureGallery / DataTemplatesPage.akbura | localized-literal | stats / stats / enabled | 13954.11 | 3723.00 | 3675.82 - 3830.58 | 3830.58 | 1 / 27 | 1 | 1 |
| PurityUIDashborad / NeedHelp.akbura | localized-literal | stats / stats / enabled | 5407.97 | 177.60 | 168.30 - 289.28 | 289.28 | 1 / 14 | 1 | 1 |
| PurityUIDashborad / Sidebar.akbura | localized-literal | stats / stats / enabled | 5424.24 | 587.42 | 559.78 - 843.61 | 843.61 | 1 / 14 | 1 | 1 |

All stage durations and allocations are inclusive. Do not sum nested or parallel stages. With n=8, nearest-rank p95 equals the maximum.
<!-- END GENERATED: invalidation -->
