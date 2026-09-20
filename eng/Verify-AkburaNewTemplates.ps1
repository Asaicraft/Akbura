param(
    [Parameter(Mandatory)] [string] $Version,
    [Parameter(Mandatory)] [string] $Hive,
    [Parameter(Mandatory)] [string] $Projects,
    [Parameter(Mandatory)] [string] $NuGetConfig,
    [Parameter(Mandatory)] [string] $Packages
)

$ErrorActionPreference = "Stop"

function Invoke-DotNet {
    & dotnet @args
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet failed ($LASTEXITCODE): $($args -join ' ')"
    }
}

function Assert-Condition {
    param([bool] $Condition, [string] $Message)
    if (!$Condition) { throw $Message }
}

$templateRoot = Join-Path $PSScriptRoot "../src/Akbura.Templates/templates"
$sharedSources = @(
    @{ Desktop = "Views/MainView.akbura"; Xplat = "AkburaXplatTemplate/Views/MainView.akbura" },
    @{ Desktop = "Components/GreetingCard.akbura"; Xplat = "AkburaXplatTemplate/Components/GreetingCard.akbura" },
    @{ Desktop = "Services/IGreetingService.cs"; Xplat = "AkburaXplatTemplate/Services/IGreetingService.cs" },
    @{ Desktop = "Services/GreetingService.cs"; Xplat = "AkburaXplatTemplate/Services/GreetingService.cs" },
    @{ Desktop = "Variants/CommunityToolkit/ViewModels/ViewModelBase.cs"; Xplat = "Variants/CommunityToolkit/ViewModelBase.cs" },
    @{ Desktop = "Variants/CommunityToolkit/ViewModels/MainViewModel.cs"; Xplat = "Variants/CommunityToolkit/MainViewModel.cs" },
    @{ Desktop = "Variants/ReactiveUI/ViewModels/ViewModelBase.cs"; Xplat = "Variants/ReactiveUI/ViewModelBase.cs" },
    @{ Desktop = "Variants/ReactiveUI/ViewModels/MainViewModel.cs"; Xplat = "Variants/ReactiveUI/MainViewModel.cs" }
)
foreach ($pair in $sharedSources) {
    $desktopPath = Join-Path $templateRoot "app-mvvm/$($pair.Desktop)"
    $xplatPath = Join-Path $templateRoot "xplat/$($pair.Xplat)"
    $desktopSource = [IO.File]::ReadAllText([IO.Path]::GetFullPath($desktopPath))
    $xplatSource = [IO.File]::ReadAllText([IO.Path]::GetFullPath($xplatPath))
    Assert-Condition ($desktopSource.Equals($xplatSource, [StringComparison]::Ordinal)) (
        "MVVM source parity changed: $($pair.Desktop) differs from $($pair.Xplat).")
}

function Get-PackageReferences {
    param([string] $ProjectDirectory)
    foreach ($file in Get-ChildItem -LiteralPath $ProjectDirectory -Recurse -Filter *.csproj) {
        $project = [xml] (Get-Content -LiteralPath $file.FullName -Raw)
        foreach ($reference in $project.SelectNodes("//*[local-name()='PackageReference']")) {
            [pscustomobject] @{
                Project = $file.FullName
                Name = $reference.GetAttribute("Include")
                Version = $reference.GetAttribute("Version")
                VersionOverride = $reference.GetAttribute("VersionOverride")
            }
        }
    }
}

