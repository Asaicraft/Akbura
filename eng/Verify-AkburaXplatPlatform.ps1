param(
    [Parameter(Mandatory)]
    [ValidateSet("Browser", "Android", "iOS")]
    [string] $Platform,

    [Parameter(Mandatory)]
    [string] $Version,

    [Parameter(Mandatory)]
    [string] $Feed,

    [Parameter(Mandatory)]
    [string] $WorkingDirectory,

    [ValidateSet("CommunityToolkit", "ReactiveUI")]
    [string] $Toolkit,

    [string] $BinLogDirectory
)

$ErrorActionPreference = "Stop"
$createdBinaryLogs = [Collections.Generic.List[string]]::new()

if ([string]::IsNullOrWhiteSpace($Toolkit)) {
    $Toolkit = if ($Platform -eq "Browser") {
        "ReactiveUI"
    }
    else {
        "CommunityToolkit"
    }
}

if (![string]::IsNullOrWhiteSpace($BinLogDirectory)) {
    New-Item -ItemType Directory -Path $BinLogDirectory -Force | Out-Null
}

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

function Invoke-DotNetLogged {
    param(
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [string[]] $Arguments
    )

    if ($Arguments[0] -in @("build", "test", "publish")) {
        $Arguments += "-p:UseSharedCompilation=false"
    }

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

function Get-PackageGraph {
    param([string] $ProjectPath)

    $assetsPath = Join-Path (Split-Path -Parent $ProjectPath) "obj/project.assets.json"
    Assert-Condition (Test-Path -LiteralPath $assetsPath -PathType Leaf) (
        "Restore did not create $assetsPath.")

    $assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json
    @($assets.libraries.PSObject.Properties |
        Where-Object { $_.Value.type -eq "package" } |
        ForEach-Object { $_.Name })
}

$feedPath = [IO.Path]::GetFullPath($Feed)
$workingPath = [IO.Path]::GetFullPath($WorkingDirectory)
$templatePackage = Join-Path $feedPath "Akbura.Templates.$Version.nupkg"
$runtimePackage = Join-Path $feedPath "Akbura.$Version.nupkg"
$diagnosticsPackage = Join-Path $feedPath "Akbura.Diagnostics.$Version.nupkg"

foreach ($package in @($templatePackage, $runtimePackage, $diagnosticsPackage)) {
    Assert-Condition (Test-Path -LiteralPath $package -PathType Leaf) (
        "The local CI package is missing: $package")
}

$root = Join-Path $workingPath (
    "akbura-xplat-$($Platform.ToLowerInvariant())-" +
    "$($Toolkit.ToLowerInvariant())-" +
    [Guid]::NewGuid().ToString("N").Substring(0, 8))
$hive = Join-Path $root "hive"
$packages = Join-Path $root "packages"
$projectDirectory = Join-Path $root "PlatformSmoke"
$project = Join-Path $projectDirectory (
    "PlatformSmoke.$Platform/PlatformSmoke.$Platform.csproj")

New-Item -ItemType Directory -Path $hive -Force | Out-Null

# Stop any parent checkout's MSBuild package settings from leaking into the
# generated sample. The sample's own props take precedence when present.
foreach ($fileName in @("Directory.Build.props", "Directory.Packages.props")) {
    [IO.File]::WriteAllText(
        (Join-Path $root $fileName),
        "<Project />`r`n",
        [Text.UTF8Encoding]::new($false))
}

Invoke-DotNet new install $templatePackage --debug:custom-hive $hive

$arguments = @(
    "new", "akbura.xplat",
    "--name", "PlatformSmoke",
    "--output", $projectDirectory,
    "--debug:custom-hive", $hive)

if ($Platform -eq "Browser" -and $Toolkit -eq "ReactiveUI") {
    $arguments += @(
        "--mvvm", "ReactiveUI",
        "--di", "Microsoft.Extensions.DependencyInjection",
        "--cpm", "true",
        "--remove-view-locator", "false",
        "--main-view-page-type", "None")
}
elseif ($Platform -eq "Browser") {
    $arguments += @(
        "--mvvm", "CommunityToolkit",
        "--di", "None",
        "--cpm", "false",
        "--remove-view-locator", "true",
        "--main-view-page-type", "ContentPage")
}
elseif ($Platform -eq "Android" -and $Toolkit -eq "CommunityToolkit") {
    $arguments += @(
        "--mvvm", "CommunityToolkit",
        "--di", "Splat.Locator",
        "--cpm", "false",
        "--remove-view-locator", "true",
        "--main-view-page-type", "NavigationPage")
}
elseif ($Platform -eq "Android") {
    $arguments += @(
        "--mvvm", "ReactiveUI",
        "--di", "Microsoft.Extensions.DependencyInjection",
        "--cpm", "true",
        "--remove-view-locator", "false",
        "--main-view-page-type", "None")
}
elseif ($Toolkit -eq "CommunityToolkit") {
    $arguments += @(
        "--mvvm", "CommunityToolkit",
        "--di", "None",
        "--cpm", "true",
        "--remove-view-locator", "false",
        "--main-view-page-type", "ContentPage")
}
else {
    $arguments += @(
        "--mvvm", "ReactiveUI",
        "--di", "Splat.Locator",
        "--cpm", "false",
        "--remove-view-locator", "true",
        "--main-view-page-type", "NavigationPage")
}

Invoke-DotNet @arguments
Assert-Condition (Test-Path -LiteralPath $project -PathType Leaf) (
    "The packaged template did not generate $project.")

Invoke-DotNet new nugetconfig --force --output $root
$nugetConfig = Get-ChildItem -LiteralPath $root -File |
    Where-Object { $_.Name -ieq "nuget.config" } |
    Select-Object -First 1
Assert-Condition ($null -ne $nugetConfig) (
    "dotnet new nugetconfig did not create NuGet.Config.")

Invoke-DotNet nuget add source $feedPath `
    --name AkburaPlatformVerification `
    --configfile $nugetConfig.FullName

# CPM with multiple sources needs explicit mapping. Akbura packages must be
# restored from the downloaded CI artifact, never from a published version.
$settings = [xml] (Get-Content -LiteralPath $nugetConfig.FullName -Raw)
$nugetSource = $settings.SelectSingleNode(
    "//*[local-name()='packageSources']" +
    "/*[local-name()='add' and contains(@value, 'nuget.org')]")
Assert-Condition ($null -ne $nugetSource) (
    "The isolated NuGet.Config has no nuget.org source.")

$mapping = $settings.CreateElement("packageSourceMapping")
foreach ($source in @(
    @{ Key = "AkburaPlatformVerification"; Pattern = "Akbura*" },
    @{ Key = $nugetSource.GetAttribute("key"); Pattern = "*" })) {
    $entry = $settings.CreateElement("packageSource")
    $entry.SetAttribute("key", $source.Key)
    $pattern = $settings.CreateElement("package")
    $pattern.SetAttribute("pattern", $source.Pattern)
    [void] $entry.AppendChild($pattern)
    [void] $mapping.AppendChild($entry)
}

[void] $settings.configuration.AppendChild($mapping)
$settings.Save($nugetConfig.FullName)

foreach ($configuration in @("Debug", "Release")) {
    $platformBuildArguments = @()
    if ($Platform -eq "iOS") {
        $platformBuildArguments = @(
            "--runtime", "iossimulator-arm64",
            "-p:EnableCodeSigning=false")
    }

    $restoreArguments = @(
        "restore", $project,
        "-p:Configuration=$configuration") +
        $platformBuildArguments + @(
        "--configfile", $nugetConfig.FullName,
        "--packages", $packages)
    Invoke-DotNetLogged (
        "$Platform-$Toolkit-$configuration-restore") $restoreArguments

    $graph = @(Get-PackageGraph $project)
    Assert-Condition ($graph -contains "Akbura/$Version") (
        "$Platform $configuration did not restore local Akbura $Version.")

    if ($configuration -eq "Debug") {
        Assert-Condition ($graph -contains "Akbura.Diagnostics/$Version" -and
            $graph -contains "AvaloniaUI.DiagnosticsSupport/2.2.3") (
            "$Platform Debug is missing diagnostics dependencies.")
        $buildArguments = @(
            "build", $project,
            "--configuration", "Debug",
            "--no-restore") + $platformBuildArguments
        Invoke-DotNetLogged "$Platform-$Toolkit-Debug-build" $buildArguments
    }
    else {
        Assert-Condition (!(@($graph | Where-Object {
            $_ -like "Akbura.Diagnostics/*" -or
            $_ -like "AvaloniaUI.DiagnosticsSupport/*"
        }).Count)) (
            "$Platform Release unexpectedly includes Debug diagnostics.")

        if ($Platform -eq "Browser") {
            $publishPath = Join-Path $root "browser-publish"
            Invoke-DotNetLogged "$Platform-$Toolkit-Release-publish" @(
                "publish", $project,
                "--configuration", "Release",
                "--no-restore",
                "--output", $publishPath)
            Assert-Condition (Test-Path -LiteralPath (
                Join-Path $publishPath "wwwroot/index.html") -PathType Leaf) (
                "Browser publish did not produce wwwroot/index.html.")
            Assert-Condition (Test-Path -LiteralPath (
                Join-Path $publishPath "wwwroot/_framework") -PathType Container) (
                "Browser publish did not produce the WebAssembly framework.")
        }
        else {
            $buildArguments = @(
                "build", $project,
                "--configuration", "Release",
                "--no-restore") + $platformBuildArguments
            Invoke-DotNetLogged "$Platform-$Toolkit-Release-build" $buildArguments
        }
    }
}

Write-Host (
    "Verified packaged akbura.xplat $Platform/$Toolkit host in Debug and Release.")

Remove-DirectoryWithRetry $root

foreach ($binaryLog in $createdBinaryLogs) {
    if (Test-Path -LiteralPath $binaryLog) {
        Remove-Item -LiteralPath $binaryLog -Force
    }
}
