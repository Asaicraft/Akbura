# AKCSS Hot Reload: Phase 1 verification

## Implementation

The baseline was `559208b` on `origin/master`. This work is on
`feature/improve-akcss-hot-reload`; it does not implement Phase 2.

Debug structural generation compares a cascade revision through the existing
`AkburaRenderState.ShouldApplyOwnedOperation` contract. An unchanged operation is
registered as retained, not omitted. Its activator array is constructed only when
the guard requires replacement. Changed definitions and changed target nodes
still replace the complete cascade. The original three-argument
`ApplyAkcssStylesOperation` API remains available with unconditional replacement.

`AkcssHotReloadIdentity` hashes emitted C# tokens with SHA-256. The input encodes
token kind and length-prefixed token value, excluding whitespace, comments, and
source-mapping trivia. It includes ordered activator definitions, references,
arguments, variants, conflict metadata, and binding priorities, but not live
reactive values. Each referenced source-module style/utility contributes a
separate revision returned by `__AkburaStyleRevision(index)`. That method hashes
the emitted style/utility implementation, including metadata attributes and
expanded transitive `@apply` operations. These are method literals, not mutable
values hidden behind static readonly initializers.

The existing module dependency graph and generated handler select affected
components for module-only changes. Current style instances still come from
`__AkburaGetStyle(index)`, including current utility operation arrays. The CLR
tests cover both transitive imports and insertion/removal that changes indexes.
Unreferenced style changes and formatting-only edits retain installed cascades.

Debug frame refresh reuses the runtime's conflict-key queues to refresh dynamic
arguments, variants, and frame conditions. It does not unconditionally reset and
execute static cascades. Normal refresh and ReleaseDirect/ReleaseConditional
generation retain their previous paths.
Detached targets are not refreshed or resubscribed; visual reattachment installs
their sources through the existing attachment lifecycle.

`ClearCache` forwards the CLR's updated types to targeted observed-property name
invalidation. Nested styles, updated module scopes, and inherited attributes on
derived styles are covered; unrelated cache entries survive. A null update list
clears the metadata cache. Reflection lookup and invalidation are synchronized
so a concurrent lookup cannot reinsert stale metadata after invalidation.
Application refresh remains dispatched through Avalonia's UI thread.

Each observable argument attachment now has an observer that is deactivated
before subscription disposal. A retired observer ignores late values/errors
without calling an old converter and releases its reference to the value source.
This also protects detach/reattach of the same activator. Unchanged cascades do
not allocate new observers because they do not attach again.

## Important files

- Generator: `AkcssHotReloadIdentity.cs`, `AkcssModuleWriter.cs`,
  `AkcssDocumentWriter.cs`, `AkcssActivatorWriter.cs`, `ComponentScopeWriter.cs`.
- Runtime: `AkburaRenderState.cs`, `AkburaControl.cs`, `AkcssRuntime.cs`,
  `AkcssUtilityCandidateActivator.cs`, `AkcssUtilityValueSource.cs`.
- Cache lifecycle: `AkcssObservedPropertySignal.cs`,
  `AkburaHotReloadRuntime.cs`, `HotReloadServiceWriter.cs`.
- Tests: `AkcssHotReloadTests.cs`, `AkburaCompilationSession.cs`,
  `ClrHotReloadSession.cs`, revision/cache/ownership/runtime/writer unit tests.

## Real CLR scenarios

All integration cases use the production incremental generator, real
`EmitDifference` and `MetadataUpdater.ApplyUpdate`, the original assembly,
existing live controls, and generated handlers on Avalonia's UI thread.
Unavailable CLR Hot Reload fails the isolated child process; it is not skipped.

| Scenario | Verified behavior |
| --- | --- |
| A: unrelated component markup | Same controls and activators, same subscriptions, no extra reset/execute marker writes; reactive values still update. |
| B: utility definition | Same control reflects changed utility code and subsequent observable values. |
| C: module-only delta | Component is absent from the actual changed-type list; updated module/nested types refresh the existing component. |
| D: declaration removal | Removed utility releases its subscription and Width; unrelated class styling remains; removing the remaining class clears Height and the owned cascade. Late callbacks are inert. |
| E: 20 consecutive module deltas | Stable controls, one active subscription, one replacement/disposal per delta, no stale callback effects or growth of the installed activator array. |
| F: observed-property metadata | Real attribute delta changes Width to Height; reflection reports Height, Width stops causing updates, Height remains active. |
| G: failed refresh and retry | One-shot source attachment failure restores the previous owned activator array and active subscription; a later valid delta updates successfully. |
| Unreferenced style and reattachment | Unchanged cascade survives module edits and detach/reattach; a changed module while detached is applied on reattachment, and retired subscriptions stay inert. |
| Transitive import | Changing a base `@apply` declaration refreshes the dependent cascade. |
| Style indexes | Insert/remove declarations before the used style; current factories select the right definition. |
| Formatting only | Real source-mapping delta retains the cascade and its subscriptions. |

The harness updates source types and nested types and supplies types resolved
from `EmitDifferenceResult.ChangedTypes`, including compiler-generated types,
rather than assuming every delta updates the component.

