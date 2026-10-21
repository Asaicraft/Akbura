# Cross-revision semantic binding investigation

Status: the current-owner timing audit is collected and reviewed; cross-revision implementation remains incomplete. This archive starts from `af6c310a8859600f27943fd8cf972b8fcf296f0f` on `feature/improve-hot-reload`. All eight experimental closures built and passed statistics ABI and real-project semantic preflight checks. All 96 timing groups completed, with 1,056 successful fresh-driver comparisons. The final report-generation failure was corrected without repeating any measurements. No cross-revision expression cache has been implemented or accepted. Do not describe the instrumentation or audit variants as the requested semantic-reuse result.

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

This recorded timing run has finished; do not restart it to repair its report. Its original controller status remains Failed at the summarize step as historical evidence. The corrected postprocessing outcome is recorded separately in bin/CrossRevision/audit-validated/postprocessing-status.json. The original sources.json and run manifests are not rewritten to make the changed summarizer appear to be the measured version.

### Preparation verification and interrupted launch

The initial focused DebugStats verification passed 14/14 cases, including publication counts, exact-key collisions and concurrent requests. Six separate synthetic report-guard fixtures exercise acceptance of complete records and rejection of altered sources, altered diagnostics, duplicate sample indices, missing parity and wrong profiling modes. An additional guard checks discarded stream-copy task results, for seven harness checks in total. Their fabricated records are harness unit-test inputs, not executed generator or timing evidence.

The first controller launch stopped before any timing group. PowerShell emitted the stream-copy tasks' VoidTaskResult objects into the Freeze function's output, producing an invalid tool path. Both await results are now explicitly discarded, and Freeze must return exactly one existing tool path. The actual new statistics ABI check passed independently (60 counters and 84 stages). The failed launcher/source manifest/logs are retained under unsuccessful/audit-launch. The restarted run uses a new audit-validated directory and fresh fingerprint; frozen original/current controls remain byte-identical. This was a launcher-path failure, not a source-generator or semantic-parity failure.

### Corrected stage-count summary

After all timing groups completed, the summarizer used `$group.Group.Count` where it needed the values of the CSV Count column. PowerShell resolves that expression to the row array's length (8), not eight individual stage invocation counts. Median therefore received one number and correctly rejected it. Every real sample group had 11 revisions, including eight measured edits; every present measured expression-stage group also had eight rows. No missing samples were filled, and the sample-count guard was not weakened.

The corrected script explicitly enumerates `$_.Count` for each row. Regression fixtures now contain nonempty stage data whose invocation-count median must be 23.5, rather than only an empty stage CSV. A missing-stage-row fixture must still fail. All nine report/launcher guards pass. The original failing summarizer, stderr and controller status are preserved under unsuccessful/audit-summary, together with pre-repair hashes of all 384 files in the portable measurement archive. Postprocessing verifies those files remain byte-identical and records the new summarizer/test hashes separately. No build, generator test suite or timing group was rerun for this repair.

## Reviewed scoped-cache result

The [generated measurements](audit-measured-summary.md), [per-cell data](audit-samples.csv) and [inclusive stage data](audit-stages.csv) contain all variants and both production rounds, including slower observations. All generated-source digests and complete diagnostic values also match between variants within each profiling configuration. The 96 cells contain 768 measured updates plus initial/warm-up revisions; 1,056 complete fresh-driver comparisons passed in total.

Production comparisons below use the same current compiler with its scoped cache disabled as the paired control. Negative changes mean less GeneratorDriver elapsed time with the committed cache. Each round is independent and has eight measured updates per cell; ranges remain in the generated tables.

| Real edit | Disabled -> committed cache, round 1 ms | Change | Disabled -> committed cache, round 2 ms | Change |
| --- | ---: | ---: | ---: | ---: |
| Counter literal | 208.03 -> 210.26 | +1.07% | 228.94 -> 209.52 | -8.48% |
| DataTemplates binding sibling | 468.64 -> 383.09 | -18.26% | 411.12 -> 384.60 | -6.45% |
| DataTemplatesPage literal | 3,601.05 -> 3,516.47 | -2.35% | 3,684.43 -> 3,515.72 | -4.58% |
| NavButton binding sibling | 134.59 -> 123.17 | -8.49% | 140.89 -> 121.17 | -14.00% |
| NeedHelp literal | 164.39 -> 171.53 | +4.34% | 175.79 -> 168.41 | -4.20% |
| Sidebar literal | 795.63 -> 574.92 | -27.74% | 650.93 -> 643.11 | -1.20% |