function Assert-GeneratedTemplate {
    param(
        [string] $Kind,
        [string] $Name,
        [string] $Directory,
        [string] $Toolkit,
        [string] $DependencyInjection,
        [bool] $Cpm,
        [bool] $RemoveViewLocator,
        [string] $PageType
    )

    $projects = @(Get-ChildItem -LiteralPath $Directory -Recurse -Filter *.csproj)
    $expectedProjects = if ($Kind -eq "mvvm") { 1 } else { 5 }
    Assert-Condition ($projects.Count -eq $expectedProjects) (
        "$Name generated $($projects.Count) projects, expected $expectedProjects.")

    $mainView = @(Get-ChildItem -LiteralPath $Directory -Recurse -Filter MainView.akbura)
    $mainViewModel = @(Get-ChildItem -LiteralPath $Directory -Recurse -Filter MainViewModel.cs)
    Assert-Condition ($mainView.Count -eq 1 -and $mainViewModel.Count -eq 1) (
        "$Name must contain one Akbura MainView and one MainViewModel.")
    $markup = Get-Content -LiteralPath $mainView[0].FullName -Raw
    foreach ($binding in @("CountText", "IncrementCommand", "ResetCounterCommand", "UserName", "Greeting", "UseExampleNameCommand")) {
        Assert-Condition ($markup.Contains("Binding $binding", [StringComparison]::Ordinal)) (
            "$Name does not bind $binding in MainView.akbura.")
    }
    Assert-Condition (!$markup.Contains("state count", [StringComparison]::Ordinal)) (
        "$Name contains the old state-based counter.")
    Assert-Condition ($markup.Contains('x.DataType=', [StringComparison]::Ordinal)) (
        "$Name does not declare a typed binding context.")

    $locator = @(Get-ChildItem -LiteralPath $Directory -Recurse -Filter ViewLocator.cs)
    Assert-Condition (($locator.Count -eq 0) -eq $RemoveViewLocator) (
        "$Name generated an incorrect ViewLocator selection.")
    $apps = @(Get-ChildItem -LiteralPath $Directory -Recurse -Filter App.axaml)
    Assert-Condition ($apps.Count -eq 1) (
        "$Name must contain exactly one App.axaml.")
    $appMarkup = Get-Content -LiteralPath $apps[0].FullName -Raw
    Assert-Condition ($appMarkup.Contains("ViewLocator", [StringComparison]::Ordinal) -eq
        !$RemoveViewLocator) (
        "$Name has an incorrect ViewLocator registration in App.axaml.")
    Assert-Condition (!(Test-Path -LiteralPath (Join-Path $Directory "Variants"))) (
        "$Name leaked its Variants directory.")
    Assert-Condition (!(Test-Path -LiteralPath (Join-Path $Directory ".template.config"))) (
        "$Name leaked template configuration.")

    $central = Join-Path $Directory "Directory.Packages.props"
    $buildProps = Join-Path $Directory "Directory.Build.props"
    Assert-Condition ((Test-Path -LiteralPath $central) -eq $Cpm) (
        "$Name generated an incorrect CPM file selection.")
    if (!$Cpm) {
        Assert-Condition (Test-Path -LiteralPath $buildProps) (
            "$Name omitted non-CPM version properties.")
    }
    if ($Cpm) {
        $centralXml = [xml] (Get-Content -LiteralPath $central -Raw)
        Assert-Condition ($centralXml.OuterXml.Contains("ManagePackageVersionsCentrally")) (
            "$Name did not enable CPM.")
        $centralNames = @($centralXml.SelectNodes(
            "//*[local-name()='PackageVersion']") |
            ForEach-Object { $_.GetAttribute("Include") })
        Assert-Condition (($centralNames -contains "CommunityToolkit.Mvvm") -eq
            ($Toolkit -eq "CommunityToolkit")) (
            "$Name included the wrong toolkit in Directory.Packages.props.")
        Assert-Condition (($centralNames -contains "ReactiveUI.Avalonia") -eq
            ($Toolkit -eq "ReactiveUI")) (
            "$Name included the wrong ReactiveUI central version.")
        Assert-Condition (($centralNames -contains "Microsoft.Extensions.DependencyInjection") -eq
            ($DependencyInjection -eq "Microsoft.Extensions.DependencyInjection")) (
            "$Name included the wrong Microsoft DI central version.")
        Assert-Condition (($centralNames -contains "Splat") -eq
            ($DependencyInjection -eq "Splat.Locator")) (
            "$Name included the wrong Splat central version.")
    }
    else {
        $propsXml = [xml] (Get-Content -LiteralPath $buildProps -Raw)
        Assert-Condition ($propsXml.OuterXml.Contains("ManagePackageVersionsCentrally")) (
            "$Name did not explicitly disable inherited CPM.")
    }

    $references = @(Get-PackageReferences $Directory)
    foreach ($reference in $references) {
        Assert-Condition (![string]::IsNullOrWhiteSpace($reference.Name)) (
            "$Name has an unnamed PackageReference in $($reference.Project).")
        Assert-Condition ([string]::IsNullOrWhiteSpace($reference.VersionOverride)) (
            "$Name uses VersionOverride for $($reference.Name).")
        Assert-Condition ([string]::IsNullOrWhiteSpace($reference.Version) -eq $Cpm) (
            "$Name has an incorrect CPM/non-CPM Version for $($reference.Name).")
    }

    $referenceNames = @($references.Name)
    Assert-Condition ($referenceNames -contains "Akbura") (
        "$Name does not reference Akbura.")
    Assert-Condition (($referenceNames -contains "CommunityToolkit.Mvvm") -eq
        ($Toolkit -eq "CommunityToolkit")) (
        "$Name selected an incorrect MVVM toolkit package.")
    Assert-Condition (($referenceNames -contains "ReactiveUI.Avalonia") -eq
        ($Toolkit -eq "ReactiveUI")) (
        "$Name selected an incorrect ReactiveUI package.")
    Assert-Condition (($referenceNames -contains "Microsoft.Extensions.DependencyInjection") -eq
        ($DependencyInjection -eq "Microsoft.Extensions.DependencyInjection")) (
        "$Name selected an incorrect Microsoft DI package.")
    Assert-Condition (($referenceNames -contains "Splat") -eq
        ($DependencyInjection -eq "Splat.Locator")) (
        "$Name selected an incorrect direct Splat package.")

    if ($Kind -eq "xplat") {
        $builderExtensions = Join-Path $Directory (
            "$Name/Infrastructure/AkburaApplicationBuilderExtensions.cs")
        Assert-Condition (Test-Path -LiteralPath $builderExtensions -PathType Leaf) (
            "$Name is missing its cross-platform AppBuilder extension.")
        $builderSource = Get-Content -LiteralPath $builderExtensions -Raw
        $servicesPath = Join-Path $Directory (
            "$Name/Infrastructure/AppServices.cs")
        $servicesSource = Get-Content -LiteralPath $servicesPath -Raw
        $usesDi = $DependencyInjection -ne "None"
        Assert-Condition ($servicesSource.Contains(
            "IServiceProvider ServiceProvider", [StringComparison]::Ordinal) -eq $usesDi) (
            "$Name generated an incorrect AppServices provider for $DependencyInjection.")
        Assert-Condition ($builderSource.Contains(
            "WithServiceProvider(", [StringComparison]::Ordinal) -eq $usesDi) (
            "$Name generated incorrect UseAkbura DI wiring for $DependencyInjection.")
        if ($Toolkit -eq "ReactiveUI") {
            $reactiveIndex = $builderSource.IndexOf(
                "UseReactiveUI(", [StringComparison]::Ordinal)
            $akburaIndex = $builderSource.IndexOf(
                "UseAkbura(", [StringComparison]::Ordinal)
            Assert-Condition ($reactiveIndex -ge 0 -and $akburaIndex -gt $reactiveIndex) (
                "$Name must initialize ReactiveUI before creating services for UseAkbura.")
        }
        else {
            Assert-Condition (!$builderSource.Contains("UseReactiveUI(", [StringComparison]::Ordinal)) (
                "$Name unexpectedly initializes ReactiveUI for CommunityToolkit.")
        }
        $solution = @(Get-ChildItem -LiteralPath $Directory -Filter *.slnx)
        Assert-Condition ($solution.Count -eq 1) (
            "$Name does not contain exactly one solution file.")
        foreach ($suffix in @("", ".Desktop", ".Browser", ".Android", ".iOS")) {
            Assert-Condition (Test-Path -LiteralPath (Join-Path $Directory (
                "$Name$suffix/$Name$suffix.csproj"))) (
                "$Name is missing the $suffix project.")
        }
        $mainViewHosts = @(Get-ChildItem -LiteralPath $Directory -Recurse -Filter MainViewHost.cs)
        Assert-Condition ($mainViewHosts.Count -eq 1) (
            "$Name must have exactly one selected MainViewHost.cs.")
        $hostContent = Get-Content -LiteralPath $mainViewHosts[0].FullName -Raw
        $pageSources = (@(
            Get-ChildItem -LiteralPath $Directory -Recurse -File |
                Where-Object { $_.Extension -in @(".cs", ".axaml") } |
                ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw }
        ) -join "`n")
        if ($PageType -eq "None") {
            Assert-Condition ($hostContent.Contains("new UserControl", [StringComparison]::Ordinal) -and
                $hostContent.Contains("Content = new MainView", [StringComparison]::Ordinal)) (
                "$Name must wrap MainView in an ordinary UserControl host.")
        }
        else {
            Assert-Condition ($hostContent.Contains($PageType, [StringComparison]::Ordinal) -or
                $pageSources.Contains($PageType, [StringComparison]::Ordinal)) (
                "$Name did not generate its $PageType shell.")
        }
    }

    $forbidden = @("AkburaAppTemplate", "AkburaMvvmTemplate", "AkburaXplatTemplate",
        "AkburaTemplateNamespace", "AkburaRawProjectNamePlaceholder",
        "TemplateTargetFramework", "FrameworkParameter", "VersionTemplateParameter",
        "cnd:noEmit", "msbuild-conditional:noEmit", "ReactiveUIToolkitChosen")
    foreach ($file in Get-ChildItem -LiteralPath $Directory -Recurse -File -Force |
        Where-Object { $_.FullName -notmatch '[\\/](obj|bin)[\\/]' -and
            $_.Extension -in @(".cs", ".csproj", ".axaml", ".akbura", ".akcss",
                ".props", ".json", ".md", ".slnx", ".xml") }) {
        $content = Get-Content -LiteralPath $file.FullName -Raw -ErrorAction SilentlyContinue
        foreach ($marker in $forbidden) {
            Assert-Condition (!$content.Contains($marker, [StringComparison]::Ordinal)) (
                "$Name left template marker $marker in $($file.FullName).")
        }
    }
}

