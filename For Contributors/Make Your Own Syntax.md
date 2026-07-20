# Make Your Own Syntax

This guide explains the current manual path for adding a new Akbura syntax node.

`Syntax.nooken` is the syntax declaration file used by the project as a compact description of tokens, abstract syntax categories, concrete syntax nodes, and node fields. It does not currently generate code. It was useful during the early stages to let LLM-assisted work produce a large amount of syntax boilerplate, but new syntax still has to be wired by hand.

Maybe this will become a real generator later. For now, treat it as the source of design intent and keep the handwritten syntax implementation in sync with it.

## 1. Add The Syntax Declaration To Syntax.nooken

Start by describing the syntax in [Syntax.nooken](../Akbura.Generator/Language/Syntax/Syntax.nooken).

The file is organized into several sections:

- well-known tokens with fixed text, starting at `[id 100]`;
- DSL-specific literals, currently starting at `[id 200]`;
- abstract node categories and concrete syntax nodes, currently starting at `[id 500]`;
- shared identifier/name/type nodes, currently around `[id 700]`.

Keep new entries near related syntax. Do not insert new token or node kinds randomly. Always look at existing kind ids and keep the shape easy to audit.

Kind values can still change because Akbura does not currently need syntax binary compatibility or serialized syntax compatibility. The important part is keeping related ranges readable and avoiding accidental id collisions.

## 1.1 Nooken Syntax Overview

`Syntax.nooken` supports these declaration forms.

Well-known text tokens:

```nooken
[id 100]
token InjectKeyword = "inject";
token ParamKeyword  = "param";
token StateKeyword  = "state";
```

The `[id N]` marker sets the next numeric kind value for following declarations. For well-known text tokens, keep the block contiguous because `SyntaxKind.FirstTokenWithWellKnownText` and `SyntaxKind.LastTokenWithWellKnownText` depend on that range.

Tokens may also declare a token value:

```nooken
token TrueKeyword  = "true"  return true;
token FalseKeyword = "false" return false;
token NullKeyword  = "null"  return null;
```

DSL-specific literals:

```nooken
[id 200]
literal AkTextLiteral : string;
token CSharpRawToken;
```

Abstract syntax categories:

```nooken
abstract node AkTopLevelMember;
abstract node MarkupAttributeSyntax : MarkupSyntaxNode;
```

Concrete syntax nodes:

```nooken
node StateDeclarationSyntax : AkTopLevelMember {
    StateKeyword : StateKeyword;
    Type?        : CSharpTypeSyntax;
    Name         : SimpleName;
    Equals       : EqualsToken;
    Initializer  : StateInitializer;
    Semicolon    : SemicolonToken;
}
```

Field syntax:

- `Name : Type;` means a required child or token.
- `Name? : Type;` means an optional child or token.
- `Name : TypeA | TypeB;` means one of several accepted concrete kinds.
- `Items : syntaxlist<NodeType>;` means a syntax list of nodes.
- `Items : syntaxlist<NodeType, SeparatorToken>;` means a separated syntax list.
- `Tokens : TokenList;` means raw token storage, often used for C# fragments handled by Roslyn later.

Examples from the existing file:

```nooken
node ParamDeclarationSyntax : AkTopLevelMember {
    ParamKeyword    : ParamKeyword;
    BindingKeyword? : OutToken | BindToken;
    Type?           : CSharpTypeSyntax;
    Name            : SimpleName;
    Equals?         : EqualsToken;
    DefaultValue?   : CSharpExpressionSyntax;
    Semicolon       : SemicolonToken;
}
```

```nooken
node MarkupExtensionPropertyArgumentSyntax : MarkupExtensionArgumentSyntax {
    Name        : SimpleName;
    EqualsToken : EqualsToken;
    Value       : MarkupExtensionValueSyntax;
}
```

## 1.2 If The Syntax Uses A Keyword

If your new syntax uses a keyword, add it to the well-known text token section first. Insert it at the end of that section so existing token ids stay stable while you are working.

Example:

```nooken
// markup / AKCSS tokens
token UtilitiesKeyword = "utilities";
token AkcssKeyword     = "akcss";
token ApplyKeyword     = "apply";
token InterceptKeyword = "intercept";

token MyNewKeyword     = "myNewKeyword";
```

The maximum syntax kind storage size is currently 16 bits, because `SyntaxKind` is a `ushort`.

## 2. Update SyntaxKind

Update [SyntaxKind.g.cs](../Akbura.Generator/Language/Syntax/Generated/SyntaxKind.g.cs).

Despite the `.g.cs` suffix, this file is currently maintained by hand. Add your concrete token or syntax node kind to the correct range.

Do not use these reserved ranges for normal node ids:

```csharp
// Trivia
EndOfLineTrivia = 1000,
WhitespaceTrivia = 1001,
SkippedTokensTrivia = 1002,

// Built-in literal tokens
StringLiteralToken = 2000,
CharLiteralToken = 2001,
NumericLiteralToken = 2002,

IdentifierToken = 3000,
BadToken = 3001,
EndOfFileToken = 3002,
```

For a new well-known keyword token:

- add the enum member after the current last well-known text token;
- update `LastTokenWithWellKnownText` if the new token is now last.

