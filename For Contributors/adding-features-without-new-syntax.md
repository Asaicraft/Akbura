---
title: Adding Features Without New Syntax
summary: Learn how to extend Akbura by reusing existing language constructs, using xml.space as a reference implementation.
---

Akbura is a language, not only a source generator. Every new piece of syntax becomes part of its public contract and must be supported by the parser, semantic model, tooling, diagnostics, documentation, and future versions of the compiler.

For that reason, contributors should not introduce new syntax simply because a feature needs a new behavior.

The preferred approach is:

> Express the feature with syntax that Akbura already understands, then implement its meaning in the semantic pipeline.

The implementation of `xml.space` is a good example of this principle.

## The default rule

Before changing the grammar, ask whether the feature can be represented by an existing construct.

Akbura already supports:

- elements;
- plain attributes;
- attached-property-style attributes;
- prefixed attributes;
- literal values;
- dynamic expressions;
- markup extensions;
- directives with special compiler meaning.

A new feature should reuse one of these forms whenever its intent can be expressed clearly and without ambiguity.

For example, whitespace preservation could have been introduced with new Akbura-specific syntax:

```akbura
<Button @preserve-whitespace>
    Increment to {count + 1}
</Button>
```

or:

```akbura
<Button @whitespace={preserve}>
    Increment to {count + 1}
</Button>
```

Both alternatives would add another language rule that users must learn and the compiler must maintain.

Instead, Akbura supports the existing XML directive:

```akbura
<Button xml.space="preserve">
    Increment to {count + 1}
</Button>
```

No new grammar is required. The existing markup parser can already represent `xml.space` as an attribute. The compiler only needs to recognize that this particular attribute has special semantics.

## Why avoiding syntax matters

Adding syntax has a much larger cost than adding behavior.

A grammar change may require updates to:

- lexer rules;
- parser rules;
- syntax kinds;
- syntax nodes;
- syntax visitors and rewriters;
- error recovery;
- formatting;
- syntax highlighting;
- editor tooling;
- documentation;
- compatibility tests.

It also permanently increases the conceptual size of the language.

By contrast, a feature expressed through existing syntax can often be implemented entirely after parsing. This keeps the grammar stable and allows the semantic pipeline to remain responsible for meaning.

A stable grammar makes Akbura easier to learn, easier to maintain, and easier to evolve.

## Case study: `xml.space`

The `xml.space` directive controls how whitespace inside an element is interpreted.

Akbura supports two modes:

| Value | Behavior |
| --- | --- |
| `default` | Markup whitespace is normalized. Consecutive spaces, tabs, and line breaks are collapsed where appropriate. |
| `preserve` | Text is kept exactly as it appears in the source. |

Example:

```akbura
<Button>
    Increment to {count + 1}
</Button>
```

In the default mode, indentation used to format the source should not become part of the button text.

The effective content is equivalent to:

```text
Increment to {count + 1}
```

When preservation is explicitly requested:

```akbura
<Button xml.space="preserve">
    Increment to {count + 1}
</Button>
```

the indentation and line breaks are treated as meaningful content.

The directive is inherited by nested elements:

```akbura
<StackPanel xml.space="preserve">
    <TextBlock>
        This text preserves whitespace.
    </TextBlock>
</StackPanel>
```

A nested element can restore the default behavior:

```akbura
<StackPanel xml.space="preserve">
    <TextBlock>
        This text preserves whitespace.
    </TextBlock>

    <TextBlock xml.space="default">
        This text uses normal whitespace handling.
    </TextBlock>
</StackPanel>
```

This behavior was added without creating a new token, expression, attribute category, or parser production.

## Step 1: Confirm that the existing syntax is sufficient

Start by parsing a representative example and inspecting the syntax tree.

For `xml.space`, the existing parser already produces a `MarkupAttachedPropertyAttributeSyntax`. That node contains all required information:

- the `xml` prefix;
- the `space` member name;
- the attribute value;
- the containing element;
- the source location.

Because the syntax tree already represents the source correctly, changing the parser would provide no benefit.

A parser change is not justified merely because the compiler needs to treat one value specially.

## Step 2: Recognize the feature semantically

The syntax layer should describe what was written. It should not contain all rules about what the source means.

Recognition of a special directive belongs in the semantic model.

For a feature similar to `xml.space`, add focused semantic helpers that answer questions such as:

```csharp
IsMarkupWhitespaceDirective(attribute)
```

and:

```csharp
TryGetMarkupWhitespaceMode(attribute, out mode, out rawValue)
```

These helpers should:

1. identify the exact construct;
2. validate its basic form;
3. extract its semantic value;
4. avoid duplicating the same checks in multiple compiler stages.

Do not make the generator search raw attribute text independently. The semantic model should be the single source of truth.

## Step 3: Introduce a semantic representation

Do not pass unstructured strings through the entire compiler.

The raw values `"default"` and `"preserve"` are converted into a semantic enum:

```csharp
internal enum MarkupWhitespaceMode : byte
{
    Default = 0,
    Preserve,
}
```

This gives later stages a small, explicit, type-safe API.

