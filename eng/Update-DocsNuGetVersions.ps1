[CmdletBinding()]
param(
    [string]$Root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path,

    # By default, prerelease versions such as alpha, beta, and rc are considered.
    [switch]$StableOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$DocumentExtensions = @(
    ".md",
    ".mdx",
    ".markdown",
    ".txt",
    ".cshtml"
)

$IgnoredDirectories = @(
    ".git",
    ".vs",
    ".idea",
    "bin",
    "obj",
    "node_modules",
    "artifacts",
    "TestResults"
)

$VersionPattern =
    '[0-9]+\.[0-9]+\.[0-9]+' +
    '(?:\.[0-9]+)?' +
    '(?:-[0-9A-Za-z.-]+)?' +
    '(?:\+[0-9A-Za-z.-]+)?'

# Matches Akbura and package IDs such as:
# Akbura.Diagnostics
# Akbura.Templates
# Akbura.Some.Other.Package
$PackagePattern =
    'Akbura(?:\.[A-Za-z0-9_][A-Za-z0-9_-]*)*'

$VersionCache = @{}

function Test-IsIgnoredPath {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    $segments = $Path -split '[\\/]'

    foreach ($directory in $IgnoredDirectories) {
        if ($segments -contains $directory) {
            return $true
        }
    }

    return $false
}

function Get-LatestNuGetVersion {
    param(
        [Parameter(Mandatory)]
        [string]$PackageId
    )

    if ($VersionCache.ContainsKey($PackageId)) {
        return $VersionCache[$PackageId]
    }

    $normalizedId = $PackageId.ToLowerInvariant()
    $encodedId = [Uri]::EscapeDataString($normalizedId)

    $uri = "https://api.nuget.org/v3-flatcontainer/$encodedId/index.json"

    Write-Host "NuGet: $PackageId" -ForegroundColor DarkGray

    try {
        $response = Invoke-RestMethod `
            -Uri $uri `
            -Method Get
    }
    catch {
        throw "Could not query NuGet package '$PackageId': $($_.Exception.Message)"
    }

    $versions = @($response.versions)

    if ($StableOnly) {
        $versions = @(
            $versions | Where-Object {
                $_ -notmatch '-'
            }
        )
    }

    if ($versions.Count -eq 0) {
        throw "NuGet returned no usable versions for '$PackageId'."
    }

    # NuGet flat-container returns versions in version order.
    # The last entry is the newest published version.
    $latest = [string]$versions[-1]

    $VersionCache[$PackageId] = $latest

    Write-Host "  latest: $latest" -ForegroundColor Cyan

    return $latest
}

function Replace-MatchVersion {
    param(
        [Parameter(Mandatory)]
        [System.Text.RegularExpressions.Match]$Match,

        [Parameter(Mandatory)]
        [string]$NewVersion
    )

    $oldVersion = $Match.Groups["version"].Value

    if ($oldVersion -eq $NewVersion) {
        return $Match.Value
    }

    $relativeStart =
        $Match.Groups["version"].Index -
        $Match.Index

    return (
        $Match.Value.Substring(0, $relativeStart) +
        $NewVersion +
        $Match.Value.Substring(
            $relativeStart +
            $Match.Groups["version"].Length
        )
    )
}

function Replace-PackageVersion {
    param(
        [Parameter(Mandatory)]
        [System.Text.RegularExpressions.Match]$Match
    )

    $packageId = $Match.Groups["package"].Value
    $oldVersion = $Match.Groups["version"].Value

    if ([string]::IsNullOrWhiteSpace($packageId)) {
        return $Match.Value
    }

    $newVersion = Get-LatestNuGetVersion $packageId

    if ($oldVersion -eq $newVersion) {
        return $Match.Value
    }

    Write-Host (
        "    {0}: {1} -> {2}" -f
        $packageId,
        $oldVersion,
        $newVersion
    ) -ForegroundColor Yellow

    return Replace-MatchVersion `
        -Match $Match `
        -NewVersion $newVersion
}

function Replace-GroupedPackageVersion {
    param(
        [Parameter(Mandatory)]
        [System.Text.RegularExpressions.Match]$Match
    )

    $packageMatches = [regex]::Matches(
        $Match.Groups["packages"].Value,
        $PackagePattern,
        [System.Text.RegularExpressions.RegexOptions]::IgnoreCase
    )

    $packageIds = @(
        $packageMatches |
        ForEach-Object {
            $_.Value
        } |
        Select-Object -Unique
    )

    if ($packageIds.Count -eq 0) {
        return $Match.Value
    }

    $latestVersions = @(
        $packageIds |
        ForEach-Object {
            Get-LatestNuGetVersion $_
        } |
        Select-Object -Unique
    )

    if ($latestVersions.Count -ne 1) {
        Write-Warning (
            "Skipping grouped package reference because packages do not share " +
            "the same latest version: {0}" -f
            ($packageIds -join ", ")
        )

        return $Match.Value
    }

    $oldVersion = $Match.Groups["version"].Value
    $newVersion = [string]$latestVersions[0]

    if ($oldVersion -eq $newVersion) {
        return $Match.Value
    }

    Write-Host (
        "    {0}: {1} -> {2}" -f
        ($packageIds -join " + "),
        $oldVersion,
        $newVersion
    ) -ForegroundColor Yellow

    return Replace-MatchVersion `
        -Match $Match `
        -NewVersion $newVersion
}

# dotnet add package Akbura --version 12.0.4-alpha.14
# dotnet package add Akbura.Diagnostics --version 12.0.4-alpha.14
$DotnetPackageRegex = [regex]::new(
    '(?i)' +
    '\bdotnet\s+' +
    '(?:add\s+package|package\s+add)\s+' +
    '(?<package>' + $PackagePattern + ')' +
    '\s+' +
    '(?:--version|-v)\s+' +
    '(?<version>' + $VersionPattern + ')'
)

# dotnet new install Akbura.Templates::12.0.4-alpha.14
$DotnetTemplateRegex = [regex]::new(
    '(?i)' +
    '\bdotnet\s+new\s+install\s+' +
    '(?<package>' + $PackagePattern + ')' +
    '::' +
    '(?<version>' + $VersionPattern + ')'
)

# Install-Package Akbura -Version 12.0.4-alpha.14
$InstallPackageRegex = [regex]::new(
    '(?i)' +
    '\bInstall-Package\s+' +
    '(?<package>' + $PackagePattern + ')' +
    '\s+' +
    '(?:-Version|-RequiredVersion)\s+' +
    '(?<version>' + $VersionPattern + ')'
)

# <PackageReference Include="Akbura" Version="12.0.4-alpha.14" />
# <PackageVersion Include="Akbura.Diagnostics" Version="12.0.4-alpha.14" />
$PackageReferenceRegex = [regex]::new(
    '(?is)' +
    '<(?:PackageReference|PackageVersion)\b' +
    '(?=[^>]*(?:Include|Update)\s*=\s*["'']' +
    '(?<package>' + $PackagePattern + ')' +
    '["''])' +
    '[^>]*?' +
    '\bVersion\s*=\s*["'']' +
    '(?<version>' + $VersionPattern + ')' +
    '["'']' +
    '[^>]*>'
)

# <PackageReference Include="Akbura.Diagnostics">
#     <Version>12.0.4-alpha.14</Version>
# </PackageReference>
$PackageReferenceBlockRegex = [regex]::new(
    '(?is)' +
    '<PackageReference\b' +
    '(?=[^>]*(?:Include|Update)\s*=\s*["'']' +
    '(?<package>' + $PackagePattern + ')' +
    '["''])' +
    '[^>]*>' +
    '(?:(?!</PackageReference>).)*?' +
    '<Version>\s*' +
    '(?<version>' + $VersionPattern + ')' +
    '\s*</Version>' +
    '(?:(?!</PackageReference>).)*?' +
    '</PackageReference>'
)

# Grouped package references:
#
# Akbura and Akbura.Diagnostics | `12.0.4-alpha.14`
# Akbura, Akbura.Diagnostics | 12.0.4-alpha.14
# Akbura / Akbura.Templates: 12.0.4-alpha.14
# Akbura & Akbura.Diagnostics 12.0.4-alpha.14
$GroupedPackageRegex = [regex]::new(
    '(?i)' +
    '(?<packages>' +
        '\b' + $PackagePattern +
        '(?:\s*(?:,|/|&|\band\b)\s*' + $PackagePattern + ')+' +
    ')' +
    '\s*' +
    '(?:\|\s*)?' +
    '(?::\s*)?' +
    '\x60?' +
    '(?<version>' + $VersionPattern + ')' +
    '\x60?'
)

# Inline documentation references:
#
# Akbura 12.0.4-alpha.14
# Akbura `12.0.4-alpha.14`
# Akbura.Diagnostics `12.0.4-alpha.14`
#
# \x60 represents a backtick without invoking PowerShell escaping rules.
$InlinePackageRegex = [regex]::new(
    '(?i)' +
    '\b(?<package>' + $PackagePattern + ')' +
    '\s+' +
    '\x60?' +
    '(?<version>' + $VersionPattern + ')' +
    '\x60?'
)

# Template option references:
#
# --akbura-version | 12.0.4-alpha.14
# --akbura-version 12.0.4-alpha.14
#
# The package ID is implicit here and refers to the main Akbura package.
$AkburaOptionRegex = [regex]::new(
    '(?i)' +
    '(?<prefix>--akbura-version[^\r\n]*?)' +
    '(?<version>' + $VersionPattern + ')'
)

$files = Get-ChildItem `
    -LiteralPath $Root `
    -Recurse `
    -File |
    Where-Object {
        $DocumentExtensions -contains $_.Extension.ToLowerInvariant() -and
        -not (Test-IsIgnoredPath $_.FullName)
    }

Write-Host ""
Write-Host "Scanning documentation:" -ForegroundColor White
Write-Host "  $Root"
Write-Host ""

$changedFiles = @()

foreach ($file in $files) {
    $path = $file.FullName

    $original = [IO.File]::ReadAllText($path)
    $updated = $original

    # Grouped references must be processed before ordinary inline references.
    $updated = $GroupedPackageRegex.Replace(
        $updated,
        [System.Text.RegularExpressions.MatchEvaluator] {
            param($match)

            Replace-GroupedPackageVersion $match
        }
    )

    $patterns = @(
        $DotnetPackageRegex,
        $DotnetTemplateRegex,
        $InstallPackageRegex,
        $PackageReferenceRegex,
        $PackageReferenceBlockRegex,
        $InlinePackageRegex
    )

    foreach ($regex in $patterns) {
        $updated = $regex.Replace(
            $updated,
            [System.Text.RegularExpressions.MatchEvaluator] {
                param($match)

                Replace-PackageVersion $match
            }
        )
    }

    # --akbura-version does not explicitly contain a package ID,
    # so it is handled separately as the main Akbura package.
    $updated = $AkburaOptionRegex.Replace(
        $updated,
        [System.Text.RegularExpressions.MatchEvaluator] {
            param($match)

            $oldVersion = $match.Groups["version"].Value
            $newVersion = Get-LatestNuGetVersion "Akbura"

            if ($oldVersion -eq $newVersion) {
                return $match.Value
            }

            Write-Host (
                "    Akbura: {0} -> {1}" -f
                $oldVersion,
                $newVersion
            ) -ForegroundColor Yellow

            return Replace-MatchVersion `
                -Match $match `
                -NewVersion $newVersion
        }
    )

    if ($updated -eq $original) {
        continue
    }

    # Preserve documentation as UTF-8 without BOM.
    [IO.File]::WriteAllText(
        $path,
        $updated,
        [Text.UTF8Encoding]::new($false)
    )

    $relativePath = [IO.Path]::GetRelativePath(
        $Root,
        $path
    )

    $changedFiles += $relativePath

    Write-Host "Updated: $relativePath" -ForegroundColor Green
}

Write-Host ""

if ($changedFiles.Count -eq 0) {
    Write-Host "Documentation is already up to date." -ForegroundColor Green
}
else {
    Write-Host (
        "Updated {0} documentation file(s)." -f
        $changedFiles.Count
    ) -ForegroundColor Green

    Write-Host ""
    Write-Host "Changed files:" -ForegroundColor White

    foreach ($file in $changedFiles) {
        Write-Host "  $file"
    }

    Write-Host ""
    Write-Host "Review with:" -ForegroundColor DarkGray
    Write-Host "  git diff" -ForegroundColor DarkGray
}

Write-Host ""
Write-Host "No commits or pushes were performed." -ForegroundColor DarkGray
