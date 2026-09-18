# Akbura Visual Studio Extension

Language support for `.akbura` and `.akcss` files in Visual Studio.

## Features

- syntax highlighting and classification;
- completion and completion commit behavior;
- diagnostics and suggested actions;
- native **Akbura Component** item template for Add New Item and Quick Add;
- quick info and navigation;
- outlining and smart indentation;
- syntax-aware automatic pairing and markup editing.

## Requirements

- Visual Studio 2022 version 17.14 or later;
- Community, Professional, or Enterprise edition;
- 64-bit Visual Studio.

The extension keeps its stable VSIX identity so installed copies receive future
updates from the same Marketplace listing.

## What's new in 12.0.4.3

- Added editor support for reactive $foreach markup.
- Added $foreach syntax classification and highlighting.
- Improved language services for foreach variables, @index, navigation, rename, completion, and formatting.
- Included the latest workspace and parser fixes.
## What's new in 12.0.4.2

- Fixed incremental parsing while typing utilities such as `w-30`, so completed values no longer retain a stale `Identifier expected` diagnostic.
- Added regression coverage for utility prefixes, numeric continuations, replacements, and deletions.

## What's new in 12.0.4.1

- Added the native **Akbura Component** item template for Add New Item and Quick Add.

## What's new in 12.0.4

- Aligned the extension version with Akbura and Avalonia.
- Added Akbura artwork to the Marketplace listing and Extension Manager.

