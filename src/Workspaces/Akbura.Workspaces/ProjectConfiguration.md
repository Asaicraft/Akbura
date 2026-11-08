# Project configuration

`AkburaProject` is a workspace-bound facade over the current immutable `ProjectContext`.
It exposes `Workspace`, `Id`, `Context`, `ReferenceResolver`, and these operations:

```csharp
project.AddAssembly(assembly);
project.AddAssemblyOf<MyControl>();
project.AddAssemblyImage(completeManagedPeImage);
project.AddReference(metadataReference); // Advanced Roslyn integration; aliases are preserved.
project.AddSource("Models/ViewModel.cs", source);
project.UpdateSource("Models/ViewModel.cs", changedSource);
var removed = project.RemoveSource("Models/ViewModel.cs");
project.Update(update => { /* One atomic configuration batch. */ });
```

`workspace.DefaultProject` addresses the existing default project.
`workspace.GetProject(projectId)` addresses an existing project. The facade retains its
workspace/project identity across updates and reads the latest published context. Removing
the project invalidates its facade, even if a project with the same ID is subsequently added.
Using a facade after workspace disposal throws `ObjectDisposedException`.

## Desktop example

The `UseAvalonia` extension and facade-based `Bind` overload live in `Akbura.AvaloniaEdit`.
The generic Workspaces project has no new Avalonia UI dependency.

```csharp
using Akbura.AvaloniaEdit;
using Akbura.Workspaces;

using var workspace = new AkburaWorkspace();
var project = workspace.DefaultProject;

// Potentially expensive synchronous preparation: use the host's background scheduling
// policy on desktop. It must finish before binding the editor on the UI thread.
await Task.Run(() =>
{
    project.UseAvalonia();
    project.AddSource("Models/ViewModel.cs", """
        namespace Demo;

        public sealed class ViewModel
        {
            public Profile Profile
            {
                get; set;
            } = new();
        }

        public sealed class Profile
        {
            public string Name
            {
                get; set;
            } = "";
        }
        """);
});

var editor = new AkburaCodeEditor
{
    Text = """
        using Avalonia.Controls;
        using Demo;

        param ViewModel Vm;
        <TextBlock Text=${x.bind Vm.Profile.Name} />
        """,
};
using var session = editor.Bind(project, new Uri("memory:///Main.akbura"));
await session.Ready;
```

On desktop, no-argument `UseAvalonia()` uses the configured project resolver if present.
Otherwise it prepares a catalog from the .NET host's `TRUSTED_PLATFORM_ASSEMBLIES` list
and explicit locations of the Avalonia controls/base/XAML and Akbura assemblies. It does
not enumerate loaded assemblies or search SDK/package directories. Only the preset's
framework roots and their declared dependency closure are added to the compilation.
The preset includes `System.Runtime`, collections, LINQ, the object model and component
model primitives, plus actual Avalonia and Akbura metadata.

`AddAssemblyOf<T>()` references exactly `typeof(T).Assembly` and imposes no class or
constructor constraint. Adding an assembly never instantiates its types. When no resolver
is configured, `AddAssembly` can read that explicitly supplied assembly's file location;
its dependencies must already be staged or available through a resolver. An unavailable
location is a setup error, not a reason to synthesize framework declarations.

## Prepared catalogs and browser hosts

Use one coherent set of compiler assets resolved for the host's target. Build a catalog
from complete managed PE images without constructing Roslyn objects:

```csharp
using Akbura.AvaloniaEdit;
using Akbura.AvaloniaEdit.Infrastructure;
using Akbura.Workspaces;
using Akbura.Workspaces.Projects;
using System.Reflection;

var resourceAssembly = Assembly.GetExecutingAssembly();
var images = new List<byte[]>();
foreach (var name in resourceAssembly.GetManifestResourceNames()
    .Where(name => name.StartsWith("SmokeReferences.", StringComparison.Ordinal)))
{
    using var stream = resourceAssembly.GetManifestResourceStream(name)!;
    using var image = new MemoryStream();
    await stream.CopyToAsync(image);
    images.Add(image.ToArray());
}

var references = AkburaReferenceSet.FromImages(images);
using var workspace = new AkburaWorkspace();
workspace.DefaultProject.UseAvalonia(references);
workspace.DefaultProject.AddSource("ViewModel.cs", "namespace Demo; public class ViewModel {}");
var editor = new AkburaCodeEditor { Text = "using Demo; param ViewModel Vm;" };
using var session = editor.Bind(workspace.DefaultProject, new Uri("memory:///Main.akbura"),
    new() { Scheduler = new AkburaInlineScheduler() });
await session.Ready;
```

The BrowserSmoke project embeds the compiler's `ReferencePathWithRefAssemblies` items
after `FindReferenceAssembliesForReferences`. This obtains framework reference assemblies
and the selected package/project assets from MSBuild, without machine-specific SDK paths
or manually copied binaries. Preparation and metadata binding still cost CPU on a browser
without worker threads; this API does not pretend that `Task.Run` provides a worker there.

`AkburaReferenceSet.FromFiles(explicitPaths)` is the filesystem equivalent of `FromImages`.
Both validate assembly identities and metadata fingerprints. Alternatively, implement
`IAkburaReferenceResolver.Resolve(AssemblyIdentity)` over a host-owned catalog and assign
it to `project.ReferenceResolver` or supply it to `UseAvalonia(resolver)`. Presets can request
an unsigned, version-zero identity as a name-only lookup; actual assembly/dependency
requests carry full identities and are checked by the workspace.

