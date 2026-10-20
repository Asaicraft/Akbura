# How x.bind is generated

This guide describes how to trace and modify the generated code for
`${x.bind expression}`. Start with the emitted evaluator and its owner, then
follow the compiler plan that produced them.

An expression binding is a typed C# evaluator plus a destination callback.
It does not create a `BindingPath`, a CLR markup extension, or a new
`State<T>` for the expression result. It can observe an existing `state`
declaration, but its result is written directly to the destination.

## Try it in FeatureGallery

The interactive [Expression bindings page](../src/Akbura.FeatureGallery/Akbura.FeatureGallery/Pages/ExpressionBindingsPage.akbura)
is available under **Expression bindings** (`/expression-bindings`) in the
FeatureGallery sidebar. It contains live previews and matching source code in
the existing **Preview / Code** tabs. Start there to learn the feature before
tracing its generated implementation below.

### Write a one-way C# expression binding

```akbura
state string firstName = "Ada";
state string lastName = "Lovelace";
state int count = 1;

<StackPanel>
    <TextBlock Text=${x.bind $"{firstName} {lastName}"}/>
    <TextBlock Text=${x.bind $"Twice the count: {count * 2}"}/>
    <Button Content="Increase" Click={count++}/>
</StackPanel>
```

`x.bind` evaluates a typed C# expression and writes its result **one way**
to the destination property. It does not create a `BindingBase`, make the
destination editable in reverse, or automatically create a separate state.
These examples use ordinary Akbura state, whose changes may also schedule
the component's normal render independently of expression binding.

### Compare the three property-value mechanisms

```akbura
// An ordinary C# expression: evaluated during the normal render/update.
<TextBlock Text={model.FirstName}/>

// Avalonia Binding: a DataContext-based path by default.
<TextBlock DataContext={model} Text=${Binding FirstName}/>

// x.bind: a C# expression with tracked observable reads.
<TextBlock Text=${x.bind model.FirstName}/>
```

For an `INotifyPropertyChanged` model, changing `FirstName` can update the
last two controls through their respective observers without a component
render. The ordinary `{model.FirstName}` expression reads the value again
on a normal render; it does not subscribe to that model property merely by
appearing in the markup. `x.bind` is a one-way assignment; Avalonia
`Binding` follows its own binding-path and mode semantics.

### Observe nested and indexed values

```akbura
<TextBlock Text=${x.bind model.Profile?.Name ?? "No profile"}/>
<TextBlock Text=${x.bind model.People[model.SelectedIndex].Name}/>
<TextBlock Text=${x.bind model.UsePrimaryName ? model.PrimaryName : model.FallbackName}/>
```

On a supported observable source, the active receiver, selected index and
executed conditional branch determine the current observations. Replacing a
profile or choosing another person reconnects the relevant subscriptions.
These expressions still use ordinary C# evaluation: an out-of-range index
can throw, and a non-observable collection is not assumed to raise
collection-change notifications.

### Make an AKCSS utility condition reactive

```akbura
state double width = 800;
<TextBlock ${x.bind width > 1000}:hidden/>
```

`hidden` participates in the existing AKCSS cascade while the Boolean
expression is true; when it becomes false, AKCSS restores the appropriate
winning contribution. An ordinary `{width > 1000}:hidden` condition instead
uses the normal frame-based utility refresh path. The gallery's `width`
is a simulated state value, **not** the actual top-level window width.
See [Reactive AKCSS utility conditions](reactive-utility-conditions.md)
for the full compiler and candidate-lifetime contract.

### Explore the live demos

| Demo | What to try | Source |
| --- | --- | --- |
| Basics | Edit two names, increment a counter, and toggle a conditional expression | [ExpressionBindingBasicsDemo.akbura](../src/Akbura.FeatureGallery/Akbura.FeatureGallery/Components/ExpressionBindingBasicsDemo.akbura) |
| MVVM and nested receivers | Edit `ObservableObject` properties, replace/clear a profile, and switch conditional branches; compare normal C#, Avalonia Binding and `x.bind` | [ExpressionBindingModelDemo.akbura](../src/Akbura.FeatureGallery/Akbura.FeatureGallery/Components/ExpressionBindingModelDemo.akbura) |
| Indexed observation | Select, rename and replace an item in an `ObservableCollection`; observe only the active item | [ExpressionBindingIndexedDemo.akbura](../src/Akbura.FeatureGallery/Akbura.FeatureGallery/Components/ExpressionBindingIndexedDemo.akbura) |
| Reactive utilities | Cross a visibility threshold, switch competing background utilities, and compare reactive versus ordinary utility conditions | [ExpressionBindingUtilityDemo.akbura](../src/Akbura.FeatureGallery/Akbura.FeatureGallery/Components/ExpressionBindingUtilityDemo.akbura) |

