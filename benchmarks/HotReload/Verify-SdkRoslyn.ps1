#requires -Version 7.2
param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../../bin/CSharpInvalidationInvestigation/sdk-tests'),
    [switch]$Prepare
)

$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$testProject = Join-Path $repository 'src/Akbura.UnitTests/Akbura.UnitTests.csproj'
if ($Prepare) {
    & dotnet build $testProject -c DebugStats --nologo -v:minimal
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not build the instrumentation tests.'
    }
}
$original = Join-Path $repository 'src/Akbura.UnitTests/bin/DebugStats/net10.0'
if (-not (Test-Path -LiteralPath (Join-Path $original 'Akbura.UnitTests.dll'))) {
    throw 'Build DebugStats tests first, or pass -Prepare.'
}
$sdk = (& dotnet --version).Trim()
if ($LASTEXITCODE -ne 0) {
    throw 'Could not identify the active SDK.'
}
$dotnetRoot = Split-Path (Get-Command dotnet).Source
$roslyn = Join-Path $dotnetRoot "sdk/$sdk/Roslyn/bincore"
$root = Join-Path ([IO.Path]::GetFullPath($OutputDirectory)) ([Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $root -Force
$shadow = Join-Path $root 'test-host'
Copy-Item -LiteralPath $original -Destination $shadow -Recurse
$assemblies = foreach ($name in @('Microsoft.CodeAnalysis', 'Microsoft.CodeAnalysis.CSharp')) {
    $source = Join-Path $roslyn "$name.dll"
    if (-not (Test-Path -LiteralPath $source)) {
        throw "The selected SDK does not contain $source."
    }
    Copy-Item -LiteralPath $source -Destination (Join-Path $shadow "$name.dll") -Force
    [PSCustomObject]@{
        Name = $name
        AssemblyVersion = [Reflection.AssemblyName]::GetAssemblyName($source).Version.ToString()
        InformationalVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($source).ProductVersion
        Sha256 = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
    }
}
$previousTrace = $env:DOTNET_HOST_TRACE
$previousTraceFile = $env:DOTNET_HOST_TRACEFILE
$previousExpectedVersion = $env:AKBURA_TEST_EXPECTED_ROSLYN_VERSION
try {
    $env:DOTNET_HOST_TRACE = '1'
    $env:DOTNET_HOST_TRACEFILE = Join-Path $root 'host-resolution.log'
    $env:AKBURA_TEST_EXPECTED_ROSLYN_VERSION = $assemblies[0].AssemblyVersion
    & dotnet vstest (Join-Path $shadow 'Akbura.UnitTests.dll') `
        '--TestCaseFilter:FullyQualifiedName~InvalidationTrace_|FullyQualifiedName~BlackSilenceGenerationStatisticsTests' `
        '--Logger:trx;LogFileName=sdk-invalidation.trx' "--ResultsDirectory:$root" `
        -- xUnit.MaxParallelThreads=4 2>&1 | ForEach-Object { "$_" } |
        Tee-Object -FilePath (Join-Path $root 'tests.log')
    $exitCode = $LASTEXITCODE
}
finally {
    $env:DOTNET_HOST_TRACE = $previousTrace
    $env:DOTNET_HOST_TRACEFILE = $previousTraceFile
    $env:AKBURA_TEST_EXPECTED_ROSLYN_VERSION = $previousExpectedVersion
}
if ($exitCode -ne 0) {
    throw "SDK-host test run failed; see $root."
}
[xml]$result = Get-Content -LiteralPath (Join-Path $root 'sdk-invalidation.trx') -Raw
$counters = $result.TestRun.ResultSummary.Counters
if ([int]$counters.failed -ne 0 -or [int]$counters.executed -ne 17) {
    throw 'Expected all 17 instrumentation/statistics checks to pass.'
}
$resolution = [IO.File]::ReadAllText((Join-Path $root 'host-resolution.log'))
foreach ($name in @('Microsoft.CodeAnalysis', 'Microsoft.CodeAnalysis.CSharp')) {
    if (-not $resolution.Contains((Join-Path $shadow "$name.dll"))) {
        throw "Host resolution did not select the isolated $name assembly."
    }
}
$metadata = @{
    Sdk = $sdk
    Assemblies = @($assemblies)
    Executed = [int]$counters.executed
    Passed = [int]$counters.passed
    Failed = [int]$counters.failed
    TestAssemblySha256 = (Get-FileHash -LiteralPath (Join-Path $shadow 'Akbura.UnitTests.dll')).Hash
    RuntimeVersionAssertion = 'InvalidationTrace_UnchangedCompilationSkipsCallbacks asserts the SDK assembly version inside the executing test host.'
    Mode = 'Isolated copy of the existing tests; SDK Roslyn replaces only the two host assemblies. Repository package references and original test output remain unchanged.'
}
[IO.File]::WriteAllText((Join-Path $root 'sdk-verification.json'),
    ($metadata | ConvertTo-Json -Depth 8) + "`r`n", [Text.UTF8Encoding]::new($false))
Write-Host "SDK Roslyn checks passed; isolated host and resolution evidence: $root"
