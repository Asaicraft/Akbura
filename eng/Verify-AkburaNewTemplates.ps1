param(
    [Parameter(Mandatory)] [string] $Version,
    [Parameter(Mandatory)] [string] $Hive,
    [Parameter(Mandatory)] [string] $Projects,
    [Parameter(Mandatory)] [string] $NuGetConfig,
    [Parameter(Mandatory)] [string] $Packages,
    [ValidateSet("Smoke", "Full")] [string] $Mode = "Smoke",
    [string] $BinLogDirectory
)

$ErrorActionPreference = "Stop"

if (![string]::IsNullOrWhiteSpace($BinLogDirectory)) {
    New-Item -ItemType Directory -Path $BinLogDirectory -Force | Out-Null
}
$createdBinaryLogs = [Collections.Generic.List[string]]::new()

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

function Invoke-DotNetLogged {
    param(
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [string[]] $Arguments
    )

    if ($Arguments[0] -in @("build", "test", "publish")) {
        $Arguments += "-p:UseSharedCompilation=false"
    }

    $binaryLog = $null
    if (![string]::IsNullOrWhiteSpace($BinLogDirectory)) {
        $safeName = $Name -replace '[^A-Za-z0-9_.-]', '-'
        $binaryLog = Join-Path $BinLogDirectory "$safeName.binlog"
        $Arguments += "-bl:$binaryLog"
        [void] $script:createdBinaryLogs.Add($binaryLog)
    }

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet failed ($LASTEXITCODE): $($Arguments -join ' ')"
    }

}

function Clear-BinaryLogsSince {
    param([Parameter(Mandatory)] [int] $StartIndex)

    while ($createdBinaryLogs.Count -gt $StartIndex) {
        $binaryLog = $createdBinaryLogs[$StartIndex]
        if (Test-Path -LiteralPath $binaryLog) {
            Remove-Item -LiteralPath $binaryLog -Force
        }
        $createdBinaryLogs.RemoveAt($StartIndex)
    }
}

function Remove-SuccessfulDirectory {
    param([Parameter(Mandatory)] [string] $Directory)

    $projectsRoot = [IO.Path]::GetFullPath($Projects).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar) +
        [IO.Path]::DirectorySeparatorChar
    $candidate = [IO.Path]::GetFullPath($Directory)
    Assert-Condition ($candidate.StartsWith(
        $projectsRoot,
        [StringComparison]::OrdinalIgnoreCase)) (
        "Refusing to remove a directory outside the generated-project root: $candidate")

    $maxAttempts = 120
    for ($attempt = 1; $attempt -le $maxAttempts; $attempt++) {
        if (!(Test-Path -LiteralPath $candidate)) {
            return
        }

        try {
            [IO.Directory]::Delete($candidate, $true)
            return
        }
        catch {
            if ($attempt -eq $maxAttempts) {
                throw
            }
            Start-Sleep -Milliseconds 500
        }
    }
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
        $androidProject = Join-Path $Directory (
            "$Name.Android/$Name.Android.csproj")
        $androidProjectXml = [xml] (
            Get-Content -LiteralPath $androidProject -Raw)
        $androidProperties = $androidProjectXml.Project.PropertyGroup
        Assert-Condition (
            $androidProperties.UseDefaultPublishRuntimeIdentifier -eq "false") (
            "$Name Android project does not disable the default publish " +
            "runtime identifier.")
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

function New-TemplateCase {
    param(
        [string] $Name,
        [string] $Kind,
        [string] $Toolkit,
        [string] $DependencyInjection,
        [bool] $Cpm,
        [bool] $RemoveViewLocator,
        [string] $PageType,
        [string[]] $Configurations = @()
    )

    [pscustomobject] @{
        Name = $Name
        Kind = $Kind
        Toolkit = $Toolkit
        DependencyInjection = $DependencyInjection
        Cpm = $Cpm
        RemoveViewLocator = $RemoveViewLocator
        PageType = $PageType
        Configurations = $Configurations
    }
}

