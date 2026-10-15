---
title: How Hot Reload Works
summary: A contributor's guide to Akbura's generated Hot Reload contract, component refresh lifecycle, structural reconciliation, state preservation, resource ownership, and failure recovery.
---

# How Hot Reload Works

Akbura Hot Reload updates a running component without unconditionally replacing its control tree. The generator produces an update-friendly C# contract, .NET applies the supported code changes, and Akbura reconciles the new declarations with the objects already in memory.

The central idea is:

> Update the generated instructions, preserve compatible live objects, and explicitly reconcile the resources and values owned by those instructions.

This guide explains the implementation for contributors working on the generator, runtime, or tests. In particular, it includes the render-hook reset and lifecycle changes in that revision. The examples are explanations of the implementation, not a promise that every illustrated source edit is accepted by every .NET Hot Reload host.

Diagrams use Mermaid and are embedded directly in this Markdown file. GitHub can render these fenced blocks without separate image assets. See [GitHub's diagram documentation][mermaid-docs] for renderer details.

## Contents

- [1. The two layers of Hot Reload](#1-the-two-layers-of-hot-reload)
- [2. Why Debug generates a different control representation](#2-why-debug-generates-a-different-control-representation)
- [3. From a metadata update to a component refresh](#3-from-a-metadata-update-to-a-component-refresh)
- [4. Descriptor and state preparation](#4-descriptor-and-state-preparation)
- [5. Refresh revisions, scheduling, and acknowledgment](#5-refresh-revisions-scheduling-and-acknowledgment)
- [6. The structural render plan](#6-the-structural-render-plan)
- [7. How old and new nodes are matched](#7-how-old-and-new-nodes-are-matched)
- [8. Worked example: inserting and removing children](#8-worked-example-inserting-and-removing-children)
- [9. Property values and declaration ownership](#9-property-values-and-declaration-ownership)
- [10. Collection reconciliation](#10-collection-reconciliation)
- [11. Bindings, events, and AKCSS](#11-bindings-events-and-akcss)
- [12. Structural completion and rollback](#12-structural-completion-and-rollback)
- [13. Render hooks during Hot Reload](#13-render-hooks-during-hot-reload)
- [14. An end-to-end edit](#14-an-end-to-end-edit)
- [15. Supported scope and important boundaries](#15-supported-scope-and-important-boundaries)
- [16. Debugging a failed refresh](#16-debugging-a-failed-refresh)
- [17. Tests contributors should add](#17-tests-contributors-should-add)
- [18. Source map and contributor checklist](#18-source-map-and-contributor-checklist)
- [19. AKCSS baseline implementation and verification](#19-akcss-baseline-implementation-and-verification)
- [20. AKCSS incremental reconciliation](#20-akcss-incremental-reconciliation)

## 1. The two layers of Hot Reload

There are two separate mechanisms involved.

**.NET Hot Reload changes executable code and metadata.** The development host detects changes, obtains updated compilation output, and applies the edits supported by its runtime and debugger. Akbura's generated code participates in that process.

**Akbura Hot Reload updates existing component instances.** After the metadata update, Akbura refreshes generated descriptors, schedules affected components, reconciles their render trees, and rebuilds their render-hook frames.

Akbura's runtime is not a second parser that reads a modified `.akbura` file. The new instructions arrive as updated generated C# methods and data. The source generator reads `.akbura` and `.akcss` through Roslyn's additional-text pipeline; the runtime consumes the resulting contract.

```mermaid
flowchart TD
    A["Edit .akbura or .akcss"] --> B["Host supplies changed compiler inputs"]
    B --> C["BlackSilence generates updated C#"]
    C --> D[".NET applies a supported metadata update"]
    D --> E["Generated metadata-update handler"]
    E --> F["Dispatch to the Avalonia UI thread"]
    F --> G["Refresh affected component types"]
    G --> H["Prepare and schedule existing instances"]
    H --> I["Reconcile nodes, values, and owned resources"]
    I --> J["Complete the render-hook frame"]
    J --> K["Acknowledge the refresh"]
```

A source edit can therefore fail at different layers. A compilation error or unsupported metadata edit prevents the new code from reaching Akbura's runtime. A reconciliation or effect failure happens later, after updated code has arrived. Diagnosing one as the other leads to fixes in the wrong subsystem.

**Read:** [the incremental generator][generator], [the generated update handler writer][handler], and [the .NET metadata-update handler contract][metadata-docs].

## 2. Why Debug generates a different control representation

The generator selects `ComponentGenerationMode.DebugStructural` when the consuming C# parse options contain the `DEBUG` preprocessor symbol. Otherwise, it selects `ReleaseDirect`. The selection is based on the symbol, not merely on a configuration's display name.

In the direct representation, generated code can use ordinary fields and direct element references. That is convenient for normal execution, but tying each element to a separate CLR field makes structural source edits change the component's metadata shape.

The structural representation instead places eagerly created component-scope elements in an `AkburaRenderState`. The component contains a stable runtime-store field, and generated expressions retrieve the current objects through local identifiers.

The important generated members are:

| Member | Responsibility |
| --- | --- |
| `__akburaRenderState` | Retains live objects and the last applied structural snapshot. |
| `__AkburaCurrentRenderRevision()` | Returns the current render-plan fingerprint. |
| `__AkburaDescribeRenderTree(...)` | Describes the current nodes and their structural relationships. |
| `__AkburaCreateRenderNode(int)` | Constructs a missing node for a current-plan identifier. |
| `__AkburaEnsureRenderTree()` | Starts reconciliation when the plan is different or was invalidated. |

For example, this is a simplified generated-code excerpt, with qualification and attributes omitted:

```csharp
private readonly AkburaRenderState __akburaRenderState = new();

private static object __AkburaCreateRenderNode(int localId)
{
    return localId switch
    {
        0 => new StackPanel(),
        1 => new TextBlock(),
        2 => new Button(),
        _ => throw new ArgumentOutOfRangeException(nameof(localId)),
    };
}

private bool __AkburaEnsureRenderTree()
{
    return __akburaRenderState.BeginRevision(
        __AkburaCurrentRenderRevision(),
        __AkburaDescribeRenderTree,
        __AkburaCreateRenderNode);
}
```

The factory describes how to create an object **when no old object can be reused**. It does not recreate every object on every update. Generated access to an existing element uses expressions such as:

```csharp
var button = __akburaRenderState.GetRequired<Button>(2);
```

Adding an unnamed eager child can now change the description and factory method bodies without requiring another per-element field. Existing `x.Name` elements receive typed accessors backed by the runtime store.

This is a targeted metadata-stability strategy, not a guarantee that arbitrary source edits preserve CLR shape. Adding named members, changing state declarations, or changing compiler-generated closure shapes can still involve metadata changes outside that strategy. The emitted-metadata tests are important precisely because stable-looking generated source alone is not enough.

**Read:** [mode selection][generator], [structural code generation][structural-writer], and [metadata-contract tests][metadata-tests].

## 3. From a metadata update to a component refresh

`HotReloadServiceWriter` generates `Akbura.HotReloadService.g.cs`. Under `#if DEBUG`, that file registers an assembly-specific handler through `MetadataUpdateHandlerAttribute`.

The handler has the two .NET callbacks:

```csharp
internal static void ClearCache(Type[]? updatedTypes)
internal static void UpdateApplication(Type[]? updatedTypes)
```

Akbura's generated `ClearCache` clears affected AKCSS observed-property metadata before refresh. The actual component refresh work begins in `UpdateApplication`. The .NET contract calls cache-clearing callbacks before application-update callbacks; Akbura performs its generated descriptor reconciliation in the application-update path.

### UI-thread handoff

`UpdateApplication` checks `Dispatcher.UIThread.CheckAccess()`. On the UI thread, it runs `UpdateApplicationCore` immediately. Otherwise, it posts that work to the UI dispatcher.

Posting the callback is not the same as completing a refresh. The component's successful-update acknowledgment happens substantially later.

### Selecting affected types

`UpdateApplicationCore` has two paths:

| Input | Selection |
| --- | --- |
| `updatedTypes` is `null` | Refresh every generated component known to this handler. |
| The generated handler itself is included | Refresh every known component, using the current generated list. |
| A concrete type list is supplied | Select affected component types and consumers of affected generated AKCSS modules. |

The generated `Affects` helper checks the updated type and walks its `DeclaringType` chain. It also checks whether that type is assignable from a candidate component type. This accounts for updates reported against nested types and for affected base types.

The resulting component types are ordered with base types first. The handler then calls each selected component's generated `__AkburaHotReloadApply()` method. Failures are collected so that one component type does not prevent attempts to refresh the others.

This is not a general dependency graph for arbitrary helper methods. A change in an unrelated service or utility type should not automatically be assumed to invalidate every component that calls it.

**Read:** [handler generation and filtering][handler].

## 4. Descriptor and state preparation

`__AkburaHotReloadApply()` separates three concerns: generated descriptor shape, generated state shape, and instance render preparation.

A *shape* is a fingerprint computed by the generator. It describes the relevant declaration contract; it is not a snapshot of the live values currently held by a component.

### Descriptor shape

Descriptors include parameters, injected services, and commands. Their stable keys incorporate semantic identity rather than source position. For example, a parameter key includes its name, type, and property kind; a service key includes its name and type; a command key includes its signature.

The descriptor fingerprint contains additional details that can affect a descriptor's behavior, including parameter flags and default-value syntax.

The last applied descriptor and state fingerprints and property manifest are held
by `AkburaHotReloadMetadata` in a runtime `ConditionalWeakTable` keyed by the
component type. Generated Debug components do not have `s_akburaApplied*Shape` or
`s_akburaHotReloadProperties` fields. The existing service-array initializer
registers the initial snapshot after the generated descriptor fields initialize,
including when a static descriptor is accessed before any instance is created.
Reading the runtime metadata explicitly ensures that static initialization has run.

When the descriptor shape changes, generated code:

1. Saves the previous property manifest and computes the current descriptor keys.
2. Removes obsolete generated Avalonia property registrations.
3. Reuses compatible property objects through `FindProperty<TProperty>()`, creates replacements where needed, and rebuilds descriptor arrays.
4. Publishes the current manifest and applied descriptor-shape fingerprint.

Preserving a compatible Avalonia property object's identity matters because existing instances may already hold values associated with that property. Descriptor changes should not unnecessarily create a second property identity for the same compatible declaration.

`AvaloniaPropertyRegistryHotReload` isolates the compatibility-sensitive part. It accesses Avalonia's registry maps and caches through reflection, removes obsolete registrations for the specific owner, and clears the relevant caches. A private-field layout mismatch produces a `NotSupportedException`, not an attempted silent fallback.

> **Contributor rule:** An Avalonia dependency upgrade must be checked against this registry adapter. Do not replace targeted unregistering with a blanket removal of all properties belonging to a component.

### State shape

State identity is based on the state name, value type, and factory kind: a value initializer and a state-producing hook are different contracts. The state fingerprint is built from the ordered state keys. It does **not** include the initializer expression.

This distinction is deliberate. Changing:

```akbura
state int count = 0;
```

into:

```akbura
state int count = 100;
```

does not overwrite an already-created compatible `State<int>` object with `100`.

Generated Debug state access is lazy, conceptually:

```csharp
private State<int> CountState =>
    RuntimeStates.GetOrCreate(
        "state:count:System.Int32:value:None:",
        () => CreateState(__AkburaCreateStateInfo_count()));
```

The real member names include generated identity information. `GetOrCreate`
retains an existing compatible state and its current value. Its factory creates
the `StateInfo` only when a new state is needed, so new instances use the updated
initializer without a static metadata field. Release retains direct state fields
and static readonly `StateInfo` fields.

When the state shape changes, the instance preparation callback invalidates the
runtime collection's snapshot. Hook aliases are staged in a runtime transaction;
commit installs the new aliases and reconciles removed state resources, while
abort restores the previous cached snapshot. The old Hot Reload snapshot remains
available until a frame commits successfully.

> **Important:** Despite its name, `__AkburaHotReloadResetStates()` does not clear ordinary runtime states. It invalidates the cached snapshot. Compatible state objects can survive; new or incompatible identities do not receive automatic value migration.

Renaming a state or changing its type is therefore different from editing its initializer. Such changes also remain subject to the host's ability to apply the resulting CLR metadata edits.

### Instance preparation

In structural mode, the ordinary preparation callback calls:

```csharp
__component.__akburaRenderState.Invalidate();
```

Invalidation clears the applied render fingerprint but retains the live-node snapshot. The next update must reconsider the current plan even if its local fingerprint has not changed, which is useful for changes outside the element's own markup.

The generator also contains a direct-mode preparation path that reapplies generated initial values. Do not confuse that path with the structural mode selected for a normal `DEBUG` compilation.

**Read:** [component Hot Reload generation][component-hot-reload], [identity generation][identity], [state generation][state-writer], and [the registry adapter][registry-adapter].

## 5. Refresh revisions, scheduling, and acknowledgment

`AkburaHotReloadRuntime` coordinates instance refreshes. It does not use one boolean saying that a component is up to date.

### Different identifiers answer different questions

| Identifier | Question it answers |
| --- | --- |
| Descriptor/state fingerprint | Did this generated declaration contract change? |
| Render fingerprint | Is this the same generated structural plan? |
| Global refresh revision | Which published type refresh is newer? |
| Component creation revision | Which refreshes predate this instance? |
| Per-component request generation | Which scheduled update completed successfully? |
| Plan `LocalId` | Where is a node in the current description? |
| Runtime `NodeId` | Which retained live node is this across descriptions? |

These identities must not be substituted for one another. A render fingerprint does not prove that effects completed, and a local identifier is not a stable cross-edit node identity.

### Tracking instances without retaining them forever

`AkburaComponentRegistry` tracks participating attached components through weak references. Excluded top-level windows do not participate; the diagnostics window uses this mechanism.

The Hot Reload runtime keeps per-instance applied and scheduled revisions in a `ConditionalWeakTable`. Separately, it retains the **latest preparation callback per component type** so an older detached instance can catch up later.

The type-level callback should not capture a particular component instance. Otherwise, a callback intended to describe how to refresh a type could accidentally keep an instance alive.

### The attached-instance path

For a matching participating component, the runtime invokes any required preparation callbacks and calls `AkburaControl.ApplyHotReload`.

That method suppresses component updates while it resets render hooks, reinjects services, records a new request generation, and requests an update. Suppression prevents an intermediate state change from running the update before preparation has finished.

```mermaid
sequenceDiagram
    participant H as Generated handler
    participant R as Hot Reload runtime
    participant C as AkburaControl
    participant G as Generated Update
    participant K as Hook runtime
    H->>R: Refresh with generated preparation callback
    R->>C: Prepare instance and ApplyHotReload
    C->>K: ResetForHotReload
    C->>C: Reinject services under update suppression
    C->>R: Record scheduled revision and request generation
    C->>C: Request update and leave suppression
    C->>K: BeginFrame
    C->>G: Update
    G-->>C: Return root after structural work
    C->>K: CompleteFrame
    C->>R: AcknowledgeRefreshes after success
```

Acknowledgment happens only after `Update()` returns, `Child` is assigned, and the hook frame completes. The runtime moves eligible scheduled revisions into the applied-revision map at that point.

If an update fails, the request generation is retained as pending. Later update processing or another refresh attempt can retry it. A scheduled refresh is not falsely recorded as applied merely because its preparation callback ran.

Reentrant refresh requests are handled through `IsApplying` and `NeedsDrain`; they request another drain rather than recursively running the same refresh application. This is separate from `AkburaControl`'s nonrecursive update loop and its `MaxUpdatesPerBatch` guard.

### Detached-instance catch-up

A component records the currently published revision when it is created. An older instance that is detached when a refresh occurs is not refreshed through the attached-component registry. On its next participating attach, it is checked against newer applicable revisions.

Only the newest callback for a given component type is retained. Missed structural revisions are coalesced, not replayed one by one.

```mermaid
sequenceDiagram
    participant T as Type refresh history
    participant C as Existing component
    participant R as Hot Reload runtime
    C->>C: Detach with live render snapshot retained
    T->>R: Publish revision 10 for this type
    T->>R: Publish revision 11 for this type
    T->>R: Publish revision 12 for this type
    Note over R: Latest callback for this type is revision 12
    C->>R: Attach and request pending refreshes
    R->>C: Prepare using the latest retained callback
    C->>C: Reconcile old snapshot with current plan
    C->>R: Acknowledge successful application
```

An instance created after a refresh's publication starts with that creation baseline; it does not need to replay the preceding history. Multiple applicable type refreshes, such as base and derived component refreshes, still have their own revision bookkeeping.

**Read:** [refresh coordination][hot-reload-runtime], [the component registry][component-registry], and [component update integration][control].

## 6. The structural render plan

`AkburaRenderNodeDefinition` describes a node using six pieces of information:

| Field | Meaning |
| --- | --- |
| `LocalId` | Dense identifier in the current plan, starting at zero. |
| `ParentId` | Current-plan parent; the root uses `-1`. |
| `Slot` | Semantic destination under the parent, such as a collection or scalar property. |
| `Type` | Exact runtime type expected from the factory. |
| `ExplicitKey` | Optional source identity; currently derived from `x.Name`. |
| `SyntaxIdentity` | Hash of the node's own normalized declaration. |

The plan builder requires one root, dense identifiers, and parents appearing before their children. Duplicate explicit keys within the same parent and slot are rejected.

The slot is not just an arbitrary child index. For example, a parent's content property and its children collection are different destinations. Matching must not transfer an object between unrelated properties merely because the object's type happens to match.

### What the syntax identity includes

`ComponentHotReloadIdentity.CreateRenderSyntaxIdentity()` hashes the start tag and non-element body content. Nested element declarations are excluded from their parent's own identity.

Consequently, inserting a child does not automatically make the parent's own declaration appear changed. This helps preserve a parent's live values when only its descendants are edited.

Normalization uses token kinds and token values, excluding trivia. This is not a rule that every whitespace change is irrelevant: meaningful markup text remains part of the token content.

On an ordinary update, `BeginRevision()` returns `false` when the current fingerprint already matches. The generated component can still evaluate dynamic expressions and register render hooks without reconstructing the structural description on every state change.

**Read:** [the plan builder][plan-builder], [the structural writer][structural-writer], and [syntax and slot identities][identity].

## 7. How old and new nodes are matched

Matching runs within a group identified by a **retained parent node identity and a semantic slot**. The runtime processes parents before children so it can determine the correct old sibling group for each new parent.

For each group, `MatchGroup()` performs four passes in order:

```mermaid
flowchart TD
    A["New definitions under one retained parent and slot"] --> B["Pass 1: explicit key and exact type"]
    B --> C["Pass 2: unkeyed exact syntax and exact type"]
    C --> D["Pass 3: remaining unkeyed nodes of exact type"]
    D --> E["Pass 4: create unmatched nodes"]
    E --> F["Reconcile the actual parent destination"]
```

### Pass 1: explicit identity

An explicitly keyed node matches an old node with the same key and exact type. At this revision, the generator derives the key from `x.Name`:

```akbura
<TextBox x.Name="query" Text="Search" />
```

Keyed nodes do not fall back to unkeyed matching when their key changes. A renamed key is therefore an identity change, not merely a cosmetic rename from the reconciler's perspective.

Keys are scoped to the parent and slot. `x.Name` is not a global command to move a live control between arbitrary parents. Replacing a parent can prevent its old descendants from being candidates for reuse under the new parent.

### Pass 2: exact declaration matching

For unkeyed nodes, the runtime first looks for the same exact type and syntax identity. When several old candidates have identical declarations, it chooses the closest sibling position, using the earlier old position to break a tie.

This pass is what lets an unchanged later sibling survive the removal of an earlier sibling of the same type.

### Pass 3: type fallback

Remaining unkeyed nodes match remaining old nodes of the same exact type in old sibling order. This lets an edited declaration reuse an object even though its syntax hash changed.

Despite the method name `MatchCompatibleTypes`, the implementation requires type equality, not general assignability. A different derived type is not interchangeable with the original runtime type.

### Pass 4: construction

Unmatched nodes are created by the current factory and receive new runtime `NodeId` values. The runtime rejects a null result, an unexpected concrete type, or one object reused for multiple nodes. New objects implementing `ISupportInitialize` are tracked through their initialization lifecycle.

Reused nodes retain both their CLR object reference and their runtime `NodeId`. Their current `LocalId` can be different.

> **Identity limit:** Two identical unkeyed siblings do not contain enough information to recover an author's intended business identity after arbitrary edits. The implementation makes a deterministic choice; it does not infer invisible identities. Use an existing explicit name where identity matters, and test ambiguous cases rather than assuming perfect preservation.

**Read:** `MatchGroup`, `MatchExplicitKeys`, `MatchExactSyntax`, `MatchCompatibleTypes`, and `CreateUnmatchedNodes` in [the render state][render-state], plus [planner key selection][planner].

## 8. Worked example: inserting and removing children

Start with:

```akbura
using Avalonia.Controls;

<StackPanel>
    <TextBlock Text="Hello" />
    <TextBlock Text="Hi" />
</StackPanel>
```

For illustration, assign the live objects the runtime identities `P`, `A`, and `B`. These letters stand for actual `NodeId` values; they are not keys supplied by the source.

Now remove the first `TextBlock`:

```akbura
using Avalonia.Controls;

<StackPanel>
    <TextBlock Text="Hi" />
</StackPanel>
```

| Object | Old local ID | New local ID | Result |
| --- | ---: | ---: | --- |
| StackPanel `P` | 0 | 0 | Reused. |
| TextBlock `A`, `Text="Hello"` | 1 | — | No longer owned by the new child collection. |
| TextBlock `B`, `Text="Hi"` | 2 | 1 | Reused through exact syntax matching. |

```mermaid
flowchart LR
    subgraph OLD["Previous plan"]
        OP["P: StackPanel, local 0"]
        OA["A: Hello, local 1"]
        OB["B: Hi, local 2"]
        OP --> OA
        OP --> OB
    end
    subgraph NEW["Current plan"]
        NP["P: same StackPanel, local 0"]
        NB["B: same Hi object, local 1"]
        NP --> NB
    end
    OP -. "reuse" .-> NP
    OB -. "reuse despite local-ID change" .-> NB
    OA -. "remove owned occurrence" .-> REM["Not in the new child collection"]
```

The runtime does not decide that “local ID 1 survived, so it must be the first old text block.” The exact syntax match for `Hi` is considered before type-only fallback.

The inverse case is similar. Adding a previously absent `TextBlock Text="Hi"` after `Hello` preserves the old `Hello` object and constructs only the missing child. The collection reconciler inserts the new object without clearing the whole collection.

A changed literal is a different case. Changing the only unkeyed child from `Text="Hello"` to `Text="Welcome"` can reuse it through the exact-type fallback, then update the changed property declaration.

**Read:** [node matching][render-state] and [generated integration tests][integration-tests].

## 9. Property values and declaration ownership

Preserving an object reference is not enough. Reapplying every generated assignment to that same object would still destroy user-entered values.

The runtime therefore tracks declaration ownership at the property level. A retained node can have a changed start tag while only one of its property declarations actually needs to be reapplied.

Consider:

```akbura
<TextBox Text="Generated" Width="100" />
```

The user types `User value`. The source is then changed to:

```akbura
<TextBox Text="Generated" Width="200" />
```

The expected result is the **same TextBox**, with `Width` updated to `200` and live `Text` still equal to `User value`. The integration suite contains this scenario.

`ReconcileClrValue` and `ReconcileAvaloniaValue` record the generated declaration, its target, and its baseline. When the target and property still match, an unchanged declaration retains the previous property state without writing the generated value again.

```mermaid
flowchart TD
    A["Generated property declaration in a pending revision"] --> B{"Same target and property?"}
    B -->|Yes| C{"Same declaration identity?"}
    C -->|Yes| D["Keep live value; do not write again"]
    C -->|No| E["Apply new generated value; retain original baseline"]
    B -->|No| F["Release old ownership; capture and apply new ownership"]
    G["Previously owned declaration is omitted"] --> H["Restore its baseline during completion preparation"]
```

### Baseline versus current value

The baseline is the value or property state associated with the ownership mechanism before the generated declaration took control. It is not necessarily the latest value written by application code.

Removing an owned declaration releases that ownership and restores the recorded baseline. It should not simply leave the last generated value in place forever. Changing the declaration repeatedly should not make each intermediate generated value the new original baseline.

This preservation rule applies to declarations routed through the reconciliation API. It is not a blanket promise that an arbitrary dynamic expression or user callback will stop assigning a property during normal updates.

Scalar child properties, such as a `Border`'s child, are tracked too. Their omission can remove a child while preserving the parent instance.

**Read:** property reconciliation in [the render state][render-state] and the value-preservation cases in [integration tests][integration-tests].

## 10. Collection reconciliation

`AkburaRenderCollectionReconciler` synchronizes the generated portion of a collection. It does not assume that Akbura owns every item in that collection.

The reconciler receives the live collection, the previous generated items, the desired generated items, and an insertion anchor. It locates the previous **owned occurrences**, then moves or inserts desired items and removes obsolete owned occurrences.

For example, where `External` is not generated:

```text
Live before:        [External, A, B]
Previously owned:   [A, B]
Desired generated:  [B, C]
Live after:         [External, B, C]
```

The external item survives. Retained reference-type items are identified by reference, not by an overridden `Equals` method. Value-type collections use value equality.

For movable Avalonia collections, the implementation uses a native move when available. Otherwise, a move can fall back to removal followed by insertion. Do not equate a fallback move with constructing a new item, or assume it has identical notification behavior to a native move.

The insertion anchor also matters when the generated segment becomes empty and later receives new children. Without retained placement information, subsequent insertion could drift to an unrelated position in a mixed-ownership collection.

There are explicit failure conditions. A read-only collection cannot be reconciled. If a previously owned occurrence was removed outside the reconciler and cannot be found, the runtime reports an ownership violation instead of silently claiming the resulting collection is consistent.

`ReconcileComponentCollection` adds logical-child synchronization for collection content owned by an Akbura component. Updating the backing list alone is insufficient if the component's logical tree still describes the old children. The structural runtime tracks the corresponding collection mutations for rollback.

**Read:** [collection reconciliation][collections] and collection ownership in [the render state][render-state].

## 11. Bindings, events, and AKCSS

Some generated operations own active resources rather than simple values. An event handler, for example, must be detached when its declaration disappears; merely ceasing to generate another `+=` does not remove the old subscription.

The structural runtime distinguishes ordinary operation registration from owned operations:

```text
ShouldApplyOperation
    Register and compare a declaration.

ShouldApplyOwnedOperation
    Register and compare a resource-owning declaration.

Apply...Operation
    Install the corresponding resource with replacement and rollback tracking.
```

The operation slot identifies the semantic destination on a retained node. The declaration identity describes the current source operation at that destination. These two identities have different purposes.

| Resource | Runtime handling |
| --- | --- |
| Avalonia binding | Applied through `ApplyBindingOperation`, with previous ownership released or restored as required. |
| Observable binding | Applied through `ApplyObservableBindingOperation`. |
| CLR event | Applied through `ApplyClrEventOperation`. |
| Routed event | Applied through `ApplyRoutedEventOperation`. |
| Generated AKCSS cascade | Applied through `ApplyAkcssStylesOperation`. |

An unchanged operation can retain its resource. A replacement releases the previous resource and installs the new one. An omitted operation is released during completion preparation. The pending revision tracks mutations so a failed structural application can attempt to restore the old resource state.

A property value and an owned operation cannot both claim the same node slot in one pending revision. Transitions such as “literal value becomes a binding” must explicitly release the previous kind of ownership.

### AKCSS revisions and module-only updates

In Debug, the generated cascade has a semantic revision independent of the element's overall markup identity. Its identity combines normalized generated activator code with the current revisions of the referenced styles and utilities. Style revision methods include expanded transitive `@apply` operations and return their current literals after CLR Hot Reload; they do not rely on rerunning static field initializers.

`ShouldApplyOwnedOperation` registers an unchanged cascade as retained in the pending revision. Descriptors, factories, and activators are created only when the cascade changes. For supported generated utility-only cascades, `ReconcileAkcssStylesOperation` retains compatible activators and subscriptions, constructs only added or changed candidates, and updates affected conflict keys. A new target has no previous candidates to retain. Classes, mixed cascades, unknown revisions, intercepted styles, and uncertain scoped/template ownership conservatively use full replacement on change. ReleaseDirect and ReleaseConditional generation are unchanged.

The existing handler's AKCSS dependency filtering still schedules affected live components when only a referenced module changes. Debug style creation continues through `__AkburaGetStyle(index)`, so changed utility operation arrays and changed style indexes use current constructors rather than the old static `Styles` collection.

The generated `ClearCache` phase invalidates observed-property name caches for reported styles, containing modules, and affected derived styles. Unknown update scope (`null`) clears this metadata cache; unrelated type entries remain cached for a concrete update list. `UpdateApplication` continues to dispatch refreshes on the Avalonia UI thread.

Debug frame refresh reevaluates dynamic arguments and frame conditions through existing utility conflict-key queues, without resetting/executing an unchanged static cascade. Disposed observable argument subscriptions ignore late callbacks before invoking their converter and drop their reference to the old source, including after the same activator reattaches.

For the real CLR scenarios, validation commands, and limitations, see [baseline verification](#19-akcss-baseline-implementation-and-verification) and [incremental reconciliation](#20-akcss-incremental-reconciliation) below.

The implicit root DataContext binding is also tracked as an owned operation. Infrastructure-generated bindings need the same lifecycle discipline as bindings written explicitly in markup.

**Read:** owned operations in [the render state][render-state], [handler AKCSS filtering][handler], and [generated root binding logic][lifecycle-writer].

## 12. Structural completion and rollback

A structural update has an applied snapshot and a separate pending revision. `Invalidate()` does not discard the live snapshot; `BeginRevision()` uses it as the basis for the next reconciliation.

The generated lifecycle follows this outline:

```text
Ensure the current render tree
    If the applied revision is current, no structural revision is opened.

Inside the generated update:
    Register render statements.
    Apply structural initial-value work when a revision is pending.
    Evaluate ordinary update work.
    Prepare structural completion when a revision is pending.
    Refresh component content presenters.
    Complete the structural revision when one is pending.
    Return the root.

On failure while a revision is pending:
    Abort the pending structural revision.
    Preserve the original exception, including rollback failures if any.
```

```mermaid
flowchart TD
    A["Applied snapshot"] --> B["BeginRevision"]
    B --> C["Match retained nodes and create missing nodes"]
    C --> D["Apply tracked values, resources, and collection changes"]
    D --> E["PrepareRevisionCompletion"]
    E --> F["Refresh content presenters"]
    F --> G["CompleteRevision: publish pending snapshot"]
    C -. "failure" .-> R["Abort pending revision"]
    D -. "failure" .-> R
    E -. "failure" .-> R
    F -. "failure" .-> R
    R --> S["Attempt tracked restoration; retain last applied snapshot"]
```

### Preparation is not publication

`PrepareRevisionCompletion()` validates owned operations, removes omitted properties, collections, and operations, and finishes initialization of newly created nodes. It marks the pending revision as prepared but does not publish it as the applied snapshot.

This ordering allows content-presenter refreshes to happen after completion-time changes while still being inside the structural rollback boundary.

`CompleteRevision()` publishes the pending nodes, collections, properties, and fingerprint. Only then is the pending revision cleared as successfully applied.

### What rollback means here

Tracked mutations give the runtime a way to restore collections, owned properties, and resource state after failure. Rollback can itself fail; the implementation combines those failures with the original exception rather than pretending restoration succeeded.

This is not a transaction over the entire application. It does not undo arbitrary user side effects, already-executed callbacks, or every ordinary dynamic assignment. Static descriptor preparation is also outside this per-instance structural transaction.

Most importantly, structural publication happens **inside generated `Update()`**. Hook completion and Hot Reload acknowledgment happen afterward in `AkburaControl`:

```text
Generated Update:
    Structural revision is completed.
    Root is returned.

AkburaControl:
    Child is assigned.
    Render-hook frame is completed.
    Scheduled refresh is acknowledged.
```

A hook failure can therefore leave an already-committed structural tree with an unacknowledged Hot Reload request. Do not describe these three boundaries as one all-or-nothing commit.

**Read:** [generated lifecycle ordering][lifecycle-writer], [render-state completion and abort][render-state], and [component acknowledgment][control].

## 13. Render hooks during Hot Reload

Render hooks normally use a positional frame. `UseHookRuntime` verifies the count, contract key, and runtime state type of registrations against the previous frame.

Hot Reload intentionally does not try to migrate that positional frame across source edits. `AkburaControl.ApplyHotReload()` calls `ResetForHotReload()` before service reinjection and the requested update.

The reset removes the old slot list from the active runtime state, attempts to stop every old slot, and lets the next completed frame create a new list. If reset is requested while a frame is being collected or completed, it is deferred until that operation reaches a safe boundary.

```mermaid
flowchart LR
    A["Live component before edit"] --> B["Hot Reload preparation"]
    B --> S["Compatible generated State objects retained"]
    B --> R["Render-hook slots reset and stopped"]
    S --> U["Next component update"]
    R --> U
    U --> N["Fresh render-hook frame with current callbacks"]
```

As a consequence, an effect with an empty dependency list runs again in the fresh Hot Reload frame. The new callback is not blocked merely because its old dependency list was empty. Adding or removing render hooks no longer requires matching the old slot count across a successful reset, although normal compilation and metadata-update restrictions still apply.

This policy does not make conditional hook calls valid during ordinary rendering. Between ordinary frames, the positional contract is still enforced.

### Detach is not Hot Reload

Detaching suspends render-hook application and stops active resources. Reattaching resumes the hook runtime and requests the appropriate restart or pending Hot Reload work. Unlike Hot Reload reset, detach can retain the slot state for later reuse.

The current implementation attempts cleanup across all slots and aggregates failures. `AkburaControl` also attempts component-parent cleanup and registry detachment even if hook cleanup fails.

### State hooks are a different lifetime

A state-producing hook used in a state initializer is not a render-hook slot merely because it has a `use...` name:

```akbura
state double width = useAvaloniaProperty(Width);
```

That state is accessed through the generated state-storage mechanism. Resetting render-hook slots is not equivalent to reconstructing every state initializer or disposing every object ever associated with a component.

**Read:** [the current hook runtime][hook-runtime], [Hot Reload integration][control], and [state generation][state-writer].

## 14. An end-to-end edit

Suppose this component is already running:

```akbura
using Avalonia.Controls;

state int count = 0;

<StackPanel>
    <TextBox x.Name="query" Text="Search" Width="180" />
    <Button Content="Run" />
</StackPanel>
```

The user has typed `Avalonia` into the TextBox. Existing application behavior has also changed `count` to `7`.

Now edit the component to:

```akbura
using Avalonia.Controls;

state int count = 100;

<StackPanel>
    <TextBlock Text="Find items" />
    <TextBox x.Name="query" Text="Search" Width="260" />
    <Button Content="Run" />
</StackPanel>
```

Assuming the host accepts the generated update, the intended path through the implementation is:

1. BlackSilence emits the updated factory, description, identities, and initializer method body; .NET applies the update and calls the generated handler.
2. The handler selects the component type. Its unchanged state key means the initializer edit does not reset the existing counter state.
3. Instance preparation invalidates the structural revision. `ApplyHotReload` resets render hooks, reinjects services, and requests an update under suppression.
4. The reconciler retains the StackPanel, finds `query` by explicit key and exact type, retains the unchanged Button, and creates the new TextBlock.
5. Property reconciliation applies the changed `Width` declaration. The unchanged `Text="Search"` declaration does not overwrite the user's live `Avalonia` value.
6. The collection receives the new first child. Generated lifecycle code prepares and completes the structural revision; the component then completes hooks and acknowledges the refresh.

The resulting state is:

| Item | Result |
| --- | --- |
| StackPanel | Same object. |
| Named TextBox | Same object. |
| TextBox text | `Avalonia`, because its generated text declaration did not change. |
| TextBox width | `260`, because that declaration changed. |
| Button | Same object. |
| New TextBlock | Newly constructed object. |
| Existing `count` state | Still `7`; changing the initializer does not replace an existing compatible state. |
| Render hooks, if present | Recreated for the new Hot Reload frame. |

Changing `Text="Search"` itself would be a different edit: the generated text declaration would then be new and could replace the live text. Preservation is based on unchanged declarations, not on a universal rule that user input always wins.

**Implementation basis:** [state identity][identity], [lazy state storage][state-writer], [matching and property ownership][render-state], and [hook reset][control].

## 15. Supported scope and important boundaries

The most useful guarantee is narrow and testable: compatible eager component-scope objects and unchanged owned declarations can survive a supported update.

| Boundary | Contributor implication |
| --- | --- |
| .NET metadata acceptance | A correct runtime reconciler cannot make a rejected CLR edit apply. Check the host before debugging node matching. |
| `DEBUG` selection | The structural representation depends on consuming parse options. Do not infer it solely from a folder or configuration label. |
| One structural root | Changing an existing single-root component to zero or multiple roots reaches an explicit restart guard. Keep a stable root container for structural-edit scenarios. |
| Deferred and template-local content | These are separate local scopes, not entries in the same eager component render store. Do not promise identity-preserving migration of all previously realized template instances. |
| Parent and slot boundaries | Reuse is local to a matched parent and semantic destination, not global reparenting by name. |
| Identical unkeyed siblings | Matching is deterministic but cannot recover an identity absent from the source. |
| State contract changes | Compatible fields can survive; renames, type changes, and factory-kind changes are not automatic value migrations. |
| Resource ownership | Only operations routed through ownership tracking receive its replacement and rollback behavior. Arbitrary subscriptions need their own lifecycle. |
| Multiple commit boundaries | Structural completion, hook completion, and scheduler acknowledgment are distinct. |
| Avalonia registry internals | Descriptor removal uses an isolated, version-sensitive reflection adapter. |
| Affected-type filtering | Arbitrary helper-code edits are not a universal component invalidation signal. |

The structural runtime contains deliberate validation rather than a universal “rebuild everything on any error” fallback. An ownership error should lead to understanding the invalid mutation, not to removing the check until the symptom disappears.

**Read:** [scope planning][planner], [root handling][lifecycle-writer], [matching validation][render-state], and [registry compatibility checks][registry-adapter].

## 16. Debugging a failed refresh

Debug in execution order. Establish where the update stopped before changing the next layer.

| Checkpoint | What to inspect |
| --- | --- |
| Compiler inputs and output | Did the changed additional text reach BlackSilence? Does the generated source reflect the edit? |
| Generation mode | Does the consuming compilation define `DEBUG`? Is the component using runtime storage? |
| Generated service | Is `Akbura.HotReloadService.g.cs` present with the metadata-update handler attribute? |
| Host update | Did .NET accept the update, or did the host report an unsupported edit or compilation failure? |
| Handler dispatch | Did `UpdateApplicationCore` run on the UI thread? Which `updatedTypes` were supplied? |
| Affected-type selection | Did the component or one of its known AKCSS modules satisfy the generated filter? |
| Participation | Is the instance attached and participating, or is its top-level excluded? |
| Preparation and scheduling | Was the render state invalidated? Is the latest revision prepared, merely scheduled, or acknowledged? |
| Structural reconciliation | Which parent/slot group was matched? What were the old and new keys, types, and syntax identities? |
| Property/resource ownership | Did the declaration change? Was an old owned collection occurrence removed externally? |
| Completion and hooks | Did structural preparation, content refresh, hook cleanup, or hook application throw? |

Useful breakpoint targets are `__AkburaHotReloadApply`, `ApplyPendingRefreshes`, `AkburaControl.ApplyHotReload`, `BeginRevision`, `MatchGroup`, `PrepareRevisionCompletion`, `CompleteRevision`, `UseHookRuntime.CompleteFrame`, and `AcknowledgeRefreshes`.

For an unexpectedly recreated control, inspect `ExplicitKey`, exact `Type`, retained parent `NodeId`, and `Slot` before looking at its local ID. For an unexpectedly overwritten value, inspect the specific property declaration identity, not just the node's identity.

For a refresh that appears stuck, compare scheduled revisions with acknowledged revisions and inspect the retained request generation. Do not mark a failed refresh as applied to suppress retries; that hides the failure and corrupts the scheduler's meaning.

A refresh failure does not imply that no other component was updated. Both the generated type loop and the attached-instance refresh loop attempt other entries and report collected failures afterward.

## 17. Tests contributors should add

Test observable behavior, generated representation, and failure recovery separately. A successful before/after string comparison does not prove that a real control kept its identity or that .NET can apply the emitted change.

### Runtime and integration scenarios

For a structural or ownership feature, cover the following cases where relevant:

| Area | Required observations |
| --- | --- |
| Insert/remove/reorder children | Reference identity of survivors, correct order, and absence of obsolete generated occurrences. |
| Change one constant | Changed property updates while another unchanged declaration preserves its live value. |
| Omit a property | Original baseline is restored through the appropriate ownership mechanism. |
| Replace/remove an event or binding | Old resource is released; repeated refreshes do not accumulate handlers or subscriptions. |
| Change AKCSS only | A consumer refreshes even when the element's own syntax is unchanged. |
| Mixed collection ownership | Unrelated items survive; insertion-anchor and empty-segment behavior remain correct. |
| Replace a scalar child | Parent identity and child relationships are correct. |
| Fail during a pending revision | Tracked state is restored where possible; the failure is not acknowledged as success. |
| Detach across several revisions | Latest applicable callback is used without replaying every intermediate revision. |
| Edit render hooks | Old slots stop, the next frame is rebuilt, and unchanged component state is not confused with hook state. |

Assertions should use object identity where identity is the guarantee. Equal text in a newly constructed TextBox does not prove preservation of the original TextBox.

### Emitted metadata contracts

`ComponentStructuralHotReloadMetadataContractTests` runs the generator, emits assemblies, and compares metadata contracts across edits. It covers representative eager-child, AKCSS-value, event, and binding edits.

Extend these tests when changing generated storage, helper methods, cached delegates, or closure construction. Compiler-generated fields and nested types can change the metadata contract even when the handwritten writer appears to emit only a small source change.

### What the integration tests do not establish

The inspected structural integration tests compile original and updated revisions separately and transfer the retained `AkburaRenderState` into an owner compiled from the updated revision. This is useful for testing the generated/runtime contract against live Avalonia objects.

It is not the same as applying a debugger metadata delta to the original running process. Keep a manual smoke test in the supported development host for the final source-edit workflow, especially when changing generated CLR shape.

### Running the focused tests

The test project targets .NET 10. Run commands from the repository root with the SDK and normal repository restore requirements available:

```sh
dotnet test src/Akbura.UnitTests/Akbura.UnitTests.csproj -c Debug --filter "FullyQualifiedName~HotReload|FullyQualifiedName~AkburaRender|FullyQualifiedName~UseHookRuntimeTests"
```

Also check the direct-generation path and broader regressions:

```sh
dotnet test src/Akbura.UnitTests/Akbura.UnitTests.csproj -c Release
```

These are contributor verification commands, not a record that the test suite was executed while this document was prepared.

**Read:** [structural integration tests][integration-tests], [metadata-contract tests][metadata-tests], [runtime refresh tests][refresh-tests], [render-state tests][render-tests], [hook tests][hook-tests], and [the test project][test-project].

## 18. Source map and contributor checklist

Use this map to decide where a change belongs. Links are pinned to the inspected revision so the explanations remain reproducible.

| Source | Start here when changing... |
| --- | --- |
| [`AkburaBlackSilenceGenerator.cs`][generator] | Additional-text input, generation pipeline, or mode selection. |
| [`HotReloadServiceWriter.cs`][handler] | Assembly handler registration, affected-type selection, UI dispatch, or type ordering. |
| [`ComponentHotReloadWriter.cs`][component-hot-reload] | Descriptor/state shape handling or per-instance preparation callbacks. |
| [`ComponentHotReloadIdentity.cs`][identity] | Stable declaration keys, syntax hashes, or semantic slot identities. |
| [`ComponentPlanner.cs`][planner] | Scope boundaries, `x.Name` identity, or runtime-storage eligibility. |
| [`ComponentStructuralHotReloadWriter.cs`][structural-writer] | The fixed generated structural contract. |
| [`ComponentLifecycleWriter.cs`][lifecycle-writer] | Initial rendering, revision application, content refresh, or commit ordering. |
| [`StateWriter.cs`][state-writer] | Lazy state fields and identity-preserving state generation. |
| [`AkburaHotReloadRuntime.cs`][hot-reload-runtime] | Type revisions, detached catch-up, scheduling, retries, and acknowledgment. |
| [`AkburaComponentRegistry.cs`][component-registry] | Attached-instance discovery, exclusions, or weak tracking. |
| [`AkburaControl.cs`][control] | Runtime lifecycle integration, update suppression, hook frames, and refresh completion. |
| [`AkburaRenderPlanBuilder.cs`][plan-builder] | Structural-plan invariants and duplicate-key validation. |
| [`AkburaRenderState.cs`][render-state] | Matching, owned properties/resources, pending revisions, and rollback. |
| [`AkburaRenderCollectionReconciler.cs`][collections] | Ordered collection mutation and generated-occurrence ownership. |
| [`AvaloniaPropertyRegistryHotReload.cs`][registry-adapter] | Compatibility with Avalonia's internal property registry. |
| [`UseHookRuntime.cs`][hook-runtime] | Render-hook reset, suspension, slot ownership, and cleanup. |

Before submitting a Hot Reload change, verify these invariants:

- [ ] Current-plan local IDs are not treated as stable identities across edits.
- [ ] Matching remains scoped to the retained parent and semantic slot, with exact-type checks.
- [ ] Unchanged owned declarations do not unnecessarily overwrite live values.
- [ ] Removed or replaced generated resources have an explicit release path.
- [ ] New mutations either participate in the pending revision's recovery model or have a clearly documented boundary.
- [ ] A scheduled refresh is acknowledged only after the component update and hook frame succeed.
- [ ] Deferred catch-up and render-hook reset are tested separately from ordinary updates.
- [ ] Representative edits preserve the intended emitted metadata contract, and Release generation still works.

The design is easiest to maintain when its guarantees remain explicit: **identity is not position, ownership is not the entire control, preparation is not completion, and completion of one layer is not completion of every layer.**

## 19. AKCSS baseline implementation and verification

This section describes complete-cascade replacement and its validation results.
Selective updates extend this mechanism as documented in section 20.

### Implementation

The complete-cascade implementation was validated against baseline `559208b`.

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

### Important files

- Generator: `AkcssHotReloadIdentity.cs`, `AkcssModuleWriter.cs`,
  `AkcssDocumentWriter.cs`, `AkcssActivatorWriter.cs`, `ComponentScopeWriter.cs`.
- Runtime: `AkburaRenderState.cs`, `AkburaControl.cs`, `AkcssRuntime.cs`,
  `AkcssUtilityCandidateActivator.cs`, `AkcssUtilityValueSource.cs`.
- Cache lifecycle: `AkcssObservedPropertySignal.cs`,
  `AkburaHotReloadRuntime.cs`, `HotReloadServiceWriter.cs`.
- Tests: `AkcssHotReloadTests.cs`, `AkburaCompilationSession.cs`,
  `ClrHotReloadSession.cs`, revision/cache/ownership/runtime/writer unit tests.

### Real CLR scenarios

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

### Validation

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

### Complete-replacement boundaries

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

Per-activator reconciliation extends this mechanism as described in section 20.
Independent property contributions, compiler/IDE deletion interoperability, and
broader scoped ownership retention are not guarantees of complete replacement.

## 20. AKCSS incremental reconciliation

This section documents the incremental implementation, its compatibility boundaries,
and the validation results recorded after the baseline in section 19.

### 1. Architecture and baseline

Incremental reconciliation extends the complete-cascade implementation at
`18e3be27e5b46bb822f4f305a1deed0e8120f9a2`. The complete-cascade semantic
guard remains the first gate: an unchanged cascade registers the existing owned
resource and does not create descriptors, factories, activators or subscriptions.

For changed Debug utility-only cascades, the generator now supplies descriptors
and a lazy indexed factory to `AkburaRenderState.ReconcileAkcssStylesOperation`.
The existing target runtime stages a new ordered snapshot containing retained
instances and newly constructed replacements. It uses the existing utility
conflict resolver, operation reset/execute methods and reactive queues; this is
not another cascade engine or an independent property-contribution system.

ReleaseDirect and ReleaseConditional retain their existing generated paths.
Generated Debug components also work against a Release-built runtime.

### 2. Important files

- `src/Akbura/Akcss/AkcssActivatorDescriptor.cs`: hidden generated-code descriptor contract.
- `src/Akbura/Akcss/AkcssRuntime.Reconciliation.cs`: matching, staged transitions,
  affected-key invalidation, publication, commit and rollback.
- `src/Akbura/Akcss/AkcssRuntime.cs`: existing resolver with snapshot-local
  precedence, candidate membership checks and selective Debug condition refresh.
- `src/Akbura/HotReload/AkburaRenderState.AkcssReconciliation.cs` and
  `AkburaRenderState.cs`: owned resource integration and revision commit hook.
- `src/Akbura/AkburaControl.cs`: reconciliation-aware property-changed forwarding.
- `src/Akbura/Akcss/AkcssUtilityCandidateActivator.cs`, `AkcssUtilityOperation.cs`
  and `AkcssUtilityValueSource.cs`: eligibility, stable converters and lifecycle guards.
- Generator `AkcssActivatorPlan.cs`, `AkcssActivatorPlanner.cs` and
  `AkcssActivatorWriter.cs`: declaration anchors, individual revisions and lazy factories.
- Generator `AkcssDocumentWriter.cs`, `AkcssModuleWriter.cs`,
  `AkcssUtilityWriter.cs` and `AkcssUtilityOperationWriter.cs`: Debug safety metadata.
- `AkcssReconciliationTests.cs`, `AkcssReconciliationPerformanceTests.cs` and
  `AkcssIncrementalHotReloadTests.cs`: algorithm, ownership, measurements and real CLR tests.
- `AkburaBlackSilenceGeneratorTests.cs`: align the empty-project fixture's references
  with the generated runtime cache dependency.
- CLR harness `AkburaCompilationSession.cs` and `ClrHotReloadSession.cs`: optional
  generated-code counters and syntax maps for named local factories.

These generated-code entry points and the descriptor are public only because
generated components live in consumers' assemblies; the runtime reconciliation
engine is internal. They are not intended as user-facing styling APIs.

### 3. Declaration identity versus semantic revision

The descriptor contains `Identity`, `Family`, `SyntaxIdentity` and `Revision`.
Family is the existing utility-name key. The syntax anchor uses
`ComponentHotReloadIdentity.CreateOperationSyntaxIdentity`, not a positional
index or `GetHashCode()`. It ignores trivia and source mappings.

Within each family, uniquely matching old/new syntax anchors are paired first.
If exactly one declaration remains unmatched on each side, that pair represents
a modified declaration. The runtime carries the previous installed identity
forward for either retained or modified pairs. A value edit such as `w-12` to
`w-13` can therefore keep the declaration slot while changing its revision.
New slots use deterministic family/occurrence identities; collision suffixes
are deterministic and never steal a matched installed identity.

Repeated identical anchors are ambiguous. Their affected subset is replaced,
not guessed by position. Unique neighbors are still eligible for retention.
Multiple simultaneous unanchored edits in one family are also replaced.

The semantic revision hashes normalized emitted candidate tokens with SHA-256,
using zero in place of source order and module accessor indexes. It includes
arguments, activation, priorities, conflict metadata and value-source context,
plus the current `__AkburaStyleRevision(index)` of every referenced definition.
Live observable values are not revision inputs. A null referenced revision
preserves the unknown-definition full-replacement fallback.

Module implementation hashes currently include generated style names/indexes.
Inserting earlier definitions can therefore conservatively replace additional
utilities even if their behavior is unchanged. Correct current factories and
values are verified; maximal retention across module index shifts is not promised.

### 4. Reconciliation algorithm

Matching groups declarations by family and syntax anchor in O(n) expected time
and O(n) storage. A matched, supported candidate with an equal semantic revision
is retained by exact reference. Factories run for added, modified and ambiguous
declarations only. If any installed or desired operation is incompatible, the
whole transition uses complete replacement; already created replacements are
not constructed a second time.

Retained instances keep immutable `SourceOrder`. Effective precedence is the
desired snapshot's array index, stored in the runtime's snapshot sidecar rather
than mutating candidates. Old indexes are restored by rollback. The existing
resolver still handles operation priority/order, application order, prefixed
variants, conflict groups and unprefixed precedence.

### 5. Lazy generated factories and CLR shape

Named local factories are generated per declaration, with names based on the
existing syntax anchor and duplicate occurrence, not the structural local ID.
Each cascade's lexical block supplies the element-level namespace. A neighboring
element insertion must not rename and retire a retained factory's lambdas; a real
CLR regression shifts the styled element's local ID and then detaches/reattaches
the same controls. Value-source context changes may still replace affected
candidates conservatively, while compatible candidates retain callable factories.
An indexed switch dispatches
only requested declarations. No new component instance fields or mutable static
factory arrays are emitted. Fresh utilities use `__AkburaGetStyle(index)` and
their current operation arrays; static initializers are not replayed.

Debug object and same-type observable converters use runtime method groups,
so retiring an unrelated generated factory cannot retire a retained converter.
Observable conversions between different element/value types retain the older
complete-replacement generation path. This avoids promising compatibility for
converter lambdas whose identities could be changed by CLR edits.

The test harness maps named local functions and their scoped lambdas. With the
repository's Roslyn 4.11, removal of generated lambdas/local functions from
multiple methods can emit duplicate deleted-method rows. Such destructive
method edits are emitted as separate actual CLR deltas; handlers run only after
the complete logical source update. This extends the existing harness workaround,
not a production runtime workaround or simulated assembly replacement.

### 6. Subscriptions and activation state

Retained candidates are not attached or detached during supported reconciliation.
Their current arguments, observable variants, watchers and reversible binding
contributions remain installed. Changed candidates attach before publication;
obsolete subscriptions are retired only when the enclosing revision commits.
An in-flight callback from a staged/obsolete candidate cannot enqueue work unless
that candidate belongs to the currently installed snapshot.

Each utility Watch attachment has a deactivatable observer, as observable
arguments do. Deactivation precedes disposal and drops the
owner reference. Retired values/errors are ignored even after the same candidate
reattaches. Cleanup attempts all owned sources if one disposal throws, and clears
internal references before retry. User code that fails to actually unsubscribe
cannot be made transactional by reconciliation, but its retired observer is inert.

Detached targets do not attach added sources or execute the new cascade.
Reattachment uses the existing lifecycle and activates the latest snapshot.
`RefreshForHotReload` remains selective: inputs follow their existing refresh
contracts, and frame conditions invalidate keys only when their evaluated
activation changes. Sources explicitly requiring recreation on each frame still
follow that existing contract; retention does not override it.

### 7. Affected conflict groups

Changed, added and removed candidates contribute the union of their old/new
operation conflict keys. This includes every operation of multi-property
utilities. A relative-order inversion among retained candidates sharing a key
also marks that key. For each affected key, old operations are reset and the
existing resolver executes the current winner. A removed winner therefore exposes
the original retained shadowed candidate.

Unrelated keys are neither reset nor executed, and their contributions are not
disposed. Dependency-driven reactive notifications can legitimately invalidate
additional keys through the existing queue. Eligibility requires generated,
granular Avalonia-property operations with known revisions and reversible reset
metadata. Each candidate operation captures the original AvaloniaProperty and
receiver Type at construction. Its reset passes that captured property to a
stable runtime-backed overload instead of using the current generated Reset body.
The Debug overload remains emitted even if a later revision uses a CLR setter,
so a retired generated override is not deleted while it is still needed.
This matters because a CLR delta also replaces methods on retired instances:
changing Width to MinWidth must still clear the original Width. The captured
receiver guard preserves the original reset's target compatibility. A real CLR
regression additionally changes both keys of a multi-property utility and verifies
the old/new union without resubscribing its unaffected neighbor.

Arbitrary CLR setters do not opt in. Non-priority `ClearValue()` still
has its existing ownership limitations within an affected property; this work
does not introduce independent ownership of arbitrary external local values.

### 8. Avalonia property synchronization

The target runtime publishes the exact reconciled ordered array to
`AkburaControl.AkcssStylesProperty`. A flag scoped to that single target and
publication suppresses only the matching self-notification. There is no global
notification suppression. A different reentrant property replacement during
publication or reset/execute/retirement is rejected and causes rollback; both runtime and property snapshots
are checked against the expected array.

Normal `SetAkcssStyles`, its already-set validation, normal replacement and
`GetAkcssStyles` keep their existing behavior. Non-Control targets bypass the
Avalonia property and use the same runtime transition directly.

### 9. Ownership, staging, rollback and fallback

The reconciled resource occupies the existing `$akcss` owned-operation slot and
uses `ShouldApplyOwnedOperation` registration. Its baseline is inherited from the
previous resource, not from a chain of previous resources. Omission releases the
current cascade and restores the original public/runtime baseline. Aborting
omission reapplies the owned snapshot and restores its descriptor metadata.

Partial transitions retain the old ordered array, descriptors and working sources
until revision completion. Abort detaches staged replacements, resets affected
new operations, restores old snapshots/order, reapplies old affected winners and
restores the previous property-set state. Commit detaches only obsolete sources.
If cleanup fails during commit, rollback reattaches only candidates whose
retirement was attempted; unaffected sources are not attached twice.

The installed operation drops the transition snapshot and factory after success.
Pending mutations own rollback data only until completion, avoiding unbounded
previous-resource chains across repeated updates. Constructor/attachment failures,
explicit abort, omission abort, commit disposal failure and reentrant publication
are covered separately.

Fallback uses the established full `SetStyles` reset/replacement and previous
render-revision recovery path. It does not promise rollback of old IL after a CLR
delta or transactional compensation for arbitrary user style effects.

### 10. Support matrix

| Scenario | Behavior |
| --- | --- |
| Unchanged complete cascade, including classes | The semantic revision guard retains the complete resource. |
| Changed generated utility-only cascade with known revisions and Avalonia operations | Incremental matching, construction and affected-key update. |
| Observable arguments/variants with compatible contexts | Retained instances and subscriptions; existing refresh contracts remain in force. |
| Binding priorities and reversible contributions | Retained unrelated contributions remain installed. |
| Multi-operation utility or conflicting/reordered candidates | Old/new key union and snapshot-local precedence. |
| One external utility definition changes | Selective replacement of its applications on existing controls. |
| Earlier module definitions inserted/removed | Current indexes/factories; conservative extra replacements may occur. |
| Repeated identical declarations | Ambiguous subset replaced, unique neighbors retained. |
| Classes, mixed cascades, interceptors, legacy/custom operations, unknown revisions | Complete replacement on change. |
| Source-dependent local scope rejected by the existing retention guard | Existing forced/full replacement path. |
| Typed observable conversion requiring a generated converter lambda | Existing full-replacement generation path. |
| Arbitrary setters/effects or external-value contribution ownership | No new transactional/property-ownership guarantees. |

### 11. Tests and execution

Algorithm/ownership tests cover selective change, insertion, removal, reorder,
ambiguous duplicates, attachment failure, explicit abort, priority contributions,
omission/baseline restoration, unchanged frame conditions, retired Watch callbacks,
non-Control targets, commit cleanup failure, reentrant property publication/reset and
preservation/collision handling of installed declaration identities.

`AkcssIncrementalHotReloadTests` implements all requested A-M cases plus
module-only multi-property conflict-key migration and structural local-ID movement
regressions using the
production generator, the original loaded assembly, real `EmitDifference` and
`MetadataUpdater.ApplyUpdate`, live retained controls and generated handlers.
The fixtures optionally insert counters directly into generated utility
constructors and operation methods; counters measure actual construction,
Reset/Update/Apply calls and conflict keys. No production instrumentation is added.
Tests also assert reference identities, actual Avalonia values, active/cumulative
subscriptions and delayed retired callbacks. Supported cases fail if the runtime
unnecessarily uses full replacement. The isolated child fails, rather than skips,
if CLR updates are unavailable. J applies 20 successive logical CLR updates.

Validation commands (from the repository root):

```powershell
dotnet test src/Akbura.UnitTests/Akbura.UnitTests.csproj --no-restore --filter "FullyQualifiedName~Akcss|FullyQualifiedName~AkburaRenderState|FullyQualifiedName~AkburaHotReload|FullyQualifiedName~ComponentDocumentWriter|FullyQualifiedName~HotReloadServiceWriter|FullyQualifiedName~ComponentStructuralHotReload|FullyQualifiedName~GenerateSources_DebugCompilationRegistersHotReloadService" --logger "console;verbosity=minimal" -m:1 /nodeReuse:false
dotnet test src/Akbura.UnitTests/Akbura.UnitTests.csproj -c Release --no-restore --filter "FullyQualifiedName~Akcss|FullyQualifiedName~AkburaRenderState|FullyQualifiedName~AkburaHotReload|FullyQualifiedName~ComponentDocumentWriter|FullyQualifiedName~HotReloadServiceWriter|FullyQualifiedName~ComponentStructuralHotReload|FullyQualifiedName~GenerateSources_DebugCompilationRegistersHotReloadService" --logger "console;verbosity=minimal" -m:1 /nodeReuse:false
dotnet test src/Akbura.HotReloadIntegrationTests/Akbura.HotReloadIntegrationTests.csproj --no-restore --logger "console;verbosity=minimal" -m:1 /nodeReuse:false
dotnet test src/Akbura.HotReloadIntegrationTests/Akbura.HotReloadIntegrationTests.csproj -c Release --no-restore --logger "console;verbosity=minimal" -m:1 /nodeReuse:false
dotnet test src/Akbura.UnitTests/Akbura.UnitTests.csproj --no-restore --filter "FullyQualifiedName~OneOfTenUtilities" --logger "console;verbosity=detailed" -m:1 /nodeReuse:false
```

On Windows, .NET SDK 10.0.401, CLR 10.0.12 and repository Roslyn 4.11.0:

- The regression unit filter includes 17 new unit/measurement cases and the
  corrected empty-project fixture: 381 tests in both Debug and Release, zero skips.
- The complete real CLR project contains 15 new cases (A-M, conflict-key migration
  and structural local-ID movement) and 24 existing cases: all 39 passed in both
  Debug and Release, zero skips.

Final reruns on 2026-10-15 also saved machine-readable TRX results under the ignored
unit-test and integration-project `TestResults` (Debug) and `bin` (Release)
directories. All four result summaries report zero failures,
errors, timeouts, aborted cases and unexecuted cases. The latest complete unit
reruns include the stable-factory-name fix.

An optional unfiltered Debug run was stopped after 21 minutes because it had
started before the final reset-metadata fix: 2,570 cases had passed and the
empty-project fixture below had failed. This interrupted run is not reported as
a successful whole-repository suite. Validation uses the current focused unit
filter and the complete real CLR project in both runtime configurations.

The full Debug run exposed an existing fixture that deliberately removed all
Akbura references. Its generated ClearCache has called
`AkburaHotReloadRuntime.ClearAkcssMetadataCache` since the complete-cascade
implementation; incremental reconciliation does not change that writer.
Keeping the normal runtime reference fixes that test;
its assertions were not weakened. Existing unrelated analyzer warnings
(Furioso CS8602 and QuotedResourceRegressionTests xUnit2031/xUnit2013) remain.

### 12. Performance methodology

`AkcssReconciliationPerformanceTests` compares the old owned-operation
full-replacement API against the incremental API on the same current runtime.
Each path retains one Border across updates of ten independent real Avalonia
properties, including Padding and CornerRadius. Nine unchanged arguments are
observable; one scalar Width changes. Both paths include render ownership,
matching, arrays, reactive sources, property synchronization and conflict execution.
After 100 warm-up updates, 1,000 updates are measured with Stopwatch and
`GC.GetAllocatedBytesForCurrentThread`. Counters verify 10 versus 1 constructions,
9 versus 0 replacement subscriptions, 20 versus 1 resets and 10 versus 1 executions
per update. The old ownership path resets during release and again during install.

This is a runtime microbenchmark, not an end-to-end compiler/debugger latency
benchmark or BenchmarkDotNet statistical result. Tests do not assert timings;
results depend on JIT, machine load and cascade size. Descriptor matching has
nontrivial allocation overhead; small cascades may not benefit in elapsed time.

Recorded Debug sample after lifecycle/rollback hardening:

| Per update | Full-replacement path | Incremental path |
| --- | ---: | ---: |
| Constructed activators | 10 | 1 |
| Replaced observable subscriptions | 9 | 0 |
| Reset operations | 20 | 1 |
| Executed operations | 10 | 1 |
| Allocated bytes | 40,013 | 27,740 |
| Elapsed microseconds | 241.41 | 99.30 |

That sample uses about 30% fewer allocated bytes. A preceding sample measured
133.00/53.62 microseconds, illustrating timing variability; no universal latency
or allocation ratio is asserted. The exact operation/subscription counts are
test assertions. The benchmark excludes compiler generation and CLR delta
emission/application costs.

### 13. Remaining limitations

Remaining work includes independent property-contribution ownership for all
non-priority setters, better compensation for arbitrary custom/intercepted style
effects, granular metadata for class activators, stronger failure isolation for
misbehaving disposal code, and reducing conservative replacements across module
index changes. Pooling/matching allocation optimization needs further measurement.
Incremental reconciliation does not implement those mechanisms or change AKCSS syntax.


<!-- Source references: implementation snapshot inspected for this guide. -->
[baseline]: https://github.com/Asaicraft/Akbura/commit/959ae1590cce507bab17893da8284df69887120b
[generator]: https://github.com/Asaicraft/Akbura/blob/959ae1590cce507bab17893da8284df69887120b/src/Akbura.BlackSilence/AkburaBlackSilenceGenerator.cs
[handler]: https://github.com/Asaicraft/Akbura/blob/959ae1590cce507bab17893da8284df69887120b/src/Akbura.BlackSilence/HotReloadServiceWriter.cs
[component-hot-reload]: https://github.com/Asaicraft/Akbura/blob/959ae1590cce507bab17893da8284df69887120b/src/Akbura.Generator/Language/CodeGeneration/ComponentHotReloadWriter.cs
[identity]: https://github.com/Asaicraft/Akbura/blob/959ae1590cce507bab17893da8284df69887120b/src/Akbura.Generator/Language/CodeGeneration/ComponentHotReloadIdentity.cs
[state-writer]: https://github.com/Asaicraft/Akbura/blob/959ae1590cce507bab17893da8284df69887120b/src/Akbura.Generator/Language/CodeGeneration/StateWriter.cs
[structural-writer]: https://github.com/Asaicraft/Akbura/blob/959ae1590cce507bab17893da8284df69887120b/src/Akbura.Generator/Language/CodeGeneration/ComponentStructuralHotReloadWriter.cs
[lifecycle-writer]: https://github.com/Asaicraft/Akbura/blob/959ae1590cce507bab17893da8284df69887120b/src/Akbura.Generator/Language/CodeGeneration/ComponentLifecycleWriter.cs
[planner]: https://github.com/Asaicraft/Akbura/blob/959ae1590cce507bab17893da8284df69887120b/src/Akbura.Generator/Language/CodeGeneration/ComponentPlanner.cs
[hot-reload-runtime]: https://github.com/Asaicraft/Akbura/blob/959ae1590cce507bab17893da8284df69887120b/src/Akbura/HotReload/AkburaHotReloadRuntime.cs
[control]: https://github.com/Asaicraft/Akbura/blob/959ae1590cce507bab17893da8284df69887120b/src/Akbura/AkburaControl.cs
[component-registry]: https://github.com/Asaicraft/Akbura/blob/959ae1590cce507bab17893da8284df69887120b/src/Akbura/ComponentTree/AkburaComponentRegistry.cs
[plan-builder]: https://github.com/Asaicraft/Akbura/blob/959ae1590cce507bab17893da8284df69887120b/src/Akbura/HotReload/AkburaRenderPlanBuilder.cs
[render-state]: https://github.com/Asaicraft/Akbura/blob/959ae1590cce507bab17893da8284df69887120b/src/Akbura/HotReload/AkburaRenderState.cs
[collections]: https://github.com/Asaicraft/Akbura/blob/959ae1590cce507bab17893da8284df69887120b/src/Akbura/HotReload/AkburaRenderCollectionReconciler.cs
[registry-adapter]: https://github.com/Asaicraft/Akbura/blob/959ae1590cce507bab17893da8284df69887120b/src/Akbura/HotReload/AvaloniaPropertyRegistryHotReload.cs
[hook-runtime]: https://github.com/Asaicraft/Akbura/blob/959ae1590cce507bab17893da8284df69887120b/src/Akbura/Hooks/UseHookRuntime.cs
[integration-tests]: https://github.com/Asaicraft/Akbura/blob/959ae1590cce507bab17893da8284df69887120b/src/Akbura.UnitTests/ComponentStructuralHotReloadIntegrationTests.cs
[metadata-tests]: https://github.com/Asaicraft/Akbura/blob/959ae1590cce507bab17893da8284df69887120b/src/Akbura.UnitTests/ComponentStructuralHotReloadMetadataContractTests.cs
[refresh-tests]: https://github.com/Asaicraft/Akbura/blob/959ae1590cce507bab17893da8284df69887120b/src/Akbura.UnitTests/AkburaHotReloadRuntimeTests.cs
[render-tests]: https://github.com/Asaicraft/Akbura/blob/959ae1590cce507bab17893da8284df69887120b/src/Akbura.UnitTests/AkburaRenderStateTests.cs
[hook-tests]: https://github.com/Asaicraft/Akbura/blob/959ae1590cce507bab17893da8284df69887120b/src/Akbura.UnitTests/UseHookRuntimeTests.cs
[test-project]: https://github.com/Asaicraft/Akbura/blob/959ae1590cce507bab17893da8284df69887120b/src/Akbura.UnitTests/Akbura.UnitTests.csproj
[metadata-docs]: https://learn.microsoft.com/en-us/dotnet/api/system.reflection.metadata.metadataupdatehandlerattribute?view=net-10.0
[mermaid-docs]: https://docs.github.com/en/get-started/writing-on-github/working-with-advanced-formatting/creating-diagrams