For a new concrete syntax node:

- add the enum member near the related node group;
- pick the next available value in that group;
- keep abstract categories and concrete nodes readable.

## 3. Update SyntaxFacts

Update [SyntaxFacts.g.cs](../Akbura.Generator/Language/Syntax/Generated/SyntaxFacts.g.cs).

If you added a token with well-known text, update:

- `GetText(SyntaxKind kind)`;
- `GetKeywordKind(string text)` if it is a keyword-like identifier;
- `IsReservedKeyword(SyntaxKind kind)` if users should not be able to use the text as a normal identifier.

If the token is a literal or trivia-like token, also check:

- `IsLiteral(SyntaxKind kind)`;
- `IsAnyToken(SyntaxKind kind)`;
- `IsTrivia(SyntaxKind kind)`;
- `GetValue(SyntaxKind kind, string text)`.

## 4. Create The Syntax Node

Create the red syntax node and green syntax node files by hand.

Use the existing naming style:

```text
MyNodeName.g.cs
```

Keep the generated-style files boring and predictable. They should mostly expose:

- strongly typed child/token properties;
- constructor wiring;
- `Update(...)`;
- `With...(...)` helpers when the surrounding syntax pattern already has them;
- visitor dispatch if the current syntax family uses visitors.

Use nearby nodes as templates. For example, if you add a markup attribute syntax, copy the shape of an existing markup attribute node before adapting it.

## 4.1 Optional LLM-Assisted Node Generation

There is an old notebook workflow that was used to generate the first wave of `.g.cs` syntax files from `Syntax.nooken`. It is not an official generator, but it can still be useful as a starting point when adding a large syntax node.

The important files are:

- [Syntax.nooken](../Akbura.Generator/Language/Syntax/Syntax.nooken);
- [SystemNodeGenerationPrompt.md](../Akbura.Generator/Language/Syntax/Generated/SystemNodeGenerationPrompt.md);
- generated syntax node files in [Akbura.Generator/Language/Syntax/Generated](../Akbura.Generator/Language/Syntax/Generated).

The workflow is:

1. Read the full `Syntax.nooken` grammar.
2. Split it into individual `node ...` and `abstract node ...` declarations.
3. Send the full grammar plus one node declaration to the node-generation prompt.
4. Write one generated `.g.cs` file for that node.
5. Review and fix the output by hand.

The splitter prompt used by the notebook was intentionally strict. It asked the model to return JSON like this:

```json
{
  "nodes": [
    {
      "rawValue": "node CSharpTypeSyntax {\n    Tokens : TokenList;\n}"
    }
  ]
}
```

The rules for splitting are:

- extract declarations starting with `node X ...` or `abstract node X ...`;
- include exact whitespace, comments, blank lines, and indentation;
- end a declaration at `;` for semicolon-form declarations;
- end a declaration at the matching closing brace for block-form declarations;
- do not include token or literal declarations;
- return only JSON, with no markdown and no explanations.

The old Python shape was roughly:

```python
import json
import re
from pathlib import Path
from textwrap import dedent
from openai import OpenAI

client = OpenAI()
NOTEBOOK_DIR = Path.cwd()

NOOKEN_GRAMMAR = Path(
    "../Akbura.Generator/Language/Syntax/Syntax.nooken"
).read_text(encoding="utf-8")

NODE_SYSTEM_PROMPT = (
    NOTEBOOK_DIR / "SystemNodeGenerationPrompt.md"
).read_text(encoding="utf-8")
```

The node file name helper was:

```python
def generate_file_name(node_raw: str) -> str:
    match = re.search(
        r"(?:abstract\s+node|node)\s+([A-Za-z_][A-Za-z0-9_]*)",
        node_raw,
    )

    if not match:
        raise ValueError(f"Cannot parse node name from Nooken text:\n{node_raw}")

    node_name = match.group(1)

    if not node_name.endswith("Syntax"):
        node_name = f"{node_name}Syntax"

    return f"{node_name}.g.cs"
```

The generation call used the full grammar and exactly one node:

````python
def generate_node(full_grammar: str, node: str) -> str:
    user_content = f"""Full Nooken grammar:
```nooken
{full_grammar}
```
Node to generate:
{node}
"""

    response = client.chat.completions.create(
        model="gpt-5.1",
        temperature=0.0,
        messages=[
            {"role": "system", "content": NODE_SYSTEM_PROMPT},
            {"role": "user", "content": user_content},
        ],
    )

    return response.choices[0].message.content
````

Do not blindly accept the generated file. The prompt describes the desired generated-style shape, but the repository is the source of truth. Always compare with nearby syntax files and fix:

- green node slot order;
- red cached slots;
- list handling;
- separated list handling;
- token kind validation;
- `Update...` methods;
- `With...` helpers;
- `WithLeadingTrivia` / `WithTrailingTrivia`;
- green and red visitors;
- green and red rewriters;
- `SyntaxFactory` and `GreenSyntaxFactory`;
- diagnostics and annotations preservation.

If the prompt is stale, update the generated file manually first. Update the prompt only when the new rule should apply to future generated syntax nodes too.

## 4.2 Example: C# Using And Namespace Syntax

