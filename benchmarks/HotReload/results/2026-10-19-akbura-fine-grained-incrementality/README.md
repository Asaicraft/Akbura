# Localized .akbura edit investigation

Status: evidence collection and engineering review are complete (October 20, 2026). All 3,543 Release unit tests passed on the successful full-suite repeat. The 40-cell instrumented matrix, six-cell non-STATS Release repeat and actual dotnet watch checks finished. Correctness is verified, but a general editing-latency improvement is not established: this candidate is not accepted as an across-the-board performance optimization. The evidence controller did not commit, push, merge or start a subsequent optimization stage. Starting revision: `ba6b0a1a55a07714ecd97c34946c9caad167c18c`, branch `feature/improve-hot-reload`.

## Reviewed result

The change eliminates real duplicate probes within a current document owner, but it does not avoid the first binding of unchanged expressions after a markup edit creates a new owner. That remaining limitation matters more than a lower probe counter alone. The candidate and its tests are preserved for review; the mixed timings do not justify merging it as a general responsiveness improvement.

All 880 instrumented and 132 production before/after source/diagnostic records match, and every revision passed fresh-driver parity. The instrumented comparison contains 40 cells with eight measured edits each: 13 median times decreased and 27 increased. The six production cells split three faster and three slower. These are observed differences from sequential runs, not statistical confidence intervals or proof that every difference was caused by memoization. Do not dismiss the regressions as noise without further evidence.

Representative literal-edit medians from the instrumented matrix:

| Component | Generator ms before -> after | Time change | Actual probe compilations before -> after | Process allocation change | Post-GC heap change |
| --- | ---: | ---: | ---: | ---: | ---: |
| Counter | 222.75 -> 224.91 | +0.97% | 93 -> 93 | +0.24% | -1.67% |
| DataTemplatesPage | 3,944.69 -> 5,284.92 | +33.98% | 625 -> 417 | -2.47% | +1.47% |
| NeedHelp | 207.60 -> 221.86 | +6.87% | 95 -> 83 | -3.77% | +2.58% |
| Sidebar | 699.28 -> 687.04 | -1.75% | 362 -> 295 | -8.19% | +1.70% |

In the non-STATS Release repeat, Sidebar's literal edit decreased from 726.53 to 634.38 ms (-12.68%) and diagnostic edit from 693.54 to 640.87 ms (-7.59%). Counter's literal edit increased from 240.25 to 249.04 ms (+3.66%), its diagnostic edit from 230.04 to 252.39 ms (+9.72%), and its binding-sibling edit decreased from 227.66 to 226.00 ms (-0.73%). NavButton's binding-sibling edit increased from 163.64 to 178.09 ms (+8.83%). Production process allocations decreased by about 8.2% for Sidebar and 12.0% for NavButton's measured cases, without establishing consistent latency improvements. The production Sidebar literal retained-heap median increased from 147.15 to 152.87 MB (+3.89%); allocation savings are not retained-memory savings.

For a literal edit, the optimized cache records 0/17 hits/requests in Counter, 204/344 in DataTemplatesPage, 12/66 in NeedHelp and 67/248 in Sidebar. The latter hits prove avoided exact duplicate executions inside the same owner; they do not establish that all remaining probes are necessary. Counter still makes 93 actual probes, including work outside this scoped-expression cache. Existing type/diagnostic/current-model caches do not cover these identical scoped-expression requests, but remain in use. Parsing was not the dominant observed cost, and the driver still regenerates one component while reusing the other 27 gallery or 14 Purity components.

The safety/collectibility tests passed for 100 and 500 revisions, cancellation and independent concurrent branches. Those tests establish the tested old-owner objects can be collected; they do not promise zero additional retention in the current owner or an unbounded-run memory plateau. Full per-cell allocation and retained-heap measurements, inclusive stage costs and ranges are preserved in [the generated summary](measured-summary.md), [the instrumented comparison](comparison.csv) and [the production comparison](production/comparison.csv).

### Actual dotnet watch

Both frozen analyzer versions completed nine real markup updates in the headless Counter fixture: one first-update warm-up and eight measured updates. The application process, original control, state and user-entered input survived every update.

