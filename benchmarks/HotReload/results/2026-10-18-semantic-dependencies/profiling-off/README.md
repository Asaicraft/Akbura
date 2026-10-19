# C# invalidation measurements

See the [engineering investigation](../../../../../For%20Contributors/csharp-invalidation-investigation.md) for methodology, environment, interpretation and limitations.

<!-- BEGIN GENERATED: invalidation -->
Recorded 88 driver runs; 8 workload/mode groups; 88 fresh-driver parity checks. Each group has two warm-ups and eight measured samples.

| Project / file | Scenario | Binary / profiling | Initial ms | Median ms | Range ms | p95 ms | Components generated / reused | Diagnostics evaluated | Changed texts |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Akbura.FeatureGallery / Counter.akbura | akbura | stats / off / disabled | 14695.79 | 225.46 | 221.06 - 231.89 | 231.89 | not measured / not measured | not measured | 1 |
| Akbura.FeatureGallery / Counter.akbura | akbura | stats / off / enabled | 14842.89 | 224.90 | 217.67 - 236.80 | 236.80 | not measured / not measured | not measured | 1 |
| Akbura.FeatureGallery / Counter.akbura | semantic-signature | stats / off / disabled | 15094.14 | 8479.34 | 8402.77 - 8524.18 | 8524.18 | not measured / not measured | not measured | 0 |
| Akbura.FeatureGallery / Counter.akbura | semantic-signature | stats / off / enabled | 15129.06 | 8545.54 | 8485.24 - 8616.70 | 8616.70 | not measured / not measured | not measured | 0 |
| PurityUIDashborad / Sidebar.akbura | akbura | stats / off / disabled | 5954.84 | 635.05 | 620.84 - 924.12 | 924.12 | not measured / not measured | not measured | 1 |
| PurityUIDashborad / Sidebar.akbura | akbura | stats / off / enabled | 5949.93 | 653.90 | 629.09 - 777.23 | 777.23 | not measured / not measured | not measured | 1 |
| PurityUIDashborad / Sidebar.akbura | semantic-signature | stats / off / disabled | 5908.94 | 2005.80 | 1907.77 - 2230.76 | 2230.76 | not measured / not measured | not measured | 0 |
| PurityUIDashborad / Sidebar.akbura | semantic-signature | stats / off / enabled | 5892.88 | 2078.78 | 1963.63 - 2234.43 | 2234.43 | not measured / not measured | not measured | 0 |

All stage durations and allocations are inclusive. Do not sum nested or parallel stages. With n=8, nearest-rank p95 equals the maximum.
<!-- END GENERATED: invalidation -->