No-argument `UseAvalonia()` works in a browser only if a suitable resolver is already
configured. Otherwise it explains how to supply a prepared catalog. These synchronous
APIs never perform hidden network downloads or arbitrary `Assembly.Load` calls.

## Reference validation and ownership

Dependency resolution uses the complete staged reference set, then the configured resolver.
Missing dependencies identify both the requested assembly and its requesting assembly.
Non-framework dependencies require an exact identity. Signed .NET framework facades can
bind to the same or a newer version in the host's coherent framework set; they must retain
the same name, culture and public key token. Explicitly adding two versions of one assembly,
or different metadata images with the same identity, is rejected. Reference and implementation
images are not interchangeable just because their filenames or identities match.

Equivalent images and reference properties are idempotent. Different aliases or embedding
properties remain separate Roslyn references. Self-references to the current compilation's
assembly name are rejected. Standalone modules and multi-module assemblies are not supported.

Image inputs are copied immediately. The original `byte[]` can be modified afterward.
Workspace-created image references use Roslyn's GC-managed metadata lifetime: pinned bytes
are released when the last reference/catalog/snapshot becomes unreachable. Workspace disposal
does not prematurely dispose metadata still used by snapshots or active requests. References
passed to `AddReference` remain caller-owned; callers must keep externally owned metadata
valid while their snapshots are in use.

Adding a reference configures compiler and language-service visibility. It does not install
that library into a future preview runtime. Compiler-only reference assemblies cannot be
used as executable implementations.

## Sources, batching and automatic refresh

Source identifiers are case-sensitive virtual paths, independent of the OS. Both slash
styles are accepted; repeated separators and `.` segments are normalized, and `..` segments
are resolved without permitting escape above the virtual project root. Absolute paths,
drive/URI prefixes and non-`.cs` paths are rejected. No source file needs to exist on disk.
Akbura and AKCSS documents continue to use the existing workspace document APIs.

`AddSource` rejects a duplicate; `UpdateSource` rejects a missing source. `RemoveSource`
returns whether it removed a tree. Identical source updates do not publish another solution.
Invalid C# remains in the compilation for diagnostics and recovery. Updates replace only
the affected tree, preserving its parse options. New trees use the first existing C# tree's
options; a tree-less project deterministically uses C# 14, regular source, and no preprocessor
symbols. Compilation options, project identity/settings/references, diagnostics configuration
and existing Akbura/AKCSS text are retained.

```csharp
project.Update(update =>
{
    update.AddAssemblyOf<MyControl>();
    update.AddSource("Models/Person.cs", personSource);
    update.AddSource("ViewModel.cs", viewModelSource);
});
```

`Update` stages a synchronous callback and commits once after validation. Dependency order
within the callback does not matter. Callback or validation failure publishes none of the
staged operations. Nested/reentrant configuration batches in the same workspace are rejected;
the update object cannot be used after its callback or from another thread. Operations apply
to the latest project under the workspace mutation gate, retaining concurrent editor text
and unrelated project changes. There is no commit-on-`Dispose` transaction pattern.

The ordinary single-operation methods have the same synchronous publication guarantee.
They do not marshal work onto the UI thread; hosts choose their scheduling policy.
For example, update `Models/ViewModel.cs` to rename `Profile.Name` to `Profile.DisplayName`.
Existing editor sessions refresh through the normal workspace notification, without rebinding
or calling `RefreshAsync`. Request completion again at `Vm.Profile.` to see `DisplayName`.
Old completion edits and analysis results remain protected by their project-context identity;
project refreshes retain the provisional highlighting presentation until new analysis arrives.

## Verified platform coverage

Release workspace tests cover configuration, ownership, dependency validation, concurrent
updates and snapshot lifetime. Avalonia headless integration tests cover the desktop preset,
prepared compile-time assets, visible completion and documentation, live source and library
updates, stale requests and preservation of highlighting.

The published Release BrowserSmoke runs against the embedded real compile-time assets. It
checks an inherited Avalonia member, native/projected completion, commit, hover, undo and
the automatic `Name` to `DisplayName` source update without rebinding. Its startup runs after
the view is loaded so popup positioning has a visual root. After verifying the rename, it
restores the original `Profile.Name` model for interactive editing. Automatic completion
and the default `Ctrl+Space` gesture remain enabled.

BrowserSmoke also enables native linking during the build portion of publishing and Visual
Studio's `DeployOnBuild`. The SDK otherwise skips that step and leaves the development
static-web-assets manifest pointing at the stock runtime without SkiaSharp/HarfBuzz. The
project sets `WasmBuildOnlyAfterPublish=false` so both the development server and published
site use a linked runtime.

Known browser limitation: Roslyn's background persistent-storage configuration attempts
`Process.GetCurrentProcess()`, which is unsupported on WebAssembly, and reports a background
`PlatformNotSupportedException` in the console. The smoke scenario above still passes, but
disk-backed symbol/import caches are not verified as functional in the browser. This API
does not replace or patch Roslyn's internal persistence implementation. Native desktop UI,
mobile hosts, trimmed/AOT publishing and preview execution are not covered by this smoke.
