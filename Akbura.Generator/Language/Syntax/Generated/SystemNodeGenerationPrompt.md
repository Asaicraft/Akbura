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

6.1 Avoid hiding System.Object members

When generating identifiers for token fields/properties, NEVER use names that would hide members from `System.Object` (e.g. `Equals`, `GetHashCode`, `ToString`).

For the '=' token (SyntaxKind.EqualsToken) specifically:
- Use `EqualsToken` instead of `Equals` for all fields, properties, parameters, and With/Update methods.


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

11) Factory
Generate GreenSyntaxFactory.<NodeName>(...)
Generate SyntaxFactory.<NodeName>(...)
Both must respect null checks, kind checks, and existing helper patterns (GreenNodeCache, ToGreenList<T>(), etc.), but MUST NOT invent new helper types.
When generating GreenSyntaxFactory.<NodeName>(...) you MUST:
- Use GreenNodeCache.TryGetNode / GreenNodeCache.AddNode ONLY if the node has at most 3 child slots (SlotCount <= 3).
- For nodes with more than 3 child slots, construct the green node directly without using GreenNodeCache.


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

14. WithLeadingTrivia / WithTrailingTrivia
For every RED node (both abstract and concrete), generate the following methods:
```csharp
public new XxxSyntax WithLeadingTrivia(SyntaxTriviaList trivia)
{
    return (XxxSyntax)base.WithLeadingTrivia(trivia);
}

public new XxxSyntax WithTrailingTrivia(SyntaxTriviaList trivia)
{
    return (XxxSyntax)base.WithTrailingTrivia(trivia);
}
```

where XxxSyntax is the corresponding RED node type (e.g., AkburaDocumentSyntax, UseEffectDeclarationSyntax, StateInitializerSyntax, SimpleStateInitializerSyntax, etc.).

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
abstract node StateInitializer
{
    Expression : CSharpExpressionSyntax;
}

node SimpleStateInitializer: StateInitializer;

node BindableStateInitializer: StateInitializer
{
    BindingKeyword : InToken | OutToken | BindToken;
    Expression     : CSharpExpressionSyntax;
}
                            
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

/// <summary>
/// Entire .akbura compilation unit.
/// </summary>
node AkburaDocumentSyntax {
    Members   : syntaxlist<AkTopLevelMember>;
    /// <summary> 
    /// End-of-file token provided by the host; we reference it as a generic Token.
    /// </summary>
    EndOfFile : Token;
}

/// <summary>inject ILogger&lt;ProfileWithTasks&gt; log;</summary>
node InjectDeclarationSyntax : AkTopLevelMember {
    InjectKeyword : InjectKeyword;
    Type          : CSharpTypeSyntax;
    Name          : SimpleName;
    Semicolon     : SemicolonToken;
}

/// <summary>
/// useEffect($cancel, UserId, tasks) { ... }
/// cancel { ... }
/// finally { ... }
/// </summary>
node UseEffectDeclarationSyntax : AkTopLevelMember {
    UseEffectKeyword : UseEffectKeyword;
    OpenParen        : OpenParenToken;
    // Dependency list: e.g., $cancel, UserId, tasks
    Parameters       : syntaxlist<SimpleName, CommaToken>;
    CloseParen       : CloseParenToken;
    Body             : CSharpBlockSyntax;
    CancelBlock?     : EffectCancelBlockSyntax;
    FinallyBlock?    : EffectFinallyBlockSyntax;
}
                            
Will generate this files:

- StateInitializer.g.cs
```csharp
// <auto-generated/>

#nullable enable

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Collections.Immutable;

namespace Akbura.Language.Syntax.Green
{
    internal abstract partial class GreenStateInitializerSyntax : global::Akbura.Language.Syntax.Green.GreenNode
    {
        public readonly global::Akbura.Language.Syntax.Green.GreenCSharpExpressionSyntax Expression;

        protected GreenStateInitializerSyntax(
            global::Akbura.Language.Syntax.Green.GreenCSharpExpressionSyntax expression,
            ushort kind,
            ImmutableArray<global::Akbura.Language.Syntax.AkburaDiagnostic>? diagnostics,
            ImmutableArray<global::Akbura.Language.Syntax.AkburaSyntaxAnnotation>? annotations)
            : base(kind, diagnostics, annotations)
        {
            this.Expression = expression;

            AkburaDebug.Assert(this.Expression != null);

            var flags = Flags;
            var fullWidth = FullWidth;

            AdjustWidthAndFlags(Expression, ref fullWidth, ref flags);

            SlotCount = 1;
            FullWidth = fullWidth;
            Flags = flags;
        }

        public GreenStateInitializerSyntax WithExpression(global::Akbura.Language.Syntax.Green.GreenCSharpExpressionSyntax expression)
        {
            return UpdateStateInitializerSyntax(expression);
        }

        public abstract GreenStateInitializerSyntax UpdateStateInitializerSyntax(
            global::Akbura.Language.Syntax.Green.GreenCSharpExpressionSyntax expression);

        public override global::Akbura.Language.Syntax.Green.GreenNode? GetSlot(int index)
        {
            return index switch
            {
                0 => Expression,
                _ => null,
            };
        }
    }
}

namespace Akbura.Language.Syntax
{
    internal abstract partial class StateInitializerSyntax : global::Akbura.Language.Syntax.AkburaSyntax
    {
        protected StateInitializerSyntax(
            global::Akbura.Language.Syntax.Green.GreenStateInitializerSyntax greenNode,
            global::Akbura.Language.Syntax.AkburaSyntax? parent,
            int position)
            : base(greenNode, parent, position)
        {
        }

        internal new global::Akbura.Language.Syntax.Green.GreenStateInitializerSyntax Green
            => Unsafe.As<global::Akbura.Language.Syntax.Green.GreenStateInitializerSyntax>(base.Green);

        public abstract CSharpExpressionSyntax Expression { get; }

        public StateInitializerSyntax WithExpression(global::Akbura.Language.Syntax.CSharpExpressionSyntax expression)
        {
            return UpdateStateInitializerSyntax(expression);
        }

        public abstract StateInitializerSyntax UpdateStateInitializerSyntax(
            global::Akbura.Language.Syntax.CSharpExpressionSyntax expression);

        public override global::Akbura.Language.Syntax.AkburaSyntax? GetNodeSlot(int index)
        {
            return index switch
            {
                0 => Expression,
                _ => null,
            };
        }

        public override global::Akbura.Language.Syntax.AkburaSyntax? GetCachedSlot(int index)
        {
            return null;
        }

        public new StateInitializerSyntax WithLeadingTrivia(SyntaxTriviaList trivia)
        {
            return (StateInitializerSyntax)base.WithLeadingTrivia(trivia);
        }

        public new StateInitializerSyntax WithTrailingTrivia(SyntaxTriviaList trivia)
        {
            return (StateInitializerSyntax)base.WithTrailingTrivia(trivia);
        }
    }
}

#nullable restore
```

