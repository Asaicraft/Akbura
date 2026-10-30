param(
    [string]$Output = (Join-Path $env:TEMP 'Akbura-component-probe-cache'),
    [int]$Processes = 1,
    [int]$ColdProcesses = 1,
    [int]$FocusedProcesses = 8,
    [int]$Warmups = 1,
    [int]$Iterations = 3,
    [switch]$BuildOnly
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$outputRoot = [IO.Path]::GetFullPath($Output)
# The matrix retains exact gallery sources for every scenario and variant.
# Check space before starting the long-running comparison.
$outputDrive = [IO.DriveInfo]::new([IO.Path]::GetPathRoot($outputRoot))
if ($outputDrive.AvailableFreeSpace -lt 1GB) {
    throw "Choose an Output volume with at least 1 GB free: $outputRoot"
}
$source = Join-Path $repo 'src/Akbura.Generator/Language/Binder/CSharpProbeContext.cs'
$originalBytes = [IO.File]::ReadAllBytes($source)
$original = [Text.Encoding]::UTF8.GetString($originalBytes).TrimStart([char]0xFEFF)
$generatorProject = Join-Path $repo 'src/Akbura.Generator/Akbura.Generator.csproj'
$benchmarkProject = Join-Path $PSScriptRoot 'Akbura.Benchmarks.csproj'
$benchmarkBin = Join-Path $PSScriptRoot 'bin/Release/net10.0'
$blackSilence = Join-Path $repo 'src/Akbura.BlackSilence/bin/Release/netstandard2.0/Akbura.BlackSilence.dll'
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null

function Build-Checked([string[]]$Arguments, [string]$Log) {
    & dotnet @Arguments *> $Log
    if ($LASTEXITCODE -ne 0) { throw "Build failed. See $Log" }
}

try {
    Build-Checked @('build', (Join-Path $repo 'src/Akbura.BlackSilence/Akbura.BlackSilence.csproj'), '-c', 'Release', '--no-restore', '--nologo', '-v', 'quiet') (Join-Path $outputRoot 'build-compiler.log')
    # Existing reference outputs are used so the archived compiler and the gallery
    # project are not rebuilt by this comparison script.
    Build-Checked @('build', $benchmarkProject, '-c', 'Release', '--no-restore', '-p:BuildProjectReferences=false', '--nologo', '-v', 'quiet') (Join-Path $outputRoot 'build-benchmarks.log')
    foreach ($profile in @($false, $true)) {
        $group = if ($profile) { 'profile-variants' } else { 'variants' }
        foreach ($variant in @('Dictionary', 'Cache16', 'Cache32', 'Cache64', 'Cache128')) {
            $directory = Join-Path $outputRoot "$group/$variant"
            New-Item -ItemType Directory -Path $directory -Force | Out-Null
            $text = [regex]::Replace($original,
                'private readonly (?:ConcurrentCache|ConcurrentDictionary)<string, CSharp\.CompilationUnitSyntax> _componentTemplates = new\([^;]+;',
                $(if ($variant -eq 'Dictionary') {
                    'private readonly ConcurrentDictionary<string, CSharp.CompilationUnitSyntax> _componentTemplates = new(StringComparer.Ordinal);'
                } else {
                    "private readonly ConcurrentCache<string, CSharp.CompilationUnitSyntax> _componentTemplates = new($($variant.Substring(5)), StringComparer.Ordinal);"
                }))
            $text = [regex]::Replace($text,
                '(?:_componentTemplates\.TryAdd\(fallbackTypeName, template\)|template = _componentTemplates\.GetOrAdd\(fallbackTypeName, template\));',
                $(if ($variant -eq 'Dictionary') { 'template = _componentTemplates.GetOrAdd(fallbackTypeName, template);' }
                  else { '_componentTemplates.TryAdd(fallbackTypeName, template);' }))
            if (!$text.Contains('using Akbura.Collections;')) { $text = "using Akbura.Collections;`r`n" + $text }
            if ($profile) {
                $text = $text.Replace('    private ImmutableArray<CSharp.UsingDirectiveSyntax> _defaultUsings;',
                    "    internal static Action<object, object, string, bool, object, object>? TemplateTrace;`r`n    private ImmutableArray<CSharp.UsingDirectiveSyntax> _defaultUsings;")
                $text = $text.Replace('        if (!_componentTemplates.TryGetValue(fallbackTypeName, out var template))',
                    "        var templateHit = _componentTemplates.TryGetValue(fallbackTypeName, out var template);`r`n        TemplateTrace?.Invoke(this, semanticModel, fallbackTypeName, templateHit, members, usings);`r`n        if (!templateHit)")
                if (!$text.Contains('TemplateTrace?.Invoke')) { throw 'Cannot insert profiling hook.' }
            }
            [IO.File]::WriteAllText($source, $text, [Text.UTF8Encoding]::new($false))
            $intermediate = Join-Path $directory 'obj/'
            Build-Checked @('build', $generatorProject, '-c', 'Release', '--no-restore', '--nologo', '-v', 'quiet',
                "-p:OutputPath=$directory/", "-p:IntermediateOutputPath=$intermediate", '-p:AkburaEmbedSourceFiles=false') (Join-Path $directory 'build.log')
            Copy-Item -LiteralPath $blackSilence -Destination $directory
            Copy-Item -LiteralPath (Join-Path $benchmarkBin 'Akbura.Benchmarks.dll') -Destination $directory
            Write-Host "Built $group/$variant"
        }
    }
} finally {
    [IO.File]::WriteAllBytes($source, $originalBytes)
}

if ($BuildOnly) { return }
$previousTiering = $env:DOTNET_TieredCompilation
try {
    $env:DOTNET_TieredCompilation = '0'
    for ($process = 0; $process -lt $Processes; $process++) {
        $results = Join-Path $outputRoot "process-$process"
        & dotnet (Join-Path $benchmarkBin 'Akbura.Benchmarks.dll') --component-probe-cache `
            --variants (Join-Path $outputRoot 'variants') --profile-variants (Join-Path $outputRoot 'profile-variants') `
            --output $results --warmups $Warmups --iterations $Iterations --round ($process * 2 + 1) *> (Join-Path $outputRoot "process-$process.log")
        if ($LASTEXITCODE -ne 0) { throw "Comparison process $process failed. See its log." }
        Write-Host "Completed independent process $process"
    }
    for ($process = 0; $process -lt $ColdProcesses; $process++) {
        $results = Join-Path $outputRoot "cold-$process"
        & dotnet (Join-Path $benchmarkBin 'Akbura.Benchmarks.dll') --component-probe-cache `
            --variants (Join-Path $outputRoot 'variants') --profile-variants (Join-Path $outputRoot 'profile-variants') `
            --output $results --scenario ColdGeneration --reverse --warmups $Warmups --iterations $Iterations --round ($process * 2 + 1) *> (Join-Path $outputRoot "cold-$process.log")
        if ($LASTEXITCODE -ne 0) { throw "Cold control process $process failed. See its log." }
        Write-Host "Completed independent cold control $process in reverse variant order"
    }
    for ($process = 0; $process -lt $FocusedProcesses; $process++) {
        $results = Join-Path $outputRoot "focused-$process"
        New-Item -ItemType Directory -Path $results -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $outputRoot 'process-0/focused-input.json') -Destination $results
        & dotnet (Join-Path $benchmarkBin 'Akbura.Benchmarks.dll') --component-probe-cache --focused-only `
            --variants (Join-Path $outputRoot 'variants') --profile-variants (Join-Path $outputRoot 'profile-variants') `
            --output $results --warmups $Warmups --iterations $Iterations --round (($process + 1) * 2) *> (Join-Path $outputRoot "focused-$process.log")
        if ($LASTEXITCODE -ne 0) { throw "Focused process $process failed. See its log." }
        Write-Host "Completed independent focused process $process"
    }
} finally {
    $env:DOTNET_TieredCompilation = $previousTiering
}