function Get-PackageGraph {
    param([string] $ProjectPath)
    $assetsPath = Join-Path (Split-Path -Parent $ProjectPath) "obj/project.assets.json"
    $assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json
    @($assets.libraries.PSObject.Properties |
        Where-Object { $_.Value.type -eq "package" } |
        ForEach-Object { $_.Name } |
        Sort-Object)
}

$toolkits = @("CommunityToolkit", "ReactiveUI")
$diOptions = @("None", "Microsoft.Extensions.DependencyInjection", "Splat.Locator")
$cpms = @($false, $true)
$locatorOptions = @($false, $true)
$pageTypes = @("None", "ContentPage", "TabbedPage", "DrawerPage", "NavigationPage")
$graphs = @{}
$index = 0

foreach ($kind in @("mvvm", "xplat")) {
    $pages = if ($kind -eq "mvvm") { @("None") } else { $pageTypes }
    foreach ($toolkit in $toolkits) {
        foreach ($di in $diOptions) {
            foreach ($cpm in $cpms) {
                foreach ($removeLocator in $locatorOptions) {
                    foreach ($page in $pages) {
                        $index++
                        $name = "Matrix" + $index.ToString("000")
                        $directory = Join-Path $Projects $name
                        $shortName = "akbura.$kind"
                        $arguments = @(
                            "new", $shortName,
                            "--name", $name,
                            "--output", $directory,
                            "--mvvm", $toolkit,
                            "--di", $di,
                            "--cpm", $cpm.ToString().ToLowerInvariant(),
                            "--remove-view-locator", $removeLocator.ToString().ToLowerInvariant(),
                            "--debug:custom-hive", $Hive)
                        if ($kind -eq "mvvm") {
                            $arguments += "--no-restore"
                        }
                        else {
                            $arguments += @("--main-view-page-type", $page)
                        }
                        Invoke-DotNet @arguments

                        Assert-GeneratedTemplate $kind $name $directory $toolkit $di `
                            $cpm $removeLocator $page

                        $build = ($kind -eq "mvvm") -or ($page -eq "None") -or
                            ($di -eq "None") -or
                            ($page -eq "NavigationPage" -and $removeLocator)
                        if (!$build) { continue }

                        $projectPath = if ($kind -eq "mvvm") {
                            Join-Path $directory "$name.csproj"
                        }
                        else {
                            Join-Path $directory "$name.Desktop/$name.Desktop.csproj"
                        }
                        Assert-Condition (Test-Path -LiteralPath $projectPath) (
                            "$name does not contain build entry point $projectPath.")
                        $configurations = if ($kind -eq "mvvm" -or $page -eq "None") {
                            @("Debug", "Release")
                        }
                        else { @("Debug") }
                        foreach ($configuration in $configurations) {
                            Invoke-DotNet restore $projectPath `
                                "-p:Configuration=$configuration" `
                                --configfile $NuGetConfig `
                                --packages $Packages
                            $graph = @(Get-PackageGraph $projectPath)
                            Assert-Condition ($graph -contains "Akbura/$Version") (
                                "$name $configuration restored the wrong Akbura version.")
                            if ($configuration -eq "Debug") {
                                Assert-Condition ($graph -contains "Akbura.Diagnostics/$Version" -and
                                    $graph -contains "AvaloniaUI.DiagnosticsSupport/2.2.3") (
                                    "$name Debug assets omitted diagnostics packages.")
                            }
                            else {
                                Assert-Condition (!(@($graph | Where-Object {
                                    $_ -like "Akbura.Diagnostics/*" -or
                                    $_ -like "AvaloniaUI.DiagnosticsSupport/*" }).Count)) (
                                    "$name Release assets include Debug diagnostics.")
                            }

                            $graphKey = "$kind|$toolkit|$di|$removeLocator|$page|$configuration"
                            if ($cpm) {
                                Assert-Condition ($graphs.ContainsKey($graphKey)) (
                                    "Missing non-CPM comparison for $name.")
                                Assert-Condition (($graph -join "`n") -eq $graphs[$graphKey]) (
                                    "$name has a different normalized package graph under CPM.")
                            }
                            else {
                                $graphs[$graphKey] = $graph -join "`n"
                            }

                            Invoke-DotNet build $projectPath `
                                --configuration $configuration --no-restore
                            $outputPath = Join-Path (Split-Path -Parent $projectPath) (
                                "bin/$configuration/net10.0")
                            $diagnosticsOutput = Join-Path $outputPath "Akbura.Diagnostics.dll"
                            $avaloniaDiagnosticsOutput = Join-Path $outputPath (
                                "AvaloniaUI.DiagnosticsSupport.Avalonia.dll")
                            Assert-Condition ((Test-Path -LiteralPath $diagnosticsOutput) -eq
                                ($configuration -eq "Debug")) (
                                "$name $configuration has incorrect Akbura.Diagnostics output.")
                            Assert-Condition ((Test-Path -LiteralPath $avaloniaDiagnosticsOutput) -eq
                                ($configuration -eq "Debug")) (
                                "$name $configuration has incorrect Avalonia diagnostics output.")
                        }
                    }
                }
            }
        }
    }
}

Assert-Condition ($index -eq 144) (
    "Generated $index cases, expected all 144 new-template combinations.")

# Unlike the matrix's isolated --no-restore cases, this exercises the default
# MVVM post-action against the local CI feed.
$defaultName = "DefaultRestore"
$defaultDirectory = Join-Path $Projects $defaultName
$previousPackages = $env:NUGET_PACKAGES
try {
    $env:NUGET_PACKAGES = $Packages
    Invoke-DotNet new akbura.mvvm --name $defaultName `
        --output $defaultDirectory --debug:custom-hive $Hive
}
finally {
    $env:NUGET_PACKAGES = $previousPackages
}
$defaultProject = Join-Path $defaultDirectory "$defaultName.csproj"
Assert-Condition (Test-Path -LiteralPath $defaultProject -PathType Leaf) (
    "The default MVVM command did not create its project.")
$defaultAssets = Join-Path $defaultDirectory "obj/project.assets.json"
Assert-Condition (Test-Path -LiteralPath $defaultAssets -PathType Leaf) (
    "Default MVVM generation did not automatically restore packages.")
Assert-Condition ((Get-PackageGraph $defaultProject) -contains "Akbura/$Version") (
    "Default MVVM generation did not restore local Akbura $Version.")

# Execute the short aliases, rather than accepting help text as proof that
# the template engine actually binds each alias to its symbol.
foreach ($aliasCase in @(
    @{ Kind = "mvvm"; Name = "AliasMvvm"; Page = "None" },
    @{ Kind = "xplat"; Name = "AliasXplat"; Page = "NavigationPage" })) {
    $directory = Join-Path $Projects $aliasCase.Name
    $arguments = @("new", "akbura.$($aliasCase.Kind)",
        "-n", $aliasCase.Name, "-o", $directory,
        "-f", "net10.0", "-m", "ReactiveUI",
        "-av", "12.0.99", "-akv", "12.0.4-alpha.999",
        "-rvl", "true", "-cpm", "true",
        "--debug:custom-hive", $Hive)
    if ($aliasCase.Kind -eq "mvvm") {
        $arguments += "--no-restore"
    }
    else {
        $arguments += @("-page", $aliasCase.Page)
    }
    Invoke-DotNet @arguments
    Assert-GeneratedTemplate $aliasCase.Kind $aliasCase.Name $directory `
        "ReactiveUI" "None" $true $true $aliasCase.Page

    $central = Get-Content -LiteralPath (Join-Path $directory (
        "Directory.Packages.props")) -Raw
    Assert-Condition ($central.Contains("12.0.4-alpha.999", [StringComparison]::Ordinal) -and
        $central.Contains("12.0.99", [StringComparison]::Ordinal)) (
        "Short version aliases did not update $($aliasCase.Kind) package versions.")
    $project = if ($aliasCase.Kind -eq "mvvm") {
        Join-Path $directory "$($aliasCase.Name).csproj"
    }
    else {
        Join-Path $directory "$($aliasCase.Name)/$($aliasCase.Name).csproj"
    }
    $projectXml = [xml] (Get-Content -LiteralPath $project -Raw)
    Assert-Condition ($projectXml.OuterXml.Contains("net10.0", [StringComparison]::Ordinal)) (
        "The -f alias did not select net10.0 for $($aliasCase.Kind).")
}

foreach ($invalid in @(
    @("akbura.mvvm", "--mvvm", "OtherToolkit"),
    @("akbura.mvvm", "--di", "OtherContainer"),
    @("akbura.mvvm", "--framework", "net9.0"),
    @("akbura.mvvm", "--language", "F#"),
    @("akbura.xplat", "--main-view-page-type", "OtherPage"),
    @("akbura.xplat", "--framework", "net9.0"))) {
    $invalidName = "Invalid" + [Guid]::NewGuid().ToString("N")
    $invalidDirectory = Join-Path $Projects $invalidName
    & dotnet new $invalid[0] $invalid[1] $invalid[2] `
        --name $invalidName --output $invalidDirectory `
        --debug:custom-hive $Hive *> $null
    Assert-Condition ($LASTEXITCODE -ne 0) (
        "Invalid choice '$($invalid -join ' ')' was accepted.")
}

foreach ($shortName in @("akbura.mvvm", "akbura.xplat")) {
    $helpText = (& dotnet new $shortName --help --debug:custom-hive $Hive |
        Out-String)
    Assert-Condition ($LASTEXITCODE -eq 0) (
        "$shortName --help failed.")
    foreach ($option in @("--framework", "--mvvm", "--avalonia-version",
        "--akbura-version", "--remove-view-locator", "--di")) {
        Assert-Condition ($helpText.Contains($option, [StringComparison]::Ordinal)) (
            "$shortName --help does not show $option.")
    }
    Assert-Condition ($helpText.Contains("-cpm", [StringComparison]::Ordinal)) (
        "$shortName --help does not show the CPM alias.")
    $specificOption = if ($shortName -eq "akbura.mvvm") {
        "--no-restore"
    }
    else { "--main-view-page-type" }
    Assert-Condition ($helpText.Contains($specificOption, [StringComparison]::Ordinal)) (
        "$shortName --help does not show $specificOption.")
    if ($shortName -eq "akbura.xplat") {
        Assert-Condition (!$helpText.Contains("--no-restore", [StringComparison]::Ordinal)) (
            "xplat advertises a no-restore option but must not restore the whole solution.")
    }
}

foreach ($case in @(
    @{ ShortName = "akbura.mvvm"; Name = "Company.Product"; Output = "Dotted Name" },
    @{ ShortName = "akbura.xplat"; Name = "Demo-Beta"; Output = "Hyphen Name" },
    @{ ShortName = "akbura.xplat"; Name = "NamedApp"; Output = "Different Output Name" })) {
    $directory = Join-Path $Projects $case.Output
    $arguments = @("new", $case.ShortName, "--name", $case.Name,
        "--output", $directory, "--debug:custom-hive", $Hive)
    if ($case.ShortName -eq "akbura.mvvm") {
        $arguments += "--no-restore"
    }
    Invoke-DotNet @arguments
    $mainView = @(Get-ChildItem -LiteralPath $directory -Recurse -Filter MainView.akbura)
    Assert-Condition ($mainView.Count -eq 1) (
        "Special-name case $($case.Name) did not create MainView.akbura.")
    $markup = Get-Content -LiteralPath $mainView[0].FullName -Raw
    Assert-Condition ($markup -match '(?m)^namespace [A-Za-z_][A-Za-z_0-9.]*;') (
        "Special-name case $($case.Name) has an invalid markup namespace.")
    Assert-Condition (!$markup.Contains("AkburaTemplateNamespace", [StringComparison]::Ordinal)) (
        "Special-name case $($case.Name) retained a namespace placeholder.")
    if ($case.ShortName -eq "akbura.xplat") {
        $identities = foreach ($platform in @("Android", "iOS")) {
            $project = Join-Path $directory (
                "$($case.Name).$platform/$($case.Name).$platform.csproj")
            $xml = [xml] (Get-Content -LiteralPath $project -Raw)
            [string] $xml.Project.PropertyGroup.ApplicationId
        }
        Assert-Condition ($identities[0] -match
            '^[a-z_][a-z0-9_]*(\.[a-z_][a-z0-9_]*)+$') (
            "Special-name case $($case.Name) has invalid Android ID '$($identities[0])'.")
        Assert-Condition ($identities[1] -match
            '^[a-z][a-z0-9]*(\.[a-z][a-z0-9-]*)+$') (
            "Special-name case $($case.Name) has invalid iOS ID '$($identities[1])'.")
        $infoPlist = Get-Content -LiteralPath (Join-Path $directory (
            "$($case.Name).iOS/Info.plist")) -Raw
        Assert-Condition ($infoPlist.Contains($identities[1], [StringComparison]::Ordinal)) (
            "Special-name case $($case.Name) has inconsistent iOS Info.plist identity.")
    }
}

foreach ($shortName in @("akbura.mvvm", "akbura.xplat")) {
    $name = if ($shortName -eq "akbura.mvvm") {
        "OverrideMvvm"
    }
    else { "OverrideXplat" }
    $directory = Join-Path $Projects $name
    $arguments = @("new", $shortName, "--name", $name,
        "--output", $directory, "--cpm", "true",
        "--akbura-version", "12.0.4-alpha.999",
        "--avalonia-version", "12.0.99",
        "--debug:custom-hive", $Hive)
    if ($shortName -eq "akbura.mvvm") {
        $arguments += "--no-restore"
    }
    Invoke-DotNet @arguments
    $central = Get-Content -LiteralPath (Join-Path $directory (
        "Directory.Packages.props")) -Raw
    Assert-Condition ($central.Contains("12.0.4-alpha.999", [StringComparison]::Ordinal) -and
        $central.Contains("12.0.99", [StringComparison]::Ordinal)) (
        "$shortName did not apply explicit Akbura/Avalonia version choices.")
}

$negativeName = "BrokenBinding"
$negativeDirectory = Join-Path $Projects $negativeName
Invoke-DotNet new akbura.mvvm --name $negativeName `
    --output $negativeDirectory --no-restore --debug:custom-hive $Hive
$negativeMarkup = Join-Path $negativeDirectory "Views/MainView.akbura"
$negativeSource = Get-Content -LiteralPath $negativeMarkup -Raw
Assert-Condition ($negativeSource.Contains('Binding CountText', [StringComparison]::Ordinal)) (
    "The negative typed-binding fixture cannot find CountText.")
$negativeSource = $negativeSource.Replace('Binding CountText', 'Binding MissingFromViewModel')
[IO.File]::WriteAllText($negativeMarkup, $negativeSource, [Text.UTF8Encoding]::new($false))
$negativeProject = Join-Path $negativeDirectory "$negativeName.csproj"
Invoke-DotNet restore $negativeProject --configfile $NuGetConfig --packages $Packages
$negativeBuildOutput = (& dotnet build $negativeProject --no-restore 2>&1 | Out-String)
Assert-Condition ($LASTEXITCODE -ne 0 -and
    $negativeBuildOutput.Contains("AKBURA_SEMANTIC_MarkupExpressionError", [StringComparison]::Ordinal)) (
    "Invalid x.DataType binding did not produce the expected Akbura semantic diagnostic.`n$negativeBuildOutput")

$probeProject = Join-Path $PSScriptRoot "TemplateRuntimeProbe/TemplateRuntimeProbe.csproj"
foreach ($kind in @("mvvm", "xplat")) {
    foreach ($toolkit in $toolkits) {
        $runtimePages = if ($kind -eq "xplat") { $pageTypes } else { @("None") }
        foreach ($page in $runtimePages) {
            foreach ($removeLocator in $locatorOptions) {
                $name = "TemplateRuntimeProbeApp"
                $runtimeKey = if ($kind -eq "mvvm") { "m" } else { "x" }
                $toolkitKey = if ($toolkit -eq "CommunityToolkit") { "c" } else { "r" }
                $locatorKey = if ($removeLocator) { "nl" } else { "wl" }
                $pageKey = switch ($page) {
                    "None" { "n" }
                    "ContentPage" { "con" }
                    "TabbedPage" { "tab" }
                    "DrawerPage" { "drw" }
                    "NavigationPage" { "nav" }
                    Default { throw "Unknown PageType: $page" }
                }
                $directory = Join-Path $Projects "rt-$runtimeKey-$toolkitKey-$pageKey-$locatorKey"
                $arguments = @("new", "akbura.$kind", "--name", $name,
                    "--output", $directory, "--mvvm", $toolkit,
                    "--di", "None", "--cpm", "true",
                    "--remove-view-locator", $removeLocator.ToString().ToLowerInvariant(),
                    "--debug:custom-hive", $Hive)
                if ($kind -eq "mvvm") {
                    $arguments += "--no-restore"
                }
                else {
                    $arguments += @("--main-view-page-type", $page)
                }
                Invoke-DotNet @arguments
                $projectPath = if ($kind -eq "mvvm") {
                    Join-Path $directory "$name.csproj"
                }
                else { Join-Path $directory "$name/$name.csproj" }
                $projectProperty = "-p:TemplateProject=$projectPath"
                $kindProperty = "-p:TemplateKind=$kind"
                $pageProperty = "-p:TemplatePageType=$page"
                $toolkitProperty = "-p:TemplateToolkit=$toolkit"
                $diProperty = "-p:TemplateDependencyInjection=None"
                Invoke-DotNet clean $probeProject $projectProperty $kindProperty $pageProperty $toolkitProperty $diProperty
                Invoke-DotNet restore $probeProject $projectProperty $kindProperty $pageProperty $toolkitProperty $diProperty `
                    --configfile $NuGetConfig --packages $Packages
                Invoke-DotNet build $probeProject $projectProperty $kindProperty $pageProperty $toolkitProperty $diProperty `
                    --no-restore
                Invoke-DotNet test $probeProject $projectProperty $kindProperty $pageProperty $toolkitProperty $diProperty `
                    --no-build --no-restore --blame-hang-timeout 2m
            }
        }
    }
}

# The full matrix compiles all DI modes; these representative generated apps
# also initialize and resolve their actual composition roots under Headless.
foreach ($kind in @("mvvm", "xplat")) {
    foreach ($toolkit in $toolkits) {
        foreach ($di in $diOptions) {
            $name = "TemplateRuntimeProbeApp"
            $kindKey = if ($kind -eq "mvvm") { "m" } else { "x" }
            $toolkitKey = if ($toolkit -eq "CommunityToolkit") { "c" } else { "r" }
            $diKey = switch ($di) {
                "None" { "n" }
                "Splat.Locator" { "s" }
                Default { "m" }
            }
            $directory = Join-Path $Projects "rt-di-$kindKey-$toolkitKey-$diKey"
            $arguments = @("new", "akbura.$kind", "--name", $name,
                "--output", $directory, "--mvvm", $toolkit,
                "--di", $di, "--cpm", "true",
                "--remove-view-locator", "false",
                "--debug:custom-hive", $Hive)
            if ($kind -eq "mvvm") {
                $arguments += "--no-restore"
            }
            else {
                $arguments += @("--main-view-page-type", "None")
            }
            Invoke-DotNet @arguments
            $projectPath = if ($kind -eq "mvvm") {
                Join-Path $directory "$name.csproj"
            }
            else { Join-Path $directory "$name/$name.csproj" }
            $projectProperty = "-p:TemplateProject=$projectPath"
            $kindProperty = "-p:TemplateKind=$kind"
            $pageProperty = "-p:TemplatePageType=None"
            $toolkitProperty = "-p:TemplateToolkit=$toolkit"
            $diProperty = "-p:TemplateDependencyInjection=$di"
            $startupProperty = "-p:TemplateStartupProbe=true"
            # The generated MVVM App attaches classic-desktop-only diagnostics
            # in Debug; its headless startup must therefore use Release.
            Invoke-DotNet clean $probeProject $projectProperty $kindProperty $pageProperty $toolkitProperty $diProperty $startupProperty `
                --configuration Release
            Invoke-DotNet restore $probeProject $projectProperty $kindProperty $pageProperty $toolkitProperty $diProperty $startupProperty `
                "-p:Configuration=Release" `
                --configfile $NuGetConfig --packages $Packages
            Invoke-DotNet test $probeProject $projectProperty $kindProperty $pageProperty $toolkitProperty $diProperty $startupProperty `
                --configuration Release --no-restore `
                --filter FullyQualifiedName~GeneratedApplicationStartsWithRegisteredServices `
                --blame-hang-timeout 2m
        }
    }
}

Write-Host "Verified 144 new template generations, selected desktop builds, aliases, default restore, and DI startup."
