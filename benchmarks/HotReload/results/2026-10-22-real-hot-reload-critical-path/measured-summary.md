# Generated real Sidebar measurements

Non-STATS production sessions only. Equal session weights. p95 below is descriptive pooled-edit p95, not an independent-edit confidence interval. Instrumented stages are a separate population. Missing allocation/EnC/memory/correctness-gate values are not substituted from historical experiments.

| Scenario | Variant | Sessions | Median of session medians, ms | Pooled p95, ms |
| --- | --- | ---: | ---: | ---: |
| supported | baseline | 5 | 1498.844 | 2256.012 |
| supported | projection | 5 | 1498.777 | 2268.761 |
| supported | binding | 5 | 1483.257 | 2119.155 |
| supported | combined | 5 | 1415.240 | 2119.412 |
| fallback | baseline | 3 | 1519.761 | 2138.112 |
| fallback | projection | 3 | 1570.671 | 2162.381 |
| fallback | binding | 3 | 1509.033 | 2196.392 |
| fallback | combined | 3 | 1531.948 | 2257.649 |

| Scenario | Candidate/reference | Improvement, % | 95% session-cluster interval |
| --- | --- | ---: | ---: |
| supported | projection/baseline | -1.99 | -9.75 to 4.83 |
| supported | binding/baseline | 2.68 | -0.57 to 6.54 |
| supported | combined/baseline | 3.29 | -1.53 to 7.50 |
| supported | combined/binding | 0.63 | -1.45 to 3.14 |
| fallback | projection/baseline | -3.73 | -8.32 to 1.01 |
| fallback | binding/baseline | -0.84 | -5.98 to 2.56 |
| fallback | combined/baseline | -1.65 | -2.71 to -0.73 |
| fallback | combined/binding | -0.81 | -4.20 to 3.08 |
