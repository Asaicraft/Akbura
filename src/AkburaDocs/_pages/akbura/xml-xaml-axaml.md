---
title: Differences between XML, XAML and AXAML
summary: Map XML and AXAML concepts to Akbura keys, styles, whitespace and C# raw strings.
---

Akbura uses an XML-like element structure, but an `.akbura` file is not an XML, XAML or AXAML document. It is an Akbura component that can contain normal C# declarations and expressions.

This distinction matters when syntax from XML or AXAML is copied into an Akbura component.

## XML, XAML and AXAML

**XML** is a general-purpose markup format.

**XAML** is an XML-based language for declaring .NET object graphs and user interfaces.

**AXAML** means Avalonia XAML. Avalonia uses the `.axaml` extension to distinguish its XAML dialect from XAML files used by other frameworks.

Akbura keeps familiar concepts such as elements, attributes and property elements, but it is compiled by the Akbura compiler rather than treated as an XML document.

## Dictionary keys

AXAML uses the XML directive `x:Key`. Akbura uses `x.key` or its alias `x.Key`:

```akbura
using Avalonia.Controls;
using Avalonia.Media;

state int resourceIndex = 0;

<Border>
    <Border.Resources>
        <SolidColorBrush x.key="AccentBrush" Color="Red" />
        <SolidColorBrush x.Key={resourceIndex + 1} Color="Blue" />
    </Border.Resources>
</Border>
```

These are two spellings of one dictionary-entry directive. `x:Key` is not an
additional spelling in Akbura. Other attribute names are still case-sensitive.
A quoted key is a string; `{resourceIndex + 1}` is a typed C# key expression.
The key is attached to the parent dictionary entry rather than assigned to a
property of `SolidColorBrush`.

