# Real CLR Hot Reload integration tests

These tests run the production `AkburaBlackSilenceGenerator`, emit Portable PDBs,
and apply Roslyn `EmitDifference` output to the original loaded assembly with
`MetadataUpdater.ApplyUpdate`. They do not replace the assembly or component with
an instance compiled from a later revision.

Run this project independently of the fast unit tests with the .NET 10 SDK:

```sh
dotnet test src/Akbura.HotReloadIntegrationTests/Akbura.HotReloadIntegrationTests.csproj -c Debug
```

Each test starts an isolated `dotnet vstest` child with
`DOTNET_MODIFIABLE_ASSEMBLIES=debug`. The parent verifies that its exact test was
executed and passed; unsupported metadata updates fail rather than skip. Children
have a three-minute timeout and return their output on failure. No environment
changes or running development application are required.

## Scenarios

- `StateSurvivesRealHotReload`: adding `visible` preserves the component, assembly,
  `State<int>` instance and value `42`, creates the new state, updates the existing
  UI element, and leaves both states reactive.
- `EmptyComponent_AddsItsFirstOrdinaryAndHookStatesToTheSameOwner`: adding the first
  states to a previously empty component rebuilds the cached snapshot and keeps
  the existing UI owner and current root element reactive.
- `TwentyUpdates_PreserveStatesAcrossInitializerEditsAndReordering`: twenty actual
  deltas retain state objects and edited values while initializers, declaration
  order and UI text change; the original initializer runs only once.
- `TypeChangeRenameAndRemoval_ReconcileTheSameLiveComponent`: changing a state's
  type, renaming it and deleting it create or remove the expected state entries
  without replacing the retained counter or live component.
- `HookAdditionChangesAndRemoval_PreserveTheOwnerAndOrdinaryState`: adding chained
  composed hooks, changing a projection and removing hooks retain the ordinary
  counter and UI owner, rebind hook states and detach the previous aliases.
- `FailedFrames_RestoreSnapshotsAndRetryRealHotReloadWithFreshHookAliases`: a
  failed ordinary render restores the exact committed snapshot; a failed CLR
  refresh discards provisional hook aliases and can retry against the updated IL.
- `StateInfoFactories_UseUpdatedInitializersForNewOwnersWithoutStaticFields`:
  repeated initializer updates preserve the original state and its metadata while
  new owners receive the latest initializer and their own `StateInfo` instance.
- `DescriptorManifest_IsCapturedBeforeFirstOwnerAndTracksRepeatedReloads`:
  accessing a static descriptor captures the initial property manifest before any
  owner exists; removal, addition and re-addition reconcile Avalonia registrations
  while preserving retained properties and the live owner.
- `TwentyObservableHookReloads_KeepOneSubscriptionAndRejectStaleCallbacks`:
  twenty real updates and declaration reorders keep exactly one active source
  subscription and bounded state-store entries; removal, re-addition and visual
  detach/reattach dispose subscriptions exactly once and ignore stale callbacks.
- `FailedHookPreparation_DiscardsPartialAliasesAndRetriesTheSameDelta`: a hook
  factory failure after an earlier alias is staged aborts the runtime transaction
  without activating provisional subscriptions; retry uses the already applied IL.
- `NewDeltaAfterFailedReload_PrunesProvisionalOrdinaryBindingResources`: a binding
  created by an aborted reload is removed by a later delta, loses its subscription
  and owner, cannot trigger the old component, and is not reused on re-addition.
- `DetachedOwner_CatchesUpWithLatestStateShapeAfterMultipleDeltas`: an existing
  detached owner skips intermediate state/hook shapes, applies the latest one on
  reattachment, preserves ordinary values and resumes only current subscriptions.
- `HookTypeChangeRenameAndReorder_ReleaseOldAliasesAndKeepOrdinaryValue`: an
  observable `State<int>` becomes a derived `State<string>`, is renamed and
  reordered around an independent ordinary state; retired aliases cannot notify the
  owner, while the retained counter and current hook remain reactive.

## State compatibility matrix