The model examples use [ExpressionBindingDemoModel.cs](../src/Akbura.FeatureGallery/Akbura.FeatureGallery/Models/ExpressionBindingDemoModel.cs).
All four demos are mounted by the [ExpressionBindingsPage](../src/Akbura.FeatureGallery/Akbura.FeatureGallery/Pages/ExpressionBindingsPage.akbura).
To run them, launch the FeatureGallery desktop or other supported host and
select **Expression bindings** from its navigation drawer.

Keep the supported-observation contract in mind: Akbura cannot infer hidden
reads inside arbitrary method bodies or notifications from ordinary CLR
properties that do not report changes. Partial-observation warnings are
meaningful; unsupported mutating or deferred expressions are diagnosed.

---
## 1. Read the generated code

Given an injected view model implementing `INotifyPropertyChanged`:

```akbura
inject PersonViewModel Vm;

<TextBlock Text=${x.bind Vm.FirstName + " " + Vm.LastName} />
```

The direct-component registration has this shape:

```csharp
this.__AkburaBindExpression<string?>(
    textBlock,
    propertySlot,
    this,
    tracker =>
        tracker.Property(
            tracker.Avalonia(this, 0, VmProperty.AvaloniaProperty).Vm,
            1, "FirstName").FirstName
        + " " +
        tracker.Property(
            tracker.Avalonia(this, 0, VmProperty.AvaloniaProperty).Vm,
            2, "LastName").LastName,
    value => { ((TextBlock)textBlock).Text = value; });
```

Throughout this guide, generated identifiers, dependency IDs, property slots
and revision identities are abbreviated; namespace qualifications, source
mappings and surrounding component code are omitted. These are code-generation
shapes, not complete generated component files.

Read the arguments as follows:

- `textBlock` and `propertySlot` identify the destination binding slot.
- The third argument is the owning lifetime root, here the component.
- `tracker => ...` is the original expression with observed reads inserted.
- `value => ...` performs the assignment chosen by `PropertyWriter`.

`tracker.Avalonia(...).Vm` observes replacement of the injected view model
through its generated descriptor. `tracker.Property(...).FirstName` observes
the actual view-model instance and then performs the original getter read.
The second occurrence of `Vm` is still evaluated: subscription deduplication
must not eliminate C# reads or alter their order.

When `FirstName` changes, the existing binding evaluates this delegate again
and writes `Text` if the result changed. It does not call component `Update()`
to do so.

## 2. Trace the compiler pipeline

Use these entry points when following a binding from markup to generated C#:

| Responsibility | Source |
| --- | --- |
| Bind the expression and validate its destination/conversion | [MarkupBinder.ExpressionBinding.cs](../src/Akbura.Generator/Language/Binder/MarkupBinder.ExpressionBinding.cs) |
| Describe observable reads and their evaluation order | [ExpressionBindingDependencyAnalyzer.cs](../src/Akbura.Generator/Language/Binder/ExpressionBindingDependencyAnalyzer.cs), [ExpressionBindingPlan.cs](../src/Akbura.Generator/Language/Binder/ExpressionBindingPlan.cs) |
| Resolve receivers and decide initialization placement | [ComponentPlanner.ExpressionBindings.cs](../src/Akbura.Generator/Language/CodeGeneration/ComponentPlanner.ExpressionBindings.cs), [ExpressionBindingInitializationKind.cs](../src/Akbura.Generator/Language/CodeGeneration/ExpressionBindingInitializationKind.cs) |
| Emit the evaluator, registration and assignment callback | [ExpressionBindingWriter.cs](../src/Akbura.Generator/Language/CodeGeneration/ExpressionBindingWriter.cs) |
| Own direct-component binding slots | [AkburaControl.ExpressionBindings.cs](../src/Akbura/AkburaControl.ExpressionBindings.cs) |
| Own structural binding slots and revision rollback | [AkburaRenderState.ExpressionBindings.cs](../src/Akbura/HotReload/AkburaRenderState.ExpressionBindings.cs) |
| Evaluate delegates and reconcile observations | [ExpressionBinding.cs](../src/Akbura/ComponentTree/ExpressionBinding.cs) |

