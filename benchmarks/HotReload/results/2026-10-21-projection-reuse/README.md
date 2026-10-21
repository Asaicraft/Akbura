# Cross-revision projection reuse

Status: **projection-only experiment reviewed; restricted and disabled by default**. Correctness/ownership checks passed, but the production results do not justify unconditional adoption. Cross-revision semantic-result reuse is **not implemented**. This is an intermediate engineering record, not final acceptance of the complete requested optimization.

## Reproducibility

Starting revision: `6628ac9eee2d8322ec0193c97a31f1efc645a745`. The implementation is currently uncommitted. `sources.json`, emitted by the controller, identifies its exact source fingerprint; `baseline.json` identifies the frozen pre-change binaries. No implementation commit hash is claimed before a commit exists.

Run [Verify-ProjectionReuse.ps1](../../Verify-ProjectionReuse.ps1) from the repository root. The controller runs detached with durable UTC journal entries, process IDs, flushed child logs, atomic status files and successful-step markers. Invoke the same command to resume unchanged sources after interruption. Source or binary changes require a new output directory. Foreign build/test activity is allowed for functional checks; timing cells wait for a quiet host, and contaminated partial cells are preserved and retried by the existing benchmark controller.

Both variants preserve the existing current-document expression-result cache. The baseline does not disable that cache. The candidate is selected with `-p:ProbeReuseStage=projection`; default builds do not enable the new store. Candidate closures replace **both** Generator and BlackSilence assemblies, retaining the same benchmark host and runtime dependencies as the baseline. Instrumented and non-STATS Release binaries are kept separately.

The completed matrix covers Counter, DataTemplates, DataTemplatesPage, Sidebar, NeedHelp, NavButton and [a focused two-binding fixture](../../ProjectionFixture/Siblings.akbura). It contains 42 timing cells: 14 instrumented cells and two independent, reverse-ordered Release rounds of 14 cells each. Each cell uses two warmup updates, eight measured updates and fresh source/diagnostic comparisons for all eleven initial/updated generations: **336 measured updates and 462 successful fresh-parity comparisons**. The controller ran from 07:34:50 to 08:57:30 UTC on 2026-10-21, approximately 82 minutes 40 seconds.

Measured results are in [the generated summary](projection-measured-summary.md), [cell distributions and GC activity](projection-cells.csv), [individual samples](projection-samples.csv), and [causal stages/counters](projection-stages.csv). Portable raw evidence is under `runs/`; the executed relevant-suite result is [projection-gate.trx](tests/projection-gate.trx). `postprocessing.json` records the subsequent locale-independent report regeneration without rebuilding or rerunning measurements. The original source manifest and binary-closure hashes remain intact.

## Ownership and correctness contract

```text
BlackSilenceProjectState (one exact CSharpCompilation instance)
  -> CompilationBoundProbeStore
     -> latest immutable generation
        -> scalar declaration/document contracts
        -> prepared Roslyn C# syntax + exact expected-type identity
        -> weak source-green identity (instrumentation only)

Private generation attempt -> previous published generation, temporarily
Seal/discard -> releases that temporary edge and closes document sessions

BoundedArtifactStore -X-> compilation-bound probe store
New project state -X-> previous state/store
Published probe generation -X-> previous generation / markup semantic owner
```

The store lives in the lower Generator layer and is owned by the existing BlackSilence compilation-provider state. It is not static and does not enter detached artifacts. A different `CSharpCompilation` instance requires a different state/store. Current semantic-model compatibility rules are unchanged.

The initial matching algorithm is deliberately conservative: exact document text outside same-width, quoted numeric `Margin` literals, with exact scalar declarations for **all** project documents, root namespace, project directory, document identities, expression source position/kind, full expression text/structure and expected-type reference identity. All scope text and absolute positions stay unchanged. Green identity is recorded separately and is never sufficient for reuse. Literal nodes include trailing whitespace in this parser; their full width is checked as well.

