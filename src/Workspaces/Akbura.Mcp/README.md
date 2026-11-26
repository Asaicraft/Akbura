# Akbura MCP

Akbura MCP is a local stdio server for one prepared C# project. Stage 1 reports the analyzed project context and finds AKCSS utilities for the actual control type in a saved `.akbura` document. The catalog lives in `Akbura.Workspaces` and is also available to editor integrations through `AkburaWorkspace.LanguageServices.UtilityCatalog`.

## Build and connect

Use the .NET SDK selected by the repository's `global.json` to build the server. Install the SDK and workloads required by the project you plan to analyze.

```sh
dotnet build src/Workspaces/Akbura.Mcp/Akbura.Mcp.csproj --configuration Release
```

Prepare the trusted project separately. Restore and build it for the configuration and framework you want to analyze so MSBuild has the resolved dependencies and generated inputs.

```sh
dotnet restore /absolute/path/MyApp.csproj
dotnet build /absolute/path/MyApp.csproj --no-restore --configuration Debug --framework net10.0
```

Configure your MCP client to launch the built DLL over stdio. `--project` is required and accepts an absolute `.csproj` path. `--configuration` defaults to `Debug`. A project with multiple target frameworks requires `--target-framework`.

```sh
dotnet /absolute/path/Akbura/src/Workspaces/Akbura.Mcp/bin/Release/net10.0/akbura-mcp.dll \
  --project /absolute/path/MyApp.csproj --configuration Debug --target-framework net10.0
```

An equivalent client entry uses the command and argument list below. On Windows, use absolute paths with JSON-escaped backslashes or forward slashes.

```json
{
  "command": "dotnet",
  "args": [
    "/absolute/path/akbura-mcp.dll",
    "--project",
    "/absolute/path/MyApp.csproj",
    "--configuration",
    "Debug",
    "--target-framework",
    "net10.0"
  ]
}
```

Stdout carries MCP messages. Startup and runtime logs go to stderr. The server uses the existing MSBuild design-time loader and does not restore, build, launch, or edit the application in response to a tool call.

## `akbura_project_info`

Call with `{}`. The structured response includes `projectPath`, `configuration`, `targetFramework`, `status`, `snapshotId`, `isComplete`, and `diagnostics`. `documents` summarizes registered `.akbura` and `.akcss` files and lists project-relative `.akbura` paths.

`serverVersion`, `compilerVersion`, and `workspaceVersion` identify the running tools. `packageVersions` comes from the selected target in `project.assets.json`. `assemblyVersions` reports the referenced Akbura and Avalonia assembly identities separately. `languageCompatibility` is `unknown` because version numbers alone do not verify compatibility with the project’s language features.

`standardLibrary` reports whether the standard utility module is present and the imports for utilities and variants found in the references. `guidance` includes a checked example when the references provide the example’s utility and variant.

## `akbura_find_utilities`

Pass `documentPath` for a registered `.akbura` input of the selected project. Relative paths are resolved against the project directory. Select a target with exactly one of `targetType`, resolved in the document’s project and import context, or `position`, a zero-based UTF-16 offset inside a markup element.

`query` accepts a name, prefix, category, or supported keyword or phrase. Examples include `spacing between items`, `расстояние между элементами`, and `расстояние между карточками`. `category` narrows the results. The initial categories are `sizing`, `spacing`, `grid`, `alignment`, `visibility`, and `appearance`. `layout` covers sizing, spacing, grid, and alignment. `includeImportable` defaults to `true`. `limit` defaults to `20` and accepts `1` through `100`.

```json
{
  "documentPath": "Views/Heroes.akbura",
  "targetType": "Akbura.Row",
  "query": "расстояние между карточками",
  "category": "layout",
  "includeImportable": true,
  "limit": 1
}
```

A project that references the standard library without importing it can return a `gap` definition with these fields.

```json
{
  "name": "gap",
  "usageSyntax": "gap-<value>",
  "parameters": [
    {
      "name": "value",
      "type": "double",
      "isOptional": false
    }
  ],
  "declaredTargetType": "Akbura.Row",
  "availability": "requires_import",
  "requiredImports": [
    "using Akbura.Styles.akcss;"
  ],
  "originKind": "library",
  "category": "spacing"
}
```

`usageSyntax` uses angle-bracket placeholders for arguments. An `available` definition resolves in the current document. A `requires_import` definition comes from a referenced library and returns the exact import to add. When the library is absent or its import is shadowed, the diagnostics explain the limitation. Project utility definitions have source locations when the Workspace has the source document.

The response includes `status`, `resolvedTargetType`, `utilities`, `snapshotId`, `isComplete`, `truncated`, and `diagnostics`. Exact names rank first, then prefixes, then keyword or category matches, with ties sorted by name and signature. An empty successful search differs from errors such as `unknown_document`, `invalid_position`, `unresolved_target`, `ambiguous_target`, and `missing_references`.

## Working with saved sources

The server synchronizes changes to registered `.akbura`, `.akcss`, and C# sources before capturing a request snapshot. If a project file, monitored build import, resolved reference, or project input membership changes, the server reports `reload_required`. Prepare the project again as needed and reconnect. Syntax-only or incomplete semantic information is reported through status and diagnostics.

Call `akbura_project_info` first. Search for applicable standard utilities before implementing custom sizing, spacing, visibility, or layout behavior. Use utilities when they express the intended behavior and respect their target types and required imports. Verify variant support before using `${variant}:utility`. Use ordinary properties and custom styles where they express the behavior more clearly.

Stage 1 covers saved documents and deterministic search vocabulary. It supports one selected `.csproj` per process and local stdio transport.

## Run the tests

```sh
dotnet test src/Workspaces/Akbura.Mcp.IntegrationTests/Akbura.Mcp.IntegrationTests.csproj
```
