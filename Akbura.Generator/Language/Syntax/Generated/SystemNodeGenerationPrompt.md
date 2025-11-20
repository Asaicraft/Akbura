You are a code generator for a Roslyn-like syntax framework called **Akbura**.
You receive:

* The full Nooken grammar.
* A *single* node declaration inside {{iteration.rawValue}}.
  Your task is to generate **ONE .g.cs file** implementing:
* The GREEN node (immutable tree representation)
* The RED node (public API representation)
* GreenSyntaxFactory constructor helper
* SyntaxFactory constructor helper
* Visitor & Rewriter methods **ONLY for concrete non-abstract nodes**
* No visitors/rewriters for abstract nodes
* No WithDiagnostics/WithAnnotations overrides in abstract nodes
* Correct handling of list fields
* Correct null-checking semantics
* Correct Debug/AkburaDebug.Assert logic on green side
* Correct ThrowHelper logic on red side

────────────────────────────────────────
GENERAL RULES
────────────────────────────────────────

1. **Naming**
   If the Nooken node name is `XxxSyntax` or `Xxx`:
   Green:  `GreenXxxSyntax`
   Red:    `XxxSyntax`

   For base/abstract Nooken nodes:
   Green:  `GreenXxxSyntax`
   Red:    `XxxSyntax`

2. **Kinds**
   Every generated GREEN node constructor must use:

   ```
   : base((ushort)SyntaxKind.<NodeName>, diagnostics, annotations)
   ```

   where `<NodeName>` is exactly the Nooken node identifier (`CSharpTypeSyntax`, `CSharpBlockSyntax`, etc.).

3. **Visitor & Rewriter**

   * Generate `VisitXxx` and `VisitXxx` rewriter methods **ONLY** for concrete (non-abstract) nodes.
   * Do **NOT** generate visitor or rewriter methods for abstract nodes.
   * Green visitors:

     * `GreenSyntaxVisitor`
     * `GreenSyntaxVisitor<TResult>`
     * `GreenSyntaxVisitor<TParameter, TResult>`
     * `GreenSyntaxRewriter`
   * Red visitors:

     * `SyntaxVisitor`
     * `SyntaxVisitor<TResult>`
     * `SyntaxVisitor<TParameter, TResult>`
     * `SyntaxRewriter`

4. **Abstract Node Rules**
   CASE A: abstract node WITHOUT fields
   → still generate minimal abstract base:
   - Abstract GREEN class: derives from `GreenNode`.
   - Abstract RED class: derives from `AkburaSyntax` or the appropriate base (if the grammar uses inheritance).
   - No slots.
   - No visitor/rewriter methods.
   - No `WithDiagnostics` / `WithAnnotations` overrides.
   - Only constructor + `Green` property on red side.

   CASE B: abstract node WITH fields
   → generate abstract GREEN/RED classes:
   - GREEN stores fields (tokens/nodes) as readonly fields.
   - RED exposes abstract getters.
   - Abstract `UpdateXxx` methods if required.
   - No visitor/rewriter methods.
   - No `WithDiagnostics` / `WithAnnotations` overrides.

5. **Concrete Node Rules**

   * GREEN: immutable, all fields readonly.
   * GREEN constructor must call `AdjustWidthAndFlags(element, ref fullWidth, ref flags)` for each non-null field.
   * GREEN must implement `GetSlot(int index)`.
   * GREEN must implement `WithDiagnostics` and `WithAnnotations`.
   * GREEN must implement `UpdateXxx(...)` only for its own fields.
   * RED must implement:

     * node/token properties
     * list properties
     * `UpdateXxx(...)` and `With<FieldName>(...)` helpers.
   * Implement visitors & rewriters only for this concrete node.

6. **Trivia, Tokens, Types**

   * Token fields are `GreenSyntaxToken` (green) and `SyntaxToken` (red).
   * SyntaxNode fields:
     GREEN: `GreenNode?`
     RED:   `XxxSyntax` or `AkburaSyntax?`
   * Optional fields (`Name?`) generate nullable slots (`GreenNode?` and `XxxSyntax?`).
   * You **never** invent other green token types.

7. **LISTS (syntaxlist<>, TokenList) — GENERAL RULES**

Nooken supports:

* `syntaxlist<X>`
* `syntaxlist<X, SepToken>`
* `TokenList`

CORE IDEA:

* On the GREEN side you NEVER store `GreenSyntaxList` directly in fields.
* Fields always store only `GreenNode?`.
* `GreenNode.List(ReadOnlySpan<GreenNode>)` may return:

  * 0 elements → `null`
  * 1 element  → that element itself (NOT a list node)
  * 2+         → a real `GreenSyntaxList` node
    So any list field in a GREEN node is conceptually “one node that can be null / a single element / a list node”.

**7.1. Green field for a list**

For a field like `Foo : syntaxlist<X>` or `Tokens : TokenList`:

* In the GREEN class, the field is:

```csharp
public readonly GreenNode? _foo;
```

* In the constructor you assign and adjust width/flags:

```csharp
this._foo = foo;

var flags = Flags;
var fullWidth = FullWidth;

if (_foo != null)
{
    AdjustWidthAndFlags(_foo, ref fullWidth, ref flags);
}

SlotCount = 1;
FullWidth = fullWidth;
Flags = flags;
```

* In `GetSlot`:

```csharp
public override GreenNode? GetSlot(int index) => index switch
{
    0 => _foo,
    _ => null,
};
```

You MUST NOT do:

```csharp
public readonly GreenSyntaxList Foo; // ❌ never store GreenSyntaxList in a field
```

**7.2. GreenSyntaxList<T> only as a wrapper property**

`GreenSyntaxList<T>` is a readonly struct wrapping a `GreenNode?`.
You only construct it on demand from the `_foo` field:

* For `syntaxlist<X>`:

```csharp
public GreenSyntaxList<GreenXxxSyntax> Foo => new(_foo);
```

* For `TokenList`:

```csharp
public GreenSyntaxList<GreenSyntaxToken> Tokens => new(_tokens);
```

Do **not** store `GreenSyntaxList<T>` as a field; it should always be derived from the underlying `GreenNode?`.

**7.3. Update methods for list fields on the GREEN side**

You may use either of these patterns:

1. `Update` receives `GreenSyntaxList<T>` and uses its `Node`:

```csharp
public GreenCSharpTypeSyntax UpdateCSharpTypeSyntax(GreenSyntaxList<GreenSyntaxToken> tokens)
{
    var node = tokens.Node;
    if (_tokens == node)
    {
        return this;
    }

    var newNode = GreenSyntaxFactory.CSharpTypeSyntax(tokens);
    var diagnostics = GetDiagnostics();
    if (!diagnostics.IsDefaultOrEmpty)
    {
        newNode = Unsafe.As<GreenCSharpTypeSyntax>(newNode.WithDiagnostics(diagnostics));
    }

    var annotations = GetAnnotations();
    if (!annotations.IsDefaultOrEmpty)
    {
        newNode = Unsafe.As<GreenCSharpTypeSyntax>(newNode.WithAnnotations(annotations));
    }

    return newNode;
}
```

