# Template parity and upstream updates

The Akbura application templates are adapted from the C# application templates
in [Avalonia.Templates](https://github.com/AvaloniaUI/Avalonia.Templates/tree/2b1aac216f3a8a6404e98670fa9c9e24e70bcecc/templates/csharp).
The upstream baseline for this implementation is
`2b1aac216f3a8a6404e98670fa9c9e24e70bcecc`; the corresponding Akbura
baseline is `8d45d90be636bd0c11ec4fedb64db750637f3ac4`.
Keep the attribution and license notices of copied sources and resources.

| Akbura template | Upstream source | Shared user choice | Deliberate difference |
| --- | --- | --- | --- |
| `akbura.mvvm` | `templates/csharp/app-mvvm` | MVVM toolkit, ViewLocator, Avalonia version, restore opt-out | Akbura `.akbura` UI, .NET 10/C# only, Akbura version, DI, optional CPM. |
| `akbura.xplat` | `templates/csharp/xplat` | MVVM toolkit, ViewLocator, Avalonia version, CPM, five page types | Akbura `.akbura` UI in thin page shells, .NET 10/C# only, Akbura version and DI; no whole-solution restore. |

`akbura.app` is independent and intentionally remains a local-state/hooks
demonstration; it is not replaced by MVVM. The component item templates are
likewise independent. Do not change `templates/app/Views/MainView.akbura` while
updating MVVM examples.

## Dependency baseline

| Package family | Version at the baseline | Rule |
| --- | --- | --- |
| Avalonia packages | `12.0.4` | Use one compatible `AvaloniaVersion`; do not take a newer upstream value without validating Akbura. |
| Akbura and Akbura.Diagnostics | `12.0.4-alpha.8` | Use one `AkburaVersion`, stamped from the local CI package or release tag. |
| CommunityToolkit.Mvvm | `8.4.2` | Only CommunityToolkit variants. |
| ReactiveUI.Avalonia | `12.0.3` | Only ReactiveUI variants. |
| Microsoft.Extensions.DependencyInjection | `10.0.0` | Only Microsoft DI variants. |
| Splat | `19.4.1` | Direct reference only for `Splat.Locator`; ReactiveUI may bring it transitively. |
| AvaloniaUI.DiagnosticsSupport | `2.2.3` | Supported Debug diagnostics paths only. |
| Xamarin.AndroidX.Core.SplashScreen | `1.2.0.2` | Android, identically in CPM and non-CPM output. |

The two new templates support `net10.0` and C# only. F#, net8.0, and net9.0
are not offered because the current Akbura runtime and compiler template path
do not support those combinations. `--di` and `--mvvm` are independent: selecting
ReactiveUI does not imply Splat.Locator registrations, and selecting a container
does not select a toolkit. The generated UI uses native `${Binding ...}` with a
runtime DataContext and a compile-time `x.DataType`; `.axaml` page shells use
normal Avalonia XAML syntax. AkburaControl remains a Control, so an Avalonia
Page navigation host must receive an actual thin Page shell.

## Known drift points

- Compare upstream toolkit packages and API signatures before carrying a new
  version across. In particular, ReactiveUI.Avalonia `12.0.3` requires Splat
  `>= 19.4.1`; do not reuse the older direct Splat version from `akbura.app`.
- Upstream's Android SplashScreen version differs between CPM and non-CPM
  branches. Keep one tested value and compare normalized restored package graphs.
- Recheck Avalonia's desktop, activity (`MainViewFactory`), and single-view
  lifetime APIs. The activity factory must return a fresh view tree while the
  example ViewModel remains application-scoped.
- In xplat ReactiveUI variants, call `UseReactiveUI` before `UseAkbura` resolves
  `AppServices.Current`: constructing `MainViewModel` first fails on Browser.
- Recheck all five `MainViewPageType` choices. No choice may silently fall back
  to an ordinary Control or leave unused page implementations in the output.
- Keep all application-template version defaults in sync. Both template CI and
  NuGet release invoke `eng/Set-AkburaTemplateVersions.ps1`, which stamps
  `app`, `app-mvvm`, and `xplat` and checks their values inside the packed nupkg.
  Item templates have no version symbols and must not be touched by stamping.

## Updating from upstream

1. Record the new Avalonia.Templates commit SHA. Compare the complete
   `templates/csharp/app-mvvm/**` and `templates/csharp/xplat/**` trees, especially
   `.template.config`, package versions, all host entry points, and page options.
2. Carry only compatible behavior into the two new Akbura templates. Preserve
   Akbura UI, DI/version options, attribution, and the state-based `akbura.app`.
   Update this parity table and the dependency baseline when choices change.
3. Check `dotnet new ... --help`, generation of all 24 MVVM and 120 xplat option
   combinations, and selected/unselected files. Check `-n` versus `-o`, dotted
   and hyphenated names, and invalid choice rejection.
4. Pack `Akbura.Templates` and install the nupkg in an isolated template home.
   Inspect archived `.template.config` host files and stamped defaults. Run
   `eng/Verify-AkburaTemplates.ps1` against locally packed runtime and
   diagnostics packages, including desktop Debug/Release builds, binding tests,
   and CPM/non-CPM restored-graph comparisons.
5. Build/publish platform hosts only where the corresponding .NET workload,
   SDK, simulator, or device exists. Record unavailable environments separately
   from a failed test; no skipped platform is a passed platform.

## Verification modes

The verification scripts share one canonical catalog of 144 valid cases: 24
`akbura.mvvm` variants and 120 `akbura.xplat` variants. A case is identified
by its template kind, toolkit, dependency-injection mode, CPM flag,
`RemoveViewLocator` flag, and page type. The selected cases and exact
Debug/Release executions are written to
`template-verification-plan.json` before any project is generated.

`Sampled` is the automatic push, pull-request, and NuGet pre-publish mode. It
uses a bounded, stratified CSPRNG selection from the complete catalog:

- six MVVM cases, one for every toolkit and DI pair, covering both boolean flags;
- fourteen xplat cases, seven per toolkit, covering all page types and DI modes;
- Debug for all 20 cases and four Release repeats, one per kind and toolkit.

This gives 20 structural generations and 24 main builds. Package checks, legacy
templates, CPM graph comparisons, aliases, invalid choices, the negative typed
binding test, and the limited runtime/startup probes remain separate checks and
are not included in that count. Sampled is a best-effort regression safety net,
not proof that every supported combination works.

`Smoke` preserves the earlier deterministic contract: all 144 cases receive
structural generation checks and the explicit smoke list performs 20 Debug plus
four Release main builds. Use it when a stable local diagnostic set is more
useful than sampling.

`Full` generates all 144 cases and builds every case in both Debug and Release:
288 main builds. It also runs the extended runtime/startup checks. Full uses the
xplat Desktop entry point; Browser, Android, and iOS are verified separately by
`eng/Verify-AkburaXplatPlatform.ps1` in environments that provide their SDKs
and workloads.

### Release responsibility

Before creating a release tag, the release maintainer must pack the future
release versions into an isolated local feed and run Full against the exact
source revision and packages:

```powershell
./eng/Verify-AkburaTemplates.ps1 `
    -Version $akburaVersion `
    -AvaloniaVersion $avaloniaVersion `
    -Feed $localPackageFeed `
    -WorkingDirectory $verificationDirectory `
    -Mode Full `
    -PlanOutputPath "$verificationDirectory/template-verification-plan.json" `
    -ReportOutputPath "$verificationDirectory/template-verification-report.json" `
    -BinLogDirectory $binLogDirectory
```

The maintainer must also run the platform verifier for both toolkits in suitable
Browser, Android, and iOS environments. A missing workload, SDK, Xcode host,
simulator, or device is unverified, not passed. This is an organizational release
requirement; the GitHub environment approval does not technically prove that the
local Full and platform runs happened.

A saved plan is replayed without invoking the random selector:

```powershell
./eng/Verify-AkburaTemplates.ps1 `
    -Version $akburaVersion `
    -AvaloniaVersion $avaloniaVersion `
    -Feed $localPackageFeed `
    -WorkingDirectory $verificationDirectory `
    -Mode Sampled `
    -SelectionManifest "$verificationDirectory/template-verification-plan.json" `
    -ReportOutputPath "$verificationDirectory/template-verification-replay.json" `
    -BinLogDirectory $binLogDirectory
```

Replay validates the schema, catalog hash, source inputs, package hashes, case
IDs, and duplicate executions. It never replaces a failed case with a new
sample. The JSON report records planned, passed, failed, and not-run work plus
separate structural-generation, restore, main-build, graph-comparison,
additional-check, and runtime-probe timings. Pack and platform setup/verification
remain distinct workflow steps with their own GitHub Actions durations.

The template workflow exposes `Sampled`, `Smoke`, and `Full` through
`workflow_dispatch`. In Sampled and Smoke, platform jobs keep one established
representative per platform; Full additionally verifies the complementary
toolkit. Successful generated directories and successful binlogs are removed.
A failed restore, build, or test keeps its generated directory and binary log
for diagnosis.