- SimpleStateInitializer.g.cs
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
    internal sealed partial class GreenSimpleStateInitializerSyntax : global::Akbura.Language.Syntax.Green.GreenStateInitializerSyntax
    {
        public GreenSimpleStateInitializerSyntax(
            global::Akbura.Language.Syntax.Green.GreenCSharpExpressionSyntax expression,
            ImmutableArray<global::Akbura.Language.Syntax.AkburaDiagnostic>? diagnostics,
            ImmutableArray<global::Akbura.Language.Syntax.AkburaSyntaxAnnotation>? annotations)
            : base(expression, (ushort)global::Akbura.Language.Syntax.SyntaxKind.SimpleStateInitializer, diagnostics, annotations)
        {
            AkburaDebug.Assert(this.Expression != null);

            var flags = Flags;
            var fullWidth = FullWidth;

            AdjustWidthAndFlags(Expression, ref fullWidth, ref flags);

            SlotCount = 1;
            FullWidth = fullWidth;
            Flags = flags;
        }

        public new GreenSimpleStateInitializerSyntax WithExpression(global::Akbura.Language.Syntax.Green.GreenCSharpExpressionSyntax expression)
        {
            return UpdateSimpleStateInitializerSyntax(expression);
        }

        public GreenStateInitializerSyntax UpdateStateInitializer(global::Akbura.Language.Syntax.Green.GreenCSharpExpressionSyntax expression)
        {
            return UpdateSimpleStateInitializerSyntax(expression);
        }

        public override GreenStateInitializerSyntax UpdateStateInitializerSyntax(GreenCSharpExpressionSyntax expression)
        {
            return UpdateSimpleStateInitializerSyntax(expression);
        }

        public GreenSimpleStateInitializerSyntax UpdateSimpleStateInitializerSyntax(global::Akbura.Language.Syntax.Green.GreenCSharpExpressionSyntax expression)
        {
            if (this.Expression == expression)
            {
                return this;
            }

            var newNode = GreenSyntaxFactory.SimpleStateInitializerSyntax(expression);
            var diagnostics = GetDiagnostics();

            if (!diagnostics.IsDefaultOrEmpty)
            {
                newNode = Unsafe.As<GreenSimpleStateInitializerSyntax>(newNode.WithDiagnostics(diagnostics));
            }

            var annotations = GetAnnotations();
            if (!annotations.IsDefaultOrEmpty)
            {
                newNode = Unsafe.As<GreenSimpleStateInitializerSyntax>(newNode.WithAnnotations(annotations));
            }

            return newNode;
        }

        public override global::Akbura.Language.Syntax.Green.GreenNode? GetSlot(int index)
        {
            return index switch
            {
                0 => Expression,
                _ => null,
            };
        }

        public override global::Akbura.Language.Syntax.AkburaSyntax CreateRed(global::Akbura.Language.Syntax.AkburaSyntax? parent, int position)
        {
            return new global::Akbura.Language.Syntax.SimpleStateInitializerSyntax(this, parent, position);
        }

        public override global::Akbura.Language.Syntax.Green.GreenNode WithDiagnostics(ImmutableArray<global::Akbura.Language.Syntax.AkburaDiagnostic>? diagnostics)
        {
            return new GreenSimpleStateInitializerSyntax(this.Expression, diagnostics, GetAnnotations());
        }

        public override global::Akbura.Language.Syntax.Green.GreenNode WithAnnotations(ImmutableArray<global::Akbura.Language.Syntax.AkburaSyntaxAnnotation>? annotations)
        {
            return new GreenSimpleStateInitializerSyntax(this.Expression, GetDiagnostics(), annotations);
        }

        public override void Accept(GreenSyntaxVisitor greenSyntaxVisitor)
        {
            greenSyntaxVisitor.VisitSimpleStateInitializerSyntax(this);
        }

        public override TResult? Accept<TResult>(GreenSyntaxVisitor<TResult> greenSyntaxVisitor) where TResult : default
        {
            return greenSyntaxVisitor.VisitSimpleStateInitializerSyntax(this);
        }

        public override TResult? Accept<TParameter, TResult>(GreenSyntaxVisitor<TParameter, TResult> greenSyntaxVisitor, TParameter argument) where TResult : default
        {
            return greenSyntaxVisitor.VisitSimpleStateInitializerSyntax(this, argument);
        }
    }

    internal static partial class GreenSyntaxFactory
    {
        public static GreenSimpleStateInitializerSyntax SimpleStateInitializerSyntax(
            global::Akbura.Language.Syntax.Green.GreenCSharpExpressionSyntax expression)
        {
            AkburaDebug.Assert(expression != null);

            var kind = global::Akbura.Language.Syntax.SyntaxKind.SimpleStateInitializer;
            int hash;
            var cache = Unsafe.As<GreenSimpleStateInitializerSyntax?>(
                GreenNodeCache.TryGetNode(
                    (ushort)kind,
                    expression,
                    out hash));

            if (cache != null)
            {
                return cache;
            }

            var result = new GreenSimpleStateInitializerSyntax(
                expression,
                diagnostics: null,
                annotations: null);

            if (hash > 0)
            {
                GreenNodeCache.AddNode(result, hash);
            }

            return result;
        }
    }

    internal partial class GreenSyntaxVisitor
    {
        public virtual void VisitSimpleStateInitializerSyntax(GreenSimpleStateInitializerSyntax node)
        {
            DefaultVisit(node);
        }
    }

    internal partial class GreenSyntaxVisitor<TResult>
    {
        public virtual TResult? VisitSimpleStateInitializerSyntax(GreenSimpleStateInitializerSyntax node)
        {
            return DefaultVisit(node);
        }
    }

    internal partial class GreenSyntaxVisitor<TParameter, TResult>
    {
        public virtual TResult? VisitSimpleStateInitializerSyntax(GreenSimpleStateInitializerSyntax node, TParameter argument)
        {
            return DefaultVisit(node, argument);
        }
    }

    internal partial class GreenSyntaxRewriter
    {
        public override GreenNode? VisitSimpleStateInitializerSyntax(GreenSimpleStateInitializerSyntax node)
        {
            return node.UpdateSimpleStateInitializerSyntax(
                (GreenCSharpExpressionSyntax)Visit(node.Expression)!);
        }
    }
}

namespace Akbura.Language.Syntax
{
    internal sealed partial class SimpleStateInitializerSyntax : global::Akbura.Language.Syntax.StateInitializerSyntax
    {
        private CSharpExpressionSyntax? _expression;

        public SimpleStateInitializerSyntax(
            global::Akbura.Language.Syntax.Green.GreenSimpleStateInitializerSyntax greenNode,
            global::Akbura.Language.Syntax.AkburaSyntax? parent,
            int position)
            : base(greenNode, parent, position)
        {
        }

        internal new global::Akbura.Language.Syntax.Green.GreenSimpleStateInitializerSyntax Green
            => Unsafe.As<global::Akbura.Language.Syntax.Green.GreenSimpleStateInitializerSyntax>(base.Green);

        public override CSharpExpressionSyntax Expression
            => (CSharpExpressionSyntax)GetRed(ref _expression, 0)!;

        public override global::Akbura.Language.Syntax.AkburaSyntax? GetNodeSlot(int index)
        {
            return index switch
            {
                0 => GetRed(ref _expression, 0),
                _ => null,
            };
        }

        public override global::Akbura.Language.Syntax.AkburaSyntax? GetCachedSlot(int index)
        {
            return index switch
            {
                0 => _expression,
                _ => null,
            };
        }

        public new SimpleStateInitializerSyntax WithExpression(CSharpExpressionSyntax expression)
        {
            return UpdateSimpleStateInitializerSyntax(expression);
        }

        public override StateInitializerSyntax UpdateStateInitializerSyntax(CSharpExpressionSyntax expression)
        {
            return UpdateSimpleStateInitializerSyntax(expression);
        }

        public SimpleStateInitializerSyntax UpdateSimpleStateInitializerSyntax(CSharpExpressionSyntax expression)
        {
            if (this.Expression == expression)
            {
                return this;
            }

            if (expression is null)
            {
                ThrowHelper.ThrowArgumentNullException(nameof(expression));
            }

            var newNode = SyntaxFactory.SimpleStateInitializerSyntax(expression);

            var annotations = this.GetAnnotations();
            if (!annotations.IsDefaultOrEmpty)
            {
                newNode = (SimpleStateInitializerSyntax)newNode.WithAnnotations(annotations);
            }

            var diagnostics = this.GetDiagnostics();
            if (!diagnostics.IsDefaultOrEmpty)
            {
                newNode = (SimpleStateInitializerSyntax)newNode.WithDiagnostics(diagnostics);
            }

            return newNode;
        }

        public override void Accept(SyntaxVisitor visitor)
        {
            visitor.VisitSimpleStateInitializerSyntax(this);
        }

        public override TResult? Accept<TResult>(SyntaxVisitor<TResult> visitor) where TResult : default
        {
            return visitor.VisitSimpleStateInitializerSyntax(this);
        }

        public override TResult? Accept<TParameter, TResult>(SyntaxVisitor<TParameter, TResult> visitor, TParameter argument) where TResult : default
        {
            return visitor.VisitSimpleStateInitializerSyntax(this, argument);
        }

        public new SimpleStateInitializerSyntax WithLeadingTrivia(SyntaxTriviaList trivia)
        {
            return (SimpleStateInitializerSyntax)base.WithLeadingTrivia(trivia);
        }

        public new SimpleStateInitializerSyntax WithTrailingTrivia(SyntaxTriviaList trivia)
        {
            return (SimpleStateInitializerSyntax)base.WithTrailingTrivia(trivia);
        }

    }

    internal static partial class SyntaxFactory
    {
        internal static SimpleStateInitializerSyntax SimpleStateInitializerSyntax(
            CSharpExpressionSyntax expression)
        {
            if (expression is null)
            {
                ThrowHelper.ThrowArgumentNullException(nameof(expression));
            }

            var green = global::Akbura.Language.Syntax.Green.GreenSyntaxFactory.SimpleStateInitializerSyntax(
                expression.Green);

            return Unsafe.As<SimpleStateInitializerSyntax>(green.CreateRed(null, 0));
        }
    }

    internal partial class SyntaxVisitor
    {
        public virtual void VisitSimpleStateInitializerSyntax(SimpleStateInitializerSyntax node)
        {
            DefaultVisit(node);
        }
    }

    internal partial class SyntaxVisitor<TResult>
    {
        public virtual TResult? VisitSimpleStateInitializerSyntax(SimpleStateInitializerSyntax node)
        {
            return DefaultVisit(node);
        }
    }

    internal partial class SyntaxVisitor<TParameter, TResult>
    {
        public virtual TResult? VisitSimpleStateInitializerSyntax(SimpleStateInitializerSyntax node, TParameter argument)
        {
            return DefaultVisit(node, argument);
        }
    }