| File write to observed rendered value | Before ms | After ms |
| --- | ---: | ---: |
| First update, excluded from measured median | 2,308.57 | 2,039.64 |
| Median of eight subsequent updates | 139.39 | 131.06 |
| Measured range | 107.51-206.54 | 109.72-248.05 |

The observed median decreased by 5.98%, with overlapping ranges and a higher maximum afterwards. This is one before/after fixture pair, not proof of an end-to-end optimization: Counter has no scoped-expression cache hits in the real-project matrix. The watch wrapper does not continuously audit foreign build activity as the generator timing controller does. Treat this as successful real CLR Hot Reload/state-preservation verification with descriptive latency observations, not a clean causal performance experiment. Exact generator processing and compiler-delta submission times remain unavailable. Raw logs and analyzer/host digests are under [watch/before](watch/before/result.json) and [watch/after](watch/after/result.json).

### Acceptance and remaining bottleneck

Do not merge the speculative cache as an unconditional speedup on this evidence. Preserve the implementation and raw results for review, including unsuccessful attempts. The next decision would be to remove the candidate or separately investigate its lookup/retention cost and perform controlled production repetitions on the slower paths; neither a new optimization nor another long run was started automatically. The broader unresolved cost is first-time semantic/probe work in each changed document owner, including current scope preparation and complete current-component planning/emission. Safe cross-owner expression reuse has not been implemented or demonstrated by this change.

## Investigation

The existing document graph already limits emission to the changed component for ordinary markup edits. Changing a literal still creates a new document semantic owner. `SemanticModelState` deliberately requires the exact original source tree and C# compilation, excludes bound trees/operations/probe contexts, and is not an expression cache across markup revisions. Reusing an expression's green node alone would not establish imports, locals, expected conversions, name scope or compiler-owner compatibility.

`Parser.Incremental`, Blender and the green tree already support sibling attribute, element and embedded-C# subtree reuse when given precise change ranges. Red nodes have new parents/positions in the new tree even when their green nodes are shared. Recovery, skipped text and affected lexical/conditional contexts restrict reuse. `LexerCache` pools lexical tables; it does not preserve semantic results. BlackSilence's bounded parse cache supplies the previous tree, but independent `SourceText.From` inputs report a whole-document change. The initial real-project trace consequently observed zero shared green nodes after a small edit. No second parser or new diff algorithm was added: parsing took less than a few milliseconds in these diagnostic runs, versus hundreds of milliseconds or more in binding.

`SemanticBindingCache` caches symbols, operations, bound nodes and diagnostics within one current document. Type probes and covering diagnostic queries already have current-owner caches in `CSharpProbeContext`. The scoped `BindMarkupAttributeExpression` path, however, directly requested a fresh C# probe even when another request in that same owner had prepared an identical C# unit. `CreateSemanticModel` derives a new probe compilation and acquires a model for every execution. Recursive bound-expression construction performs additional symbol/operation and diagnostic queries; covering-span diagnostics already avoid repeating Roslyn queries inside one such model.

Initial investigation used the frozen unoptimized implementation with opt-in trace instrumentation. The Sidebar literal edit requested 261 expression probes; grouping complete prepared text by document/category/target gave 193 groups (68 repeated requests). Requiring the same source position as well gave 247 groups. These are observed duplicate-text candidates, not a semantic-equivalence proof or a prediction of cache hits. Some requests belong to dependencies whose symbols are needed even though their generated output is reused. The small Counter trace showed 17 scoped requests without same-position duplicates. Those traces overlapped tests in another worktree: their elapsed values are excluded from accepted timing comparisons.

The full component plan and C# text still need reconstruction after a property change. `ComponentPlanner` and `ComponentMemberPlanner` consume current operations, declarations and content; `ComponentScopeWriter`/`ComponentWriter` emit the current plan and Hot Reload identities. `ComponentDocumentWriter` creates the complete current source text. BlackSilence then compares complete outputs and publishes current diagnostics/mappings. Output equality is not used to bypass semantic analysis. No runtime reconciliation, public syntax, identity algorithm or detached-artifact schema changes are included. Verification also uncovered the conservative bodyless-class compatibility fix documented below.