function New-GeneratedTemplateCase {
    param(
        [Parameter(Mandatory)] $Case,
        [Parameter(Mandatory)] [string] $Directory
    )

    $arguments = @(
        "new", "akbura.$($Case.Kind)",
        "--name", $Case.Name,
        "--output", $Directory,
        "--mvvm", $Case.Toolkit,
        "--di", $Case.DependencyInjection,
        "--cpm", $Case.Cpm.ToString().ToLowerInvariant(),
        "--remove-view-locator",
        $Case.RemoveViewLocator.ToString().ToLowerInvariant(),
        "--debug:custom-hive", $Hive)

    if ($Case.Kind -eq "mvvm") {
        $arguments += "--no-restore"
    }
    else {
        $arguments += @("--main-view-page-type", $Case.PageType)
    }

    Invoke-DotNet @arguments
    Assert-GeneratedTemplate $Case.Kind $Case.Name $Directory $Case.Toolkit `
        $Case.DependencyInjection $Case.Cpm $Case.RemoveViewLocator $Case.PageType
}

function Get-BuildProjectPath {
    param(
        [Parameter(Mandatory)] $Case,
        [Parameter(Mandatory)] [string] $Directory
    )

    if ($Case.Kind -eq "mvvm") {
        return Join-Path $Directory "$($Case.Name).csproj"
    }

    return Join-Path $Directory (
        "$($Case.Name).Desktop/$($Case.Name).Desktop.csproj")
}

function Assert-RestoredPackages {
    param(
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [string] $Configuration,
        [Parameter(Mandatory)] [string[]] $Graph
    )

    Assert-Condition ($Graph -contains "Akbura/$Version") (
        "$Name $Configuration restored the wrong Akbura version.")

    if ($Configuration -eq "Debug") {
        Assert-Condition ($Graph -contains "Akbura.Diagnostics/$Version" -and
            $Graph -contains "AvaloniaUI.DiagnosticsSupport/2.2.3") (
            "$Name Debug assets omitted diagnostics packages.")
        return
    }

    Assert-Condition (!(@($Graph | Where-Object {
        $_ -like "Akbura.Diagnostics/*" -or
        $_ -like "AvaloniaUI.DiagnosticsSupport/*"
    }).Count)) (
        "$Name Release assets include Debug diagnostics.")
}

function Assert-BuildOutput {
    param(
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [string] $ProjectPath,
        [Parameter(Mandatory)] [string] $Configuration
    )

    $outputPath = Join-Path (Split-Path -Parent $ProjectPath) (
        "bin/$Configuration/net10.0")
    $diagnosticsOutput = Join-Path $outputPath "Akbura.Diagnostics.dll"
    $avaloniaDiagnosticsOutput = Join-Path $outputPath (
        "AvaloniaUI.DiagnosticsSupport.Avalonia.dll")
    $debug = $Configuration -eq "Debug"

    Assert-Condition ((Test-Path -LiteralPath $diagnosticsOutput) -eq $debug) (
        "$Name $Configuration has incorrect Akbura.Diagnostics output.")
    Assert-Condition ((Test-Path -LiteralPath $avaloniaDiagnosticsOutput) -eq $debug) (
        "$Name $Configuration has incorrect Avalonia diagnostics output.")
}

$toolkits = @("CommunityToolkit", "ReactiveUI")
$diOptions = @("None", "Microsoft.Extensions.DependencyInjection", "Splat.Locator")
$cpms = @($false, $true)
$locatorOptions = @($false, $true)
$pageTypes = @("None", "ContentPage", "TabbedPage", "DrawerPage", "NavigationPage")
$index = 0

foreach ($kind in @("mvvm", "xplat")) {
    $pages = if ($kind -eq "mvvm") { @("None") } else { $pageTypes }
    foreach ($toolkit in $toolkits) {
        foreach ($di in $diOptions) {
            foreach ($cpm in $cpms) {
                foreach ($removeLocator in $locatorOptions) {
                    foreach ($page in $pages) {
                        $index++
                        $name = "Structure" + $index.ToString("000")
                        $directory = Join-Path $Projects $name
                        $case = New-TemplateCase $name $kind $toolkit $di $cpm `
                            $removeLocator $page
                        New-GeneratedTemplateCase $case $directory
                        Remove-SuccessfulDirectory $directory
                        if (($index % 12) -eq 0) {
                            Write-Host "Verified $index of 144 structural generations."
                        }
                    }
                }
            }
        }
    }
}