    internal partial class SyntaxRewriter
    {
        public override AkburaSyntax? VisitSimpleStateInitializerSyntax(SimpleStateInitializerSyntax node)
        {
            return node.UpdateSimpleStateInitializerSyntax(
                (CSharpExpressionSyntax)Visit(node.Expression)!);
        }
    }
}

#nullable restore
```

- BindableStateInitializer.g.cs
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
    internal sealed partial class GreenBindableStateInitializerSyntax : global::Akbura.Language.Syntax.Green.GreenStateInitializerSyntax
    {
        public readonly global::Akbura.Language.Syntax.Green.GreenSyntaxToken BindingKeyword;

        public GreenBindableStateInitializerSyntax(
            global::Akbura.Language.Syntax.Green.GreenSyntaxToken bindingKeyword,
            global::Akbura.Language.Syntax.Green.GreenCSharpExpressionSyntax expression,
            ImmutableArray<global::Akbura.Language.Syntax.AkburaDiagnostic>? diagnostics,
            ImmutableArray<global::Akbura.Language.Syntax.AkburaSyntaxAnnotation>? annotations)
            : base(expression, (ushort)global::Akbura.Language.Syntax.SyntaxKind.BindableStateInitializer, diagnostics, annotations)
        {
            this.BindingKeyword = bindingKeyword;

            AkburaDebug.Assert(this.BindingKeyword != null);
            AkburaDebug.Assert(this.Expression != null);

            AkburaDebug.Assert(
                this.BindingKeyword.Kind == global::Akbura.Language.Syntax.SyntaxKind.InToken ||
                this.BindingKeyword.Kind == global::Akbura.Language.Syntax.SyntaxKind.OutToken ||
                this.BindingKeyword.Kind == global::Akbura.Language.Syntax.SyntaxKind.BindToken);

            var flags = Flags;
            var fullWidth = FullWidth;

            AdjustWidthAndFlags(BindingKeyword, ref fullWidth, ref flags);
            AdjustWidthAndFlags(Expression, ref fullWidth, ref flags);

            SlotCount = 2;
            FullWidth = fullWidth;
            Flags = flags;
        }

        public override GreenStateInitializerSyntax UpdateStateInitializerSyntax(GreenCSharpExpressionSyntax expression)
        {
            return UpdateBindableStateInitializerSyntax(this.BindingKeyword, expression);
        }

        public GreenBindableStateInitializerSyntax UpdateBindableStateInitializerSyntax(
            global::Akbura.Language.Syntax.Green.GreenSyntaxToken bindingKeyword,
            global::Akbura.Language.Syntax.Green.GreenCSharpExpressionSyntax expression)
        {
            if (this.BindingKeyword == bindingKeyword &&
                this.Expression == expression)
            {
                return this;
            }

            var newNode = GreenSyntaxFactory.BindableStateInitializerSyntax(
                bindingKeyword,
                expression);

            var diagnostics = GetDiagnostics();
            if (!diagnostics.IsDefaultOrEmpty)
            {
                newNode = Unsafe.As<GreenBindableStateInitializerSyntax>(newNode.WithDiagnostics(diagnostics));
            }

            var annotations = GetAnnotations();
            if (!annotations.IsDefaultOrEmpty)
            {
                newNode = Unsafe.As<GreenBindableStateInitializerSyntax>(newNode.WithAnnotations(annotations));
            }

            return newNode;
        }

        public GreenBindableStateInitializerSyntax WithBindingKeyword(global::Akbura.Language.Syntax.Green.GreenSyntaxToken bindingKeyword)
        {
            return UpdateBindableStateInitializerSyntax(bindingKeyword, this.Expression);
        }

        public new GreenBindableStateInitializerSyntax WithExpression(global::Akbura.Language.Syntax.Green.GreenCSharpExpressionSyntax expression)
        {
            return UpdateBindableStateInitializerSyntax(this.BindingKeyword, expression);
        }

        public override global::Akbura.Language.Syntax.Green.GreenNode? GetSlot(int index)
        {
            return index switch
            {
                0 => BindingKeyword,
                1 => Expression,
                _ => null,
            };
        }

        public override global::Akbura.Language.Syntax.AkburaSyntax CreateRed(global::Akbura.Language.Syntax.AkburaSyntax? parent, int position)
        {
            return new global::Akbura.Language.Syntax.BindableStateInitializerSyntax(this, parent, position);
        }

        public override global::Akbura.Language.Syntax.Green.GreenNode WithDiagnostics(ImmutableArray<global::Akbura.Language.Syntax.AkburaDiagnostic>? diagnostics)
        {
            return new GreenBindableStateInitializerSyntax(this.BindingKeyword, this.Expression, diagnostics, GetAnnotations());
        }

        public override global::Akbura.Language.Syntax.Green.GreenNode WithAnnotations(ImmutableArray<global::Akbura.Language.Syntax.AkburaSyntaxAnnotation>? annotations)
        {
            return new GreenBindableStateInitializerSyntax(this.BindingKeyword, this.Expression, GetDiagnostics(), annotations);
        }

        public override void Accept(GreenSyntaxVisitor greenSyntaxVisitor)
        {
            greenSyntaxVisitor.VisitBindableStateInitializerSyntax(this);
        }

        public override TResult? Accept<TResult>(GreenSyntaxVisitor<TResult> greenSyntaxVisitor) where TResult : default
        {
            return greenSyntaxVisitor.VisitBindableStateInitializerSyntax(this);
        }

        public override TResult? Accept<TParameter, TResult>(GreenSyntaxVisitor<TParameter, TResult> greenSyntaxVisitor, TParameter argument) where TResult : default
        {
            return greenSyntaxVisitor.VisitBindableStateInitializerSyntax(this, argument);
        }
    }

    internal static partial class GreenSyntaxFactory
    {
        public static GreenBindableStateInitializerSyntax BindableStateInitializerSyntax(
            global::Akbura.Language.Syntax.Green.GreenSyntaxToken bindingKeyword,
            global::Akbura.Language.Syntax.Green.GreenCSharpExpressionSyntax expression)
        {
            AkburaDebug.Assert(bindingKeyword != null);
            AkburaDebug.Assert(expression != null);

            AkburaDebug.Assert(
                bindingKeyword!.Kind == global::Akbura.Language.Syntax.SyntaxKind.InToken ||
                bindingKeyword!.Kind == global::Akbura.Language.Syntax.SyntaxKind.OutToken ||
                bindingKeyword!.Kind == global::Akbura.Language.Syntax.SyntaxKind.BindToken);

            var kind = global::Akbura.Language.Syntax.SyntaxKind.BindableStateInitializer;
            int hash;
            var cache = Unsafe.As<GreenBindableStateInitializerSyntax?>(
                GreenNodeCache.TryGetNode(
                    (ushort)kind,
                    bindingKeyword,
                    expression,
                    out hash));

            if (cache != null)
            {
                return cache;
            }

            var result = new GreenBindableStateInitializerSyntax(
                bindingKeyword,
                expression,
                diagnostics: null,
                annotations: null);

            if (hash > 0)
            {
                GreenNodeCache.AddNode(result, hash);
            }

            return result;
        }
    }

    internal partial class GreenSyntaxVisitor
    {
        public virtual void VisitBindableStateInitializerSyntax(GreenBindableStateInitializerSyntax node)
        {
            DefaultVisit(node);
        }
    }

    internal partial class GreenSyntaxVisitor<TResult>
    {
        public virtual TResult? VisitBindableStateInitializerSyntax(GreenBindableStateInitializerSyntax node)
        {
            return DefaultVisit(node);
        }
    }

    internal partial class GreenSyntaxVisitor<TParameter, TResult>
    {
        public virtual TResult? VisitBindableStateInitializerSyntax(GreenBindableStateInitializerSyntax node, TParameter argument)
        {
            return DefaultVisit(node, argument);
        }
    }

    internal partial class GreenSyntaxRewriter
    {
        public override GreenNode? VisitBindableStateInitializerSyntax(GreenBindableStateInitializerSyntax node)
        {
            return node.UpdateBindableStateInitializerSyntax(
                (GreenSyntaxToken)VisitToken(node.BindingKeyword),
                (GreenCSharpExpressionSyntax)Visit(node.Expression)!);
        }
    }
}

namespace Akbura.Language.Syntax
{
    internal sealed partial class BindableStateInitializerSyntax : global::Akbura.Language.Syntax.StateInitializerSyntax
    {
        private AkburaSyntax? _expression;

        public BindableStateInitializerSyntax(
            global::Akbura.Language.Syntax.Green.GreenBindableStateInitializerSyntax greenNode,
            global::Akbura.Language.Syntax.AkburaSyntax? parent,
            int position)
            : base(greenNode, parent, position)
        {
        }

        internal new global::Akbura.Language.Syntax.Green.GreenBindableStateInitializerSyntax Green
            => Unsafe.As<global::Akbura.Language.Syntax.Green.GreenBindableStateInitializerSyntax>(base.Green);

        public SyntaxToken BindingKeyword
            => new(this, this.Green.BindingKeyword, GetChildPosition(0), GetChildIndex(0));

        public override CSharpExpressionSyntax Expression
            => (CSharpExpressionSyntax)GetRed(ref _expression, 1)!;

        public override StateInitializerSyntax UpdateStateInitializerSyntax(CSharpExpressionSyntax expression)
        {
            return UpdateBindableStateInitializerSyntax(this.BindingKeyword, expression);
        }

        public BindableStateInitializerSyntax UpdateBindableStateInitializerSyntax(
            SyntaxToken bindingKeyword,
            CSharpExpressionSyntax expression)
        {
            if (this.BindingKeyword == bindingKeyword &&
                this.Expression == expression)
            {
                return this;
            }

            var newNode = SyntaxFactory.BindableStateInitializerSyntax(
                bindingKeyword,
                expression);

            var annotations = this.GetAnnotations();
            if (!annotations.IsDefaultOrEmpty)
            {
                newNode = (BindableStateInitializerSyntax)newNode.WithAnnotations(annotations);
            }

            var diagnostics = this.GetDiagnostics();
            if (!diagnostics.IsDefaultOrEmpty)
            {
                newNode = (BindableStateInitializerSyntax)newNode.WithDiagnostics(diagnostics);
            }

            return newNode;
        }

        public BindableStateInitializerSyntax WithBindingKeyword(SyntaxToken bindingKeyword)
        {
            return UpdateBindableStateInitializerSyntax(bindingKeyword, this.Expression);
        }

        public new BindableStateInitializerSyntax WithExpression(CSharpExpressionSyntax expression)
        {
            return UpdateBindableStateInitializerSyntax(this.BindingKeyword, expression);
        }

        public override global::Akbura.Language.Syntax.AkburaSyntax? GetNodeSlot(int index)
        {
            return index switch
            {
                1 => GetRed(ref _expression, 1),
                _ => null,
            };
        }

        public override global::Akbura.Language.Syntax.AkburaSyntax? GetCachedSlot(int index)
        {
            return index switch
            {
                1 => _expression,
                _ => null,
            };
        }

        public override void Accept(SyntaxVisitor visitor)
        {
            visitor.VisitBindableStateInitializerSyntax(this);
        }

        public override TResult? Accept<TResult>(SyntaxVisitor<TResult> visitor) where TResult : default
        {
            return visitor.VisitBindableStateInitializerSyntax(this);
        }

        public override TResult? Accept<TParameter, TResult>(SyntaxVisitor<TParameter, TResult> visitor, TParameter argument) where TResult : default
        {
            return visitor.VisitBindableStateInitializerSyntax(this, argument);
        }

        public new BindableStateInitializerSyntax WithLeadingTrivia(SyntaxTriviaList trivia)
        {
            return (BindableStateInitializerSyntax)base.WithLeadingTrivia(trivia);
        }

        public new BindableStateInitializerSyntax WithTrailingTrivia(SyntaxTriviaList trivia)
        {
            return (BindableStateInitializerSyntax)base.WithTrailingTrivia(trivia);
        }
    }

    internal static partial class SyntaxFactory
    {
        internal static BindableStateInitializerSyntax BindableStateInitializerSyntax(
            SyntaxToken bindingKeyword,
            CSharpExpressionSyntax expression)
        {
            if (bindingKeyword.Node is not global::Akbura.Language.Syntax.Green.GreenSyntaxToken)
            {
                ThrowHelper.ThrowArgumentException(nameof(bindingKeyword), message: $"bindingKeyword must be a GreenSyntaxToken. Use SyntaxFactory.Token(...)?");
            }

            if (bindingKeyword.RawKind != (ushort)SyntaxKind.InToken &&
                bindingKeyword.RawKind != (ushort)SyntaxKind.OutToken &&
                bindingKeyword.RawKind != (ushort)SyntaxKind.BindToken)
            {
                ThrowHelper.ThrowArgumentException(nameof(bindingKeyword), message: $"bindingKeyword must be one of: SyntaxKind.InToken, SyntaxKind.OutToken, SyntaxKind.BindToken");
            }

            if (expression is null)
            {
                ThrowHelper.ThrowArgumentNullException(nameof(expression));
            }

            var green = global::Akbura.Language.Syntax.Green.GreenSyntaxFactory.BindableStateInitializerSyntax(
                Unsafe.As<global::Akbura.Language.Syntax.Green.GreenSyntaxToken>(bindingKeyword.Node!),
                expression.Green);

            return Unsafe.As<BindableStateInitializerSyntax>(green.CreateRed(null, 0));
        }
    }

    internal partial class SyntaxVisitor
    {
        public virtual void VisitBindableStateInitializerSyntax(BindableStateInitializerSyntax node)
        {
            DefaultVisit(node);
        }
    }

    internal partial class SyntaxVisitor<TResult>
    {
        public virtual TResult? VisitBindableStateInitializerSyntax(BindableStateInitializerSyntax node)
        {
            return DefaultVisit(node);
        }
    }

    internal partial class SyntaxVisitor<TParameter, TResult>
    {
        public virtual TResult? VisitBindableStateInitializerSyntax(BindableStateInitializerSyntax node, TParameter argument)
        {
            return DefaultVisit(node, argument);
        }
    }

    internal partial class SyntaxRewriter
    {
        public override AkburaSyntax? VisitBindableStateInitializerSyntax(BindableStateInitializerSyntax node)
        {
            return node.UpdateBindableStateInitializerSyntax(
                VisitToken(node.BindingKeyword),
                (CSharpExpressionSyntax)Visit(node.Expression)!);
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

        public new CSharpTypeSyntax WithLeadingTrivia(SyntaxTriviaList trivia)
        {
            return (CSharpTypeSyntax)base.WithLeadingTrivia(trivia);
        }

        public new CSharpTypeSyntax WithTrailingTrivia(SyntaxTriviaList trivia)
        {
            return (CSharpTypeSyntax)base.WithTrailingTrivia(trivia);
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

- MarkupSyntaxNode.g.cs
```csharp
// <auto-generated/>

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
            ImmutableArray<global::Akbura.Language.Syntax.AkburaDiagnostic>? diagnostics,
            ImmutableArray<global::Akbura.Language.Syntax.AkburaSyntaxAnnotation>? annotations)
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

        public new MarkupSyntaxNodeSyntax WithLeadingTrivia(SyntaxTriviaList trivia)
        {
            return (MarkupSyntaxNodeSyntax)base.WithLeadingTrivia(trivia);
        }

        public new MarkupSyntaxNodeSyntax WithTrailingTrivia(SyntaxTriviaList trivia)
        {
            return (MarkupSyntaxNodeSyntax)base.WithTrailingTrivia(trivia);
        }
    }
}

