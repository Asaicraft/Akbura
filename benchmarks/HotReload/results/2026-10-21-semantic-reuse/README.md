# Compilation-bound cross-revision semantic result reuse

Status: **restricted opt-in experiment; measured, reviewed and disabled by default**. Successful supported expressions genuinely skip their first redundant C# binding. The combined variant improves some real GeneratorDriver workloads, but this is not a general end-to-end Hot Reload speedup. No default feature is enabled.

## Implementation and reproduction

Starting commit: `f2c29caef38fd8f08b1e6777dd31f4fa231b12dc`. The candidate was measured from uncommitted sources under fingerprint `BC357369A536EC46175AA29324C317F2EC066EB5B9500493653DB8BE2520D0DD`. [The measured source manifest](release-import-fix/sources.json) and eight frozen closure manifests preserve its exact provenance. A nonexistent implementation commit hash is not substituted for that fingerprint. Later supplementary tests and watch harness changes do not modify any measured compiler source or frozen compiler binary; their provenance is recorded separately under [review](review). Previous dated result archives are unchanged.

[Verify-SemanticReuse.ps1](../../Verify-SemanticReuse.ps1) runs detached with a UTC journal, atomic status, readable/flushed child logs, source/binary hashes, an exclusive controller lock and resumable completed steps. Re-run the same command after interruption to resume matching sources. Changes require a new output directory. Tests have separate artifact directories for binding-only and combined builds. Accepted timing cells wait for a quiet host; contaminated partial cells are preserved and retried. Other worktrees are never stopped.

Four variants keep normal current-owner memoization enabled:

| Build property | Projection reuse | Semantic-result reuse |
| --- | --- | --- |
| Default baseline | Off | Off |
| `ProbeReuseStage=projection` | On | Off |
| `ProbeReuseStage=binding` | Off | On |
| `ProbeReuseStage=combined` | On | On |

Baseline compilers were rebuilt from the starting commit before compiler edits. Projection-only controls are the existing frozen experiment, not rebuilt from candidate behavior. A common updated host is installed in copies of every closure; original compiler binaries and controls remain unchanged. It adds post-GC retention observations and fresh detached-dependency parity outside measured GeneratorDriver intervals. The independent four-variant comparison does not rerun the earlier full projection investigation.

The matrix covers Counter, DataTemplates, DataTemplatesPage, Sidebar, NeedHelp, NavButton and the new [injected-view-model Siblings fixture](../../SemanticFixture/Siblings.akbura). The earlier projection fixture and its report are not rewritten. All 84 cells completed: 28 instrumented, plus two independent reverse-ordered non-STATS Release rounds of 28. Each cell has two warmups, eight measured updates and eleven fresh comparisons: 672 measured updates and 924 successful source/diagnostic/dependency parity checks. [Generated measurements](release-import-fix/projection-measured-summary.md), [cells](release-import-fix/projection-cells.csv), [raw samples](release-import-fix/projection-samples.csv) and [causal stages/counters](release-import-fix/projection-stages.csv) are retained separately from this interpretation.

## Ownership and payload

```text
BlackSilenceProjectState / exact CSharpCompilation A
  -> CompilationBoundProbeStore
     -> latest immutable generation
        -> per-document exact region keys and prepared C# syntax
        -> optional ReusableProbeBinding
           -> CSharpBindingResult (Roslyn-only semantic graph)
           -> detached scalar per-probe dependency facts

Current AkburaSemanticModel
  -> current BindingSession / CSharpProbeContext / bound wrappers
  -> transient private publication session

BoundedArtifactStore -X-> semantic payloads / Roslyn objects
Published generation -X-> previous generation / old Akbura owner
ProjectState B -X-> ProjectState A / ProbeStore A
```

`CSharpBindingResult` contains Roslyn types/symbols, candidate information, a conversion and a Roslyn operation definition. `AkburaConversion` contains flags, Roslyn types and conversion/method data, not a markup binder. A successful operation intentionally retains its synthetic probe SemanticModel/compilation/tree. Direct weak-reference audits retain the actual result while releasing the previous Akbura model, session, context, binder, syntax, snapshot and component-plan syntax. Static parse-cache ownership is excluded through the direct generation path, not misclassified as a result leak.

