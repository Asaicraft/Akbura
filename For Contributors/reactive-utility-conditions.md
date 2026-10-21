# Reactive expression conditions for AKCSS utilities

`${x.bind expression}:utility` observes a C# Boolean expression and changes the
utility's participation in the existing AKCSS cascade. It does not request a
component update. For example:

```akbura
using Akbura.Styles.akcss;
state double width = 800;
<TextBlock ${x.bind width > 1000}:hidden/>
```

Ordinary `{expression}:utility` remains frame-evaluated. `${hover}:utility`
continues to use the existing markup-extension variant contract. Neither form
is converted into an expression binding.

## Syntax and semantics

`TailwindExpressionBindingConditionalPrefixSyntax` contains the existing
`MarkupExpressionBindingSyntax` and a colon token. The binding retains its
embedded `CSharpExpressionSyntax`, delimiters and trivia. Full and incremental
parsers share the existing expression-binding parsing entry points. The new
kind participates in Red/Green factories, visitors, rewriters and update helpers.

The binder uses the normal scoped C# probe with `bool` as the contextual type.
There is no truthiness conversion: integers, strings and nullable Booleans need
an explicit Boolean expression. Existing expression diagnostics are mapped back
to the Akbura source. The utility operation retains `ConditionExpressionBinding`
separately from the legacy condition and markup-extension variant.

`ExpressionBindingDependencyAnalyzer` and
`ExpressionBindingWriter.TrackingRewriter` are shared with property bindings.
There is no additional path interpreter or tracker. Short-circuit evaluation,
conditional access, indexers, nested dependencies and source ordering keep their
normal expression-binding semantics.

## Generated evaluator and cascade

An abbreviated generated evaluator for the example above is:

```csharp
new AkcssUtilityCandidateActivator(
    conflictKey: "hidden",
    sourceOrder: 0,
    applications: applications,
    reactiveCondition:
        ((Func<Owner, Func<ExpressionBindingContext, bool>>)(static owner =>
            (ExpressionBindingContext tracker) =>
                tracker.State(owner.__State_width_generatedName, 0) > 1000))(this));
```

The actual state accessor uses the generated member identity. Stable callbacks
have an explicit owner capture boundary so neighboring iteration/local lambdas
cannot make them retain an obsolete item. Render-local, foreach/template and
named-element callbacks are refreshed against their current receivers on an
ordinary frame using `RefreshReactiveCondition`. Notification-driven evaluation
works between frames and does not run that refresh method.

The candidate owns `ExpressionBinding<bool>` and its cached condition result.
The first value is computed before cascade activation. Later result changes call
the existing candidate change callback and targeted
`AkcssRuntime.TargetRuntime.RequestApplyCandidate` path. `IsActive` reads the
cached value; it does not poll or re-evaluate the expression. Utilities use their
ordinary operation contributions, priorities, conflict keys and restoration
rules rather than directly assigning target properties.

## Lifetime and Hot Reload

Detach disposes the condition binding and releases observations even when other
cleanup fails. Reattachment creates a new binding and reads current sources.
Detached callback refreshes store the new evaluator without restarting observers.
Candidate replacement/removal uses existing AKCSS and render-operation ownership.

After a successful render-revision commit, nodes discard their previous-operation
lookup snapshots. Those snapshots remain available during preparation/rollback;
keeping them after commit would retain retired candidate delegates and old keyed
items despite having removed their event subscriptions.

Condition/utility edits participate in existing syntax operation identities and
structural AKCSS reconciliation. No separate Hot Reload coordinator is added.
Tests cover unchanged conditions with unrelated edits, changed expressions,
changed utilities, removal and element replacement on the same loaded CLR owner.
Runtime construction is covered in ReleaseDirect, DebugStructural and
ReleaseConditional generation modes.

## Workspaces

The dedicated prefix exposes the same embedded C# expression as property bindings.
The compiler provides Boolean contextual typing and collects real C# references
from the condition. Shared Workspaces projections supply member completion,
classification, references/rename, definition, hover and source diagnostics;
there are no IDE-specific semantic implementations.

## Verification and boundaries

`ReactiveUtilityConditionSyntaxTests`, `ReactiveUtilityConditionSemanticTests`,
`ReactiveUtilityCandidateRuntimeTests` and
`ReactiveUtilityConditionGenerationTests` construct/parse, bind, compile and run
real generated components. The state regression holds the existing
`SuppressUpdates()` scope while changing 800 -> 1100 -> 1400 -> 800 -> 1100:
the reactive TextBlock changes visibility immediately, its ordinary-condition
sibling does not, and the render counter is unchanged. Releasing the scope runs
the existing batched state update and updates the ordinary sibling.

This distinction is important: existing `state` notifications still request a
component render by design. Expression conditions neither require that render
nor disable it. INPC/Avalonia dependency tests change the UI without requesting
component renders at all. This feature does not optimize state scheduling.

`ConditionalClrHotReloadSmokeTests.ReactiveUtilityConditions_ApplyRealDeltasWithoutStaleSubscriptions`
uses an enabled child process, one initial assembly load and actual metadata/IL
deltas. Element-type replacement uses native `Name`, avoiding an unrelated public
typed `x.Name` getter signature change in the method-update-only CLR harness.
`WorkspaceExpressionBindingTests.ReactiveUtilities.cs` adds shared editor
coverage for the utility-prefix context.

Run `eng/Verify-ReactiveUtilityConditions.ps1 -Phase Generation` for focused
Debug/Release checks, `-Phase Regression` for incremental reuse and real CLR
condition-delta regressions, or `-Phase Full` for all five .NET test projects in both
configurations, Visual Studio builds, VS Code tests and the existing Windows
editor/VSIX verification. Each run records exact commands, changed files, source
hashes, TRX counts and console logs in its results directory. `DONE.txt` means
the runner stopped, not that all checks passed. Inspect `summary.csv`; skipped,
aborted, timed-out and zero-test steps are not success.

The existing expression-binding limitations remain: unsupported deferred/mutating
expressions are diagnosed; arbitrary method bodies and non-observable CLR members
cannot expose hidden dependencies. Partial-observation warnings are preserved.
Capturing a render local observes that local's current value, not the expression
that originally initialized the local. Color utilities still require their
normal `--color-*` resources. Native IDE UI automation is not claimed by shared
Workspaces tests or editor builds.