#nullable restore
```

- AkburaDocumentSyntax.g.cs
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
    internal sealed partial class GreenAkburaDocumentSyntax : global::Akbura.Language.Syntax.Green.GreenNode
    {
        public readonly global::Akbura.Language.Syntax.Green.GreenNode? _members;
        public readonly global::Akbura.Language.Syntax.Green.GreenSyntaxToken EndOfFile;

        public GreenAkburaDocumentSyntax(
            global::Akbura.Language.Syntax.Green.GreenNode? members,
            global::Akbura.Language.Syntax.Green.GreenSyntaxToken endOfFile,
            ImmutableArray<global::Akbura.Language.Syntax.AkburaDiagnostic>? diagnostics,
            ImmutableArray<global::Akbura.Language.Syntax.AkburaSyntaxAnnotation>? annotations)
            : base((ushort)global::Akbura.Language.Syntax.SyntaxKind.AkburaDocumentSyntax, diagnostics, annotations)
        {
            this._members = members;
            this.EndOfFile = endOfFile;

            AkburaDebug.Assert(this.EndOfFile != null);

            var flags = Flags;
            var fullWidth = FullWidth;

            if (_members != null)
            {
                AdjustWidthAndFlags(_members, ref fullWidth, ref flags);
            }

            AdjustWidthAndFlags(EndOfFile, ref fullWidth, ref flags);

            SlotCount = 2;
            FullWidth = fullWidth;
            Flags = flags;
        }

        public GreenSyntaxList<GreenAkTopLevelMemberSyntax> Members => new(_members);

        public GreenAkburaDocumentSyntax UpdateAkburaDocumentSyntax(
            global::Akbura.Language.Syntax.Green.GreenNode? members,
            global::Akbura.Language.Syntax.Green.GreenSyntaxToken endOfFile)
        {
            if (this._members == members && this.EndOfFile == endOfFile)
            {
                return this;
            }

            var newNode = GreenSyntaxFactory.AkburaDocumentSyntax(
                members.ToGreenList<GreenNode>(),
                endOfFile);

            var diagnostics = GetDiagnostics();
            if (!diagnostics.IsDefaultOrEmpty)
            {
                newNode = Unsafe.As<GreenAkburaDocumentSyntax>(newNode.WithDiagnostics(diagnostics));
            }

            var annotations = GetAnnotations();
            if (!annotations.IsDefaultOrEmpty)
            {
                newNode = Unsafe.As<GreenAkburaDocumentSyntax>(newNode.WithAnnotations(annotations));
            }

            return newNode;
        }

        public GreenAkburaDocumentSyntax WithMembers(global::Akbura.Language.Syntax.Green.GreenSyntaxList<GreenNode> members)
        {
            return UpdateAkburaDocumentSyntax(members.Node, this.EndOfFile);
        }

        public GreenAkburaDocumentSyntax WithEndOfFile(global::Akbura.Language.Syntax.Green.GreenSyntaxToken endOfFile)
        {
            return UpdateAkburaDocumentSyntax(this._members, endOfFile);
        }

        public override global::Akbura.Language.Syntax.Green.GreenNode? GetSlot(int index)
        {
            return index switch
            {
                0 => _members,
                1 => EndOfFile,
                _ => null,
            };
        }

        public override global::Akbura.Language.Syntax.AkburaSyntax CreateRed(global::Akbura.Language.Syntax.AkburaSyntax? parent, int position)
        {
            return new global::Akbura.Language.Syntax.AkburaDocumentSyntax(this, parent, position);
        }

        public override global::Akbura.Language.Syntax.Green.GreenNode WithDiagnostics(ImmutableArray<global::Akbura.Language.Syntax.AkburaDiagnostic>? diagnostics)
        {
            return new GreenAkburaDocumentSyntax(this._members, this.EndOfFile, diagnostics, GetAnnotations());
        }

        public override global::Akbura.Language.Syntax.Green.GreenNode WithAnnotations(ImmutableArray<global::Akbura.Language.Syntax.AkburaSyntaxAnnotation>? annotations)
        {
            return new GreenAkburaDocumentSyntax(this._members, this.EndOfFile, GetDiagnostics(), annotations);
        }

        public override void Accept(GreenSyntaxVisitor greenSyntaxVisitor)
        {
            greenSyntaxVisitor.VisitAkburaDocumentSyntax(this);
        }

        public override TResult? Accept<TResult>(GreenSyntaxVisitor<TResult> greenSyntaxVisitor) where TResult : default
        {
            return greenSyntaxVisitor.VisitAkburaDocumentSyntax(this);
        }

        public override TResult? Accept<TParameter, TResult>(GreenSyntaxVisitor<TParameter, TResult> greenSyntaxVisitor, TParameter argument) where TResult : default
        {
            return greenSyntaxVisitor.VisitAkburaDocumentSyntax(this, argument);
        }
    }

    internal static partial class GreenSyntaxFactory
    {
        public static GreenAkburaDocumentSyntax AkburaDocumentSyntax(
            global::Akbura.Language.Syntax.Green.GreenSyntaxList<GreenNode> members,
            global::Akbura.Language.Syntax.Green.GreenSyntaxToken endOfFile)
        {
            AkburaDebug.Assert(endOfFile != null);

            var kind = global::Akbura.Language.Syntax.SyntaxKind.AkburaDocumentSyntax;
            int hash;
            var cache = Unsafe.As<GreenAkburaDocumentSyntax?>(
                GreenNodeCache.TryGetNode(
                    (ushort)kind,
                    members.Node,
                    endOfFile,
                    out hash));

            if (cache != null)
            {
                return cache;
            }

            var result = new GreenAkburaDocumentSyntax(members.Node, endOfFile, diagnostics: null, annotations: null);

            if (hash > 0)
            {
                GreenNodeCache.AddNode(result, hash);
            }

            return result;
        }
    }

    internal partial class GreenSyntaxVisitor
    {
        public virtual void VisitAkburaDocumentSyntax(GreenAkburaDocumentSyntax node)
        {
            DefaultVisit(node);
        }
    }

    internal partial class GreenSyntaxVisitor<TResult>
    {
        public virtual TResult? VisitAkburaDocumentSyntax(GreenAkburaDocumentSyntax node)
        {
            return DefaultVisit(node);
        }
    }

    internal partial class GreenSyntaxVisitor<TParameter, TResult>
    {
        public virtual TResult? VisitAkburaDocumentSyntax(GreenAkburaDocumentSyntax node, TParameter argument)
        {
            return DefaultVisit(node, argument);
        }
    }

    internal partial class GreenSyntaxRewriter
    {
        public override GreenNode? VisitAkburaDocumentSyntax(GreenAkburaDocumentSyntax node)
        {
            return node.UpdateAkburaDocumentSyntax(
                VisitList(node.Members).Node,
                (GreenSyntaxToken)VisitToken(node.EndOfFile));
        }
    }
}

namespace Akbura.Language.Syntax
{
    internal sealed partial class AkburaDocumentSyntax : global::Akbura.Language.Syntax.AkburaSyntax
    {
        private AkburaSyntax? _members;

        public AkburaDocumentSyntax(
            global::Akbura.Language.Syntax.Green.GreenAkburaDocumentSyntax greenNode,
            global::Akbura.Language.Syntax.AkburaSyntax? parent,
            int position)
            : base(greenNode, parent, position)
        {
        }

        internal new global::Akbura.Language.Syntax.Green.GreenAkburaDocumentSyntax Green
            => Unsafe.As<global::Akbura.Language.Syntax.Green.GreenAkburaDocumentSyntax>(base.Green);

        public SyntaxList<AkTopLevelMemberSyntax> Members
        {
            get
            {
                return new SyntaxList<AkTopLevelMemberSyntax>(GetRed(ref this._members, 0));
            }
        }

        /// <summary> 
        /// End-of-file token provided by the host; we reference it as a generic Token.
        /// </summary>
        public SyntaxToken EndOfFile
            => new(this, this.Green.EndOfFile, GetChildPositionFromEnd(1), GetChildIndex(1));

        public AkburaDocumentSyntax UpdateAkburaDocumentSyntax(
            SyntaxList<AkTopLevelMemberSyntax> members,
            SyntaxToken endOfFile)
        {
            if (this.Members == members && this.EndOfFile == endOfFile)
            {
                return this;
            }

            var newNode = SyntaxFactory.AkburaDocumentSyntax(members, endOfFile);

            var annotations = this.GetAnnotations();
            if (!annotations.IsDefaultOrEmpty)
            {
                newNode = (AkburaDocumentSyntax)newNode.WithAnnotations(annotations);
            }

            var diagnostics = this.GetDiagnostics();
            if (!diagnostics.IsDefaultOrEmpty)
            {
                newNode = (AkburaDocumentSyntax)newNode.WithDiagnostics(diagnostics);
            }

            return newNode;
        }

        public AkburaDocumentSyntax WithMembers(SyntaxList<AkTopLevelMemberSyntax> members)
        {
            return UpdateAkburaDocumentSyntax(members, this.EndOfFile);
        }

        public AkburaDocumentSyntax WithEndOfFile(SyntaxToken endOfFile)
        {
            return UpdateAkburaDocumentSyntax(this.Members, endOfFile);
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
            visitor.VisitAkburaDocumentSyntax(this);
        }

        public override TResult? Accept<TResult>(SyntaxVisitor<TResult> visitor) where TResult : default
        {
            return visitor.VisitAkburaDocumentSyntax(this);
        }

        public override TResult? Accept<TParameter, TResult>(SyntaxVisitor<TParameter, TResult> visitor, TParameter argument) where TResult : default
        {
            return visitor.VisitAkburaDocumentSyntax(this, argument);
        }

        public new AkburaDocumentSyntax WithLeadingTrivia(SyntaxTriviaList trivia)
        {
            return (AkburaDocumentSyntax)base.WithLeadingTrivia(trivia);
        }

        public new AkburaDocumentSyntax WithTrailingTrivia(SyntaxTriviaList trivia)
        {
            return (AkburaDocumentSyntax)base.WithTrailingTrivia(trivia);
        }
    }

    internal static partial class SyntaxFactory
    {
        internal static AkburaDocumentSyntax AkburaDocumentSyntax(
            SyntaxList<AkTopLevelMemberSyntax> members,
            SyntaxToken endOfFile)
        {
            if (endOfFile.Node is not global::Akbura.Language.Syntax.Green.GreenSyntaxToken)
            {
                ThrowHelper.ThrowArgumentException(nameof(endOfFile), message: $"endOfFile must be a GreenSyntaxToken. Use SyntaxFactory.Token(...)?");
            }

            if (members != default && members.Node?.Green is not global::Akbura.Language.Syntax.Green.GreenNode)
            {
                ThrowHelper.ThrowArgumentException(nameof(members), message: $"members must be backed by a GreenSyntaxList.");
            }



            var green = global::Akbura.Language.Syntax.Green.GreenSyntaxFactory.AkburaDocumentSyntax(
                members.ToGreenList<GreenAkTopLevelMemberSyntax, AkTopLevelMemberSyntax>(),
                Unsafe.As<global::Akbura.Language.Syntax.Green.GreenSyntaxToken>(endOfFile.Node!));

            return Unsafe.As<AkburaDocumentSyntax>(green.CreateRed(null, 0));
        }
    }

    internal partial class SyntaxVisitor
    {
        public virtual void VisitAkburaDocumentSyntax(AkburaDocumentSyntax node)
        {
            DefaultVisit(node);
        }
    }

    internal partial class SyntaxVisitor<TResult>
    {
        public virtual TResult? VisitAkburaDocumentSyntax(AkburaDocumentSyntax node)
        {
            return DefaultVisit(node);
        }
    }

    internal partial class SyntaxVisitor<TParameter, TResult>
    {
        public virtual TResult? VisitAkburaDocumentSyntax(AkburaDocumentSyntax node, TParameter argument)
        {
            return DefaultVisit(node, argument);
        }
    }

    internal partial class SyntaxRewriter
    {
        public override AkburaSyntax? VisitAkburaDocumentSyntax(AkburaDocumentSyntax node)
        {
            return node.UpdateAkburaDocumentSyntax(
                VisitList(node.Members),
                VisitToken(node.EndOfFile));
        }
    }
}

#nullable restore
```