No old `BoundCSharpExpression`, conversion wrapper, Akbura operation, binder or plan is copied into the new owner. Existing callers construct those wrappers and `CSharpOperationTreeBuilder` creates current-syntax operation trees with the current symbol mapper. Projected component locals carry scalar kind/name/declaration-span annotations. Stable source positions allow the current mapper to associate reused native symbols with current declarations; arbitrary user locals, command/loop parameters and template captures are not admitted.

The initial live-current-state GC test exposed a pre-existing temporary edge: `SyntaxAndDeclarationManager` retained its previous declaration table after materialization. That table kept the previous markup tree alive even when old models were collectible. The previous-table field is now cleared after the current lazy declaration state is published; declaration reuse during construction and semantic-model compatibility rules remain intact. This is covered by the retained-owner smoke/stress checks, not hidden by releasing the current cache owner.

## Equivalence and supported subset

The projection matching boundary is unchanged: exact C# compilation reference, document identity, complete project declaration surface, root namespace/directory, exact surrounding source and source positions, with only same-width numeric `Margin` literals varying. Green identity is observational, never a sufficient proof.

After a same-owner miss, semantic validation checks the region/expression syntax and text, expected-type reference, prepared projection text plus structural equivalence, and probe-relative query span. In combined mode the validated prepared projection can be the same immutable object. Incoming annotations/directives, recovery and skipped syntax are rejected; compiler-generated origin annotations are governed by the unchanged exact context contract.

The initial subset consists of successful property/field access chains and compiler-projected component state/parameter/injected-service locals with known scalar origins. Payload operation trees must contain only supported member/instance/conversion/projected-local operations. Arbitrary captured locals, invocations, extension methods, generic inference, lambdas, queries, awaits, conditional access, ambiguous/missing symbols, diagnostic-bearing results, user-defined or non-implicit target conversions and incomplete dependency contributions fall back to original binding.

The existing binder maps even successful Roslyn candidate reasons to Akbura `NotFound`. This API behavior is preserved. Admission therefore additionally requires an actual supported selected symbol, empty candidates and diagnostics, supported operations and complete facts; the Akbura reason alone is not used as a success proof.

## Per-probe dependencies

An instance-owned, scoped `AsyncLocal` routes the existing collector algorithm to an independent per-probe collector during the actual factory call. This is not a static semantic cache or a set-difference snapshot of the whole document. Nested scopes contribute to their parent and unrelated parallel flows cannot mix facts.

The original logic records nested receivers/operations, source types, selected/candidate symbols, receiver-wide lookup candidates, negative lookups, conversions and incomplete reasons. Capture completion detaches sorted scalar/value records, restores the ambient scope and merges the facts into the current document. The payload never retains the capture scope or collector. Failed/incomplete contributions are not admitted.

On a validated hit the exact complete contribution is imported before returning, without invoking the factory or acquiring a validation SemanticModel. The current document's conservative completeness policy is preserved; a complete probe contribution does not make an otherwise unsupported document complete. Positive/negative capture parity, parallel isolation and incremental/fresh document-fact equality are tested. Benchmark fresh drivers keep dependency collection enabled when verifying facts; a fresh driver has no preceding artifact store to reuse.

## Publication and limits

Semantic candidates use the existing private session, immutable generation and project snapshot publication lock/version ordering. Error-diagnostic generations discard candidates. Cancellation is checked before lookup, capture, replay return and publication. Publication timing instrumentation is outside the mutation window, with a token check under the state lock before store/snapshot mutation. No partial entry or previous-session chain is published; older concurrent completions cannot replace newer generations.

Existing projection caps remain. Semantic admission is additionally limited to 32 entries/document, 128 entries/project, 8,192 prepared characters/semantic entry and 32 supported operation child nodes. These are conservative experimental bounds, not proof of complete retained-graph byte sizes. Sealing applies deterministic hard limits and removes obsolete documents/expressions. Binding-only mode does not retain projection-only entries.

