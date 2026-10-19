#requires -Version 7.2
param(
    [Parameter(Mandatory = $true)][string]$TracePath,
    [Parameter(Mandatory = $true)][string]$ResultDirectory
)

$ErrorActionPreference = 'Stop'
[Globalization.CultureInfo]::CurrentCulture = [Globalization.CultureInfo]::InvariantCulture
$null = New-Item -ItemType Directory -Path $ResultDirectory -Force
$rows = @([IO.File]::ReadLines([IO.Path]::GetFullPath($TracePath)) | ForEach-Object { $_ | ConvertFrom-Json })
$samples = @($rows | Where-Object Kind -eq 'collection-sample')
if (-not $samples.Count) {
    throw 'No collection samples were recorded.'
}

function Median($values) {
    $ordered = @($values | ForEach-Object { [double]$_ } | Sort-Object)
    if ($ordered.Count % 2) {
        return $ordered[[int][Math]::Floor($ordered.Count / 2)]
    }
    return ($ordered[$ordered.Count / 2 - 1] + $ordered[$ordered.Count / 2]) / 2
}

$summary = @($samples | Group-Object Operation,Variant,Size,Duplicates | ForEach-Object {
    $group = $_.Group
    if ($group.Count -ne 8 -or @($group.Sample | Sort-Object -Unique).Count -ne 8) {
        throw "Incomplete microbenchmark cell: $($_.Name)"
    }
    [pscustomobject][ordered]@{
        Operation = $group[0].Operation; Variant = $group[0].Variant; Size = $group[0].Size; Duplicates = $group[0].Duplicates
        Samples = $group.Count; Iterations = $group[0].Iterations
        MedianNanoseconds = Median $group.NanosecondsPerOperation
        MedianBytes = Median $group.BytesPerOperation
        Gen0 = ($group.Gen0 | Measure-Object -Sum).Sum; Gen1 = ($group.Gen1 | Measure-Object -Sum).Sum
    }
} | Sort-Object Operation,Size,Duplicates,Variant)
$encoding = [Text.UTF8Encoding]::new($false)
[IO.File]::WriteAllText((Join-Path $ResultDirectory 'collections-summary.csv'), (($summary | ConvertTo-Csv -NoTypeInformation) -join "`r`n") + "`r`n", $encoding)
[IO.File]::WriteAllText((Join-Path $ResultDirectory 'collections-retention.json'), (ConvertTo-Json -InputObject @($rows | Where-Object Kind -eq 'collection-retention') -Depth 5) + "`r`n", $encoding)
$environment = @($rows | Where-Object Kind -eq 'environment')
[IO.File]::WriteAllText((Join-Path $ResultDirectory 'collections-environment.json'), (ConvertTo-Json -InputObject $environment -Depth 5) + "`r`n", $encoding)
[IO.File]::WriteAllText((Join-Path $ResultDirectory 'collections-provenance.json'), (ConvertTo-Json -InputObject @($rows | Where-Object Kind -eq 'collection-provenance') -Depth 5) + "`r`n", $encoding)
$destination = [IO.Path]::GetFullPath((Join-Path $ResultDirectory 'collections.jsonl'))
if ($destination -ne [IO.Path]::GetFullPath($TracePath)) {
    Copy-Item -LiteralPath $TracePath -Destination $destination
}
$lines = [Collections.Generic.List[string]]::new()
$lines.Add('# Collection microbenchmarks')
$lines.Add('')
$lines.Add('Raw samples, all sizes and collection-retention observations are archived alongside this report. Input creation, delegates, correctness assertions and serialization are outside the timed loops. Each cell has eight batches of repeated operations after 256 warm-up operations. GC columns are batch totals, not normalized collections per operation.')
$lines.Add('')
$lines.Add('The LINQ controls reproduce the original collection pipelines. Repeated collector Detach includes its lock and existing completeness flags; the five-array LINQ control excludes that lock and is not an exact old-collector timing. Candidate tests start with precomputed identity strings, so symbol display/identity construction is deliberately excluded. Diagnostic tests isolate key deduplication and immutable result construction, not Roslyn diagnostic queries or message formatting.')
$lines.Add('')
$lines.Add('| Operation | Variant | Size | High duplicates | Median ns/op | Bytes/op | Gen0 / Gen1 |')
$lines.Add('| --- | --- | --- | --- | --- | --- | --- |')
foreach ($row in $summary) {
    $lines.Add(('| {0} | {1} | {2} | {3} | {4:F2} | {5:F2} | {6} / {7} |' -f $row.Operation, $row.Variant, $row.Size,
        $row.Duplicates, $row.MedianNanoseconds, $row.MedianBytes, $row.Gen0, $row.Gen1))
}
[IO.File]::WriteAllText((Join-Path $ResultDirectory 'README.md'), ($lines -join "`r`n") + "`r`n", $encoding)
Write-Host "$($samples.Count) batches; $($summary.Count) complete cells summarized."
