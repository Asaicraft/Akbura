# Real-member declaration contracts

This archive concerns generator-only reuse after implementation changes in an existing multi-member ViewModel or service, including members directly referenced by markup. Historical October 17 isolated-leaf measurements remain unchanged. Paired controls in this archive must use the same final binary, inputs and profiling mode; the pre-implementation trace run is separate reproduction evidence, not a speedup denominator.

## Outcome and reproducibility

Completed on branch `feature/improve-hot-reload`, based on commit `e7aa465ffdda6328e85fbc89c56e88b05145fd0d`. The implementation was uncommitted when measured; that commit alone does **not** identify the new implementation. [Environment/source hashes](invalidation-environment.json), [frozen tool/input hashes](run.json) and [per-group provenance](invalidation-provenance.json) identify the actual measured code and inputs. No merge or rebase was performed.

The final matrix ran from October 17, 2026, 18:56 UTC to 21:57 UTC (October 18, 00:56–03:57 in Asia/Bishkek). It completed all 48 main groups, 754 fresh-generation comparisons, a separate eight-comparison real-project preflight, and nine live C# Hot Reload edits. Relevant package-host DebugStats, SDK-host and Release tests all passed. The [portable evidence audit](verification/results-audit.json) independently checks archived TRXs, raw parity records, concurrent indices, owner-only weak observations and the live transcript. Re-run it without rebuilding projects:

```powershell
& ./benchmarks/HotReload/results/2026-10-17-member-contracts/verification/Verify-Results.ps1
```

For the ten project/scenario pairs covering the five newly supported pure implementation edits, the arithmetic mean of `100 * (1 - enabled median / disabled median)` is **99.56% less GeneratorDriver time**. This excludes mixed markup edits, fallback cases, the pre-existing isolated leaf, cold generation and live watch. It is not an overall application or IDE speedup.

| Edit | Gallery disabled → enabled, ms | Purity disabled → enabled, ms | Components reused, Gallery / Purity |
| --- | ---: | ---: | ---: |
| Existing ViewModel method (`IsActive`) | 8,479.22 → 24.12 | 1,920.22 → 11.40 | 28 / 15 |
| Directly referenced method (`GetName`) | 8,350.80 → 22.80 | 1,944.26 → 11.41 | 28 / 15 |
| Service instance method (`Format`) | 8,407.63 → 25.33 | 1,920.06 → 11.10 | 28 / 15 |
| Explicit property getter (`Name`) | 8,107.82 → 23.20 | 1,903.34 → 11.29 | 28 / 15 |
| Multiple bodies in one class | 8,168.01 → 22.54 | 1,930.25 → 11.18 | 28 / 15 |
| Method body plus one markup edit | 8,279.25 → 672.32 | 1,975.00 → 1,121.87 | 27 / 14 |
| Ordinary markup only | 220.39 → 230.82 | 628.86 → 615.73 | 27 / 14 |
| Signature change, mandatory fallback | 8,139.07 → 8,205.47 | 1,942.28 → 1,948.97 | 0 / 0 |
| Added overload, mandatory fallback | 8,111.20 → 8,169.64 | 1,906.03 → 1,972.37 | 0 / 0 |

The realistic fixture contains an instance ViewModel, fields, an auto-property, an explicit getter/setter, several methods and a separate service class. It is inserted into the real evaluated Gallery and pinned Purity projects; markup directly calls the edited member through `Current.Vm` or `Current.Service`. The service scenario measures an instance call, not dependency-container activation. It does not pretend to edit the tutorial's existing partial ViewModel. [Fixture source](../../Program.Contracts.cs) specifies the exact edits. Purity is pinned to `f752e29d7dc791c7fcdac6b740aad7a52552738e`; local analyzer selection uses the existing benchmark host, not modifications to the tutorial checkout.

Measured SHA-256 values:

| Assembly | SHA-256 |
| --- | --- |
| Akbura.BlackSilence.dll | `874394E2280F28D9DCC32899FDB993B0AED3E7E5DB7BBAE303DDD55924A80609` |
| Akbura.Generator.dll | `6F282990D792A6B81598341B8A7F72C6D35818F1B74211EE21577DA7658EDDA8` |
| HotReloadBenchmarks.dll | `F4B65C42EE344CB168A392407BCFA6C2D5CE0BE45C9271A20D0ACE94A8127C97` |

Subsequent source edits finish the report, clarify an XML summary and recognize the SDK's `File change: Update` log spelling. They do not change the generator algorithm measured by these frozen binaries. Original measured watch rows and all frozen hashes are preserved.

