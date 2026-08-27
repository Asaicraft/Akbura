# Akbura Language Support

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
- document, range, and on-type formatting.

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
