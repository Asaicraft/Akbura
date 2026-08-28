# Akbura Vs Code Extension

Visual Studio Code support for `.akbura` and `.akcss` files.

## Features

- syntax and semantic highlighting;
- diagnostics;
- completion and completion resolve;
- hover;
- go to definition;
- code actions;
- semantic tokens;
- document and workspace symbols;
- folding;
- references;
- rename;
- signature help;
- document, range, and on-type formatting;
- syntax-aware pairs, markup closing tags, raw strings, interpolation,
  paired Backspace, and Tab overtype.

## Automatic pairing

Akbura asks the language server whether a delimiter is structural before it
creates a pair. This keeps markup, embedded C#, standalone AKCSS, and inline
`@akcss` behavior consistent, including syntax-only workspaces that do not have
a loaded project yet.

Use `akbura.editor.automaticPairing` to select one of these modes:

- `syntax` uses the syntax-aware Akbura typing service;
- `basic` uses only the pairs from VS Code's language configuration;
- `off` inserts delimiters without Akbura pairing.

`akbura.editor.autoClosingTags` controls matching markup end tags, while
`akbura.editor.rawStringCompletion` controls dynamic C# raw-string delimiters.
The extension also respects `editor.autoClosingBrackets` and
`editor.autoClosingQuotes`.

The syntax-aware mode intercepts VS Code's global `type` command only for
single ASCII delimiter characters in `.akbura` and `.akcss` editors. Ordinary
text, selections, multiple carets, and multi-character IME input are forwarded
to the standard editor. An extension that also replaces `type` may conflict;
use `basic` mode when both extensions cannot cooperate.

## Requirements

The bundled language server currently targets .NET 10.

Install the .NET 10 SDK and make sure `dotnet` is available in PATH,
or configure:

```json
{
  "akbura.server.dotnetPath": "C:\\Program Files\\dotnet\\dotnet.exe"
}
```

## Explicit project selection

When a workspace contains multiple solutions or projects, run:

- `Akbura: Select Solution`
- `Akbura: Select Project`

Use `Akbura: Clear Explicit Solution or Project` to return to automatic
workspace discovery.

## Diagnostics

Use:

- `Akbura: Show Language Server Output`
- `"akbura.trace.server": "verbose"`
- `"akbura.server.logLevel": "trace"`

The extension runs the packaged `akbura-lsp` process over standard input and
output. Server logs are written to the Akbura output channel and never to the
LSP protocol stream.
