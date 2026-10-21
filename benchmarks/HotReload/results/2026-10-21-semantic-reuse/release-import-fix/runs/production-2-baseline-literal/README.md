# C# invalidation measurements

See the [engineering investigation](../../../../../../../For%20Contributors/csharp-invalidation-investigation.md) for methodology, environment, interpretation and limitations.

<!-- BEGIN GENERATED: invalidation -->
Recorded 44 driver runs; 4 workload/mode groups; 44 fresh-driver parity checks. Each group has two warm-ups and eight measured samples.

| Project / file | Scenario | Binary / profiling | Initial ms | Median ms | Range ms | p95 ms | Components generated / reused | Diagnostics evaluated | Changed texts |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Akbura.FeatureGallery / Counter.akbura | localized-literal | production / off / enabled | 16311.88 | 213.35 | 207.44 - 319.72 | 319.72 | not measured / not measured | not measured | 1 |
| Akbura.FeatureGallery / DataTemplatesPage.akbura | localized-literal | production / off / enabled | 12912.05 | 3525.56 | 3481.74 - 3544.26 | 3544.26 | not measured / not measured | not measured | 1 |
| PurityUIDashborad / NeedHelp.akbura | localized-literal | production / off / enabled | 5698.12 | 172.99 | 146.41 - 333.23 | 333.23 | not measured / not measured | not measured | 1 |
| PurityUIDashborad / Sidebar.akbura | localized-literal | production / off / enabled | 5375.92 | 571.52 | 501.85 - 848.05 | 848.05 | not measured / not measured | not measured | 1 |

All stage durations and allocations are inclusive. Do not sum nested or parallel stages. With n=8, nearest-rank p95 equals the maximum.
<!-- END GENERATED: invalidation -->
