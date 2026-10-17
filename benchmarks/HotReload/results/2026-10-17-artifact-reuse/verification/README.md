# Correctness and ownership verification

All compiler/unit-test runs completed before the latency matrix; no tests/builds overlapped timed samples. The 19 invalidation-report and 17 existing report checks were repeated successfully after the final timing run. The final syntax/style, provenance, archive-completeness and CRLF checks are recorded in [final-checks.json](final-checks.json).

| Run | Host | Passed / failed |
| --- | --- | --- |
| BlackSilence, project-index, document-version and statistics regressions, including 44 new artifact cases | Package Roslyn 4.11, DebugStats | 274 / 0 |
| Invalidation/statistics and all artifact cases, including 100/500 revisions | Isolated SDK Roslyn 5.9, DebugStats | 61 / 0 |
| Artifact cases excluding the two long lifetime cases | Package Roslyn 4.11, Release | 39 / 0 |
| Focused markup/global-fact memo and isolated-leaf checks | Package Roslyn 4.11, DebugStats | 5 / 0 |
| Changed/new C# brace and multiline XML checks | SDK Roslyn syntax parser / text checks | 22 files / 0 violations |
| Invalidation report generation | PowerShell 7 | 19 / 0 |
| Existing benchmark report generation | PowerShell 7 | 17 / 0 |

The Release set excludes three STATS-only cancellation cases by compilation, in addition to the two explicitly excluded long cases. DebugStats and SDK-host runs include all five. Existing repository warnings in unrelated Furioso/test files and nullable warnings in the prepared tutorial are not new generator failures. Gallery/Purity preparation builds succeeded. Analyzer/tool builds succeeded without warnings.

All executed cases in the final TRX files passed, with zero skipped cases. These are the relevant selected suites, not a claim that every repository test was rerun. The deliberately failing pre-fix global-import TRX below is historical regression evidence and is excluded from final pass counts.

```powershell
dotnet test src/Akbura.UnitTests/Akbura.UnitTests.csproj -c DebugStats --filter 'FullyQualifiedName~DetachedArtifacts_MarkupEdit|FullyQualifiedName~DetachedArtifacts_ChangedGlobalMarkup|FullyQualifiedName~DetachedArtifacts_IsolatedLeaf' --logger 'trx;LogFileName=markup-memo-focused.trx' --results-directory bin/CrossCompilationReuse/tests -- xUnit.MaxParallelThreads=4
dotnet test src/Akbura.UnitTests/Akbura.UnitTests.csproj -c DebugStats --no-build --filter 'FullyQualifiedName~BlackSilence|FullyQualifiedName~AkburaProjectIndex|FullyQualifiedName~DocumentSyntaxVersion|FullyQualifiedName~GenerationStatistics' --logger 'trx;LogFileName=artifact-regression-memo-final.trx' --results-directory bin/CrossCompilationReuse/tests -- xUnit.MaxParallelThreads=4
& ./benchmarks/HotReload/Verify-SdkRoslyn.ps1 -OutputDirectory bin/CrossCompilationReuse/sdk-tests-memo -TestFilter 'FullyQualifiedName~InvalidationTrace_|FullyQualifiedName~BlackSilenceGenerationStatisticsTests|FullyQualifiedName~DetachedArtifacts_' -ExpectedTestCount 61
dotnet test src/Akbura.UnitTests/Akbura.UnitTests.csproj -c Release --filter 'FullyQualifiedName~DetachedArtifacts_&FullyQualifiedName!~OnlyNewestStoreAlive' --logger 'trx;LogFileName=artifact-release-memo-final.trx' --results-directory bin/CrossCompilationReuse/tests -- xUnit.MaxParallelThreads=4
& ./benchmarks/HotReload/Test-InvalidationReport.ps1
& ./benchmarks/HotReload/Test-Report.ps1
```

## Isolated newest-owner GC proof

| Case | Revisions / branches | Deliberately retained root | Compilation-bound targets alive after full GC | Distinct detached generations alive |
| --- | --- | --- | --- | --- |
| Successive edits, including markup every 13th revision | 100 | Newest artifact store | 0 | At most 1 |
| Successive edits, including markup every 13th revision | 500 | Newest artifact store | 0 | At most 1 |
| Concurrent unit-driver forks | 4 | Shared newest artifact store | 0 | At most 1 |
| Canceled lookup, validation and preparation; successful retry | 5 attempts at each of 3 stages | Newest artifact store | 0 | At most 1 |
| Current Roslyn request remains alive after same-compilation markup edit | 1 update | Current request/current context | Previous snapshot/index/Akbura compilation/model/probe context: 0 | Current publication only |
| Document removal | 1 update | Newest artifact store | Previous detached generation and removed source: 0 | 1, removed entry absent |

Both 100/500 tests ran under package and SDK Roslyn. Every incremental update has exact source and full diagnostic-value parity with a fresh driver. Weak observations include C# and Akbura compilations, indexes, states, bound/detached snapshots, created Akbura/Roslyn semantic models, probe compilations and probe contexts. Non-inlined helpers release legitimate driver/compiler roots before collection; repeated forced GC avoids a single JIT-local/collection artifact. Only weak references and scalar observations are accumulated by the tests. A recursive audit of the live detached value graph rejects compilation-bound values and arbitrary `SourceText` implementations.

Out-of-order publication, schema mismatch, independent drivers, shared current-compilation input facts, diagnostic-only evaluation, compiler options/PE identity, mappings, AKCSS imports, add/remove trees and adverse semantic edits are checked separately. Unsupported changes **must** bind the current compilation; matching output is an oracle, never their reuse decision. Full TRX and SDK assembly/host-resolution metadata accompany this report.

Markup memo checks assert that unchanged scalar facts/text records can be shared in fresh current maps, while a changed global markup document rebuilds every consumer's lookup universe. A following C# leaf edit must then reject reuse if the new import names that leaf. The safe-leaf cases also assert zero new detached-text wrappers. Reused values never link to the bound snapshot that produced them.

The controlled [pre-fix global-import test](global-imports-before.trx) intentionally failed: a consumer's detached lookup universe omitted a static global import supplied by a different markup document. This was a validation-invariant failure, not a demonstrated generated-output mismatch. The final validator propagates global names to all consumers and rejects unclassified global dependencies for all of them. Both the valid-import and malformed-global cases pass in all final hosts. Preliminary timings taken before this strengthening remain in ignored scratch storage and are excluded from this archive.

## Real-project benchmark verification

The final frozen benchmark completed all 68 pipeline groups: 64 primary matrix groups, each with 11 updates/fresh comparisons, and four lifetime groups. It contains 704 primary driver observations, 704 primary exact source/diagnostic parity checks and 704 primary post-GC heap observations. The lifetime groups contain 206 sequential heap observations and 214 fresh comparisons including eight concurrent branches. All parity and owner-only GC assertions passed.

FeatureGallery ran 100 revisions per mode; each observed 103 C# compilations, Akbura compilations, indexes, states and bound snapshots including the forks, with zero alive after releasing legitimate roots. Enabled Gallery retained exactly one distinct detached generation and 33 current entries/193 weak identity slots; disabled retained none. Purity ran one revision plus two forks per mode, observed four compiler contexts and collected all four, with one enabled detached generation/15 entries/194 slots. This Purity sequence is not presented as a long-session memory stress test.

The [measurement report](../README.md#lifetime-and-concurrency) includes actual heap, allocations, working set, active-owner counts and the slower single Purity concurrent observation. Top-level entry counts alone are not used as evidence of collectibility. Both historical October 16 archives are unchanged. The final implementation-source and analyzer hashes still match the provenance captured before measurement.