## Targeted implementation and safety boundary

The existing document-owned `CSharpProbeContext` now memoizes successful scoped markup expression results. It does not share semantic state between markup revisions or compilations. The key includes all of:

- The complete prepared C# compilation unit, including exact trivia/text and structural syntax equivalence, not just the expression text.
- The exact expected target-type object, including the null-target case.
- The queried return-expression span, so annotations selecting different occurrences cannot alias despite identical C# text.
- Implicitly, the immutable current document/compilation owner. Every owner gets a separate context.

The prepared unit is constructed before lookup, so current imports, generated members, visible names, preceding locals, foreach/conditional wrappers, method type parameters/modifiers and expected return types participate in the key. The same immutable base C# probe compilation and parse/compilation options belong to the context throughout its lifetime. This is the equivalence boundary for the actual Roslyn input, rather than a claim that unchanged markup text has unchanged semantics.

On a hit, callers receive the existing complete raw C# binding result; current Akbura property/content analysis, literal conversion, diagnostics construction, source mapping and planning still execute. No old Akbura bound node or binder is returned for a new location. Raw successful probe operations remain in their original, compatible current-owner probe compilation. They are not inserted into detached cross-compilation artifacts or `SemanticModelState`.

Syntax recovery, skipped text and directives bypass the cache. Results carrying Roslyn errors are not published. A throwing or canceled factory publishes no entry. Concurrent misses can compute independently; only completed results enter the concurrent dictionary. This is not a single-flight lock and does not claim one execution for simultaneous first requests. Generation's existing transactional artifact/snapshot publication remains unchanged.

The dictionary is limited to one live document model's requests, with no previous-snapshot reference or parent-cache chain. Identical queries within that owner share the successful operation; a new markup revision gets a new dictionary and performs its own first binding. Retained-heap measurements and weak-reference tests must verify the cost of retaining current results separately from allocated-byte savings.

### Deliberately rejected alternatives

- Sharing by expression text or green identity: insufficient semantic context and stale diagnostic mapping risks.
- Importing bound operations or probe models through `SemanticModelState`: violates its owner-independent result boundary.
- New cross-compilation or cross-revision expression cache: not justified by this narrow duplicate-request finding; it would require additional declaration/environment equivalence and ownership rules.
- Rewriting the incremental parser: precise-range reuse already exists and parsing was not the measured dominant cost.
- Omitting recursive expression binding or dependency capture: the current implementation preserves those paths on every actual execution; removing them would require a separate proof about diagnostics and dependency facts.
- Special-casing literal semantics or replacing Roslyn conversion logic: unnecessary for exact-input memoization and potentially changes contextual conversions.

## Workload and measurement protocol

The existing invalidation driver and resumable contract controller are extended, not replaced. Ten `localized-*` scenarios produce 40 cells across the real projects. Counter and NeedHelp each cover all ten categories. DataTemplatesPage and Sidebar each cover eight; the two inline-Text-expression categories use the actual DataTemplates and NavButton components respectively. These documentation/parent components do not themselves contain live inline Text expressions. Original project inputs remain unchanged on disk. The driver reconstructs the selected revision from the fixed original, recording the original input digest and initial controlled markup in JSONL.

| Category | Controlled edit |
| --- | --- |
| Literal | Existing/inserted Margin: 10 -> 20 -> 30 -> 10 -> 40, repeated |
| Simple property | Width: 200 <-> 250 |
| AKCSS | p-4 <-> p-5 on an existing Border or Button with Padding |
| Binding sibling | Margin changes beside an existing dynamic Text expression |
| Expression | Change an existing Text expression's appended string, retaining its real state/parameter dependencies |
| Add/remove | Opacity assignment added and removed |
| Structure | Insert/remove a sibling Border |
| Named scope | Change an added x.Name between two names |
| Diagnostic | Introduce/repair an unknown property |
| Trivia | Insert/remove whitespace before an existing element, including position shifts |