- UseEffectDeclarationSyntax.g.cs
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
    internal sealed partial class GreenUseEffectDeclarationSyntax : global::Akbura.Language.Syntax.Green.GreenAkTopLevelMemberSyntax
    {
        public readonly global::Akbura.Language.Syntax.Green.GreenSyntaxToken UseEffectKeyword;
        public readonly global::Akbura.Language.Syntax.Green.GreenSyntaxToken OpenParen;
        public readonly global::Akbura.Language.Syntax.Green.GreenNode? _parameters;
        public readonly global::Akbura.Language.Syntax.Green.GreenSyntaxToken CloseParen;
        public readonly global::Akbura.Language.Syntax.Green.GreenCSharpBlockSyntax Body;
        public readonly global::Akbura.Language.Syntax.Green.GreenEffectCancelBlockSyntax? CancelBlock;
        public readonly global::Akbura.Language.Syntax.Green.GreenEffectFinallyBlockSyntax? FinallyBlock;

        public GreenUseEffectDeclarationSyntax(
            global::Akbura.Language.Syntax.Green.GreenSyntaxToken useEffectKeyword,
            global::Akbura.Language.Syntax.Green.GreenSyntaxToken openParen,
            global::Akbura.Language.Syntax.Green.GreenNode? parameters,
            global::Akbura.Language.Syntax.Green.GreenSyntaxToken closeParen,
            global::Akbura.Language.Syntax.Green.GreenCSharpBlockSyntax body,
            global::Akbura.Language.Syntax.Green.GreenEffectCancelBlockSyntax? cancelBlock,
            global::Akbura.Language.Syntax.Green.GreenEffectFinallyBlockSyntax? finallyBlock,
            ImmutableArray<global::Akbura.Language.Syntax.AkburaDiagnostic>? diagnostics,
            ImmutableArray<global::Akbura.Language.Syntax.AkburaSyntaxAnnotation>? annotations)
            : base((ushort)global::Akbura.Language.Syntax.SyntaxKind.UseEffectDeclarationSyntax, diagnostics, annotations)
        {
            this.UseEffectKeyword = useEffectKeyword;
            this.OpenParen = openParen;
            this._parameters = parameters;
            this.CloseParen = closeParen;
            this.Body = body;
            this.CancelBlock = cancelBlock;
            this.FinallyBlock = finallyBlock;

            AkburaDebug.Assert(this.UseEffectKeyword != null);
            AkburaDebug.Assert(this.OpenParen != null);
            AkburaDebug.Assert(this.CloseParen != null);
            AkburaDebug.Assert(this.Body != null);

            AkburaDebug.Assert(this.UseEffectKeyword.Kind == global::Akbura.Language.Syntax.SyntaxKind.UseEffectKeyword);
            AkburaDebug.Assert(this.OpenParen.Kind == global::Akbura.Language.Syntax.SyntaxKind.OpenParenToken);
            AkburaDebug.Assert(this.CloseParen.Kind == global::Akbura.Language.Syntax.SyntaxKind.CloseParenToken);

            var flags = Flags;
            var fullWidth = FullWidth;

            AdjustWidthAndFlags(UseEffectKeyword, ref fullWidth, ref flags);
            AdjustWidthAndFlags(OpenParen, ref fullWidth, ref flags);

            if (_parameters != null)
            {
                AdjustWidthAndFlags(_parameters, ref fullWidth, ref flags);
            }

            AdjustWidthAndFlags(CloseParen, ref fullWidth, ref flags);
            AdjustWidthAndFlags(Body, ref fullWidth, ref flags);

            if (CancelBlock != null)
            {
                AdjustWidthAndFlags(CancelBlock, ref fullWidth, ref flags);
            }

            if (FinallyBlock != null)
            {
                AdjustWidthAndFlags(FinallyBlock, ref fullWidth, ref flags);
            }

            SlotCount = 7;
            FullWidth = fullWidth;
            Flags = flags;
        }

        public GreenSyntaxList<GreenSimpleNameSyntax> Parameters => new(_parameters);

        public GreenUseEffectDeclarationSyntax UpdateUseEffectDeclarationSyntax(
            global::Akbura.Language.Syntax.Green.GreenSyntaxToken useEffectKeyword,
            global::Akbura.Language.Syntax.Green.GreenSyntaxToken openParen,
            global::Akbura.Language.Syntax.Green.GreenNode? parameters,
            global::Akbura.Language.Syntax.Green.GreenSyntaxToken closeParen,
            global::Akbura.Language.Syntax.Green.GreenCSharpBlockSyntax body,
            global::Akbura.Language.Syntax.Green.GreenEffectCancelBlockSyntax? cancelBlock,
            global::Akbura.Language.Syntax.Green.GreenEffectFinallyBlockSyntax? finallyBlock)
        {
            if (this.UseEffectKeyword == useEffectKeyword &&
                this.OpenParen == openParen &&
                this._parameters == parameters &&
                this.CloseParen == closeParen &&
                this.Body == body &&
                this.CancelBlock == cancelBlock &&
                this.FinallyBlock == finallyBlock)
            {
                return this;
            }

            var newNode = GreenSyntaxFactory.UseEffectDeclarationSyntax(
                useEffectKeyword,
                openParen,
                parameters.ToGreenSeparatedList<GreenSimpleNameSyntax>(),
                closeParen,
                body,
                cancelBlock,
                finallyBlock);

            var diagnostics = GetDiagnostics();
            if (!diagnostics.IsDefaultOrEmpty)
            {
                newNode = Unsafe.As<GreenUseEffectDeclarationSyntax>(newNode.WithDiagnostics(diagnostics));
            }

            var annotations = GetAnnotations();
            if (!annotations.IsDefaultOrEmpty)
            {
                newNode = Unsafe.As<GreenUseEffectDeclarationSyntax>(newNode.WithAnnotations(annotations));
            }

            return newNode;
        }

        public GreenUseEffectDeclarationSyntax WithUseEffectKeyword(global::Akbura.Language.Syntax.Green.GreenSyntaxToken useEffectKeyword)
        {
            return UpdateUseEffectDeclarationSyntax(useEffectKeyword, this.OpenParen, this._parameters, this.CloseParen, this.Body, this.CancelBlock, this.FinallyBlock);
        }

        public GreenUseEffectDeclarationSyntax WithOpenParen(global::Akbura.Language.Syntax.Green.GreenSyntaxToken openParen)
        {
            return UpdateUseEffectDeclarationSyntax(this.UseEffectKeyword, openParen, this._parameters, this.CloseParen, this.Body, this.CancelBlock, this.FinallyBlock);
        }

        public GreenUseEffectDeclarationSyntax WithParameters(global::Akbura.Language.Syntax.Green.GreenSyntaxList<GreenSimpleNameSyntax> parameters)
        {
            return UpdateUseEffectDeclarationSyntax(this.UseEffectKeyword, this.OpenParen, parameters.Node, this.CloseParen, this.Body, this.CancelBlock, this.FinallyBlock);
        }

        public GreenUseEffectDeclarationSyntax WithCloseParen(global::Akbura.Language.Syntax.Green.GreenSyntaxToken closeParen)
        {
            return UpdateUseEffectDeclarationSyntax(this.UseEffectKeyword, this.OpenParen, this._parameters, closeParen, this.Body, this.CancelBlock, this.FinallyBlock);
        }

        public GreenUseEffectDeclarationSyntax WithBody(global::Akbura.Language.Syntax.Green.GreenCSharpBlockSyntax body)
        {
            return UpdateUseEffectDeclarationSyntax(this.UseEffectKeyword, this.OpenParen, this._parameters, this.CloseParen, body, this.CancelBlock, this.FinallyBlock);
        }

        public GreenUseEffectDeclarationSyntax WithCancelBlock(global::Akbura.Language.Syntax.Green.GreenEffectCancelBlockSyntax? cancelBlock)
        {
            return UpdateUseEffectDeclarationSyntax(this.UseEffectKeyword, this.OpenParen, this._parameters, this.CloseParen, this.Body, cancelBlock, this.FinallyBlock);
        }

        public GreenUseEffectDeclarationSyntax WithFinallyBlock(global::Akbura.Language.Syntax.Green.GreenEffectFinallyBlockSyntax? finallyBlock)
        {
            return UpdateUseEffectDeclarationSyntax(this.UseEffectKeyword, this.OpenParen, this._parameters, this.CloseParen, this.Body, this.CancelBlock, finallyBlock);
        }

        public override global::Akbura.Language.Syntax.Green.GreenNode? GetSlot(int index)
        {
            return index switch
            {
                0 => UseEffectKeyword,
                1 => OpenParen,
                2 => _parameters,
                3 => CloseParen,
                4 => Body,
                5 => CancelBlock,
                6 => FinallyBlock,
                _ => null,
            };
        }

        public override global::Akbura.Language.Syntax.AkburaSyntax CreateRed(global::Akbura.Language.Syntax.AkburaSyntax? parent, int position)
        {
            return new global::Akbura.Language.Syntax.UseEffectDeclarationSyntax(this, parent, position);
        }

        public override global::Akbura.Language.Syntax.Green.GreenNode WithDiagnostics(ImmutableArray<global::Akbura.Language.Syntax.AkburaDiagnostic>? diagnostics)
        {
            return new GreenUseEffectDeclarationSyntax(this.UseEffectKeyword, this.OpenParen, this._parameters, this.CloseParen, this.Body, this.CancelBlock, this.FinallyBlock, diagnostics, GetAnnotations());
        }

        public override global::Akbura.Language.Syntax.Green.GreenNode WithAnnotations(ImmutableArray<global::Akbura.Language.Syntax.AkburaSyntaxAnnotation>? annotations)
        {
            return new GreenUseEffectDeclarationSyntax(this.UseEffectKeyword, this.OpenParen, this._parameters, this.CloseParen, this.Body, this.CancelBlock, this.FinallyBlock, GetDiagnostics(), annotations);
        }

        public override void Accept(GreenSyntaxVisitor greenSyntaxVisitor)
        {
            greenSyntaxVisitor.VisitUseEffectDeclarationSyntax(this);
        }

        public override TResult? Accept<TResult>(GreenSyntaxVisitor<TResult> greenSyntaxVisitor) where TResult : default
        {
            return greenSyntaxVisitor.VisitUseEffectDeclarationSyntax(this);
        }

        public override TResult? Accept<TParameter, TResult>(GreenSyntaxVisitor<TParameter, TResult> greenSyntaxVisitor, TParameter argument) where TResult : default
        {
            return greenSyntaxVisitor.VisitUseEffectDeclarationSyntax(this, argument);
        }
    }

    internal static partial class GreenSyntaxFactory
    {
        public static GreenUseEffectDeclarationSyntax UseEffectDeclarationSyntax(
            global::Akbura.Language.Syntax.Green.GreenSyntaxToken useEffectKeyword,
            global::Akbura.Language.Syntax.Green.GreenSyntaxToken openParen,
            global::Akbura.Language.Syntax.Green.SeparatedGreenSyntaxList<GreenSimpleNameSyntax> parameters,
            global::Akbura.Language.Syntax.Green.GreenSyntaxToken closeParen,
            global::Akbura.Language.Syntax.Green.GreenCSharpBlockSyntax body,
            global::Akbura.Language.Syntax.Green.GreenEffectCancelBlockSyntax? cancelBlock,
            global::Akbura.Language.Syntax.Green.GreenEffectFinallyBlockSyntax? finallyBlock)
        {
            AkburaDebug.Assert(useEffectKeyword != null);
            AkburaDebug.Assert(openParen != null);
            AkburaDebug.Assert(closeParen != null);
            AkburaDebug.Assert(body != null);

            AkburaDebug.Assert(
                useEffectKeyword!.Kind == global::Akbura.Language.Syntax.SyntaxKind.UseEffectKeyword ||
                false);
            AkburaDebug.Assert(
                openParen!.Kind == global::Akbura.Language.Syntax.SyntaxKind.OpenParenToken ||
                false);
            AkburaDebug.Assert(
                closeParen!.Kind == global::Akbura.Language.Syntax.SyntaxKind.CloseParenToken ||
                false);

            // SlotCount = 7 (>3), so do not use GreenNodeCache.
            var result = new GreenUseEffectDeclarationSyntax(
                useEffectKeyword,
                openParen,
                parameters.Node,
                closeParen,
                body,
                cancelBlock,
                finallyBlock,
                diagnostics: null,
                annotations: null);

            return result;
        }
    }

    internal partial class GreenSyntaxVisitor
    {
        public virtual void VisitUseEffectDeclarationSyntax(GreenUseEffectDeclarationSyntax node)
        {
            DefaultVisit(node);
        }
    }

    internal partial class GreenSyntaxVisitor<TResult>
    {
        public virtual TResult? VisitUseEffectDeclarationSyntax(GreenUseEffectDeclarationSyntax node)
        {
            return DefaultVisit(node);
        }
    }

    internal partial class GreenSyntaxVisitor<TParameter, TResult>
    {
        public virtual TResult? VisitUseEffectDeclarationSyntax(GreenUseEffectDeclarationSyntax node, TParameter argument)
        {
            return DefaultVisit(node, argument);
        }
    }

    internal partial class GreenSyntaxRewriter
    {
        public override GreenNode? VisitUseEffectDeclarationSyntax(GreenUseEffectDeclarationSyntax node)
        {
            return node.UpdateUseEffectDeclarationSyntax(
                (GreenSyntaxToken)VisitToken(node.UseEffectKeyword),
                (GreenSyntaxToken)VisitToken(node.OpenParen),
                VisitList(node.Parameters).Node,
                (GreenSyntaxToken)VisitToken(node.CloseParen),
                (GreenCSharpBlockSyntax)Visit(node.Body)!,
                (GreenEffectCancelBlockSyntax?)Visit(node.CancelBlock),
                (GreenEffectFinallyBlockSyntax?)Visit(node.FinallyBlock));
        }
    }
}

