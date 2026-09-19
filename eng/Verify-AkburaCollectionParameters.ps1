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
        'FullyQualifiedName~CollectionParameter|FullyQualifiedName~ParameterWriterTests|FullyQualifiedName~ComponentMemberIntegrationTests|FullyQualifiedName~ForeachRegionRuntimeTests')
    Invoke-DotNet -Arguments @('test', $workspace, '-c', 'Debug', '--filter',
        'FullyQualifiedName~WorkspaceCollectionParameterTests|FullyQualifiedName~Documentation')
    if ($Full) {
        Invoke-DotNet -Arguments @('test', $unit, '-c', 'Debug')
        Invoke-DotNet -Arguments @('test', $workspace, '-c', 'Debug')
        Invoke-DotNet -Arguments @('test',
            'src/Workspaces/Akbura.LanguageServer.UnitTests/Akbura.LanguageServer.UnitTests.csproj', '-c', 'Debug')
    }
    if ($BuildVisualStudio) {
        if (-not $IsWindows) { throw 'Building the native Visual Studio extension requires Windows.' }
        Invoke-DotNet -Arguments @('build',
            'src/Workspaces/Akbura.VisualStudio.Vsix/Akbura.VisualStudio.Vsix.csproj', '-c', 'Debug', '-t:Rebuild')
    }
}
finally { Pop-Location }
