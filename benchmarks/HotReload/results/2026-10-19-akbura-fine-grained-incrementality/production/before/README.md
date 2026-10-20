# C# invalidation measurements

See the [engineering investigation](../../../../../../For%20Contributors/csharp-invalidation-investigation.md) for methodology, environment, interpretation and limitations.

<!-- BEGIN GENERATED: invalidation -->
Recorded 66 driver runs; 6 workload/mode groups; 66 fresh-driver parity checks. Each group has two warm-ups and eight measured samples.

| Project / file | Scenario | Binary / profiling | Initial ms | Median ms | Range ms | p95 ms | Components generated / reused | Diagnostics evaluated | Changed texts |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Akbura.FeatureGallery / Counter.akbura | localized-binding-sibling | production / off / enabled | 15351.47 | 227.66 | 221.47 - 260.08 | 260.08 | not measured / not measured | not measured | 1 |
| Akbura.FeatureGallery / Counter.akbura | localized-diagnostic | production / off / enabled | 15325.29 | 230.04 | 213.03 - 246.42 | 246.42 | not measured / not measured | not measured | 1 |
| Akbura.FeatureGallery / Counter.akbura | localized-literal | production / off / enabled | 16257.26 | 240.25 | 221.59 - 244.86 | 244.86 | not measured / not measured | not measured | 1 |
| PurityUIDashborad / NavButton.akbura | localized-binding-sibling | production / off / enabled | 6909.30 | 163.64 | 136.95 - 217.79 | 217.79 | not measured / not measured | not measured | 1 |
| PurityUIDashborad / Sidebar.akbura | localized-diagnostic | production / off / enabled | 6927.97 | 693.54 | 588.48 - 973.08 | 973.08 | not measured / not measured | not measured | 1 |
| PurityUIDashborad / Sidebar.akbura | localized-literal | production / off / enabled | 6543.35 | 726.53 | 620.78 - 984.28 | 984.28 | not measured / not measured | not measured | 1 |

All stage durations and allocations are inclusive. Do not sum nested or parallel stages. With n=8, nearest-rank p95 equals the maximum.
<!-- END GENERATED: invalidation -->
