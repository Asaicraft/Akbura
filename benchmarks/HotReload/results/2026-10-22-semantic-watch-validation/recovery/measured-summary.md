# Live semantic watch: generated measurements

Production results use non-STATS Release analyzers. Latency starts after the file write completes and ends when the host receives the validated UI acknowledgment. Startup, deliberate delays, diagnostic repair and explicit full collections are excluded. Per-production-update semantic hits are unavailable; causal runs are separate.

| Round | Scenario | Variant | Samples | Median ms | Mean ms | p95 ms | Min–max ms |
| ---: | --- | --- | ---: | ---: | ---: | ---: | ---: |
| 1 | margin | baseline | 20 | 167.829 | 186.237 | 270.730 | 111.790–285.720 |
| 1 | margin | projection | 20 | 160.493 | 181.481 | 251.559 | 127.362–252.590 |
| 1 | margin | binding | 20 | 152.795 | 163.523 | 250.802 | 109.148–283.332 |
| 1 | margin | combined | 20 | 158.412 | 161.463 | 223.799 | 112.169–237.309 |
| 1 | text | baseline | 20 | 129.441 | 156.118 | 252.789 | 97.641–255.250 |
| 1 | text | projection | 20 | 136.552 | 156.909 | 237.142 | 111.859–251.101 |
| 1 | text | binding | 20 | 143.999 | 163.921 | 266.937 | 112.681–280.682 |
| 1 | text | combined | 20 | 145.060 | 166.005 | 253.628 | 112.346–253.714 |
| 2 | margin | baseline | 20 | 164.340 | 165.730 | 225.498 | 113.221–237.527 |
| 2 | margin | projection | 20 | 151.197 | 160.608 | 237.746 | 112.127–240.660 |
| 2 | margin | binding | 20 | 152.549 | 155.032 | 206.447 | 96.552–255.269 |
| 2 | margin | combined | 20 | 159.889 | 163.499 | 255.155 | 96.077–267.634 |
| 2 | text | baseline | 20 | 166.919 | 170.405 | 253.478 | 96.549–254.361 |
| 2 | text | projection | 20 | 173.707 | 161.938 | 237.275 | 111.531–256.593 |
| 2 | text | binding | 20 | 144.944 | 165.222 | 253.192 | 98.304–253.414 |
| 2 | text | combined | 20 | 144.995 | 163.522 | 221.925 | 110.251–252.505 |
| 3 | margin | baseline | 20 | 157.605 | 158.106 | 193.105 | 96.647–254.774 |
| 3 | margin | projection | 20 | 159.599 | 163.199 | 223.855 | 96.763–266.162 |
| 3 | margin | binding | 20 | 157.796 | 158.494 | 237.042 | 96.057–252.077 |
| 3 | margin | combined | 20 | 137.540 | 155.460 | 206.681 | 95.995–238.517 |
| 3 | text | baseline | 20 | 143.333 | 155.957 | 240.436 | 97.404–253.317 |
| 3 | text | projection | 20 | 160.321 | 172.327 | 250.697 | 111.956–286.099 |
| 3 | text | binding | 20 | 157.773 | 173.511 | 236.537 | 128.620–250.877 |
| 3 | text | combined | 20 | 144.464 | 162.485 | 252.725 | 111.678–266.986 |

Equal-weight round-median ratios and hierarchical bootstrap intervals are descriptive. Three sessions per variant provide limited uncertainty resolution; do not infer a general speedup, default activation or a cache hit in an uninstrumented update. Compiler allocations/post-GC heap are not observable in production here; application metrics must not be substituted for compiler metrics.