Changed expressions, declarations, imports/aliases, local/foreach/template/named-element context, other sibling text or source-width changes miss. Recovery, skipped syntax, incoming C# annotations/directives and unsupported syntax fail closed. This is not general unchanged-region matching and does not optimize arbitrary styling edits or source-span shifts.

A hit bypasses the actual `CreateReturnExpressionProbe()` call. Normal current-model binding, probe compilation/model acquisition, operation rewriting, dependency collection and diagnostics still execute. Old Akbura bound nodes, binders, semantic models and plans are not cached. No old `CSharpBindingResult` or `CSharpOperationDefinition` is reused. Consequently no dependency-import shortcut or diagnostic-location reuse is needed at this step.

Publication uses the existing project snapshot version check and publication lock, after generation/dependency capture succeeds and cancellation is checked. Error-diagnostic generations discard the private probe candidate. Older branch completions cannot replace newer state publications. Sealing carries unchanged clean documents only under the full declaration contract and exact source shape; changed documents retain only observed current entries. Removed documents and superseded entries are dropped.

Provisional deterministic ceilings are 128 entries/document, 1,024 entries/project, 32,768 characters/projection and 2,097,152 total retained projection characters. These are experimental safety ceilings, **not measured optimal limits**. Rejection counts, retained-character publication totals, post-GC heap and net production cost must inform the acceptance decision. Text character counts do not measure the entire retained Roslyn object graph.

## Instrumentation and tests

Change-range instrumentation distinguishes whole-document ranges from precise `SourceText.WithChanges` ranges. Existing parser subtree-reuse counters remain separate. New matching, lookup, actual construction-skipped, publication, context-rejection and size-rejection statistics explain the candidate. Inclusive stage times are not additive exclusive costs.

The expanded relevant DebugStats suite passed **86/86 tests**, with no final failures or skips, in 7 minutes 5 seconds. It covers generated-source/diagnostic parity for repeated edits and undo/redo, expected-type/query-region invalidation, compilation/declaration rejection, private-candidate cancellation, latest-only publication, document deletion, precise/whole parse ranges and live-store 100/500-revision collectibility of obsolete markup models, sessions, trees and operations. Real project-state/C#-transition tests also passed for both 100 and 500 revisions, including fresh-generation comparisons and collectibility of obsolete project states, stores, C# compilations and snapshots after the old owner is released. These results establish ownership for the projection-only graph; they do not establish safety for semantic graphs that are not cached by this implementation. Full SDK-host/Release suites and live `dotnet watch` verification remain pending for the eventual complete implementation.

The summarizer's synthetic fixture guards passed for nontrivial stage-count sums, medians, complete matrices, fresh-parity failures, unfinished runs and missing heap observations. Additional guards require byte-identical CSV/Markdown output under `en-US` and `ru-RU`, including fractional numeric values. Synthetic guard data is never used as benchmark evidence. An earlier development test failure exposed trailing whitespace inside literal nodes; this was corrected before the frozen benchmark binaries and the final passing suite were produced.

## Measured outcome and gate decision

The fast path genuinely bypasses projection construction. Instrumented totals below cover eight measured updates per component; they are not a claim that semantic binding was bypassed.

| Component | Baseline constructions | Candidate constructions | Actual construction skips | First bindings, baseline / candidate |
| --- | ---: | ---: | ---: | ---: |
| Counter | 136 | 80 | 56 | 136 / 136 |
| DataTemplates | 609 | 280 | 327 | 288 / 288 |
| DataTemplatesPage | 2,752 | 2,008 | 744 | 1,120 / 1,120 |
| Sidebar | 1,984 | 960 | 1,024 | 1,448 / 1,448 |
| NeedHelp | 528 | 296 | 232 | 432 / 432 |
| NavButton | 448 | 184 | 264 | 280 / 280 |
| Focused siblings | 24 | 8 | 16 | 24 / 24 |

The DataTemplates construction-count difference exceeds its explicit skip counter by two calls; raw call counts can differ outside the new fast path. Only the explicit skipped-construction branch is credited as avoided work. First expression factory binding counts are identical in every paired instrumented cell. Probe compilation/model work is not intentionally bypassed. DataTemplatesPage's total probe compilation count varies from 3,368 to 3,336 despite unchanged first expression binding counts; that difference is not credited as cross-revision semantic-result reuse.

