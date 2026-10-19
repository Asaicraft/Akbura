# Expression bindings

`${x.bind expression}` is a built-in, one-way expression binding, not a CLR
markup extension. Stages 1–3 implement syntax, parsing and semantics. Stage 4
adds typed code generation and runtime observation in ReleaseDirect,
DebugStructural and ReleaseConditional generation modes. Full Debug and Release
validation passes for UnitTests, Workspaces, Language Server unit/integration
tests and Hot Reload integration tests.

## Binding and ownership

`MarkupBinder.ExpressionBinding.cs` uses the existing
`BindMarkupAttributeExpression` / `CSharpProbeBinder` scope and conversion path.
The attribute remains a `BoundMarkupPropertySetter` and
`IMarkupPropertySetterOperation`, with the distinct `ExpressionBinding` value
kind. `ValueOperation` and its projected C# operation children are preserved.
An optional `ExpressionBindingPlan` exposes the semantic analysis. Old dynamic
expressions and markup extensions have no such plan.

The original expression result type is `plan.ResultType`; the contextual
destination type and assignment conversion are separate. In particular,
`AssignBinding` does not turn a C# value into `BindingBase` or erase its type to
`object`. Existing property metadata resolves contextual assignments such as
`Setter.Value` before checking the expression conversion.

Plans contain immutable arrays, current-compilation symbols and operations.
They use the existing bound-node/operation caches; there are no new static or
cross-snapshot caches. Expression, destination-type and referenced-symbol edits
therefore use normal semantic-model invalidation. Identity is preserved for
repeated queries within a model and for no-op bound-tree rewriting.

## Dependency and evaluation structure

`ExpressionBindingDependencyAnalyzer` walks Roslyn `IOperation`, never source
paths or regular expressions. Each dependency identifies a receiver, actual
member symbol, receiver/result types, classification and notification contract.
Indexers additionally retain their index evaluations. Only actual
`INotifyCollectionChanged` receivers gain collection notification capability;
an ordinary list or array is not assumed observable.

Evaluation occurrences form a separate immutable tree. Repeated member reads
can share a dependency/subscription identity without eliminating either read.
Calls and indexers with potentially different receivers are not deduplicated.
Ordered condition paths describe `?:`, `&&`, `||`, `??`, `?.`, switch arm order,
patterns and guards. Generation preserves the C# control flow instead of
evaluating inactive branches to discover observers.

Observation descriptions include existing Akbura state/parameter sources,
`INotifyPropertyChanged`, typed Avalonia property registration fields and
collection notifications. Non-observable CLR members remain explicitly
unobservable. Constant/readonly reads are not fabricated as subscriptions.

## Supported forms and limitations

Property/field chains, parameters/state, injected services, component members,
existing foreach/template/local scopes, arithmetic, interpolation, casts,
indexers, unary operators and scalar patterns are analyzed. Method receivers
and arguments are traversed. Primitive/string formatting methods have known
call-site inputs; other calls and user-defined operators/conversions emit
`ExpressionBindingHiddenDependencies` warnings and a partial plan. The analyzer
does not inspect arbitrary method bodies or claim to know their hidden reads.

Mutation, deferred lambda/local-function bodies, object construction, await,
dynamic dispatch, method/event references, recursive/list patterns and unknown
operation forms emit `ExpressionBindingUnsupportedExpression`. They require a
future explicit evaluation/observation contract rather than an inaccurate graph.

Only ordinary writable, one-way scalar property targets are accepted. Routed
events, command handlers, `bind:`/`out:`, directional component parameters,
metadata directives, collection assignment contracts, readonly/init-only/static
properties and unresolved contextual target types are diagnosed explicitly;
unsupported targets do not receive an incomplete usable plan. Missing members,
invalid expressions and incompatible conversions use existing diagnostics,
with probe locations mapped back to the expression in the Akbura document.

CLR destinations also require a publicly readable getter so structural rollback
can restore their baseline. Ref-like destination values cannot be retained by
the typed binding coordinator. Observable value-type receivers are diagnosed
rather than copied/boxed into observers, which could change mutable getter
semantics or observe a different instance. Ordinary unobserved struct reads are
not subject to that restriction.

A partial plan is not fully reactive. No runtime objects, subscriptions,
handlers or closures are stored in the semantic model.

## Generated evaluation

`ComponentPlanner` lowers the value to the dedicated `ExpressionBinding` kind
and retains the semantic plan. `ExpressionBindingWriter` rewrites resolved
read sites in its Roslyn expression. A generated typed lambda evaluates the
expression and performs the normal contextual assignment conversion. The
destination is written with the existing `PropertyWriter` metadata; it does not
also receive an ordinary dynamic-expression assignment.

