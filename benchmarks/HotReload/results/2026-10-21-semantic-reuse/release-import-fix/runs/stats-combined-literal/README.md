# C# invalidation measurements

See the [engineering investigation](../../../../../../../For%20Contributors/csharp-invalidation-investigation.md) for methodology, environment, interpretation and limitations.

<!-- BEGIN GENERATED: invalidation -->
Recorded 44 driver runs; 4 workload/mode groups; 44 fresh-driver parity checks. Each group has two warm-ups and eight measured samples.

| Project / file | Scenario | Binary / profiling | Initial ms | Median ms | Range ms | p95 ms | Components generated / reused | Diagnostics evaluated | Changed texts |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Akbura.FeatureGallery / Counter.akbura | localized-literal | stats / stats / enabled | 13271.09 | 229.94 | 221.51 - 286.03 | 286.03 | 1 / 27 | 1 | 1 |
| Akbura.FeatureGallery / DataTemplatesPage.akbura | localized-literal | stats / stats / enabled | 13256.55 | 3696.31 | 3635.12 - 3726.48 | 3726.48 | 1 / 27 | 1 | 1 |
| PurityUIDashborad / NeedHelp.akbura | localized-literal | stats / stats / enabled | 5468.40 | 183.88 | 158.94 - 323.64 | 323.64 | 1 / 14 | 1 | 1 |
| PurityUIDashborad / Sidebar.akbura | localized-literal | stats / stats / enabled | 5605.47 | 571.38 | 543.52 - 795.92 | 795.92 | 1 / 14 | 1 | 1 |

All stage durations and allocations are inclusive. Do not sum nested or parallel stages. With n=8, nearest-rank p95 equals the maximum.
<!-- END GENERATED: invalidation -->