Using and namespace syntax is a good example of why generated syntax files are only part of the work.

Akbura has syntax nodes for C#-style namespace imports:

```nooken
node UsingAliasSyntax {
    Name        : SimpleName;
    EqualsToken : EqualsToken;
}

node UsingDirectiveSyntax : AkTopLevelMember {
    GlobalKeyword? : GlobalKeyword;
    UsingKeyword   : UsingKeyword;
    StaticKeyword? : StaticKeyword;
    UnsafeKeyword? : UnsafeKeyword;
    Alias?         : UsingAliasSyntax;
    Name           : CSharpTypeSyntax;
    Semicolon      : SemicolonToken;
}

node NamespaceDeclarationSyntax : AkTopLevelMember {
    NamespaceKeyword : NamespaceKeyword;
    Name             : CSharpTypeSyntax;
    Semicolon        : SemicolonToken;
}
```

This syntax has to support the C# using forms Akbura forwards to generated C#:

```csharp
using System;
using static System.Math;
using Alias = My.Namespace.Type;
using unsafe Alias = int*;
global using System;
global using static System.Math;
global using Alias = My.Namespace.Type;
global using unsafe Alias = int*;
```

Do not assume Roslyn has a tiny `CSharpSyntaxFactory.ParseUsingDeclarator(...)` helper for the exact fragment you need. For C# fragments, use the Roslyn parser when it matches the fragment shape (`ParseExpression`, `ParseParameterList`, `ParseArgumentList`, etc.). When no fragment parser exists, either parse a larger C# construct and extract the part you need, or manually map the Akbura syntax node into Roslyn syntax.

For namespace syntax, Akbura currently models file-scoped namespace declarations:

```akbura
namespace Demo.Pages;
```

The declaration remains a top-level member in `AkburaDocumentSyntax.Members`; following members are still siblings in Akbura syntax. Namespace scoping is handled by semantic/codegen layers, not by nesting the syntax tree.

## 5. Wire The Parser

After the syntax shape exists, update the parser so it actually produces the new node.

The parser lives in the `Akbura.Generator/Language` folder, not under `Language/Syntax`.

The main files to inspect first are:

- [Parser.cs](../Akbura.Generator/Language/Parser.cs) for token buffering, `EatToken`, `TryEatToken`, missing tokens, and parser lifetime;
- [Parser_LanguageParser.cs](../Akbura.Generator/Language/Parser_LanguageParser.cs) for the main grammar productions;
- [Parser.Incremental.cs](../Akbura.Generator/Language/Parser.Incremental.cs) for incremental parsing hooks;
- [Blender.cs](../Akbura.Generator/Language/Blender.cs) and `Blender.*.cs` for old-tree reuse;
- [ComponentSyntaxTree.cs](../Akbura.Generator/Language/ComponentSyntaxTree.cs) and [AkcssSyntaxTree.cs](../Akbura.Generator/Language/AkcssSyntaxTree.cs) for `ParseText(...)` and `WithChangedText(...)`.

Usually parser wiring means:

- recognize the new keyword or punctuation in the correct parse context;
- add a `Parse...` method or extend an existing one;
- preserve trivia and skipped tokens;
- preserve exact `ToFullString()` round-trip behavior;
- keep error recovery predictable.

For keyword-based syntax, make sure the lexer can classify the keyword through `SyntaxFacts.GetKeywordKind`.

### 5.1 Pick The Parse Context

Start by deciding where the new syntax can appear.

For component top-level syntax, update `ParseCompilationUnitMember()` and usually `ParseTopLevelMember()`:

```csharp
return CurrentToken.Kind switch
{
    SyntaxKind.StateKeyword => ParseStateDeclaration(),
    SyntaxKind.ParamKeyword => ParseParamDeclarationSyntax(),
    SyntaxKind.InjectKeyword => ParseInjectDeclarationSyntax(),
    SyntaxKind.CommandKeyword => ParseCommandDeclarationSyntax(),
    SyntaxKind.YourKeyword => ParseYourNewSyntax(),
    _ => ParseCSharpStatementSyntax(),
};
```

For file-scoped directives that must be recognized before normal statements, add them in `ParseCompilationUnitMember()` too. `using` and `namespace` are examples:

```csharp
return CurrentToken.Kind switch
{
    SyntaxKind.UsingKeyword => ParseUsingDirectiveSyntax(),
    SyntaxKind.GlobalKeyword when PeekToken(1).Kind == SyntaxKind.UsingKeyword => ParseUsingDirectiveSyntax(),
    SyntaxKind.NamespaceKeyword => ParseNamespaceDeclarationSyntax(),
    _ => ParseTopLevelMember()
};
```

For AKCSS top-level syntax, update `ParseAkcssTopLevelMemberSyntaxCore()`.

For AKCSS body syntax, update `ParseAkcssBodyMemberSyntax()`.

For markup attributes or markup content, find the matching markup parser method and add the new branch there. Keep markup parsing conservative because bad recovery inside markup can swallow the rest of a document very quickly.

### 5.2 Write The Normal Parser Method

Parser methods should usually return green nodes directly:

```csharp
internal GreenYourNewSyntax ParseYourNewSyntax()
{
    var keyword = EatToken(SyntaxKind.YourKeyword);
    var name = ParseIdentifierName();
    var semicolon = EatToken(SyntaxKind.SemicolonToken);

    return GreenSyntaxFactory.YourNewSyntax(
        keyword,
        name,
        semicolon);
}
```

Use these helpers consistently:

- `CurrentToken` for the current token;
- `PeekToken(n)` for lookahead;
- `EatToken()` to consume any token;
- `EatToken(kind)` to consume the expected token or create a missing token;
- `TryEatToken(kind)` for optional punctuation or keywords;
- `EatTokenAsKind(kind)` when the current token should be consumed as skipped syntax and replaced by a missing expected token;
- `ReturnToken()` only for narrow lookahead rollback cases.

Do not manually construct missing tokens unless the existing helpers cannot express the recovery. Prefer `EatToken(kind)` because it preserves parser diagnostics/recovery behavior.

### 5.3 Parse Lists With The Pool

For `syntaxlist<T>` fields, use `GreenSyntaxListPool` through `_pool.Allocate<T>()`.

```csharp
private GreenSyntaxList<GreenYourItemSyntax> ParseYourItemList()
{
    var items = _pool.Allocate<GreenYourItemSyntax>();

    try
    {
        while (CurrentToken.Kind is not (SyntaxKind.EndOfFileToken or SyntaxKind.CloseBraceToken))
        {
            items.Add(ParseYourItemSyntax());
        }

        return items.ToList();
    }
    finally
    {
        _pool.Free(items);
    }
}
```

For separated lists, follow an existing separated-list parser before adding a new pattern. The green list must preserve separators exactly, because `ToFullString()` depends on the original token sequence.

### 5.4 Parse Embedded C# Fragments

Akbura often stores C# fragments as `CSharpRawToken` inside wrapper syntax nodes such as `CSharpTypeSyntax`, `CSharpExpressionSyntax`, `CSharpParameterListSyntax`, and `CSharpArgumentListSyntax`.

Use Roslyn parsing for the fragment shape when a matching parser exists:

```csharp
var expression = CSharpFactory.ParseExpression(
    rawText.ToString(),
    offset: 0,
    options: null,
    consumeFullText: true);

return GreenSyntaxFactory.CSharpExpressionSyntax(
    GreenSyntaxFactory.CSharpRawToken(expression));
```

Existing examples:

- `CSharpExpressionSyntax.GetRawCSharpExpression()` uses `ParseExpression`;
- `CSharpParameterListSyntax.GetRawCSharpParameterList()` uses `ParseParameterList`;
- `CSharpArgumentListSyntax.GetRawCSharpArgumentList()` uses `ParseArgumentList`;
- `UsingDirectiveSyntax.GetRawCSharpUsingDirective()` has to map to Roslyn using syntax more manually.

When parsing C# text until a terminator, keep balanced delimiters in mind. AKCSS expressions, for example, can contain parentheses, brackets, braces, generic calls, tuples, strings, and member accesses. Do not stop at the first `;`, `}`, or `)` if that token belongs to a nested C# fragment.

### 5.5 Preserve Lexer Mode

Some parser paths temporarily change lexer mode. Always save and restore it with `try/finally`.

```csharp
var mode = _mode;
_mode = Lexer.LexerMode.InAkcss;

try
{
    // parse AKCSS-specific syntax
}
finally
{
    _mode = mode;
}
```

This is especially important for markup, AKCSS, and C# fragment parsing because the same characters may mean different things in different contexts.

### 5.6 Add Incremental Parsing Support

Akbura syntax trees support incremental parsing through `WithChangedText(...)`.

The syntax tree entry point looks like this:

```csharp
public ComponentSyntaxTree WithChangedText(
    SourceText newText,
    IEnumerable<TextChangeRange>? changes = null,
    CancellationToken cancellationToken = default)
{
    var changeRanges = changes?.ToArray() ?? newText.GetChangeRanges(Text).ToArray();
    if (changeRanges.Length == 0 && newText.ToString() == Text.ToString())
    {
        return this;
    }

    var lexer = new Lexer(newText);
    using var parser = new Parser(lexer, cancellationToken, GetRoot(), changeRanges);

    return new ComponentSyntaxTree(newText, FilePath, parser.ParseCompilationUnit());
}
```

The incremental constructor enables `Blender`:

```csharp
public Parser(
    Lexer lexer,
    CancellationToken cancellationToken,
    AkburaSyntax? oldTree,
    IEnumerable<TextChangeRange>? changes)
    : this(lexer, cancellationToken)
{
    if (oldTree == null)
    {
        return;
    }

    _isIncremental = true;
    _blender = new Blender(lexer, oldTree, changes);
    _blendersBeforeToken = s_blendersBeforeTokenPool.Allocate();
}
```

`Blender` walks the old red tree and the new text together. If an old node/token does not intersect the changed range and is safe to reuse, the incremental parser can return its existing green node instead of rebuilding it.

For a new syntax kind, add an incremental parser when the node is large enough or common enough that reuse matters. Top-level declarations, markup roots, AKCSS blocks, style rules, utilities, and large C# blocks are good candidates.

