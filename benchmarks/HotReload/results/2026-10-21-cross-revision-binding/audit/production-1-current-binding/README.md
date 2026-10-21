# C# invalidation measurements

See the [engineering investigation](../../../../../../For%20Contributors/csharp-invalidation-investigation.md) for methodology, environment, interpretation and limitations.

<!-- BEGIN GENERATED: invalidation -->
Recorded 22 driver runs; 2 workload/mode groups; 22 fresh-driver parity checks. Each group has two warm-ups and eight measured samples.

| Project / file | Scenario | Binary / profiling | Initial ms | Median ms | Range ms | p95 ms | Components generated / reused | Diagnostics evaluated | Changed texts |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Akbura.FeatureGallery / DataTemplates.akbura | localized-binding-sibling | production / off / enabled | 13080.93 | 383.09 | 374.52 - 410.46 | 410.46 | not measured / not measured | not measured | 1 |
| PurityUIDashborad / NavButton.akbura | localized-binding-sibling | production / off / enabled | 5393.57 | 123.17 | 111.29 - 215.32 | 215.32 | not measured / not measured | not measured | 1 |

All stage durations and allocations are inclusive. Do not sum nested or parallel stages. With n=8, nearest-rank p95 equals the maximum.
<!-- END GENERATED: invalidation -->
