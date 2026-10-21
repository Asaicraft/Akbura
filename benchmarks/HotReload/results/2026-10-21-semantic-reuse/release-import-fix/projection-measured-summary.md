# Compilation-bound semantic reuse: generated measurements

These are independently measured baseline, projection-only, binding-only and combined variants; no automatic acceptance is implied. Production timings are non-STATS Release. Stage timings are inclusive and must not be added together. Every variant retains the existing current-owner expression cache. Each cell has 2 warmup updates, 8 measured updates and 11 fresh-source/diagnostic parity checks.

| Round | Variant | Component | Baseline median, ms | Candidate median, ms | Reduction | Allocation change | Post-GC heap change |
| ---: | --- | --- | ---: | ---: | ---: | ---: | ---: |
| 1 | binding | Akbura.FeatureGallery/Counter.akbura | 210.287 | 207.131 | 1.50% | -0.27% | 0.95% |
| 1 | binding | Akbura.FeatureGallery/DataTemplates.akbura | 386.178 | 382.081 | 1.06% | -0.21% | 0.85% |
| 1 | binding | Akbura.FeatureGallery/DataTemplatesPage.akbura | 3498.409 | 3458.185 | 1.15% | -0.10% | 0.55% |
| 1 | binding | PurityUIDashborad/NavButton.akbura | 125.527 | 122.321 | 2.55% | -2.46% | -0.69% |
| 1 | binding | PurityUIDashborad/NeedHelp.akbura | 180.365 | 163.512 | 9.34% | -1.87% | -1.13% |
| 1 | binding | PurityUIDashborad/Sidebar.akbura | 571.295 | 562.126 | 1.61% | -1.54% | -1.88% |
| 1 | binding | SemanticFixture/Siblings.akbura | 15.756 | 12.622 | 19.89% | -7.81% | -2.15% |
| 1 | combined | Akbura.FeatureGallery/Counter.akbura | 210.287 | 207.479 | 1.33% | -0.32% | 1.79% |
| 1 | combined | Akbura.FeatureGallery/DataTemplates.akbura | 386.178 | 376.364 | 2.54% | -0.71% | 1.62% |
| 1 | combined | Akbura.FeatureGallery/DataTemplatesPage.akbura | 3498.409 | 3450.717 | 1.36% | -0.13% | 1.17% |
| 1 | combined | PurityUIDashborad/NavButton.akbura | 125.527 | 114.518 | 8.77% | -3.79% | 0.68% |
| 1 | combined | PurityUIDashborad/NeedHelp.akbura | 180.365 | 154.615 | 14.28% | -3.45% | -0.50% |
| 1 | combined | PurityUIDashborad/Sidebar.akbura | 571.295 | 550.759 | 3.59% | -4.20% | -1.60% |
| 1 | combined | SemanticFixture/Siblings.akbura | 15.756 | 12.559 | 20.29% | -8.92% | -2.21% |
| 1 | projection | Akbura.FeatureGallery/Counter.akbura | 210.287 | 211.651 | -0.65% | -0.06% | 0.87% |
| 1 | projection | Akbura.FeatureGallery/DataTemplates.akbura | 386.178 | 385.094 | 0.28% | -0.46% | 0.82% |
| 1 | projection | Akbura.FeatureGallery/DataTemplatesPage.akbura | 3498.409 | 3462.140 | 1.04% | -0.10% | 0.65% |
| 1 | projection | PurityUIDashborad/NavButton.akbura | 125.527 | 119.605 | 4.72% | -0.62% | -1.45% |
| 1 | projection | PurityUIDashborad/NeedHelp.akbura | 180.365 | 177.676 | 1.49% | -0.45% | 1.38% |
| 1 | projection | PurityUIDashborad/Sidebar.akbura | 571.295 | 566.675 | 0.81% | -2.59% | -1.63% |
| 1 | projection | SemanticFixture/Siblings.akbura | 15.756 | 15.869 | -0.72% | 3.01% | -1.98% |
| 2 | binding | Akbura.FeatureGallery/Counter.akbura | 213.352 | 206.203 | 3.35% | -0.19% | 1.10% |
| 2 | binding | Akbura.FeatureGallery/DataTemplates.akbura | 382.990 | 385.249 | -0.59% | -0.30% | 0.75% |
| 2 | binding | Akbura.FeatureGallery/DataTemplatesPage.akbura | 3525.556 | 3520.232 | 0.15% | -0.07% | 0.51% |
| 2 | binding | PurityUIDashborad/NavButton.akbura | 124.509 | 119.619 | 3.93% | -2.66% | 2.21% |
| 2 | binding | PurityUIDashborad/NeedHelp.akbura | 172.992 | 153.682 | 11.16% | -3.71% | 1.48% |
| 2 | binding | PurityUIDashborad/Sidebar.akbura | 571.522 | 575.115 | -0.63% | -1.61% | 0.14% |
| 2 | binding | SemanticFixture/Siblings.akbura | 15.438 | 13.476 | 12.71% | -11.09% | -0.17% |
| 2 | combined | Akbura.FeatureGallery/Counter.akbura | 213.352 | 202.885 | 4.91% | -0.33% | 1.76% |
| 2 | combined | Akbura.FeatureGallery/DataTemplates.akbura | 382.990 | 385.536 | -0.66% | -0.81% | 1.50% |
| 2 | combined | Akbura.FeatureGallery/DataTemplatesPage.akbura | 3525.556 | 3432.482 | 2.64% | -0.15% | 1.07% |
| 2 | combined | PurityUIDashborad/NavButton.akbura | 124.509 | 107.170 | 13.93% | -3.97% | 2.87% |
| 2 | combined | PurityUIDashborad/NeedHelp.akbura | 172.992 | 160.700 | 7.11% | -4.57% | 2.34% |
| 2 | combined | PurityUIDashborad/Sidebar.akbura | 571.522 | 559.854 | 2.04% | -4.46% | 0.26% |
| 2 | combined | SemanticFixture/Siblings.akbura | 15.438 | 13.560 | 12.17% | -12.59% | -0.19% |
| 2 | projection | Akbura.FeatureGallery/Counter.akbura | 213.352 | 219.507 | -2.89% | -0.08% | 0.97% |
| 2 | projection | Akbura.FeatureGallery/DataTemplates.akbura | 382.990 | 392.086 | -2.38% | -0.50% | 0.81% |
| 2 | projection | Akbura.FeatureGallery/DataTemplatesPage.akbura | 3525.556 | 3601.516 | -2.15% | -0.06% | 0.74% |
| 2 | projection | PurityUIDashborad/NavButton.akbura | 124.509 | 126.386 | -1.51% | -1.23% | 1.02% |
| 2 | projection | PurityUIDashborad/NeedHelp.akbura | 172.992 | 179.429 | -3.72% | -0.94% | 1.14% |
| 2 | projection | PurityUIDashborad/Sidebar.akbura | 571.522 | 613.711 | -7.38% | -3.00% | 0.37% |
| 2 | projection | SemanticFixture/Siblings.akbura | 15.438 | 17.698 | -14.64% | -1.26% | -0.00% |

Positive reduction means lower GeneratorDriver elapsed time. Positive allocation/heap change means greater cost. Full distributions, GC activity and causal counters are in the CSV files. Entry/character counters are publication totals over measured updates, not a single latest-store gauge. Character counts are UTF-16 text lengths, not complete retained object-graph byte sizes.
MaxRetained columns report actual live-store entry/root/model/compilation/tree counts observed after GC, separately from whole-driver heap bytes. Compare those counts with 100/500-revision ownership evidence; dictionary bounds alone do not prove memory safety. Dedicated first-binding/probe/model skip counters, not unrelated total-probe deltas, identify the actual semantic fast path.