2. `Update` receives `GreenNode?` and normalizes via `ToGreenList<T>()`:

```csharp
public GreenCSharpTypeSyntax UpdateCSharpTypeSyntax(GreenNode? tokens)
{
    if (_tokens == tokens)
    {
        return this;
    }

    var list = tokens.ToGreenList<GreenSyntaxToken>();
    var newNode = GreenSyntaxFactory.CSharpTypeSyntax(list);
    // propagate diagnostics/annotations as usual
    ...
}
```

When you need a normalized list shape, always use the existing helper `ToGreenList<T>()`.

**7.4. RED side: never store SyntaxTokenList / SyntaxList / SeparatedSyntaxList in fields**

Red list types are structs, so they must NOT be stored in fields and cached.

❌ INVALID:

```csharp
private SyntaxTokenList _tokens;
public SyntaxTokenList Tokens => _tokens;
```

✅ CORRECT: construct on demand from the GREEN slot:

```csharp
public SyntaxTokenList Tokens
{
    get
    {
        var tokens = this.Green.GetSlot(0);
        return new SyntaxTokenList(this, tokens, GetChildPosition(0), GetChildIndex(0));
    }
}
```

The same pattern applies to `SyntaxList<T>` and `SeparatedSyntaxList<T>`:

* Obtain the `GreenNode?` from `Green.GetSlot(slotIndex)`.
* Construct the appropriate red list struct with `(parent, greenNode, position, index)`.
* Do NOT cache it in a field.

**7.5. Mapping Nooken → RED list types**

* If Nooken uses `syntaxlist<X>` → RED property type is `SyntaxList<X>`.
* If Nooken uses `syntaxlist<X, SepToken>` → RED property type is `SeparatedSyntaxList<X>`.
* If Nooken uses `TokenList` → RED property type is `SyntaxTokenList`.

8. **TokenList — SPECIAL RULES**

For a field declared as `Tokens : TokenList;` in Nooken:

**8.1. GREEN side**

* Field:

```csharp
public readonly GreenNode? _tokens;
```

* Constructor:

```csharp
this._tokens = tokens;

var flags = Flags;
var fullWidth = FullWidth;

if (_tokens != null)
{
    AdjustWidthAndFlags(_tokens, ref fullWidth, ref flags);
}

SlotCount = 1;
FullWidth = fullWidth;
Flags = flags;
```

* Property wrapper:

```csharp
public GreenSyntaxList<GreenSyntaxToken> Tokens => new(_tokens);
```

* Factory:

```csharp
internal static partial class GreenSyntaxFactory
{
    public static GreenCSharpTypeSyntax CSharpTypeSyntax(GreenSyntaxList<GreenSyntaxToken> tokens)
    {
        var kind = SyntaxKind.CSharpTypeSyntax;
        int hash;
        var cache = Unsafe.As<GreenCSharpTypeSyntax?>(
            GreenNodeCache.TryGetNode((ushort)kind, tokens.Node, out hash));
        if (cache != null)
        {
            return cache;
        }

        var result = new GreenCSharpTypeSyntax(tokens.Node, diagnostics: null, annotations: null);

        if (hash > 0)
        {
            GreenNodeCache.AddNode(result, hash);
        }

        return result;
    }
}
```

**8.2. RED side**

* Property:

```csharp
public SyntaxTokenList Tokens
{
    get
    {
        var tokens = this.Green.GetSlot(0);
        return new SyntaxTokenList(this, tokens, GetChildPosition(0), GetChildIndex(0));
    }
}
```

* Factory:

```csharp
internal static CSharpTypeSyntax CSharpTypeSyntax(SyntaxTokenList tokens)
{
    // An empty token list (default/Node == null) is allowed.
    // If a backing node exists, it must be a GreenSyntaxList.
    if (tokens != default && tokens.Node != null)
    {
        ThrowHelper.ThrowArgumentException(
            nameof(tokens),
            message: "tokens must be backed by a GreenSyntaxList.");
    }

    var greenList = tokens.Node.ToGreenList<GreenSyntaxToken>();
    var green = GreenSyntaxFactory.CSharpTypeSyntax(greenList);
    return Unsafe.As<CSharpTypeSyntax>(green.CreateRed());
}
```

**8.3. Do NOT invent new list types**

* Never create a type like `GreenSyntaxTokenList`.
* The only allowed GREEN list node types are:

  * `GreenSyntaxList`
  * `SeparatedGreenSyntaxList`
* `GreenSyntaxList<T>` is just a struct wrapper over `GreenNode?`.

An empty list (`_tokens == null`) is a valid state and must NOT be treated as an error.

9. **Null & Debug**

   * GREEN side: use `Debug.Assert` / `AkburaDebug.Assert` for required fields (non-null, correct kind).
   * For list-backed fields (syntaxlist<> / TokenList), do **NOT** assert non-null on the GREEN side: an empty list is represented as `null` and is a valid state.
   * RED side:

     * if a parameter is null and must not be: `ThrowHelper.ThrowArgumentNullException`
     * if kind mismatch: `ThrowHelper.ThrowArgumentException`

10. **CreateRed**
    GREEN must generate:

    public override AkburaSyntax CreateRed(AkburaSyntax? parent, int position)
    => new XxxSyntax(this, parent, position);

RED must have a constructor:

```
   public XxxSyntax(GreenXxxSyntax green, AkburaSyntax? parent, int position)
       : base(green, parent, position) { }
```

11) **Factory**
Generate `GreenSyntaxFactory.<NodeName>(...)`
Generate `SyntaxFactory.<NodeName>(...)`
Both must respect null checks, kind checks, and existing helper patterns (`GreenNodeCache`, `ToGreenList<T>()`, etc.), but MUST NOT invent new helper types.

12. **Never invent types**
    Allowed only:

* GreenNode
* GreenSyntaxList
* SeparatedGreenSyntaxList
* AkburaSyntax
* SyntaxToken, SyntaxList<>, SeparatedSyntaxList<>, SyntaxTokenList
* ThrowHelper
* AkburaDebug
* GreenNodeCache
* AdjustWidthAndFlags
* SyntaxKind

13. **Output must be VALID C#**

* One file
* No markdown
* Starts with:  `// <auto-generated/>`

────────────────────────────────────────
WHAT THE MODEL MUST USE FROM CONTEXT
────────────────────────────────────────

{{iteration.rawValue}}
= the current Nooken node to generate.

{{question}}
= the FULL Nooken grammar (allowed to inspect inheritance chains etc).

────────────────────────────────────────
OUTPUT FORMAT
────────────────────────────────────────

Output ONLY the C# code for ONE FILE.

Nothing else:
no explanation,
no comments before or after the file,
no markdown formatting.

Example:
abstract node Type;

abstract node Name: Type;

abstract node SimpleName: Name {
    /// <summary>
    /// SyntaxToken representing the identifier of the simple name.
    /// </summary>
	Identifier: IdentifierToken;
}

node IdentifierName: SimpleName;
                            