namespace Akbura.Language.Syntax
{
    internal sealed partial class UseEffectDeclarationSyntax : global::Akbura.Language.Syntax.AkTopLevelMemberSyntax
    {
        private AkburaSyntax? _parameters;
        private AkburaSyntax? _body;
        private AkburaSyntax? _cancelBlock;
        private AkburaSyntax? _finallyBlock;

        public UseEffectDeclarationSyntax(
            global::Akbura.Language.Syntax.Green.GreenUseEffectDeclarationSyntax greenNode,
            global::Akbura.Language.Syntax.AkburaSyntax? parent,
            int position)
            : base(greenNode, parent, position)
        {
        }

        internal new global::Akbura.Language.Syntax.Green.GreenUseEffectDeclarationSyntax Green
            => Unsafe.As<global::Akbura.Language.Syntax.Green.GreenUseEffectDeclarationSyntax>(base.Green);

        public SyntaxToken UseEffectKeyword
            => new(this, this.Green.UseEffectKeyword, GetChildPosition(0), GetChildIndex(0));

        public SyntaxToken OpenParen
            => new(this, this.Green.OpenParen, GetChildPosition(1), GetChildIndex(1));

        public SeparatedSyntaxList<SimpleNameSyntax> Parameters
        {
            get
            {
                var red = GetRed(ref this._parameters, 2);
                return new SeparatedSyntaxList<SimpleNameSyntax>(red!, GetChildIndex(2));
            }
        }

