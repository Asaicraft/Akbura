# Per-document C# semantic dependencies

Status: relevant-suite verification, paired measurements and live watch verification completed on October 19, 2026 at 01:20 UTC. The narrow selective-reuse proof passes correctness and ownership checks, but the measured real-project declaration edits do not demonstrate a material end-to-end speedup. Most original components still use conservative fallback. This is not a claim that the broader performance objective is achieved. Starting branch: `feature/improve-hot-reload`; clean starting commit: `320605884d4cc59f53da2e67c499661a45f69c5c`. Implementation changes are uncommitted. Previous archives remain unchanged.

## Coverage audit before enabling selective reuse

This audit distinguishes queries used by generation/diagnostics from navigation and completion APIs. It defines the initial implementation boundary, not a promise to support every C# or Akbura construct. Every active path outside the implemented boundary must mark the document's dependency proof incomplete. Existing implementation-only proofs remain independent.

| Semantic operation | Existing implementation entry points | Required dependency / initial classification |
| --- | --- | --- |
| Markup C# expressions | `BindingSession.BindExpression`; `CSharpProbeBinder.BindExpression`, `BindReturnExpression`, `BindExpressionTree` | Record existing symbol/type/operation results, receivers, arguments, conversions and whole receiver candidate spaces. Support only classified expression/operation forms. |
| Binding expressions/paths | `MarkupBinder.BindExpression`; `CSharpProbeBinder.BindReturnExpression`; `BindingWriter` | Same receiver proof plus target type. Unknown dynamic/path metadata is incomplete; source-control metadata paths remain conservative. |
| Statements and inline C# | `CSharpProbeBinder.BindStatement`, `BindStatementTree`, `TryBindComponentMethodStatement` | Initially incomplete: local-function/projection/capture scopes are not a closed lookup proof. |
| Component methods | `CSharpProbeContext.GetMethodSignature`, `GetMethodBody`; component member semantic models | Initially incomplete, including exported inferred results. No intra-component binding reuse is introduced. |
| State declarations/initializers | `DeclarationSymbolTable`; `AkburaSemanticModel.CreateStateProbeFieldsBefore`; `ComponentMemberPlanner` | Initially incomplete. Distinguish public declaration dependency proof from implementation proof; never infer surface stability from emitted text. |
| Hooks and hook arguments | `UseHookBinder`; `CSharpProbeBinder.UseHooks`, `UseHookImports` | Initially incomplete: discovery, hook attributes and argument rewriting need broader proof. |
| Injected services | `DeclarationSymbolTable`; `ComponentMemberPlanner` | Initially incomplete for generated injection declarations. Ordinary explicitly typed service calls in expressions use the receiver proof. |
| Component parameters | `DeclarationSymbolTable`; `CreateAkburaComponentParameters`; `ObservableListParameterShape` | Initially incomplete for exported parameter metadata. Existing document-surface propagation remains active. |
| Commands/event handlers | `DeclarationSymbolTable`; `ComponentPlanner.Commands`; `MarkupBinder` | Initially incomplete for command declarations and handler/lambda scopes. Metadata-only ordinary property paths can be classified separately. |
| Property/attached-property assignment | `ResolveMarkupProperty`, `TryResolveMarkupAttachedProperty`; `MarkupPropertyMetadataReader`; `MarkupBinder.PropertyAssignments` | Metadata-only owner/type paths are immutable under the supported source-change universe. Original-source control/attached metadata and unknown companion-owner paths are incomplete. |
| Conversions | `AkburaConversions.ClassifyConversion`; probe `GetConversion`; planners' conversion checks | Record source/target types and selected conversion operators from existing operations. Operator/conversion declaration edits reject the selective change set. Unclassified user-source destination paths are incomplete. |
| Generic inference | Roslyn probes; `UseHookBinder`; `OverloadResolver.ResolveMethodGroup` | Initially incomplete for generic/source inference contexts. Explicit closed metadata type arguments can supply broader type facts. |
| Overload resolution | Roslyn probe binding; `OverloadResolver.ResolveMethodGroup` | Capture selected/error candidates and conservatively represent the full receiver/name candidate universe, including empty sets. Member additions/removals invalidate that receiver's consumers. |
| Extension resolution | Probe scope/usings; `Binder_Lookup` in Roslyn | No selective acceptance of extension declarations, extension blocks or changed imports. Unknown active extension contexts are incomplete; never treat `CandidateSymbols` as all candidates. |
| Namespaces/imports | `CSharpProbeContext.DefaultUsings`; `GetCSharpUsingDirectives`; `CSharpProbeBinder.Scope` | Require unchanged namespace/type universe and imports. Active static/alias imports are initially incomplete, even in a document without expression probes; unresolved/unknown scopes are incomplete. |
| Markup extensions | `AkburaSemanticModel.MarkupOperations`; `MarkupExtensionResultPlan` | Initially incomplete, including `ProvideValue` and conversion/discovery metadata. |
| Templates/conditionals | `MarkupTemplateContentResolver`; `ConditionalTemplateRoots`; probe conditional helpers | Initially incomplete: implicit destination/template lookup paths are not silently accepted. |
| Foreach/captured locals | `CSharpProbeBinder.MarkupForeach`; `MarkupBinder.Foreach`; `ComponentPlanner.Foreach` | Initially incomplete, including enumerator and captured-local semantics. |
| Component inheritance/base types | `AkburaComponentTypeResolver.Resolve`; probe scaffolding; `ResolveMarkupComponent` | Changed source type headers/base/interface/generic declarations reject the change set. Track receiver base/interface identities; original-source control metadata remains incomplete. |
| AKCSS semantic queries | `AkcssStyleBinder`; `AkcssActivatorPlanner`; `AkcssExpressionRewriters`; metadata planners | Initially incomplete for selective C# declaration reuse. Whole-body dependency edges propagate the broader proof; imported immutable metadata remains protected by exact references. |
| Generated/implicit declarations | `AkburaProjectIndex`; `AkburaComponentProbeCompilationBuilder`; `ComponentWriter`; project service writer | Preserve generated-name collision checks and syntax-surface identities. Exported semantic proof is separate; unknown inferred surfaces propagate conservative invalidation. |
| Diagnostic-only queries | `BlackSilenceDocumentBatch.CollectDiagnostics`; `GetSemanticDiagnostics`; `CSharpProbeContext.GetDiagnostics` | Separate detached diagnostic facts/availability/mapping checks. Incomplete diagnostic proof always recomputes. Compiler diagnostics always belong to the current compilation. |
| Navigation/reference/completion APIs | `AkburaSemanticModel.CSharpReferences`, completion/code-action partials | Not a generation dependency recorder. These can create additional probes and omit negative/implicit dependencies; do not call them as an extra collection pass. |

