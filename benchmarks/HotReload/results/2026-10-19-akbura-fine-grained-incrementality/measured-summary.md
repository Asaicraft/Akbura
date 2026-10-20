# Measured localized markup edit results

Generated from completed groups, excluding the initial revision and two warm-ups. Each cell contains eight measured edits. Negative change means less elapsed time/allocation; positive means more. Inclusive stages are not additive. These are generator-driver measurements, not complete Hot Reload latency.

## Executed verification

| Suite | Executed | Passed | Failed |
| --- | ---: | ---: | ---: |
| Relevant DebugStats | 546 | 546 | 0 |
| SDK Roslyn host | 238 | 238 | 0 |
| Relevant Release | 284 | 284 | 0 |
| Complete Release Akbura.UnitTests | 3543 | 3543 | 0 |

Suites overlap; these are not additive unique-test counts. Detailed TRXs and actual SDK identities are archived under verification/.

## Instrumented

Matching before/after source/diagnostic records: 880. All measured revisions also passed fresh-driver parity.

| Component / edit | Generator ms before | After | Change | Process bytes before | After | Probe compilations before | After |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Counter.akbura / localized-akcss | 220.83 | 232.58 | +5.32% | 53655676 | 54806400 | 93 | 97 |
| Counter.akbura / localized-binding-sibling | 227.34 | 226.65 | -0.31% | 53547864 | 53579444 | 93 | 93 |
| Counter.akbura / localized-diagnostic | 260.74 | 262.60 | +0.71% | 53443612 | 54065104 | 92.5 | 94.5 |
| Counter.akbura / localized-expression | 236.49 | 235.26 | -0.52% | 55786056 | 54564888 | 100 | 96 |
| Counter.akbura / localized-literal | 222.75 | 224.91 | +0.97% | 53440944 | 53566764 | 93 | 93 |
| Counter.akbura / localized-name | 231.41 | 268.30 | +15.94% | 53321804 | 53441928 | 92 | 92 |
| Counter.akbura / localized-property | 259.34 | 227.18 | -12.40% | 53704528 | 53728052 | 93 | 93 |
| Counter.akbura / localized-property-add-remove | 220.81 | 274.24 | +24.20% | 53547748 | 60085624 | 92.5 | 114.5 |
| Counter.akbura / localized-structure | 238.12 | 222.79 | -6.44% | 53920264 | 53937672 | 93 | 93 |
| Counter.akbura / localized-trivia | 273.81 | 264.39 | -3.44% | 53364520 | 53373368 | 92 | 92 |
| DataTemplates.akbura / localized-binding-sibling | 437.37 | 541.24 | +23.75% | 122589104 | 123551860 | 153.5 | 136 |
| DataTemplates.akbura / localized-expression | 451.99 | 477.71 | +5.69% | 122477112 | 116699120 | 152.5 | 113 |
| DataTemplatesPage.akbura / localized-akcss | 3872.33 | 4343.10 | +12.16% | 1184549148 | 1156527508 | 622 | 419 |
| DataTemplatesPage.akbura / localized-diagnostic | 3996.46 | 4227.91 | +5.79% | 1190877676 | 1156042624 | 643.5 | 416.5 |
| DataTemplatesPage.akbura / localized-literal | 3944.69 | 5284.92 | +33.98% | 1185393216 | 1156135176 | 625 | 417 |
| DataTemplatesPage.akbura / localized-name | 4253.26 | 4245.63 | -0.18% | 1184024028 | 1156408824 | 620 | 417 |
| DataTemplatesPage.akbura / localized-property | 3948.43 | 4367.15 | +10.60% | 1185411592 | 1160398476 | 625 | 430 |
| DataTemplatesPage.akbura / localized-property-add-remove | 3844.91 | 4101.40 | +6.67% | 1184238860 | 1162522900 | 620.5 | 438.5 |
| DataTemplatesPage.akbura / localized-structure | 4608.17 | 4229.66 | -8.21% | 1184422792 | 1158958060 | 620.5 | 425.5 |
| DataTemplatesPage.akbura / localized-trivia | 4249.70 | 4309.67 | +1.41% | 1184021840 | 1156087136 | 620 | 416 |
| NavButton.akbura / localized-binding-sibling | 150.46 | 141.36 | -6.05% | 29558232 | 26068128 | 82 | 61 |
| NavButton.akbura / localized-expression | 153.36 | 121.88 | -20.53% | 29425204 | 25897592 | 81 | 60 |
| NeedHelp.akbura / localized-akcss | 194.02 | 220.74 | +13.77% | 41215048 | 39391292 | 95 | 82.5 |
| NeedHelp.akbura / localized-binding-sibling | 209.85 | 227.17 | +8.25% | 41038048 | 39394016 | 95 | 83 |
| NeedHelp.akbura / localized-diagnostic | 181.24 | 214.48 | +18.34% | 41024736 | 39328644 | 94.5 | 82.5 |
| NeedHelp.akbura / localized-expression | 192.79 | 211.38 | +9.64% | 41042928 | 39373748 | 94 | 82 |
| NeedHelp.akbura / localized-literal | 207.60 | 221.86 | +6.87% | 41104116 | 39555336 | 95 | 83 |
| NeedHelp.akbura / localized-name | 240.34 | 338.13 | +40.69% | 47453400 | 45727828 | 100 | 88 |
| NeedHelp.akbura / localized-property | 196.37 | 219.81 | +11.94% | 41064392 | 39360516 | 95 | 83 |
| NeedHelp.akbura / localized-property-add-remove | 199.77 | 203.13 | +1.69% | 41024620 | 39238360 | 94.5 | 82.5 |
| NeedHelp.akbura / localized-structure | 257.66 | 243.32 | -5.57% | 41268952 | 39474776 | 94.5 | 82.5 |
| NeedHelp.akbura / localized-trivia | 192.55 | 213.67 | +10.97% | 40879780 | 39178348 | 94 | 82 |
| Sidebar.akbura / localized-akcss | 692.75 | 695.65 | +0.42% | 139820852 | 128232660 | 362 | 295 |
| Sidebar.akbura / localized-diagnostic | 695.04 | 776.43 | +11.71% | 139739428 | 128024632 | 361.5 | 294.5 |
| Sidebar.akbura / localized-literal | 699.28 | 687.04 | -1.75% | 139532104 | 128098616 | 362 | 295 |
| Sidebar.akbura / localized-name | 665.25 | 746.15 | +12.16% | 139461304 | 128059336 | 361 | 294 |
| Sidebar.akbura / localized-property | 680.71 | 638.56 | -6.19% | 139712912 | 128063372 | 362 | 295 |
| Sidebar.akbura / localized-property-add-remove | 719.16 | 832.73 | +15.79% | 139502924 | 128162640 | 361.5 | 294.5 |
| Sidebar.akbura / localized-structure | 684.07 | 679.10 | -0.73% | 139718792 | 128393348 | 361.5 | 294.5 |
| Sidebar.akbura / localized-trivia | 639.79 | 741.12 | +15.84% | 139406660 | 128113700 | 361 | 294 |

