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
| Akbura and Akbura.Diagnostics | `12.0.4-alpha.6` | Use one `AkburaVersion`, stamped from the local CI package or release tag. |
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