| Contract | Coverage |
| --- | --- |
| State addition, including an initially empty owner | `StateSurvivesRealHotReload`, `EmptyComponent_AddsItsFirstOrdinaryAndHookStatesToTheSameOwner` |
| Removal, rename and type changes | `TypeChangeRenameAndRemoval_ReconcileTheSameLiveComponent`, `HookTypeChangeRenameAndReorder_ReleaseOldAliasesAndKeepOrdinaryValue`; retired states lose ownership |
| Initializer edits and declaration order | `TwentyUpdates_PreserveStatesAcrossInitializerEditsAndReordering`, `StateInfoFactories_UseUpdatedInitializersForNewOwnersWithoutStaticFields` |
| Hook changes and chained state aliases | `HookAdditionChangesAndRemoval_PreserveTheOwnerAndOrdinaryState` |
| Failed preparation, failed rendering and retry | `FailedHookPreparation_DiscardsPartialAliasesAndRetriesTheSameDelta`, `FailedFrames_RestoreSnapshotsAndRetryRealHotReloadWithFreshHookAliases` |
| Another delta after an aborted reload | `NewDeltaAfterFailedReload_PrunesProvisionalOrdinaryBindingResources` |
| Repeated updates, resource counts and stale callbacks | `TwentyObservableHookReloads_KeepOneSubscriptionAndRejectStaleCallbacks` |
| Detached owners and missed revisions | `DetachedOwner_CatchesUpWithLatestStateShapeAfterMultipleDeltas` |
| Release generation retains direct fields and lifecycle behavior | Unit `StateWriterTests` and both variants of `ComposableHookIntegrationTests` |
| Debug components can use a Release-built runtime | Run this CLR project with `-c Release`; generated components still define `DEBUG` |

Reordering scenarios keep hook dependencies declared before their consumers.
They check state identity across valid edits, not forward-declaration support.

Debug components store both hook aliases and their ordered snapshots in the
runtime `StateCollection`. `BeginFrame`, `CommitFrame` and `AbortFrame` stage these
bindings without generated instance fields. Hot Reload retains the old snapshot
until a frame commits so removed states and their resources are reconciled only
after new hooks have been prepared. Release components keep the direct field path.
Removed ordinary states are detached from their owner as well as suspended and
pruned, including states created in an aborted frame and absent from the last
committed snapshot. Keeping an external reference to such a state must not let
later value changes schedule an update of the former owner.

Debug state accessors create `StateInfo` through generated methods only when a
new ordinary state is needed. They have no `s_stateInfo_*` fields; Release keeps
its static readonly metadata. Last-applied descriptor/state shapes and the property
manifest live in a runtime `ConditionalWeakTable<Type, AkburaHotReloadMetadata>`.
The existing service-array initializer registers this metadata after the generated
descriptors have initialized, without adding a field or a static constructor to
the component. Descriptor rebuilds publish the new shape and manifest only after
all descriptor and array assignments succeed.

## Harness and boundaries

`AkburaCompilationSession` keeps the same incremental driver and replaces the
`.akbura` additional text for every revision. `ClrHotReloadSession` retains PE/PDB
metadata and advances its `EmitBaseline` after each successful update. It matches
members of the component type by signature, emits inserts, updates and deletions
of methods/properties, and uses initial PDB EnC maps and syntax maps to preserve
existing lambdas. All retained component methods/accessors are recompiled: changing
a state's type can change the bound getter call even if the source text is identical.
Deleted fields remain in CLR metadata; Akbura's reconciliation
removes their runtime state registrations.

The harness explicitly dispatches the generated `MetadataUpdateHandler`
`ClearCache` and `UpdateApplication` phases on Avalonia's UI thread **after** the
CLR delta is applied. The first test checks that UI text remains unchanged between
these two operations, then changes through the actual Akbura handler. It does not
invoke the reconciliation callback directly.

This verifies CLR delta application and Akbura's generated/runtime contract.
It does not verify file watching, IDE edit classification, active statements,
or the SDK's automatic handler discovery. A `dotnet watch` end-to-end fixture is
a separate layer of coverage. The current harness handles one existing component
type with fixed code-behind, not arbitrary edits across a whole solution.

A separate property-reconciliation gap was observed while testing an empty
component: changing `Text="empty"` to an expression using newly added states left
the live `TextBlock.Text` null, although the state snapshot was initialized.
The empty-component state test starts with `Text={EmptyTextForTest()}` to keep the
property's assignment kind stable. Constant-to-expression property transitions
need their own regression test and fix outside the state-storage migration.

Add a scenario as a `[Fact]` with `[Trait("Category", "ClrHotReload")]`, delegate
the parent run to `HotReloadChildProcess`, and perform the scenario on the
headless dispatcher in the child. Keep the original component and state references
so assertions compare live identities, not only values.

API references: [Roslyn semantic edits](https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.emit.semanticedit),
[CLR delta application](https://learn.microsoft.com/en-us/dotnet/api/system.reflection.metadata.metadataupdater.applyupdate?view=net-10.0).
