# C# invalidation measurements

See the [engineering investigation](../../../../For%20Contributors/csharp-invalidation-investigation.md) for methodology, environment, interpretation and limitations.

<!-- BEGIN GENERATED: invalidation -->
Recorded 44 driver runs; 4 workload/mode groups; 44 fresh-driver parity checks. Each group has two warm-ups and eight measured samples.

| Project / file | Scenario | Binary / profiling | Initial ms | Median ms | Range ms | p95 ms | Components generated / reused | Diagnostics evaluated | Changed texts |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Akbura.FeatureGallery / Counter.akbura | real-referenced-method | stats / trace / disabled | 15444.83 | 9395.46 | 8689.68 - 13688.01 | 13688.01 | 28 / 0 | 32 | 0 |
| Akbura.FeatureGallery / Counter.akbura | real-referenced-method | stats / trace / enabled | 15408.62 | 10849.86 | 8698.66 - 15341.72 | 15341.72 | 28 / 0 | 32 | 0 |
| PurityUIDashborad / Sidebar.akbura | real-referenced-method | stats / trace / disabled | 7257.66 | 2359.47 | 2209.11 - 2628.88 | 2628.88 | 15 / 0 | 17 | 0 |
| PurityUIDashborad / Sidebar.akbura | real-referenced-method | stats / trace / enabled | 8158.11 | 2374.07 | 2234.22 - 2638.90 | 2638.90 | 15 / 0 | 17 | 0 |

All stage durations and allocations are inclusive. Do not sum nested or parallel stages. With n=8, nearest-rank p95 equals the maximum.
<!-- END GENERATED: invalidation -->