## Roslyn research and validation argument

Roslyn's [tree equivalence API](https://github.com/dotnet/roslyn/blob/7ed110c0f961a5f8800139bc2df914ffb0099aca/src/Compilers/CSharp/Portable/Syntax/CSharpSyntaxTree.cs) delegates to syntax equivalence. Its [implementation](https://github.com/dotnet/roslyn/blob/7ed110c0f961a5f8800139bc2df914ffb0099aca/src/Compilers/CSharp/Portable/Syntax/SyntaxEquivalence.cs) can skip blocks, arrow clauses and non-constant field initializers in top-level mode. This is useful for understanding declaration invalidation, but is not the BlackSilence proof: exact non-body source, initializers, directives, member presence, containing structure and independent diagnostic mappings still matter. No previous syntax node is retained or reparsed solely to call that API.

The SDK's [ordinary-method implementation](https://github.com/dotnet/dotnet/blob/e34a38d2ae1fc26406a317517196e55c68ff83ab/src/roslyn/src/Compilers/CSharp/Portable/Symbols/Source/SourceOrdinaryMethodSymbol.cs) binds explicit return/parameter types from declaration syntax and determines iterator flags from the body. Consequently, implementation-body exclusion must not accidentally accept iterator/async, partial, inferred or other unclassified forms. The supported set is deliberately narrower than all syntax which Roslyn calls top-level equivalent.