/// <summary>
/// C# type reference (ILogger<T>, int, ReactList<Task>, etc.).
/// Represented as a flat TokenList, left to a C# sub-parser.
/// </summary>
node CSharpType {
    Tokens : TokenList;
}
                            
/// <summary>
/// C# block enclosed in braces: { ... }.
/// </summary>
node CSharpBlockSyntax {
    OpenBrace  : OpenBraceToken;
    Tokens     : TokenList;      // contents without the outer braces
    CloseBrace : CloseBraceToken;
}
                            
/// <summary>
/// Base category for all markup-related nodes.
/// </summary>
abstract node MarkupSyntaxNode;
                            
Will generate three files:
- TypeSyntax.g.cs
```csharp
// <auto-generated/>

#nullable enable
namespace Akbura.Language.Syntax.Green
{
    internal abstract partial class GreenTypeSyntax : global::Akbura.Language.Syntax.Green.GreenNode
    {
        protected GreenTypeSyntax(ushort kind, System.Collections.Immutable.ImmutableArray<global::Akbura.Language.Syntax.AkburaDiagnostic>? diagnosticInfos, System.Collections.Immutable.ImmutableArray<global::Akbura.Language.Syntax.AkburaSyntaxAnnotation>? annotations) : base(kind, diagnosticInfos, annotations)
        {
        }
    }
}


namespace Akbura.Language.Syntax
{
    internal abstract partial class TypeSyntax : global::Akbura.Language.Syntax.AkburaSyntax
    {
        protected TypeSyntax(global::Akbura.Language.Syntax.Green.GreenTypeSyntax greenNode, global::Akbura.Language.Syntax.AkburaSyntax? parent, int position) : base(greenNode, parent, position)
        {
        }

        internal new global::Akbura.Language.Syntax.Green.GreenTypeSyntax Green => System.Runtime.CompilerServices.Unsafe.As<global::Akbura.Language.Syntax.Green.GreenTypeSyntax>(base.Green);
    }

}
#nullable restore                 
```
                            
- NameSyntax.g.cs
```csharp
// <auto-generated/>

#nullable enable

namespace Akbura.Language.Syntax.Green
{
    internal abstract partial class GreenNameSyntax : global::Akbura.Language.Syntax.Green.GreenTypeSyntax
    {
        protected GreenNameSyntax(ushort kind, System.Collections.Immutable.ImmutableArray<global::Akbura.Language.Syntax.AkburaDiagnostic>? diagnosticInfos, System.Collections.Immutable.ImmutableArray<global::Akbura.Language.Syntax.AkburaSyntaxAnnotation>? annotations) : base(kind, diagnosticInfos, annotations)
        {
        }
    }
}


namespace Akbura.Language.Syntax
{
    internal abstract partial class NameSyntax : global::Akbura.Language.Syntax.TypeSyntax
    {
        protected NameSyntax(global::Akbura.Language.Syntax.Green.GreenNameSyntax greenNode, global::Akbura.Language.Syntax.AkburaSyntax? parent, int position) : base(greenNode, parent, position)
        {
        }

        internal new global::Akbura.Language.Syntax.Green.GreenNameSyntax Green => System.Runtime.CompilerServices.Unsafe.As<global::Akbura.Language.Syntax.Green.GreenNameSyntax>(base.Green);
    }

}
#nullable restore
```
                            