The audit found queries outside `CSharpProbeBinder`, notably property metadata readers, content insertion, conditional/template inference, hooks, AKCSS and code-generation planners. Instrumenting probe positives alone would therefore be insufficient. The initial document allowlist and explicit incomplete paths are part of the proof, not optional diagnostics.

## Implemented proof and ownership

The selective change set preserves exact compiler options, ordered references, parse/encoding/path/tree identities, imports, type identities and type headers. It allows only explicitly classified existing-class method/property declaration changes, additions and removals. Constants, attributes, operators, conversions, extensions, nested/generic/partial declarations and unknown changes keep project-wide fallback. Type additions/removals also retain fallback. Qualified source declaration inventory checks reject duplicate/partial identities across files, including escaped identifiers. Framework/runtime namespaces and implicit generated-declaration collisions remain conservative exclusions.

Per-document scalar facts identify assemblies and metadata-qualified types, selected member signatures, receiver candidate spaces, negative lookups, argument/conversion facts and completeness reasons. Whole-type candidate dependencies deliberately over-invalidate a receiver when any permitted member contract changes. Simple names are not universal symbol identities. Any syntactic name expansion is only a conservative addition of qualified candidates, never the independence proof. Candidate snapshots are evidence and completeness inputs, not a standalone successful-symbol or signature-hash validator. Validation uses the closed change universe plus every explicit receiver/type contract, including nested static receivers absent from operation children. It never executes a fresh generator or builds replacement semantic models to prove a hit.

Body and exported-surface dependency facts must remain distinct. A rebound component with a proven unchanged declaration surface need not invalidate a surface-only consumer. Unknown inferred public surfaces and whole-body AKCSS/global edges require the broader dependency proof. Fresh output equality is an oracle, not production validation.

```text
Current compilation/model -> binding-session collector -> current symbols/operations
                                      |
                                 detach values
                                      v
BoundedArtifactStore -> ONE latest publication
                          +-- current source/diagnostic artifacts
                          +-- current scalar semantic dependency facts
                          +-- exact declaration/source contracts
                          +-- weak current input identities
```

No collector, symbol, operation, syntax/model/version object or previous-snapshot link enters the persistent publication. Facts are deduplicated, current-key-only and atomically published with their artifacts. Missing/incomplete facts cause rebinding. A fixed exported-surface flag requires a complete bare-component proof; an incomplete child cannot supply it. A matching original-source partial component declaration and original-source component metadata close the proof rather than assuming the markup describes the entire public surface. Diagnostic capture occurs after diagnostic evaluation, source capture after writing, and compatible same-compilation semantic-state imports carry their scalar dependency seed. Existing semantic-state compatibility is unchanged. Canceled/failed attempts cannot publish partial facts, and reservation-version ordering still governs concurrent publication.