Retention reporting distinguishes entries, native operation roots, probe models, probe compilations and syntax trees from whole-owner post-GC heap bytes. The 100/500 revision tests keep the current state alive, compare reachable native graphs against current entries, and separately release the old compilation owner while retaining state B. Stress allocations include fresh-generation oracles and are labeled accordingly. No external GeneratorDriver is held in those direct ownership tests.

## Verification and decision

Focused technical checks have demonstrated two first binding skips with zero additional probe compilations/models for the isolated calls, successive hits, exact detached facts and same-owner-hit separation. Generator-level checks cover global/injected properties, current-owner markup operations, fresh source/diagnostics/facts, context and C# changes. Cancellation/publication and long-lived graph checks are included in the gated controller, alongside relevant DebugStats, Release, SDK-host, Hot Reload and allocation tests. Counts from earlier investigation suites are not reported as new executions.

The gated controller completed at 15:01:08 UTC. Its correctness, stress and production evidence was reviewed before live `dotnet watch` verification. No unavailable CLR delta/EnC timing is inferred from GeneratorDriver measurements.

The initial run (`bin/SemanticReuse/gate`, source fingerprint `63092F557F0A55D9BDF03CDCE67801DAA44D1290452616C233A60F555B21BE6F`) completed binding-only DebugStats and SDK checks, then stopped at 11:44:25 UTC because Release compilation could not resolve `DetachedCSharpDependencySet`. Its namespace import was incorrectly inside `#if STATS`; no Release tests or timing cells ran. The import is now unconditional. Original logs, manifests and test results are preserved rather than attributed to the corrected sources. Validation restarts in `bin/SemanticReuse/gate-release-fix`, with new evidence under [release-import-fix](release-import-fix). Both Release variants now run before the long DebugStats/GC checks so configuration-specific compilation failures surface earlier.

| Suite | Binding-only | Combined |
| --- | ---: | ---: |
| DebugStats regressions, including 100/500 revisions | 141/141 | 141/141 |
| Release regressions, including Hot Reload and render revision tests | 216/216 | 216/216 |
| SDK Roslyn isolated-host checks | 31/31 | 31/31 |

These are 776 successful test executions, not 776 distinct tests. [Supplementary invalidation tests](../../../../src/Akbura.UnitTests/BlackSilenceIncrementalGenerationTests.SemanticReuseInvalidation.cs) add C# method-body edits, property removal, added overloads, negative lookup repair, conversion changes, unrelated/other-document changes, valid unsupported expressions, and document/expression deletion. They require normal binding where applicable and compare full fresh output, diagnostics and dependencies. Supplementary results are separate from the frozen matrix and its original suite counts.

All 13 supplementary DebugStats cases passed in binding-only and again with the exact frozen combined-STATS compiler pair installed in an isolated copy of that new test host: 26 additional successful executions. The test assembly enables semantic tests in both runs; the actual compiler pair selects binding-only versus combined behavior. These supplementary cases were not separately rerun in Release or the SDK-shadow suite and are not counted in the 216/31 gates. Their TRX and combined-shadow binary provenance are in [review/tests](review/tests). The complete changed-file inventory and final source hashes are in [review/source-provenance.json](review/source-provenance.json).

## Causal evidence

Counts below are per measured update in binding-only and combined, not same-owner repeat hits. Each hit also increments the dedicated probe-compilation and probe-SemanticModel skip counters by the same amount.

| Component | First C# bindings skipped/update |
| --- | ---: |
| Counter | 1 |
| DataTemplates | 3 |
| DataTemplatesPage | 6 |
| NavButton | 4 |
| NeedHelp | 7 |
| Sidebar | 15 |
| Siblings | 2 |

Baseline and projection-only report zero semantic first-binding skips. In Siblings, actual expression factory invocations decrease from three to one per update, and total probe compilations/models decrease from six to four. The two isolated property calls themselves perform zero new factories, compilations or model acquisitions; remaining unrelated probe work is not misreported as a failed skip. Projection constructions are three per update for baseline/binding-only and one for projection-only/combined.

