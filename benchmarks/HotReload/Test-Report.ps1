param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../../bin/HotReloadReportTests')
)

$ErrorActionPreference = 'Stop'
$archive = Join-Path $PSScriptRoot 'results/2026-10-16'
$testRoot = Join-Path ([IO.Path]::GetFullPath($OutputDirectory)) ([Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $testRoot -Force
$passed = 0

function Assert([bool]$Condition, [string]$Message) {
    if (-not $Condition) {
        throw $Message
    }
}

function Case([string]$Name) {
    $path = Join-Path $testRoot $Name
    $null = New-Item -ItemType Directory -Path $path
    foreach ($file in Get-ChildItem -LiteralPath $archive -File | Where-Object Extension -in @('.csv', '.json')) {
        Copy-Item -LiteralPath $file.FullName -Destination $path
    }
    return $path
}

function Write-Text([string]$Path, [string]$Value) {
    [IO.File]::WriteAllText($Path, ($Value -replace '\r?\n', "`r`n"), [Text.UTF8Encoding]::new($false))
}

function Manual([string]$Value) {
    return [regex]::Replace($Value, '(?s)<!-- BEGIN GENERATED: ([a-z-]+) -->.*?<!-- END GENERATED: \1 -->', '<!-- GENERATED -->')
}

function Update([string]$Path) {
    & (Join-Path $PSScriptRoot 'Update-Report.ps1') -ResultDirectory $Path
}

$path = Case 'archived-report'
$readme = Join-Path $path 'README.md'
$before = "Manual introduction must survive.`r`n" + [IO.File]::ReadAllText((Join-Path $archive 'README.md'))
Write-Text $readme $before
Update $path
$after = [IO.File]::ReadAllText($readme)
Assert ((Manual $before) -eq (Manual $after)) 'Manual sections changed.'
Assert ($after.Contains('| NeedHelp | 21.424 | 12.810 | 8 | 1.249 |')) 'Watch values changed.'
Assert ($after.Contains('| property-500 | 8 | 859.44 | 239.36 | 154.25 | 44.36 | 1295.20 | 1516.34 |')) 'CLR values changed.'
Assert ($after.Contains('| gallery-rebuild | 1 | 70.789 |')) 'Build values changed.'
Assert ($after.Contains('Primary observations: 407. Graph observations: 33. Fresh-driver parity: 19/19 passed.')) 'Coverage changed.'
Update $path
Assert ($after -ceq [IO.File]::ReadAllText($readme)) 'Second update was not byte-stable.'
Assert (-not ($after -match '(?<!\r)\n')) 'Report contains bare LF.'
Assert ($after.EndsWith("`r`n")) 'Final CRLF missing.'
$passed++

$originalCulture = [Threading.Thread]::CurrentThread.CurrentCulture
try {
    [Threading.Thread]::CurrentThread.CurrentCulture = [Globalization.CultureInfo]::GetCultureInfo('de-DE')
    Update $path
    Assert ($after -ceq [IO.File]::ReadAllText($readme)) 'Report changed under a decimal-comma culture.'
}
finally {
    [Threading.Thread]::CurrentThread.CurrentCulture = $originalCulture
}
$passed++

$path = Case 'new-report'
Update $path
$value = [IO.File]::ReadAllText((Join-Path $path 'README.md'))
Assert ($value.Contains('Describe the machine') -and $value.Contains('Write conclusions manually.')) 'Manual placeholders missing.'
Assert ($value.Contains('No tutorial revision metadata was recorded')) 'Missing provenance invented.'
$passed++

$path = Case 'watch-only'
Import-Csv (Join-Path $path 'samples.csv') | Where-Object Kind -eq 'watch' |
    Export-Csv -LiteralPath (Join-Path $path 'filtered.csv') -NoTypeInformation -Encoding UTF8
Copy-Item -LiteralPath (Join-Path $path 'filtered.csv') -Destination (Join-Path $path 'samples.csv') -Force
Write-Text (Join-Path $path 'watch-events.csv') ''
Write-Text (Join-Path $path 'graph.csv') ''
Update $path
$value = [IO.File]::ReadAllText((Join-Path $path 'README.md'))
$missing = [string][char]0x2014
Assert ($value.Contains("| NeedHelp | 0 | $missing | $missing |")) 'Missing send boundary was reported as zero.'
Assert ($value.Contains('_No measurements recorded for this section._')) 'Missing suites invented.'
$passed++

$path = Case 'no-task-evidence'
Write-Text (Join-Path $path 'build-tasks.csv') ''
Update $path
$value = [IO.File]::ReadAllText((Join-Path $path 'README.md'))
Assert ($value.Contains("70.789 | $missing | $missing |")) 'Missing build tasks were reported as zero.'
$passed++

$path = Case 'graph-variation'
$graph = @(Import-Csv (Join-Path $path 'graph.csv'))
$changed = @($graph | Where-Object { $_.Scenario -eq 'Counter.akbura:scalar' -and $_.Warmup -eq 'False' })[0]
$changed.RecomputeComponents = '2'
$changed.ReuseComponents = '26'
$graph | Export-Csv -LiteralPath (Join-Path $path 'graph.csv') -NoTypeInformation -Encoding UTF8
Update $path
$value = [IO.File]::ReadAllText((Join-Path $path 'README.md'))
$range = [string][char]0x2013
Assert ($value.Contains("| Counter.akbura:scalar | 8 | 28 | 1${range}2 | 26${range}27 |")) 'Variable graph counts were hidden.'
$passed++

$path = Case 'escaped-cells'
foreach ($name in @('samples.csv', 'summary.csv')) {
    $records = @(Import-Csv (Join-Path $path $name))
    foreach ($record in $records | Where-Object { $_.Kind -eq 'watch' -and $_.Scenario -eq 'Fixture' }) {
        $record.Scenario = 'Fixture | <sample>'
    }
    $records | Export-Csv -LiteralPath (Join-Path $path $name) -NoTypeInformation -Encoding UTF8
}
Update $path
$value = [IO.File]::ReadAllText((Join-Path $path 'README.md'))
Assert ($value.Contains('Fixture &#124; &lt;sample&gt;')) 'Markdown cells were not escaped.'
$passed++

$invalid = @(
    'Manual README without markers.',
    '<!-- BEGIN GENERATED: watch -->',
    '<!-- END GENERATED: watch --><!-- BEGIN GENERATED: watch -->',
    '<!-- BEGIN GENERATED: watch --><!-- BEGIN GENERATED: runtime --><!-- END GENERATED: runtime --><!-- END GENERATED: watch -->',
    '<!-- BEGIN GENERATED: watch --><!-- END GENERATED: watch --><!-- BEGIN GENERATED: watch --><!-- END GENERATED: watch -->',
    '<!-- BEGIN GENERATED: unknown --><!-- END GENERATED: unknown -->'
)
for ($index = 0; $index -lt $invalid.Count; $index++) {
    $path = Case "invalid-markers-$index"
    $readme = Join-Path $path 'README.md'
    Write-Text $readme $invalid[$index]
    $originalBytes = [Convert]::ToBase64String([IO.File]::ReadAllBytes($readme))
    $failed = $false
    try {
        Update $path
    }
    catch {
        $failed = $true
    }
    Assert $failed "Invalid marker case $index was accepted."
    Assert ($originalBytes -ceq [Convert]::ToBase64String([IO.File]::ReadAllBytes($readme))) 'Invalid report was overwritten.'
    Assert (@(Get-ChildItem -LiteralPath $path -Filter '*.tmp').Count -eq 0) 'Temporary report leaked.'
    $passed++
}

$path = Case 'stale-summary'
$summary = @(Import-Csv (Join-Path $path 'summary.csv'))
@($summary | Where-Object { $_.Kind -eq 'watch' -and $_.Scenario -eq 'Fixture' })[0].Count = '7'
$summary | Export-Csv -LiteralPath (Join-Path $path 'summary.csv') -NoTypeInformation -Encoding UTF8
$failed = $false
try {
    Update $path
}
catch {
    $failed = $true
}
Assert $failed 'Stale summary accepted.'
Assert (-not (Test-Path -LiteralPath (Join-Path $path 'README.md'))) 'Stale summary created a report.'
$passed++

$path = Case 'graph-only'
Write-Text (Join-Path $path 'samples.csv') ''
Write-Text (Join-Path $path 'summary.csv') ''
Import-Csv (Join-Path $path 'validation.csv') | Where-Object { $_.Log -like 'graph-*' } |
    Export-Csv -LiteralPath (Join-Path $path 'filtered.csv') -NoTypeInformation -Encoding UTF8
Copy-Item -LiteralPath (Join-Path $path 'filtered.csv') -Destination (Join-Path $path 'validation.csv') -Force
Update $path
$value = [IO.File]::ReadAllText((Join-Path $path 'README.md'))
Assert ($value.Contains('Primary observations: 0. Graph observations: 33. Fresh-driver parity: 3/3 passed.')) 'Graph-only archive was not rendered.'
$passed++

$inputPath = Join-Path $testRoot 'raw-input'
$outputPath = Join-Path $testRoot 'summarized'
$null = New-Item -ItemType Directory -Path $inputPath
$items = @(
    @{ Kind = 'watch'; Scenario = 'Fixture'; Index = 1; Warmup = $true; TotalMs = 1700 },
    @{ Kind = 'watch'; Scenario = 'Fixture'; Index = 2; Warmup = $false; TotalMs = 100 },
    @{ Kind = 'watch'; Scenario = 'Fixture'; Index = 3; Warmup = $false; TotalMs = 200 }
)
$json = @($items | ForEach-Object { $_ | ConvertTo-Json -Compress }) -join "`r`n"
Write-Text (Join-Path $inputPath 'watch.jsonl') $json
Write-Text (Join-Path $inputPath 'purity-source.json') (@{ Repository = 'https://example.invalid/tutorial.git'; Revision = 'abc123'; PreparedRoot = 'C:/private/snapshot' } | ConvertTo-Json)
& (Join-Path $PSScriptRoot 'Summarize.ps1') -InputDirectory $inputPath -OutputDirectory $outputPath
$value = [IO.File]::ReadAllText((Join-Path $outputPath 'README.md'))
Assert ($value.Contains("| Fixture | $missing | 1.700 | 2 | 0.150 | 0.100${range}0.200 | 0.200 |")) 'Summarize did not generate a valid report.'
Assert ($value.Contains('abc123') -and -not $value.Contains('C:/private/snapshot')) 'Portable provenance was lost or leaked a machine path.'
$passed++

$inputPath = Join-Path $testRoot 'raw-graph'
$outputPath = Join-Path $testRoot 'summarized-graph'
$null = New-Item -ItemType Directory -Path $inputPath
$item = @{ Kind = 'graph'; Scenario = 'Counter.akbura:scalar'; Index = 1; Warmup = $false; Changed = @('Counter.g.cs'); Requests = @{
    Components = @{ Total = 16; Recompute = 1; Reuse = 15 }; Diagnostics = @{ Recompute = 1 }
    ExternalAkcss = @{ Recompute = 0 }; InlineAkcss = @{ Recompute = 0 }
} }
Write-Text (Join-Path $inputPath 'graph.jsonl') ($item | ConvertTo-Json -Depth 8 -Compress)
& (Join-Path $PSScriptRoot 'Summarize.ps1') -InputDirectory $inputPath -OutputDirectory $outputPath
$value = [IO.File]::ReadAllText((Join-Path $outputPath 'README.md'))
Assert ($value.Contains('Primary observations: 0. Graph observations: 1.')) 'Graph-only raw logs were not summarized.'
$passed++

Write-Host "$passed report checks passed. Fixtures: $testRoot"
