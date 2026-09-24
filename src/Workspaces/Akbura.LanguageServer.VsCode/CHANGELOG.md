# Change Log

## 12.0.7

- Added completion for state hooks and improved declaration-name completion.
- Preserved valid completion candidates while Visual Studio semantic snapshots update.
- Fixed markup type discovery and sibling project-reference resolution.
- Added support for non-generic collection parameter sources.

## 12.0.6

- Stabilized `${Binding ...}` completion while semantic snapshots lag behind typing.
- Prevented markup statement snippets from appearing inside binding extensions.
- Added current-snapshot completion diagnostics and LSP regression coverage.

## 12.0.5

- Added reactive markup $foreach support.
- Added editor support for $foreach, including syntax highlighting and language services.
- Improved Hot Reload behavior for foreach collections and keyed item identity.
- Added documentation and executable documentation examples.
## 12.0.4

- Aligned the extension version with Akbura and Avalonia.
- Added the Akbura icon to the VS Code Marketplace package.

## 0.1.1

- Renamed the extension to Akbura Vs Code Extension.

## 0.1.0

- Added language declarations and TextMate grammars for `.akbura` and `.akcss`.
- Added the packaged Akbura language server and stdio client.
- Added diagnostics, completion, navigation, semantic tokens, symbols,
  references, rename, signature help, code actions, and formatting support.
- Added commands for restarting the server and selecting a solution or project.
