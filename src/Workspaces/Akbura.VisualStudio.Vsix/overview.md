# Akbura Visual Studio Extension

Language support for `.akbura` and `.akcss` files in Visual Studio.

Expression bindings (`${x.bind ...}`) use shared Workspaces and Roslyn-projected
C# completion, hover, navigation, classification and diagnostics. The built-in
prefix has a separate completion item and retains safe `${...}` commit boundaries.
Syntactic highlighting and prefix completion work before project loading; C#
semantic features need a loaded project. Shared references/rename semantics are
also available through LSP; a native Visual Studio Rename UI adapter is not yet
implemented. Native snapshot translation, pairing and Undo must be verified in
an experimental Visual Studio instance in addition to component-level tests.

## Features

- syntax highlighting and classification;
- completion and completion commit behavior;
- diagnostics and suggested actions;
- native **Akbura Component** item template for Add New Item and Quick Add;
- quick info and navigation;
- outlining and smart indentation;
- syntax-aware automatic pairing and markup editing.

## Requirements

- Visual Studio 2026 version 18.9 or later (Roslyn 5.9 or later);
- Community, Professional, or Enterprise edition;
- 64-bit Visual Studio.

The extension keeps its stable VSIX identity so installed copies receive future
updates from the same Marketplace listing.

## What's new in 12.0.4.17

- Reduced compiler and shared workspace allocations through pooling, symbol and metadata caches, and streaming symbol names.
- Updated Roslyn to 5.9.0 and preserved event-hook completion for incomplete lambdas.
- Requires Visual Studio 2026 version 18.9 or later.

## What's new in 12.0.4.6

- Added completion for state hooks and improved declaration-name completion.
- Preserved valid completion candidates while semantic snapshots update.
- Fixed markup type discovery and sibling project-reference resolution.
- Added support for non-generic collection parameter sources.
- Fixed automatic closing of braces when an opening brace is typed on a new line.

## What's new in 12.0.4.5

- Stabilized `${Binding ...}` completion while semantic snapshots lag behind typing.
- Prevented `$if` and `$foreach` suggestions from appearing inside binding extensions.
- Rejects stale completion commits after the editor snapshot changes.

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

