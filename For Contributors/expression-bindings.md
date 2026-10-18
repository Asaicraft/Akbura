# Expression bindings: semantic model

`${x.bind expression}` is a built-in, one-way expression binding, not a CLR
markup extension. Stages 1–3 implement syntax, parsing and semantics only.
There is no generated subscription code or runtime execution yet.

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
patterns and guards; inactive branches must not be evaluated eagerly by Stage 4.

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

Stage 4 must implement evaluation, notification attachment, downstream receiver
replacement, branch reconciliation and disposal using this semantic plan. A
partial plan must not be presented as fully reactive. No runtime objects,
subscriptions, handlers or closures are stored in the semantic model.