The same principle applies to other features:

- parse raw source once;
- convert it into a semantic representation;
- let later stages consume that representation.

Avoid repeatedly comparing strings such as:

```csharp
value == "preserve"
```

throughout the binder, generator, and operation tree.

## Step 4: Resolve scope and inheritance separately

Some features are local to one node. Others affect descendants.

`xml.space` is inherited, so the implementation uses a dedicated resolver that can answer two different questions:

- What mode is declared directly on this element?
- What mode is effective for this element after inheritance?

Conceptually:

```text
effective mode =
    mode declared on the current element
    or nearest ancestor declaration
    or Default
```

Keeping this logic in a resolver prevents each consumer from implementing its own parent traversal.

For inheritable features, consider creating a dedicated resolver when:

- parent lookup is required;
- multiple compiler stages need the effective value;
- a child can override or reset the value;
- default behavior must be applied consistently.

Do not hide inheritance rules inside code generation.

## Step 5: Bind the construct explicitly

Even when no new syntax node is needed, a feature may still deserve its own bound node.

The binder recognizes `xml.space` before treating the attribute as an ordinary property or event assignment. It creates a specialized `BoundMarkupWhitespaceDirective` containing information such as:

- the original attribute syntax;
- the containing component;
- the raw value;
- the declared mode;
- the effective mode;
- diagnostics;
- error state.

This is an important distinction:

> Reusing existing syntax does not mean pretending that the feature has no unique semantics.

The syntax node can remain generic while the bound node becomes specific.

A dedicated bound representation is useful when the feature:

- has special validation;
- is not a normal runtime property assignment;
- affects compilation of other nodes;
- needs to appear in semantic APIs;
- requires specialized diagnostics or tooling.

## Step 6: Expose the feature through operations

Akbura's operation model is the public semantic view of compiled source.

A complete semantic feature should not exist only as a private condition inside the generator. `xml.space` has a dedicated operation kind and operation interface, allowing visitors and tools to observe it directly.

A specialized operation can expose:

- `RawValue`;
- `DeclaredMode`;
- `EffectiveMode`;
- `ContainingComponent`;
- `HasErrors`;
- the original syntax.

This keeps semantic tooling independent from compiler implementation details.

When adding a new operation, remember to update all related infrastructure:

- `OperationKind`;
- the operation interface;
- the concrete operation;
- the operation factory;
- visitors;
- parameterized visitors;
- rewriters, when applicable;
- equality and display behavior;
- semantic model operation access.

Missing one of these updates can leave the feature partially visible or cause visitor logic to silently skip it.

## Step 7: Apply behavior at the correct boundary

Whitespace behavior is applied while markup child content is converted into semantic text content.

This is the correct boundary because the compiler already knows:

- the effective whitespace mode;
- whether a child is text or an expression;
- how text and expressions will be combined;
- whether the resulting value is literal or interpolated.

The `MarkupWhitespaceNormalizer` receives a `MarkupWhitespaceMode` and produces normalized literal and interpolated text.

In `Default` mode, it:

- recognizes spaces, tabs, carriage returns, and line feeds as markup whitespace;
- collapses pending whitespace;
- avoids preserving indentation before the first meaningful character;
- preserves a single separator between meaningful content;
- handles boundaries between text and inline expressions.

In `Preserve` mode, it:

- appends the source text unchanged;
- still escapes characters required by generated interpolated strings.

The generator then consumes semantic child content instead of rebuilding whitespace rules from raw syntax.

This separation is essential:

```text
Syntax
  -> semantic recognition
  -> binding and inheritance
  -> normalized child content
  -> code generation
```

Code generation should emit the result of semantic analysis, not perform semantic analysis again.

## Step 8: Add diagnostics at the semantic level

Existing syntax can still contain an invalid semantic value:

```akbura
<Button xml.space="sometimes">
    Text
</Button>
```

The parser should accept this as a structurally valid attribute. The semantic pipeline should report that the value is invalid.

This produces better compiler behavior:

- the syntax tree remains complete;
- editor tooling can still inspect the attribute;
- the diagnostic can point to the relevant source;
- error recovery does not require a special parser rule;
- the bound node and operation can still be created with `HasErrors`.

Use parser diagnostics for malformed language structure.

Use semantic diagnostics for valid structure with invalid meaning.

For a new feature, add:

- a stable error code;
- a localized resource message;
- a helper that creates the diagnostic;
- tests for invalid values;
- tests confirming that valid values produce no diagnostic.

## Step 9: Test every compiler layer that changed

A feature is not complete when one generated example works.

For a change similar to `xml.space`, tests should cover several layers.

### Semantic recognition

Verify that the compiler recognizes the existing attribute as the intended directive rather than an ordinary attached property.

### Bound tree

Verify that binding creates the specialized bound node and stores:

- declared value;
- inherited value;
- effective value;
- diagnostics;
- error state.

### Operation tree

Verify that `GetOperation` returns the expected operation type and that visitors can reach it.

### Default behavior

Verify that source indentation is normalized:

```akbura
<Button>
    Increment to {count + 1}
</Button>
```

