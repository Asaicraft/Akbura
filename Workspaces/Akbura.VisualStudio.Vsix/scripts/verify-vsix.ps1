[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $VsixPath,

    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+$')]
    [string] $ExpectedVersion,

    [string] $PublishManifestPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$expectedVsixId =
    'Akbura.VisualStudio.Vsix.09fb6c7e-e90e-4536-ac90-4c71949913da'
$expectedDisplayName = 'Akbura Visual Studio Extension'
$expectedPublisher = 'Asaicraft'
$expectedTargetIds = @(
    'Microsoft.VisualStudio.Community'
    'Microsoft.VisualStudio.Enterprise'
    'Microsoft.VisualStudio.Pro'
)
$requiredEntries = @(
    'Akbura.VisualStudio.dll'
    'Akbura.VisualStudio.Vsix.dll'
    'Akbura.VisualStudio.Vsix.pkgdef'
    'AkburaFileIcons.pkgdef'
)

function Assert-Exact {
    param(
        [AllowNull()]
        [object] $Actual,

        [AllowNull()]
        [object] $Expected,

        [Parameter(Mandatory)]
        [string] $Label
    )

    if ([string] $Actual -cne [string] $Expected) {
        throw "Unexpected ${Label}: '$Actual'; expected '$Expected'."
    }
}

$resolvedVsixPath = (Resolve-Path -LiteralPath $VsixPath).Path
Add-Type -AssemblyName System.IO.Compression.FileSystem

$archive = [System.IO.Compression.ZipFile]::OpenRead($resolvedVsixPath)
try {
    $manifestEntry = $archive.GetEntry('extension.vsixmanifest')
    if ($null -eq $manifestEntry) {
        throw 'VSIX does not contain extension.vsixmanifest.'
    }

    $reader = [System.IO.StreamReader]::new($manifestEntry.Open())
    try {
        [xml] $manifest = $reader.ReadToEnd()
    }
    finally {
        $reader.Dispose()
    }

    $entryNames = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::OrdinalIgnoreCase
    )
    foreach ($entry in $archive.Entries) {
        [void] $entryNames.Add($entry.FullName)
    }

    foreach ($requiredEntry in $requiredEntries) {
        if (-not $entryNames.Contains($requiredEntry)) {
            throw "VSIX does not contain required entry '$requiredEntry'."
        }
    }
}
finally {
    $archive.Dispose()
}

$namespaceManager = [System.Xml.XmlNamespaceManager]::new($manifest.NameTable)
$namespaceManager.AddNamespace('vs', $manifest.DocumentElement.NamespaceURI)

$identity = $manifest.SelectSingleNode(
    '/vs:PackageManifest/vs:Metadata/vs:Identity',
    $namespaceManager
)
if ($null -eq $identity) {
    throw 'VSIX manifest does not contain Metadata/Identity.'
}

Assert-Exact $identity.GetAttribute('Id') $expectedVsixId 'VSIX ID'
Assert-Exact $identity.GetAttribute('Version') $ExpectedVersion 'VSIX version'
Assert-Exact $identity.GetAttribute('Language') 'en-US' 'VSIX language'
Assert-Exact $identity.GetAttribute('Publisher') $expectedPublisher `
    'VSIX publisher'

$displayName = $manifest.SelectSingleNode(
    '/vs:PackageManifest/vs:Metadata/vs:DisplayName',
    $namespaceManager
)
if ($null -eq $displayName) {
    throw 'VSIX manifest does not contain Metadata/DisplayName.'
}
Assert-Exact $displayName.InnerText.Trim() $expectedDisplayName `
    'VSIX display name'

$targets = @($manifest.SelectNodes(
    '/vs:PackageManifest/vs:Installation/vs:InstallationTarget',
    $namespaceManager
))
$actualTargetIds = @(
    $targets |
        ForEach-Object { $_.GetAttribute('Id') } |
        Sort-Object
)
Assert-Exact ($actualTargetIds -join ',') ($expectedTargetIds -join ',') `
    'Visual Studio installation targets'

foreach ($target in $targets) {
    Assert-Exact $target.GetAttribute('Version') '[17.14,)' `
        "version range for $($target.GetAttribute('Id'))"

    $architecture = $target.SelectSingleNode(
        'vs:ProductArchitecture',
        $namespaceManager
    )
    if ($null -eq $architecture) {
        throw "Installation target '$($target.GetAttribute('Id'))' has no architecture."
    }
    Assert-Exact $architecture.InnerText.Trim() 'amd64' `
        "architecture for $($target.GetAttribute('Id'))"
}

$prerequisites = @($manifest.SelectNodes(
    '/vs:PackageManifest/vs:Prerequisites/vs:Prerequisite',
    $namespaceManager
))
if ($prerequisites.Count -ne 1) {
    throw "Unexpected prerequisite count: $($prerequisites.Count); expected 1."
}
Assert-Exact $prerequisites[0].GetAttribute('Id') `
    'Microsoft.VisualStudio.Component.CoreEditor' 'prerequisite ID'
Assert-Exact $prerequisites[0].GetAttribute('Version') '[17.14,)' `
    'prerequisite version'

if (-not [string]::IsNullOrWhiteSpace($PublishManifestPath)) {
    $resolvedPublishManifestPath =
        (Resolve-Path -LiteralPath $PublishManifestPath).Path
    $publishManifest = Get-Content -Raw -LiteralPath `
        $resolvedPublishManifestPath | ConvertFrom-Json

    $identityProperties = @(
        $publishManifest.identity.PSObject.Properties.Name
    )
    Assert-Exact ($identityProperties -join ',') 'internalName' `
        'publish identity fields'
    Assert-Exact $publishManifest.identity.internalName `
        'akbura-visual-studio-extension' 'Marketplace internal name'

    $internalName = [string] $publishManifest.identity.internalName
    if ($internalName.Length -gt 63 -or
        $internalName -notmatch '^[A-Za-z0-9][A-Za-z0-9-]*$') {
        throw "Marketplace internal name '$internalName' is invalid."
    }

    $expectedCategories = @('coding', 'programming languages')
    $actualCategories = @($publishManifest.categories | Sort-Object)
    Assert-Exact ($actualCategories -join ',') `
        (($expectedCategories | Sort-Object) -join ',') `
        'Marketplace categories'
    Assert-Exact $publishManifest.overview 'overview.md' `
        'Marketplace overview path'
    Assert-Exact $publishManifest.priceCategory 'free' `
        'Marketplace price category'
    Assert-Exact $publishManifest.publisher 'asaicraft' `
        'Marketplace publisher'
    Assert-Exact $publishManifest.private $false `
        'Marketplace private flag'
    Assert-Exact $publishManifest.qna $true 'Marketplace Q&A flag'
    Assert-Exact $publishManifest.repo `
        'https://github.com/Asaicraft/Akbura' 'Marketplace repository'

    $overviewPath = Join-Path `
        (Split-Path -Parent $resolvedPublishManifestPath) `
        ([string] $publishManifest.overview)
    if (-not (Test-Path -LiteralPath $overviewPath -PathType Leaf)) {
        throw "Marketplace overview does not exist: '$overviewPath'."
    }
    if ((Get-Item -LiteralPath $overviewPath).Length -eq 0) {
        throw "Marketplace overview is empty: '$overviewPath'."
    }
}

$hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $resolvedVsixPath).Hash
Write-Host (
    "Verified Visual Studio VSIX: {0}, {1}, version {2}." -f `
        $expectedVsixId,
        $expectedDisplayName,
        $ExpectedVersion
)
Write-Host "SHA256: $($hash.ToLowerInvariant())"