[TransformNode](https://github.com/dotnet/roslyn/blob/7ed110c0f961a5f8800139bc2df914ffb0099aca/src/Compilers/Core/Portable/SourceGeneration/Nodes/TransformNode.cs) still executes transforms after modified upstream inputs before comparing results; [NodeStateTable](https://github.com/dotnet/roslyn/blob/7ed110c0f961a5f8800139bc2df914ffb0099aca/src/Compilers/Core/Portable/SourceGeneration/Nodes/NodeStateTable.cs) preserves equal values and compacts removals. Neither supplies semantic equivalence across different compilations. [SemanticModel](https://github.com/dotnet/roslyn/blob/7ed110c0f961a5f8800139bc2df914ffb0099aca/src/Compilers/Core/Portable/Compilation/SemanticModel.cs) belongs to its compilation/tree and caches symbols; these objects remain outside every detached contract.

### Explicit proof boundary

The supported regions are ordinary, explicitly typed method bodies in non-generic, non-partial, non-nested class declarations, and explicitly typed property accessor implementations with no auto/init/required/backing-field synthesis. Changed trees containing any attribute syntax are excluded, including attributes on other declarations or parameters. An earlier body can add lines and change a later attribute's implicit `CallerLineNumber` argument even while every attribute token remains identical; a regression explicitly checks that semantic difference, consistent with the C# [attribute caller-information rules](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/attributes#233-attribute-specification). File-local declarations are also excluded. Methods/accessors have no async/iterator/extern/unsafe/partial semantics, local functions or other unclassified bodies. Constructors, indexers, generic/partial/nested declarations and uncertain bodies retain fallback. Existing isolated-leaf validation remains available independently.

Only approved implementation regions may differ. Exact source segments between those regions, ordered scalar member identities and body-form kinds must match; everything else remains an exact compiler input. This includes all other members, fields and their initializers, constants, attributes, imports/aliases, type ancestry, accessibility, member ordering, explicit types, parameters, defaults and constraints. Reference/options/parse/encoding identities retain their existing checks. Directives, parser errors and unknown compiler services reject the proof.

The complete declaration/candidate universe is therefore unchanged, rather than merely a collection of previously resolved positive references. No new overload, extension candidate, type, import or formerly missing declaration can enter through an accepted body region. The changed containing type may legitimately occur in markup lookups. Implicit generated-declaration collisions and unclassified document dependencies still reject reuse. A declaration-changing edit remains conservative for the whole project unless a separate complete independence argument applies; this does not claim arbitrary per-member semantic dependency tracking.

The contract has schema version 1 and contains exact non-body string segments plus ordered scalar member records (containing type name, member name, body syntax kind and body characters). Exact unchanged segments supply declaration identity, not a hash or a signature-only summary. Block-to-arrow changes are not accepted as body-only equivalence. The containing simple name must occur in exactly one old and one current source declaration; ambiguous names fall back even if a more elaborate namespace-aware proof might accept them. Detached artifact schema 2 adds scalar dependency identities, body/surface shapes and independent diagnostic dependency values.

Constructor bodies remain exact: this implementation does not claim a complete classification of base/this initialization, primary constructors, required-member checks or related synthesis. Getter/setter support does not imply support for auto-properties, init accessors or synthesized backing-field semantics.

After global C# validation, each document separately checks its identity, exact own shape, lookup context and dependency versions. Unchanged independent documents can survive a simultaneous markup edit; the changed document cannot. AKCSS/global dependency edges compare complete body shapes, while ordinary component edges compare declaration surfaces. Global imports and generated declaration collisions remain conservative. Unsupported declarations, changed references/options/tree sets or unclassified dependencies retain the existing rebinding path; there is no new arbitrary-C# dependency graph. `AkburaCrossCompilationReuse=false` continues to bypass detached reuse.

Generated source and diagnostic eligibility remain independent. A cached generator artifact cannot hide compiler errors in the user's edited body: compiler diagnostics are evaluated on the current output compilation and compared separately from BlackSilence diagnostics. Cached diagnostic locations tied to a changed C# source require current evaluation. Fresh generation is only an external test oracle, never part of the production validation path.

## Ownership

```text
Current Roslyn driver --> current request/state --> compilation, models, symbols
                     --> stable project/options artifact store
                           --> ONE latest detached publication
                                 --> scalar declaration segments/member records
                                 --> current document dependency values
                                 --> detached characters and diagnostic records
                           --> current weak identity handles
```

The implementation must retain no old syntax/model/symbol/operation/plan/version graph, previous-snapshot link, history collection or capturing validator. Contract construction may use current syntax transiently; publication contains only scalar values. Active-driver retention and isolated newest-owner retention are tested separately.

`BoundedArtifactStore` still publishes one `ArtifactPublication`, replacing its detached artifacts and current weak identity window atomically under the existing revision-order check. `ArtifactIdentitySession` is a short-lived strong dictionary, not a persistent root. Its exported identities use `WeakReference<object>`; dead targets cannot root historical compiler graphs. Scalar strings may be shared safely, but no old compilation, syntax tree, symbol, semantic model, operation, plan or version is transferred to the current compilation. The recursive audit starts at the persistent owner, treats weak targets as weak edges, and rejects capturing delegates and forbidden strong objects.

### Lifetime results

Both old isolated-leaf and new mixed real-member 100/500-revision cases passed in both compiler hosts. The new mixed cases alternate bodies, markup edits, unsupported signatures and reversions, compare every revision with fresh generation and retain only the newest owner afterward. Cancellation tests cover declaration construction, validation, artifact selection, semantic binding, capture and publication preparation, with three canceled attempts per boundary followed by a successful retry. Independent four-branch tests also passed.

| New mixed lifetime test | Package Roslyn 4.11 | SDK Roslyn 5.9 |
| --- | --- | --- |
| 100 revisions | Passed, 58.93 s | Passed, 65.99 s |
| 500 revisions | Passed, 316.85 s | Passed, 329.90 s |

Large-project measurements are a separate evidence set: Gallery has 101 sequential observations per mode (initial plus 100 changes), Purity has two per mode (initial plus one change), and each project/mode adds eight two-branch fork pairs. Together these produce 206 live-driver heap observations and 270 fresh comparisons. Purity is **not** claimed to have a 500-revision real-project benchmark; the 100/500 mixed stress tests above use focused fixtures.

| Owner-only endpoint | Managed heap, disabled → enabled, MiB | Live detached generations, disabled → enabled | Enabled entries / weak identity slots |
| --- | ---: | ---: | ---: |
| Gallery, after 100 changes and 16 fork branches | 33.47 → 54.13 | 0 → 1 | 33 / 193 |
| Purity, after one change and 16 fork branches | 28.44 → 31.97 | 0 → 1 | 15 / 194 |

All observed obsolete C# compilations, Akbura compilations, project indexes/states/snapshots, semantic models, probe contexts and probe compilations were dead after driver release. Gallery observed 117 compiler contexts per mode; Purity observed 18. Enabled owners kept exactly one detached snapshot; disabled owners kept none. Cache payload has a real bounded memory cost: the owner-only managed heap is about 20.66 MiB larger in Gallery and 3.53 MiB larger in Purity, not zero.

While the current driver was alive, Gallery's enabled post-GC heap ranged from 174.38 to 267.28 MiB and ended at 174.70 MiB; disabled ranged from 246.50 to 383.15 MiB. Live observed project-state owners ranged from one to two in both modes. These active-driver roots are not the isolated-owner leak test. Process working set also includes native/runtime allocations and is not a retained-managed-object count. The endpoint weak checks, bounded publication structure and 100/500 owner graph tests support the ownership claim; finite runs are not a proof for every possible Roslyn host.

## Performance interpretation

The paired controls use the same DebugStats implementation, SDK Roslyn 5.9, inputs and `stats` protocol. Each of 44 groups has one initial run, two warm-ups and eight measured updates. Full sources, mappings and diagnostic values are compared with a fresh cache-disabled driver on all 11 revisions, outside the measured driver interval. Source and compiler diagnostic oracles remain distinct. The machine was Windows 11, .NET SDK 10.0.401, runtime 10.0.12, i5-11400H, 12 logical processors and approximately 16 GiB RAM. The controller froze the complete tool closure, hashed evaluated inputs, and prevented overlapping CLI build/test activity; it did not make this an isolated laboratory machine or measure Visual Studio.

### Where the saved time went

These are medians for the directly referenced method-body edit:

| Metric | Gallery disabled → enabled | Purity disabled → enabled |
| --- | ---: | ---: |
| Semantic inputs prepared | 33 → 0 | 15 → 0 |
| Semantic models created | 31 → 0 | 15 → 0 |
| Probe compilations / probe semantic models | 2,892 → 0 | 869 → 0 |
| Probe diagnostic queries | 2,911 → 0 | 872 → 0 |
| Components emitted / reused | 28 / 0 → 0 / 28 | 15 / 0 → 0 / 15 |
| Diagnostic documents evaluated / detached-reused | 32 / 0 → 0 / 32 | 17 / 0 → 0 / 17 |
| Total generated texts created | 33 → 0 | 15 → 0 |
| Process managed allocations, MiB | 2,861.09 → 7.43 | 907.72 → 4.17 |
| Active-driver post-GC heap, MiB | 410.27 → 201.47 | 192.18 → 85.01 |
| Artifact validation, ms | 0 → 11.74 | 0 → 6.88 |
| Declaration contract construction, ms | 0 → 0.316 | 0 → 0.347 |
| Declaration contract comparison, ms | 0 → 0.024 | 0 → 0.021 |

Contract construction records 19 Gallery / 8 Purity tree analyses per changed compilation. Counts include conservative exclusions. Validation includes exact environment and document checks; it is not only member comparison. Stage durations are inclusive and must not be summed. Allocation figures are bytes allocated during the measured interval, not a number of allocation objects; live heaps include legitimate current drivers. All per-scenario allocations, heaps, diagnostic counts and stages are in [invalidation-summary.csv](invalidation-summary.csv) and the detailed CSV files.

The simultaneous markup edit still needs its own binding/emission, hence 672.32 / 1,121.87 ms rather than the pure-body path's 22.80 / 11.41 ms. Independent components and AKCSS modules remain reusable. Declaration-changing edits deliberately prepare all 33 / 15 semantic inputs and reuse no detached sources. Signature overhead was +0.82% / +0.34%; overload overhead +0.72% / +3.48%, with overlapping ranges. Ordinary markup was +4.73% (+10.43 ms) in Gallery and -2.09% in Purity. No large markup regression appeared in this run, but the small sample and sequential mode order do not establish zero overhead or statistical significance. Initial generation still does full work and may pay artifact-capture overhead.

### Repeated concurrent branches

Each observation measures both branch generations before either fresh oracle starts. Branches share a previous driver, edit a real referenced body plus one markup document, and are independently verified afterward. This is not a timing pair polluted by the other branch's fresh generation.

| Project | Disabled median (range), ms | Enabled median (range), ms | Pairs per mode |
| --- | ---: | ---: | ---: |
| Gallery | 14,829.26 (12,412.45–15,497.76) | 1,023.22 (895.25–1,091.70) | 8 |
| Purity | 2,915.86 (2,817.70–3,589.38) | 1,535.36 (1,493.23–2,125.76) | 8 |

Enabled pair allocations were 1,307.90 MiB in Gallery and 1,431.94 MiB in Purity; disabled were 6,100.01 / 1,848.05 MiB. Enabled semantic-model counts were 6 / 12 per pair versus 62 / 30 disabled, reflecting the two changed markup consumers. [Concurrent summaries](artifact-concurrent-summary.csv) include allocations, heaps, hits and fallbacks; [individual rows](artifact-concurrent.csv) retain branch timings. With eight observations, nearest-rank p95 equals the maximum. Variability is reported, not hidden behind a single best pair.

## Real dotnet watch

The disposable Avalonia/headless fixture built with zero warnings/errors and used the frozen local analyzers. All nine edits actually changed `MemberViewModel.GetName()` in application PID `18476`; the original control, count state and user input survived, and displayed text matched the newly executed method result. The application increments a separate tick state every 10 ms to exercise ordinary rendering. This does not add arbitrary service types to the runtime refresh dependency graph.

| Observed boundary from source write | First edit, ms | Warm median, ms | Warm range, ms |
| --- | ---: | ---: | ---: |
| Native file-change log | 5.41 | 1.23 | 1.04–1.50 |
| Post-delta ClearCache callback | 2,231.23 | 460.19 | 440.54–507.17 |
| Post-delta UpdateApplication callback | 2,231.62 | 460.20 | 440.56–507.18 |
| Updated method result displayed | 2,216.88 | 477.53 | 452.12–514.88 |

The first edit warms EnC; the other eight form the measured sample. These are observed event times, not durations of the corresponding internal phases. Metadata handlers and the UI run independently; the first UI observation arrived before the custom handler, so subtracting these rows is not a valid delta-application measurement. Exact source-generator processing, delta submission and runtime ApplyUpdate duration were not exposed and remain unavailable. The original recorder's detection column is null because this SDK writes `File change: Update`, not `File changed:`. The audit recovers the observed native timestamps from the untouched [raw transcript](member-watch.jsonl) into [watch samples](member-watch-samples.csv) and [summary](member-watch-summary.csv); future recording recognizes both spellings.

This verifies CLR behavior and runtime compatibility, not an enabled/disabled watch speedup. No disabled watch control or Visual Studio measurement was collected. The actual SDK host can also supply compiler services that deliberately force conservative reuse fallback; no cache-hit count inside watch was recorded. GeneratorDriver speedups must not be presented as equivalent watcher/IDE acceleration.

## Verification and commands

| Executed suite | Passed | Failed / skipped | Evidence |
| --- | ---: | ---: | --- |
| Relevant DebugStats, package Roslyn 4.11 | 325 | 0 / 0 | [TRX](verification/debugstats.trx) |
| Detached/member/invalidation tests, SDK Roslyn 5.9 | 112 | 0 / 0 | [TRX](verification/sdk-invalidation.trx), [host identities](verification/sdk-verification.json) |
| Release artifact/member compatibility | 82 | 0 / 0 | [TRX](verification/release.trx) |
| Supported-body / caller-line guard | 8 | 0 / 0 | [TRX](verification/guard.trx) |
| Invalidation report checks | 23 | 0 | [Log](verification/Test-InvalidationReport.ps1.log) |
| Existing report checks | 17 | 0 | [Log](verification/Test-Report.ps1.log) |
| Detached controller checks | 12 | 0 | [Log](verification/Test-ContractRunner.ps1.log) |

These are relevant suites, not a claim that every repository test project ran. DebugStats and the SDK suite both include the old and new 100/500 lifetime cases. Release excludes those duplicate long-running tests; the six instrumentation-specific cancellation cases compile only with STATS. The SDK runner asserts Roslyn 5.9 inside the executing isolated test host, rather than inferring its version from the installed SDK. Package references and original 4.11 test output were not replaced.

Executed test commands (from the repository root):

```powershell
dotnet build src/Akbura.UnitTests/Akbura.UnitTests.csproj -c DebugStats --no-restore -p:UseSharedCompilation=false -nr:false --nologo -v:minimal
dotnet test src/Akbura.UnitTests/Akbura.UnitTests.csproj -c DebugStats --no-build --filter 'FullyQualifiedName~MemberContracts_UnchangedAttributeSyntax|FullyQualifiedName~MemberContracts_ReferencedAndIndependentBodiesReuseWithoutBinding' --logger 'trx;LogFileName=guard.trx' --results-directory bin/SemanticContracts/final -v:minimal -- xUnit.MaxParallelThreads=2
dotnet test src/Akbura.UnitTests/Akbura.UnitTests.csproj -c DebugStats --no-build --filter 'FullyQualifiedName~BlackSilence|FullyQualifiedName~AkburaProjectIndex|FullyQualifiedName~DocumentSyntaxVersion|FullyQualifiedName~GenerationStatistics' --logger 'trx;LogFileName=debugstats.trx' --results-directory bin/SemanticContracts/final -v:minimal -- xUnit.MaxParallelThreads=2
& ./benchmarks/HotReload/Verify-SdkRoslyn.ps1 -OutputDirectory bin/SemanticContracts/final/sdk -TestFilter 'FullyQualifiedName~InvalidationTrace_|FullyQualifiedName~BlackSilenceGenerationStatisticsTests|FullyQualifiedName~DetachedArtifacts_|FullyQualifiedName~MemberContracts_' -ExpectedTestCount 112
dotnet test src/Akbura.UnitTests/Akbura.UnitTests.csproj -c Release --no-restore -p:UseSharedCompilation=false -nr:false --filter '(FullyQualifiedName~DetachedArtifacts_|FullyQualifiedName~MemberContracts_)&FullyQualifiedName!~OnlyNewestStoreAlive&FullyQualifiedName!~MixedLongSession' --logger 'trx;LogFileName=release.trx' --results-directory bin/SemanticContracts/final -v:minimal -- xUnit.MaxParallelThreads=2
& ./benchmarks/HotReload/Test-InvalidationReport.ps1
& ./benchmarks/HotReload/Test-Report.ps1
& ./benchmarks/HotReload/Test-ContractRunner.ps1
```

Executed preparation/measurement commands (use **new** output/result directories to repeat; existing evidence is not overwritten):

```powershell
& ./benchmarks/HotReload/Run.ps1 -Suite artifacts -Prepare -FreshCompiler -ArtifactOptimization @() -OutputDirectory bin/SemanticContracts/final/prepared
& ./benchmarks/HotReload/Start-ContractBenchmarks.ps1 -InputDirectory bin/SemanticContracts/final/prepared -OutputDirectory bin/SemanticContracts/final/measurements -ResultDirectory benchmarks/HotReload/results/2026-10-17-member-contracts
```

The tracked controller issues `artifacts <inputs> <file> <scenario> stats <disabled|enabled>` and `member-memory <inputs> <file> <revisions> <disabled|enabled> 8` against its frozen tool. It then calls `Verify-MemberWatch.ps1 -OutputDirectory <fresh-watch-directory> -ToolDirectory <frozen-tool-directory>`. The latter performs restore/build and `watch <fixture.csproj> <MemberViewModel.cs> MemberFixture`. Journals, per-revision raw output, errors, atomic status and checked resume remain under the ignored run directory. Portable raw compressed records and the exact configuration are archived here.

### Diagnostic and evidence limits

The generator-only harness runs BlackSilence, not the separate Avalonia XAML generator. Its evaluated Gallery compilation therefore has two existing CS0103 `InitializeComponent` errors, and Purity has one. They are retained in the raw current/fresh diagnostic sets, not suppressed. The benchmark does not claim those reconstructed compilations are error-free full application builds. Supported clean unit fixtures check expected semantic results explicitly; a dedicated body-error test verifies that cached BlackSilence sources cannot hide a new C# compiler error. The real watch fixture is a separate clean build.

Before the new compiler binaries were built, the realistic referenced-member fixture reproduced `ChangedDeclarationOutsideSupportedSubset`, with no hits in both projects. [Baseline reproduction](baseline/README.md) uses trace profiling, and [the red regression](verification/member-contracts-before.trx) records seven expected failures against the unrebuilt base binaries. Those are negative controls, not final failing tests and not denominators for the final stats-mode percentages. Historical October 17 artifact-reuse measurements were not overwritten.

No full arbitrary-C# dependency tracking, constructor-body optimization, partial/generic/nested support, intra-component binding incrementality or IDE timing is claimed. Unsupported source/compiler environments continue to rebind. The final implementation expands the actual semantic-work avoidance to realistic referenced members while preserving the existing bounded owner and fallback rules.

## Measurements and verification

The following block is generated from the final paired raw samples. Manual interpretation, commands, ownership results and limits above are maintained separately. Re-run `Summarize-Invalidation.ps1` against the original run directory to update only this block and the machine-readable tables.

<!-- BEGIN GENERATED: invalidation -->
Recorded 484 driver runs; 44 workload/mode groups; 484 fresh-driver parity checks. Each group has two warm-ups and eight measured samples.

| Project / file | Scenario | Binary / profiling | Initial ms | Median ms | Range ms | p95 ms | Components generated / reused | Diagnostics evaluated | Changed texts |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Akbura.FeatureGallery / Counter.akbura | akbura | stats / stats / disabled | 14437.83 | 220.39 | 214.56 - 235.63 | 235.63 | 1 / 27 | 1 | 1 |
| Akbura.FeatureGallery / Counter.akbura | akbura | stats / stats / enabled | 14382.77 | 230.82 | 220.29 - 233.78 | 233.78 | 1 / 27 | 1 | 1 |
| Akbura.FeatureGallery / Counter.akbura | real-method | stats / stats / disabled | 14666.17 | 8479.22 | 8422.81 - 8760.31 | 8760.31 | 28 / 0 | 32 | 0 |
| Akbura.FeatureGallery / Counter.akbura | real-method | stats / stats / enabled | 15182.45 | 24.12 | 21.88 - 32.54 | 32.54 | 0 / 28 | 0 | 0 |
| Akbura.FeatureGallery / Counter.akbura | real-mixed | stats / stats / disabled | 14218.82 | 8279.25 | 8225.95 - 8397.65 | 8397.65 | 28 / 0 | 32 | 1 |
| Akbura.FeatureGallery / Counter.akbura | real-mixed | stats / stats / enabled | 15050.45 | 672.32 | 663.22 - 718.08 | 718.08 | 1 / 27 | 1 | 1 |
| Akbura.FeatureGallery / Counter.akbura | real-multiple | stats / stats / disabled | 13997.46 | 8168.01 | 8092.52 - 8200.21 | 8200.21 | 28 / 0 | 32 | 0 |
| Akbura.FeatureGallery / Counter.akbura | real-multiple | stats / stats / enabled | 14977.34 | 22.54 | 21.79 - 27.36 | 27.36 | 0 / 28 | 0 | 0 |
| Akbura.FeatureGallery / Counter.akbura | real-overload | stats / stats / disabled | 14274.60 | 8111.20 | 8089.16 - 8187.83 | 8187.83 | 28 / 0 | 32 | 0 |
| Akbura.FeatureGallery / Counter.akbura | real-overload | stats / stats / enabled | 14819.68 | 8169.64 | 8137.57 - 8311.41 | 8311.41 | 28 / 0 | 32 | 0 |
| Akbura.FeatureGallery / Counter.akbura | real-property | stats / stats / disabled | 14346.52 | 8107.82 | 8024.02 - 8271.52 | 8271.52 | 28 / 0 | 32 | 0 |
| Akbura.FeatureGallery / Counter.akbura | real-property | stats / stats / enabled | 14282.81 | 23.20 | 20.81 - 25.89 | 25.89 | 0 / 28 | 0 | 0 |
| Akbura.FeatureGallery / Counter.akbura | real-referenced-method | stats / stats / disabled | 14408.16 | 8350.80 | 8234.65 - 8411.89 | 8411.89 | 28 / 0 | 32 | 0 |
| Akbura.FeatureGallery / Counter.akbura | real-referenced-method | stats / stats / enabled | 14992.10 | 22.80 | 21.22 - 25.56 | 25.56 | 0 / 28 | 0 | 0 |
| Akbura.FeatureGallery / Counter.akbura | real-service | stats / stats / disabled | 14426.56 | 8407.63 | 8312.60 - 8506.91 | 8506.91 | 28 / 0 | 32 | 0 |
| Akbura.FeatureGallery / Counter.akbura | real-service | stats / stats / enabled | 14377.69 | 25.33 | 21.74 - 29.27 | 29.27 | 0 / 28 | 0 | 0 |
| Akbura.FeatureGallery / Counter.akbura | real-signature | stats / stats / disabled | 14133.61 | 8139.07 | 8029.95 - 8212.66 | 8212.66 | 28 / 0 | 32 | 0 |
| Akbura.FeatureGallery / Counter.akbura | real-signature | stats / stats / enabled | 14983.78 | 8205.47 | 8131.20 - 8276.89 | 8276.89 | 28 / 0 | 32 | 0 |
| Akbura.FeatureGallery / Counter.akbura | referenced-signature | stats / stats / disabled | 13984.13 | 8155.59 | 8036.51 - 8232.74 | 8232.74 | 28 / 0 | 32 | 0 |
| Akbura.FeatureGallery / Counter.akbura | referenced-signature | stats / stats / enabled | 14102.02 | 8143.88 | 8108.89 - 8233.36 | 8233.36 | 28 / 0 | 32 | 0 |
| Akbura.FeatureGallery / Counter.akbura | unrelated-body | stats / stats / disabled | 14584.05 | 8172.69 | 8124.47 - 8885.71 | 8885.71 | 28 / 0 | 32 | 0 |
| Akbura.FeatureGallery / Counter.akbura | unrelated-body | stats / stats / enabled | 13840.81 | 21.47 | 20.25 - 24.00 | 24.00 | 0 / 28 | 0 | 0 |
| PurityUIDashborad / Sidebar.akbura | akbura | stats / stats / disabled | 5888.42 | 628.86 | 589.67 - 803.35 | 803.35 | 1 / 14 | 1 | 1 |
| PurityUIDashborad / Sidebar.akbura | akbura | stats / stats / enabled | 5648.46 | 615.73 | 593.25 - 875.85 | 875.85 | 1 / 14 | 1 | 1 |
| PurityUIDashborad / Sidebar.akbura | real-method | stats / stats / disabled | 5745.18 | 1920.22 | 1820.44 - 2170.72 | 2170.72 | 15 / 0 | 17 | 0 |
| PurityUIDashborad / Sidebar.akbura | real-method | stats / stats / enabled | 5761.37 | 11.40 | 11.03 - 16.45 | 16.45 | 0 / 15 | 0 | 0 |
| PurityUIDashborad / Sidebar.akbura | real-mixed | stats / stats / disabled | 5680.35 | 1975.00 | 1886.21 - 2136.11 | 2136.11 | 15 / 0 | 17 | 1 |
| PurityUIDashborad / Sidebar.akbura | real-mixed | stats / stats / enabled | 5717.15 | 1121.87 | 1060.27 - 1200.43 | 1200.43 | 1 / 14 | 1 | 1 |
| PurityUIDashborad / Sidebar.akbura | real-multiple | stats / stats / disabled | 5844.54 | 1930.25 | 1856.09 - 2191.11 | 2191.11 | 15 / 0 | 17 | 0 |
| PurityUIDashborad / Sidebar.akbura | real-multiple | stats / stats / enabled | 5640.78 | 11.18 | 10.33 - 12.61 | 12.61 | 0 / 15 | 0 | 0 |
| PurityUIDashborad / Sidebar.akbura | real-overload | stats / stats / disabled | 5495.82 | 1906.03 | 1820.05 - 2106.06 | 2106.06 | 15 / 0 | 17 | 0 |
| PurityUIDashborad / Sidebar.akbura | real-overload | stats / stats / enabled | 5642.80 | 1972.37 | 1882.01 - 2200.08 | 2200.08 | 15 / 0 | 17 | 0 |
| PurityUIDashborad / Sidebar.akbura | real-property | stats / stats / disabled | 5606.29 | 1903.34 | 1816.28 - 2185.81 | 2185.81 | 15 / 0 | 17 | 0 |
| PurityUIDashborad / Sidebar.akbura | real-property | stats / stats / enabled | 5638.96 | 11.29 | 10.25 - 12.59 | 12.59 | 0 / 15 | 0 | 0 |
| PurityUIDashborad / Sidebar.akbura | real-referenced-method | stats / stats / disabled | 5628.88 | 1944.26 | 1858.13 - 2139.09 | 2139.09 | 15 / 0 | 17 | 0 |
| PurityUIDashborad / Sidebar.akbura | real-referenced-method | stats / stats / enabled | 5926.74 | 11.41 | 10.39 - 13.89 | 13.89 | 0 / 15 | 0 | 0 |
| PurityUIDashborad / Sidebar.akbura | real-service | stats / stats / disabled | 5649.29 | 1920.06 | 1816.77 - 2142.81 | 2142.81 | 15 / 0 | 17 | 0 |
| PurityUIDashborad / Sidebar.akbura | real-service | stats / stats / enabled | 5666.44 | 11.10 | 10.68 - 12.42 | 12.42 | 0 / 15 | 0 | 0 |
| PurityUIDashborad / Sidebar.akbura | real-signature | stats / stats / disabled | 5622.05 | 1942.28 | 1852.35 - 2168.14 | 2168.14 | 15 / 0 | 17 | 0 |
| PurityUIDashborad / Sidebar.akbura | real-signature | stats / stats / enabled | 5865.61 | 1948.97 | 1862.27 - 2129.11 | 2129.11 | 15 / 0 | 17 | 0 |
| PurityUIDashborad / Sidebar.akbura | referenced-signature | stats / stats / disabled | 5542.74 | 1962.89 | 1874.13 - 2108.63 | 2108.63 | 15 / 0 | 17 | 0 |
| PurityUIDashborad / Sidebar.akbura | referenced-signature | stats / stats / enabled | 5674.68 | 1963.91 | 1865.05 - 2194.36 | 2194.36 | 15 / 0 | 17 | 0 |
| PurityUIDashborad / Sidebar.akbura | unrelated-body | stats / stats / disabled | 5505.30 | 1904.72 | 1864.51 - 2145.44 | 2145.44 | 15 / 0 | 17 | 0 |
| PurityUIDashborad / Sidebar.akbura | unrelated-body | stats / stats / enabled | 5677.95 | 11.06 | 9.95 - 15.72 | 15.72 | 0 / 15 | 0 | 0 |

All stage durations and allocations are inclusive. Do not sum nested or parallel stages. With n=8, nearest-rank p95 equals the maximum.
<!-- END GENERATED: invalidation -->
