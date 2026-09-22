param(
    [Parameter(Mandatory)]
    [string] $Version,

    [Parameter(Mandatory)]
    [string] $AvaloniaVersion,

    [Parameter(Mandatory)]
    [string] $Feed,

    [Parameter(Mandatory)]
    [string] $WorkingDirectory,

    [ValidateSet("Sampled", "Smoke", "Full")]
    [string] $Mode = "Smoke",

    [string] $BinLogDirectory,

    [string] $SelectionManifest,

    [string] $PlanOutputPath,

    [string] $ReportOutputPath,

    [switch] $PlanOnly
)

$ErrorActionPreference = "Stop"
Import-Module (Join-Path $PSScriptRoot "AkburaTemplateVerificationPlan.psm1") -Force
$createdBinaryLogs = [Collections.Generic.List[string]]::new()
$dotNetLogIndex = 0

if (![string]::IsNullOrWhiteSpace($BinLogDirectory)) {
    New-Item -ItemType Directory -Path $BinLogDirectory -Force | Out-Null
}

function Invoke-DotNet {
    $arguments = @($args)
    $command = [string] $arguments[0]
    if ($command -in @("build", "test", "publish")) {
        $arguments += "-p:UseSharedCompilation=false"
    }
    if (![string]::IsNullOrWhiteSpace($BinLogDirectory) -and
        $command -in @("restore", "build", "publish", "test", "clean")) {
        $script:dotNetLogIndex++
        $binaryLog = Join-Path $BinLogDirectory (
            "legacy-{0:0000}-{1}.binlog" -f $script:dotNetLogIndex, $command)
        $arguments += "-bl:$binaryLog"
        [void] $script:createdBinaryLogs.Add($binaryLog)
    }

    & dotnet @arguments

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet failed ($LASTEXITCODE): $($arguments -join ' ')"
    }
}

function Assert-Condition {
    param(
        [Parameter(Mandatory)]
        [bool] $Condition,

        [Parameter(Mandatory)]
        [string] $Message
    )

    if (!$Condition) {
        throw $Message
    }
}

