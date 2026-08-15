---
title: Built-in Utility Variants
summary: Use Tailwind-style interaction, state, structural, theme, and responsive prefixes with AKCSS utilities.
---

## Built-in utility variants

Import the variants from `Akbura.Markup` and place a markup extension before
an AKCSS utility:

```akbura
using Akbura.Markup;
using Akbura.Styles.akcss;

<Button p-2
        ${hover}:p-3
        ${focusVisible}:opacity-100 />

<TextBox opacity-75
         ${focus}:opacity-100
         ${invalid}:border-red-500 />

<Border bg-white
        ${dark}:bg-slate-900
        ${light}:bg-slate-50 />
```

Every built-in variant uses
`UnprefixedUtilityPrecedence.Above` and installs the winning property
operation at `BindingPriority.StyleTrigger`. When a condition becomes false,
Akbura disposes only that contribution and Avalonia reveals the next winning
value.

Names that contain a hyphen in Tailwind use camel case in Akbura because
markup-extension type names are C# identifiers. For example,
`focus-within` becomes `${focusWithin}`, `max-md` becomes
`${maxMd}`, and `out-of-range` becomes `${outOfRange}`.

## Interaction and theme

| Variant | Active condition | Conflict group | Order |
| --- | --- | --- | ---: |
| `${focusWithin}` | The target or a descendant has keyboard focus | Interaction | `5` |
| `${hover}` | The target's `IsPointerOver` is `true` | Interaction | `10` |
| `${focus}` | The target's own `IsFocused` is `true` | Interaction | `20` |
| `${focusVisible}` | Avalonia exposes `:focus-visible` on the target | Interaction | `30` |
| `${active}` | A supported control exposes `:pressed` | Interaction | `40` |
| `${light}` | The effective theme inherits `ThemeVariant.Light` | Color scheme | `10` |
| `${dark}` | The effective theme inherits `ThemeVariant.Dark` | Color scheme | `20` |

`${focus}` is intentionally different from `${focusWithin}`: focusing a
child activates only the latter. `${focusVisible}` follows Avalonia's input
navigation state rather than treating every focused control as keyboard
focused.

Theme variants observe the target's effective `ActualThemeVariant`. They
react to application, window, and inherited `ThemeVariantScope` changes. A
custom theme variant also matches the light or dark variant it inherits.

Tailwind has a built-in `dark` variant but normally uses unprefixed utilities
for light mode. Explicit `${light}` is an Akbura convenience.

## Control state

| Variant | Avalonia condition | Supported target |
| --- | --- | --- |
| `${enabled}` | `IsEffectivelyEnabled == true` | Any `InputElement` |
| `${disabled}` | `IsEffectivelyEnabled == false` | Any `InputElement` |
| `${visited}` | Pseudo-class `:visited` | Controls that publish it, such as links |
| `${open}` | `:open`, `:expanded`, `:dropdownopen`, or `:flyout-open` | Disclosure, menu, drop-down, and flyout controls |
| `${checked}` | Pseudo-class `:checked` | Toggle controls |
| `${indeterminate}` | Pseudo-class `:indeterminate` | Three-state toggle controls |
| `${selected}` | Pseudo-class `:selected` | Selecting item containers |
| `${optional}` | `AutomationProperties.IsRequiredForForm == false` | Any `StyledElement` |
| `${required}` | `AutomationProperties.IsRequiredForForm == true` | Any `StyledElement` |
| `${valid}` | `DataValidationErrors.HasErrors == false` | Any `StyledElement` |
| `${invalid}` | `DataValidationErrors.HasErrors == true` | Any `StyledElement` |
| `${inRange}` | Non-null `Value` is between `Minimum` and `Maximum`, inclusive | `NumericUpDown` |
| `${outOfRange}` | Non-null `Value` is outside `Minimum` and `Maximum` | `NumericUpDown` |
| `${readOnly}` | `IsReadOnly == true` | `TextBox`, `NumericUpDown` |
| `${placeholderShown}` | Text is empty and `PlaceholderText` is non-empty | `TextBox` |
| `${default}` | `IsDefault == true` | `Button` |

Related opposite states share a conflict group, so the applicable state wins
deterministically. Inherited disabling is respected because `${disabled}`
uses `IsEffectivelyEnabled`, not only the target's local `IsEnabled`.

