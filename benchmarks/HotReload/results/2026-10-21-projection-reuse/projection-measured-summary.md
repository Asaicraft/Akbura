# Projection reuse: generated measurements

This is projection-only evidence, not acceptance of cross-revision semantic binding. Production timings are non-STATS Release. Stage timings are inclusive and must not be added together. Both variants retain the existing current-owner expression cache. Each cell has 2 warmup updates, 8 measured updates and 11 fresh-source/diagnostic parity checks.

| Round | Component | Baseline median, ms | Projection median, ms | Reduction | Allocation change | Post-GC heap change |
| ---: | --- | ---: | ---: | ---: | ---: | ---: |
| 1 | Akbura.FeatureGallery/Counter.akbura | 209.743 | 206.001 | 1.78% | -0.07% | 0.93% |
| 1 | Akbura.FeatureGallery/DataTemplates.akbura | 382.167 | 395.093 | -3.38% | -0.49% | 0.76% |
| 1 | Akbura.FeatureGallery/DataTemplatesPage.akbura | 3495.148 | 3487.298 | 0.22% | -0.27% | 0.19% |
| 1 | ProjectionFixture/Siblings.akbura | 17.724 | 17.585 | 0.78% | -1.22% | 0.04% |
| 1 | PurityUIDashborad/NavButton.akbura | 124.209 | 123.343 | 0.70% | -1.47% | 0.92% |
| 1 | PurityUIDashborad/NeedHelp.akbura | 167.523 | 166.445 | 0.64% | -0.90% | 1.18% |
| 1 | PurityUIDashborad/Sidebar.akbura | 571.721 | 552.449 | 3.37% | -2.85% | 0.33% |
| 2 | Akbura.FeatureGallery/Counter.akbura | 209.878 | 208.140 | 0.83% | -0.02% | 0.98% |
| 2 | Akbura.FeatureGallery/DataTemplates.akbura | 380.334 | 389.978 | -2.54% | -0.40% | 0.89% |
| 2 | Akbura.FeatureGallery/DataTemplatesPage.akbura | 3469.690 | 3540.563 | -2.04% | -0.08% | 0.78% |
| 2 | ProjectionFixture/Siblings.akbura | 17.211 | 17.591 | -2.21% | -1.46% | 0.00% |
| 2 | PurityUIDashborad/NavButton.akbura | 124.918 | 125.035 | -0.09% | -1.14% | 1.24% |
| 2 | PurityUIDashborad/NeedHelp.akbura | 175.558 | 172.146 | 1.94% | -0.00% | 1.16% |
| 2 | PurityUIDashborad/Sidebar.akbura | 568.472 | 569.010 | -0.09% | -3.29% | 0.16% |

Positive reduction means lower GeneratorDriver elapsed time. Positive allocation/heap change means greater cost. Full distributions, GC activity and causal counters are in the CSV files. Entry/character counters are publication totals over measured updates, not a single latest-store gauge. Character counts are UTF-16 text lengths, not complete retained object-graph byte sizes. No first semantic binding, probe compilation or probe SemanticModel is intentionally bypassed by this candidate.
