# C# invalidation measurements

See the [engineering investigation](../../../../../../../For%20Contributors/csharp-invalidation-investigation.md) for methodology, environment, interpretation and limitations.

<!-- BEGIN GENERATED: invalidation -->
Recorded 22 driver runs; 2 workload/mode groups; 22 fresh-driver parity checks. Each group has two warm-ups and eight measured samples.

| Project / file | Scenario | Binary / profiling | Initial ms | Median ms | Range ms | p95 ms | Components generated / reused | Diagnostics evaluated | Changed texts |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Akbura.FeatureGallery / DataTemplates.akbura | localized-binding-sibling | stats / stats / enabled | 13445.11 | 422.31 | 416.78 - 433.71 | 433.71 | 1 / 27 | 1 | 1 |
| PurityUIDashborad / NavButton.akbura | localized-binding-sibling | stats / stats / enabled | 5763.30 | 122.90 | 113.68 - 217.98 | 217.98 | 1 / 14 | 1 | 1 |

All stage durations and allocations are inclusive. Do not sum nested or parallel stages. With n=8, nearest-rank p95 equals the maximum.
<!-- END GENERATED: invalidation -->