Assert-Condition ($index -eq 144) (
    "Generated $index cases, expected all 144 new-template combinations.")

$smokeBuildCases = @(
    New-TemplateCase "MvvmCommunityDefault" "mvvm" "CommunityToolkit" "None" `
        $false $false "None" @("Debug", "Release")
    New-TemplateCase "MvvmCommunityMicrosoft" "mvvm" "CommunityToolkit" `
        "Microsoft.Extensions.DependencyInjection" $true $true "None" @("Debug")
    New-TemplateCase "MvvmCommunitySplat" "mvvm" "CommunityToolkit" `
        "Splat.Locator" $false $true "None" @("Debug")
    New-TemplateCase "MvvmReactiveNone" "mvvm" "ReactiveUI" "None" `
        $true $false "None" @("Debug")
    New-TemplateCase "MvvmReactiveMicrosoft" "mvvm" "ReactiveUI" `
        "Microsoft.Extensions.DependencyInjection" $false $true "None" @("Debug")
    New-TemplateCase "MvvmReactiveSplat" "mvvm" "ReactiveUI" "Splat.Locator" `
        $true $false "None" @("Debug", "Release")

    New-TemplateCase "XplatCommunityDefault" "xplat" "CommunityToolkit" "None" `
        $true $false "None" @("Debug", "Release")
    New-TemplateCase "XplatCommunityMicrosoft" "xplat" "CommunityToolkit" `
        "Microsoft.Extensions.DependencyInjection" $false $true "None" @("Debug")
    New-TemplateCase "XplatCommunitySplat" "xplat" "CommunityToolkit" `
        "Splat.Locator" $true $true "None" @("Debug")
    New-TemplateCase "XplatReactiveNone" "xplat" "ReactiveUI" "None" `
        $false $false "None" @("Debug")
    New-TemplateCase "XplatReactiveMicrosoft" "xplat" "ReactiveUI" `
        "Microsoft.Extensions.DependencyInjection" $true $true "None" @("Debug")
    New-TemplateCase "XplatReactiveSplat" "xplat" "ReactiveUI" "Splat.Locator" `
        $false $false "None" @("Debug", "Release")

    New-TemplateCase "ContentCommunity" "xplat" "CommunityToolkit" "None" `
        $false $false "ContentPage" @("Debug")
    New-TemplateCase "ContentReactive" "xplat" "ReactiveUI" "None" `
        $true $true "ContentPage" @("Debug")
    New-TemplateCase "TabbedCommunity" "xplat" "CommunityToolkit" "None" `
        $true $false "TabbedPage" @("Debug")
    New-TemplateCase "TabbedReactive" "xplat" "ReactiveUI" "None" `
        $false $true "TabbedPage" @("Debug")
    New-TemplateCase "DrawerCommunity" "xplat" "CommunityToolkit" "None" `
        $false $true "DrawerPage" @("Debug")
    New-TemplateCase "DrawerReactive" "xplat" "ReactiveUI" "None" `
        $true $false "DrawerPage" @("Debug")
    New-TemplateCase "NavigationCommunity" "xplat" "CommunityToolkit" "None" `
        $true $true "NavigationPage" @("Debug")
    New-TemplateCase "NavigationReactive" "xplat" "ReactiveUI" "None" `
        $false $true "NavigationPage" @("Debug")
)