Siblings binding-only lookup costs 0.0768 ms/update inclusive of validation (0.0513 ms); dependency import costs 0.0116 ms/update. Combined lookup costs 0.0340 ms/update inclusive of validation (0.0267 ms); import costs 0.0115 ms/update. Do not add nested lookup and validation times. Cold admission performs the original binding and capture; it is not counted as a reuse hit. Some real components still attempt incomplete captures on every update. Total probe deltas therefore need not equal dedicated skip counters: capture and other probe paths can add work. Counter binding-only, for example, has one genuine skip but more total probes than the old baseline. The dedicated counters and direct zero-factory assertions establish the causal claim.

## Production timing interpretation

Each percentage below is a reduction of a non-STATS Release GeneratorDriver median against the matching baseline round. Negative values mean regression. These are two small rounds, not confidence intervals or production tail-latency estimates.

| Component | Binding-only, rounds 1 / 2 | Combined, rounds 1 / 2 |
| --- | ---: | ---: |
| Counter | 1.50% / 3.35% | 1.33% / 4.91% |
| DataTemplates | 1.06% / -0.59% | 2.54% / -0.66% |
| DataTemplatesPage | 1.15% / 0.15% | 1.36% / 2.64% |
| NavButton | 2.55% / 3.93% | 8.77% / 13.93% |
| NeedHelp | 9.34% / 11.16% | 14.28% / 7.11% |
| Sidebar | 1.61% / -0.63% | 3.59% / 2.04% |
| Siblings, focused fixture | 19.89% / 12.71% | 20.29% / 12.17% |

Using equal-weight geometric means of candidate/baseline median ratios over the six real components and both rounds, elapsed-time reductions are **2.95% binding-only** and **5.27% combined**. Including the focused fixture gives 4.99% and 6.94%, respectively. These aggregates are descriptive and must not conceal DataTemplates' sign reversal or the near-noise results on several other components. Projection-only regresses by 0.99% over real components (1.89% including the fixture); its second round regresses on every workload. It is not accepted as a standalone general performance improvement.

The repeatable useful wins are concentrated in specific reuse workloads, notably NavButton and NeedHelp, with modest combined Sidebar gains. Retaining this narrow experiment is justified; broadening or enabling it globally is not. Allocation reductions do not establish a retained-memory or end-to-end latency reduction.

## Retained memory review

The four 100/500-revision tests report zero obsolete markup owners alive while the latest state remains alive, and zero obsolete objects alive after the compilation transition. Each keeps exactly two current semantic payloads, operation roots, probe models, compilations and syntax trees. Binding-only retains two total entries; combined retains two at 100 revisions and three at 500 (one is projection-only).

| Variant | Revisions | Live semantic graphs | Post-GC whole-owner heap, bytes | Obsolete owners / transition objects |
| --- | ---: | ---: | ---: | ---: |
| Binding-only | 100 | 2 | 66,676,872 | 0 / 0 |
| Binding-only | 500 | 2 | 67,401,472 | 0 / 0 |
| Combined | 100 | 2 | 66,805,328 | 0 / 0 |
| Combined | 500 | 2 | 67,539,000 | 0 / 0 |

These heap values cover the entire retained test owner, not separately sized operation subgraphs. Stress allocation totals include fresh-generation oracles: approximately 60.96 GB at 100 revisions and 305.6 GB at 500. They are cumulative transient allocations, not retained cache bytes. Raw retention JSON is under [binding](release-import-fix/tests/binding/retention) and [combined](release-import-fix/tests/combined/retention).

Real-project observations retain 29 semantic operation/model/compilation/tree roots for Gallery and 27 for Purity, rather than an ever-growing revision count. Combined additionally retains up to 567/251 projection entries, subject to the separate hard bounds. Across production rounds, binding-only whole-driver heap changes range from -1.88% to +2.21% on real components; combined ranges from -1.60% to +2.87%. The small fixture improves, but that does not prove universal memory savings. Candidate compiler changes also release the pre-existing previous-declaration-table edge; heap differences against the frozen old baseline cannot be attributed exclusively to the cache.