Small and complex components are both retained. The six-cell production Release repeat uses Counter and Sidebar for literal/diagnostic categories, Counter for binding-sibling, and the live NavButton for the Purity binding-sibling category. Every group has an initial revision, two warm-ups and eight measured edits; each revision is compared to a fresh driver for every source hint/text and generator/relevant compiler diagnostic value. The diagnostic oracle includes IDs, severity, messages, original/mapped spans, additional locations and properties; the publisher's volatile revision stamp is separately validated.

The original instrumented binaries were frozen before changing binding behavior in `bin/FineGrained/before/tool`. The original non-STATS Release binaries were separately frozen from the previously verified build (October 19, 06:30 UTC), before any Release build of this implementation. Separate before/after directories and binary/input hashes prevent accidentally rebuilding the baseline from new code. The new production closure retains the original benchmark host and replaces only its analyzer pair with the optimized Release builds.

STATS counters cover explicit semantic cache getters, parser subtree acceptance, probe requests, actual probe compilation/model creation and diagnostic queries. Existing inclusive stage timers cover parsing, binding, planning, emission and publication. Outside the timed interval, the driver counts actual shared/new green nodes and embedded expressions from the preceding/current roots; this is syntax identity, not semantic reuse. Trace mode additionally records per-expression prepared-input digests and cache outcome reasons. It is diagnostic overhead and is separate from the normal STATS matrix and non-STATS Release timing groups. Unsupported rebind reasons are not inferred.

Timing groups wait for other builds/tests. A contaminated partial group is retained but excluded and retried; completed groups are not repeated. Functional checks may run alongside another worktree. The outer controller journals UTC progress, timestamps failed child attempts, preserves successful verification markers, fingerprints sources and resumes only matching evidence.

Actual `dotnet watch` verification ran separately on copies of the existing headless Counter fixture using frozen original/optimized Release analyzers: one first update and eight measured Text-literal updates, with an unchanged count binding, original process/control, state and user input preserved. Its boundary is file write to rendered value; exact EnC generator processing, IDE timings and compiler delta submission are unavailable and must not be invented.

## Verification status

The first focused run passed 84 cases (25 localized driver cases, 12 expression-cache cases, 21 existing probe cases and 26 existing semantic binding cache cases). It includes the 21-requests/one-actual-probe assertion, different locals/expected types, failure/cancellation, complete detached-fact parity and 100/500 model-revision collectibility. Further annotation-target and generated-C# difference tests were added afterwards, so that initial run is not final-source sign-off.

The extended verification passed 546/546 relevant DebugStats, 284/284 relevant Release and 238/238 SDK Roslyn-host cases, including existing C# invalidation/detached-artifact lifetime tests, parser/semantic architecture/Hot Reload identity and CLR smoke tests. The successful complete Release Akbura.UnitTests repeat passed 3,543/3,543 cases. These overlapping suites are not additive unique-test counts. New cases cover literal neighbors, nested markup, names, state/parameters/services, locals, foreach/templates/conditionals, add/remove/structural edits, diagnostic repair, source shifts, imports, repeated identical source, undo/redo and independent concurrent driver forks. Existing cancellation publication checks remain enabled. Harness-only changes after these suites were validated using the source/binary reuse checks described below, rather than reported as new executions of unchanged compiler suites.

The generated-code comparison records actual differing lines for Margin="10" -> Margin="20" in both direct and structural generation. The first version incorrectly assumed exactly one differing C# line: two new tests failed that assumption, while the other 39 final focused cases passed. Inspection showed two emitted value assignments (initialization/reconciliation) in direct mode, and those two values plus four derived fingerprint constants in structural mode. The test now asserts those explicit differences, permits no other changes, and retains every neighboring binding line exactly. No existing test was weakened or generator identity behavior altered. After verification, the summarizer extracts the recorded lines into generated-code-diffs.json and measured-summary.md; it does not reconstruct or invent the emitted code. The unsuccessful initial assertion run is preserved under investigation/.