$fullBuildCases = @()
if ($Mode -eq "Full") {
    $fullIndex = 0
    foreach ($kind in @("mvvm", "xplat")) {
        $pages = if ($kind -eq "mvvm") { @("None") } else { $pageTypes }
        foreach ($toolkit in $toolkits) {
            foreach ($di in $diOptions) {
                foreach ($cpm in $cpms) {
                    foreach ($removeLocator in $locatorOptions) {
                        foreach ($page in $pages) {
                            $build = ($kind -eq "mvvm") -or ($page -eq "None") -or
                                ($di -eq "None") -or
                                ($page -eq "NavigationPage" -and $removeLocator)
                            if (!$build) {
                                continue
                            }

                            $fullIndex++
                            $configurations = if ($kind -eq "mvvm" -or
                                $page -eq "None") {
                                @("Debug", "Release")
                            }
                            else {
                                @("Debug")
                            }
                            $fullBuildCases += New-TemplateCase (
                                "Full" + $fullIndex.ToString("000")) $kind $toolkit `
                                $di $cpm $removeLocator $page $configurations
                        }
                    }
                }
            }
        }
    }
}

$buildCases = if ($Mode -eq "Full") {
    $fullBuildCases
}
else {
    $smokeBuildCases
}

$graphs = @{}
$buildCount = 0
$expectedBuildCount = if ($Mode -eq "Full") { 136 } else { 24 }
foreach ($case in $buildCases) {
    $binaryLogStart = $createdBinaryLogs.Count
    $directory = Join-Path $Projects ("build-" + $case.Name)
    New-GeneratedTemplateCase $case $directory
    $projectPath = Get-BuildProjectPath $case $directory
    Assert-Condition (Test-Path -LiteralPath $projectPath -PathType Leaf) (
        "$($case.Name) does not contain build entry point $projectPath.")

    foreach ($configuration in $case.Configurations) {
        $buildCount++
        Write-Host (
            "Running $Mode build $buildCount/${expectedBuildCount}: " +
            "$($case.Name) $configuration.")
        Invoke-DotNetLogged "$($case.Name)-$configuration-restore" @(
            "restore", $projectPath,
            "-p:Configuration=$configuration",
            "--configfile", $NuGetConfig,
            "--packages", $Packages)
        $graph = @(Get-PackageGraph $projectPath)
        Assert-RestoredPackages $case.Name $configuration $graph

        if ($Mode -eq "Full") {
            $graphKey = "$($case.Kind)|$($case.Toolkit)|" +
                "$($case.DependencyInjection)|$($case.RemoveViewLocator)|" +
                "$($case.PageType)|$configuration"
            if ($case.Cpm) {
                Assert-Condition ($graphs.ContainsKey($graphKey)) (
                    "Missing non-CPM comparison for $($case.Name).")
                Assert-Condition (($graph -join "`n") -eq $graphs[$graphKey]) (
                    "$($case.Name) has a different normalized package graph under CPM.")
            }
            else {
                $graphs[$graphKey] = $graph -join "`n"
            }
        }

        Invoke-DotNetLogged "$($case.Name)-$configuration-build" @(
            "build", $projectPath,
            "--configuration", $configuration,
            "--no-restore")
        Assert-BuildOutput $case.Name $projectPath $configuration
    }

    Remove-SuccessfulDirectory $directory
    Clear-BinaryLogsSince $binaryLogStart
}

Assert-Condition ($buildCount -eq $expectedBuildCount) (
    "$Mode verification ran $buildCount builds, expected $expectedBuildCount.")

if ($Mode -eq "Smoke") {
    $cpmComparisons = @(
        New-TemplateCase "GraphMvvmCommunity" "mvvm" "CommunityToolkit" "None" `
            $false $false "None" @("Debug")
        New-TemplateCase "GraphMvvmReactiveSplat" "mvvm" "ReactiveUI" `
            "Splat.Locator" $false $false "None" @("Release")
        New-TemplateCase "GraphXplatCommunity" "xplat" "CommunityToolkit" "None" `
            $false $false "None" @("Debug")
        New-TemplateCase "GraphXplatReactiveSplat" "xplat" "ReactiveUI" `
            "Splat.Locator" $false $true "NavigationPage" @("Release")
    )

    foreach ($comparison in $cpmComparisons) {
        $binaryLogStart = $createdBinaryLogs.Count
        $pair = @()
        $pairDirectories = @()
        foreach ($cpm in @($false, $true)) {
            $suffix = if ($cpm) { "Cpm" } else { "Direct" }
            $case = New-TemplateCase "$($comparison.Name)$suffix" `
                $comparison.Kind $comparison.Toolkit `
                $comparison.DependencyInjection $cpm `
                $comparison.RemoveViewLocator $comparison.PageType `
                $comparison.Configurations
            $directory = Join-Path $Projects ("graph-" + $case.Name)
            New-GeneratedTemplateCase $case $directory
            $projectPath = Get-BuildProjectPath $case $directory
            $configuration = $case.Configurations[0]
            Invoke-DotNetLogged "$($case.Name)-$configuration-restore" @(
                "restore", $projectPath,
                "-p:Configuration=$configuration",
                "--configfile", $NuGetConfig,
                "--packages", $Packages)
            $graph = @(Get-PackageGraph $projectPath)
            Assert-RestoredPackages $case.Name $configuration $graph
            $pair += ,$graph
            $pairDirectories += $directory
        }

        Assert-Condition (($pair[0] -join "`n") -eq ($pair[1] -join "`n")) (
            "$($comparison.Name) has different CPM and non-CPM package graphs.")
        foreach ($directory in $pairDirectories) {
            Remove-SuccessfulDirectory $directory
        }
        Clear-BinaryLogsSince $binaryLogStart
    }
}

Write-Host (
    "Verified 144 structural generations and $buildCount $Mode template builds.")

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
Remove-SuccessfulDirectory $defaultDirectory

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
    Remove-SuccessfulDirectory $directory
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
    if (Test-Path -LiteralPath $invalidDirectory) {
        Remove-SuccessfulDirectory $invalidDirectory
    }
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
    Remove-SuccessfulDirectory $directory
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
    Remove-SuccessfulDirectory $directory
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
$binaryLogStart = $createdBinaryLogs.Count
Invoke-DotNetLogged "negative-binding-restore" @(
    "restore", $negativeProject,
    "--configfile", $NuGetConfig,
    "--packages", $Packages)
$negativeBuildArguments = @(
    "build", $negativeProject,
    "--no-restore",
    "-p:UseSharedCompilation=false")
if (![string]::IsNullOrWhiteSpace($BinLogDirectory)) {
    $negativeBinLog = Join-Path $BinLogDirectory "negative-binding-build.binlog"
    $negativeBuildArguments += "-bl:$negativeBinLog"
    [void] $createdBinaryLogs.Add($negativeBinLog)
}
$negativeBuildOutput = (& dotnet @negativeBuildArguments 2>&1 | Out-String)
Assert-Condition ($LASTEXITCODE -ne 0 -and
    $negativeBuildOutput.Contains("AKBURA_SEMANTIC_MarkupExpressionError", [StringComparison]::Ordinal)) (
    "Invalid x.DataType binding did not produce the expected Akbura semantic diagnostic.`n$negativeBuildOutput")
Remove-SuccessfulDirectory $negativeDirectory
Clear-BinaryLogsSince $binaryLogStart

$probeProject = Join-Path $PSScriptRoot "TemplateRuntimeProbe/TemplateRuntimeProbe.csproj"
$runtimeCases = @()
if ($Mode -eq "Full") {
    foreach ($kind in @("mvvm", "xplat")) {
        foreach ($toolkit in $toolkits) {
            $runtimePages = if ($kind -eq "xplat") {
                $pageTypes
            }
            else {
                @("None")
            }
            foreach ($page in $runtimePages) {
                foreach ($removeLocator in $locatorOptions) {
                    $runtimeCases += New-TemplateCase "TemplateRuntimeProbeApp" `
                        $kind $toolkit "None" $true $removeLocator $page
                }
            }
        }
    }
}
else {
    $runtimeCases = @(
        New-TemplateCase "TemplateRuntimeProbeApp" "mvvm" `
            "CommunityToolkit" "None" $false $false "None"
        New-TemplateCase "TemplateRuntimeProbeApp" "mvvm" `
            "ReactiveUI" "None" $true $true "None"
        New-TemplateCase "TemplateRuntimeProbeApp" "xplat" `
            "CommunityToolkit" "None" $true $false "None"
        New-TemplateCase "TemplateRuntimeProbeApp" "xplat" `
            "ReactiveUI" "None" $false $true "NavigationPage"
    )
}

$runtimeIndex = 0
foreach ($case in $runtimeCases) {
    $binaryLogStart = $createdBinaryLogs.Count
    $runtimeIndex++
    $runtimeKey = if ($case.Kind -eq "mvvm") { "m" } else { "x" }
    $toolkitKey = if ($case.Toolkit -eq "CommunityToolkit") { "c" } else { "r" }
    $locatorKey = if ($case.RemoveViewLocator) { "nl" } else { "wl" }
    $pageKey = switch ($case.PageType) {
        "None" { "n" }
        "ContentPage" { "con" }
        "TabbedPage" { "tab" }
        "DrawerPage" { "drw" }
        "NavigationPage" { "nav" }
        Default { throw "Unknown PageType: $($case.PageType)" }
    }
    $caseId = "rt-$runtimeKey-$toolkitKey-$pageKey-$locatorKey"
    Write-Host (
        "Running functional runtime case $runtimeIndex/$($runtimeCases.Count): " +
        "$caseId.")
    $directory = Join-Path $Projects $caseId
    New-GeneratedTemplateCase $case $directory
    $projectPath = if ($case.Kind -eq "mvvm") {
        Join-Path $directory "$($case.Name).csproj"
    }
    else {
        Join-Path $directory "$($case.Name)/$($case.Name).csproj"
    }
    $properties = @(
        "-p:TemplateProject=$projectPath",
        "-p:TemplateKind=$($case.Kind)",
        "-p:TemplatePageType=$($case.PageType)",
        "-p:TemplateToolkit=$($case.Toolkit)",
        "-p:TemplateDependencyInjection=None")

    $cleanArguments = @("clean", $probeProject) + $properties
    Invoke-DotNetLogged "$caseId-clean" $cleanArguments
    $restoreArguments = @(
        "restore", $probeProject) + $properties + @(
        "--configfile", $NuGetConfig,
        "--packages", $Packages)
    Invoke-DotNetLogged "$caseId-restore" $restoreArguments
    $buildArguments = @(
        "build", $probeProject) + $properties + @(
        "--no-restore")
    Invoke-DotNetLogged "$caseId-build" $buildArguments
    $testArguments = @(
        "test", $probeProject) + $properties + @(
        "--no-build", "--no-restore",
        "--blame-hang-timeout", "2m")
    Invoke-DotNetLogged "$caseId-test" $testArguments
    Remove-SuccessfulDirectory $directory
    Clear-BinaryLogsSince $binaryLogStart
}

$startupCases = @()
if ($Mode -eq "Full") {
    foreach ($kind in @("mvvm", "xplat")) {
        foreach ($toolkit in $toolkits) {
            foreach ($di in $diOptions) {
                $startupCases += New-TemplateCase "TemplateRuntimeProbeApp" `
                    $kind $toolkit $di $true $false "None"
            }
        }
    }
}
else {
    $startupCases = @(
        New-TemplateCase "TemplateRuntimeProbeApp" "mvvm" `
            "CommunityToolkit" "None" $false $false "None"
        New-TemplateCase "TemplateRuntimeProbeApp" "mvvm" `
            "CommunityToolkit" "Microsoft.Extensions.DependencyInjection" `
            $true $false "None"
        New-TemplateCase "TemplateRuntimeProbeApp" "mvvm" `
            "ReactiveUI" "Splat.Locator" $true $false "None"
        New-TemplateCase "TemplateRuntimeProbeApp" "xplat" `
            "CommunityToolkit" "None" $true $false "None"
        New-TemplateCase "TemplateRuntimeProbeApp" "xplat" `
            "CommunityToolkit" "Microsoft.Extensions.DependencyInjection" `
            $false $false "None"
        New-TemplateCase "TemplateRuntimeProbeApp" "xplat" `
            "ReactiveUI" "Splat.Locator" $true $false "None"
    )
}

$startupIndex = 0
foreach ($case in $startupCases) {
    $binaryLogStart = $createdBinaryLogs.Count
    $startupIndex++
    $kindKey = if ($case.Kind -eq "mvvm") { "m" } else { "x" }
    $toolkitKey = if ($case.Toolkit -eq "CommunityToolkit") { "c" } else { "r" }
    $diKey = switch ($case.DependencyInjection) {
        "None" { "n" }
        "Splat.Locator" { "s" }
        Default { "m" }
    }
    $caseId = "rt-di-$kindKey-$toolkitKey-$diKey"
    Write-Host (
        "Running startup case $startupIndex/$($startupCases.Count): $caseId.")
    $directory = Join-Path $Projects $caseId
    New-GeneratedTemplateCase $case $directory
    $projectPath = if ($case.Kind -eq "mvvm") {
        Join-Path $directory "$($case.Name).csproj"
    }
    else {
        Join-Path $directory "$($case.Name)/$($case.Name).csproj"
    }
    $properties = @(
        "-p:TemplateProject=$projectPath",
        "-p:TemplateKind=$($case.Kind)",
        "-p:TemplatePageType=None",
        "-p:TemplateToolkit=$($case.Toolkit)",
        "-p:TemplateDependencyInjection=$($case.DependencyInjection)",
        "-p:TemplateStartupProbe=true")

    # The generated MVVM App attaches classic-desktop-only diagnostics in
    # Debug; its headless startup must therefore use Release.
    $cleanArguments = @(
        "clean", $probeProject) + $properties + @(
        "--configuration", "Release")
    Invoke-DotNetLogged "$caseId-release-clean" $cleanArguments
    $restoreArguments = @(
        "restore", $probeProject) + $properties + @(
        "-p:Configuration=Release",
        "--configfile", $NuGetConfig,
        "--packages", $Packages)
    Invoke-DotNetLogged "$caseId-release-restore" $restoreArguments
    $testArguments = @(
        "test", $probeProject) + $properties + @(
        "--configuration", "Release",
        "--no-restore",
        "--filter",
        "FullyQualifiedName~GeneratedApplicationStartsWithRegisteredServices",
        "--blame-hang-timeout", "2m")
    Invoke-DotNetLogged "$caseId-release-test" $testArguments
    Remove-SuccessfulDirectory $directory
    Clear-BinaryLogsSince $binaryLogStart
}

Write-Host (
    "Verified $Mode templates: 144 structural generations, $buildCount builds, " +
    "$runtimeIndex functional runtime cases, and $startupIndex startup cases.")

foreach ($binaryLog in $createdBinaryLogs) {
    if (Test-Path -LiteralPath $binaryLog) {
        Remove-Item -LiteralPath $binaryLog -Force
    }
}