## Validation

Environment: Windows, .NET SDK 10.0.401, CLR 10.0.12, repository Roslyn 4.11.0.

```powershell
dotnet test src/Akbura.UnitTests/Akbura.UnitTests.csproj --no-restore --filter "FullyQualifiedName~Akcss|FullyQualifiedName~AkburaRenderState|FullyQualifiedName~AkburaHotReload|FullyQualifiedName~ComponentDocumentWriter|FullyQualifiedName~HotReloadServiceWriter" --logger "console;verbosity=minimal" -m:1 /nodeReuse:false
dotnet test src/Akbura.HotReloadIntegrationTests/Akbura.HotReloadIntegrationTests.csproj --no-restore --logger "console;verbosity=minimal" -m:1 /nodeReuse:false
dotnet test src/Akbura.HotReloadIntegrationTests/Akbura.HotReloadIntegrationTests.csproj -c Release --no-restore --logger "console;verbosity=minimal" -m:1 /nodeReuse:false
```

The integration assembly's configuration selects the runtime build. Its dynamic
owner compilation deliberately remains Debug so CLR deltas are supported.
Separate generation tests exercise DebugStructural, ReleaseDirect, and
ReleaseConditional; this is not a claim that Release-optimized owners support
structural CLR edits.

Final results (all passed, none skipped):

| Run | Passed | Duration |
| --- | --- | --- |
| Selected Debug unit tests | 332 / 332 | 2 m 43 s |
| Complete CLR suite, Debug runtime | 24 / 24 | 1 m 55 s |
| Complete CLR suite, Release runtime | 24 / 24 | 2 m 31 s |

The CLR suite includes 11 new AKCSS scenarios and 13 existing scenarios. There
are 19 new unit-test cases covering hashing, cache scope, operation ownership,
generation modes, refresh behavior, and detached subscription lifetime. All 25
modified/new files passed CRLF and final-newline checks; modified/new C# files
passed Roslyn AST checks for braced if/for/foreach bodies and multiline XML
summaries. Embedded C# fixtures were checked as well. `git diff --check` passed.

Development runs additionally filtered on `AkcssHotReloadTests`,
`DeclarationRemoval`, and `AkcssDetachedRefreshTests`. The detached refresh test
was run before the runtime guard and failed with Expected 0 / Actual 1 active
subscriptions; it passed in the final selected suite after the fix. Intermediate
failed runs are described below rather than represented as successful checks.

During development, the tests exposed and resolved:

- Duplicate method-token emission when repeated generated lambdas were not
  matched. The harness now matches repeated occurrences, not only unique names.
- Duplicate `DeletedPEMethodDefinition` rows when removing the same cascade
  factories from multiple methods in one delta. This reproduced with Roslyn
  4.11.0, 5.0.0, and 5.9.0. The test harness emits those destructive method edits
  as separate real CLR deltas, unions their actual changed types, and invokes
  handlers only after the complete logical source update. No edits are omitted,
  and the repository's package versions were not changed.
- A late callback invoking a deleted converter after utility removal. Fixed by
  deactivating per-attachment observers before disposal.
- A dynamic source resubscribing while its target was detached. A new headless
  test first reproduced one subscription instead of zero, then verified the
  Debug-only refresh guard and normal reattachment.
- Standalone handler fixtures missing their newly required runtime reference.
- Incorrect fixture assumptions about initial attachment counts, reusing the
  once-only Set API, and resetting arbitrary CLR setter effects.
- One development build hit an MSBuild DLL lock. Shutting down build servers and
  using single-node, non-reused builds resolved the infrastructure failure.

Existing nullable/analyzer warnings in `Akbura.Furioso` and
`QuotedResourceRegressionTests` are outside these changes.

## Boundaries and Phase 2

- Metadata-only style references, intercepted declarations, and scoped/template
  ownership use conservative full replacement rather than an uncertain identity.
- The dependency graph is for AKCSS declarations, not arbitrary external C#
  helper/service implementation changes. Such changes are not promised to
  automatically invalidate dependent components.
- CLR-supported edits vary by host. The tested observed-property attribute edit
  works on this runtime; arbitrary base-type/layout/attribute edits are not
  promised. The harness explicitly rejects changed base types and CLR retains
  metadata for deleted types/fields.
- An IDE's one-delta deletion behavior remains dependent on its compiler/host;
  the harness's split-delta workaround is not installed into the production host.
- Rollback restores the existing owned-resource contract, not old method IL or
  arbitrary user-defined style effects. For example, an unregistered CLR setter
  such as the test's Marker has no reversible Avalonia property contribution;
  deletion assertions cover actual Avalonia Width/Height reset instead.
- Subscription callbacks already executing on another thread are not an added
  thread-quiescence guarantee. The live UI scenarios run on Avalonia's UI thread.
- No per-activator diff, reversible contribution infrastructure, transactional
  cascade engine, or new global module registry was introduced.

Phase 2 can investigate per-activator reconciliation, reversible property
contributions, compiler/IDE deletion interoperability, and broader scoped
ownership retention. Those are separate changes, not implicit guarantees of
this phase.