Final before/after timings, allocations, retained heaps and probe counts are now recorded in measured-summary.md and the adjacent CSV/raw archives. The remaining major limitation is intentional: unchanged expressions still perform a first binding in each changed document owner; this optimization removes duplicate exact-input work within that owner, not all unchanged-expression rebinding across edits. Review distinguishes actual probe avoidance, observed time improvements/regressions, retained-cache cost and measurement variability. Since common paths do become slower in this comparison, the speculative memoization is not signed off as a general performance improvement.

## Interrupted verification and corrections

The first extended DebugStats run completed 511 tests: 509 passed and two existing CLR smoke tests failed before applying their first CLR delta. Both hit the same pre-existing declaration-contract assumption: C# permits `class Empty;`, whose default brace tokens have no usable source positions. `CaptureTypes` attempted a negative-length substring. Bodyless classes now remain in the exact source segments instead of entering selective type-contract comparison. Their changes still require normal invalidation. Nine new cases cover global/file-scoped/block namespaces, position before/after a supported class, rename, adding a body and removing a body, with fresh-driver parity. The focused repeat passed all 11 cases (nine new cases and the two previously failing CLR tests).

The original timing controller completed two groups and then rejected the AKCSS fixture: `p-4` was attached to a `TextBlock`, which has no Padding property. The corrected workload edits an existing Border or Button instead, without replacing the real component. Both original and optimized implementations use the same corrected benchmark host. Only the host DLL/PDB were changed in new copies of the frozen closures; the original analyzer DLLs are byte-for-byte preserved, with hashes in `investigation/corrected-harness-provenance.json`. The original directories and failed evidence remain intact. All timing groups are rerun in new directories; the two earlier groups are not mixed into the corrected comparison.

Before the long suites, the restarted controller checked both AKCSS revisions on all four components with original and optimized instrumented binaries. All eight checks passed. These are functional/fresh-parity checks, not accepted timing samples. The first extended failure and original benchmark attempt are archived under `unsuccessful/initial-attempt/`; focused correction evidence is under `verification/fixes-focused/`.

### Full Release allocation-check failure

The corrected verification passed 546/546 relevant DebugStats cases, 238/238 SDK Roslyn-host cases and 284/284 relevant Release cases. The subsequent full Release run executed 3,543 cases in 44.93 minutes: 3,542 passed and `ConditionalRenderStateTests.UnchangedActiveBranch_DoesNotCreateTransactionOrAllocate` failed its exact zero-allocation assertion (1,016 bytes instead of zero). It did not fail a branch-identity, transaction or generated-code assertion. The failed full run is preserved under `unsuccessful/full-release-allocation/`, not counted as a successful full suite.

Without changing source, binaries, allocation thresholds or runtime settings, the failing test passed in five separate fresh test-host processes. The enclosing conditional-render/name-scope/capture group also passed in five fresh processes (37/37 each). All original allocation assertions remained enabled. The successful isolated checks suggest a non-deterministic allocation measurement but do not establish its cause or prove the entire suite green. No runtime or test implementation change was made on that assumption. The diagnosis summary, binary hashes and all repeat TRXs are archived beside the failed run.

The controller resumed with the same source fingerprint and arguments, preserving the eight AKCSS checks and completed quality step. The repeated, unfiltered full Release suite then passed all 3,543 tests without changing its assertions or runtime settings. That success does not erase the earlier unsuccessful attempt or establish its allocation-noise cause.

### Frozen statistics ABI compatibility

The first corrected timing group subsequently exposed a harness bug, not a generator failure: `Enum.GetValues` used the loaded old enum, but its numeric `Count` comparison was embedded from the new enum at host compilation. The frozen instrumented DLL has `Count = 50`; the new DLL has `Count = 54`. The old sentinel was therefore treated as a metric and `GetCounterCount` correctly threw `ArgumentOutOfRangeException`. Earlier `off` preflight checks bypassed statistics serialization and did not catch this. The failed zero-group attempt remains in `unsuccessful/counter-sentinel/`.