Full ranges, initial generation, per-stage costs and post-GC retained heaps are in the adjacent before/after reports and comparison.csv. Do not infer a speedup from a probe-count reduction alone.

## Production Release, no STATS call sites

Matching before/after source/diagnostic records: 132. All measured revisions also passed fresh-driver parity.

| Component / edit | Generator ms before | After | Change | Process bytes before | After | Probe compilations before | After |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Counter.akbura / localized-binding-sibling | 227.66 | 226.00 | -0.73% | 52956400 | 52987972 | unavailable | unavailable |
| Counter.akbura / localized-diagnostic | 230.04 | 252.39 | +9.72% | 52911960 | 52910992 | unavailable | unavailable |
| Counter.akbura / localized-literal | 240.25 | 249.04 | +3.66% | 54176184 | 52970712 | unavailable | unavailable |
| NavButton.akbura / localized-binding-sibling | 163.64 | 178.09 | +8.83% | 29384700 | 25860216 | unavailable | unavailable |
| Sidebar.akbura / localized-diagnostic | 693.54 | 640.87 | -7.59% | 138591456 | 127426436 | unavailable | unavailable |
| Sidebar.akbura / localized-literal | 726.53 | 634.38 | -12.68% | 138684808 | 127318844 | unavailable | unavailable |

Full ranges, initial generation, per-stage costs and post-GC retained heaps are in the adjacent before/after reports and comparison.csv. Do not infer a speedup from a probe-count reduction alone.

## Current-owner cache observations