The parser stores the construct in `MarkupExpressionBindingSyntax`, including
its embedded `CSharpExpressionSyntax`, tokens and trivia. It does not resolve
`x.bind` as a markup-extension type.

The binder uses `BindMarkupAttributeExpression` and the scoped C# probe.
A property binding remains a `BoundMarkupPropertySetter` /
`IMarkupPropertySetterOperation`, but has the distinct `ExpressionBinding`
value kind and an `ExpressionBindingPlan`.

Keep these parts of the plan separate:

- `ResultType`: the original C# expression's type.
- `TargetType` and `Conversion`: the destination's contextual type and
  assignment conversion.
- `Dependencies`: resolved sources, member symbols and notification contracts.
- `Evaluation`: individual operation occurrences, children and branch paths.

For example, `Width=${x.bind Vm.Count + 1}` can have an `int` result and a
`double` destination. The generated typed delegate preserves the contextual
conversion. A destination's binding-assignment channel does not make the
expression's value type `BindingBase` or `object`.

The planner lowers the property to `ComponentPropertyValueKind.ExpressionBinding`.
It must not also emit the ordinary frame-based dynamic-property assignment
for that destination.

## 3. Instrument reads, not expression strings

`ExpressionBindingWriter.TrackingRewriter` rewrites the Roslyn syntax using
the semantic evaluation plan. Add observation support there and in the analyzer,
not by substituting identifier text or constructing string paths.

The runtime helpers have different return contracts:

| Source/read | Emitted shape | Returned value |
| --- | --- | --- |
| Existing Akbura state | `tracker.State(__State_count, id)` | The state's current value |
| Parameter or injected service | `tracker.Avalonia(this, id, VmProperty.AvaloniaProperty).Vm` | The generated property value after observing its owner |
| INPC property | `tracker.Property(receiver, id, "Name").Name` | The original property read |
| Registered Avalonia property | `tracker.Avalonia(control, id, Control.WidthProperty).Width` | The original property read |
| Observable collection indexer | `tracker.Collection(items, id)[index]` | The original indexer read |

Except for `State`, tracker helpers return the receiver unchanged. The
generated getter/indexer performs the read; runtime observation must not read
the property again to discover its value.

For a collection/index expression, the evaluator can contain:

```csharp
tracker.Property(
    tracker.Collection(
        tracker.Property(Vm, 0, "Items").Items, 1)
        [tracker.Property(Vm, 2, "SelectedIndex").SelectedIndex],
    3, "Name").Name
```

Here `Vm` stands for an already-resolved receiver; a state or injected root
also receives its own instrumentation. Collection observation is emitted for
the notification contract identified by the analyzer. An ordinary array/list
does not acquire collection notifications merely because it has an indexer.

Preserve C# control flow. For example:

```csharp
tracker.Property(Vm, 0, "Enabled").Enabled
    && tracker.Property(Vm, 1, "Ready").Ready
```

does not read or observe `Ready` while `Enabled` is false. The same rule
applies to `?:`, `??`, switch arms and guards. Conditional access is lowered
to a fresh receiver temporary and the correct lifted result type so the
receiver is evaluated once and the null branch stays inactive.

Use resolved symbols and projected origins for state, foreach, template and
named-element receivers. A local named `Vm` may shadow an injected `Vm`;
matching their spelling is not sufficient. Generated temporaries must use the
rewriter's collision-checked identifiers. Keep source mappings through
`SourceMappingWriter`.

## 4. Decide when to register or refresh callbacks

Registration placement and notification-driven evaluation are different things.
A binding can keep observing after its registration code stops running.