### 5.7 Write A TryParseIncremental Method

Incremental methods follow this shape:

```csharp
private bool TryParseIncrementalYourNewSyntax(out GreenYourNewSyntax node)
{
    node = null!;

    if (!CanReadIncrementalNodeOrToken())
    {
        return false;
    }

    if (TryReadReusableIncrementalNode<GreenYourNewSyntax>(out node))
    {
        return true;
    }

    if (!TryReadIncrementalToken(SyntaxKind.YourKeyword, out var keyword))
    {
        return false;
    }

    var name = ParseIncrementalIdentifierName();
    var semicolon = ReadRequiredIncrementalToken(SyntaxKind.SemicolonToken);

    node = GreenSyntaxFactory.YourNewSyntax(
        keyword,
        name,
        semicolon);
    return true;
}
```

Use incremental helpers instead of normal token helpers inside this method:

- `CanReadIncrementalNodeOrToken()` checks whether incremental reuse is currently possible;
- `TryReadReusableIncrementalNode<TNode>(out node)` reuses an old complete node;
- `TryReadIncrementalToken(kind, out token)` reuses a token only if it matches and is safe;
- `ReadRequiredIncrementalToken(kind)` reuses a token or creates a missing token;
- `PeekIncrementalTokenKind(offset)` performs lookahead through the blender;
- `ReadIncrementalToken()` reads the next incremental token when the exact kind is not known.

If the method starts parsing and then discovers the syntax does not match, be careful: returning `false` after consuming incremental tokens corrupts the caller. Use lookahead before consuming whenever possible.

### 5.8 Call Incremental Parsers Before Normal Parsers

The normal parser entry points should try incremental reuse first.

For example, top-level parsing currently checks reusable nodes and incremental declarations before falling back to normal parsing:

```csharp
if (TryEatReusableTopLevelMember(out var reusableMember))
{
    members.Add(reusableMember);
    continue;
}

if (TryParseIncrementalStateDeclaration(out var incrementalState))
{
    members.Add(incrementalState);
    continue;
}

var member = ParseCompilationUnitMember();
members.Add(member);
```

When adding a new top-level node, add its incremental branch near related syntax:

```csharp
if (TryParseIncrementalYourNewSyntax(out var incrementalNode))
{
    members.Add(incrementalNode);
    continue;
}
```

For nested syntax, put the incremental branch at the start of the corresponding parse method:

```csharp
private GreenAkcssBodyMemberSyntax ParseIncrementalAkcssBodyMemberSyntax()
{
    if (TryReadReusableIncrementalNode<GreenAkcssBodyMemberSyntax>(out var member))
    {
        return member;
    }

    // parse by incremental token lookahead
}
```

### 5.9 Reuse Rules And Safety

Only reuse a node when the existing parser infrastructure says it is safe. Do not compare green nodes manually in parser code.

In practice:

- use `TryReadReusableIncrementalNode<TNode>()`;
- use `TryReadIncrementalToken(...)`;
- let `CanReuseIncrementalNode(...)` decide safety;
- let `Blender` handle change ranges and old-tree cursor movement.

Avoid reusing syntax that depends on external parser state unless that state is part of the check. Lexer mode is the usual example. If the syntax can appear in different modes, make sure the incremental method reads with the same `_mode` that the normal parser would use.

### 5.10 Add Incremental Parser Tests

For every new reusable syntax shape, add at least one incremental parser test.

The test should:

1. parse the original text;
2. create a changed text with a narrow edit;
3. call `WithChangedText(...)`;
4. assert the edited node changed;
5. assert unrelated nodes were reused with `Assert.Same(...)`;
6. assert `ToFullString()` equals the new text.

Example shape:

```csharp
[Fact]
public void YourSyntax_Edit_ReusesUnchangedSiblings()
{
    const string oldText =
        """
        state int before = 0;
        your keyword oldName;
        state int after = 1;
        """;
    const string newText =
        """
        state int before = 0;
        your keyword newName;
        state int after = 1;
        """;

    var oldTree = ComponentSyntaxTree.ParseText(oldText, "Counter.akbura");
    var newTree = oldTree.WithChangedText(newText);

    var oldRoot = oldTree.GetRoot();
    var newRoot = newTree.GetRoot();

    Assert.Same(oldRoot.Members[0], newRoot.Members[0]);
    Assert.NotSame(oldRoot.Members[1], newRoot.Members[1]);
    Assert.Same(oldRoot.Members[2], newRoot.Members[2]);
    Assert.Equal(newText, newRoot.ToFullString());
}
```

If the syntax is nested, assert reuse at the nearest useful boundary: an unchanged markup attribute, AKCSS assignment, utility parameter, or C# fragment.

## 6. Update Visitors

If the new syntax participates in syntax visitors or walkers, add the corresponding visit method and dispatch.

Check the existing syntax visitor pattern before adding anything new. Akbura syntax visitors should stay consistent with the rest of the syntax tree implementation.

## 7. Update Semantic Binding If Needed

Parser-only syntax should not create semantic concepts just because the node exists. Add semantic binding only when the syntax carries meaning beyond structure.

The semantic pipeline is:

```text
Syntax
  -> Declaration tree
  -> ISymbol creation / lookup
  -> Binder
  -> BoundNode
  -> IOperation
```

These layers are intentionally separate.

- `Declaration` is a cheap structural summary of source declarations.
- `ISymbol` is the named semantic entity exposed by declarations or external C#.
- `Binder` is the scope-aware object that performs lookup and creates bound nodes.
- `BoundNode` is the internal semantic result: binding facts, symbols, conversions, diagnostics, and children.
- `IOperation` is the public-facing semantic operation tree built from bound nodes.

Do not skip directly from syntax to operation unless the syntax is truly not semantic-bearing. Do not put operation creation into the binder. Do not put bound-node creation into the operation factory. The binder is the bound-node factory; the operation factory only materializes operations from bound nodes.

### 7.1 Decision Table

Use this table before adding semantic code:

| New syntax kind | Declaration? | Symbol? | Binder? | BoundNode? | Operation? |
| --- | --- | --- | --- | --- | --- |
| Pure punctuation / helper node | No | No | No | No | No |
| A named declaration (`state`, `param`, `command`) | Yes | Yes | Usually existing component binder | Yes | Usually no |
| A new scope/container | Yes | Maybe | Yes | Yes | Maybe |
| Markup attribute with behavior | No | Maybe target symbol | Markup binder | Yes | Yes |
| Markup element/content | Maybe if it is a scope-like declaration | Usually target component symbol | Markup binder | Yes | Sometimes |
| AKCSS style/utility/module | Yes | Yes | AKCSS binder | Yes | Style body operations |
| AKCSS body directive/property setter | No | Target property/style/type symbols | AKCSS style binder | Yes | Yes |
| C# expression wrapper | No | Roslyn symbols plus mapped Akbura symbols | C# probe binder | Yes | C# operation subtree |

If in doubt, ask: "Can another piece of code refer to this by name?" If yes, it probably needs a symbol. Ask: "Does this introduce a scope or a declaration entry in the project?" If yes, it probably needs a declaration.

### 7.2 Declaration

A `Declaration` is not a symbol. It is a lightweight source declaration tree used to organize the project before full semantic binding.

Declaration files live in [Akbura.Generator/Language/Declarations](../Akbura.Generator/Language/Declarations).

Important files:

- `DeclarationKind.cs`;
- `DeclarationTreeBuilder.cs`;
- `DeclarationTable.cs`;
- `DeclarationSymbolTable.cs` under `Language/Symbols`.

Create or extend declaration support when the new syntax:

- introduces a named source entity;
- introduces a source container or scope;
- should participate in project-level discovery;
- should be visible to binders without walking the full syntax tree every time;
- needs declared-symbol caching.

Current declaration kinds include:

```csharp
internal enum DeclarationKind : byte
{
    None = 0,
    Namespace,
    Using,
    Component,
    State,
    Parameter,
    InjectedService,
    Command,
    MarkupRoot,
    MarkupElement,
    AkcssModule,
    AkcssUsing,
    AkcssStyle,
    AkcssUtility,
}
```

To add a declaration-backed syntax:

1. Add a `DeclarationKind` only if no existing kind describes the source entity.
2. Update `DeclarationTreeBuilder` so it visits the syntax and creates a `Single...Declaration`.
3. Add the declaration as a child of the correct container.
4. Add member-name collection if the container caches member names.
5. Add diagnostics only when they are cheap and declaration-level.
6. Update `DeclarationSymbolTable` if the declaration can produce an `ISymbol`.

Example shape:

```csharp
public override SingleNamespaceOrTypeDeclaration VisitAkcssStyleRuleSyntax(
    AkcssStyleRuleSyntax node)
{
    return CreateLeafDeclaration(
        DeclarationKind.AkcssStyle,
        node.Selector.ToFullString().Trim(),
        node);
}
```

Declarations should stay cheap. Do not do C# binding, type resolution, overload resolution, property lookup, command signature validation, or operation creation in the declaration layer.

### 7.3 ISymbol

An `ISymbol` represents a semantic entity that users or other semantic layers can refer to by name.

Symbol files live in [Akbura.Generator/Language/Symbols](../Akbura.Generator/Language/Symbols).

Create a symbol when the new syntax declares something like:

- a component;
- a state;
- a parameter;
- an injected service;
- a command;
- a user hook;
- a use effect;
- an AKCSS module;
- an AKCSS style;
- an AKCSS utility;
- a utility parameter.

Do not create a symbol for syntax that is only structure. For example, a token, punctuation node, wrapper node, or anonymous body node usually should not have its own symbol.

All symbols implement `ISymbol`:

```csharp
internal interface ISymbol
{
    SymbolKind Kind { get; }
    SymbolLanguage Language { get; }
    string Name { get; }
    string MetadataName { get; }
    ISymbol? ContainingSymbol { get; }
    CSharpSymbolDefinition CSharpDefinition { get; }
    ImmutableArray<Location> Locations { get; }
    ImmutableArray<ISymbolDeclarationReference> DeclaringSyntaxReferences { get; }
    bool CanBeReferencedByName { get; }
    bool IsDefinition { get; }
    bool IsImplicitlyDeclared { get; }
}
```

To add a new symbol:

