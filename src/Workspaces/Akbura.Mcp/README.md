# Akbura MCP

Akbura MCP is a local stdio server for one prepared C# project. It describes the analyzed project, finds AKCSS utilities, retrieves layout reference components, and analyzes saved `.akbura` and `.akcss` documents. Workspaces supplies the shared utility, layout, and diagnostic services.

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

`standardLibrary` reports whether the standard utility module is present and the imports for utilities and variants found in the references. `guidance` includes a checked example when the references provide the example’s utility and variant. `layoutRecipes` lists each recipe's ID, title, and description.

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

## `akbura_get_layout_recipe`

Pass `documentPath` for a saved `.akbura` input and a `recipeId` from `akbura_project_info`.

```json
{
  "documentPath": "Views/Heroes.akbura",
  "recipeId": "responsive-card-grid"
}
```

The result includes the recipe ID and revision, title, purpose, tags, required imports and definitions, and the complete `exampleSource` in `akbura` format. `recipe.suggestedFileName` identifies the reference component. `snapshot` gives the captured solution, project, and destination document versions. Adapt the example to the real component and its surrounding layout.

| Recipe ID | Layout and preconditions |
| --- | --- |
| [`responsive-card-grid`](../Akbura.Workspaces/Layout/Recipes/ResponsiveCardGrid.akbura) | A `Row` with 12 logical columns and finite available parent width. Cards span 12 columns by default, 6 from 768 DIPs, and 4 from 1024 DIPs. `gap-3` sets both row and column spacing. |
| [`responsive-spacing`](../Akbura.Workspaces/Layout/Recipes/ResponsiveSpacing.akbura) | A `Border` with padding scale values 2, 4, and 6 at the same thresholds. Narrowing the window restores the remaining active value. Actual padding comes from the active `--spacing` resource. |
| [`responsive-secondary-content`](../Akbura.Workspaces/Layout/Recipes/ResponsiveSecondaryContent.akbura) | A vertical stack with stretchable primary content. At 1024 DIPs the secondary section appears below it with a gap of four spacing units. Below the threshold the primary content keeps the available width and the secondary section and gap take no layout space. |

All three examples use the top-level client width for breakpoint conditions and expect a `double` value for the standard `--spacing` resource. The widths in the table are from the bundled `md` and `lg` definitions exercised by the repository tests. The parent determines the space it allocates to each component.

`availability` is `available`, `requires_import`, `unavailable`, or `unknown`. `missingImports` lists imports needed in the destination document. `availabilityReasons` explains missing or shadowed definitions, validation failures, or incomplete project loading. A known recipe can return its reference source regardless of availability. An unknown ID is an `unknown_recipe` error.

Representative validation fields for a project that references the standard library and needs the styles import follow.

```json
{
  "status": "success",
  "availability": "requires_import",
  "missingImports": [
    "using Akbura.Styles.akcss;"
  ],
  "validation": {
    "status": "passed",
    "completedLevel": "semantic",
    "scope": "standalone_reference_component",
    "hasRepositoryRuntimeTests": true,
    "integratedDocumentValidated": false,
    "projectRuntimeValidated": false
  }
}
```

`validation.status` is `passed`, `failed`, `incomplete`, or `not_run`. Validation uses an isolated Workspace snapshot with the example's own imports. `examplePath` is virtual. The repository's headless tests exercise the bundled examples. The destination document and runtime layout in your project each require separate verification.

## `akbura_analyze_document`

Pass `documentPath` for a registered saved `.akbura` or `.akcss` input. `analysisLevel` defaults to `semantic` and also accepts `syntax`. `maxDiagnostics` defaults to `100` and accepts `1` through `500`.

```json
{
  "documentPath": "Views/Heroes.akbura",
  "analysisLevel": "semantic",
  "maxDiagnostics": 100
}
```

Representative fields after successful semantic analysis with a utility-resolution error follow.

```json
{
  "status": "completed",
  "requestedLevel": "semantic",
  "completedLevel": "semantic",
  "scope": "saved_document_diagnostics",
  "isComplete": true,
  "hasErrors": true,
  "truncated": false,
  "counts": {
    "total": 1,
    "errors": 1,
    "warnings": 0,
    "information": 0,
    "hidden": 0
  },
  "diagnostics": [
    {
      "code": "AKBURA_SEMANTIC_TailwindUtilityNotFound",
      "severity": "error",
      "kind": "semantic",
      "sourceOwnership": "document"
    }
  ]
}
```

`status` describes completion of the requested analysis. Language errors appear in `diagnostics` as ordinary results. `syntax` runs the parser and reports `completedLevel: syntax`. If essential project references are missing, a `semantic` request can return syntax diagnostics with `status: incomplete`. `incompleteReasons` and `projectDiagnostics` explain the limits. Invalid input and unavailable sessions are tool errors. `counts` and `hasErrors` describe the entire collected set even when the returned array is truncated.

Diagnostics use the Workspace's canonical records and preserve path, provenance, related locations, and the logical ID. `sourceOwnership` identifies the requested document, a foreign document, a referenced module, or a locationless issue. Use the record's own path and `originalRange`. Lines, characters, and absolute `originalSpan` offsets are zero-based UTF-16, with an exclusive end. If an original Akbura location is unavailable, the result carries the known path and a mapping limitation. Locationless records have a null `location`.

`snapshot` identifies the captured solution, project, and document versions. The scope is saved-document diagnostics, including related canonical issues emitted by that analysis. Project builds, application execution, previews, and runtime layout have separate verification paths.

## Working with saved sources

The server synchronizes changes to registered `.akbura`, `.akcss`, and C# sources before capturing a request snapshot. If a project file, monitored build import, resolved reference, or project input membership changes, the server reports `reload_required`. Prepare the project again as needed and reconnect.

1. Call `akbura_project_info` to learn the project context and available recipe IDs.
2. Use `akbura_find_utilities` to verify names, target types, and imports.
3. Retrieve a relevant recipe when its preconditions fit the task.
4. Adapt the reference component with your usual editing tools and save the file.
5. Call `akbura_analyze_document`, address the diagnostics, and analyze the new snapshot.

Use breakpoint variants supported by the project and account for the window width that drives them. Check visual appearance with a separate runtime test. These instructions guide clients and agents. Each client decides when to call the tools.

The host supports one selected `.csproj` per process and local stdio transport.

## Run the tests

```sh
dotnet test src/Workspaces/Akbura.Mcp.IntegrationTests/Akbura.Mcp.IntegrationTests.csproj
dotnet test src/Akbura.UnitTests/Akbura.UnitTests.csproj --filter FullyQualifiedName~LayoutRecipe_
```