Four cells exercising real reuse have lower medians in both rounds; Counter and NeedHelp remain mixed. Counter has zero scoped-cache hits, so its timing changes do not demonstrate a cache benefit. Sidebar's effect size changes substantially between rounds. These observations do not establish statistical significance, uniform gains, or an explanation for every earlier regression. The original frozen pre-memoization control and rebuilt-current control are included in the full archive rather than substituted with these paired controls.

The direct syntax-equivalence-cost hypothesis is not supported as the dominant explanation for the earlier large regressions. For current-cache literal edits, measured inclusive AreEquivalent medians are 0 ms in Counter (zero calls), 0.6294 ms in DataTemplatesPage (204 calls), 0.0550 ms in NeedHelp (12 calls), and 0.3609 ms in Sidebar (67 calls). These STATS scopes include instrumentation overhead. They do not measure possible indirect retention effects or identify the cause of the remaining timing variation.

| Current-cache literal edit | Projection preparation ms | Projection text ms | Key hashing ms | Dictionary lookup ms | Actual factory binding ms |
| --- | ---: | ---: | ---: | ---: | ---: |
| Counter | 2.1091 | 0.1911 | 0.0262 | 0.0218 | 13.4274 |
| DataTemplatesPage | 15.2930 | 2.8638 | 0.2891 | 1.1348 | 109.7599 |
| NeedHelp | 4.4766 | 0.8774 | 0.1024 | 0.1543 | 66.8277 |
| Sidebar | 567.5421 | 3.5898 | 0.4018 | 0.7601 | 209.8845 |

Scopes are nested/inclusive and can accumulate across parallel work; never sum this table or equate it with wall time. Projection preparation remains necessary before the existing lookup, and is a substantial measured cost in Sidebar. With scoped caching disabled, actual factory-binding medians for these four cells are 12.0148, 234.4405, 70.9751 and 316.7636 ms respectively. Those are executed observations in separate runs, not exact per-hit predictions of avoided time.

Current-cache factory executions versus disabled executions are 17/17 in Counter, 140/344 in DataTemplatesPage, 54/66 in NeedHelp and 181/248 in Sidebar. The broader total-probe counter also includes unrelated binding paths: for example, Counter has 97 versus 93 median probe compilations despite identical scoped factory counts and no hits. Do not attribute all total-probe differences to this cache or infer necessary binding counts from hit counts alone.

In the two production rounds, current versus disabled allocations decrease by approximately 4.6-4.7% for DataTemplates, 2.3-2.4% for DataTemplatesPage, 12.3-12.7% for NavButton and 8.2-8.3% for Sidebar. Their active-driver post-GC heap medians increase by approximately 0.2-0.3%, 1.5-1.7%, 0.9% and 1.6-1.9% respectively. Heap values are not isolated retained-entry sizes. This audit does not replace the required final detached-owner GC proof or establish a zero-retention cost.

### Current-owner cache decision

Keep the established default unchanged for the next cross-revision investigation. Controlled production repetitions support useful existing reuse in several real components, while directly measured key/equivalence costs are small. There is no repeatable general improvement from prehashing or second-request admission that justifies adopting either experimental alternative. Second-request admission also reduces actual hit counts and repeats more Roslyn work in the profiled workloads. Neither alternative becomes the default.

This decision is not an unconditional responsiveness claim: preserve mixed Counter/NeedHelp results, retained-heap increases and variation between rounds. It does not validate any cross-owner result transfer. A final cross-revision candidate must still be compared against the committed current control and must demonstrate first-time binding avoidance on a real supported component.

## Cross-revision decision gate

Do not add another persistent expression cache before reviewing this audit. The current parser, binding dependencies, detached-artifact publication and planner consumers must then establish a supported region representation. Old syntax, symbols, operations, semantic models, binders and plans may not enter detached artifacts. Complete current semantic output and diagnostic locations must be reconstructed, not replaced by a cache-hit counter while equivalent binding still executes.

The final accepted implementation must show a real unchanged expression avoiding its first redundant binding in a new owner, current-context invalidation and fresh-driver parity, bounded detached ownership, collectibility, and a repeatable production resource benefit on a real component exercising that path. No such result is claimed in this initial archive. No merge, rebase, push, automatic commit or subsequent roadmap stage is authorized by the controller.
