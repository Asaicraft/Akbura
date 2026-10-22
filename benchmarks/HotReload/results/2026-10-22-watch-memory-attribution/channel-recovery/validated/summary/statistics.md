# Generated memory statistics

Three independent 100-edit sessions per row. Values are MiB (1,048,576 bytes). Growth is the median of per-session differences, not the difference between two medians. Ranges are observed minima/maxima, not confidence intervals. These are diagnostic memory measurements, not production latency results.

## Compiler/watch: forced GC

| Configuration | Warmup median | Revision 100 median | Growth median | Growth range |
| --- | ---: | ---: | ---: | ---: |
| baseline-observer | 49.44 | 117.43 | 68.00 | 67.28-68.19 |
| baseline-release | 49.42 | 116.75 | 67.17 | 67.02-67.53 |
| baseline-stats-no-observer | 49.46 | 117.06 | 67.60 | 67.38-67.76 |
| combined-observer | 49.64 | 117.07 | 67.43 | 67.04-67.81 |
| combined-release | 49.46 | 117.13 | 67.57 | 67.54-67.67 |
| combined-stats-no-observer | 49.44 | 117.08 | 67.64 | 67.55-67.72 |

## Compiler/watch: natural GC

| Configuration | Warmup median | Revision 100 median | Growth median | Growth range |
| --- | ---: | ---: | ---: | ---: |
| baseline-observer | 52.81 | 124.30 | 71.00 | 70.92-71.49 |
| baseline-release | 50.56 | 124.43 | 73.10 | 67.89-73.87 |
| baseline-stats-no-observer | 49.47 | 120.56 | 71.09 | 67.00-72.03 |
| combined-observer | 50.10 | 139.38 | 89.28 | 73.70-94.20 |
| combined-release | 49.62 | 118.10 | 68.50 | 67.49-89.22 |
| combined-stats-no-observer | 49.46 | 118.91 | 69.35 | 66.73-70.42 |

Forced values are gcdump live-graph bytes. Natural values are last-GC heap less fragmentation and may lag current allocations. Do not pool the two modes. Detailed process curves, independent rounds, early/late slopes and diagnostic counters remain in the CSV files. Type counts are measured only in the first forced-GC round at warmup, revision 50 and revision 100; their absence in another round is not a repeated object-graph validation.
