# Verification evidence

All final checks passed. These are selected compiler/instrumentation regressions, not a repeated full-repository test audit. No benchmark timings overlap these tests/builds.

| Check | Result | Evidence |
| --- | --- | --- |
| DebugStats regressions, repository Roslyn 4.11 host | 230 passed, 0 failed, 0 skipped | [TRX](invalidation-complete.trx) |
| Focused instrumentation/statistics tests, SDK Roslyn 5.9 host | 17 passed, 0 failed, 0 skipped | [TRX](sdk-invalidation.trx), [assembly hashes/versions](sdk-verification.json), [selected host-resolution lines](sdk-host-resolution.txt) |
| New invalidation report checks | 12 passed | [Verification manifest](verification.json) |
| Existing report checks | 17 passed | [Verification manifest](verification.json) |
| Release analyzer build | 0 errors, 0 warnings | [Build log](release-build.log) |
| Final benchmark-project build | 0 errors, 0 warnings | [Verification manifest](verification.json) |
| Braces / multiline XML summaries | 25 changed C# files checked, 0 violations | [Verification manifest](verification.json) |

Commands run from the repository root:

```powershell
dotnet build src/Akbura.BlackSilence/Akbura.BlackSilence.csproj -c Release --nologo -v:minimal
dotnet build benchmarks/HotReload/HotReloadBenchmarks.csproj -p:BlackSilenceConfiguration=DebugStats `
  -o bin/CSharpInvalidationInvestigation/verified-tool -v:minimal
dotnet test src/Akbura.UnitTests/Akbura.UnitTests.csproj -c DebugStats `
  --filter 'FullyQualifiedName~BlackSilence|FullyQualifiedName~AkburaProjectIndex|FullyQualifiedName~DocumentSyntaxVersion|FullyQualifiedName~GenerationStatistics' `
  -v:minimal --logger 'trx;LogFileName=invalidation-complete.trx' `
  --results-directory bin/CSharpInvalidationInvestigation/tests -- xUnit.MaxParallelThreads=4
pwsh -NoProfile -File benchmarks/HotReload/Verify-SdkRoslyn.ps1
pwsh -NoProfile -File benchmarks/HotReload/Test-InvalidationReport.ps1
pwsh -NoProfile -File benchmarks/HotReload/Test-Report.ps1
```

The ordinary test output remains on Roslyn `4.11.0-3.24376.4+5e3a11e2e7f952da93f9d35bd63a2fa181c0608b`. The SDK verification copies that output into an ignored isolated host and replaces only `Microsoft.CodeAnalysis` and `Microsoft.CodeAnalysis.CSharp` with SDK 10.0.401's 5.9 assemblies. A test asserts `5.9.0.0` from inside the executing host. Host-resolution evidence provides an independent path check; the manifest also records the full ignored host log's hash. Repository package references and the original output are unchanged.

The DebugStats build still reports pre-existing `CS8602` in `ComponentGenerator.cs` and `xUnit2031`/`xUnit2013` in `QuotedResourceRegressionTests.cs`. They do not fail the test run and were not changed in this task. Release analyzer and benchmark builds are warning-free.

The new test coverage is 11 trace tests plus one parse-cache fixture regression. The fixture fix selects distinct real direct-mapped cache slots rather than changing production caching or weakening exact parse-count assertions. The [engineering report](../../../../../For%20Contributors/csharp-invalidation-investigation.md) explains the causal measurements, ownership constraints and remaining limitations.