1. Add a `SymbolKind` if an existing kind does not fit.
2. Add an interface if the symbol exposes domain-specific facts.
3. Add a concrete symbol class, usually derived from the existing symbol base.
4. Add visitor methods to `SymbolVisitor` variants if the symbol is first-class.
5. Teach `DeclarationSymbolTable` or the relevant binder/member model how to create it.
6. Teach lookup binders how to return it.
7. Add tests for `GetSymbolInfo(...)`, `GetDeclaredSymbol(...)`, and lookup from C# expressions if applicable.

Symbols should describe facts, not perform binding. If computing a property requires binding C# expressions or resolving overloads, keep that work in the binder and store only the result.

### 7.4 Binder

A `Binder` is a scope-aware semantic worker. It owns lookup and creates bound nodes.

Binder files live in [Akbura.Generator/Language/Binder](../Akbura.Generator/Language/Binder).

The base binder chain is a chain of responsibility:

```csharp
internal abstract class Binder
{
    public Binder? Next { get; }
    public Declaration? Declaration { get; }
    public AkburaSyntax? ScopeDesignator { get; }
    public AkburaBinderFlags Flags { get; }

    public virtual AkburaSymbolInfo LookupSymbol(AkburaSyntax syntax);
    public virtual BoundNode BindSemanticSyntax(AkburaSyntax syntax);
    public virtual BoundNode BindOperationSyntax(AkburaSyntax syntax);
}
```

Create or extend a binder when the syntax:

- introduces a new lookup scope;
- needs custom name lookup;
- binds a syntax node to a symbol;
- validates semantic rules;
- creates bound nodes;
- coordinates C# probe binding.

Existing binder responsibilities:

- `CompilationBinder` is the root.
- `ComponentBinder` owns component-level declarations such as state, param, inject, command, useEffect, userHook.
- `MarkupBinder` binds markup elements, attributes, content, routed events, command bindings, and tailwind attributes.
- `AkcssModuleBinder` owns AKCSS module-level styles/utilities.
- `AkcssStyleBinder` binds AKCSS style/utility body members and utility parameters.
- `BlockBinder` owns symbols declared inside executable blocks.
- `CSharpProbeBinder` coordinates Roslyn C# binding for Akbura expression contexts.

To add binder support:

1. Pick the binder that owns the scope.
2. Override `BindSemanticSyntax(...)` if the syntax should have a semantic bound node.
3. Override `BindOperationSyntax(...)` if the syntax should produce an operation later.
4. Override `LookupSymbolsInSingleBinder(...)` if the syntax declares names visible in the scope.
5. Add diagnostics to `BindingDiagnosticBag`, not ad hoc side channels.
6. Return a `Bound...` node even for failure cases when useful. Use error/bad bound nodes instead of throwing in normal user-code paths.

Example shape:

```csharp
public override BoundNode BindOperationSyntax(AkburaSyntax syntax)
{
    return syntax.Kind switch
    {
        AkburaSyntaxKind.AkcssAssignmentSyntax =>
            BindAkcssPropertySetter((AkcssAssignmentSyntax)syntax),
        AkburaSyntaxKind.AkcssIfDirectiveSyntax =>
            BindAkcssIf((AkcssIfDirectiveSyntax)syntax),
        _ => base.BindOperationSyntax(syntax),
    };
}
```

Binder code can call Roslyn through `CSharpProbeBinder`, but should do so through the existing session helpers when possible:

```csharp
var boundExpression = SemanticModel.BindingSession.BindExpression(
    syntax,
    expression,
    targetType);
```

### 7.5 BoundNode

A `BoundNode` is the internal semantic tree. It is not public API and it is not an operation.

Bound node files live in [Akbura.Generator/Language/BoundTree](../Akbura.Generator/Language/BoundTree).

`BoundNode` stores:

- `BoundKind`;
- source `Syntax`;
- the `Binder` that produced it;
- `AkburaSymbolInfo`;
- semantic diagnostics;
- child bound nodes;
- cached `HasErrors`.

Create a bound node when the syntax has semantic meaning after binding.

Current `BoundKind` includes:

```csharp
internal enum BoundKind : byte
{
    Declaration,
    ComponentDeclaration,
    StateDeclaration,
    ParamDeclaration,
    InjectDeclaration,
    CommandDeclaration,
    StateInitializer,
    ParamDefaultValue,
    UseHookInvocation,
    UseHookStatement,
    MarkupRoot,
    MarkupComponent,
    MarkupContent,
    MarkupContentSetter,
    MarkupPropertySetter,
    MarkupCommandBinding,
    MarkupRoutedEventBinding,
    TailwindUtilityAttribute,
    AkcssModule,
    AkcssStyle,
    AkcssUtility,
    AkcssPropertySetter,
    AkcssIf,
    AkcssApply,
    AkcssIntercept,
    Block,
    CSharpStatement,
    BadStatement,
    LocalDeclarationStatement,
    Expression,
    CSharpExpression,
    ConversionExpression,
}
```

To add a new bound node:

