# C# invalidation measurements

See the [engineering investigation](../../../../../For%20Contributors/csharp-invalidation-investigation.md) for methodology, environment, interpretation and limitations.

<!-- BEGIN GENERATED: invalidation -->
Recorded 88 driver runs; 8 workload/mode groups; 88 fresh-driver parity checks. Each group has two warm-ups and eight measured samples.

| Project / file | Scenario | Binary / profiling | Initial ms | Median ms | Range ms | p95 ms | Components generated / reused | Diagnostics evaluated | Changed texts |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Akbura.FeatureGallery / Counter.akbura | akbura | stats / stats / disabled | 14573.89 | 229.90 | 225.79 - 241.28 | 241.28 | 1 / 27 | 1 | 1 |
| Akbura.FeatureGallery / Counter.akbura | akbura | stats / stats / enabled | 15344.06 | 230.63 | 228.35 - 245.10 | 245.10 | 1 / 27 | 1 | 1 |
| Akbura.FeatureGallery / Counter.akbura | semantic-signature | stats / stats / disabled | 15056.63 | 8496.47 | 8405.66 - 8561.50 | 8561.50 | 30 / 0 | 34 | 0 |
| Akbura.FeatureGallery / Counter.akbura | semantic-signature | stats / stats / enabled | 15272.14 | 8634.75 | 8553.84 - 8794.55 | 8794.55 | 28 / 2 | 31 | 0 |
| PurityUIDashborad / Sidebar.akbura | akbura | stats / stats / disabled | 5763.63 | 660.41 | 625.50 - 786.64 | 786.64 | 1 / 14 | 1 | 1 |
| PurityUIDashborad / Sidebar.akbura | akbura | stats / stats / enabled | 6136.99 | 681.55 | 636.86 - 885.30 | 885.30 | 1 / 14 | 1 | 1 |
| PurityUIDashborad / Sidebar.akbura | semantic-signature | stats / stats / disabled | 5874.61 | 2072.84 | 2021.88 - 2297.16 | 2297.16 | 17 / 0 | 19 | 0 |
| PurityUIDashborad / Sidebar.akbura | semantic-signature | stats / stats / enabled | 6020.90 | 2120.49 | 2010.19 - 2314.60 | 2314.60 | 15 / 2 | 15 | 0 |

All stage durations and allocations are inclusive. Do not sum nested or parallel stages. With n=8, nearest-rank p95 equals the maximum.
<!-- END GENERATED: invalidation -->
