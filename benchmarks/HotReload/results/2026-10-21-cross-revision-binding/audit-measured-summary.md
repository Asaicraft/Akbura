# Scoped-expression cache audit measurements

Generated from frozen tools and identical real project inputs. Each row has an initial generation, two warm-ups and eight measured edits. Production has independent forward/reverse-order rounds. Negative differences mean less time, but no statistical significance or cross-revision reuse is implied. Engineering acceptance remains manual.

| Profile / round | Variant | Component / edit | Median ms | Range ms | Allocated bytes | Post-GC heap bytes |
| --- | --- | --- | ---: | ---: | ---: | ---: |
| production / 1 | current | DataTemplates.akbura / localized-binding-sibling | 383.09 | 374.52-410.46 | 116273248 | 281374324 |
| production / 1 | current | NavButton.akbura / localized-binding-sibling | 123.17 | 111.29-215.32 | 25871440 | 108110732 |
| production / 1 | current | Counter.akbura / localized-literal | 210.26 | 201.87-217.59 | 53003276 | 277106380 |
| production / 1 | current | DataTemplatesPage.akbura / localized-literal | 3516.47 | 3474.62-3570.17 | 1157022588 | 324953468 |
| production / 1 | current | NeedHelp.akbura / localized-literal | 171.53 | 162.34-332.70 | 39465468 | 112116084 |
| production / 1 | current | Sidebar.akbura / localized-literal | 574.92 | 528.69-850.85 | 127437404 | 149624180 |
| production / 1 | disabled | DataTemplates.akbura / localized-binding-sibling | 468.64 | 414.22-629.24 | 121904544 | 280749708 |
| production / 1 | disabled | NavButton.akbura / localized-binding-sibling | 134.59 | 117.18-190.57 | 29500776 | 107119948 |
| production / 1 | disabled | Counter.akbura / localized-literal | 208.03 | 200.83-214.68 | 53013116 | 276676220 |
| production / 1 | disabled | DataTemplatesPage.akbura / localized-literal | 3601.05 | 3563.00-3639.77 | 1184868652 | 320040856 |
| production / 1 | disabled | NeedHelp.akbura / localized-literal | 164.39 | 142.34-343.91 | 40866720 | 109394796 |
| production / 1 | disabled | Sidebar.akbura / localized-literal | 795.63 | 603.28-1911.80 | 139001000 | 147312920 |
| production / 1 | original | DataTemplates.akbura / localized-binding-sibling | 411.17 | 404.22-448.29 | 121798680 | 280909776 |
| production / 1 | original | NavButton.akbura / localized-binding-sibling | 139.91 | 116.22-221.74 | 29430768 | 107136660 |
| production / 1 | original | Counter.akbura / localized-literal | 203.11 | 198.64-212.43 | 53003808 | 276566604 |
| production / 1 | original | DataTemplatesPage.akbura / localized-literal | 3572.36 | 3531.77-3615.20 | 1182716260 | 318494692 |
| production / 1 | original | NeedHelp.akbura / localized-literal | 172.39 | 152.59-275.92 | 40929312 | 109491720 |
| production / 1 | original | Sidebar.akbura / localized-literal | 605.66 | 559.40-806.68 | 138696664 | 146962176 |
| production / 1 | prehashed | DataTemplates.akbura / localized-binding-sibling | 441.05 | 426.85-462.60 | 116305288 | 281529912 |
| production / 1 | prehashed | NavButton.akbura / localized-binding-sibling | 139.37 | 128.02-253.15 | 25898700 | 107997576 |
| production / 1 | prehashed | Counter.akbura / localized-literal | 227.89 | 216.19-261.46 | 52981088 | 277561732 |
| production / 1 | prehashed | DataTemplatesPage.akbura / localized-literal | 3877.12 | 3805.13-3969.63 | 1154874420 | 323941772 |
| production / 1 | prehashed | NeedHelp.akbura / localized-literal | 220.99 | 175.05-267.44 | 39267032 | 111930264 |
| production / 1 | prehashed | Sidebar.akbura / localized-literal | 700.97 | 558.52-962.34 | 127575068 | 152559848 |
| production / 1 | rebuilt-current | DataTemplates.akbura / localized-binding-sibling | 387.63 | 380.36-398.96 | 116307156 | 281443252 |
| production / 1 | rebuilt-current | NavButton.akbura / localized-binding-sibling | 123.88 | 107.51-220.60 | 25896024 | 107900836 |
| production / 1 | rebuilt-current | Counter.akbura / localized-literal | 211.35 | 204.28-223.50 | 53034156 | 277157708 |
| production / 1 | rebuilt-current | DataTemplatesPage.akbura / localized-literal | 3519.72 | 3489.36-3586.83 | 1154851136 | 323700000 |
| production / 1 | rebuilt-current | NeedHelp.akbura / localized-literal | 175.79 | 154.89-311.43 | 39282000 | 112074840 |
| production / 1 | rebuilt-current | Sidebar.akbura / localized-literal | 568.36 | 485.21-826.18 | 127268924 | 149600248 |
| production / 1 | repeat-admission | DataTemplates.akbura / localized-binding-sibling | 405.11 | 394.27-412.75 | 119020688 | 282921236 |
| production / 1 | repeat-admission | NavButton.akbura / localized-binding-sibling | 123.39 | 115.53-182.27 | 27368904 | 107566168 |
| production / 1 | repeat-admission | Counter.akbura / localized-literal | 210.23 | 204.55-223.89 | 53011728 | 276887192 |
| production / 1 | repeat-admission | DataTemplatesPage.akbura / localized-literal | 3639.37 | 3568.00-4148.49 | 1163370116 | 325172552 |
| production / 1 | repeat-admission | NeedHelp.akbura / localized-literal | 178.72 | 157.20-291.13 | 40713984 | 110966116 |
| production / 1 | repeat-admission | Sidebar.akbura / localized-literal | 600.56 | 542.85-890.39 | 134103780 | 148249632 |
| production / 2 | current | DataTemplates.akbura / localized-binding-sibling | 384.60 | 376.21-390.47 | 116318924 | 281853412 |
| production / 2 | current | NavButton.akbura / localized-binding-sibling | 121.17 | 110.17-204.87 | 25802304 | 107893960 |
| production / 2 | current | Counter.akbura / localized-literal | 209.52 | 205.58-215.76 | 53010908 | 277668260 |
| production / 2 | current | DataTemplatesPage.akbura / localized-literal | 3515.72 | 3481.07-3552.69 | 1154767184 | 323944620 |
| production / 2 | current | NeedHelp.akbura / localized-literal | 168.41 | 149.76-313.42 | 39342144 | 111747988 |
| production / 2 | current | Sidebar.akbura / localized-literal | 643.11 | 509.66-863.55 | 127246992 | 149744356 |
| production / 2 | disabled | DataTemplates.akbura / localized-binding-sibling | 411.12 | 406.44-445.61 | 122109268 | 280903676 |
| production / 2 | disabled | NavButton.akbura / localized-binding-sibling | 140.89 | 129.83-232.91 | 29565640 | 106957612 |
| production / 2 | disabled | Counter.akbura / localized-literal | 228.94 | 225.70-261.75 | 55335536 | 278128176 |
| production / 2 | disabled | DataTemplatesPage.akbura / localized-literal | 3684.43 | 3656.83-3882.55 | 1182157956 | 318558520 |
| production / 2 | disabled | NeedHelp.akbura / localized-literal | 175.79 | 148.45-282.09 | 40824412 | 109654136 |
| production / 2 | disabled | Sidebar.akbura / localized-literal | 650.93 | 580.73-976.72 | 138674508 | 147001268 |
| production / 2 | original | DataTemplates.akbura / localized-binding-sibling | 430.80 | 403.56-632.25 | 121883972 | 281132236 |
| production / 2 | original | NavButton.akbura / localized-binding-sibling | 134.69 | 111.89-231.63 | 29507520 | 107172876 |
| production / 2 | original | Counter.akbura / localized-literal | 207.37 | 199.72-220.14 | 52978180 | 276801160 |
| production / 2 | original | DataTemplatesPage.akbura / localized-literal | 3655.54 | 3615.75-3708.72 | 1183100336 | 318902488 |
| production / 2 | original | NeedHelp.akbura / localized-literal | 205.08 | 169.07-249.04 | 41117212 | 109580032 |
| production / 2 | original | Sidebar.akbura / localized-literal | 823.45 | 642.94-1061.82 | 138803220 | 146830728 |
| production / 2 | prehashed | DataTemplates.akbura / localized-binding-sibling | 390.40 | 375.28-413.73 | 116270340 | 281526584 |
| production / 2 | prehashed | NavButton.akbura / localized-binding-sibling | 133.31 | 107.51-192.21 | 25846972 | 108172204 |
| production / 2 | prehashed | Counter.akbura / localized-literal | 212.83 | 209.66-243.15 | 53028560 | 277210264 |
| production / 2 | prehashed | DataTemplatesPage.akbura / localized-literal | 3520.09 | 3487.28-3550.61 | 1155343996 | 323923156 |
| production / 2 | prehashed | NeedHelp.akbura / localized-literal | 171.70 | 151.60-307.73 | 39386220 | 111848660 |
| production / 2 | prehashed | Sidebar.akbura / localized-literal | 580.20 | 534.19-895.27 | 127535148 | 149736548 |
| production / 2 | rebuilt-current | DataTemplates.akbura / localized-binding-sibling | 389.06 | 380.25-427.48 | 116587532 | 282059404 |
| production / 2 | rebuilt-current | NavButton.akbura / localized-binding-sibling | 126.17 | 104.35-206.31 | 25868632 | 107842184 |
| production / 2 | rebuilt-current | Counter.akbura / localized-literal | 225.21 | 211.67-237.59 | 53048644 | 277220748 |
| production / 2 | rebuilt-current | DataTemplatesPage.akbura / localized-literal | 3557.92 | 3527.49-3610.59 | 1155067296 | 324161572 |
| production / 2 | rebuilt-current | NeedHelp.akbura / localized-literal | 170.02 | 154.10-296.41 | 39332620 | 111936536 |
| production / 2 | rebuilt-current | Sidebar.akbura / localized-literal | 606.48 | 541.95-862.31 | 127699292 | 149701240 |
| production / 2 | repeat-admission | DataTemplates.akbura / localized-binding-sibling | 406.06 | 391.06-419.16 | 118954008 | 282946040 |
| production / 2 | repeat-admission | NavButton.akbura / localized-binding-sibling | 132.58 | 115.18-220.20 | 27309872 | 107450400 |
| production / 2 | repeat-admission | Counter.akbura / localized-literal | 210.98 | 208.26-218.81 | 52999460 | 276821864 |
| production / 2 | repeat-admission | DataTemplatesPage.akbura / localized-literal | 3624.06 | 3586.49-3666.11 | 1162966356 | 325124040 |
| production / 2 | repeat-admission | NeedHelp.akbura / localized-literal | 182.59 | 153.41-276.02 | 40692580 | 110973308 |
| production / 2 | repeat-admission | Sidebar.akbura / localized-literal | 594.34 | 542.34-1025.78 | 133782468 | 148173356 |
| stats / 0 | current | DataTemplates.akbura / localized-binding-sibling | 455.91 | 442.38-480.08 | 116967880 | 281671136 |
| stats / 0 | current | NavButton.akbura / localized-binding-sibling | 128.18 | 119.92-177.40 | 26060540 | 108018436 |
| stats / 0 | current | Counter.akbura / localized-literal | 259.16 | 239.87-292.02 | 54758976 | 278101964 |
| stats / 0 | current | DataTemplatesPage.akbura / localized-literal | 3806.33 | 3786.21-3905.69 | 1156494200 | 324102792 |
| stats / 0 | current | NeedHelp.akbura / localized-literal | 197.52 | 181.46-307.27 | 39466040 | 112006152 |
| stats / 0 | current | Sidebar.akbura / localized-literal | 633.73 | 582.23-908.71 | 128691352 | 150053320 |
| stats / 0 | disabled | DataTemplates.akbura / localized-binding-sibling | 460.60 | 451.79-469.75 | 122455076 | 286161196 |
| stats / 0 | disabled | NavButton.akbura / localized-binding-sibling | 136.37 | 126.67-169.83 | 29610168 | 110186208 |
| stats / 0 | disabled | Counter.akbura / localized-literal | 235.95 | 230.90-243.10 | 53522528 | 276852932 |
| stats / 0 | disabled | DataTemplatesPage.akbura / localized-literal | 3941.16 | 3896.73-3979.85 | 1184215468 | 318460700 |
| stats / 0 | disabled | NeedHelp.akbura / localized-literal | 207.28 | 186.74-273.25 | 41050000 | 109422204 |
| stats / 0 | disabled | Sidebar.akbura / localized-literal | 730.00 | 647.69-806.01 | 139818348 | 147110228 |
| stats / 0 | prehashed | DataTemplates.akbura / localized-binding-sibling | 423.81 | 410.01-435.05 | 116967260 | 281488984 |
| stats / 0 | prehashed | NavButton.akbura / localized-binding-sibling | 122.79 | 115.67-170.09 | 26072476 | 107979488 |
| stats / 0 | prehashed | Counter.akbura / localized-literal | 225.99 | 222.05-232.73 | 53567836 | 277238032 |
| stats / 0 | prehashed | DataTemplatesPage.akbura / localized-literal | 3628.92 | 3594.42-3670.40 | 1156171444 | 323750168 |
| stats / 0 | prehashed | NeedHelp.akbura / localized-literal | 176.54 | 165.97-257.40 | 39689244 | 111999376 |
| stats / 0 | prehashed | Sidebar.akbura / localized-literal | 600.72 | 562.04-767.48 | 128307548 | 149752960 |
| stats / 0 | repeat-admission | DataTemplates.akbura / localized-binding-sibling | 442.83 | 431.94-468.31 | 119658476 | 282925040 |
| stats / 0 | repeat-admission | NavButton.akbura / localized-binding-sibling | 136.13 | 123.43-189.56 | 27549064 | 107607700 |
| stats / 0 | repeat-admission | Counter.akbura / localized-literal | 226.64 | 214.59-229.52 | 53571428 | 277053736 |
| stats / 0 | repeat-admission | DataTemplatesPage.akbura / localized-literal | 3735.30 | 3709.42-3778.82 | 1164608192 | 324411756 |
| stats / 0 | repeat-admission | NeedHelp.akbura / localized-literal | 186.81 | 169.98-296.16 | 40938548 | 111115300 |
| stats / 0 | repeat-admission | Sidebar.akbura / localized-literal | 628.71 | 588.38-882.40 | 134889828 | 148455076 |

Original/current production controls are historical frozen analyzers; rebuilt-current controls for the audit refactoring. Disabled uses the same current compiler with only scoped memoization bypassed. Prehashed preserves full exact-text/type/query-span/syntax equality and computes the hash once. Repeat-admission additionally retains a result only after a second successful exact request; admission keys still retain current-owner data.

STATS timing scopes include their instrumentation overhead. Lookup/equality/syntax/hash scopes are nested and must not be summed. Factory-binding time is actual executed work, not a prediction of time saved by hits. Post-GC heap is the whole active-driver heap, not an isolated cache-size measurement. Published-entry counts are actual successful insertions during that update, not the number of all live entries across the process. A rejected key comparison may be a hash collision; its counter is not by itself a full collision-cost model.

Completed cells: 96. Fresh-driver checks: 1056. No final cache decision or cross-revision implementation is asserted by this generated file.