The generated content should not include formatting indentation.

### Preserve behavior

Verify that `xml.space="preserve"` keeps source whitespace:

```akbura
<Button xml.space="preserve">
    Increment to {count + 1}
</Button>
```

### Inheritance

Verify that descendants inherit the nearest declared value.

### Override and reset

Verify that a nested `xml.space="default"` resets inherited preservation.

### Invalid values

Verify that unsupported values produce the expected semantic diagnostic.

### Literal and interpolated content

Test both plain text and text containing inline expressions. These paths often differ during code generation.

### Generated compilation

Verify not only the emitted source text but also that the generated C# compiles.

## Keep responsibilities separated

A well-designed feature should have one owner for each responsibility.

| Responsibility | Preferred location |
| --- | --- |
| Represent source structure | Syntax tree |
| Identify a special existing construct | Semantic model |
| Convert raw values into typed meaning | Semantic helpers or resolver |
| Validate semantic rules | Binder and diagnostics |
| Represent compiled meaning | Bound tree |
| Expose meaning to tools | Operation tree |
| Apply content transformation | Dedicated semantic utility |
| Emit C# | Generator |
| Prove behavior | Unit tests |

Avoid implementations where:

- the parser performs inheritance;
- the generator scans raw attribute names;
- diagnostics are produced only during code emission;
- multiple stages parse the same string;
- runtime code compensates for missing compile-time semantics.

## A practical decision process

Before implementing a feature, follow this sequence.

### 1. Write the intended user-facing form

Start with a realistic example.

```akbura
<Element existing-form="value" />
```

Do not begin by adding a token or syntax kind.

### 2. Check whether the current parser can represent it

Inspect the syntax tree.

If all meaningful source parts are already present, keep the grammar unchanged.

### 3. Define the semantics independently from syntax

Write down:

- valid values;
- default value;
- inheritance rules;
- override rules;
- error cases;
- effect on generated code;
- expected operation model.

### 4. Choose the narrowest semantic extension

Add only the structures the behavior actually needs:

- a semantic recognizer;
- a typed value;
- a resolver;
- a bound node;
- an operation;
- a transformation utility;
- diagnostics.

Not every feature needs every item, but each responsibility should have an explicit home.

### 5. Keep the generator simple

The generator should ask semantic questions and emit code from semantic results.

It should not reinterpret the original feature.

### 6. Test behavior, not only implementation

Tests should prove what users observe and what tooling receives.

## When new syntax is justified

Avoiding syntax is a strong default, not an absolute prohibition.

New syntax may be appropriate when all of the following are true:

1. Existing syntax cannot express the feature without ambiguity.
2. Reusing an existing construct would communicate the wrong meaning.
3. The feature is fundamental enough to justify permanent language complexity.
4. Its grammar can remain consistent with the rest of Akbura.
5. Error recovery and tooling behavior are understood.
6. The proposal includes syntax, semantic, operation, generator, diagnostic, test, and documentation plans.

Examples of weak justification:

- the new form is slightly shorter;
- implementation appears easier in the parser;
- another framework uses similar punctuation;
- the feature is special internally;
- a custom keyword feels more explicit.

Internal uniqueness does not automatically require syntactic uniqueness.

## Pull request checklist

Before submitting a feature, confirm the following.

### Language design

- [ ] I first tried to express the feature with existing Akbura syntax.
- [ ] The chosen form is understandable to users.
- [ ] I documented why a grammar change is or is not required.
- [ ] Defaults, inheritance, overrides, and invalid cases are defined.

### Semantic pipeline

- [ ] Recognition is centralized.
- [ ] Raw values are converted into typed semantic values.
- [ ] The binder owns validation.
- [ ] Inheritance or scope resolution is not duplicated.
- [ ] The generator consumes semantic results.

### Compiler model

- [ ] Bound nodes are added when the feature has unique semantics.
- [ ] Operations expose the feature when tooling may need it.
- [ ] Visitors, factories, kinds, and rewriters are updated consistently.
- [ ] Error states remain inspectable.

### Diagnostics

- [ ] Invalid semantic values produce compiler diagnostics.
- [ ] Diagnostics have stable codes and clear messages.
- [ ] Diagnostics point to useful syntax locations.

### Tests

- [ ] Default behavior is tested.
- [ ] Explicit behavior is tested.
- [ ] Nested and inherited behavior is tested when applicable.
- [ ] Invalid inputs are tested.
- [ ] Literal and interpolated paths are tested.
- [ ] Generated C# is verified to compile.

### Documentation

- [ ] User-facing syntax is documented.
- [ ] Contributor-facing architecture is documented when non-obvious.
- [ ] The documentation does not describe implementation details as language guarantees.

## Final principle

The best language feature is often one that adds capability without adding grammar.

`xml.space` works because Akbura reuses a construct the markup language can already express, then gives that construct precise meaning through the semantic pipeline.

When implementing the next feature, begin with this question:

> Can Akbura understand this feature without learning a new way to parse it?

When the answer is yes, keep the syntax stable and put the new behavior where it belongs.
