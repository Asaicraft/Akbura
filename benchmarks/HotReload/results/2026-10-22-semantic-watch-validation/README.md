# Supported semantic cache hits in live Hot Reload

Status: **completed and reviewed; real live cache hits verified, production speedup inconclusive, experimental defaults unchanged**. See the [final results and engineering decision](recovery/README.md#results-and-engineering-decision).

The first controller stopped after the 100 measured supported updates, two warmups, six undo/redo updates and three negative-control updates had all been acknowledged. Its invalid-runtime oracle incorrectly required the deliberately broken `FirstName` expression to remain reactive. Independent baseline and combined reproductions showed identical existing behavior: that label keeps its last displayed value while its invalid binding is removed; the other three name bindings continue updating, with one shared external subscription. This is not evidence of stale semantic reuse. The failed generation reports one error and does not replace the last valid semantic-store generation.

The [recovery run](recovery/README.md) waits for the diagnostic fallback to be applied, checks the invalid label's retained value and all unaffected bindings, then requires all four bindings again after repair. No compiler or unit-test source was changed for this harness correction. Completed correctness executions are reused only after exact source and binary verification, with their original timestamps and provenance, not counted as new test executions. The first source manifest, closures, completed causal runs and failed stress evidence remain separate and unchanged.

Starting reference: `0510c165b575c85703bb8b8ac5636917ddb25466`. Execution starts on 2026-10-21 UTC. This directory is separate from all earlier investigations. The previous [semantic reuse evidence](../2026-10-21-semantic-reuse/README.md), [projection experiment](../2026-10-21-projection-reuse/README.md) and [scoped-cache audit](../2026-10-21-cross-revision-binding/README.md) remain unchanged. Previously completed tests are not reported as new executions.

## Reproduction and progress

From the repository root:

```powershell
pwsh -NoProfile -File benchmarks/HotReload/Verify-SemanticWatch.ps1
Get-Content bin/SemanticWatch/gate/journal.log -Tail 20 -Wait
Get-Content bin/SemanticWatch/gate/status.json
```

The launcher returns a hidden background controller PID. UTC journal entries, readable child logs, atomic status, source fingerprints, frozen closure hashes and successful-step markers survive interruption. Re-run the same command to resume matching sources. Source, seed or protocol changes require a new output directory. Interrupted completed watch jobs restore their archive from the preserved attempt before being reused. Active orphaned children prevent duplicate launches.

Functional tests may overlap unrelated work. Accepted production groups wait for a quiet host; detected foreign build/test activity contaminates the whole group. Such attempts remain local evidence and are retried after the competing work finishes. No foreign process is stopped. The process monitor samples every 15 seconds; idle compiler servers and reusable MSBuild worker nodes alone are not classified as active competing builds. Slow uncontaminated samples are retained. The controller imposes explicit child deadlines rather than leaving a failed observer waiting indefinitely.

## Workload and protocol

The original Text-edit verifier is unchanged and remains a historical unsupported-edit control. A separate [supported fixture](../../SupportedWatchFixture/Counter.akbura) adds a numeric `Margin` hole and two ordinary property chains:

```csharp
global::Demo.WatchPerson.Current.FirstName
global::Demo.WatchPerson.Current.LastName
```

These two expressions meet the existing member-chain and operation restrictions; their successful per-probe contributions can be captured completely. The two reactive state initializers are not assumed to qualify. They independently exercise ongoing binding behavior and one shared external notification subscription. The two ordinary property labels must also display the newly changed model values after each reload.

Every edit is acknowledged by a numbered command/response protocol. The application verifies the full uniform Margin, original root/panel/child identities, TextBox input, component state 42, both reactive labels, both direct member labels and exactly one external notification handler. It changes both model names and verifies their new values on a later dispatcher turn. Duplicate, stale, missing, foreign or out-of-order acknowledgments fail verification. Oracle exceptions explicitly terminate the child.

Primary latency starts **after** the source file write and close complete, not before the write. It ends at the host's verified UI acknowledgment. Write duration is separate. Startup, 500 ms inter-update delay, telemetry-file processing and explicit post-GC observation are outside the primary interval. Native detection messages are reception observations, not a synchronized compiler clock. Unavailable generator, reconciliation and CLR-delta segments remain null.

## Causal attribution

An opt-in STATS-only recorder runs inside the actual incremental generator's request and generation callbacks. It records scalar observations to the directory inherited by the watch child, not through application access to compiler statistics. Every credited record matches the edited source SHA-256, the intended compiler/generator module MVIDs and a `dotnet-watch` host command line. Header identities use the existing weak diagnostic identity service, never object hashes as semantic equivalence proofs. The recorder itself has no static cache, no persistent subscriptions and only weak previous-input/state references. Production builds contain no recorder type or trace formatting call sites.

Both request and generation scopes are measured because semantic work can occur in either callback. Only successfully published, noncanceled, error-free versions are credited; stale/failed raw records remain preserved. Dependency import and first-binding/compilation/model skip counters come from the existing actual fast-return branch. Per-expression decisions and document-contract decisions distinguish unsupported expressions, incomplete contributions, missing prior generations and incompatible surrounding source.

The development pilot confirmed two actual first-binding skips per eligible Margin edit, with matching compilation/model skips and imported contributions, while the application remained alive and its Margin changed. It also exposed an important existing boundary: CompilationProvider can supply a different input object while `CSharpEnvironmentSnapshotComparer` retains the equivalent original compilation. The **selected owner**, project state and store remained identical. No equality rule, matcher or ownership rule was relaxed to obtain that result. Pilot data is not a frozen production performance sample; final causal runs must independently confirm it.

Three focused recorder tests passed during preparation: actual skip reporting/ambient measurement restoration, diagnostic publication and repair, and collectibility of the state/compilation/store while the recorder stays alive. Final-source suites are run again by the gated controller; preparation counts must not be added to those suite counts as distinct tests.

## Evidence collected

- Four current-source variants: baseline, projection-only, binding-only and combined; normal current-owner memoization stays enabled in all.
- Complete frozen DebugStats and non-STATS Release compiler closures, common host, source hashes, environment and explicit loaded-module checks.
- Final-source relevant DebugStats/Release suites and SDK Roslyn-host checks for binding-only and combined. Existing direct 100/500-revision stress is not repeated without a retention change; the recorder has its own retained-recorder GC test.
- Causal Margin runs for all four variants, plus actual watched Text, scope and C#-compilation-changing negative controls, repeated in the recovery run.
- A completed combined session with 100 measured supported edits and two warmups, followed by undo/redo, unsupported edits, a C# change, a diagnostic failure/runtime check/repair and a subsequent supported edit. A baseline diagnostic session also completed.
- Three independently restarted production rounds, different variant orders, two warmups and twenty measured updates per variant for both Margin and Text scenarios: 480 measured production samples across 24 completed sessions.

`summary.csv`, `samples.csv`, `stage-observations.csv`, `comparisons.csv` and `measured-summary.md` are generated only from completed accepted runs. The summary retains medians, means, p90/p95, ranges and standard deviations. Hierarchical bootstrap intervals resample independent rounds and then samples within selected sessions; individual edits across variants are not described as paired observations. Three sessions provide limited uncertainty resolution.

Compiler managed heap, GC counts, working set and actual current operation/model/compilation/tree roots are available in causal observations. The stress configuration explicitly forces collections outside production timing. Production application allocations and post-GC heap are separately observed; they are not compiler allocation or retention measurements. Production compiler managed allocations/post-GC heap are unavailable here. Launcher working set is labeled separately from the instrumented compiler/watch host. Working-set growth alone is not a managed leak, and Roslyn/EnC may legitimately retain compiler history independently of this latest-only store.

## Engineering decision

No default flag is changed. The [review](recovery/README.md#results-and-engineering-decision) confirms real semantic hits and skipped Roslyn work in actual watched generations, but does not establish a statistically distinguishable end-to-end improvement. Binding-only and combined remain restricted opt-in; projection-only is inconclusive as a live performance candidate. Compiler/watch heap grew during stress even though the latest store's semantic entries and operation roots remained bounded; the report separates these observations and does not claim that whole-process retention has been attributed. No semantic-cache redesign, generated-C# delta minimization, commit, push or subsequent optimization was performed.
