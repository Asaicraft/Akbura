#requires -Version 7.2
param(
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][string]$ToolDirectory,
    [ValidateSet('baseline','projection','binding','combined')][string]$Variant = 'baseline',
    [ValidateSet('supported','fallback','binding','compilation')][string]$Scenario = 'supported',
    [ValidateRange(1,50)][int]$Updates = 30,
    [ValidateRange(0,3)][int]$Warmups = 3,
    [string]$TraceCollector = '',
    [switch]$Instrumented
)
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Set-Location -LiteralPath $repository
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$ToolDirectory = [IO.Path]::GetFullPath($ToolDirectory)
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Preserve completed/partial evidence; choose a fresh session directory.' }
$null = New-Item -ItemType Directory -Path $OutputDirectory
$encoding = [Text.UTF8Encoding]::new($false)
$traceProcess = $null
function Save([string]$name, $value) {
    [IO.File]::WriteAllText((Join-Path $OutputDirectory $name),(($value | ConvertTo-Json -Depth 10).ReplaceLineEndings("`r`n") + "`r`n"),$encoding)
}
function Run([string]$name, [string[]]$arguments) {
    $writer = [IO.StreamWriter]::new((Join-Path $OutputDirectory $name),$false,$encoding)
    $writer.AutoFlush = $true
    try {
        & dotnet @arguments 2>&1 | ForEach-Object {
            $line = "$_"
            $writer.WriteLine($line)
            if ($name -eq 'watch.jsonl' -and $TraceCollector -and $line.StartsWith('{')) {
                $row = $line | ConvertFrom-Json
                if ($row.Kind -eq 'purity-critical-ready') {
                    $all = @(Get-CimInstance Win32_Process)
                    $owned = [Collections.Generic.HashSet[int]]::new(); $null = $owned.Add([int]$row.WatchLauncherPid)
                    do {
                        $changed = $false
                        foreach ($item in $all) { if ($owned.Contains([int]$item.ParentProcessId) -and $owned.Add([int]$item.ProcessId)) { $changed = $true } }
                    } while ($changed)
                    $compiler = @($all | Where-Object { $owned.Contains([int]$_.ProcessId) -and $_.CommandLine -match 'dotnet-watch\.(dll|exe)' })
                    if ($compiler.Count -ne 1) { throw 'Cannot identify one owned compiler/watch process for EventPipe.' }
                    Save 'trace-target.json' @{ CompilerWatch=$compiler[0] | Select-Object ProcessId,ParentProcessId,ExecutablePath,CommandLine
                        ApplicationPid=$row.ApplicationPid; WatchLauncherPid=$row.WatchLauncherPid; CollectorSha256=(Get-FileHash -LiteralPath $TraceCollector).Hash }
                    $traceArgs = @($TraceCollector,"$($compiler[0].ProcessId)",(Join-Path $OutputDirectory 'roslyn-events.jsonl'),(Join-Path $OutputDirectory 'trace-ready.json'))
                    $script:traceProcess = Start-Process (Get-Command dotnet).Source -WindowStyle Hidden -PassThru -ArgumentList @($traceArgs | ForEach-Object { '"'+$_+'"' }) `
                        -RedirectStandardOutput (Join-Path $OutputDirectory 'trace.stdout.log') -RedirectStandardError (Join-Path $OutputDirectory 'trace.stderr.log')
                }
            }
        }
        if ($LASTEXITCODE -ne 0) { throw "Exit $LASTEXITCODE in $name; inspect $OutputDirectory" }
    }
    finally { $writer.Dispose() }
}
$closure = @(Get-ChildItem -LiteralPath $ToolDirectory -File | ForEach-Object { @{ File=$_.Name; Sha256=(Get-FileHash -LiteralPath $_.FullName).Hash } })
$prepared = & (Join-Path $PSScriptRoot 'Prepare-Purity.ps1') -SourceRoot (Join-Path $PSScriptRoot 'Akbura-PurityUI-Tutorial') -OutputDirectory $OutputDirectory -RepositoryRoot $repository
$project = Join-Path $prepared 'PurityUIDashborad/PurityUIDashborad.csproj'
$hostProject = Join-Path $prepared 'PurityHost/PurityHost.csproj'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'PurityCriticalPathHost/Program.cs') -Destination (Join-Path $prepared 'PurityHost/Program.cs') -Force
$xml = [IO.File]::ReadAllText($project)
foreach ($assembly in @('Akbura.Generator','Akbura.BlackSilence')) {
    $old = Join-Path $repository "src/$assembly/bin/Debug/netstandard2.0/$assembly.dll"
    if (-not $xml.Contains($old)) { throw "The prepared analyzer reference changed: $assembly" }
    $xml = $xml.Replace($old,(Join-Path $ToolDirectory "$assembly.dll"))
}
$xml = $xml.Replace('</PropertyGroup>','<UseSharedCompilation>false</UseSharedCompilation><AkburaCrossCompilationReuse>true</AkburaCrossCompilationReuse></PropertyGroup>')
[IO.File]::WriteAllText($project,$xml,$encoding)
$sidebar = Join-Path $prepared 'PurityUIDashborad/Components/Sidebar.akbura'
$original = [IO.File]::ReadAllText($sidebar)
$originalHash = (Get-FileHash -LiteralPath $sidebar).Hash
if ([regex]::Matches($original,'<Panel>').Count -ne 1 -or [regex]::Matches($original,'Text="Dashboard"').Count -ne 1) { throw 'The real Sidebar fixture changed; review preparation.' }
$root = switch ($Scenario) {
    'binding' { '<Panel Margin={new global::Avalonia.Thickness(10d)}>' }
    'compilation' { '<Panel Margin={CriticalPathMargin}>' }
    default { '<Panel Margin="10">' }
}
$text = $original.Replace('<Panel>',$root)
[IO.File]::WriteAllText($sidebar,$text,$encoding)
if ($Scenario -eq 'compilation') {
    [IO.File]::WriteAllText((Join-Path (Split-Path $sidebar) 'Sidebar.CriticalPath.cs'),"namespace PurityUIDashborad.Components;`r`npartial class Sidebar`r`n{`r`n    private global::Avalonia.Thickness CriticalPathMargin => new global::Avalonia.Thickness(10d);`r`n}`r`n",$encoding)
}
$generated = Join-Path $OutputDirectory 'generated'
Run 'build.log' @('build',$hostProject,'--nologo','-v:minimal','-p:UseSharedCompilation=false','-nr:false',
    '-p:EmitCompilerGeneratedFiles=true',"-p:CompilerGeneratedFilesOutputPath=$generated")
Run 'inputs.json' @('msbuild',$project,'-nologo','-v:q','-t:ResolveReferences','-getProperty:RootNamespace,MSBuildProjectDirectory,DefineConstants','-getItem:Compile,ReferencePath,AdditionalFiles')
$generatedFiles = @(Get-ChildItem -LiteralPath $generated -File -Recurse -Filter '*.cs')
Save 'workload.json' @{ Workload='Unmodified committed Purity Sidebar plus explicitly recorded scenario fixture adaptation'; Scenario=$Scenario
    Variant=$Variant; OriginalSidebarSha256=$originalHash; PreparedSidebarSha256=(Get-FileHash -LiteralPath $sidebar).Hash
    Adaptation=$root; CompilationControlMeaning=$(if ($Scenario -eq 'compilation') { 'A root Margin binding to a prepared C# getter; each edit changes its value and adds a const declaration, ensuring a changed C# declaration contract without changing an existing CLR member signature.' } else { $null })
    PreparedRoot=$prepared; HostProject=$hostProject; ComponentSourceCount=@(Get-ChildItem (Join-Path $prepared 'PurityUIDashborad') -Filter '*.akbura' -File -Recurse).Count
    GeneratedCsFiles=$generatedFiles.Count; GeneratedCsBytes=($generatedFiles | Measure-Object Length -Sum).Sum
    Generated=@($generatedFiles | ForEach-Object { @{ File=$_.Name; Bytes=$_.Length; Sha256=(Get-FileHash -LiteralPath $_.FullName).Hash } })
    Runtime=@('Akbura','Akbura.Diagnostics','Akbura.Build.Tasks') | ForEach-Object { @{ Assembly=$_; Sha256=(Get-FileHash -LiteralPath (Join-Path $repository "src/$_/bin/Debug/net10.0/$_.dll")).Hash } }
    ToolClosure=$closure; Instrumented=[bool]$Instrumented }
$observations = if ($Instrumented) { Join-Path $OutputDirectory 'analyzer-observations' } else { 'off' }
if ($TraceCollector -and -not $Instrumented) { throw 'EventPipe diagnostics must not contaminate primary non-STATS timing sessions.' }
$previousTrace = $env:AKBURA_CRITICAL_TRACE_REQUIRED
try {
    $env:AKBURA_CRITICAL_TRACE_REQUIRED = if ($TraceCollector) { '1' } else { '' }
    Run 'watch.jsonl' @((Join-Path $ToolDirectory 'HotReloadBenchmarks.dll'),'purity-critical-watch',$hostProject,$sidebar,$Scenario,"$Updates","$Warmups",$OutputDirectory,$observations,$ToolDirectory)
    if ($traceProcess) {
        if (-not $traceProcess.WaitForExit(30000)) { throw 'The owned diagnostic collector did not finish after watch exited.' }
        if ($traceProcess.ExitCode -ne 0) { throw 'The EventPipe collector failed; inspect trace.stderr.log.' }
        & (Join-Path $PSScriptRoot 'Summarize-PurityTrace.ps1') -SessionDirectory $OutputDirectory
    }
}
finally {
    $env:AKBURA_CRITICAL_TRACE_REQUIRED = $previousTrace
    if ($traceProcess) {
        if (-not $traceProcess.HasExited) { $traceProcess.Kill($true); $traceProcess.WaitForExit() }
        $traceProcess.Dispose()
    }
}
$rows = @(Get-Content -LiteralPath (Join-Path $OutputDirectory 'watch.jsonl') | ForEach-Object { $_ | ConvertFrom-Json })
$samples = @($rows | Where-Object { $_.Kind -eq 'purity-critical-sample' -and -not $_.Warmup })
if ($samples.Count -ne $Updates -or @($rows | Where-Object Kind -eq 'purity-critical-complete').Count -ne 1 -or
    @($samples.Application.Pid | Sort-Object -Unique).Count -ne 1 -or @($samples.Revision | Sort-Object -Unique).Count -ne $Updates) { throw 'Missing, duplicated or restarted live revisions.' }
$causal = @($rows | Where-Object Kind -eq 'purity-critical-causal')
$causalGate = if ($Instrumented) { 'Passed' } else { 'UnverifiedInProduction' }
if ($Instrumented) {
    foreach ($sample in $samples) {
        $records = @($causal | Where-Object Revision -eq $sample.Revision | ForEach-Object Record)
        $totals = @{}
        foreach ($counter in @('CrossRevisionBindingHit','CrossRevisionFirstBindingSkipped','CrossRevisionProbeCompilationSkipped','CrossRevisionProbeSemanticModelSkipped','CrossRevisionSemanticContributionImported')) {
            $totals[$counter] = ($records | ForEach-Object { [long]$_.('counter.'+$counter) } | Measure-Object -Sum).Sum
        }
        if (@($totals.Values | Sort-Object -Unique).Count -ne 1) { throw 'A semantic hit did not skip actual first binding/probe work and import its contribution.' }
        if ($Scenario -eq 'supported' -and $Variant -in @('binding','combined') -and $totals.CrossRevisionBindingHit -le 0) { $causalGate = 'NoAdmittedBindingReuseOnRealSidebar' }
        if (($Variant -in @('baseline','projection') -or $Scenario -eq 'compilation') -and $totals.CrossRevisionBindingHit -gt 0) { throw 'Unexpected semantic reuse in a negative compilation/variant control.' }
    }
}
foreach ($binary in $closure) { if ((Get-FileHash -LiteralPath (Join-Path $ToolDirectory $binary.File)).Hash -ne $binary.Sha256) { throw 'Frozen tool closure changed during measurement.' } }
Save 'result.json' @{ Complete=$true; Variant=$Variant; Scenario=$Scenario; Updates=$Updates; Warmups=$Warmups; Instrumented=[bool]$Instrumented
    RuntimeCorrectness=$true; Restarts=0; CausalGate=$causalGate; CompletedUtc=[DateTime]::UtcNow.ToString('o')
    TimingBoundary='Completed source write to verified original-owner UI acknowledgment; includes state/command validation. No forced GC.' }
Write-Output "PASS $Variant/$Scenario instrumented=$Instrumented updates=$Updates causal=$causalGate"
