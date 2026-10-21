# C# invalidation measurements

See the [engineering investigation](../../../../../../../For%20Contributors/csharp-invalidation-investigation.md) for methodology, environment, interpretation and limitations.

<!-- BEGIN GENERATED: invalidation -->
Recorded 44 driver runs; 4 workload/mode groups; 44 fresh-driver parity checks. Each group has two warm-ups and eight measured samples.

| Project / file | Scenario | Binary / profiling | Initial ms | Median ms | Range ms | p95 ms | Components generated / reused | Diagnostics evaluated | Changed texts |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Akbura.FeatureGallery / Counter.akbura | localized-literal | stats / stats / enabled | 13321.81 | 225.52 | 220.58 - 236.78 | 236.78 | 1 / 27 | 1 | 1 |
| Akbura.FeatureGallery / DataTemplatesPage.akbura | localized-literal | stats / stats / enabled | 13176.64 | 3702.54 | 3691.59 - 3777.84 | 3777.84 | 1 / 27 | 1 | 1 |
| PurityUIDashborad / NeedHelp.akbura | localized-literal | stats / stats / enabled | 5489.45 | 179.61 | 168.43 - 284.52 | 284.52 | 1 / 14 | 1 | 1 |
| PurityUIDashborad / Sidebar.akbura | localized-literal | stats / stats / enabled | 5579.90 | 577.28 | 556.54 - 809.53 | 809.53 | 1 / 14 | 1 | 1 |

All stage durations and allocations are inclusive. Do not sum nested or parallel stages. With n=8, nearest-rank p95 equals the maximum.
<!-- END GENERATED: invalidation -->