These are Avalonia mappings, not an HTML validation engine. In particular,
required state comes from automation metadata, valid state comes from
Avalonia data-validation errors, and `${default}` means Avalonia's default
button. `${selected}` is an Akbura convenience for item containers.

Pseudo-class-backed variants activate only on controls that publish the
corresponding Avalonia pseudo-class.

## Logical-tree structure

| Variant | Active condition |
| --- | --- |
| `${first}` | First logical child |
| `${last}` | Last logical child |
| `${only}` | Only logical child |
| `${odd}` | Odd one-based logical-child position |
| `${even}` | Even one-based logical-child position |
| `${firstOfType}` | First sibling with the same `StyleKey` |
| `${lastOfType}` | Last sibling with the same `StyleKey` |
| `${onlyOfType}` | Only sibling with the same `StyleKey` |
| `${empty}` | No logical children |

The conditions update after insertions, removals, or reparenting. They use
Avalonia's logical tree rather than CSS's DOM tree, and “of type” compares the
effective Avalonia `StyleKey`.

The parameterized variants model CSS's `an+b` positions:

```akbura
<Border ${nth Step=2, Offset=1}:bg-slate-100 />
<Border ${nthLast Step=0, Offset=3}:font-bold />
<Border ${nthOfType Step=2, Offset=0}:opacity-75 />
<Border ${nthLastOfType Step=-1, Offset=3}:p-2 />
```

`Step=0` matches the single one-based position in `Offset`. The default is
`Step=0, Offset=1`.

## Direction, viewport, and platform

| Variant | Active condition |
| --- | --- |
| `${ltr}` | Effective `FlowDirection.LeftToRight` |
| `${rtl}` | Effective `FlowDirection.RightToLeft` |
| `${portrait}` | Top-level client height is at least its width |
| `${landscape}` | Top-level client width is greater than its height |
| `${contrastMore}` | Platform contrast preference is `High` |

Direction follows Avalonia's inherited flow direction. Orientation and
contrast observe the target's current `TopLevel`; if no top level or platform
settings are available, the extension does not produce a candidate.

## Responsive variants

The minimum breakpoints are mobile-first. Maximum breakpoints use an exclusive
upper bound:

| Minimum | Width | Maximum | Width |
| --- | ---: | --- | ---: |
| `${sm}` | `>= 640` | `${maxSm}` | `< 640` |
| `${md}` | `>= 768` | `${maxMd}` | `< 768` |
| `${lg}` | `>= 1024` | `${maxLg}` | `< 1024` |
| `${xl}` | `>= 1280` | `${maxXl}` | `< 1280` |
| `${xxl}` | `>= 1536` | `${maxXxl}` | `< 1536` |

`${xxl}` and `${maxXxl}` correspond to Tailwind's `2xl` names because
a C# type cannot begin with a digit. All fixed breakpoints share one conflict
group. The largest active minimum breakpoint wins; among simultaneous maximum
conditions, the narrowest active range wins.

Arbitrary viewport thresholds use a named width in device-independent pixels:

```akbura
<Border ${min Width=900}:p-6 />
<Border ${max Width=900}:p-2 />
```

`${min}` uses `>= Width`; `${max}` uses `< Width`. They intentionally
use source order rather than joining the fixed-breakpoint scale.

## Current limits

Akbura currently accepts exactly one prefix per utility attribute. Native
Tailwind stacking such as `dark:md:hover:...` is not yet parsed; write
separate single-prefix attributes or a custom condition extension.

Variants that would change the utility target or require a CSS selector model
are not represented by a misleading approximation. This includes
pseudo-elements, direct/all-child selectors, `group-*`, `peer-*`,
`has-*`, `in-*`, `not-*`, arbitrary selector/data/ARIA variants,
`target`, and `details-content`.

The current Avalonia platform contract also has no portable reactive signal
for browser autofill, user-valid interaction history, reduced motion,
contrast-less, forced or inverted colors, pointer capability, scripting,
print, CSS `@supports`, or starting-style. Tailwind container queries need a
container-selection model and are therefore not treated as viewport queries.

See the [Tailwind variant reference](https://tailwindcss.com/docs/hover-focus-and-other-states#appendix)
and [responsive design documentation](https://tailwindcss.com/docs/responsive-design)
for the CSS originals.
