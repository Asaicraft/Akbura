# C# invalidation measurements

See the [engineering investigation](../../../../../For%20Contributors/csharp-invalidation-investigation.md) for methodology, environment, interpretation and limitations.

<!-- BEGIN GENERATED: invalidation -->
Recorded 44 driver runs; 4 workload/mode groups; 44 fresh-driver parity checks. Each group has two warm-ups and eight measured samples.

| Project / file | Scenario | Binary / profiling | Initial ms | Median ms | Range ms | p95 ms | Components generated / reused | Diagnostics evaluated | Changed texts |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Akbura.FeatureGallery / Counter.akbura | real-signature | stats / stats / disabled | 16115.96 | 9312.79 | 9239.88 - 9679.68 | 9679.68 | 28 / 0 | 32 | 0 |
| Akbura.FeatureGallery / Counter.akbura | real-signature | stats / stats / enabled | 16827.78 | 9434.30 | 9289.86 - 9610.46 | 9610.46 | 28 / 0 | 32 | 0 |
| PurityUIDashborad / Sidebar.akbura | real-signature | stats / stats / disabled | 6615.87 | 2273.31 | 2157.90 - 2447.83 | 2447.83 | 15 / 0 | 17 | 0 |
| PurityUIDashborad / Sidebar.akbura | real-signature | stats / stats / enabled | 6722.17 | 2303.85 | 2185.61 - 2430.29 | 2430.29 | 15 / 0 | 17 | 0 |

All stage durations and allocations are inclusive. Do not sum nested or parallel stages. With n=8, nearest-rank p95 equals the maximum.
<!-- END GENERATED: invalidation -->
