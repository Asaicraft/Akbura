param(
    [Parameter(Mandatory)]
    [string] $Version,

    [Parameter(Mandatory)]
    [string] $AvaloniaVersion,

    [Parameter(Mandatory)]
    [string] $Feed,

    [Parameter(Mandatory)]
    [string] $WorkingDirectory
)

$ErrorActionPreference = "Stop"

function Invoke-DotNet {
    & dotnet @args

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet failed ($LASTEXITCODE): $($args -join ' ')"
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

$feedPath = [IO.Path]::GetFullPath($Feed)
$workingPath = [IO.Path]::GetFullPath($WorkingDirectory)
$smokeRoot = Join-Path $workingPath (
    "akbura-template-smoke-" + [Guid]::NewGuid().ToString("N"))
$hivePath = Join-Path $smokeRoot "hive"
$projectsPath = Join-Path $smokeRoot "projects"
$packagesPath = Join-Path $smokeRoot "packages"
$templatePackage = Join-Path $feedPath "Akbura.Templates.$Version.nupkg"
$diagnosticsPackage = Join-Path $feedPath "Akbura.Diagnostics.$Version.nupkg"

Assert-Condition (Test-Path -LiteralPath $templatePackage -PathType Leaf) (
    "Template package does not exist: $templatePackage")
Assert-Condition (Test-Path -LiteralPath $diagnosticsPackage -PathType Leaf) (
    "Diagnostics package does not exist: $diagnosticsPackage")

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($templatePackage)
try {
    $entries = @($archive.Entries.FullName)
    $requiredEntries = @(
        "README.md",
        "LICENSE.txt",
        "icon.png",
        "content/templates/app/.template.config/template.json",
        "content/templates/app/README.md",
        "content/templates/component/.template.config/template.json"
    )

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

    $configurationEntry = $archive.GetEntry(
        "content/templates/app/.template.config/template.json")
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
        "App template targets Akbura " +
        "'$($configuration.symbols.AkburaVersion.defaultValue)', " +
        "expected '$Version'.")
    Assert-Condition (
        $configuration.symbols.AvaloniaVersion.defaultValue -eq
            $AvaloniaVersion) (
        "App template targets Avalonia " +
        "'$($configuration.symbols.AvaloniaVersion.defaultValue)', " +
        "expected '$AvaloniaVersion'.")

    $unexpectedEntries = @(
        $entries |
            Where-Object {
                $_ -match "(^|/)(bin|obj)/" -or
                $_ -match "Akbura\.Templates\.dll$"
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

Write-Host "Verified Akbura.Templates $Version in $smokeRoot"