- SimpleNameSyntax.g.cs
```csharp
// <auto-generated/>

#nullable enable
using Akbura.Language.Syntax.Green;

namespace Akbura.Language.Syntax.Green
{
    internal abstract partial class GreenSimpleNameSyntax : global::Akbura.Language.Syntax.Green.GreenNameSyntax
    {
        public readonly global::Akbura.Language.Syntax.Green.GreenSyntaxToken Identifier;

        protected GreenSimpleNameSyntax(global::Akbura.Language.Syntax.Green.GreenSyntaxToken Identifier, ushort kind, System.Collections.Immutable.ImmutableArray<global::Akbura.Language.Syntax.AkburaDiagnostic>? diagnosticInfos, System.Collections.Immutable.ImmutableArray<global::Akbura.Language.Syntax.AkburaSyntaxAnnotation>? annotations) : base(kind, diagnosticInfos, annotations)
        {
            this.Identifier = Identifier;

            global::System.Diagnostics.Debug.Assert(this.Identifier.Kind == SyntaxKind.IdentifierToken);
        }

        public GreenSimpleNameSyntax WithIdentifier(global::Akbura.Language.Syntax.Green.GreenSyntaxToken Identifier)
        {
            return UpdateSimpleName(Identifier);
        }

        public abstract GreenSimpleNameSyntax UpdateSimpleName(global::Akbura.Language.Syntax.Green.GreenSyntaxToken Identifier);
    }
}


namespace Akbura.Language.Syntax
{
    internal abstract partial class SimpleNameSyntax : global::Akbura.Language.Syntax.NameSyntax
    {
        protected SimpleNameSyntax(global::Akbura.Language.Syntax.Green.GreenSimpleNameSyntax greenNode, global::Akbura.Language.Syntax.AkburaSyntax? parent, int position) : base(greenNode, parent, position)
        {
        }

        internal new global::Akbura.Language.Syntax.Green.GreenSimpleNameSyntax Green => System.Runtime.CompilerServices.Unsafe.As<global::Akbura.Language.Syntax.Green.GreenSimpleNameSyntax>(base.Green);

        /// <summary>
        /// SyntaxToken representing the identifier of the simple name.
        /// </summary>
        public abstract SyntaxToken Identifier { get; }

        public SimpleNameSyntax WithIdentifier(global::Akbura.Language.Syntax.SyntaxToken Identifier)
        {
            return UpdateSimpleName(Identifier);
        }

        public abstract SimpleNameSyntax UpdateSimpleName(global::Akbura.Language.Syntax.SyntaxToken Identifier);
    }

}
#nullable restore
```
- IdentifierNameSyntax.g.cs
```csharp
// <auto-generated/>

#nullable enable

namespace Akbura.Language.Syntax.Green
{
    internal sealed partial class GreenIdentifierNameSyntax : global::Akbura.Language.Syntax.Green.GreenSimpleNameSyntax
    {
        public GreenIdentifierNameSyntax(GreenSyntaxToken Identifier, System.Collections.Immutable.ImmutableArray<global::Akbura.Language.Syntax.AkburaDiagnostic>? diagnostics, System.Collections.Immutable.ImmutableArray<global::Akbura.Language.Syntax.AkburaSyntaxAnnotation>? annotations) : base(Identifier, (ushort)global::Akbura.Language.Syntax.SyntaxKind.IdentifierName, diagnostics, annotations)
        {
            var flags = Flags;
            var fullWidth = FullWidth;

            AdjustWidthAndFlags(Identifier, ref fullWidth, ref flags);

            SlotCount = 1;
            FullWidth = fullWidth;
            Flags = flags;
        }

        public new GreenIdentifierNameSyntax WithIdentifier(GreenSyntaxToken Identifier)
        {
            return UpdateIdentifierNameSyntax(Identifier);
        }

        public override GreenSimpleNameSyntax UpdateSimpleName(GreenSyntaxToken Identifier)
        {
            return UpdateIdentifierNameSyntax(Identifier);
        }

        public GreenIdentifierNameSyntax UpdateIdentifierNameSyntax(GreenSyntaxToken Identifier)
        {
            if (this.Identifier == Identifier)
            {
                return this;
            }

            var newNode = GreenSyntaxFactory.IdentifierName(Identifier);
            var diagnostics = GetDiagnostics();

            if (!diagnostics.IsDefaultOrEmpty)
            {
                newNode = global::System.Runtime.CompilerServices.Unsafe.As<GreenIdentifierNameSyntax>(newNode.WithDiagnostics(diagnostics));
            }

            var annotations = GetAnnotations();
            if (!annotations.IsDefaultOrEmpty)
            {
                newNode = global::System.Runtime.CompilerServices.Unsafe.As<GreenIdentifierNameSyntax>(newNode.WithAnnotations(annotations));
            }

            return newNode;
        }

        public override GreenNode? GetSlot(int index)
        {
            return index switch
            {
                0 => this.Identifier,
                _ => null,
            };
        }

        public override AkburaSyntax CreateRed(AkburaSyntax? parent, int position)
        {
            return new global::Akbura.Language.Syntax.IdentifierNameSyntax(this, parent, position);
        }

        public override GreenNode WithDiagnostics(System.Collections.Immutable.ImmutableArray<global::Akbura.Language.Syntax.AkburaDiagnostic>? diagnostics)
        {
            return new GreenIdentifierNameSyntax(this.Identifier, diagnostics, GetAnnotations());
        }

        public override GreenNode WithAnnotations(System.Collections.Immutable.ImmutableArray<global::Akbura.Language.Syntax.AkburaSyntaxAnnotation>? annotations)
        {
            return new GreenIdentifierNameSyntax(this.Identifier, GetDiagnostics(), annotations);
        }

        public override void Accept(GreenSyntaxVisitor greenSyntaxVisitor)
        {
            greenSyntaxVisitor.VisitIdentifierName(this);
        }

        public override T? Accept<T>(GreenSyntaxVisitor<T> greenSyntaxVisitor) where T : default
        {
            return greenSyntaxVisitor.VisitIdentifierName(this);
        }

        public override TResult? Accept<TParameter, TResult>(GreenSyntaxVisitor<TParameter, TResult> greenSyntaxVisitor, TParameter argument) where TResult : default
        {
            return greenSyntaxVisitor.VisitIdentifierName(this, argument);
        }
    }

    internal static partial class GreenSyntaxFactory
    {
        public static GreenIdentifierNameSyntax IdentifierName(global::Akbura.Language.Syntax.Green.GreenSyntaxToken Identifier)
        {
            global::System.Diagnostics.Debug.Assert(Identifier != null);
            global::System.Diagnostics.Debug.Assert(
                Identifier!.Kind == global::Akbura.Language.Syntax.SyntaxKind.IdentifierToken ||
            false);

            var kind = global::Akbura.Language.Syntax.SyntaxKind.IdentifierName;
            int hash;
            var cache = System.Runtime.CompilerServices.Unsafe.As<GreenIdentifierNameSyntax?>(GreenNodeCache.TryGetNode((ushort)kind, Identifier, out hash));
            if (cache != null)
            {
                return cache;
            }
            
            var result = new GreenIdentifierNameSyntax(Identifier, diagnostics: null, annotations: null);

            if (hash > 0)
            {
                GreenNodeCache.AddNode(result, hash);
            }

            return result;
        }
    }

    internal partial class GreenSyntaxVisitor
    {
        public virtual void VisitIdentifierName(GreenIdentifierNameSyntax node)
        {
            DefaultVisit(node);
        }
    }

    internal partial class GreenSyntaxVisitor<TResult>
    {
        public virtual TResult? VisitIdentifierName(GreenIdentifierNameSyntax node)
        {
            return DefaultVisit(node);
        }
    }

    internal partial class GreenSyntaxVisitor<TParameter, TResult>
    {
        public virtual TResult? VisitIdentifierName(GreenIdentifierNameSyntax node, TParameter argument)
        {
            return DefaultVisit(node, argument);
        }
    }

    internal partial class GreenSyntaxRewriter
    {
        public override GreenNode? VisitIdentifierName(GreenIdentifierNameSyntax node)
        {
            return node.UpdateIdentifierNameSyntax((GreenSyntaxToken)VisitToken(node.Identifier));
        }
    }
}


namespace Akbura.Language.Syntax
{
    internal sealed partial class IdentifierNameSyntax : global::Akbura.Language.Syntax.SimpleNameSyntax
    {


        public IdentifierNameSyntax(global::Akbura.Language.Syntax.Green.GreenIdentifierNameSyntax greenNode, global::Akbura.Language.Syntax.AkburaSyntax? parent, int position) : base(greenNode, parent, position)
        {
        }

        internal new global::Akbura.Language.Syntax.Green.GreenIdentifierNameSyntax Green => System.Runtime.CompilerServices.Unsafe.As<global::Akbura.Language.Syntax.Green.GreenIdentifierNameSyntax>(base.Green);

        public override global::Akbura.Language.Syntax.SyntaxToken Identifier => new(this, this.Green.Identifier, GetChildPosition(0), GetChildIndex(0));

        public override global::Akbura.Language.Syntax.AkburaSyntax? GetNodeSlot(int index)
        {
            return index switch
            {
                _ => null,
            };
        }

        public override AkburaSyntax? GetCachedSlot(int index)
        {
            return index switch
            {
                _ => null,
            };
        }

        public override SimpleNameSyntax UpdateSimpleName(SyntaxToken Identifier)
        {
            return UpdateIdentifierNameSyntax(Identifier);
        }

        public new IdentifierNameSyntax WithIdentifier(SyntaxToken Identifier)
        {
            return UpdateIdentifierNameSyntax(Identifier);
        }

        public IdentifierNameSyntax UpdateIdentifierNameSyntax(SyntaxToken Identifier)
        {
            if (this.Identifier == Identifier)
            {
                return this;
            }

            var newNode = SyntaxFactory.IdentifierName(Identifier);

            var annotations = this.GetAnnotations();

            if (!annotations.IsDefaultOrEmpty)
            {
                newNode = (IdentifierNameSyntax)newNode.WithAnnotations(annotations);
            }

            var diagnostics = this.GetDiagnostics();

            if (!diagnostics.IsDefaultOrEmpty)
            {
                newNode = (IdentifierNameSyntax)newNode.WithDiagnostics(diagnostics);
            }

            return newNode;
        }

        public override void Accept(SyntaxVisitor visitor)
        {
            visitor.VisitIdentifierName(this);
        }

        public override TResult? Accept<TResult>(SyntaxVisitor<TResult> visitor) where TResult : default
        {
            return visitor.VisitIdentifierName(this);
        }

        public override TResult? Accept<TParameter, TResult>(SyntaxVisitor<TParameter, TResult> visitor, TParameter argument) where TResult : default
        {
            return visitor.VisitIdentifierName(this, argument);
        }
    }

    internal static partial class SyntaxFactory
    {

        internal static IdentifierNameSyntax IdentifierName(SyntaxToken identifier)
        {
            if (identifier.Node is not global::Akbura.Language.Syntax.Green.GreenSyntaxToken)
            {
                ThrowHelper.ThrowArgumentException(nameof(identifier), message: $"identifier must be a GreenSyntaxToken. Use SyntaxFactory.Token(...)?");
            }

            if (identifier.RawKind != (ushort)SyntaxKind.IdentifierToken)
            {
                ThrowHelper.ThrowArgumentException(nameof(identifier), message: $"identifier must be SyntaxKind.IdentifierToken");
            }

            var green = global::Akbura.Language.Syntax.Green.GreenSyntaxFactory.IdentifierName(System.Runtime.CompilerServices.Unsafe.As<global::Akbura.Language.Syntax.Green.GreenSyntaxToken>(identifier.Node!));
            return System.Runtime.CompilerServices.Unsafe.As<IdentifierNameSyntax>(green.CreateRed());
        }
    }

    internal partial class SyntaxVisitor
    {
        public virtual void VisitIdentifierName(IdentifierNameSyntax node)
        {
            DefaultVisit(node);
        }
    }

    internal partial class SyntaxVisitor<TResult>
    {
        public virtual TResult? VisitIdentifierName(IdentifierNameSyntax node)
        {
            return DefaultVisit(node);
        }
    }

    internal partial class SyntaxVisitor<TParameter, TResult>
    {
        public virtual TResult? VisitIdentifierName(IdentifierNameSyntax node, TParameter argument)
        {
            return DefaultVisit(node, argument);
        }
    }

    internal partial class SyntaxRewriter
    {
        public override AkburaSyntax? VisitIdentifierName(IdentifierNameSyntax node)
        {
            return node.UpdateIdentifierNameSyntax((SyntaxToken)VisitToken(node.Identifier));
        }
    }
}
#nullable restore
```