| Component / edit | Requests (median) | Cache hits (median) | Misses (median) | Recovery/directive bypasses (median) |
| --- | ---: | ---: | ---: | ---: |
| Counter.akbura / localized-akcss | 17 | 0 | 17 | 0 |
| Counter.akbura / localized-binding-sibling | 17 | 0 | 17 | 0 |
| Counter.akbura / localized-diagnostic | 16.5 | 0 | 16.5 | 0 |
| Counter.akbura / localized-expression | 16 | 0 | 16 | 0 |
| Counter.akbura / localized-literal | 17 | 0 | 17 | 0 |
| Counter.akbura / localized-name | 16 | 0 | 16 | 0 |
| Counter.akbura / localized-property | 17 | 0 | 17 | 0 |
| Counter.akbura / localized-property-add-remove | 16.5 | 0 | 16.5 | 0 |
| Counter.akbura / localized-structure | 16.5 | 0 | 16.5 | 0 |
| Counter.akbura / localized-trivia | 16 | 0 | 16 | 0 |
| DataTemplates.akbura / localized-binding-sibling | 75 | 39 | 36 | 0 |
| DataTemplates.akbura / localized-expression | 75 | 40 | 35 | 0 |
| DataTemplatesPage.akbura / localized-akcss | 344 | 204 | 140 | 0 |
| DataTemplatesPage.akbura / localized-diagnostic | 343.5 | 204 | 139.5 | 0 |
| DataTemplatesPage.akbura / localized-literal | 344 | 204 | 140 | 0 |
| DataTemplatesPage.akbura / localized-name | 343 | 204 | 139 | 0 |
| DataTemplatesPage.akbura / localized-property | 344 | 204 | 140 | 0 |
| DataTemplatesPage.akbura / localized-property-add-remove | 343.5 | 204 | 139.5 | 0 |
| DataTemplatesPage.akbura / localized-structure | 343.5 | 204 | 139.5 | 0 |
| DataTemplatesPage.akbura / localized-trivia | 343 | 204 | 139 | 0 |
| NavButton.akbura / localized-binding-sibling | 56 | 21 | 35 | 0 |
| NavButton.akbura / localized-expression | 55 | 21 | 34 | 0 |
| NeedHelp.akbura / localized-akcss | 66 | 12.5 | 53.5 | 0 |
| NeedHelp.akbura / localized-binding-sibling | 66 | 12 | 54 | 0 |
| NeedHelp.akbura / localized-diagnostic | 65.5 | 12 | 53.5 | 0 |
| NeedHelp.akbura / localized-expression | 65 | 12 | 53 | 0 |
| NeedHelp.akbura / localized-literal | 66 | 12 | 54 | 0 |
| NeedHelp.akbura / localized-name | 65 | 12 | 53 | 0 |
| NeedHelp.akbura / localized-property | 66 | 12 | 54 | 0 |
| NeedHelp.akbura / localized-property-add-remove | 65.5 | 12 | 53.5 | 0 |
| NeedHelp.akbura / localized-structure | 65.5 | 12 | 53.5 | 0 |
| NeedHelp.akbura / localized-trivia | 65 | 12 | 53 | 0 |
| Sidebar.akbura / localized-akcss | 248 | 67 | 181 | 0 |
| Sidebar.akbura / localized-diagnostic | 247.5 | 67 | 180.5 | 0 |
| Sidebar.akbura / localized-literal | 248 | 67 | 181 | 0 |
| Sidebar.akbura / localized-name | 247 | 67 | 180 | 0 |
| Sidebar.akbura / localized-property | 248 | 67 | 181 | 0 |
| Sidebar.akbura / localized-property-add-remove | 247.5 | 67 | 180.5 | 0 |
| Sidebar.akbura / localized-structure | 247.5 | 67 | 180.5 | 0 |
| Sidebar.akbura / localized-trivia | 247 | 67 | 180 | 0 |

## Observed generated C# difference for Margin="10" to Margin="20"

DebugStructural:

```diff
-            "D0F1669E12503A18";
+            "AB28EDA24CA8E84E";
-                "AD002153AB0ED5980DB74DB51D36F3817E34A778001E3A690456C2CCEB93DAE7",
+                "7C364477703BB0C25335B03CD877C9CAFE3E1F2F94265AB31DF93A979ECC04BE",
-                    "6028E00A78D72B695B5110B8F345C47D9F39D3FE17CA3F6AAB84E52663087B6E",
+                    "534B9ED6C251DB4FF63694B3BA5F5E5D3AD0E6F54D5FBFE5FF2725BCE91169FF",
-                    global::Avalonia.Thickness.Parse("10"));
+                    global::Avalonia.Thickness.Parse("20"));
-                "6028E00A78D72B695B5110B8F345C47D9F39D3FE17CA3F6AAB84E52663087B6E",
+                "534B9ED6C251DB4FF63694B3BA5F5E5D3AD0E6F54D5FBFE5FF2725BCE91169FF",
-                global::Avalonia.Thickness.Parse("10"));
+                global::Avalonia.Thickness.Parse("20"));
```

ReleaseDirect:

```diff
-            ((global::Avalonia.AvaloniaObject)__element1).SetValue(global::Avalonia.Layout.Layoutable.MarginProperty, global::Avalonia.Thickness.Parse("10"));
+            ((global::Avalonia.AvaloniaObject)__element1).SetValue(global::Avalonia.Layout.Layoutable.MarginProperty, global::Avalonia.Thickness.Parse("20"));
-            ((global::Avalonia.AvaloniaObject)__element1).SetValue(global::Avalonia.Layout.Layoutable.MarginProperty, global::Avalonia.Thickness.Parse("10"));
+            ((global::Avalonia.AvaloniaObject)__element1).SetValue(global::Avalonia.Layout.Layoutable.MarginProperty, global::Avalonia.Thickness.Parse("20"));
```