1. Add a `BoundKind` value.
2. Add a concrete `Bound...` class in the relevant `Bound...Nodes.cs` file or a new focused file.
3. Store only binding facts: symbols, types, conversions, constants, diagnostics, child bound nodes.
4. Do not store `IOperation`.
5. Add an `Update(...)` method if rewriters need to preserve node identity when nothing changed.
6. Add visitor/walker/rewriter methods in `BoundTreeVisitor`, `BoundTreeWalker`, and `BoundTreeRewriter` if the node is first-class.
7. Update the binder to create this node.

Bound nodes should answer questions like:

- What syntax produced this semantic result?
- Which symbol did it bind to?
- What type/conversion did it get?
- What diagnostics were produced?
- Which child bound nodes belong under it?

Bound nodes should not answer UI-style questions like "what operation should an IDE display?" That belongs to `IOperation`.

### 7.6 IOperation

An `IOperation` is the semantic operation tree materialized from a `BoundNode`.

Operation files live in [Akbura.Generator/Language/Operations](../Akbura.Generator/Language/Operations).

Create an operation when consumers should be able to inspect an executable or semantic action through `GetOperation(...)`.

Examples:

- markup property setter;
- markup routed event binding;
- markup command binding;
- markup content setter;
- tailwind utility attribute;
- AKCSS property setter;
- AKCSS `@if`;
- AKCSS `@apply`;
- AKCSS `@intercept`;
- C# expression subtree projected from Roslyn `IOperation`.

All operations implement `IOperation`:

```csharp
internal interface IOperation
{
    OperationKind Kind { get; }
    OperationLanguage Language { get; }
    AkburaSyntax Syntax { get; }
    IOperation? Parent { get; }
    ImmutableArray<IOperation> Children { get; }
    ISymbol? TargetSymbol { get; }
    ISymbol? TypeSymbol { get; }
    CSharpOperationDefinition CSharpDefinition { get; }
    bool IsImplicit { get; }
    bool HasErrors { get; }
    object? ConstantValue { get; }
}
```

To add a new operation:

1. Add an `OperationKind` value.
2. Add an interface if consumers need domain-specific properties.
3. Add a concrete operation class.
4. Add visitor methods in `OperationVisitor` variants.
5. Update `AkburaOperationFactory.CreateOperation(...)` to map the new `BoundKind` to the new operation.
6. Build child operations from child bound nodes or C# operation definitions.
7. Set parent/children consistently.

The operation factory should read facts from bound nodes. It should not perform fresh binding, lookup, conversion classification, or diagnostics creation unless it is intentionally materializing nested operations from already-bound facts.

### 7.7 How The Layers Connect

The normal flow for declaration syntax is:

```text
StateDeclarationSyntax
  -> DeclarationTreeBuilder creates DeclarationKind.State
  -> DeclarationSymbolTable creates IStateSymbol
  -> ComponentBinder exposes it in component lookup
  -> ComponentMemberSemanticModel creates BoundStateDeclaration
  -> GetSymbolInfo(stateSyntax) returns the state symbol
```

The normal flow for operation syntax is:

```text
MarkupPlainAttributeSyntax
  -> MarkupBinder.BindOperationSyntax(...)
  -> BoundMarkupPropertySetter / BoundMarkupRoutedEventBinding / ...
  -> AkburaOperationFactory.CreateOperation(...)
  -> IMarkupPropertySetterOperation / IMarkupRoutedEventBindingOperation / ...
  -> GetOperation(attributeSyntax)
```

The normal flow for AKCSS operation syntax is:

```text
AkcssAssignmentSyntax
  -> AkcssStyleBinder.BindOperationSyntax(...)
  -> BoundAkcssPropertySetter
  -> AkburaOperationFactory.CreateOperation(...)
  -> IAkcssPropertySetterOperation
```

The normal flow for embedded C# is:

```text
CSharpExpressionSyntax or InlineExpressionSyntax
  -> CSharpProbeBinder / BindingSession.BindExpression(...)
  -> BoundCSharpExpression or operation-bearing bound node with CSharpOperationDefinition
  -> CSharpOperationTreeBuilder projects Roslyn IOperation
  -> ICSharpOperation subtree under the owning Akbura operation
```

### 7.8 What Not To Do

Avoid these mistakes:

- Do not create a declaration for every syntax node. Only declaration-like and scope-like syntax belongs there.
- Do not create a symbol for anonymous syntax structure.
- Do not make `AkburaSemanticModel` create bound nodes directly for normal syntax paths.
- Do not put `IOperation` into `BoundNode`.
- Do not make `AkburaOperationFactory` do lookup or type checking that belongs in binders.
- Do not throw for normal user-code errors. Report diagnostics and produce error/bad bound nodes.
- Do not test private fields or class modifiers just to enforce architecture. Prefer behavior: symbol lookup, bound node shape, operation shape, diagnostics, and incremental reuse.

## 8. Add Tests

At minimum, add parser tests that verify:

- the syntax parses into the expected node kind;
- important child properties are present;
- invalid input recovers well;
- `ToFullString()` preserves the original text.

If the syntax is semantic-bearing, also add tests for:

- symbol info;
- bound node shape;
- operations;
- diagnostics;
- incremental parsing when editing around the new syntax.

Keep architecture tests focused on observable behavior. Avoid tests that only check whether a private field, private method, file, or class modifier exists.