`ExpressionBindingInitializationClassifier` classifies property evaluators:

| Classification | Generated placement | Why |
| --- | --- | --- |
| `InitialOnly` | Initial creation and actual source revisions | The evaluator can keep resolving its inputs without capturing stale frame values |
| `RequiresRenderRefresh` | Render-time registration/callback refresh | The evaluator depends on inputs whose current capture must be supplied by the frame |

State, parameter and injected-service reads, constants, readonly receivers and
proven observable paths can be `InitialOnly`. This does not freeze their
values: their notifications still reevaluate the installed binding.

Render locals, foreach/template item captures, mutable unobserved members,
unobservable array indexing, arbitrary calls and unproven named-control
lifetimes require conservative refresh. Inspect the evaluation tree, not just
whether the expression contains a local identifier.

For example:

```akbura
var current = Vm.Profile;
<TextBlock Text=${x.bind current.Name} />
```

The binding observes `current.Name`. It does not observe the initializer
`Vm.Profile` as if it were part of the expression. A later render supplies
the current frame's `current` capture. Refresh updates the existing binding's
callbacks; it does not require duplicate subscriptions to unchanged sources.

The generation mode determines the owning registration API:

| Mode/owner | Registration |
| --- | --- |
| Direct component target | `__AkburaBindExpression<T>` |
| Structural/runtime-stored target | `AkburaRenderState.ReconcileExpressionBinding<T>` |
| AKCSS utility condition | Candidate-owned `ExpressionBinding<bool>` |

In direct generation, `InitialOnly` property bindings register in
`FirstUpdate`. Structural/single-pass generation registers them during
owning-node initialization and real source revisions, not on ordinary steady
frames. `ReleaseConditional` also uses structural ownership where the target
requires runtime storage; choose the API from the element plan rather than
assuming every Release binding is direct.

Stable property bindings in retained local stores use static local factories
with explicit component/store/target inputs. Keep that capture boundary:
neighboring lambdas can otherwise share a C# display class and retain an old
foreach item even when the stable expression never reads it.

## 5. Preserve structural ownership and rollback

A structural registration has this abbreviated shape:

```csharp
__akburaRenderState.ReconcileExpressionBinding<string?>(
    localId,
    propertySlot,
    expressionIdentity,
    tracker => tracker.Property(Vm, 0, "Name").Name,
    value => { textBlock.Text = value; },
    target: textBlock,
    property: TextBlock.TextProperty,
    ownLifetime: binding => this.__AkburaRegisterLocalRenderScope(
        lifetimeRoot, binding.Resume, binding.Suspend, __akburaRenderState));
```

The real emitter resolves the root/target expressions from the element's
scope. Property slots come from `ComponentHotReloadIdentity.CreatePropertySlot`;
operation syntax identities distinguish source revisions from ordinary frames.

Avalonia destinations supply their target and property so the coordinator can
capture a baseline. CLR destinations supply a baseline-restoration callback
that captures the actual target object. Do not look up that target from a
rolled-back node store when restoring a previous value.

During replacement, callback snapshots belong to the transient rollback
record. Abort restores the previous callbacks and observations; successful
commit releases obsolete captures. Completed node snapshots must not retain
retired operation metadata and its old candidate/item closures.

Foreach name-scope bookkeeping can prepare a revision on an ordinary frame.
For stable expression slots the emitter uses
`preserveUnchanged: !__foreachFrameN.IsSourceRevision` so bookkeeping does
not refresh them as if the template source changed. Actual source revisions
still reconcile the binding.

## 6. Follow a dependency notification at runtime

`__AkburaBindExpression<T>` finds or creates a per-target/property slot;
`ReconcileExpressionBinding<T>` does the corresponding owned-operation work.
Both ultimately install a `Func<ExpressionBindingContext, T>` evaluator and
an `Action<T>` writer on `ExpressionBinding<T>`.

For an active binding, `Update(evaluate, write)` installs the callbacks and
invalidates evaluation. Each evaluation:

1. Starts an observation epoch.
2. Executes the generated C# delegate, recording the sources actually read.
3. Removes observations not used by this successful evaluation and retains
   matching receiver/member subscriptions.
4. Calls the writer on the first value or when the result changes.

