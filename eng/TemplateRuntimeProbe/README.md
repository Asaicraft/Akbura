# Generated-template runtime probe

This is a headless integration test for an application generated from the
**packed** `akbura.mvvm` or `akbura.xplat` template. Generate the application
with the fixed name `TemplateRuntimeProbeApp`, then reference its MVVM project
(the shared project for xplat):

```powershell
dotnet test eng/TemplateRuntimeProbe/TemplateRuntimeProbe.csproj `
    -p:TemplateProject=C:/absolute/path/TemplateRuntimeProbeApp.csproj `
    -p:TemplateKind=mvvm `
    -p:TemplateToolkit=CommunityToolkit `
    -p:TemplateDependencyInjection=None
```

The probe instantiates the generated `MainView` and `MainViewModel` under
Avalonia.Headless. It tests visible counter text, Reset enablement, two-way
TextBox binding, updates from VM commands, the bound `GreetingCard`, DataContext
replacement, and view reattachment. It does not claim to exercise mobile host
launch, animated navigation, or a real browser/device. For an xplat shared project,
pass `-p:TemplateKind=xplat -p:TemplatePageType=None` (or the selected
`ContentPage`, `TabbedPage`, `DrawerPage`, or `NavigationPage` value). This also
tests the chosen page shell and that the shared `MainViewHost` factory returns
distinct visual trees with the same ViewModel DataContext. It does not invoke
the Android `IActivityApplicationLifetime.MainViewFactory` delegate.
The NavigationPage assertion disables visual animation under Headless while
checking real PushAsync/PopAsync, page-stack, and Back behavior. Pass
`-p:TemplateToolkit=ReactiveUI` for a ReactiveUI-generated project so the
headless AppBuilder initializes ReactiveUI the same way as the generated host.
For representative DI startup coverage, pass
`-p:TemplateDependencyInjection=Microsoft.Extensions.DependencyInjection` or
`-p:TemplateDependencyInjection=Splat.Locator`, together with
`-p:TemplateStartupProbe=true -c Release`. This also supports
`TemplateDependencyInjection=None`: each case initializes the generated `App`
and verifies the composition-root ViewModel and bound view; DI cases also
resolve the greeting service and ViewModel through their service provider.

The generated project must be restored against the same local Akbura package
feed as the template verifier. The test project intentionally has no reference
to template source files: it compiles against the generated project itself.