        public SyntaxToken CloseParen
            => new(this, this.Green.CloseParen, GetChildPosition(3), GetChildIndex(3));

        public CSharpBlockSyntax Body
            => (CSharpBlockSyntax)GetRed(ref _body, 4)!;

        public EffectCancelBlockSyntax? CancelBlock
            => (EffectCancelBlockSyntax?)GetRed(ref _cancelBlock, 5);

        public EffectFinallyBlockSyntax? FinallyBlock
            => (EffectFinallyBlockSyntax?)GetRed(ref _finallyBlock, 6);

        public UseEffectDeclarationSyntax UpdateUseEffectDeclarationSyntax(
            SyntaxToken useEffectKeyword,
            SyntaxToken openParen,
            SeparatedSyntaxList<SimpleNameSyntax> parameters,
            SyntaxToken closeParen,
            CSharpBlockSyntax body,
            EffectCancelBlockSyntax? cancelBlock,
            EffectFinallyBlockSyntax? finallyBlock)
        {
            if (this.UseEffectKeyword == useEffectKeyword &&
                this.OpenParen == openParen &&
                this.Parameters == parameters &&
                this.CloseParen == closeParen &&
                this.Body == body &&
                this.CancelBlock == cancelBlock &&
                this.FinallyBlock == finallyBlock)
            {
                return this;
            }

            var newNode = SyntaxFactory.UseEffectDeclarationSyntax(
                useEffectKeyword,
                openParen,
                parameters,
                closeParen,
                body,
                cancelBlock,
                finallyBlock);

            var annotations = this.GetAnnotations();
            if (!annotations.IsDefaultOrEmpty)
            {
                newNode = (UseEffectDeclarationSyntax)newNode.WithAnnotations(annotations);
            }

            var diagnostics = this.GetDiagnostics();
            if (!diagnostics.IsDefaultOrEmpty)
            {
                newNode = (UseEffectDeclarationSyntax)newNode.WithDiagnostics(diagnostics);
            }

            return newNode;
        }

