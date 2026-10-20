#requires -Version 7.2
param(
    [Parameter(Mandatory = $true)][string]$ToolDirectory,
    [Parameter(Mandatory = $true)][string]$InputDirectory,
    [Parameter(Mandatory = $true)][string]$OutputPath,
    [string]$CellOverrideFile = ''
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'BenchmarkCellOverrides.ps1')
$ToolDirectory = [IO.Path]::GetFullPath($ToolDirectory)
$InputDirectory = [IO.Path]::GetFullPath($InputDirectory)
$components = @('gallery:Counter.akbura','gallery:DataTemplatesPage.akbura','purity:NeedHelp.akbura','purity:Sidebar.akbura')
$scenarios = @('localized-literal','localized-property','localized-akcss','localized-binding-sibling','localized-expression',
    'localized-property-add-remove','localized-structure','localized-name','localized-diagnostic','localized-trivia')
$overrides = if ($CellOverrideFile) { @((Get-Content -LiteralPath $CellOverrideFile -Raw | ConvertFrom-Json).Overrides) } else { @() }
$map = Get-BenchmarkCellOverrides $components $scenarios $overrides

# Invoke only our frozen host's pure input-construction helper. An isolated load
# context avoids PowerShell's own Roslyn version and does not run the generator.
$context = [Runtime.Loader.AssemblyLoadContext]::new(('LocalizedInputs-' + [Guid]::NewGuid().ToString('N')), $true)
$resolver = [Func[Runtime.Loader.AssemblyLoadContext, Reflection.AssemblyName, Reflection.Assembly]]({
    param($loader, $name)
    $path = [IO.Path]::Combine($ToolDirectory, $name.Name + '.dll')
    if ([IO.File]::Exists($path)) { return $loader.LoadFromAssemblyPath($path) }
    return $null
}.GetNewClosure())
$context.add_Resolving($resolver)
try {
    foreach ($name in @('Microsoft.CodeAnalysis','Microsoft.CodeAnalysis.CSharp')) {
        $null = $context.LoadFromAssemblyPath((Join-Path $ToolDirectory ($name + '.dll')))
    }
    $assembly = $context.LoadFromAssemblyPath((Join-Path $ToolDirectory 'HotReloadBenchmarks.dll'))
    $method = $assembly.GetType('Program').GetMethod('LocalizedSource', [Reflection.BindingFlags]'NonPublic,Static')
    if (-not $method) { throw 'The frozen host has no localized input constructor.' }
    $records = @(foreach ($component in $components) {
        $workload,$requested = $component.Split(':')
        $evaluated = Get-Content -LiteralPath (Join-Path $InputDirectory ($workload + '-inputs.json')) -Raw | ConvertFrom-Json
        foreach ($scenario in $scenarios) {
            $key = $component + '/' + $scenario
            $file = if ($map.ContainsKey($key)) { $map[$key] } else { $requested }
            $selectedPaths = @($evaluated.Items.AdditionalFiles | Where-Object { [IO.Path]::GetFileName($_.FullPath) -ceq $file } |
                Select-Object -ExpandProperty FullPath -Unique)
            if ($selectedPaths.Count -ne 1) { throw "Expected one actual component for $key -> $file" }
            $path = $selectedPaths[0]
            $source = [IO.File]::ReadAllText($path)
            $digests = @(foreach ($index in 0..10) {
                try { $changed = [string]$method.Invoke($null, [object[]]@([string]$source,[string]$path,[string]$scenario,[int]$index)) }
                catch { throw "Input construction failed for $key -> $file at revision ${index}: $($_.Exception.Message)" }
                [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($changed)))
            })
            if (@($digests | Sort-Object -Unique).Count -lt 2) { throw "The scenario did not edit its input: $key" }
            [pscustomobject]@{ Workload = $workload; RequestedFile = $requested; File = $file; Scenario = $scenario
                SourceSha256 = (Get-FileHash -LiteralPath $path).Hash; Revisions = 11; InputDigests = $digests }
        }
    })
    [IO.File]::WriteAllText([IO.Path]::GetFullPath($OutputPath), (@{ Cells = $records; Checks = $records.Count * 11
        HostSha256 = (Get-FileHash -LiteralPath (Join-Path $ToolDirectory 'HotReloadBenchmarks.dll')).Hash
        Boundary = 'Input construction only; semantic and generated-output parity are checked separately.' } |
        ConvertTo-Json -Depth 6) + "`r`n", [Text.UTF8Encoding]::new($false))
    Write-Host "$($records.Count * 11) localized input-construction checks passed ($($records.Count) cells)."
}
finally {
    $context.remove_Resolving($resolver)
    $context.Unload()
}