function Remove-DirectoryWithRetry {
    param([Parameter(Mandatory)] [string] $Directory)

    $maxAttempts = 120
    for ($attempt = 1; $attempt -le $maxAttempts; $attempt++) {
        if (!(Test-Path -LiteralPath $Directory)) {
            return
        }

        try {
            [IO.Directory]::Delete($Directory, $true)
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

$feedPath = [IO.Path]::GetFullPath($Feed)
$workingPath = [IO.Path]::GetFullPath($WorkingDirectory)
$smokeRoot = Join-Path $workingPath (
    "at-" + [Guid]::NewGuid().ToString("N").Substring(0, 8))
$hivePath = Join-Path $smokeRoot "hive"
$projectsPath = Join-Path $smokeRoot "projects"
$packagesPath = Join-Path $smokeRoot "packages"
$runtimePackage = Join-Path $feedPath "Akbura.$Version.nupkg"
$templatePackage = Join-Path $feedPath "Akbura.Templates.$Version.nupkg"
$diagnosticsPackage = Join-Path $feedPath "Akbura.Diagnostics.$Version.nupkg"
$legacyMainView = Join-Path $PSScriptRoot (
    "../src/Akbura.Templates/templates/app/Views/MainView.akbura")
$legacyMainViewHash = "EA84C6F09A33FA88C59F0DB927949C4B4BD83EBD48CB3319828F91BD192FB15A"

Assert-Condition ((Get-FileHash -Algorithm SHA256 -LiteralPath $legacyMainView).Hash -eq
    $legacyMainViewHash) (
    "The state-based akbura.app MainView changed. Keep this template independent " +
    "from the new MVVM templates.")

Assert-Condition (Test-Path -LiteralPath $templatePackage -PathType Leaf) (
    "Template package does not exist: $templatePackage")
Assert-Condition (Test-Path -LiteralPath $diagnosticsPackage -PathType Leaf) (
    "Diagnostics package does not exist: $diagnosticsPackage")
Assert-Condition (Test-Path -LiteralPath $runtimePackage -PathType Leaf) (
    "Runtime package does not exist: $runtimePackage")

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($templatePackage)
try {
    $entries = @($archive.Entries.FullName)
    $requiredEntries = @(
        "README.md",
        "LICENSE.txt",
        "icon.png",
        "content/templates/app/.template.config/template.json",
        "content/templates/app/GlobalUsings.akbura",
        "content/templates/app/GlobalUsings.akcss",
        "content/templates/app/README.md",
        "content/templates/app/Views/MainView.akbura",
        "content/templates/app-mvvm/.template.config/template.json",
        "content/templates/app-mvvm/.template.config/dotnetcli.host.json",
        "content/templates/app-mvvm/.template.config/ide.host.json",
        "content/templates/app-mvvm/Views/MainView.akbura",
        "content/templates/app-mvvm/Variants/CommunityToolkit/ViewModels/MainViewModel.cs",
        "content/templates/app-mvvm/Variants/ReactiveUI/ViewModels/MainViewModel.cs",
        "content/templates/xplat/.template.config/template.json",
        "content/templates/xplat/.template.config/dotnetcli.host.json",
        "content/templates/xplat/.template.config/ide.host.json",
        "content/templates/xplat/AkburaXplatTemplate.slnx",
        "content/templates/xplat/AkburaXplatTemplate/AkburaXplatTemplate.csproj",
        "content/templates/xplat/AkburaXplatTemplate/Views/MainView.akbura",
        "content/templates/xplat/Variants/PageNone/AppShell.akbura",
        "content/templates/xplat/Variants/PageContent/AppShell.akbura",
        "content/templates/xplat/Variants/PageTabbed/AppShell.akbura",
        "content/templates/xplat/Variants/PageDrawer/AppShell.akbura",
        "content/templates/xplat/Variants/PageNavigation/AppShell.akbura",
        "content/templates/xplat/AkburaXplatTemplate.Desktop/AkburaXplatTemplate.Desktop.csproj",
        "content/templates/xplat/AkburaXplatTemplate.Browser/AkburaXplatTemplate.Browser.csproj",
        "content/templates/xplat/AkburaXplatTemplate.Android/AkburaXplatTemplate.Android.csproj",
        "content/templates/xplat/AkburaXplatTemplate.iOS/AkburaXplatTemplate.iOS.csproj",
        "content/templates/component/.template.config/template.json",
        "content/templates/partial-component/.template.config/template.json",
        "content/templates/partial-component/NewComponent.akbura",
        "content/templates/partial-component/NewComponent.akbura.cs"
    )

    Assert-Condition (@($entries | Where-Object { $_ -like "*/MainViewHost.cs" }).Count -eq 0) (
        "Template package contains the retired MainViewHost.cs factory.")

    foreach ($entry in $requiredEntries) {
        Assert-Condition ($entries -contains $entry) (
            "Template package does not contain '$entry'.")
    }

    $nuspecEntry = $archive.GetEntry("Akbura.Templates.nuspec")
    Assert-Condition ($null -ne $nuspecEntry) (
        "Template package does not contain Akbura.Templates.nuspec.")

    $nuspecReader = [IO.StreamReader]::new($nuspecEntry.Open())
    try {
        $nuspec = [xml] $nuspecReader.ReadToEnd()
    }
    finally {
        $nuspecReader.Dispose()
    }

    $metadata = $nuspec.package.metadata
    Assert-Condition ($metadata.id -eq "Akbura.Templates") (
        "Template package has an incorrect package ID: $($metadata.id)")
    Assert-Condition ($metadata.version -eq $Version) (
        "Template package has version '$($metadata.version)', expected '$Version'.")
    Assert-Condition ($metadata.icon -eq "icon.png") (
        "Template package has an incorrect icon: $($metadata.icon)")
    Assert-Condition (
        $metadata.packageTypes.packageType.name -eq "Template") (
        "Template package does not declare the Template package type.")

    foreach ($template in @("app", "app-mvvm", "xplat")) {
        $configurationEntry = $archive.GetEntry(
            "content/templates/$template/.template.config/template.json")
        $configurationReader = [IO.StreamReader]::new(
            $configurationEntry.Open())
        try {
            $configuration = $configurationReader.ReadToEnd() |
                ConvertFrom-Json
        }
        finally {
            $configurationReader.Dispose()
        }

        Assert-Condition (
            $configuration.symbols.AkburaVersion.defaultValue -eq $Version) (
            "$template template targets Akbura " +
            "'$($configuration.symbols.AkburaVersion.defaultValue)', " +
            "expected '$Version'.")
        Assert-Condition (
            $configuration.symbols.AvaloniaVersion.defaultValue -eq
                $AvaloniaVersion) (
            "$template template targets Avalonia " +
            "'$($configuration.symbols.AvaloniaVersion.defaultValue)', " +
            "expected '$AvaloniaVersion'.")
    }

    $unexpectedEntries = @(
        $entries |
            Where-Object {
                $_ -match "(^|/)(bin|obj)/" -or
                $_ -match "Akbura\.Templates\.dll$" -or
                $_ -match "^content/templates/csharp/"
            })
    Assert-Condition ($unexpectedEntries.Count -eq 0) (
        "Template package contains build output: " +
        ($unexpectedEntries -join ", "))
}
finally {
    $archive.Dispose()
}

$diagnosticsArchive = [IO.Compression.ZipFile]::OpenRead(
    $diagnosticsPackage)
try {
    Assert-Condition ($null -ne $diagnosticsArchive.GetEntry(
        "lib/net10.0/Akbura.Diagnostics.dll")) (
        "Diagnostics package does not contain Akbura.Diagnostics.dll.")

    $diagnosticsNuspecEntry = $diagnosticsArchive.GetEntry(
        "Akbura.Diagnostics.nuspec")
    Assert-Condition ($null -ne $diagnosticsNuspecEntry) (
        "Diagnostics package does not contain its nuspec.")

    $diagnosticsNuspecReader = [IO.StreamReader]::new(
        $diagnosticsNuspecEntry.Open())
    try {
        $diagnosticsNuspec = [xml] $diagnosticsNuspecReader.ReadToEnd()
    }
    finally {
        $diagnosticsNuspecReader.Dispose()
    }

    $diagnosticsMetadata = $diagnosticsNuspec.package.metadata
    Assert-Condition ($diagnosticsMetadata.id -eq "Akbura.Diagnostics") (
        "Diagnostics package has an incorrect package ID.")
    Assert-Condition ($diagnosticsMetadata.version -eq $Version) (
        "Diagnostics package has version '$($diagnosticsMetadata.version)', " +
        "expected '$Version'.")

    $akburaDependency = $diagnosticsNuspec.SelectSingleNode(
        "/*[local-name()='package']" +
        "/*[local-name()='metadata']" +
        "/*[local-name()='dependencies']" +
        "//*[local-name()='dependency' and @id='Akbura']")
    Assert-Condition ($null -ne $akburaDependency) (
        "Diagnostics package does not depend on Akbura.")
    Assert-Condition ($akburaDependency.version -eq $Version) (
        "Diagnostics package targets Akbura " +
        "'$($akburaDependency.version)', expected '$Version'.")
}
finally {
    $diagnosticsArchive.Dispose()
}

$sourceCommit = if (![string]::IsNullOrWhiteSpace($env:GITHUB_SHA)) {
    $env:GITHUB_SHA
}
else {
    $commit = (& git rev-parse HEAD 2>&1 | Out-String).Trim()
    Assert-Condition ($LASTEXITCODE -eq 0 -and
        ![string]::IsNullOrWhiteSpace($commit)) (
        "Could not determine the source commit for the verification plan.")
    $commit
}
$sourceStatus = @(& git status --porcelain 2>&1)
Assert-Condition ($LASTEXITCODE -eq 0) (
    "Could not determine the source working-tree state.")
$releaseContext = [pscustomobject] [ordered] @{
    SourceCommit = $sourceCommit
    SourceDirty = $sourceStatus.Count -gt 0
    AkburaVersion = $Version
    AvaloniaVersion = $AvaloniaVersion
    WorkflowRunId = [string] $env:GITHUB_RUN_ID
    WorkflowRunAttempt = [string] $env:GITHUB_RUN_ATTEMPT
    Packages = @(
        [pscustomobject] [ordered] @{
            Name = [IO.Path]::GetFileName($runtimePackage)
            Sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $runtimePackage).Hash.ToLowerInvariant()
        },
        [pscustomobject] [ordered] @{
            Name = [IO.Path]::GetFileName($diagnosticsPackage)
            Sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $diagnosticsPackage).Hash.ToLowerInvariant()
        },
        [pscustomobject] [ordered] @{
            Name = [IO.Path]::GetFileName($templatePackage)
            Sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $templatePackage).Hash.ToLowerInvariant()
        })
}