## Executed verification

The new regression initially failed against the old compiler with `Independent Orders was rebound: ChangedDeclarationOutsideSupportedSubset`. The untouched [negative-control TRX](verification/semantic-dependencies-before.trx) records that expected failure, not a failure of the final implementation. The final verifier then ran the relevant suites against the hardened sources, including template/exported-surface guards, collector ownership checks, unsupported-universe checks and DebugStructural cases.

The [pre-change baseline](baseline/README.md) contains 44 driver revisions and 44 fresh-driver comparisons. For `real-signature`, enabled old-code medians were 9,434.30 ms / 28 components rebound in Gallery and 2,303.85 ms / 15 rebound in Purity. These are new October 18 baseline observations, not recycled October 17 body timings. They are historical evidence rather than the primary speedup comparison: final paired comparisons use enabled/disabled controls from the same new binary and identical scenario inputs.

The new tests use real incremental driver revisions and fresh source/diagnostic oracles, including selected overload symbol and return-type assertions. They cover declarations, additions/removals, negative/ambiguous lookup, nested static hiding, qualified identities, unknown scopes, propagation through surface edges, cancellation and concurrent branches. All 55 new package-host cases passed, including the two mixed 100/500-revision sessions. Those sessions alternate signatures, additions/removals, body edits, markup, globals and reversions. The recursive graph audit explicitly forbids collectors and binding results, and weak observations include collectors.

| Executed check | Passed / failed | Evidence and interpretation |
| --- | --- | --- |
| Final relevant DebugStats suites, package Roslyn 4.11 | 380 / 0 | [TRX](verification/debugstats/tests.trx); includes all 55 new cases and both new long lifetime cases |
| Final relevant isolated SDK-host suites, Roslyn 5.9 | 167 / 0 | [TRX](verification/sdk/sdk-invalidation.trx), [executing host identities](verification/sdk/sdk-verification.json); includes the new 100/500 sessions |
| Final relevant Release suite | 122 / 0 | [TRX](verification/release/tests.trx); selected non-STATS cases, excluding duplicate long sessions |
| General report regression checks | 17 / 0 | [Output](verification/Test-Report/output.log) |
| Invalidation report regression checks | 23 / 0 | [Output](verification/Test-InvalidationReport/output.log) |
| Frozen controller regression checks | 12 / 0 | [Output](verification/Test-ContractRunner/output.log) |

These suites overlap and must not be summed as unique tests. They cover the relevant generator, invalidation and ownership paths, not every test project in the repository. Earlier targeted/intermediate TRXs remain archived separately and are not counted again. [Final verification metadata](verification/results.json) records the source fingerprint `990CAA3D19E3D76A15C90AE6CBB830D9DCE6222F53F3CC44D53B234F52042136`; [the source manifest](verification/source-manifest.json) identifies the exact implementation files used. Subsequent edits to this report do not change that implementation fingerprint.

### Autonomous verification

