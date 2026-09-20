param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string] $AkburaVersion,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string] $AvaloniaVersion,

    [string] $VerifyPackagePath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# Keep the application-template list in one place for CI and release packaging.
# Item templates intentionally have no version symbols.
$applicationTemplates = @("app", "app-mvvm", "xplat")
$templateRoot = Join-Path $PSScriptRoot "../src/Akbura.Templates/templates"

function Assert-TemplateVersions {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Json,

        [Parameter(Mandatory = $true)]
        [string] $Location
    )

    $configuration = $Json | ConvertFrom-Json

    foreach ($symbol in @("AkburaVersion", "AvaloniaVersion")) {
        if ($null -eq $configuration.symbols.$symbol -or
            [string]::IsNullOrWhiteSpace(
                [string] $configuration.symbols.$symbol.defaultValue)) {
            throw "$Location does not define symbols.$symbol.defaultValue."
        }
    }

    if ($configuration.symbols.AkburaVersion.defaultValue -ne
        $AkburaVersion) {
        throw "$Location has AkburaVersion '$($configuration.symbols.AkburaVersion.defaultValue)'; expected '$AkburaVersion'."
    }

    if ($configuration.symbols.AvaloniaVersion.defaultValue -ne
        $AvaloniaVersion) {
        throw "$Location has AvaloniaVersion '$($configuration.symbols.AvaloniaVersion.defaultValue)'; expected '$AvaloniaVersion'."
    }
}

if (-not [string]::IsNullOrWhiteSpace($VerifyPackagePath)) {
    $resolvedPackage = (Resolve-Path -LiteralPath $VerifyPackagePath).Path
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead(
        $resolvedPackage)

    try {
        foreach ($name in $applicationTemplates) {
            $entryPath =
                "content/templates/$name/.template.config/template.json"
            $entry = $archive.GetEntry($entryPath)

            if ($null -eq $entry) {
                throw "$resolvedPackage does not contain $entryPath."
            }

            foreach ($requiredPath in @(
                "content/templates/$name/.template.config/dotnetcli.host.json",
                "content/templates/$name/.template.config/ide.host.json",
                "content/templates/$name/README.md")) {
                if ($null -eq $archive.GetEntry($requiredPath)) {
                    throw "$resolvedPackage does not contain $requiredPath."
                }
            }

            $stream = $entry.Open()
            try {
                $reader = [System.IO.StreamReader]::new($stream)
                try {
                    Assert-TemplateVersions -Json $reader.ReadToEnd() `
                        -Location "$resolvedPackage!/$entryPath"
                }
                finally {
                    $reader.Dispose()
                }
            }
            finally {
                $stream.Dispose()
            }
        }
    }
    finally {
        $archive.Dispose()
    }

    Write-Host "Verified packaged application-template defaults: Akbura $AkburaVersion, Avalonia $AvaloniaVersion."
    return
}

foreach ($name in $applicationTemplates) {
    $path = Join-Path $templateRoot `
        "$name/.template.config/template.json"

    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Application template configuration not found: $path"
    }

    $configuration = Get-Content -LiteralPath $path -Raw |
        ConvertFrom-Json

    foreach ($symbol in @("AkburaVersion", "AvaloniaVersion")) {
        if ($null -eq $configuration.symbols.$symbol) {
            throw "$path does not define the $symbol symbol."
        }
    }

    $configuration.symbols.AkburaVersion.defaultValue =
        $AkburaVersion
    $configuration.symbols.AvaloniaVersion.defaultValue =
        $AvaloniaVersion

    # Keep CRLF even when this script runs on a Linux GitHub runner.
    $json = $configuration | ConvertTo-Json -Depth 100
    $json = $json -replace "`r?`n", "`r`n"
    [System.IO.File]::WriteAllText(
        [System.IO.Path]::GetFullPath($path),
        "$json`r`n",
        [System.Text.UTF8Encoding]::new($false))

    Assert-TemplateVersions -Json (
        Get-Content -LiteralPath $path -Raw) -Location $path
}

Write-Host "Stamped application-template defaults: Akbura $AkburaVersion, Avalonia $AvaloniaVersion."
