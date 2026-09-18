# Testing documentation examples

The documentation is the input to these tests. Do not copy a page's markup into
another string constant and then test only that copy.

## Current coverage

The explicit catalog in `src/Akbura.TestUtilities/Documentation/DocumentationExampleCatalog.cs`
registers 29 `akbura` code fences:

| Documentation page | Registered blocks | Contract |
| --- | ---: | --- |
| `src/AkburaDocs/_pages/akbura/foreach.md` | 10 | Nine valid examples; one intentionally invalid `Border` destination |
| `src/AkburaDocs/_pages/akbura/conditional-markup.md` | 5 | All five examples, with explicit hosts/models for fragments |
| `src/AkburaDocs/_pages/akbura/grid.md` | 10 | All ten examples; native lengths and constraints are checked |
| `src/AkburaDocs/_pages/index.md` | 4 | First component, Components and Markup, Dictionary Resources, Avalonia Styles |

All `akbura` fences on the first three pages are covered by an inventory assertion.
The main page is deliberately a selected set, not a claim of complete coverage.
Other pages, inline snippets in prose, AXAML samples, shell commands, and AKCSS
samples are not automatically covered by this catalog.

## How the source reaches the tests

Both test projects import `DocumentationExamples.props`. It links the shared
reader/catalog and embeds the actual Markdown files from `AkburaDocs`, using
explicit logical resource names. No documentation snapshot is maintained in the
test directory. Reading resources does not depend on the working directory,
network access, a Git checkout layout at runtime, or the website server.

The Markdown reader recognizes the ATX headings and backtick/tilde fences used
by these pages. It selects a block by relative path, nearest heading, language,
and zero-based ordinal within that heading/language. It retains the code's
characters and line endings and reports the first original Markdown code line.
It is not intended to replace a general Markdown parser for arbitrary extensions.

Missing files, missing sections, duplicate identities, and unclosed fences are
failures, not skips. Adding or removing a fence on a fully covered page requires
an explicit catalog update. Use a normal build when documentation changes:
`--no-build` deliberately reuses the previously embedded Markdown resources.

## Complete components, fragments, and negative examples

The catalog distinguishes these cases explicitly:

* A complete component is compiled unchanged.
* A fragment receives only its declared test context: imports, omitted values,
  a native host for a property element, and/or a companion C# model. `Prefix`,
  `Suffix`, `CompanionCSharp`, and `Context` make this visible. The original code
  remains an unchanged substring of the compilation input.
* A documented invalid example has a specific expected diagnostic. The `Border`
  example must report `AKBURA_SEMANTIC_UnsupportedForeachContentDestination` at
  the loop. An unrelated syntax error or missing import is not a passing result.

`Person` on the conditional page has no complete declaration. Its small C# type,
example values, and surrounding ItemsControl/ContentControl are explicitly test
fixtures, not additional documentation claims. Tests must not change the
property element into an easier, different example.

A generated partial class supplies an isolated `AkburaEngine.Empty` constructor
and access to the generated state descriptors for assertions. It does not
replace documented `state` declarations with ordinary properties or introduce
an alternative implementation of rendering.

## Compiler and runtime checks

Every positive example is parsed and bound with the real Akbura compiler,
generated using both `ReleaseDirect` and `DebugStructural`, and emitted by
Roslyn. The ordinary generation entry point selects its supported conditional
path as needed. The test rejects Akbura syntax/semantic diagnostics and C# errors;
C# warnings are not globally promoted to failures.

Each positive example then runs as a real initialized Akbura component in an
isolated Avalonia headless application and native Window. A Fluent theme is
installed for native templated controls. Assertions inspect the native controls,
text, Grid definitions, resource entries, or styles, rather than only checking
that an assembly was emitted.

Interaction tests raise the documented buttons' `Click` events or mutate the
actual generated state/source. They process the dispatcher and layout, but do
not manually call generated `FirstUpdate()`/`Update()` or `InvalidState()` to
make a missing notification appear to work. Observable mutations must schedule
the owner update that also refreshes neighboring conditions.

The suite checks loop output and source indexes, multi-root identity, `x.id`,
Move/Replace/Reset, equal-valued occurrences, source replacement, an immutable
Add action, the suffix reached after removing a breaking item, conditional
unmounting, native template branch selection, Counter state, moving resource
keys, and an applied style change. Compatible instance checks use `Assert.Same`.
Expected Grid lengths are written independently of Akbura's literal parser.

Running a generated DebugStructural component is not an actual .NET Hot Reload
session. These tests do not claim to exercise Edit and Continue, VS deployment,
.NET Framework type loading, or every behavior discussed in a page's prose.
The normal compiler/runtime/editor regression suites remain necessary.

## Editor checks

`DocumentationWorkspaceExamplesTests` uses the same embedded source and explicit
contexts. It checks syntax round-tripping, valid classification spans, exact
Directive/Keyword spans for `$foreach` and conditional directives, and full versus
incremental classification during linewise LF/CRLF edits of two loop examples.
This is a shared Workspaces check, not a visual assertion about a particular
Visual Studio theme and not a complete semantic editor integration test.

## Adding or changing a sample

1. Add the Markdown resource to `DocumentationExamples.props` for a new page.
2. Register its path, heading, ordinal, and stable test ID in the catalog. Add
   only the context explicitly missing from the snippet. Declare a negative
   expectation only when the documentation itself marks the example invalid.
3. Register an initial native-output assertion. The assertion dispatcher fails
   for an unknown positive ID instead of falling back to a smoke-only success.
4. Add an interaction test for reactive behavior. Check values and instance
   identity, not only absence of exceptions. Compare observable notifications
   through the real owner lifecycle rather than forcing an Update call.
5. Add the page to `FullyCoveredPages` only after every `akbura` fence has a
   contract. Keep all other coverage descriptions accurate.

The current compiler helper supplies no external AKCSS module map. Extend the
helper and fixture contract before registering a sample that requires generated
AKCSS modules, multiple Akbura components, custom services, or a different build
pipeline. Do not silently remove that part of the sample.

## Running the tests

From the repository root:

```powershell
$unit = ".\src\Akbura.UnitTests\Akbura.UnitTests.csproj"
$workspace = ".\src\Workspaces\Akbura.Workspaces.UnitTests\Akbura.Workspaces.UnitTests.csproj"

dotnet test $unit -c Debug --filter "FullyQualifiedName~Documentation"
dotnet test $workspace -c Debug --filter "FullyQualifiedName~Documentation"
```

The main theory generates both component modes even during this Debug test run.
Run the complete projects after a compiler or shared editor change as well:

```powershell
dotnet test $unit -c Debug
dotnet test $workspace -c Debug
```

A new failing documentation test can expose an existing documentation/compiler
mismatch. Report which stage failed and retain the original sample and its
contract; fix the documentation or implementation explicitly, rather than
weakening the expectation or skipping the example.