The shared benchmark reader now selects runtime enum names excluding `Count`, for both counters and stages, including concurrent-retention output. No exception is swallowed, counter limits are not relaxed, and unavailable newer metrics are not invented. The same new host successfully reads all 50 original instrumented counters, 54 optimized counters and 46 original non-STATS Release counters; each exposes 76 stages. Real Counter literal edits also passed statistics serialization and fresh-driver parity at both initial and edited revisions on original and optimized DLLs. Those concurrent functional checks are not accepted timing samples (`investigation/statistics-smoke.json`).

New frozen closures retain the original analyzer bytes and replace only the common host DLL/PDB; their hashes are in `investigation/statistics-harness-provenance.json`. The restarted controller checks the statistics ABI immediately and runs all component preflights with `stats`, not just `off`. A skipped retention preflight is no longer displayed as if it were running.

Only benchmark C#/PowerShell files changed after successful compiler tests. `Get-VerificationReuse.ps1` verifies the original source fingerprint, unchanged commit/compiler/test sources, the explicit harness-only change allowlist, successful unfiltered full-suite evidence and test-binary timestamps before reusing those TRXs. It records both fingerprints, actual original TRX hashes and binary hashes at validation time in `verification/reuse.json`; it does not pretend these are newly executed tests or historically captured binary digests. Six reuse-guard checks cover valid reuse and rejection of changed revisions/compiler sources, tampered current manifests, filtered full suites and failed results. Benchmark scripts and statistics checks are rerun separately. Compiler, runtime or test-source changes invalidate this reuse path.

### Selecting live expressions rather than displayed examples

The statistics-enabled baseline completed 13 groups before the DataTemplatesPage binding-sibling fixture failed while selecting its first dynamic Text attribute. Its apparent template expressions are inside a displayed raw-string example; the live template is in DataTemplates.akbura. Complete input-construction coverage also identified that Sidebar's TextBlock is a static label, while its child NavButton owns the live Text expression. Only these four unmeasured cells are redirected, with the identical mapping on before/after runs (and the corresponding single production binding-sibling cell). No required category is skipped and no synthetic expression is inserted into a static label.

All 440 input revisions (40 cells, indices 0 through 10) now pass preparation in a short separate process using the frozen host's own input-construction method. This is not semantic verification or a timing result; the repaired expression cells also receive original/optimized stats-enabled fresh-driver checks before timing. The runner archives `cell-overrides.json`, bound to the immutable run.json hash, to make effective component selection explicit. It rejects routing collisions, conflicting subsequent mappings and attempts to redirect an already completed cell.

The original benchmark host, analyzer DLLs, input hashes and all 13 accepted JSONLs remain unchanged. Their pre-resume SHA-256 digests are recorded in `investigation/preserved-timing-groups.json`. The old incomplete documentation-page attempt is moved to the run's recoverable `interrupted/` archive, not mixed into accepted traces. Completed groups are validated and resumed without waiting for unrelated builds; only new timing groups wait for a quiet host. The existing compiler/unit-test evidence is reused after the same source/binary validation, while routing/controller regression checks are rerun.

## Progress and resumption

From the repository root:

```powershell
pwsh -NoProfile -File benchmarks/HotReload/Verify-LocalizedEdits.ps1 `
    -OutputDirectory bin/FineGrained/templates `
    -BaselineDirectory bin/FineGrained/before-statistics `
    -OriginalInstrumentedDirectory bin/FineGrained/original-instrumented-statistics/tool `
    -OriginalProductionDirectory bin/FineGrained/original-production-statistics/tool `
    -VerifiedRunDirectory bin/FineGrained/retry
Get-Content bin/FineGrained/templates/journal.log -Tail 20 -Wait
Get-Content bin/FineGrained/templates/status.json
```

The launcher returns a hidden controller PID. The recorded run completed at 2026-10-20T14:41:47Z; these commands are retained for reproducibility, not an instruction to restart it. Re-running the same command resumes matching evidence; changed implementation/harness sources require a new output directory. The frozen original analyzer closures are prerequisites and must never be reconstructed from optimized source. `EvidenceCompleteNeedsReview` is the controller's terminal collection status, not a performance acceptance decision. The manual review above does not change that historical status or its source fingerprint. No commit, merge, push or subsequent optimization stage is automatic.
