# C# invalidation measurements

See the [engineering investigation](../../../../../For%20Contributors/csharp-invalidation-investigation.md) for methodology, environment, interpretation and limitations.

<!-- BEGIN GENERATED: invalidation -->
Recorded 440 driver runs; 40 workload/mode groups; 440 fresh-driver parity checks. Each group has two warm-ups and eight measured samples.

| Project / file | Scenario | Binary / profiling | Initial ms | Median ms | Range ms | p95 ms | Components generated / reused | Diagnostics evaluated | Changed texts |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Akbura.FeatureGallery / Counter.akbura | localized-akcss | stats / stats / enabled | 13950.83 | 232.58 | 229.62 - 259.01 | 259.01 | 1 / 27 | 1 | 1 |
| Akbura.FeatureGallery / Counter.akbura | localized-binding-sibling | stats / stats / enabled | 13391.73 | 226.65 | 221.21 - 235.92 | 235.92 | 1 / 27 | 1 | 1 |
| Akbura.FeatureGallery / Counter.akbura | localized-diagnostic | stats / stats / enabled | 15750.76 | 262.60 | 254.51 - 286.46 | 286.46 | 1 / 27 | 1 | 1 |
| Akbura.FeatureGallery / Counter.akbura | localized-expression | stats / stats / enabled | 13453.43 | 235.26 | 223.89 - 252.47 | 252.47 | 1 / 27 | 1 | 1 |
| Akbura.FeatureGallery / Counter.akbura | localized-literal | stats / stats / enabled | 13837.89 | 224.91 | 221.18 - 287.02 | 287.02 | 1 / 27 | 1 | 1 |
| Akbura.FeatureGallery / Counter.akbura | localized-name | stats / stats / enabled | 14759.79 | 268.30 | 253.84 - 298.34 | 298.34 | 1 / 27 | 1 | 1 |
| Akbura.FeatureGallery / Counter.akbura | localized-property-add-remove | stats / stats / enabled | 13200.39 | 274.24 | 256.38 - 367.15 | 367.15 | 1 / 27 | 1 | 1 |
| Akbura.FeatureGallery / Counter.akbura | localized-property | stats / stats / enabled | 13970.71 | 227.18 | 222.22 - 240.11 | 240.11 | 1 / 27 | 1 | 1 |
| Akbura.FeatureGallery / Counter.akbura | localized-structure | stats / stats / enabled | 13755.14 | 222.79 | 218.90 - 237.90 | 237.90 | 1 / 27 | 1 | 1 |
| Akbura.FeatureGallery / Counter.akbura | localized-trivia | stats / stats / enabled | 16597.17 | 264.39 | 250.69 - 280.96 | 280.96 | 1 / 27 | 1 | 1 |
| Akbura.FeatureGallery / DataTemplates.akbura | localized-binding-sibling | stats / stats / enabled | 15520.42 | 541.24 | 531.98 - 556.27 | 556.27 | 1 / 27 | 1 | 1 |
| Akbura.FeatureGallery / DataTemplates.akbura | localized-expression | stats / stats / enabled | 15044.11 | 477.71 | 466.04 - 509.64 | 509.64 | 1 / 27 | 1 | 1 |
| Akbura.FeatureGallery / DataTemplatesPage.akbura | localized-akcss | stats / stats / enabled | 19686.39 | 4343.10 | 4261.98 - 4639.54 | 4639.54 | 1 / 27 | 1 | 1 |
| Akbura.FeatureGallery / DataTemplatesPage.akbura | localized-diagnostic | stats / stats / enabled | 19636.02 | 4227.91 | 4200.46 - 4576.04 | 4576.04 | 1 / 27 | 1 | 1 |
| Akbura.FeatureGallery / DataTemplatesPage.akbura | localized-literal | stats / stats / enabled | 19756.16 | 5284.92 | 4447.73 - 5367.43 | 5367.43 | 1 / 27 | 1 | 1 |
| Akbura.FeatureGallery / DataTemplatesPage.akbura | localized-name | stats / stats / enabled | 15398.57 | 4245.63 | 4074.98 - 4555.08 | 4555.08 | 1 / 27 | 1 | 1 |
| Akbura.FeatureGallery / DataTemplatesPage.akbura | localized-property-add-remove | stats / stats / enabled | 14910.21 | 4101.40 | 4072.37 - 4149.52 | 4149.52 | 1 / 27 | 1 | 1 |
| Akbura.FeatureGallery / DataTemplatesPage.akbura | localized-property | stats / stats / enabled | 19307.53 | 4367.15 | 4334.91 - 4668.49 | 4668.49 | 1 / 27 | 1 | 1 |
| Akbura.FeatureGallery / DataTemplatesPage.akbura | localized-structure | stats / stats / enabled | 14993.48 | 4229.66 | 4165.38 - 4454.32 | 4454.32 | 1 / 27 | 1 | 1 |
| Akbura.FeatureGallery / DataTemplatesPage.akbura | localized-trivia | stats / stats / enabled | 16494.84 | 4309.67 | 4197.12 - 4641.89 | 4641.89 | 1 / 27 | 1 | 1 |
| PurityUIDashborad / NavButton.akbura | localized-binding-sibling | stats / stats / enabled | 6487.24 | 141.36 | 122.63 - 190.07 | 190.07 | 1 / 14 | 1 | 1 |
| PurityUIDashborad / NavButton.akbura | localized-expression | stats / stats / enabled | 6876.74 | 121.88 | 112.96 - 169.29 | 169.29 | 1 / 14 | 1 | 1 |
| PurityUIDashborad / NeedHelp.akbura | localized-akcss | stats / stats / enabled | 7155.00 | 220.74 | 188.43 - 295.21 | 295.21 | 1 / 14 | 1 | 1 |
| PurityUIDashborad / NeedHelp.akbura | localized-binding-sibling | stats / stats / enabled | 6656.71 | 227.17 | 195.62 - 245.40 | 245.40 | 1 / 14 | 1 | 1 |
| PurityUIDashborad / NeedHelp.akbura | localized-diagnostic | stats / stats / enabled | 8343.64 | 214.48 | 188.69 - 282.93 | 282.93 | 1 / 14 | 1 | 1 |
| PurityUIDashborad / NeedHelp.akbura | localized-expression | stats / stats / enabled | 7391.06 | 211.38 | 189.82 - 278.12 | 278.12 | 1 / 14 | 1 | 1 |
| PurityUIDashborad / NeedHelp.akbura | localized-literal | stats / stats / enabled | 6856.55 | 221.86 | 208.22 - 259.86 | 259.86 | 1 / 14 | 1 | 1 |
| PurityUIDashborad / NeedHelp.akbura | localized-name | stats / stats / enabled | 10926.74 | 338.13 | 273.98 - 528.47 | 528.47 | 1 / 14 | 1 | 1 |
| PurityUIDashborad / NeedHelp.akbura | localized-property-add-remove | stats / stats / enabled | 6958.06 | 203.13 | 194.90 - 314.67 | 314.67 | 1 / 14 | 1 | 1 |
| PurityUIDashborad / NeedHelp.akbura | localized-property | stats / stats / enabled | 7014.15 | 219.81 | 200.47 - 374.42 | 374.42 | 1 / 14 | 1 | 1 |
| PurityUIDashborad / NeedHelp.akbura | localized-structure | stats / stats / enabled | 7225.93 | 243.32 | 219.30 - 289.62 | 289.62 | 1 / 14 | 1 | 1 |
| PurityUIDashborad / NeedHelp.akbura | localized-trivia | stats / stats / enabled | 7070.73 | 213.67 | 195.09 - 260.71 | 260.71 | 1 / 14 | 1 | 1 |
| PurityUIDashborad / Sidebar.akbura | localized-akcss | stats / stats / enabled | 6176.02 | 695.65 | 663.74 - 779.62 | 779.62 | 1 / 14 | 1 | 1 |
| PurityUIDashborad / Sidebar.akbura | localized-diagnostic | stats / stats / enabled | 6665.61 | 776.43 | 633.06 - 1152.26 | 1152.26 | 1 / 14 | 1 | 1 |
| PurityUIDashborad / Sidebar.akbura | localized-literal | stats / stats / enabled | 7077.23 | 687.04 | 642.86 - 892.69 | 892.69 | 1 / 14 | 1 | 1 |
| PurityUIDashborad / Sidebar.akbura | localized-name | stats / stats / enabled | 7348.89 | 746.15 | 621.17 - 791.35 | 791.35 | 1 / 14 | 1 | 1 |
| PurityUIDashborad / Sidebar.akbura | localized-property-add-remove | stats / stats / enabled | 5775.36 | 832.73 | 621.33 - 940.64 | 940.64 | 1 / 14 | 1 | 1 |
| PurityUIDashborad / Sidebar.akbura | localized-property | stats / stats / enabled | 6539.16 | 638.56 | 602.06 - 774.80 | 774.80 | 1 / 14 | 1 | 1 |
| PurityUIDashborad / Sidebar.akbura | localized-structure | stats / stats / enabled | 7351.97 | 679.10 | 588.57 - 792.83 | 792.83 | 1 / 14 | 1 | 1 |
| PurityUIDashborad / Sidebar.akbura | localized-trivia | stats / stats / enabled | 7038.51 | 741.12 | 720.33 - 866.54 | 866.54 | 1 / 14 | 1 | 1 |

All stage durations and allocations are inclusive. Do not sum nested or parallel stages. With n=8, nearest-rank p95 equals the maximum.
<!-- END GENERATED: invalidation -->
