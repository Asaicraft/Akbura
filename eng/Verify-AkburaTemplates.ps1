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

Assert-Condition (Test-Path -LiteralPath $templatePackage -PathType Leaf) (
    "Template package does not exist: $templatePackage")

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($templatePackage)
try {
    $entries = @($archive.Entries.FullName)
    $requiredEntries = @(
        "README.md",
        "LICENSE.txt",
        "icon.png",
        "content/templates/app/.template.config/template.json",
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

New-Item -ItemType Directory -Path $hivePath -Force | Out-Null
New-Item -ItemType Directory -Path $projectsPath -Force | Out-Null

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
    $projectContent = Get-Content -LiteralPath $projectPath -Raw
    $programContent = Get-Content -LiteralPath $programPath -Raw
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
        "UseDI"
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

    Invoke-DotNet restore $projectPath `
        --configfile $nugetConfig.FullName `
        --packages $packagesPath
    Invoke-DotNet build $projectPath `
        --configuration Debug `
        --no-restore
    Invoke-DotNet build $projectPath `
        --configuration Release `
        --no-restore
}

Write-Host "Verified Akbura.Templates $Version in $smokeRoot"
