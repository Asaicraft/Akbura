#requires -Version 7.0
[CmdletBinding()]
param(
    [switch] $Full,
    [switch] $BuildVisualStudio,
    [switch] $PackLocal,
    [string] $LocalPackageVersion,
    [string] $PackageOutput = (Join-Path ([System.IO.Path]::GetTempPath()) 'AkburaResourceRegressions/feed')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path $PSScriptRoot -Parent

function Invoke-DotNet {
    param([Parameter(Mandatory)][string[]] $Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed (exit $LASTEXITCODE)."
    }
}

Push-Location $root
try {
    Invoke-DotNet -Arguments @('test', 'src/Akbura.UnitTests/Akbura.UnitTests.csproj',
        '-c', 'Debug', '--filter',
        'FullyQualifiedName~QuotedResourceRegressionTests|FullyQualifiedName~ResourceOutVariableRegressionTests|FullyQualifiedName~ExecutableScopeResourceIntegrationTests')
    Invoke-DotNet -Arguments @('test', 'src/Workspaces/Akbura.Workspaces.UnitTests/Akbura.Workspaces.UnitTests.csproj',
        '-c', 'Debug', '--filter', 'FullyQualifiedName~WorkspaceQuotedResourceRegressionTests')

    if ($Full) {
        Invoke-DotNet -Arguments @('test', 'src/Akbura.UnitTests/Akbura.UnitTests.csproj', '-c', 'Debug')
        Invoke-DotNet -Arguments @('test', 'src/Workspaces/Akbura.Workspaces.UnitTests/Akbura.Workspaces.UnitTests.csproj', '-c', 'Debug')
        Invoke-DotNet -Arguments @('test', 'src/Workspaces/Akbura.LanguageServer.UnitTests/Akbura.LanguageServer.UnitTests.csproj', '-c', 'Debug')
    }

    if ($BuildVisualStudio) {
        if (-not $IsWindows) { throw 'The native Visual Studio extension must be built on Windows.' }
        Invoke-DotNet -Arguments @('build',
            'src/Workspaces/Akbura.VisualStudio.Vsix/Akbura.VisualStudio.Vsix.csproj', '-c', 'Debug', '-t:Rebuild')
    }

    if ($PackLocal) {
        [xml] $props = Get-Content 'Directory.Build.props' -Raw
        $versionNode = $props.SelectSingleNode('/Project/PropertyGroup/AvaloniaVersion')
        if ($null -eq $versionNode) { throw 'AvaloniaVersion was not found in Directory.Build.props.' }
        if ([string]::IsNullOrWhiteSpace($LocalPackageVersion)) {
            $LocalPackageVersion = $versionNode.InnerText + '-resourcefix.' +
                [DateTime]::UtcNow.ToString('yyyyMMddHHmmssfff')
        }
        if ($LocalPackageVersion -notmatch '^\d+\.\d+\.\d+-[0-9A-Za-z.-]+$') {
            throw 'Use a unique prerelease LocalPackageVersion, for example 12.0.4-resourcefix.20260919001.'
        }
        $feed = [System.IO.Path]::GetFullPath($PackageOutput)
        [System.IO.Directory]::CreateDirectory($feed) | Out-Null
        $package = Join-Path $feed "Akbura.$LocalPackageVersion.nupkg"
        if (Test-Path $package) {
            throw 'That local package already exists. Choose a new version instead of overwriting a cached package.'
        }
        Invoke-DotNet -Arguments @('pack', 'src/Akbura/Akbura.csproj', '-c', 'Release',
            "-p:PackageVersion=$LocalPackageVersion", '-p:UseSharedCompilation=false', '-o', $feed)
        if (-not (Test-Path $package)) { throw "Expected package was not created: $package" }

        $zip = [System.IO.Compression.ZipFile]::OpenRead($package)
        try {
            foreach ($entry in @('analyzers/dotnet/cs/Akbura.BlackSilence.dll',
                                 'analyzers/dotnet/cs/Akbura.Generator.dll',
                                 'lib/net10.0/Akbura.dll')) {
                if ($null -eq $zip.GetEntry($entry)) { throw "Package is missing $entry" }
            }
        }
        finally { $zip.Dispose() }
        $manifest = Join-Path $feed 'last-package.json'
        [ordered]@{
            version = $LocalPackageVersion
            feed = $feed
            package = $package
            sha256 = (Get-FileHash $package -Algorithm SHA256).Hash
        } | ConvertTo-Json | Set-Content $manifest -Encoding utf8
        Write-Host "Local package: $package"
        Write-Host "Package manifest: $manifest"
        Write-Host 'No package was published and no consumer project was modified.'
    }
}
finally {
    Pop-Location
}
