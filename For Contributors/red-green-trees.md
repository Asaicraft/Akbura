# Red-Green Trees in Akbura

Akbura uses its own Roslyn-inspired red-green syntax model for `.akbura` and `.akcss` files. The implementation lives in `src/Akbura.Generator/Language/Syntax`: green nodes hold reusable syntax data, while red nodes describe its location in a particular tree.

## Green nodes: reusable syntax

[`GreenNode`][green-node] stores the syntax kind, flags, child slots, and `FullWidth`, including whitespace and comments. It has **no parent reference and no absolute source position**. Its syntax data is immutable, allowing a subtree to be shared without changing an older snapshot.

Concrete types are defined alongside their red counterparts. For example, [`GreenIdentifierNameSyntax` and `IdentifierNameSyntax`][identifier] represent the same Akbura syntax at the two layers. Green factories construct the underlying data; `CreateRed(parent, position)` creates its contextual wrapper.

## Red nodes: navigation and positions

Akbura's red base class is [`AkburaSyntax`][red-node], not Roslyn's `SyntaxNode`. It wraps a `GreenNode` and adds `Parent` and `Position`, exposing `Span` and `FullSpan`. Child positions are calculated from the parent position and preceding siblings' widths.

**Green describes the syntax; red describes where that syntax occurs.** Sharing a green node does not mean sharing its red wrapper: different occurrences can have different parents and positions.

## Reuse after an edit

[`ComponentSyntaxTree.WithChangedText(...)`][component-tree] passes the previous root and text changes into Akbura's parser. [`Blender`][blender] coordinates old-tree traversal with lexing the new text, enabling unchanged green subtrees to be reused while affected syntax is rebuilt. The previous snapshot remains valid.

[`AkcssSyntaxTree`][akcss-tree] provides the corresponding path for `.akcss` files. Reuse depends on parsing context and recovery conditions; these entry points can fall back to a full parse. **Incremental reuse is separate from the node cache.**

## How nodes are cached

**Green-node cache.** [`GreenNodeCache`][node-cache] is a bounded, 512-entry hash cache with lookup overloads for up to three children. Factories such as `GreenSyntaxFactory.IdentifierName(...)` probe it before allocating, then attempt insertion on a miss. Matching checks the kind, flags/slot count, and **child references**, not a recursive subtree comparison. Collisions replace entries; equivalent syntax does not guarantee reference equality.

> **Implementation caveat at `959ae15`:** `AddNode()` currently checks `!node.IsNotMissing`, rejecting non-missing nodes even though `GreenNode.IsCacheable` requires them. This limits the intended caching path; it does not disable incremental old-tree reuse.

**Red-wrapper cache.** `AkburaSyntax.GetRed(...)` creates child wrappers lazily and retains them in parent fields using `Interlocked.CompareExchange`. `GetCachedSlot(...)` returns an existing wrapper without constructing one. These are local caches, not a global pool. The syntax-tree classes also create their red roots on demand through `GetRoot()`.

When contributing syntax changes, preserve this separation: keep source positions and parent links out of green nodes, use the appropriate factories, and never treat cache hits as a correctness requirement.

## Further reading

These articles explain the underlying Roslyn design in more detail:

- [Persistence, Facades and Roslyn's Red-Green Trees — Eric Lippert, Microsoft Learn][lippert]
- [Roslyn Immutable Trees — Kirill Osenkov][osenkov]
- [Red-Green Trees: an Overview — Bayastan][overview]

[green-node]: https://github.com/Asaicraft/Akbura/blob/959ae1590cce507bab17893da8284df69887120b/src/Akbura.Generator/Language/Syntax/Green/GreenNode.cs
[red-node]: https://github.com/Asaicraft/Akbura/blob/959ae1590cce507bab17893da8284df69887120b/src/Akbura.Generator/Language/Syntax/AkburaSyntax.cs
[identifier]: https://github.com/Asaicraft/Akbura/blob/959ae1590cce507bab17893da8284df69887120b/src/Akbura.Generator/Language/Syntax/Generated/IdentifierNameSyntax.g.cs
[component-tree]: https://github.com/Asaicraft/Akbura/blob/959ae1590cce507bab17893da8284df69887120b/src/Akbura.Generator/Language/ComponentSyntaxTree.cs
[akcss-tree]: https://github.com/Asaicraft/Akbura/blob/959ae1590cce507bab17893da8284df69887120b/src/Akbura.Generator/Language/AkcssSyntaxTree.cs
[blender]: https://github.com/Asaicraft/Akbura/blob/959ae1590cce507bab17893da8284df69887120b/src/Akbura.Generator/Language/Blender.cs
[node-cache]: https://github.com/Asaicraft/Akbura/blob/959ae1590cce507bab17893da8284df69887120b/src/Akbura.Generator/Language/Syntax/Green/GreenNodeCache.cs
[lippert]: https://learn.microsoft.com/ru-ru/archive/blogs/ericlippert/persistence-facades-and-roslyns-red-green-trees
[osenkov]: https://github.com/KirillOsenkov/Bliki/wiki/Roslyn-Immutable-Trees
[overview]: https://medium.com/@krendelia2021/red-green-trees-an-overview-17bae2d84e8c