$catalog = @(Get-AkburaTemplateCaseCatalog)
$effectivePlanPath = if (![string]::IsNullOrWhiteSpace($SelectionManifest)) {
    [IO.Path]::GetFullPath($SelectionManifest)
}
elseif (![string]::IsNullOrWhiteSpace($PlanOutputPath)) {
    [IO.Path]::GetFullPath($PlanOutputPath)
}
else {
    Join-Path $workingPath "template-verification-plan.json"
}
$plan = Resolve-AkburaTemplateVerificationPlan `
    -Mode $Mode `
    -Catalog $catalog `
    -ReleaseContext $releaseContext `
    -SelectionManifest $SelectionManifest `
    -PlanOutputPath $effectivePlanPath

if ($PlanOnly) {
    Write-Host (
        "Prepared $Mode template verification plan with " +
        "$(@($plan.StructuralCaseIds).Count) structural cases and " +
        "$(@($plan.Executions).Count) main executions: $effectivePlanPath")
    return
}

if ([string]::IsNullOrWhiteSpace($ReportOutputPath)) {
    $ReportOutputPath = Join-Path $workingPath "template-verification-report.json"
}
$initialReportPath = [IO.Path]::GetFullPath($ReportOutputPath)
[IO.Directory]::CreateDirectory(
    (Split-Path -Parent $initialReportPath)) | Out-Null
$initialReport = [pscustomobject] [ordered] @{
    SchemaVersion = 1
    OverallStatus = "Running"
    Mode = $Mode
    StartedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    CompletedAtUtc = $null
    Failure = $null
    Source = $plan.Source
    Catalog = $plan.Catalog
    Phase = "LegacyAndPackageChecks"
}
$initialReportJson = $initialReport | ConvertTo-Json -Depth 12
$initialReportJson =
    ($initialReportJson -replace "`r?`n", "`r`n") + "`r`n"
[IO.File]::WriteAllText(
    $initialReportPath,
    $initialReportJson,
    [Text.UTF8Encoding]::new($false))

New-Item -ItemType Directory -Path $hivePath -Force | Out-Null
New-Item -ItemType Directory -Path $projectsPath -Force | Out-Null

$isolatedMsBuildProject = "<Project />" + [Environment]::NewLine
[IO.File]::WriteAllText(
    (Join-Path $smokeRoot "Directory.Build.props"),
    $isolatedMsBuildProject,
    [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText(
    (Join-Path $smokeRoot "Directory.Packages.props"),
    $isolatedMsBuildProject,
    [Text.UTF8Encoding]::new($false))

Invoke-DotNet new install $templatePackage `
    --debug:custom-hive $hivePath