Run `Verify-SemanticDependencies.ps1` from the repository root with a new output directory. It logs console output/stderr, UTC heartbeats and per-step success markers, validates the implementation fingerprint on resume and around each step, waits for other builds/tests, and then hands measurements to the existing frozen-binary controller. Commands and a live log example are in the [harness guide](../../README.md#semantic-dependency-verification). The completed local run directory is `bin/SemanticDependencies/final-validated`; its `status.json` and `journal.log` expose progress/failures without keeping a chat polling. Failed attempts were preserved. Power-loss and foreign-build interruptions were resumed from validated completed groups; interrupted groups are not accepted timing samples. A local ignored supervisor retried only foreign-build interruptions and waited for a quiet interval; it did not change the frozen implementation or weaken sample validation.

### Benchmark scope disclosure

All original Gallery/Purity evaluated files are retained. `semantic-*` scenarios add `SemanticOrders` and `SemanticSettings` as controlled independent consumers; `semantic-multiple` adds one further Profile consumer. The changed binding is inserted into actual Counter/Sidebar markup. Most complex existing application documents fall outside the initial complete subset and may still rebind. Added fixtures are explicitly identified in raw provenance and must not be presented as pre-existing application components or evidence that every original component is selectively supported.

The completed matrix contains nine declaration scenarios plus ordinary body, previous-signature and markup controls, paired by project and optimization mode: 48 timing groups, 528 driver revisions and 528 fresh-driver source/diagnostic parity checks. Each group contains an initial revision, two warm-ups and eight measured updates. Four additional memory/concurrent groups bring the main controller total to 52. The separate profiling-off control adds eight timing groups, 88 revisions and 88 successful fresh comparisons, using the same binary with measurement sessions disabled. Absent counters there mean unmeasured work, not zero work. Preflight groups are excluded from these totals.

The measured host is Windows 11 build 26200, .NET runtime 10.0.12, SDK 10.0.401, Roslyn 5.9 and 12 reported logical processors. Purity inputs are pinned to tutorial revision `f752e29d7dc791c7fcdac6b740aad7a52552738e`. `invalidation-environment.json` is inherited preparation-time metadata and still names the older preparation revision; it is not final-implementation provenance. Use `run.json`, per-run `invalidation-provenance` trace rows and `verification/source-manifest.json` for the final revision, actual loaded binaries and implementation hashes.

## Performance findings

The full per-scenario before/after timings, component counts, ranges and p95 values are generated below. "Disabled" is the same new binary with `AkburaCrossCompilationReuse=false`, not the old implementation. [Raw samples](invalidation-samples.csv), [stage observations](invalidation-stages.csv) and [compressed traces](invalidation-traces.jsonl.gz) preserve CPU, GC, allocation, lookup and per-document decision evidence. With only eight samples and sequential groups, small timing differences are observations, not established improvements or regressions.

| Observation | Gallery | Purity |
| --- | --- | --- |
| Controlled method signature, disabled -> enabled median | 8,611.15 -> 8,278.09 ms | 2,074.06 -> 2,059.75 ms |
| Components rebound / reused, disabled -> enabled | 30 / 0 -> 28 / 2 | 17 / 0 -> 15 / 2 |
| Original signature control, disabled -> enabled median | 8,398.85 -> 8,632.90 ms | 2,005.34 -> 1,998.39 ms |
| Original signature control, rebound / reused with reuse enabled | 28 / 0 | 15 / 0 |
| Existing safe body edit, disabled -> enabled median | 8,375.30 -> 23.61 ms | 2,013.63 -> 11.57 ms |
| Ordinary markup edit, disabled -> enabled median | 224.86 -> 231.88 ms | 662.29 -> 648.07 ms |

The two reused components in the controlled declaration scenarios are the explicitly added independent consumers, not original application components. They skip semantic binding and reuse diagnostics; the source and diagnostic fresh oracles match. Original complex components, including the edited Counter/Sidebar, remain outside the complete dependency subset and rebind. A document classified incomplete may also be semantically affected: the `DocumentsAffected` counter is not an exhaustive count of all rebound documents. Whole-type receiver dependencies also deliberately rebind more than individual-member tracking would.

For the controlled signature scenario, the enabled run examines 33 / 17 document proofs in Gallery / Purity, proves two independent in each, and classifies 31 / 15 as incomplete. Source artifact hits are two in each; detached diagnostic hits are three / four. The global-import scenario falls back across the project, as intended. Overload, negative-lookup, unrelated-type and mixed-edit scenarios also retain the two independent controlled consumers while rebinding unsupported original consumers. This proves selective behavior within the documented subset; it does not satisfy a claim that most real application components now avoid rebinding.

| Controlled signature metric, median | Gallery disabled -> enabled | Purity disabled -> enabled |
| --- | --- | --- |
| Semantic models created | 33 -> 31 | 17 -> 15 |
| Probe compilations | 2,898 -> 2,890 | 873 -> 869 |
| Probe diagnostic queries | 2,921 -> 2,909 | 876 -> 872 |
| Diagnostic documents evaluated | 34 -> 31 | 19 -> 15 |
| Artifact validation fallbacks | 0 -> 33 | 0 -> 15 |
| Process CPU | 9,273.44 -> 8,914.06 ms | 3,093.75 -> 3,070.31 ms |
| Process allocations | 3,016,109,848 -> 3,046,008,016 bytes | 958,193,088 -> 968,205,308 bytes |
| Heap after GC, driver/workload owners still alive | 429,953,212 -> 478,983,096 bytes | 201,568,008 -> 216,797,612 bytes |

Fallback counts are zero in disabled mode because detached validation is bypassed, not because all documents were independently proven. Allocations rise by about 1% in the controlled signature runs, despite two fewer bound components. Heap observations here include live workload ownership and must not be interpreted as retained-cache-only sizes.

Measured capture is small relative to the remaining binding work. For enabled controlled signature edits, inclusive median `SemanticDependencyCapture` is 1.1012 / 0.3159 ms, `DependencySetDetachment` is 0.3347 / 0.1722 ms, `DocumentDependencyValidation` is 0.0241 / 0.0194 ms, and overall `ArtifactValidation` is 15.5658 / 6.6806 ms in Gallery / Purity. Semantic preparation runs once per update; inclusive semantic binding still costs 7,604.29 / 1,942.93 ms. Stage durations overlap and must not be summed. The remaining bottleneck is the large unsupported binding workload and its thousands of probes, not the scalar dependency-set comparison.

### Profiling and markup controls

The [profiling-off results](profiling-off/README.md) contain the eight paired groups. Controlled signature medians with profiling off are 8,479.34 -> 8,545.54 ms in Gallery and 2,005.80 -> 2,078.78 ms in Purity: enabled is 0.78% / 3.64% slower in this separate control. These results do not corroborate a material signature-edit speedup. Profiling-on versus off median differences across the eight comparable cells range from -3.13% to +4.29%; this noisy sequential sample does not isolate a precise instrumentation penalty.

With profiling off, ordinary markup medians are 225.46 -> 224.90 ms in Gallery and 635.05 -> 653.90 ms in Purity. No large markup regression is observed in this run, but the Purity spread and sample count do not justify a strict no-regression guarantee. The very large body-only improvement is the existing implementation-body proof, not a new declaration-tracking gain. It must not be averaged with the new declaration cases to manufacture an overall speedup.

## Ownership and concurrent generation

Both new mixed-session tests passed at 100 and 500 revisions in the final package and SDK-host runs. Each revision checks fresh source/diagnostic parity and current-only bounded facts. The package-host cases took 1 min 54.88 s and 10 min 41.46 s respectively. Recursive strong-object audits and weak-reference collection checks passed after legitimate owners were released. All 13 cancellation-boundary cases and the independent signature-branch case passed; canceled work did not replace the published artifact graph.

The real-project [memory results](artifact-memory-results.json), [samples](artifact-memory-samples.csv) and [concurrent summary](artifact-concurrent-summary.csv) are separate evidence. Gallery ran 100 revisions per mode; Purity ran one per mode plus eight two-branch fork samples. The small Purity revision count is not a 100/500-revision real-project claim. There were 270 fresh comparisons in these four groups (117 per Gallery mode, 18 per Purity mode), including the fork branches. Every observed obsolete compilation, model, project context, probe context, probe compilation and dependency collector was dead after releasing drivers. Disabled mode retained zero detached snapshots; enabled retained exactly one current detached snapshot per owner.

| Workload / mode | Post-GC managed heap after driver release | Current artifact entries / weak identity slots | Old compiler objects alive |
| --- | --- | --- | --- |
| Gallery / disabled | 33,713,632 bytes | 0 / 0 | 0 |
| Gallery / enabled | 56,632,712 bytes | 35 / 193 | 0 |
| Purity / disabled | 27,573,832 bytes | 0 / 0 | 0 |
| Purity / enabled | 32,204,352 bytes | 17 / 194 | 0 |

These are whole managed heaps at the observation point, not direct measurements of cache size. Keeping the bounded detached publication still has a memory cost. Zero observed old compiler targets does not mean zero retained data or zero working set. Concurrent paired-driver median elapsed times were 9,784.18 -> 9,574.29 ms in Gallery and 3,050.81 -> 2,645.92 ms in Purity; the mixed branch workloads and large ranges do not establish general concurrent IDE speedups.

## Live Hot Reload

The [live result](member-result.json) and [raw watch log](member-watch.jsonl) record nine successful declaration updates: one first edit and eight measured edits. Every edit adds an ordinary private method and redirects `GetName`; earlier added methods remain to avoid a deletion rude edit. All updates retained application PID 11608, the original compatible control, state and input, and verified the updated member behavior and displayed value. No silent restart occurred.

Measured write-to-observed-result latency was median 492.21 ms, range 469.78-669.34 ms, with nearest-rank p95 669.34 ms. This is whole-watch latency, not GeneratorDriver processing time. Detection logs and post-delta callbacks were observed; exact generator processing and delta submission boundaries were unavailable and remain null. There is no paired disabled watch run, no Visual Studio/Rider measurement and no claim that a minute-long IDE delay has been reproduced or eliminated. The fixture advances a separate tick state to observe service changes; it does not demonstrate a general runtime refresh graph for arbitrary C# services.

## Reproduction and remaining limits

The final autonomous invocation was:

```powershell
pwsh -NoProfile -File benchmarks/HotReload/Verify-SemanticDependencies.ps1 `
  -InputDirectory bin/SemanticContracts/final/prepared `
  -OutputDirectory bin/SemanticDependencies/final-validated `
  -ResultDirectory benchmarks/HotReload/results/2026-10-18-semantic-dependencies `
  -Resume
```

Use a new output/result directory when reproducing rather than overwriting this archive. Prepare evaluated inputs as described in the harness guide. Exact executed test/build/report commands are archived in [verification/commands.json](verification/commands.json), copied from successful step markers; suite filters deliberately select relevant generator/ownership tests. Machine-local result paths identify the original run and should be replaced on another checkout. `run.json` and `profiling-off/run.json` record the frozen binary hashes, scenario selections, evaluated inputs and modes. The live runner uses `Verify-MemberWatch.ps1 -DeclarationChanges`.

Implementation changes are limited to detached contracts/reuse, binder-side capture and statistics, scalar semantic-state transfer, the incremental/ownership tests, the existing benchmark harness, and contributor documentation. New entry points are `DetachedCSharpDeclarationContract.Selective.cs`, `DetachedArtifactReuse.SemanticDependencies.cs`, `DocumentSemanticDependencyCollector.cs`, `DetachedCSharpDependencySet.cs`, the two `SemanticDependencies` test partials, and `Verify-SemanticDependencies.ps1`; the source manifest lists the measured files and hashes.

Remaining work is broader semantic-path coverage, not a relaxation of the proof. State/parameter/command/hook declarations, templates and control flow, unknown component/attached metadata, generics/extensions, source partial components and unsupported declaration-universe edits retain fallback. Constants, attributes, base/interface changes, type additions/removals and changed imports are not localized by this initial change set. Unknown compiler environments also fall back. No intra-component binding redesign, CLR metadata redesign or runtime scheduling changes were made. The results support the narrow correctness/ownership foundation and real selective reuse of two independent controlled documents; they do not demonstrate the requested broad real-project declaration-edit acceleration.

## Roslyn observations

Roslyn's [SymbolInfo implementation](https://github.com/dotnet/roslyn/blob/7ed110c0f961a5f8800139bc2df914ffb0099aca/src/Compilers/Core/Portable/Compilation/SymbolInfo.cs) describes `CandidateSymbols` as error-recovery candidates, not the complete successful overload universe. Its [lookup implementation](https://github.com/dotnet/roslyn/blob/7ed110c0f961a5f8800139bc2df914ffb0099aca/src/Compilers/CSharp/Portable/Binder/Binder_Lookup.cs) treats member, base/interface and extension scopes separately. Recording a successful symbol alone cannot rule out new competing members. These observations motivate exact change classification and broader closed candidate dependencies, with fallback for unclassified scopes.

<!-- BEGIN GENERATED: invalidation -->
Recorded 528 driver runs; 48 workload/mode groups; 528 fresh-driver parity checks. Each group has two warm-ups and eight measured samples.

| Project / file | Scenario | Binary / profiling | Initial ms | Median ms | Range ms | p95 ms | Components generated / reused | Diagnostics evaluated | Changed texts |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Akbura.FeatureGallery / Counter.akbura | akbura | stats / stats / disabled | 14699.15 | 224.86 | 220.97 - 246.19 | 246.19 | 1 / 27 | 1 | 1 |
| Akbura.FeatureGallery / Counter.akbura | akbura | stats / stats / enabled | 15160.82 | 231.88 | 228.52 - 259.78 | 259.78 | 1 / 27 | 1 | 1 |
| Akbura.FeatureGallery / Counter.akbura | real-referenced-method | stats / stats / disabled | 14505.07 | 8375.30 | 8276.87 - 8491.52 | 8491.52 | 28 / 0 | 32 | 0 |
| Akbura.FeatureGallery / Counter.akbura | real-referenced-method | stats / stats / enabled | 15307.37 | 23.61 | 21.44 - 26.68 | 26.68 | 0 / 28 | 0 | 0 |
| Akbura.FeatureGallery / Counter.akbura | real-signature | stats / stats / disabled | 15022.41 | 8398.85 | 8357.74 - 8472.65 | 8472.65 | 28 / 0 | 32 | 0 |
| Akbura.FeatureGallery / Counter.akbura | real-signature | stats / stats / enabled | 15356.71 | 8632.90 | 8591.87 - 8747.87 | 8747.87 | 28 / 0 | 31 | 0 |
| Akbura.FeatureGallery / Counter.akbura | semantic-add-overload | stats / stats / disabled | 14862.00 | 8200.39 | 8114.90 - 8285.43 | 8285.43 | 30 / 0 | 34 | 0 |
| Akbura.FeatureGallery / Counter.akbura | semantic-add-overload | stats / stats / enabled | 14510.52 | 8296.01 | 8270.83 - 8443.72 | 8443.72 | 28 / 2 | 31 | 0 |
| Akbura.FeatureGallery / Counter.akbura | semantic-global | stats / stats / disabled | 14942.32 | 8537.96 | 8441.90 - 8626.28 | 8626.28 | 30 / 0 | 34 | 35 |
| Akbura.FeatureGallery / Counter.akbura | semantic-global | stats / stats / enabled | 14561.56 | 8508.10 | 8369.70 - 8607.18 | 8607.18 | 30 / 0 | 34 | 35 |
| Akbura.FeatureGallery / Counter.akbura | semantic-mixed | stats / stats / disabled | 15200.13 | 8486.93 | 8464.33 - 8587.15 | 8587.15 | 30 / 0 | 34 | 1 |
| Akbura.FeatureGallery / Counter.akbura | semantic-mixed | stats / stats / enabled | 15131.12 | 8581.93 | 8511.72 - 9905.67 | 9905.67 | 28 / 2 | 31 | 1 |
| Akbura.FeatureGallery / Counter.akbura | semantic-multiple | stats / stats / disabled | 14699.52 | 8457.92 | 8425.52 - 8554.33 | 8554.33 | 31 / 0 | 35 | 0 |
| Akbura.FeatureGallery / Counter.akbura | semantic-multiple | stats / stats / enabled | 15056.95 | 8575.45 | 8495.88 - 8653.28 | 8653.28 | 29 / 2 | 32 | 0 |
| Akbura.FeatureGallery / Counter.akbura | semantic-negative | stats / stats / disabled | 14527.82 | 8536.28 | 8210.80 - 9228.96 | 9228.96 | 30 / 0 | 34 | 1 |
| Akbura.FeatureGallery / Counter.akbura | semantic-negative | stats / stats / enabled | 15199.78 | 8642.07 | 8584.01 - 8697.04 | 8697.04 | 28 / 2 | 31 | 1 |
| Akbura.FeatureGallery / Counter.akbura | semantic-property | stats / stats / disabled | 16918.90 | 9785.46 | 9516.47 - 15004.48 | 15004.48 | 30 / 0 | 34 | 0 |
| Akbura.FeatureGallery / Counter.akbura | semantic-property | stats / stats / enabled | 16687.98 | 10229.84 | 9977.03 - 12933.14 | 12933.14 | 28 / 2 | 31 | 0 |
| Akbura.FeatureGallery / Counter.akbura | semantic-remove-overload | stats / stats / disabled | 14520.68 | 8217.16 | 8125.85 - 8334.46 | 8334.46 | 30 / 0 | 34 | 0 |
| Akbura.FeatureGallery / Counter.akbura | semantic-remove-overload | stats / stats / enabled | 14866.28 | 8272.57 | 8157.10 - 8405.43 | 8405.43 | 28 / 2 | 31 | 0 |
| Akbura.FeatureGallery / Counter.akbura | semantic-signature | stats / stats / disabled | 14481.36 | 8611.15 | 8471.41 - 9593.21 | 9593.21 | 30 / 0 | 34 | 0 |
| Akbura.FeatureGallery / Counter.akbura | semantic-signature | stats / stats / enabled | 15164.29 | 8278.09 | 8208.50 - 8408.38 | 8408.38 | 28 / 2 | 31 | 0 |
| Akbura.FeatureGallery / Counter.akbura | semantic-unrelated | stats / stats / disabled | 14845.30 | 8505.14 | 8410.70 - 8612.82 | 8612.82 | 30 / 0 | 34 | 0 |
| Akbura.FeatureGallery / Counter.akbura | semantic-unrelated | stats / stats / enabled | 15196.01 | 8544.84 | 8499.32 - 8684.14 | 8684.14 | 28 / 2 | 31 | 0 |
| PurityUIDashborad / Sidebar.akbura | akbura | stats / stats / disabled | 5794.77 | 662.29 | 619.93 - 860.47 | 860.47 | 1 / 14 | 1 | 1 |
| PurityUIDashborad / Sidebar.akbura | akbura | stats / stats / enabled | 5872.97 | 648.07 | 623.68 - 841.12 | 841.12 | 1 / 14 | 1 | 1 |
| PurityUIDashborad / Sidebar.akbura | real-referenced-method | stats / stats / disabled | 5954.66 | 2013.63 | 1915.64 - 2189.94 | 2189.94 | 15 / 0 | 17 | 0 |
| PurityUIDashborad / Sidebar.akbura | real-referenced-method | stats / stats / enabled | 5927.51 | 11.57 | 10.88 - 13.68 | 13.68 | 0 / 15 | 0 | 0 |
| PurityUIDashborad / Sidebar.akbura | real-signature | stats / stats / disabled | 5860.45 | 2005.34 | 1919.17 - 2237.63 | 2237.63 | 15 / 0 | 17 | 0 |
| PurityUIDashborad / Sidebar.akbura | real-signature | stats / stats / enabled | 5999.19 | 1998.39 | 1916.16 - 2314.37 | 2314.37 | 15 / 0 | 15 | 0 |
| PurityUIDashborad / Sidebar.akbura | semantic-add-overload | stats / stats / disabled | 5944.79 | 2011.64 | 1928.21 - 2196.06 | 2196.06 | 17 / 0 | 19 | 0 |
| PurityUIDashborad / Sidebar.akbura | semantic-add-overload | stats / stats / enabled | 5966.33 | 2043.83 | 1935.77 - 2277.07 | 2277.07 | 15 / 2 | 15 | 0 |
| PurityUIDashborad / Sidebar.akbura | semantic-global | stats / stats / disabled | 5840.17 | 2009.72 | 1908.45 - 2205.91 | 2205.91 | 17 / 0 | 19 | 17 |
| PurityUIDashborad / Sidebar.akbura | semantic-global | stats / stats / enabled | 5888.37 | 2053.33 | 1973.86 - 2274.25 | 2274.25 | 17 / 0 | 19 | 17 |
| PurityUIDashborad / Sidebar.akbura | semantic-mixed | stats / stats / disabled | 5900.93 | 2086.26 | 1945.71 - 2290.45 | 2290.45 | 17 / 0 | 19 | 1 |
| PurityUIDashborad / Sidebar.akbura | semantic-mixed | stats / stats / enabled | 6352.69 | 2093.35 | 2026.07 - 2241.25 | 2241.25 | 15 / 2 | 15 | 1 |
| PurityUIDashborad / Sidebar.akbura | semantic-multiple | stats / stats / disabled | 5836.66 | 2022.92 | 1931.01 - 2217.49 | 2217.49 | 18 / 0 | 20 | 0 |
| PurityUIDashborad / Sidebar.akbura | semantic-multiple | stats / stats / enabled | 5986.92 | 2074.94 | 1986.24 - 2219.21 | 2219.21 | 16 / 2 | 16 | 0 |
| PurityUIDashborad / Sidebar.akbura | semantic-negative | stats / stats / disabled | 5781.33 | 2033.90 | 1935.63 - 2193.92 | 2193.92 | 17 / 0 | 19 | 1 |
| PurityUIDashborad / Sidebar.akbura | semantic-negative | stats / stats / enabled | 5951.49 | 2044.74 | 1950.38 - 2297.61 | 2297.61 | 15 / 2 | 15 | 1 |
| PurityUIDashborad / Sidebar.akbura | semantic-property | stats / stats / disabled | 5907.28 | 2078.68 | 1956.25 - 2325.47 | 2325.47 | 17 / 0 | 19 | 0 |
| PurityUIDashborad / Sidebar.akbura | semantic-property | stats / stats / enabled | 6327.09 | 2068.82 | 1984.08 - 2244.68 | 2244.68 | 15 / 2 | 15 | 0 |
| PurityUIDashborad / Sidebar.akbura | semantic-remove-overload | stats / stats / disabled | 5929.85 | 2059.31 | 1957.00 - 2291.12 | 2291.12 | 17 / 0 | 19 | 0 |
| PurityUIDashborad / Sidebar.akbura | semantic-remove-overload | stats / stats / enabled | 5919.11 | 2052.33 | 1914.23 - 2280.50 | 2280.50 | 15 / 2 | 15 | 0 |
| PurityUIDashborad / Sidebar.akbura | semantic-signature | stats / stats / disabled | 5871.64 | 2074.06 | 1968.99 - 2239.91 | 2239.91 | 17 / 0 | 19 | 0 |
| PurityUIDashborad / Sidebar.akbura | semantic-signature | stats / stats / enabled | 5937.81 | 2059.75 | 1953.37 - 2295.91 | 2295.91 | 15 / 2 | 15 | 0 |
| PurityUIDashborad / Sidebar.akbura | semantic-unrelated | stats / stats / disabled | 5716.97 | 2018.88 | 1919.69 - 2188.35 | 2188.35 | 17 / 0 | 19 | 0 |
| PurityUIDashborad / Sidebar.akbura | semantic-unrelated | stats / stats / enabled | 5926.90 | 2060.96 | 1958.83 - 2208.09 | 2208.09 | 15 / 2 | 15 | 0 |

All stage durations and allocations are inclusive. Do not sum nested or parallel stages. With n=8, nearest-rank p95 equals the maximum.
<!-- END GENERATED: invalidation -->
