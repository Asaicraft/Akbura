[CmdletBinding()]
param([string] $ResultsDirectory = '')

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$env:MSBUILDDISABLENODEREUSE = '1'
$env:DOTNET_CLI_USE_MSBUILD_SERVER = '0'
$repository = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
if (-not $ResultsDirectory) {
    $ResultsDirectory = Join-Path ([IO.Path]::GetTempPath()) ('Akbura-Stage5-Editor-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
}
$results = [IO.Path]::GetFullPath($ResultsDirectory)
if (Test-Path -LiteralPath $results) { throw "Results directory already exists: $results" }
New-Item -ItemType Directory -Path $results | Out-Null
$dotnet = (Get-Command dotnet -ErrorAction Stop).Source
$pwsh = (Get-Process -Id $PID).Path
$jobs = [Collections.Generic.List[object]]::new()
$unit = 'src/Akbura.UnitTests/Akbura.UnitTests.csproj'
$workspaceTests = 'src/Workspaces/Akbura.Workspaces.UnitTests/Akbura.Workspaces.UnitTests.csproj'
$lspTests = 'src/Workspaces/Akbura.LanguageServer.UnitTests/Akbura.LanguageServer.UnitTests.csproj'
$integrationTests = 'src/Workspaces/Akbura.LanguageServer.IntegrationTests/Akbura.LanguageServer.IntegrationTests.csproj'

function Add-Job([string] $Name, [string] $File, [string[]] $Arguments, [bool] $Gate = $false, [string] $WorkingDirectory = $repository) {
    $jobs.Add(@{ Name = ('{0:d2}-{1}' -f ($jobs.Count + 1), $Name); File = $File;
        Arguments = $Arguments; Gate = $Gate; WorkingDirectory = $WorkingDirectory })
}

function Add-Test([string] $Name, [string] $Project, [string] $Configuration, [string] $Filter = '', [bool] $Gate = $false) {
    $step = '{0:d2}-{1}' -f ($jobs.Count + 1), $Name
    $testArguments = @('test', $Project, '-c', $Configuration, '--no-restore', '--disable-build-servers', '-m:1',
        '--results-directory', (Join-Path $results $step), '--logger', 'trx;LogFileName=results.trx',
        '--logger', 'console;verbosity=normal', '--blame-hang-timeout', '10m')
    if ($Filter) { $testArguments += @('--filter', $Filter) }
    Add-Job $Name $dotnet $testArguments $Gate
}

foreach ($configuration in @('Debug', 'Release')) {
    Add-Job "Generator-$configuration" $dotnet @('build', 'src/Akbura.Generator/Akbura.Generator.csproj',
        '-c', $configuration, '--no-restore', '--disable-build-servers', '-m:1') $true
    Add-Test "ExpressionBinding-Workspaces-$configuration" $workspaceTests $configuration 'FullyQualifiedName~WorkspaceExpressionBindingTests|FullyQualifiedName~Selector_HidesOnlyTheReservedProbeMethodInBothHosts|FullyQualifiedName~CSharpCompletionChangeMapper|FullyQualifiedName~Completion_MarkupExtension' $true
    Add-Test "ExpressionBinding-LSP-$configuration" $lspTests $configuration 'FullyQualifiedName~BindingCompletionProtocolTests' $true
    Add-Test "ExpressionBinding-Lifecycle-$configuration" $integrationTests $configuration 'FullyQualifiedName~ExpressionBinding_RealProjectLifecycle' $true
    Add-Test "ExpressionBinding-Compiler-$configuration" $unit $configuration 'FullyQualifiedName~MarkupExpressionBindingSemanticTests|FullyQualifiedName~MarkupExpressionBindingParserTests|FullyQualifiedName~MarkupExpressionBindingIncrementalParserTests|FullyQualifiedName~MarkupExpressionBindingDependencyTests|FullyQualifiedName~MarkupExpressionBindingSyntaxTests' $true
}
foreach ($configuration in @('Debug', 'Release')) {
    Add-Test "Workspaces-All-$configuration" $workspaceTests $configuration
    Add-Test "LanguageServer-All-$configuration" $lspTests $configuration
    Add-Test "LanguageServer-Integration-All-$configuration" $integrationTests $configuration
    Add-Job "VisualStudio-$configuration" $dotnet @('build',
        'src/Workspaces/Akbura.VisualStudio/Akbura.VisualStudio.csproj', '-c', $configuration,
        '--no-restore', '--disable-build-servers', '-m:1')
}
$extension = Join-Path $repository 'src/Workspaces/Akbura.LanguageServer.VsCode'
foreach ($command in @('ci', 'run compile', 'run test:vscode')) {
    Add-Job ('VSCode-' + $command.Replace(' ', '-').Replace(':', '-')) $env:ComSpec @('/d', '/c', "npm.cmd $command") $true $extension
}
Add-Job 'Existing-Windows-Editor-VSIX-Verification' $pwsh @('-NoProfile', '-File',
    (Join-Path $repository 'eng/Verify-AkburaEditorResourceFixes.ps1'), '-BuildVisualStudio')

$rows = @($jobs | ForEach-Object {
    [pscustomobject]@{ Project = $_.Name; Status = 'Pending'; ExitCode = ''; Total = ''; Passed = '';
        Failed = ''; NotExecuted = ''; Started = ''; Finished = '' }
})
try {
    $jobs | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $results 'commands.json') -Encoding utf8
    $sourceFiles = @(& git -C $repository diff --name-only; & git -C $repository ls-files --others --exclude-standard) | Select-Object -Unique
    $sourceFiles | ForEach-Object { Get-FileHash -LiteralPath (Join-Path $repository $_) -Algorithm SHA256 } |
        Select-Object Path, Algorithm, Hash | Export-Csv (Join-Path $results 'source-hashes.csv') -NoTypeInformation -Encoding utf8
    for ($index = 0; $index -lt $jobs.Count; $index++) {
        $job = $jobs[$index]
        $row = $rows[$index]
        $directory = Join-Path $results $job.Name
        New-Item -ItemType Directory -Path $directory | Out-Null
        $job.Name | Set-Content (Join-Path $results 'status.txt') -Encoding utf8
        $row.Status = 'Running'
        $row.Started = [DateTimeOffset]::Now.ToString('o')
        $rows | Export-Csv (Join-Path $results 'summary.csv') -NoTypeInformation -Encoding utf8
        $info = [Diagnostics.ProcessStartInfo]::new($job.File)
        $info.WorkingDirectory = $job.WorkingDirectory
        $info.UseShellExecute = $false
        $info.CreateNoWindow = $true
        $info.RedirectStandardOutput = $true
        $info.RedirectStandardError = $true
        foreach ($argument in $job.Arguments) { $info.ArgumentList.Add($argument) }
        $process = [Diagnostics.Process]::new()
        $process.StartInfo = $info
        $process.Start() | Out-Null
        $process.Id | Set-Content (Join-Path $directory 'process.pid')
        $stdout = [IO.File]::Open((Join-Path $directory 'console.log'), 'Create', 'Write', 'ReadWrite')
        $stderr = [IO.File]::Open((Join-Path $directory 'stderr.log'), 'Create', 'Write', 'ReadWrite')
        try {
            $stdoutTask = $process.StandardOutput.BaseStream.CopyToAsync($stdout)
            $stderrTask = $process.StandardError.BaseStream.CopyToAsync($stderr)
            $timedOut = -not $process.WaitForExit(5400000)
            if ($timedOut) {
                $process.Kill($true)
                $process.WaitForExit()
                $row.ExitCode = -2
            }
            else {
                $row.ExitCode = $process.ExitCode
            }
            # Both copies are already running concurrently. Await each directly
            # to avoid PowerShell's ambiguous Task.WhenAll overload resolution.
            $null = $stdoutTask.GetAwaiter().GetResult()
            $null = $stderrTask.GetAwaiter().GetResult()
            # Publish success only after both log streams have finished safely.
            $row.Status = if ($timedOut) { 'TimedOut' }
                elseif ($row.ExitCode -eq 0) { 'Passed' }
                else { 'Failed' }
        }
        finally { $stdout.Dispose(); $stderr.Dispose(); $process.Dispose() }
        $row.Finished = [DateTimeOffset]::Now.ToString('o')
        $trxPath = Join-Path $directory 'results.trx'
        if (Test-Path -LiteralPath $trxPath) {
            $trx = [xml](Get-Content -LiteralPath $trxPath -Raw)
            $counts = $trx.TestRun.ResultSummary.Counters
            $row.Total = $counts.total; $row.Passed = $counts.passed
            $row.Failed = $counts.failed; $row.NotExecuted = $counts.notExecuted
            if ($row.Status -eq 'Passed' -and ($trx.TestRun.ResultSummary.outcome -ne 'Completed' -or [int]$counts.total -eq 0)) {
                $row.Status = 'Incomplete'
            }
            $failures = @($trx.TestRun.Results.UnitTestResult | Where-Object outcome -ne Passed | ForEach-Object {
                $_.testName + "`r`n" + $_.Output.ErrorInfo.Message + "`r`n" + $_.Output.ErrorInfo.StackTrace
            })
            if ($failures.Count) { $failures -join "`r`n`r`n" | Set-Content (Join-Path $directory 'non-passed-tests.txt') -Encoding utf8 }
        }
        elseif ($job.Arguments[0] -eq 'test' -and $row.Status -eq 'Passed') { $row.Status = 'NoTrx' }
        $rows | Export-Csv (Join-Path $results 'summary.csv') -NoTypeInformation -Encoding utf8
        if ($job.Gate -and $row.Status -ne 'Passed') {
            for ($skipped = $index + 1; $skipped -lt $rows.Count; $skipped++) { $rows[$skipped].Status = 'Skipped' }
            break
        }
    }
    'Completed. Inspect summary.csv and per-step console.log / stderr.log / results.trx. Skipped, aborted and timed-out checks are not success.' |
        Set-Content (Join-Path $results 'status.txt') -Encoding utf8
}
catch {
    $_ | Out-String | Set-Content (Join-Path $results 'ERROR.txt') -Encoding utf8
    foreach ($row in $rows) {
        if ($row.Status -eq 'Running') {
            $row.Status = 'RunnerError'; $row.ExitCode = -1
            $row.Finished = [DateTimeOffset]::Now.ToString('o')
        }
        elseif ($row.Status -eq 'Pending') { $row.Status = 'Skipped' }
    }
    'Runner error. Inspect ERROR.txt.' | Set-Content (Join-Path $results 'status.txt') -Encoding utf8
}
finally {
    $rows | Export-Csv (Join-Path $results 'summary.csv') -NoTypeInformation -Encoding utf8
    [DateTimeOffset]::Now.ToString('o') | Set-Content (Join-Path $results 'DONE.txt') -Encoding utf8
}
Write-Output $results
