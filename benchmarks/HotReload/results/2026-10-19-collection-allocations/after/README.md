# C# invalidation measurements

See the [engineering investigation](../../../../../For%20Contributors/csharp-invalidation-investigation.md) for methodology, environment, interpretation and limitations.

<!-- BEGIN GENERATED: invalidation -->
Recorded 88 driver runs; 8 workload/mode groups; 88 fresh-driver parity checks. Each group has two warm-ups and eight measured samples.

| Project / file | Scenario | Binary / profiling | Initial ms | Median ms | Range ms | p95 ms | Components generated / reused | Diagnostics evaluated | Changed texts |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Akbura.FeatureGallery / Counter.akbura | akbura | stats / stats / disabled | 14493.38 | 217.69 | 213.69 - 225.87 | 225.87 | 1 / 27 | 1 | 1 |
| Akbura.FeatureGallery / Counter.akbura | akbura | stats / stats / enabled | 15271.54 | 234.94 | 229.15 - 251.78 | 251.78 | 1 / 27 | 1 | 1 |
| Akbura.FeatureGallery / Counter.akbura | semantic-signature | stats / stats / disabled | 15075.28 | 8621.99 | 8577.14 - 8796.01 | 8796.01 | 30 / 0 | 34 | 0 |
| Akbura.FeatureGallery / Counter.akbura | semantic-signature | stats / stats / enabled | 15044.35 | 8681.11 | 8640.26 - 8725.69 | 8725.69 | 28 / 2 | 31 | 0 |
| PurityUIDashborad / Sidebar.akbura | akbura | stats / stats / disabled | 5941.01 | 693.04 | 610.09 - 825.21 | 825.21 | 1 / 14 | 1 | 1 |
| PurityUIDashborad / Sidebar.akbura | akbura | stats / stats / enabled | 5966.20 | 681.98 | 607.57 - 846.36 | 846.36 | 1 / 14 | 1 | 1 |
| PurityUIDashborad / Sidebar.akbura | semantic-signature | stats / stats / disabled | 6091.96 | 2176.87 | 2120.54 - 2373.25 | 2373.25 | 17 / 0 | 19 | 0 |
| PurityUIDashborad / Sidebar.akbura | semantic-signature | stats / stats / enabled | 6147.19 | 2221.14 | 2162.30 - 2496.86 | 2496.86 | 15 / 2 | 15 | 0 |

All stage durations and allocations are inclusive. Do not sum nested or parallel stages. With n=8, nearest-rank p95 equals the maximum.
<!-- END GENERATED: invalidation -->