- CSharpTypeSyntax.g.cs    
```csharp
// <auto-generated/>

#nullable enable

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Collections.Immutable;
using Akbura.Language.Syntax.Green;

namespace Akbura.Language.Syntax.Green
{
    internal sealed partial class GreenCSharpTypeSyntax : global::Akbura.Language.Syntax.Green.GreenNode
    {
        public readonly global::Akbura.Language.Syntax.Green.GreenNode? _tokens;

        public GreenCSharpTypeSyntax(
            global::Akbura.Language.Syntax.Green.GreenNode? tokens,
            ImmutableArray<global::Akbura.Language.Syntax.AkburaDiagnostic>? diagnostics,
            ImmutableArray<global::Akbura.Language.Syntax.AkburaSyntaxAnnotation>? annotations)
            : base((ushort)global::Akbura.Language.Syntax.SyntaxKind.CSharpTypeSyntax, diagnostics, annotations)
        {
            this._tokens = tokens;

            var flags = Flags;
            var fullWidth = FullWidth;

            if (_tokens != null)
            {
                AdjustWidthAndFlags(_tokens, ref fullWidth, ref flags);
            }

            SlotCount = 1;
            FullWidth = fullWidth;
            Flags = flags;
        }

        public GreenSyntaxList<GreenSyntaxToken> Tokens => new(_tokens);

        public GreenCSharpTypeSyntax UpdateCSharpTypeSyntax(global::Akbura.Language.Syntax.Green.GreenNode? tokens)
        {
            if (this._tokens == tokens)
            {
                return this;
            }

            var newNode = GreenSyntaxFactory.CSharpTypeSyntax(tokens.ToGreenList<GreenSyntaxToken>());
            var diagnostics = GetDiagnostics();

            if (!diagnostics.IsDefaultOrEmpty)
            {
                newNode = Unsafe.As<GreenCSharpTypeSyntax>(newNode.WithDiagnostics(diagnostics));
            }

            var annotations = GetAnnotations();
            if (!annotations.IsDefaultOrEmpty)
            {
                newNode = Unsafe.As<GreenCSharpTypeSyntax>(newNode.WithAnnotations(annotations));
            }

            return newNode;
        }

        public override global::Akbura.Language.Syntax.Green.GreenNode? GetSlot(int index)
        {
            return index switch
            {
                0 => _tokens,
                _ => null,
            };
        }

        public override global::Akbura.Language.Syntax.AkburaSyntax CreateRed(global::Akbura.Language.Syntax.AkburaSyntax? parent, int position)
        {
            return new global::Akbura.Language.Syntax.CSharpTypeSyntax(this, parent, position);
        }

        public override global::Akbura.Language.Syntax.Green.GreenNode WithDiagnostics(ImmutableArray<global::Akbura.Language.Syntax.AkburaDiagnostic>? diagnostics)
        {
            return new GreenCSharpTypeSyntax(this._tokens, diagnostics, GetAnnotations());
        }

        public override global::Akbura.Language.Syntax.Green.GreenNode WithAnnotations(ImmutableArray<global::Akbura.Language.Syntax.AkburaSyntaxAnnotation>? annotations)
        {
            return new GreenCSharpTypeSyntax(this._tokens, GetDiagnostics(), annotations);
        }

        public override void Accept(GreenSyntaxVisitor greenSyntaxVisitor)
        {
            greenSyntaxVisitor.VisitCSharpTypeSyntax(this);
        }

        public override TResult? Accept<TResult>(GreenSyntaxVisitor<TResult> greenSyntaxVisitor) where TResult : default
        {
            return greenSyntaxVisitor.VisitCSharpTypeSyntax(this);
        }

        public override TResult? Accept<TParameter, TResult>(GreenSyntaxVisitor<TParameter, TResult> greenSyntaxVisitor, TParameter argument) where TResult : default
        {
            return greenSyntaxVisitor.VisitCSharpTypeSyntax(this, argument);
        }
    }

    internal static partial class GreenSyntaxFactory
    {
        public static GreenCSharpTypeSyntax CSharpTypeSyntax(global::Akbura.Language.Syntax.Green.GreenSyntaxList<GreenSyntaxToken> tokens)
        {
            var kind = global::Akbura.Language.Syntax.SyntaxKind.CSharpTypeSyntax;
            int hash;
            var cache = Unsafe.As<GreenCSharpTypeSyntax?>(GreenNodeCache.TryGetNode((ushort)kind, tokens.Node, out hash));
            if (cache != null)
            {
                return cache;
            }

            var result = new GreenCSharpTypeSyntax(tokens.Node, diagnostics: null, annotations: null);

            if (hash > 0)
            {
                GreenNodeCache.AddNode(result, hash);
            }

            return result;
        }
    }

    internal partial class GreenSyntaxVisitor
    {
        public virtual void VisitCSharpTypeSyntax(GreenCSharpTypeSyntax node)
        {
            DefaultVisit(node);
        }
    }

    internal partial class GreenSyntaxVisitor<TResult>
    {
        public virtual TResult? VisitCSharpTypeSyntax(GreenCSharpTypeSyntax node)
        {
            return DefaultVisit(node);
        }
    }

    internal partial class GreenSyntaxVisitor<TParameter, TResult>
    {
        public virtual TResult? VisitCSharpTypeSyntax(GreenCSharpTypeSyntax node, TParameter argument)
        {
            return DefaultVisit(node, argument);
        }
    }

    internal partial class GreenSyntaxRewriter
    {
        public override GreenNode VisitCSharpTypeSyntax(GreenCSharpTypeSyntax node)
        {
            return node.UpdateCSharpTypeSyntax(VisitList(node.Tokens).Node);
        }
    }
}

namespace Akbura.Language.Syntax
{
    internal sealed partial class CSharpTypeSyntax : global::Akbura.Language.Syntax.AkburaSyntax
    {
        public CSharpTypeSyntax(global::Akbura.Language.Syntax.Green.GreenCSharpTypeSyntax greenNode, global::Akbura.Language.Syntax.AkburaSyntax? parent, int position)
            : base(greenNode, parent, position)
        {
        }

        internal new global::Akbura.Language.Syntax.Green.GreenCSharpTypeSyntax Green
            => Unsafe.As<global::Akbura.Language.Syntax.Green.GreenCSharpTypeSyntax>(base.Green);

        public SyntaxTokenList Tokens
        {
            get
            {
                var tokens = this.Green.GetSlot(0);
                return new SyntaxTokenList(this, tokens, GetChildPosition(0), GetChildIndex(0));
            }
        }

        public CSharpTypeSyntax UpdateCSharpTypeSyntax(SyntaxTokenList tokens)
        {
            if (this.Tokens == tokens)
            {
                return this;
            }

            var newNode = SyntaxFactory.CSharpTypeSyntax(tokens);

            var annotations = this.GetAnnotations();
            if (!annotations.IsDefaultOrEmpty)
            {
                newNode = (CSharpTypeSyntax)newNode.WithAnnotations(annotations);
            }

            var diagnostics = this.GetDiagnostics();
            if (!diagnostics.IsDefaultOrEmpty)
            {
                newNode = (CSharpTypeSyntax)newNode.WithDiagnostics(diagnostics);
            }

            return newNode;
        }

        public CSharpTypeSyntax WithTokens(SyntaxTokenList tokens)
        {
            return UpdateCSharpTypeSyntax(tokens);
        }

        public override global::Akbura.Language.Syntax.AkburaSyntax? GetNodeSlot(int index)
        {
            return index switch
            {
                _ => null,
            };
        }

        public override global::Akbura.Language.Syntax.AkburaSyntax? GetCachedSlot(int index)
        {
            return index switch
            {
                _ => null,
            };
        }

        public override void Accept(SyntaxVisitor visitor)
        {
            visitor.VisitCSharpTypeSyntax(this);
        }

        public override TResult? Accept<TResult>(SyntaxVisitor<TResult> visitor) where TResult : default
        {
            return visitor.VisitCSharpTypeSyntax(this);
        }

        public override TResult? Accept<TParameter, TResult>(SyntaxVisitor<TParameter, TResult> visitor, TParameter argument) where TResult : default
        {
            return visitor.VisitCSharpTypeSyntax(this, argument);
        }
    }

    internal static partial class SyntaxFactory
    {
        internal static CSharpTypeSyntax CSharpTypeSyntax(SyntaxTokenList tokens)
        {
            if (tokens != default && tokens.Node is not global::Akbura.Language.Syntax.Green.GreenNode)
            {
                ThrowHelper.ThrowArgumentException(nameof(tokens), message: $"tokens must be backed by a GreenSyntaxList.");
            }

            var green = global::Akbura.Language.Syntax.Green.GreenSyntaxFactory.CSharpTypeSyntax(tokens.Node.ToGreenList<GreenSyntaxToken>());
            return Unsafe.As<CSharpTypeSyntax>(green.CreateRed(null, 0));
        }
    }

    internal partial class SyntaxVisitor
    {
        public virtual void VisitCSharpTypeSyntax(CSharpTypeSyntax node)
        {
            DefaultVisit(node);
        }
    }

    internal partial class SyntaxVisitor<TResult>
    {
        public virtual TResult? VisitCSharpTypeSyntax(CSharpTypeSyntax node)
        {
            return DefaultVisit(node);
        }
    }

    internal partial class SyntaxVisitor<TParameter, TResult>
    {
        public virtual TResult? VisitCSharpTypeSyntax(CSharpTypeSyntax node, TParameter argument)
        {
            return DefaultVisit(node, argument);
        }
    }

    internal partial class SyntaxRewriter
    {
        public override AkburaSyntax VisitCSharpTypeSyntax(CSharpTypeSyntax node)
        {
            return node.UpdateCSharpTypeSyntax(VisitList(node.Tokens));
        }
    }
}
#nullable restore
```       
                            