## Real watch verification

The existing [watch verifier](../../Verify-MarkupWatch.ps1) now accepts `-MultipleBindings`, using a small [fixture overlay](../../SemanticWatchFixture) with two independent reactive name bindings, one component state value and an editable TextBox. It changes an existing Text literal nine times. After every update it checks the same process and controls, user-edited input, state value 42, both independently updated names and a constant external notification subscription count. The framework shares one external notification handler for both paths; expecting two handlers was an incorrect initial harness assumption, corrected against baseline observations. Timer checks run on a subsequent dispatcher turn, and oracle exceptions explicitly fail the child process.

| Variant | Measured updates | File-write-to-observed-UI median, ms | Range, ms |
| --- | ---: | ---: | ---: |
| Baseline | 8 | 171.017 | 124.886–282.877 |
| Projection-only | 8 | 194.606 | 123.975–341.204 |
| Binding-only | 8 | 193.378 | 140.146–355.117 |
| Combined | 8 | 186.484 | 138.683–421.980 |

Each run also has one first-edit warmup. All 36 updates passed identity/state/input and binding/subscription checks. This small single-round watch comparison does **not** demonstrate an end-to-end improvement. In particular, these Text edits intentionally reject the restricted Margin-only contract; they verify conservative runtime fallback, not a live semantic cache hit. Generator reuse evidence comes from the separate controlled matrix.

`-DiagnosticRepair` additionally introduces a real missing-member diagnostic, observes compiler rejection, repairs the source and observes the changed UI without restarting. Baseline and combined each passed nine ordinary updates plus one repair. Repair observation latencies were 169.405 ms and 155.918 ms, respectively; these two functional samples are not a performance comparison. Both names remained reactive and subscriptions stayed bounded after repair. Repair runs use a separately built host supporting the extra check, with the exact frozen baseline/combined production compiler pairs copied unchanged. Its host hash is intentionally different from the measured matrix host. All raw watch events, compiler/fixture hashes, build logs and repair records are in [review/watch](review/watch).

Initial unsupported global binding-root and oracle-development attempts remain excluded from timing evidence. Only the validated and repair runs above count as successes. No screen-presentation, IDE, generator-in-watch, EnC or CLR-delta-submission timing is available or invented.

## Decision and limits

| Variant | Decision | Reason |
| --- | --- | --- |
| Projection-only | Restricted, off by default | Correct narrow reuse, inconsistent standalone production benefit |
| Binding-only | Restricted, off by default | Proven first-binding skips and useful workload-specific gains; not consistently faster everywhere |
| Combined | Restricted, off by default | Best measured real-workload aggregate and repeatable targeted wins; narrow edit contract and no general watch speedup |

The implemented success is a bounded, compilation-owned semantic fast path with exact dependency replay, current-owner wrappers, fresh parity and collectible obsolete graphs. It is not a general cache for arbitrary C# expressions, templates, scope changes, overload resolution or other compilation instances. Unsupported categories stay on the original binder. The finite test matrix is not a claim that every possible ambiguous/generic/template environment has been exhaustively exercised. No broader matching, default activation or next optimization stage is authorized by these results.

The generated measurement summary is descriptive, not automatic acceptance. No next roadmap stage, generated-C# delta minimization, commit, push or merge is performed by this controller.

## Roslyn grounding

[CompilationCache](https://raw.githubusercontent.com/dotnet/roslyn/main/src/Compilers/Core/Portable/SourceGeneration/CompilationCache.cs) provides the exact-input-compilation identity model. [SemanticModel](https://raw.githubusercontent.com/dotnet/roslyn/main/src/Compilers/Core/Portable/Compilation/SemanticModel.cs) documents its compilation/tree ownership and retention cost. [NodeStateTable](https://raw.githubusercontent.com/dotnet/roslyn/main/src/Compilers/Core/Portable/SourceGeneration/Nodes/NodeStateTable.cs) motivates distinguishing cached work from equivalent outputs. This implementation retains the existing generator/state pipeline; it does not import Roslyn private storage or replace it with a global graph.
