$ErrorActionPreference = "Stop"

function Assert-ExactVersion {
    param(
        [Parameter(Mandatory)]
        [string] $Actual,

        [Parameter(Mandatory)]
        [string] $Expected,

        [Parameter(Mandatory)]
        [string] $Source
    )

    if (![string]::Equals(
            $Actual,
            $Expected,
            [StringComparison]::Ordinal))
    {
        throw "$Source version '$Actual' must match expected version '$Expected'."
    }
}

function Assert-CompatibleVsixVersion {
    param(
        [Parameter(Mandatory)]
        [string] $Actual,

        [Parameter(Mandatory)]
        [string] $AkburaVersion
    )

    $escapedVersion = [Regex]::Escape($AkburaVersion)
    if ($Actual -cnotmatch "^$escapedVersion(?:\.[1-9][0-9]*)?$") {
        throw (
            "Visual Studio VSIX version '$Actual' must match Akbura version " +
            "'$AkburaVersion' or add a positive extension revision."
        )
    }
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$propertiesPath = Join-Path $repositoryRoot "Directory.Build.props"
$vsixManifestPath = Join-Path $repositoryRoot (
    "src/Workspaces/Akbura.VisualStudio.Vsix/" +
    "source.extension.vsixmanifest")
$vscodeDirectory = Join-Path $repositoryRoot (
    "src/Workspaces/Akbura.LanguageServer.VsCode")

[xml] $properties = Get-Content -LiteralPath $propertiesPath -Raw
$avaloniaVersion = [string] (
    $properties.Project.PropertyGroup.AvaloniaVersion |
        Select-Object -First 1)
$akburaVersion = [string] (
    $properties.Project.PropertyGroup.AkburaVersion |
        Select-Object -First 1)

$vscodeVersion = [string] (
    $properties.Project.PropertyGroup.VsCodeExtensionVersion |
        Select-Object -First 1)

if ($vscodeVersion -cnotmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') {
    throw "VsCodeExtensionVersion must be a three-part version X.Y.Z."
}

if ([string]::IsNullOrWhiteSpace($avaloniaVersion)) {
    throw "Directory.Build.props does not define AvaloniaVersion."
}

if ($akburaVersion -cne '$(AvaloniaVersion)') {
    throw "AkburaVersion must remain bound to `$(AvaloniaVersion)."
}

[xml] $vsixManifest = Get-Content -LiteralPath $vsixManifestPath -Raw
$vsixVersion = [string] (
    $vsixManifest.PackageManifest.Metadata.Identity.Version)
$package = Get-Content -LiteralPath (
    Join-Path $vscodeDirectory "package.json") -Raw |
    ConvertFrom-Json
$packageLock = Get-Content -LiteralPath (
    Join-Path $vscodeDirectory "package-lock.json") -Raw |
    ConvertFrom-Json -AsHashtable
$packageVersion = [string] $package.version
$packageLockVersion = [string] $packageLock["version"]
$packageLockRootVersion = [string] $packageLock["packages"][""]["version"]

Assert-CompatibleVsixVersion $vsixVersion $avaloniaVersion
Assert-ExactVersion $packageVersion $vscodeVersion "VS Code package.json"
Assert-ExactVersion $packageLockVersion $vscodeVersion "VS Code package-lock.json"
Assert-ExactVersion `
    $packageLockRootVersion `
    $vscodeVersion `
    "VS Code package-lock.json root package"

Write-Host (
    "Verified versions: Akbura/Avalonia $avaloniaVersion; VS Code $vscodeVersion; " +
    "Visual Studio VSIX is $vsixVersion.")