Counter is faster by 1.78% and 0.83%, and NeedHelp by 0.64% and 1.94%, in the two Release rounds. These small paired changes are not a statistical confidence claim. Other workloads do not show consistent net wins: Sidebar changes from a 3.37% improvement to a 0.09% regression; DataTemplates is **3.38% and 2.54% slower**; the focused fixture changes from a 0.78% improvement to a 2.21% regression. The full distribution is retained instead of selecting only favorable cells or presenting one aggregate percentage as a general speedup.

Post-GC heap medians increase by approximately 0.00%-1.24% across the paired Release cells. Allocation medians decrease by up to 3.29%, but that does not compensate for an inconsistent elapsed-time result. Instrumented publication totals imply 567 current project entries and 310,988 retained projection characters for the gallery cells, 251 entries and 197,185 characters for Purity, and two entries / 276 characters for the focused fixture. Those are whole-project store counts, not the edited component's count or full retained-graph bytes. No projection hit the per-entry size rejection ceiling in these instrumented cells. The Sidebar skip count reaches 128 per measured update, consistent with the deliberately capped admission policy; the cap is not proven optimal.

**Decision:** do not enable the projection-only candidate by default or advertise a general Hot Reload speedup. Retain it only as an opt-in, tested experiment. The unconditional production-performance gate is not accepted. Before semantic-result reuse proceeds, the remaining preparation/publication cost needs an explicit acceptance decision or a further measured improvement; correctness and a reduced construction counter alone are insufficient. No semantic-result implementation or follow-on benchmark was silently started after this gate.

## Research grounding

[Roslyn green/red trees](https://raw.githubusercontent.com/dotnet/roslyn/main/docs/compilers/Design/Red-Green%20Trees.md) motivate separating position-independent structure from contextual owners. [Roslyn incremental parsing](https://raw.githubusercontent.com/dotnet/roslyn/main/docs/compilers/Design/Incremental%20Parser.md) informs structural reuse; this candidate does not replace the parser or treat shared greens as a semantic contract.

[Roslyn CompilationCache](https://raw.githubusercontent.com/dotnet/roslyn/main/src/Compilers/Core/Portable/SourceGeneration/CompilationCache.cs) uses exact input identity. [SemanticModel ownership](https://raw.githubusercontent.com/dotnet/roslyn/main/src/Compilers/Core/Portable/Compilation/SemanticModel.cs) motivates keeping semantic graphs under their compilation owner. [TransformNode](https://raw.githubusercontent.com/dotnet/roslyn/main/src/Compilers/Core/Portable/SourceGeneration/Nodes/TransformNode.cs) and [NodeStateTable](https://raw.githubusercontent.com/dotnet/roslyn/main/src/Compilers/Core/Portable/SourceGeneration/Nodes/NodeStateTable.cs) distinguish cached input from transformed/equivalent output. No additional output-stabilization layer has been added without demonstrated redundant downstream work.

[HotAvalonia's control manager](https://raw.githubusercontent.com/Kira-NT/HotAvalonia/master/src/HotAvalonia.Core/AvaloniaControlManager.cs) provides a useful ownership comparison, not an architecture to copy. Its runtime dynamic-XAML compilation model is not introduced here. Runtime reconciliation/state preservation and generated-C# delta minimization are unchanged.

## Current acceptance status

| Area | Current classification |
| --- | --- |
| Region matching | Restricted to the exact contract above |
| Projection correctness and ownership | Accepted for the tested projection-only graph |
| Projection reuse | Restricted: opt-in only; unconditional production gate not accepted |
| Cross-revision semantic binding | Incomplete: not implemented |
| Full compiler/runtime acceptance | Incomplete |

Only after projection correctness, retention and repeatable production usefulness are established should the next step audit and admit compilation-bound semantic results, per-probe dependency contributions and source-location handling. A projection hit must never be reported as skipped first semantic binding, skipped probe compilation or skipped probe SemanticModel acquisition.
