[CmdletBinding()]
param(
    [switch] $Full,
    [switch] $BuildVisualStudio
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

function Invoke-DotNet {
    param([Parameter(Mandatory)][string[]] $Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

Push-Location (Split-Path -Parent $PSScriptRoot)
try {
    & git diff --check
    if ($LASTEXITCODE -ne 0) { throw 'git diff --check failed.' }

    $unit = 'src/Akbura.UnitTests/Akbura.UnitTests.csproj'
    $workspace = 'src/Workspaces/Akbura.Workspaces.UnitTests/Akbura.Workspaces.UnitTests.csproj'
    Invoke-DotNet -Arguments @('test', $unit, '-c', 'Debug', '--filter',
        'FullyQualifiedName~ResourceHooksTests|FullyQualifiedName~ExecutableScopeResourceIntegrationTests')
    Invoke-DotNet -Arguments @('test', $workspace, '-c', 'Debug', '--filter',
        'FullyQualifiedName~WorkspaceNativeBracePointTests|FullyQualifiedName~WorkspaceTagDelimiterClassificationTests|FullyQualifiedName~WorkspaceReferencesRenameTests|FullyQualifiedName~WorkspaceMarkupForeachEditingTests')

    if ($Full) {
        Invoke-DotNet -Arguments @('test', $unit, '-c', 'Debug')
        Invoke-DotNet -Arguments @('test', $workspace, '-c', 'Debug')
        Invoke-DotNet -Arguments @('test',
            'src/Workspaces/Akbura.LanguageServer.UnitTests/Akbura.LanguageServer.UnitTests.csproj', '-c', 'Debug')
        Invoke-DotNet -Arguments @('test',
            'src/Workspaces/Akbura.LanguageServer.IntegrationTests/Akbura.LanguageServer.IntegrationTests.csproj', '-c', 'Debug')
    }

    if ($BuildVisualStudio) {
        if (-not $IsWindows) { throw 'The native Visual Studio build must run on Windows.' }
        Invoke-DotNet -Arguments @('build',
            'src/Workspaces/Akbura.VisualStudio.Vsix/Akbura.VisualStudio.Vsix.csproj', '-c', 'Debug', '-t:Rebuild')
        Write-Host 'Build completed. Native VS interaction checks are still required; see the contributor guide.'
    }
}
finally {
    Pop-Location
}