        public UseEffectDeclarationSyntax WithUseEffectKeyword(SyntaxToken useEffectKeyword)
        {
            return UpdateUseEffectDeclarationSyntax(useEffectKeyword, this.OpenParen, this.Parameters, this.CloseParen, this.Body, this.CancelBlock, this.FinallyBlock);
        }

        public UseEffectDeclarationSyntax WithOpenParen(SyntaxToken openParen)
        {
            return UpdateUseEffectDeclarationSyntax(this.UseEffectKeyword, openParen, this.Parameters, this.CloseParen, this.Body, this.CancelBlock, this.FinallyBlock);
        }

        public UseEffectDeclarationSyntax WithParameters(SeparatedSyntaxList<SimpleNameSyntax> parameters)
        {
            return UpdateUseEffectDeclarationSyntax(this.UseEffectKeyword, this.OpenParen, parameters, this.CloseParen, this.Body, this.CancelBlock, this.FinallyBlock);
        }

        public UseEffectDeclarationSyntax WithCloseParen(SyntaxToken closeParen)
        {
            return UpdateUseEffectDeclarationSyntax(this.UseEffectKeyword, this.OpenParen, this.Parameters, closeParen, this.Body, this.CancelBlock, this.FinallyBlock);
        }

        public UseEffectDeclarationSyntax WithBody(CSharpBlockSyntax body)
        {
            return UpdateUseEffectDeclarationSyntax(this.UseEffectKeyword, this.OpenParen, this.Parameters, this.CloseParen, body, this.CancelBlock, this.FinallyBlock);
        }

        public UseEffectDeclarationSyntax WithCancelBlock(EffectCancelBlockSyntax? cancelBlock)
        {
            return UpdateUseEffectDeclarationSyntax(this.UseEffectKeyword, this.OpenParen, this.Parameters, this.CloseParen, this.Body, cancelBlock, this.FinallyBlock);
        }

        public UseEffectDeclarationSyntax WithFinallyBlock(EffectFinallyBlockSyntax? finallyBlock)
        {
            return UpdateUseEffectDeclarationSyntax(this.UseEffectKeyword, this.OpenParen, this.Parameters, this.CloseParen, this.Body, this.CancelBlock, finallyBlock);
        }

        public override global::Akbura.Language.Syntax.AkburaSyntax? GetNodeSlot(int index)
        {
            return index switch
            {
                4 => GetRed(ref _body, 4),
                5 => GetRed(ref _cancelBlock, 5),
                6 => GetRed(ref _finallyBlock, 6),
                _ => null,
            };
        }

        public override global::Akbura.Language.Syntax.AkburaSyntax? GetCachedSlot(int index)
        {
            return index switch
            {
                4 => _body,
                5 => _cancelBlock,
                6 => _finallyBlock,
                _ => null,
            };
        }

        public override void Accept(SyntaxVisitor visitor)
        {
            visitor.VisitUseEffectDeclarationSyntax(this);
        }

        public override TResult? Accept<TResult>(SyntaxVisitor<TResult> visitor) where TResult : default
        {
            return visitor.VisitUseEffectDeclarationSyntax(this);
        }

        public override TResult? Accept<TParameter, TResult>(SyntaxVisitor<TParameter, TResult> visitor, TParameter argument) where TResult : default
        {
            return visitor.VisitUseEffectDeclarationSyntax(this, argument);
        }

        public new UseEffectDeclarationSyntax WithLeadingTrivia(SyntaxTriviaList trivia)
        {
            return (UseEffectDeclarationSyntax)base.WithLeadingTrivia(trivia);
        }

        public new UseEffectDeclarationSyntax WithTrailingTrivia(SyntaxTriviaList trivia)
        {
            return (UseEffectDeclarationSyntax)base.WithTrailingTrivia(trivia);
        }
    }

    internal static partial class SyntaxFactory
    {
        internal static UseEffectDeclarationSyntax UseEffectDeclarationSyntax(
            SyntaxToken useEffectKeyword,
            SyntaxToken openParen,
            SeparatedSyntaxList<SimpleNameSyntax> parameters,
            SyntaxToken closeParen,
            CSharpBlockSyntax body,
            EffectCancelBlockSyntax? cancelBlock,
            EffectFinallyBlockSyntax? finallyBlock)
        {
            if (useEffectKeyword.Node is not global::Akbura.Language.Syntax.Green.GreenSyntaxToken)
            {
                ThrowHelper.ThrowArgumentException(nameof(useEffectKeyword), message: $"useEffectKeyword must be a GreenSyntaxToken. Use SyntaxFactory.Token(...)?");
            }

            if (openParen.Node is not global::Akbura.Language.Syntax.Green.GreenSyntaxToken)
            {
                ThrowHelper.ThrowArgumentException(nameof(openParen), message: $"openParen must be a GreenSyntaxToken. Use SyntaxFactory.Token(...)?");
            }

            if (closeParen.Node is not global::Akbura.Language.Syntax.Green.GreenSyntaxToken)
            {
                ThrowHelper.ThrowArgumentException(nameof(closeParen), message: $"closeParen must be a GreenSyntaxToken. Use SyntaxFactory.Token(...)?");
            }

            if (useEffectKeyword.RawKind != (ushort)SyntaxKind.UseEffectKeyword)
            {
                ThrowHelper.ThrowArgumentException(nameof(useEffectKeyword), message: $"useEffectKeyword must be SyntaxKind.UseEffectKeyword");
            }

            if (openParen.RawKind != (ushort)SyntaxKind.OpenParenToken)
            {
                ThrowHelper.ThrowArgumentException(nameof(openParen), message: $"openParen must be SyntaxKind.OpenParenToken");
            }

            if (closeParen.RawKind != (ushort)SyntaxKind.CloseParenToken)
            {
                ThrowHelper.ThrowArgumentException(nameof(closeParen), message: $"closeParen must be SyntaxKind.CloseParenToken");
            }

            if (body is null)
            {
                ThrowHelper.ThrowArgumentNullException(nameof(body));
            }

            if (parameters != default && parameters.Node?.Green is not GreenNode)
            {
                ThrowHelper.ThrowArgumentException(nameof(parameters), message: $"parameters must be backed by a GreenSyntaxList or SeparatedGreenSyntaxList.");
            }

            var green = global::Akbura.Language.Syntax.Green.GreenSyntaxFactory.UseEffectDeclarationSyntax(
                Unsafe.As<global::Akbura.Language.Syntax.Green.GreenSyntaxToken>(useEffectKeyword.Node!),
                Unsafe.As<global::Akbura.Language.Syntax.Green.GreenSyntaxToken>(openParen.Node!),
                parameters.ToGreenSeparatedList<GreenSimpleNameSyntax, SimpleNameSyntax>(),
                Unsafe.As<global::Akbura.Language.Syntax.Green.GreenSyntaxToken>(closeParen.Node!),
                body.Green,
                cancelBlock?.Green,
                finallyBlock?.Green);

            return Unsafe.As<UseEffectDeclarationSyntax>(green.CreateRed(null, 0));
        }
    }

    internal partial class SyntaxVisitor
    {
        public virtual void VisitUseEffectDeclarationSyntax(UseEffectDeclarationSyntax node)
        {
            DefaultVisit(node);
        }
    }

    internal partial class SyntaxVisitor<TResult>
    {
        public virtual TResult? VisitUseEffectDeclarationSyntax(UseEffectDeclarationSyntax node)
        {
            return DefaultVisit(node);
        }
    }

    internal partial class SyntaxVisitor<TParameter, TResult>
    {
        public virtual TResult? VisitUseEffectDeclarationSyntax(UseEffectDeclarationSyntax node, TParameter argument)
        {
            return DefaultVisit(node, argument);
        }
    }

    internal partial class SyntaxRewriter
    {
        public override AkburaSyntax? VisitUseEffectDeclarationSyntax(UseEffectDeclarationSyntax node)
        {
            return node.UpdateUseEffectDeclarationSyntax(
                VisitToken(node.UseEffectKeyword),
                VisitToken(node.OpenParen),
                VisitList(node.Parameters),
                VisitToken(node.CloseParen),
                (CSharpBlockSyntax)Visit(node.Body)!,
                (EffectCancelBlockSyntax?)Visit(node.CancelBlock),
                (EffectFinallyBlockSyntax?)Visit(node.FinallyBlock));
        }
    }
}

#nullable restore
```