Invoke-DotNet new list akbura `
    --debug:custom-hive $hivePath

Invoke-DotNet new nugetconfig `
    --force `
    --output $smokeRoot

$nugetConfig = Get-ChildItem -LiteralPath $smokeRoot -File |
    Where-Object { $_.Name -ieq "nuget.config" } |
    Select-Object -First 1
Assert-Condition ($null -ne $nugetConfig) (
    "dotnet new nugetconfig did not create NuGet.Config.")

Invoke-DotNet nuget add source $feedPath `
    --name AkburaTemplateVerification `
    --configfile $nugetConfig.FullName

# Explicit source mapping keeps local CI packages on the test feed when CPM is
# enabled, while ordinary third-party dependencies resolve from nuget.org.
$nugetSettings = [xml] (Get-Content -LiteralPath $nugetConfig.FullName -Raw)
$mapping = $nugetSettings.CreateElement("packageSourceMapping")
$localSource = $nugetSettings.CreateElement("packageSource")
$localSource.SetAttribute("key", "AkburaTemplateVerification")
$localPattern = $nugetSettings.CreateElement("package")
$localPattern.SetAttribute("pattern", "Akbura*")
[void] $localSource.AppendChild($localPattern)
[void] $mapping.AppendChild($localSource)
$remoteSource = $nugetSettings.CreateElement("packageSource")
$nugetSource = $nugetSettings.SelectSingleNode(
    "//*[local-name()='packageSources']" +
    "/*[local-name()='add' and contains(@value, 'nuget.org')]")
Assert-Condition ($null -ne $nugetSource) (
    "The isolated NuGet.Config has no nuget.org package source.")
$remoteSource.SetAttribute("key", $nugetSource.GetAttribute("key"))
$remotePattern = $nugetSettings.CreateElement("package")
$remotePattern.SetAttribute("pattern", "*")
[void] $remoteSource.AppendChild($remotePattern)
[void] $mapping.AppendChild($remoteSource)
[void] $nugetSettings.configuration.AppendChild($mapping)
$nugetSettings.Save($nugetConfig.FullName)

$cases = @(
    [pscustomobject] @{
        Name = "Smoke.None"
        DependencyInjection = "None"
    },
    [pscustomobject] @{
        Name = "Smoke.MicrosoftDI"
        DependencyInjection = "Microsoft.Extensions.DependencyInjection"
    },
    [pscustomobject] @{
        Name = "Smoke.Splat"
        DependencyInjection = "Splat.Locator"
    }
)

foreach ($case in $cases) {
    $projectDirectory = Join-Path $projectsPath $case.Name
    $projectPath = Join-Path $projectDirectory ($case.Name + ".csproj")

    Invoke-DotNet new akbura.app `
        --name $case.Name `
        --output $projectDirectory `
        --di $case.DependencyInjection `
        --no-restore `
        --debug:custom-hive $hivePath

    Push-Location $projectDirectory
    try {
        Invoke-DotNet new akbura.component `
            --name ProfileCard `
            --namespace ($case.Name + ".Components") `
            --output Components `
            --debug:custom-hive $hivePath

        Invoke-DotNet new akbura.partial-component `
            --name SettingsCard `
            --namespace ($case.Name + ".Components") `
            --output Components `
            --debug:custom-hive $hivePath
    }
    finally {
        Pop-Location
    }

    $programPath = Join-Path $projectDirectory "Program.cs"
    $appCodePath = Join-Path $projectDirectory "App.axaml.cs"
    $projectContent = Get-Content -LiteralPath $projectPath -Raw
    $programContent = Get-Content -LiteralPath $programPath -Raw
    $appCodeContent = Get-Content -LiteralPath $appCodePath -Raw
    $appContent = Get-Content -LiteralPath (
        Join-Path $projectDirectory "App.axaml") -Raw
    $globalUsingsContent = Get-Content -LiteralPath (
        Join-Path $projectDirectory "GlobalUsings.akbura") -Raw
    $globalAkcssUsingsPath = Join-Path (
        $projectDirectory) "GlobalUsings.akcss"
    $mainViewPath = Join-Path (
        $projectDirectory) "Views/MainView.akbura"

    Assert-Condition (
        Test-Path -LiteralPath $globalAkcssUsingsPath -PathType Leaf) (
        "$($case.Name) does not contain GlobalUsings.akcss.")
    Assert-Condition (
        Test-Path -LiteralPath $mainViewPath -PathType Leaf) (
        "$($case.Name) does not contain Views/MainView.akbura.")

    $globalAkcssUsingsContent = Get-Content `
        -LiteralPath $globalAkcssUsingsPath `
        -Raw
    $mainViewContent = Get-Content `
        -LiteralPath $mainViewPath `
        -Raw

    Assert-Condition ($programContent.Contains(".UseAkbura(", [StringComparison]::Ordinal)) (
        "$($case.Name) does not call UseAkbura.")
    Assert-Condition ($appContent.Contains(
        "avares://Akbura/Styles.axaml",
        [StringComparison]::Ordinal)) (
        "$($case.Name) does not include Akbura resources.")
    Assert-Condition ($globalUsingsContent.Contains(
        "using Akbura.Styles.akcss;",
        [StringComparison]::Ordinal)) (
        "$($case.Name) does not import Akbura AKCSS utilities.")
    Assert-Condition ($globalAkcssUsingsContent.Contains(
        "@using Akbura.Styles.akcss;",
        [StringComparison]::Ordinal)) (
        "$($case.Name) does not import built-in styles into AKCSS.")
    Assert-Condition (
        $mainViewContent.Contains(
            "@akcss {",
            [StringComparison]::Ordinal) -and
        $mainViewContent.Contains(
            "@apply p-5",
            [StringComparison]::Ordinal) -and
        $mainViewContent.Contains(
            '${md}:gap-6',
            [StringComparison]::Ordinal)) (
        "$($case.Name) does not contain the responsive AKCSS gallery.")
    Assert-Condition (
        !$mainViewContent.Contains(
            "Amx.DynamicResource",
            [StringComparison]::Ordinal) -and
        !$mainViewContent.Contains(
            "mb-4",
            [StringComparison]::Ordinal)) (
        "$($case.Name) duplicates built-in utility behavior in MainView.")
    Assert-Condition ($projectContent.Contains(
        '<ItemGroup Condition="''$(Configuration)'' == ''Debug''">',
        [StringComparison]::Ordinal)) (
        "$($case.Name) does not condition diagnostics on Debug.")
    Assert-Condition (
        $projectContent.Contains(
            'Include="Akbura.Diagnostics"',
            [StringComparison]::Ordinal) -and
        $projectContent.Contains(
            'Include="AvaloniaUI.DiagnosticsSupport"',
            [StringComparison]::Ordinal)) (
        "$($case.Name) does not reference both diagnostics packages.")
    Assert-Condition ($appCodeContent.Contains(
        "#if DEBUG",
        [StringComparison]::Ordinal)) (
        "$($case.Name) does not preserve its Debug compilation guard.")
    Assert-Condition (
        $appCodeContent.Contains(
            "this.AttachDeveloperTools();",
            [StringComparison]::Ordinal) -and
        $appCodeContent.Contains(
            "this.AttachAkburaDevTools(options =>",
            [StringComparison]::Ordinal) -and
        $appCodeContent.Contains(
            "KeyModifiers.Control",
            [StringComparison]::Ordinal)) (
        "$($case.Name) does not configure both diagnostics tools.")
    Assert-Condition (Test-Path -LiteralPath (
        Join-Path $projectDirectory "README.md") -PathType Leaf) (
        "$($case.Name) does not contain its diagnostics README.")
    Assert-Condition (!(Test-Path -LiteralPath (Join-Path $projectDirectory "Variants"))) (
        "$($case.Name) contains the internal Variants directory.")
    Assert-Condition (!(Test-Path -LiteralPath (Join-Path $projectDirectory ".template.config"))) (
        "$($case.Name) contains template configuration files.")

    $welcomeMessages = @(
        Get-ChildItem `
            -LiteralPath (Join-Path $projectDirectory "Components") `
            -Filter "WelcomeMessage.akbura" `
            -File)
    Assert-Condition ($welcomeMessages.Count -eq 1) (
        "$($case.Name) must contain exactly one WelcomeMessage.akbura.")

    $profileCard = Get-Content -LiteralPath (
        Join-Path $projectDirectory "Components/ProfileCard.akbura") -Raw
    Assert-Condition (
        $profileCard.Contains(
            "namespace $($case.Name).Components;",
            [StringComparison]::Ordinal)) (
        "$($case.Name) generated an incorrect component namespace.")

    $settingsCardPath = Join-Path (
        $projectDirectory) "Components/SettingsCard.akbura"
    $settingsCardCodePath = Join-Path (
        $projectDirectory) "Components/SettingsCard.akbura.cs"
    Assert-Condition (
        Test-Path -LiteralPath $settingsCardPath -PathType Leaf) (
        "$($case.Name) did not generate SettingsCard.akbura.")
    Assert-Condition (
        Test-Path -LiteralPath $settingsCardCodePath -PathType Leaf) (
        "$($case.Name) did not generate SettingsCard.akbura.cs.")

    $settingsCard = Get-Content -LiteralPath $settingsCardPath -Raw
    $settingsCardCode = Get-Content -LiteralPath $settingsCardCodePath -Raw
    Assert-Condition (
        $settingsCard.Contains(
            "namespace $($case.Name).Components;",
            [StringComparison]::Ordinal) -and
        $settingsCardCode.Contains(
            "namespace $($case.Name).Components;",
            [StringComparison]::Ordinal)) (
        "$($case.Name) generated inconsistent partial component namespaces.")
    Assert-Condition (
        $settingsCardCode.Contains(
            "public partial class SettingsCard",
            [StringComparison]::Ordinal)) (
        "$($case.Name) generated an incorrect partial component class.")

    switch ($case.DependencyInjection) {
        "None" {
            Assert-Condition (!(Test-Path -LiteralPath (
                Join-Path $projectDirectory "Services"))) (
                "The None variant contains services.")
            Assert-Condition (!(Test-Path -LiteralPath (
                Join-Path $projectDirectory "Infrastructure"))) (
                "The None variant contains DI infrastructure.")
            Assert-Condition (
                !$projectContent.Contains(
                    "Microsoft.Extensions.DependencyInjection",
                    [StringComparison]::Ordinal) -and
                !$projectContent.Contains("Splat", [StringComparison]::Ordinal)) (
                "The None variant contains an external DI package.")
        }
        "Microsoft.Extensions.DependencyInjection" {
            Assert-Condition (Test-Path -LiteralPath (
                Join-Path $projectDirectory "Services/GreetingService.cs")) (
                "The Microsoft DI variant does not contain services.")
            Assert-Condition (!(Test-Path -LiteralPath (
                Join-Path $projectDirectory "Infrastructure"))) (
                "The Microsoft DI variant contains Splat infrastructure.")
            Assert-Condition (
                $projectContent.Contains(
                    "Microsoft.Extensions.DependencyInjection",
                    [StringComparison]::Ordinal) -and
                !$projectContent.Contains("Splat", [StringComparison]::Ordinal)) (
                "The Microsoft DI variant has incorrect package references.")
        }
        "Splat.Locator" {
            Assert-Condition (Test-Path -LiteralPath (
                Join-Path $projectDirectory "Services/GreetingService.cs")) (
                "The Splat variant does not contain services.")
            Assert-Condition (Test-Path -LiteralPath (
                Join-Path $projectDirectory "Infrastructure/SplatServiceProvider.cs")) (
                "The Splat variant does not contain its service-provider adapter.")
            Assert-Condition (
                $projectContent.Contains("Splat", [StringComparison]::Ordinal) -and
                !$projectContent.Contains(
                    "Microsoft.Extensions.DependencyInjection",
                    [StringComparison]::Ordinal)) (
                "The Splat variant has incorrect package references.")
        }
    }

    $templateMarkers = @(
        "AkburaAppTemplate",
        "AkburaTemplateNamespace",
        "AkburaVersionTemplateParameter",
        "AvaloniaVersionTemplateParameter",
        "TemplateTargetFramework",
        "NewComponent",
        "AkburaComponentNamespace",
        "UseMicrosoftDI",
        "UseSplat",
        "UseDI",
        "cnd:noEmit",
        "msbuild-conditional:noEmit"
    )
    $generatedSourceFiles = Get-ChildItem `
        -LiteralPath $projectDirectory `
        -File `
        -Recurse `
        -Force
    foreach ($file in $generatedSourceFiles) {
        $content = Get-Content -LiteralPath $file.FullName -Raw
        foreach ($marker in $templateMarkers) {
            Assert-Condition (!$content.Contains($marker, [StringComparison]::Ordinal)) (
                "$($case.Name) contains unreplaced marker '$marker' in '$($file.FullName)'.")
        }
    }

    foreach ($configuration in @("Debug", "Release")) {
        Invoke-DotNet restore $projectPath `
            "-p:Configuration=$configuration" `
            --configfile $nugetConfig.FullName `
            --packages $packagesPath

        $assetsPath = Join-Path $projectDirectory "obj/project.assets.json"
        $assets = Get-Content -LiteralPath $assetsPath -Raw |
            ConvertFrom-Json
        $libraries = @($assets.libraries.PSObject.Properties.Name)
        $hasAkburaDiagnostics =
            $libraries -contains "Akbura.Diagnostics/$Version"
        $hasAvaloniaDiagnostics =
            $libraries -contains "AvaloniaUI.DiagnosticsSupport/2.2.3"

        if ($configuration -eq "Debug") {
            Assert-Condition $hasAkburaDiagnostics (
                "$($case.Name) Debug restore omitted Akbura.Diagnostics.")
            Assert-Condition $hasAvaloniaDiagnostics (
                "$($case.Name) Debug restore omitted Avalonia diagnostics.")
        }
        else {
            Assert-Condition (!$hasAkburaDiagnostics) (
                "$($case.Name) Release restore contains Akbura.Diagnostics.")
            Assert-Condition (!$hasAvaloniaDiagnostics) (
                "$($case.Name) Release restore contains Avalonia diagnostics.")
        }

        Invoke-DotNet build $projectPath `
            --configuration $configuration `
            --no-restore

        $outputPath = Join-Path $projectDirectory (
            "bin/$configuration/net10.0")
        $akburaDiagnosticsAssembly = Join-Path (
            $outputPath) "Akbura.Diagnostics.dll"
        $avaloniaDiagnosticsAssembly = Join-Path (
            $outputPath) "AvaloniaUI.DiagnosticsSupport.Avalonia.dll"

        if ($configuration -eq "Debug") {
            Assert-Condition (Test-Path -LiteralPath (
                $akburaDiagnosticsAssembly) -PathType Leaf) (
                "$($case.Name) Debug output omitted Akbura.Diagnostics.dll.")
            Assert-Condition (Test-Path -LiteralPath (
                $avaloniaDiagnosticsAssembly) -PathType Leaf) (
                "$($case.Name) Debug output omitted Avalonia diagnostics.")
            continue
        }

        Assert-Condition (!(Test-Path -LiteralPath (
            $akburaDiagnosticsAssembly) -PathType Leaf)) (
            "$($case.Name) Release output contains Akbura.Diagnostics.dll.")
        Assert-Condition (!(Test-Path -LiteralPath (
            $avaloniaDiagnosticsAssembly) -PathType Leaf)) (
            "$($case.Name) Release output contains Avalonia diagnostics.")

        $publishPath = Join-Path $smokeRoot (
            "publish/" + $case.Name)
        Invoke-DotNet publish $projectPath `
            --configuration Release `
            --no-restore `
            --output $publishPath

        $publishedFiles = @(
            Get-ChildItem -LiteralPath $publishPath -File -Recurse)
        Assert-Condition (!($publishedFiles.Name -contains
            "Akbura.Diagnostics.dll")) (
            "$($case.Name) Release publish contains Akbura.Diagnostics.dll.")
        Assert-Condition (!($publishedFiles.Name -contains
            "AvaloniaUI.DiagnosticsSupport.Avalonia.dll")) (
            "$($case.Name) Release publish contains Avalonia diagnostics.")

        $depsFile = @(
            $publishedFiles |
                Where-Object { $_.Name -like "*.deps.json" })
        Assert-Condition ($depsFile.Count -eq 1) (
            "$($case.Name) Release publish must contain one deps.json file.")
        $depsContent = Get-Content -LiteralPath $depsFile[0].FullName -Raw
        Assert-Condition (
            !$depsContent.Contains(
                "Akbura.Diagnostics",
                [StringComparison]::OrdinalIgnoreCase) -and
            !$depsContent.Contains(
                "AvaloniaUI.DiagnosticsSupport",
                [StringComparison]::OrdinalIgnoreCase)) (
            "$($case.Name) Release deps.json contains diagnostics.")
    }
}

& (Join-Path $PSScriptRoot "Verify-AkburaNewTemplates.ps1") `
    -Version $Version `
    -Hive $hivePath `
    -Projects $projectsPath `
    -NuGetConfig $nugetConfig.FullName `
    -Packages $packagesPath `
    -Mode $Mode `
    -SelectionManifest $effectivePlanPath `
    -ReportOutputPath $ReportOutputPath `
    -BinLogDirectory $BinLogDirectory

Remove-DirectoryWithRetry $smokeRoot

foreach ($binaryLog in $createdBinaryLogs) {
    if (Test-Path -LiteralPath $binaryLog) {
        Remove-Item -LiteralPath $binaryLog -Force
    }
}

Write-Host "Verified Akbura.Templates $Version in $Mode mode."
