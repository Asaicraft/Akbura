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
- `TwentyUpdates_PreserveStatesAcrossInitializerEditsAndReordering`: twenty actual
  deltas retain state objects and edited values while initializers, declaration
  order and UI text change; the original initializer runs only once.
- `TypeChangeRenameAndRemoval_ReconcileTheSameLiveComponent`: changing a state's
  type, renaming it and deleting it create or remove the expected state entries
  without replacing the retained counter or live component.

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

Add a scenario as a `[Fact]` with `[Trait("Category", "ClrHotReload")]`, delegate
the parent run to `HotReloadChildProcess`, and perform the scenario on the
headless dispatcher in the child. Keep the original component and state references
so assertions compare live identities, not only values.

API references: [Roslyn semantic edits](https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.emit.semanticedit),
[CLR delta application](https://learn.microsoft.com/en-us/dotnet/api/system.reflection.metadata.metadataupdater.applyupdate?view=net-10.0).