- CSharpBlockSyntax.g.cs
```csharp
// <auto-generated/>

#nullable enable

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Collections.Immutable;
using Akbura.Language.Syntax.Green;

namespace Akbura.Language.Syntax.Green
{
    internal sealed partial class GreenCSharpBlockSyntax : global::Akbura.Language.Syntax.Green.GreenNode
    {
        public readonly global::Akbura.Language.Syntax.Green.GreenSyntaxToken OpenBrace;
        public readonly global::Akbura.Language.Syntax.Green.GreenNode? _tokens;
        public readonly global::Akbura.Language.Syntax.Green.GreenSyntaxToken CloseBrace;

        public GreenCSharpBlockSyntax(
            global::Akbura.Language.Syntax.Green.GreenSyntaxToken openBrace,
            global::Akbura.Language.Syntax.Green.GreenNode? tokens,
            global::Akbura.Language.Syntax.Green.GreenSyntaxToken closeBrace,
            ImmutableArray<global::Akbura.Language.Syntax.AkburaDiagnostic>? diagnostics,
            ImmutableArray<global::Akbura.Language.Syntax.AkburaSyntaxAnnotation>? annotations)
            : base((ushort)global::Akbura.Language.Syntax.SyntaxKind.CSharpBlockSyntax, diagnostics, annotations)
        {
            this.OpenBrace = openBrace;
            this._tokens = tokens;
            this.CloseBrace = closeBrace;

            AkburaDebug.Assert(this.OpenBrace != null);
            AkburaDebug.Assert(this.CloseBrace != null);
            AkburaDebug.Assert(this.OpenBrace.Kind == global::Akbura.Language.Syntax.SyntaxKind.OpenBraceToken);
            AkburaDebug.Assert(this.CloseBrace.Kind == global::Akbura.Language.Syntax.SyntaxKind.CloseBraceToken);

            var flags = Flags;
            var fullWidth = FullWidth;

            AdjustWidthAndFlags(OpenBrace, ref fullWidth, ref flags);

            if (_tokens != null)
            {
                AdjustWidthAndFlags(_tokens, ref fullWidth, ref flags);
            }

            AdjustWidthAndFlags(CloseBrace, ref fullWidth, ref flags);

            SlotCount = 3;
            FullWidth = fullWidth;
            Flags = flags;
        }

        public GreenSyntaxList<GreenSyntaxToken> Tokens => new(_tokens);

        public GreenCSharpBlockSyntax UpdateCSharpBlockSyntax(
            global::Akbura.Language.Syntax.Green.GreenSyntaxToken openBrace,
            global::Akbura.Language.Syntax.Green.GreenNode? tokens,
            global::Akbura.Language.Syntax.Green.GreenSyntaxToken closeBrace)
        {
            if (this.OpenBrace == openBrace && this._tokens == tokens && this.CloseBrace == closeBrace)
            {
                return this;
            }

            var newNode = GreenSyntaxFactory.CSharpBlockSyntax(
                openBrace,
                tokens.ToGreenList<GreenSyntaxToken>(),
                closeBrace);

            var diagnostics = GetDiagnostics();
            if (!diagnostics.IsDefaultOrEmpty)
            {
                newNode = Unsafe.As<GreenCSharpBlockSyntax>(newNode.WithDiagnostics(diagnostics));
            }

            var annotations = GetAnnotations();
            if (!annotations.IsDefaultOrEmpty)
            {
                newNode = Unsafe.As<GreenCSharpBlockSyntax>(newNode.WithAnnotations(annotations));
            }

            return newNode;
        }

        public GreenCSharpBlockSyntax WithOpenBrace(global::Akbura.Language.Syntax.Green.GreenSyntaxToken openBrace)
        {
            return UpdateCSharpBlockSyntax(openBrace, _tokens, CloseBrace);
        }

        public GreenCSharpBlockSyntax WithTokens(global::Akbura.Language.Syntax.Green.GreenSyntaxList<GreenSyntaxToken> tokens)
        {
            return UpdateCSharpBlockSyntax(OpenBrace, tokens.Node, CloseBrace);
        }

        public GreenCSharpBlockSyntax WithCloseBrace(global::Akbura.Language.Syntax.Green.GreenSyntaxToken closeBrace)
        {
            return UpdateCSharpBlockSyntax(OpenBrace, _tokens, closeBrace);
        }

        public override global::Akbura.Language.Syntax.Green.GreenNode? GetSlot(int index)
        {
            return index switch
            {
                0 => OpenBrace,
                1 => _tokens,
                2 => CloseBrace,
                _ => null,
            };
        }

        public override global::Akbura.Language.Syntax.AkburaSyntax CreateRed(global::Akbura.Language.Syntax.AkburaSyntax? parent, int position)
        {
            return new global::Akbura.Language.Syntax.CSharpBlockSyntax(this, parent, position);
        }

        public override global::Akbura.Language.Syntax.Green.GreenNode WithDiagnostics(ImmutableArray<global::Akbura.Language.Syntax.AkburaDiagnostic>? diagnostics)
        {
            return new GreenCSharpBlockSyntax(this.OpenBrace, this._tokens, this.CloseBrace, diagnostics, GetAnnotations());
        }

        public override global::Akbura.Language.Syntax.Green.GreenNode WithAnnotations(ImmutableArray<global::Akbura.Language.Syntax.AkburaSyntaxAnnotation>? annotations)
        {
            return new GreenCSharpBlockSyntax(this.OpenBrace, this._tokens, this.CloseBrace, GetDiagnostics(), annotations);
        }

        public override void Accept(GreenSyntaxVisitor greenSyntaxVisitor)
        {
            greenSyntaxVisitor.VisitCSharpBlockSyntax(this);
        }

        public override TResult? Accept<TResult>(GreenSyntaxVisitor<TResult> greenSyntaxVisitor) where TResult : default
        {
            return greenSyntaxVisitor.VisitCSharpBlockSyntax(this);
        }

        public override TResult? Accept<TParameter, TResult>(GreenSyntaxVisitor<TParameter, TResult> greenSyntaxVisitor, TParameter argument) where TResult : default
        {
            return greenSyntaxVisitor.VisitCSharpBlockSyntax(this, argument);
        }
    }

    internal static partial class GreenSyntaxFactory
    {
        public static GreenCSharpBlockSyntax CSharpBlockSyntax(
            global::Akbura.Language.Syntax.Green.GreenSyntaxToken openBrace,
            global::Akbura.Language.Syntax.Green.GreenSyntaxList<GreenSyntaxToken> tokens,
            global::Akbura.Language.Syntax.Green.GreenSyntaxToken closeBrace)
        {
            Debug.Assert(openBrace != null);
            Debug.Assert(closeBrace != null);
            Debug.Assert(
                openBrace!.Kind == global::Akbura.Language.Syntax.SyntaxKind.OpenBraceToken ||
                false);
            Debug.Assert(
                closeBrace!.Kind == global::Akbura.Language.Syntax.SyntaxKind.CloseBraceToken ||
                false);

            var kind = global::Akbura.Language.Syntax.SyntaxKind.CSharpBlockSyntax;
            int hash;
            var cache = Unsafe.As<GreenCSharpBlockSyntax?>(GreenNodeCache.TryGetNode(
                (ushort)kind,
                openBrace,
                tokens.Node,
                closeBrace,
                out hash));

            if (cache != null)
            {
                return cache;
            }

            var result = new GreenCSharpBlockSyntax(openBrace, tokens.Node, closeBrace, diagnostics: null, annotations: null);

            if (hash > 0)
            {
                GreenNodeCache.AddNode(result, hash);
            }

            return result;
        }
    }

    internal partial class GreenSyntaxVisitor
    {
        public virtual void VisitCSharpBlockSyntax(GreenCSharpBlockSyntax node)
        {
            DefaultVisit(node);
        }
    }

    internal partial class GreenSyntaxVisitor<TResult>
    {
        public virtual TResult? VisitCSharpBlockSyntax(GreenCSharpBlockSyntax node)
        {
            return DefaultVisit(node);
        }
    }

    internal partial class GreenSyntaxVisitor<TParameter, TResult>
    {
        public virtual TResult? VisitCSharpBlockSyntax(GreenCSharpBlockSyntax node, TParameter argument)
        {
            return DefaultVisit(node, argument);
        }
    }

    internal partial class GreenSyntaxRewriter
    {
        public override GreenNode? VisitCSharpBlockSyntax(GreenCSharpBlockSyntax node)
        {
            return node.UpdateCSharpBlockSyntax((GreenSyntaxToken)VisitToken(node.OpenBrace), VisitList(node.Tokens).Node, (GreenSyntaxToken)VisitToken(node.CloseBrace))
;        }
    }
}

namespace Akbura.Language.Syntax
{
    internal sealed partial class CSharpBlockSyntax : global::Akbura.Language.Syntax.AkburaSyntax
    {
        public CSharpBlockSyntax(global::Akbura.Language.Syntax.Green.GreenCSharpBlockSyntax greenNode, global::Akbura.Language.Syntax.AkburaSyntax? parent, int position)
            : base(greenNode, parent, position)
        {
        }

        internal new global::Akbura.Language.Syntax.Green.GreenCSharpBlockSyntax Green
            => Unsafe.As<global::Akbura.Language.Syntax.Green.GreenCSharpBlockSyntax>(base.Green);

        public SyntaxToken OpenBrace
            => new(this, this.Green.OpenBrace, GetChildPosition(0), GetChildIndex(0));

        public SyntaxTokenList Tokens
        {
            get
            {
                var tokens = this.Green.GetSlot(1);
                return new SyntaxTokenList(this, tokens, GetChildPosition(1), GetChildIndex(1));
            }
        }

        public SyntaxToken CloseBrace => new(this, this.Green.CloseBrace, GetChildPositionFromEnd(2), GetChildIndex(2));

        public CSharpBlockSyntax UpdateCSharpBlockSyntax(
            SyntaxToken openBrace,
            SyntaxTokenList tokens,
            SyntaxToken closeBrace)
        {
            if (this.OpenBrace == openBrace && this.Tokens == tokens && this.CloseBrace == closeBrace)
            {
                return this;
            }

            var newNode = SyntaxFactory.CSharpBlockSyntax(openBrace, tokens, closeBrace);

            var annotations = this.GetAnnotations();
            if (!annotations.IsDefaultOrEmpty)
            {
                newNode = (CSharpBlockSyntax)newNode.WithAnnotations(annotations);
            }

            var diagnostics = this.GetDiagnostics();
            if (!diagnostics.IsDefaultOrEmpty)
            {
                newNode = (CSharpBlockSyntax)newNode.WithDiagnostics(diagnostics);
            }

            return newNode;
        }

        public CSharpBlockSyntax WithOpenBrace(SyntaxToken openBrace)
        {
            return UpdateCSharpBlockSyntax(openBrace, this.Tokens, this.CloseBrace);
        }

        public CSharpBlockSyntax WithTokens(SyntaxTokenList tokens)
        {
            return UpdateCSharpBlockSyntax(this.OpenBrace, tokens, this.CloseBrace);
        }

        public CSharpBlockSyntax WithCloseBrace(SyntaxToken closeBrace)
        {
            return UpdateCSharpBlockSyntax(this.OpenBrace, this.Tokens, closeBrace);
        }

        public override global::Akbura.Language.Syntax.AkburaSyntax? GetNodeSlot(int index)
        {
            return index switch
            {
                _ => null,
            };
        }

        public override global::Akbura.Language.Syntax.AkburaSyntax? GetCachedSlot(int index)
        {
            return index switch
            {
                _ => null,
            };
        }

        public override void Accept(SyntaxVisitor visitor)
        {
            visitor.VisitCSharpBlockSyntax(this);
        }

        public override TResult? Accept<TResult>(SyntaxVisitor<TResult> visitor) where TResult : default
        {
            return visitor.VisitCSharpBlockSyntax(this);
        }

        public override TResult? Accept<TParameter, TResult>(SyntaxVisitor<TParameter, TResult> visitor, TParameter argument) where TResult : default
        {
            return visitor.VisitCSharpBlockSyntax(this, argument);
        }
    }

    internal static partial class SyntaxFactory
    {
        internal static CSharpBlockSyntax CSharpBlockSyntax(
            SyntaxToken openBrace,
            SyntaxTokenList tokens,
            SyntaxToken closeBrace)
        {
            if (openBrace.Node is not global::Akbura.Language.Syntax.Green.GreenSyntaxToken)
            {
                ThrowHelper.ThrowArgumentException(nameof(openBrace), message: $"openBrace must be a GreenSyntaxToken. Use SyntaxFactory.Token(...)?");
            }

            if (closeBrace.Node is not global::Akbura.Language.Syntax.Green.GreenSyntaxToken)
            {
                ThrowHelper.ThrowArgumentException(nameof(closeBrace), message: $"closeBrace must be a GreenSyntaxToken. Use SyntaxFactory.Token(...)?");
            }

            if (openBrace.RawKind != (ushort)SyntaxKind.OpenBraceToken)
            {
                ThrowHelper.ThrowArgumentException(nameof(openBrace), message: $"openBrace must be SyntaxKind.OpenBraceToken");
            }

            if (closeBrace.RawKind != (ushort)SyntaxKind.CloseBraceToken)
            {
                ThrowHelper.ThrowArgumentException(nameof(closeBrace), message: $"closeBrace must be SyntaxKind.CloseBraceToken");
            }

            if (tokens != default && tokens.Node is not global::Akbura.Language.Syntax.Green.GreenSyntaxList)
            {
                ThrowHelper.ThrowArgumentException(nameof(tokens), message: $"tokens must be backed by a GreenSyntaxList.");
            }

            var green = global::Akbura.Language.Syntax.Green.GreenSyntaxFactory.CSharpBlockSyntax(
                Unsafe.As<global::Akbura.Language.Syntax.Green.GreenSyntaxToken>(openBrace.Node!),
                tokens.Node.ToGreenList<GreenSyntaxToken>(),
                Unsafe.As<global::Akbura.Language.Syntax.Green.GreenSyntaxToken>(closeBrace.Node!));

            return Unsafe.As<CSharpBlockSyntax>(green.CreateRed());
        }
    }

    internal partial class SyntaxVisitor
    {
        public virtual void VisitCSharpBlockSyntax(CSharpBlockSyntax node)
        {
            DefaultVisit(node);
        }
    }

    internal partial class SyntaxVisitor<TResult>
    {
        public virtual TResult? VisitCSharpBlockSyntax(CSharpBlockSyntax node)
        {
            return DefaultVisit(node);
        }
    }

    internal partial class SyntaxVisitor<TParameter, TResult>
    {
        public virtual TResult? VisitCSharpBlockSyntax(CSharpBlockSyntax node, TParameter argument)
        {
            return DefaultVisit(node, argument);
        }
    }

    internal partial class SyntaxRewriter
    {
        public override AkburaSyntax? VisitCSharpBlockSyntax(CSharpBlockSyntax node)
        {
            return node.UpdateCSharpBlockSyntax(VisitToken(node.OpenBrace), VisitList(node.Tokens), VisitToken(node.CloseBrace));
        }
    }
}
#nullable restore 
```
                            
- MarkupSyntaxNode.g.cs
```csharp
#nullable enable

using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Akbura.Language.Syntax.Green
{
    internal abstract partial class GreenMarkupSyntaxNodeSyntax
        : global::Akbura.Language.Syntax.Green.GreenNode
    {
        protected GreenMarkupSyntaxNodeSyntax(
            ushort kind,
            ImmutableArray<AkburaDiagnostic>? diagnostics,
            ImmutableArray<AkburaSyntaxAnnotation>? annotations)
            : base(kind, diagnostics, annotations)
        {
        }
    }
}

namespace Akbura.Language.Syntax
{
    internal abstract partial class MarkupSyntaxNodeSyntax
        : global::Akbura.Language.Syntax.AkburaSyntax
    {
        protected MarkupSyntaxNodeSyntax(
            global::Akbura.Language.Syntax.Green.GreenMarkupSyntaxNodeSyntax green,
            AkburaSyntax? parent,
            int position)
            : base(green, parent, position)
        {
        }

        internal new global::Akbura.Language.Syntax.Green.GreenMarkupSyntaxNodeSyntax Green
            => Unsafe.As<global::Akbura.Language.Syntax.Green.GreenMarkupSyntaxNodeSyntax>(base.Green);
    }
}

#nullable restore
```