The receiver instrumentation returns its argument unchanged. For an INPC read,
the essential shape is `tracker.Property(receiver, identity, "Name").Name`.
The receiver expression and getter therefore execute in their original order,
not once to build a path and again to compute the result. Calls, interpolation,
arithmetic, `?:`, `&&`, `||`, `??`, switch arms and guards retain their C# control
flow. Conditional access is lowered using a fresh, collision-checked receiver
temporary and the original lifted result type. Generated assignment and baseline
locals use the same collision checks against names in the expression.

State reads use the actual generated `State<T>` accessor, including its stable
generated name. Parameter and service reads use their existing Avalonia
descriptors. Avalonia member observation uses the registration resolved by the
semantic plan, not a runtime naming convention or reflection lookup.

## Notifications and receiver reconciliation

`ExpressionBinding<T>` coordinates one typed evaluator and target writer.
`ExpressionBindingContext` records the live receivers encountered during that
evaluation. It supports:

- Existing `State<T>.Subscribe` notifications.
- Registered Avalonia properties through `StateBindings.ObserveAvaloniaProperty`.
- Matching INPC properties, including null/empty property-name notifications.
- Actual INCC collections at indexer reads.

Each successful evaluation marks the observations that it used. Unchanged
receiver/member pairs keep their subscriptions; obsolete pairs are detached.
Repeated reads keep their individual C# execution while sharing an observer
where safe. Collection and state observations are deduplicated per live source.
Changing a receiver, index or active branch reconciles against the new set of
actually executed reads. A reset or insertion is an invalidation, not a second
implementation of collection indexing: the original C# indexer determines the
result and retains its ordinary exceptions.

There is no runtime expression parser, reflection-based property traversal,
polling, or second compiled-path interpreter. `CompiledStatePathObserver` is
not forced to walk expression dependencies, because its additional getter
reads would change expression evaluation semantics.

Off-thread notifications post an invalidation to the Avalonia UI dispatcher.
Inactive observation records ignore queued notifications after removal or
disposal. Weak callback ownership prevents queued work from rooting targets.
Notifications during evaluation/assignment mark the binding dirty rather than
recursively entering it. A drain is bounded to 64 synchronous evaluations;
failure to stabilize is reported, not silently ignored. Equal results can skip
the target write, but still reconcile dependency observations.

## Lifetime and generation modes

The first active evaluation assigns the destination. Relevant notifications
subsequently update it without requesting a full-component render. Ordinary
component renders refresh generated callbacks and reuse unchanged observers.

Direct bindings have per-target slots in their component owner. Structural
bindings use `AkburaRenderState` owned operation slots, capture target baselines,
and participate in revision rollback and resource release. Callback refresh is
separate from stable node identity, so a reused control does not require an
obsolete evaluator closure to remain alive.

During a structural replacement, old callbacks move into the transient rollback
record rather than remaining on the retained previous-operation metadata. Abort
restores those callbacks and their observations; commit releases their captures.
Final operation release clears callbacks even when old metadata is retained.

Both paths use the existing local render-scope lifecycle. Visual detachment
suspends observation; reattachment reads the current graph and resumes it.
Updating callbacks while visually suspended does not reactivate subscriptions.
Retiring structural ownership prevents a later visual attachment from reviving
the old binding. Final disposal releases the local scope and evaluator/writer
callbacks. Cleanup attempts all recorded subscriptions even if an unsubscribe
throws, then reports the failures.

If a newly installed binding fails during its first evaluation or target write,
its registration and observers are released. A cleanup failure is reported
together with the original initialization error, not instead of it.

Template/foreach instances and conditional branches use their existing scope
and structural owners rather than a global dependency registry. Their runtime
coverage verifies capture refresh, observer retirement and independent owners.
Foreach collection bookkeeping still uses that owner's queued invalidation.
Expression binding does not make structural collection edits synchronous; tests
drain the UI queue before checking moved or removed render nodes.

## Examples and relationship to state bindings

```akbura
<TextBlock Text=${x.bind $"{Vm.FirstName} {Vm.LastName}"} />
<TextBlock Width=${x.bind Vm.Count + 1} />
<TextBlock Text=${x.bind Vm.Profile?.Name ?? "Unknown"} />
<TextBlock Text=${x.bind Vm.Enabled ? Vm.Name : Vm.GuestName} />
```

The width example preserves the normal `int`-to-`double` conversion. The null
fallback and conditional name observe only the receivers used by their active
paths. A method call can observe its known receiver/arguments, but properties
read only inside that method are not discovered. The existing
`ExpressionBindingHiddenDependencies` warning describes that partial contract;
there is no hidden polling fallback. Unsupported semantic plans do not produce
an executable expression binding.

```akbura
state string name = bind Vm.Name;
<TextBlock Text=${x.bind Vm.Name} />
```

The first declaration creates a directional Akbura state binding with state as
its consumer. The second maintains a UI property directly with a one-way C#
expression. They share state/Avalonia observation and lifecycle mechanisms,
but differ in evaluation and ownership requirements. This feature does not
introduce two-way expression binding or change ordinary `{expression}`,
`${Binding ...}` or `${CompiledBinding ...}` semantics.
