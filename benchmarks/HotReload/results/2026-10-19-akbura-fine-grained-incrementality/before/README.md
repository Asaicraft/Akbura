# C# invalidation measurements

See the [engineering investigation](../../../../../For%20Contributors/csharp-invalidation-investigation.md) for methodology, environment, interpretation and limitations.

<!-- BEGIN GENERATED: invalidation -->
Recorded 440 driver runs; 40 workload/mode groups; 440 fresh-driver parity checks. Each group has two warm-ups and eight measured samples.

| Project / file | Scenario | Binary / profiling | Initial ms | Median ms | Range ms | p95 ms | Components generated / reused | Diagnostics evaluated | Changed texts |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Akbura.FeatureGallery / Counter.akbura | localized-akcss | stats / stats / enabled | 18964.49 | 220.83 | 207.97 - 239.96 | 239.96 | 1 / 27 | 1 | 1 |
| Akbura.FeatureGallery / Counter.akbura | localized-binding-sibling | stats / stats / enabled | 16954.12 | 227.34 | 220.09 - 357.07 | 357.07 | 1 / 27 | 1 | 1 |
| Akbura.FeatureGallery / Counter.akbura | localized-diagnostic | stats / stats / enabled | 15455.62 | 260.74 | 231.91 - 270.28 | 270.28 | 1 / 27 | 1 | 1 |
| Akbura.FeatureGallery / Counter.akbura | localized-expression | stats / stats / enabled | 16177.70 | 236.49 | 232.79 - 339.34 | 339.34 | 1 / 27 | 1 | 1 |
| Akbura.FeatureGallery / Counter.akbura | localized-literal | stats / stats / enabled | 15094.07 | 222.75 | 217.00 - 227.80 | 227.80 | 1 / 27 | 1 | 1 |
| Akbura.FeatureGallery / Counter.akbura | localized-name | stats / stats / enabled | 14701.76 | 231.41 | 216.27 - 256.79 | 256.79 | 1 / 27 | 1 | 1 |
| Akbura.FeatureGallery / Counter.akbura | localized-property-add-remove | stats / stats / enabled | 15426.26 | 220.81 | 217.75 - 239.58 | 239.58 | 1 / 27 | 1 | 1 |
| Akbura.FeatureGallery / Counter.akbura | localized-property | stats / stats / enabled | 14739.18 | 259.34 | 220.60 - 520.98 | 520.98 | 1 / 27 | 1 | 1 |
| Akbura.FeatureGallery / Counter.akbura | localized-structure | stats / stats / enabled | 15324.65 | 238.12 | 225.63 - 278.59 | 278.59 | 1 / 27 | 1 | 1 |
| Akbura.FeatureGallery / Counter.akbura | localized-trivia | stats / stats / enabled | 14646.01 | 273.81 | 218.42 - 336.34 | 336.34 | 1 / 27 | 1 | 1 |
| Akbura.FeatureGallery / DataTemplates.akbura | localized-binding-sibling | stats / stats / enabled | 15380.85 | 437.37 | 427.32 - 472.82 | 472.82 | 1 / 27 | 1 | 1 |
| Akbura.FeatureGallery / DataTemplates.akbura | localized-expression | stats / stats / enabled | 14332.69 | 451.99 | 445.33 - 544.65 | 544.65 | 1 / 27 | 1 | 1 |
| Akbura.FeatureGallery / DataTemplatesPage.akbura | localized-akcss | stats / stats / enabled | 14509.67 | 3872.33 | 3838.08 - 3893.04 | 3893.04 | 1 / 27 | 1 | 1 |
| Akbura.FeatureGallery / DataTemplatesPage.akbura | localized-diagnostic | stats / stats / enabled | 17547.24 | 3996.46 | 3967.77 - 4270.56 | 4270.56 | 1 / 27 | 1 | 1 |
| Akbura.FeatureGallery / DataTemplatesPage.akbura | localized-literal | stats / stats / enabled | 14920.27 | 3944.69 | 3874.02 - 3983.65 | 3983.65 | 1 / 27 | 1 | 1 |
| Akbura.FeatureGallery / DataTemplatesPage.akbura | localized-name | stats / stats / enabled | 16083.98 | 4253.26 | 3895.42 - 5228.00 | 5228.00 | 1 / 27 | 1 | 1 |
| Akbura.FeatureGallery / DataTemplatesPage.akbura | localized-property-add-remove | stats / stats / enabled | 14971.52 | 3844.91 | 3795.36 - 4145.08 | 4145.08 | 1 / 27 | 1 | 1 |
| Akbura.FeatureGallery / DataTemplatesPage.akbura | localized-property | stats / stats / enabled | 14797.83 | 3948.43 | 3880.01 - 4507.40 | 4507.40 | 1 / 27 | 1 | 1 |
| Akbura.FeatureGallery / DataTemplatesPage.akbura | localized-structure | stats / stats / enabled | 15391.86 | 4608.17 | 3928.21 - 5159.83 | 5159.83 | 1 / 27 | 1 | 1 |
| Akbura.FeatureGallery / DataTemplatesPage.akbura | localized-trivia | stats / stats / enabled | 15513.18 | 4249.70 | 4005.21 - 4718.27 | 4718.27 | 1 / 27 | 1 | 1 |
| PurityUIDashborad / NavButton.akbura | localized-binding-sibling | stats / stats / enabled | 6270.62 | 150.46 | 135.46 - 232.32 | 232.32 | 1 / 14 | 1 | 1 |
| PurityUIDashborad / NavButton.akbura | localized-expression | stats / stats / enabled | 6386.95 | 153.36 | 138.97 - 192.68 | 192.68 | 1 / 14 | 1 | 1 |
| PurityUIDashborad / NeedHelp.akbura | localized-akcss | stats / stats / enabled | 6319.67 | 194.02 | 171.06 - 259.45 | 259.45 | 1 / 14 | 1 | 1 |
| PurityUIDashborad / NeedHelp.akbura | localized-binding-sibling | stats / stats / enabled | 6255.15 | 209.85 | 172.89 - 267.39 | 267.39 | 1 / 14 | 1 | 1 |
| PurityUIDashborad / NeedHelp.akbura | localized-diagnostic | stats / stats / enabled | 7206.92 | 181.24 | 171.89 - 286.86 | 286.86 | 1 / 14 | 1 | 1 |
| PurityUIDashborad / NeedHelp.akbura | localized-expression | stats / stats / enabled | 6696.04 | 192.79 | 171.36 - 271.80 | 271.80 | 1 / 14 | 1 | 1 |
| PurityUIDashborad / NeedHelp.akbura | localized-literal | stats / stats / enabled | 6708.82 | 207.60 | 168.30 - 239.30 | 239.30 | 1 / 14 | 1 | 1 |
| PurityUIDashborad / NeedHelp.akbura | localized-name | stats / stats / enabled | 7433.15 | 240.34 | 205.10 - 317.75 | 317.75 | 1 / 14 | 1 | 1 |
| PurityUIDashborad / NeedHelp.akbura | localized-property-add-remove | stats / stats / enabled | 6360.29 | 199.77 | 184.77 - 296.30 | 296.30 | 1 / 14 | 1 | 1 |
| PurityUIDashborad / NeedHelp.akbura | localized-property | stats / stats / enabled | 6605.81 | 196.37 | 176.18 - 242.85 | 242.85 | 1 / 14 | 1 | 1 |
| PurityUIDashborad / NeedHelp.akbura | localized-structure | stats / stats / enabled | 6679.47 | 257.66 | 207.07 - 387.05 | 387.05 | 1 / 14 | 1 | 1 |
| PurityUIDashborad / NeedHelp.akbura | localized-trivia | stats / stats / enabled | 6433.07 | 192.55 | 172.68 - 239.45 | 239.45 | 1 / 14 | 1 | 1 |
| PurityUIDashborad / Sidebar.akbura | localized-akcss | stats / stats / enabled | 6238.89 | 692.75 | 651.77 - 862.25 | 862.25 | 1 / 14 | 1 | 1 |
| PurityUIDashborad / Sidebar.akbura | localized-diagnostic | stats / stats / enabled | 5803.76 | 695.04 | 614.32 - 914.16 | 914.16 | 1 / 14 | 1 | 1 |
| PurityUIDashborad / Sidebar.akbura | localized-literal | stats / stats / enabled | 5912.50 | 699.28 | 619.56 - 923.43 | 923.43 | 1 / 14 | 1 | 1 |
| PurityUIDashborad / Sidebar.akbura | localized-name | stats / stats / enabled | 6173.19 | 665.25 | 638.90 - 832.58 | 832.58 | 1 / 14 | 1 | 1 |
| PurityUIDashborad / Sidebar.akbura | localized-property-add-remove | stats / stats / enabled | 6187.08 | 719.16 | 675.12 - 944.33 | 944.33 | 1 / 14 | 1 | 1 |
| PurityUIDashborad / Sidebar.akbura | localized-property | stats / stats / enabled | 7058.60 | 680.71 | 642.41 - 815.10 | 815.10 | 1 / 14 | 1 | 1 |
| PurityUIDashborad / Sidebar.akbura | localized-structure | stats / stats / enabled | 6235.15 | 684.07 | 641.26 - 800.28 | 800.28 | 1 / 14 | 1 | 1 |
| PurityUIDashborad / Sidebar.akbura | localized-trivia | stats / stats / enabled | 5782.23 | 639.79 | 604.99 - 769.85 | 769.85 | 1 / 14 | 1 | 1 |

All stage durations and allocations are inclusive. Do not sum nested or parallel stages. With n=8, nearest-rank p95 equals the maximum.
<!-- END GENERATED: invalidation -->