Equal results can skip the write, but do not skip observation reconciliation.
Replacing a receiver, changing an index or selecting a different branch must
release observations of the previous active path.

Subscriptions use existing state notifications, matching INPC properties,
registered Avalonia properties and collection-change events. Off-thread
notifications post to the UI dispatcher. Notifications arriving during
evaluation mark it dirty rather than recursively entering the evaluator.
A non-stabilizing synchronous drain is bounded to 64 evaluations and reports
an error.

An expression binding does not schedule a component render. Existing
`state`, parameter or service setters may independently request one; expression
binding neither requires nor suppresses that existing behavior. Do not add a
new result state merely to route a property or condition update.

Visual detach suspends observation. Reattach evaluates current sources before
resuming normal notifications. Refreshing callbacks while suspended must not
restart subscriptions. Retiring an owner prevents later attachment from
reviving its old bindings. Final disposal releases subscriptions and callbacks;
cleanup attempts all subscriptions even if one unsubscribe throws.
Initialization failure must also release newly installed resources.

## 7. Generate a reactive AKCSS condition

```akbura
state double width = 800;
<TextBlock ${x.bind width > 1000}:hidden/>
```

The utility operation carries `ConditionExpressionBinding` with `bool` as
its contextual type. `AkcssActivatorWriter` uses the same `TrackingRewriter`
to emit a candidate evaluator:

```csharp
new AkcssUtilityCandidateActivator(
    conflictKey: "hidden",
    sourceOrder: 0,
    applications: applications,
    reactiveCondition:
        ((Func<Owner, Func<ExpressionBindingContext, bool>>)(static owner =>
            (ExpressionBindingContext tracker) =>
                tracker.State(owner.__State_width, 0) > 1000))(this));
```

The static factory isolates stable captures. The candidate owns an
`ExpressionBinding<bool>` and caches its result. A result change notifies the
existing AKCSS runtime, which resolves contributions through the cascade.
`IsActive` reads that cached value; it must not poll the expression. The
evaluator must not directly assign `IsVisible`, `Background` or another
utility destination.

For render-local conditions, generated `RefreshReactiveCondition` calls
replace the candidate's evaluator capture. Dependency notifications continue
to work between those frames. Ordinary `{condition}:utility` stays
frame-evaluated, and `${hover}:utility` remains a markup-extension variant.

See [Reactive AKCSS utility conditions](reactive-utility-conditions.md) for
candidate lifecycle and cascade details.

## 8. Validate a generator change

Check the resulting C# and runtime behavior, not only whether the syntax parses:

- Compile the emitted evaluator in `ReleaseDirect`, `DebugStructural` and
  `ReleaseConditional` modes, including contextual conversions.
- Check repeated reads, short-circuit branches, null receivers, nested/indexed
  paths and state/parameter/injected roots.
- Change an observable source without calling component `Update()`; assert
  the actual destination value and unchanged render count where no other
  mechanism requests a render.
- Change render-local/item captures on an ordinary frame and confirm old
  sources are unsubscribed and collectible.
- Check detach/reattach, conditional removal, independent template instances,
  keyed replacement and revision rollback.

Use `MarkupExpressionBindingRuntimeTests` (including its initialization
tests) for generated property bindings and
`ReactiveUtilityConditionGenerationTests` for generated utility conditions.
Semantic/dependency tests cover diagnostics and plan structure. Shared
Workspaces tests cover projected source spans and symbols.

Keep unsupported constructs diagnostic. Deferred/mutating expressions,
construction, await, dynamic dispatch and unknown operation forms must not
silently receive an inaccurate reactive plan. Arbitrary method bodies and
user-defined operations can hide reads; preserve
`ExpressionBindingHiddenDependencies` warnings and partial-observation
classification instead of adding polling.

Property targets must satisfy the existing writable, one-way scalar contract.
Events, directional bindings, collection assignment targets, readonly/init-only
members and ref-like destinations need different contracts. Utility conditions
must convert to `bool`; `bool?` needs explicit null resolution.

Editor services reuse the embedded C# syntax, contextual type and real symbol
origins from this compiler pipeline. Changes to probes or source mappings must
preserve that projection. They do not require a second IDE-specific evaluator.