`x.Name` continues to name an element in the component and serves a different
purpose. Dynamic dictionary keys do not replace element identity. See
[Dictionary Resources](/#dictionary-resources) for key typing, updates,
ownership, and collisions.

## Styles and property references

Avalonia styles use the familiar property-element shape in Akbura:

```akbura
using Avalonia.Controls;
using Avalonia.Styling;

<Border>
    <Border.Styles>
        <Style Selector="Button">
            <Setter Property="Background" Value="Red" />
        </Style>
    </Border.Styles>
</Border>
```

`Property="Background"` resolves a static Avalonia property reference using the
style's target context. `Property="Button.Background"` supplies an explicit
owner, and `Property={Button.BackgroundProperty}` uses C# directly. This also
works for custom holder properties of type `AvaloniaProperty`.

Markup extensions keep Akbura's `${...}` syntax, including bindings stored in
a setter:

```akbura
using Avalonia.Controls;
using Avalonia.Styling;
using Akbura.Markup;

<Setter Property="Button.Background" Value=${Binding AccentBrush} />
```

See [Avalonia Styles](/#avalonia-styles) for supported literal selector forms,
typed child insertion, contextual values, and property metadata.

## Whitespace and `xml:space`

CDATA and `xml:space` solve different problems.

CDATA prevents its contents from being interpreted as XML markup:

```xml
<![CDATA[
<Button Content="Hello" />
]]>
```

The `xml:space` attribute controls how whitespace inside an XML element should be handled:

```xml
<poem xml:space="preserve">
    First line
        Indented line
    Last line
</poem>
```

XML defines two values:

| Value | Meaning |
| --- | --- |
| `default` | The application's default whitespace processing is acceptable |
| `preserve` | The application should preserve the whitespace |

The value is inherited by descendant elements until it is overridden:

```xml
<root xml:space="preserve">
    <first>
        Whitespace is preserved here.
    </first>

    <second xml:space="default">
        Default processing is restored here.
    </second>
</root>
```

### `xml:space` in XAML and AXAML

XAML processors normally normalize whitespace found in element content. Depending on the content model, line breaks and tabs may be converted to spaces, repeated spaces may be collapsed, and whitespace near element boundaries may be removed.

Use `xml:space="preserve"` when the text must retain its authored whitespace:

```xml
<TextBlock xml:space="preserve">
    First line
        Indented line
    Last line
</TextBlock>
```

### Whitespace in Akbura

Akbura files are not XML documents, so `xml:space` has no special meaning.

Use a C# raw string to define the exact text value:

```akbura
<TextBlock Text={"""
First line
    Indented line
Last line
"""}/>
```

The indentation of the closing delimiter determines the common indentation removed from every content line:

```akbura
<Border>
    <TextBlock Text={"""
        First line
            Indented line
        Last line
        """}/>
</Border>
```

The indentation needed to align the raw string with the surrounding component is removed. Additional indentation inside the content remains in the resulting string.

### Comparison

| Construct | Purpose |
| --- | --- |
| `<![CDATA[ ... ]]>` | Prevent XML content from being parsed as markup |
| `xml:space="preserve"` | Request preservation of whitespace in XML or XAML content |
| `{""" ... """}` | Produce a C# string value inside an Akbura expression |

Raw strings replace CDATA in Akbura. They also make `xml:space` unnecessary for defining multiline text because whitespace belongs directly to the resulting C# string.

## CDATA is not supported

XML can use a CDATA section when text must contain characters that would otherwise be interpreted as markup:

```xml
<FeatureView.Code>
    <![CDATA[
    <Button Content="Hello" />
    ]]>
</FeatureView.Code>
```

Akbura does not use CDATA. Place a C# raw string inside an Akbura expression instead:

```akbura
<FeatureView.Code>
    {"""
    <Button Content="Hello" />
    """}
</FeatureView.Code>
```

The outer `{ ... }` is an Akbura C# expression. The `""" ... """` part is a standard [C# raw string literal](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/tokens/raw-string).

## Why raw strings are preferable in Akbura

### Indentation is normalized

A multiline raw string uses the indentation of its closing delimiter to determine which leading whitespace should be removed.

This lets the embedded text stay aligned with the surrounding component:

```akbura
<TextBlock Text={"""
    First line
        Nested line
    Last line
    """}/>
```

The indentation required by the component source is not included in the resulting string.

### Quotes and backslashes need less escaping

A raw string can contain ordinary quote characters and backslashes without the escaping required by a regular C# string:

```akbura
<TextBlock Text={"""
Path: C:\Projects\Akbura
Message: "Hello"
"""}/>
```

When the content itself contains three consecutive quote characters, use a longer delimiter:

```csharp
var text = """"
The content may contain """ without ending the string.
"""";
```

The opening and closing delimiters must contain more consecutive quote characters than any quote sequence inside the content.

### Raw strings support interpolation

Add `$` before the delimiter to create an interpolated raw string:

```akbura
param string name = "Akbura";

<TextBlock Text={$"""
Hello, {name}!
"""}/>
```

The interpolation expression is normal C# and can use:

- component state;
- parameters;
- injected services;
- local variables;
- method calls;
- other C# expressions.

For example:

```akbura
state int count = 3;

<TextBlock Text={$"""
The current count is {count}.
The next value is {count + 1}.
"""}/>
```

### Literal braces can be preserved

Interpolated raw strings can use multiple `$` characters. The number of `$` characters determines how many braces start an interpolation.

This is useful when the resulting text itself contains braces:

```csharp
var json = $$"""
{
    "name": "{{name}}"
}
""";
```

Here, single braces remain part of the text, while `{{name}}` is the interpolation.

## Comparison

| XML or AXAML | Akbura |
| --- | --- |
| `<![CDATA[ ... ]]>` | `{""" ... """}` |
| XML text container | C# string expression |
| No C# interpolation | Supports C# interpolation with `$"""` |
| XML whitespace rules | C# raw-string indentation rules |
| XML escaping model | Raw-string delimiter rules |

## Summary

Do not copy CDATA sections into `.akbura` files.

Use:

```akbura
{"""
Raw text
"""}
```

Use an interpolated raw string when the text depends on component data:

```akbura
{$"""
Value: {value}
"""}
```
