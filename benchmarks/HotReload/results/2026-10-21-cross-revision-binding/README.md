# Cross-revision semantic binding investigation

Status: in progress. This archive starts from `af6c310a8859600f27943fd8cf972b8fcf296f0f` on `feature/improve-hot-reload`. The mandatory current-owner cache audit is running in the background. All eight experimental closures built and passed statistics ABI checks; real-project semantic preflights precede the 96 timing groups. No cross-revision expression cache has been implemented or accepted. Do not describe the instrumentation or audit variants as the requested semantic-reuse result.

## Confirmed starting point

The [previous investigation](../2026-10-19-akbura-fine-grained-incrementality/README.md) establishes complete fresh-driver parity and current-owner collectibility for the committed memoization, not a general responsiveness improvement. Its instrumented matrix has 27 slower and 13 faster medians, while its six production cells split three and three. The previous runs were sequential and do not prove that syntax equivalence, hashing, retention or another specific cache operation caused those differences.

BlackSilence already regenerates only the changed component for ordinary markup edits. A new semantic owner still performs first binding for unchanged expressions. The existing scoped cache neither crosses owners nor solves that remaining problem. Unchanged output and green syntax identity alone are not semantic proofs.

## Mandatory cache audit

Original pre-memoization and committed-current analyzer closures have been copied byte-for-byte into a separate ignored directory before instrumentation changes. `controls.json` records the three essential binary hashes for each control. Historical archives and their frozen tool directories are not modified. Every experiment uses the same existing benchmark host, evaluated inputs and fresh-driver oracle.

The audit compares:

| Variant | Behavior | Equality boundary |
| --- | --- | --- |
| Original | Frozen implementation preceding scoped memoization | Normal binding |
| Committed current | Frozen committed memoization | Full projection text, target reference, queried span and syntax equivalence |
| Rebuilt current | Same algorithm rebuilt with audit instrumentation compiled out in Release | Unchanged proof; controls for audit refactoring |
| Disabled | Current compiler with scoped memoization bypassed at build time | Normal binding; other current compiler behavior retained |
| Prehashed | Hash calculated once when constructing a key | The complete original equality proof remains; never hash-only equality |
| Repeat-admission | Prehashed key; retain a successful result only after a second exact successful request | The complete original equality proof remains |

The latter two are experimental alternatives, not adopted optimizations. Repeat-admission still retains its first-request keys and can lose savings for inputs requested exactly twice. Disabled still constructs the ordinary current context, including its empty dictionary. These costs must not be hidden when comparing to the original frozen implementation.

New STATS-only scopes separate prepared-projection construction, ToFullString, key hashing, equality, AreEquivalent, dictionary lookup, actual factory binding and publication. Counters record hash/equality/syntax calls, rejected comparisons, successful entry insertion and admission-only requests. These are nested inclusive observations with instrumentation overhead, not additive costs. No per-node logging is enabled by this audit. Normal Release builds contain no new timing call sites.

Factory-binding time measures executed work. The audit does not pretend to time an operation that was skipped on a hit. Disabled and cached runs provide the comparison. Rejected comparisons include adversarial equal-text/different-syntax collisions; a zero collision count in a real workload would not prove universal collision immunity. Entry-publication counters count insertions during an update, not a process-wide live-entry census. Post-GC heaps include all legitimate current-driver ownership and are not isolated retained-entry sizes.

The short mandatory checks cover real Counter initial/edit/fresh parity on every new binary before timing. New focused correctness checks verify exact-entry publication, instrumentation counts, concurrent same-owner requests without a single-flight assumption, and equal-hash/non-equivalent syntax. Broader cross-revision correctness, 100/500 revisions and complete final suites remain required after a supported reuse path exists; previous suite outcomes are not relabeled as new executions.

## Audit workload and order

Six real component/edit cells are used: literal edits in Counter, DataTemplatesPage, NeedHelp and Sidebar, plus a sibling-property edit alongside the existing live expressions in DataTemplates and NavButton. No displayed raw-string example or synthetic expression in a static label is used. These are an audit subset, not the final full ten-category cross-revision matrix.

Each group has an initial generation, two warm-ups and eight measured updates, with complete fresh source/diagnostic parity after every revision. The four current-code variants have a DebugStats profiling pass. Production has at least two independent rounds across all six variants, reversing variant order in the second round. Rebuilt-current is retained as an explicit control rather than assuming refactoring is free. All slower cells remain in the results.

The existing resumable timing controller freezes each complete closure, checks evaluated source/reference input hashes, waits for other builds/tests and discards contaminated partial timing groups. Functional checks may overlap unrelated work; accepted timing groups may not. Every completed group is reused on resume, and unsuccessful partial evidence is retained. The outer audit journal saves intermediate progress and stops on non-interference failures.

## Reproduction and progress

The machine-local frozen controls are prerequisites; they must not be rebuilt from the new source. From the repository root:

```powershell
pwsh -NoProfile -File benchmarks/HotReload/Start-ProbeCacheAudit.ps1
Get-Content bin/CrossRevision/audit-validated/journal.log -Tail 20 -Wait
Get-Content bin/CrossRevision/audit-validated/status.json
```

The launcher returns a hidden controller PID. Re-running the command resumes matching source fingerprints and successful groups. A changed source fingerprint requires a new output directory with separately frozen controls. The generated audit summary is descriptive evidence, not an automatic keep/restrict/replace/remove decision.

### Preparation verification and interrupted launch

The initial focused DebugStats verification passed 14/14 cases, including publication counts, exact-key collisions and concurrent requests. Six separate synthetic report-guard fixtures exercise acceptance of complete records and rejection of altered sources, altered diagnostics, duplicate sample indices, missing parity and wrong profiling modes. An additional guard checks discarded stream-copy task results, for seven harness checks in total. Their fabricated records are harness unit-test inputs, not executed generator or timing evidence.

The first controller launch stopped before any timing group. PowerShell emitted the stream-copy tasks' VoidTaskResult objects into the Freeze function's output, producing an invalid tool path. Both await results are now explicitly discarded, and Freeze must return exactly one existing tool path. The actual new statistics ABI check passed independently (60 counters and 84 stages). The failed launcher/source manifest/logs are retained under unsuccessful/audit-launch. The restarted run uses a new audit-validated directory and fresh fingerprint; frozen original/current controls remain byte-identical. This was a launcher-path failure, not a source-generator or semantic-parity failure.

## Cross-revision decision gate

Do not add another persistent expression cache before reviewing this audit. The current parser, binding dependencies, detached-artifact publication and planner consumers must then establish a supported region representation. Old syntax, symbols, operations, semantic models, binders and plans may not enter detached artifacts. Complete current semantic output and diagnostic locations must be reconstructed, not replaced by a cache-hit counter while equivalent binding still executes.

The final accepted implementation must show a real unchanged expression avoiding its first redundant binding in a new owner, current-context invalidation and fresh-driver parity, bounded detached ownership, collectibility, and a repeatable production resource benefit on a real component exercising that path. No such result is claimed in this initial archive. No merge, rebase, push, automatic commit or subsequent roadmap stage is authorized by the controller.
