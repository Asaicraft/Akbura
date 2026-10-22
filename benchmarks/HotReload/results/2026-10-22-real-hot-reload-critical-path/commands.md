# Reproduction commands

Run from the repository root. The existing frozen seed was prepared before instrumentation at `15d850ae1f0fef104aa77be3743cbc6f2ba0b2cd`, using all eight verified compiler closures from `bin/SemanticWatch/gate-recovery`. Its `baseline.json` records the source hashes, compiler-origin manifest, tutorial revision and runtime. Do not rebuild the baseline from the instrumented tree. On another machine, supply an equivalent explicitly frozen seed with `-SeedDirectory`; the local `bin` paths are not bundled binaries.

```powershell
pwsh -NoProfile -File benchmarks/HotReload/Verify-PurityCriticalPath.ps1 `
    -OutputDirectory bin/PurityCriticalPath/gate `
    -ResultDirectory benchmarks/HotReload/results/2026-10-22-real-hot-reload-critical-path `
    -SeedDirectory bin/PurityCriticalPath/seed
Get-Content bin/PurityCriticalPath/gate/journal.log -Tail 20 -Wait
```

The same inputs resume the same gate. A fresh diagnostic-only gate can be launched with `-PreflightOnly`, a new output directory and a new result directory. It is not a partially completed production matrix.

The controller builds one common parent, the exact-version EventPipe reader, four instrumented compiler pairs, and runs eight focused DebugStats tests plus these guards:

```powershell
pwsh -NoProfile -File benchmarks/HotReload/Test-PurityTrace.ps1
pwsh -NoProfile -File benchmarks/HotReload/Test-PurityCriticalPathSummary.ps1
```

Every real session invokes `Run-PurityCriticalPath.ps1` in a fresh directory, with `-Variant`, `-Scenario`, `-Updates`, `-Warmups` and an explicit frozen `-ToolDirectory`. Diagnostic sessions additionally use `-Instrumented -TraceCollector <frozen-reader.dll>`. Production sessions never receive that switch or collector. The per-step logs and success marker preserve the exact arguments. The existing `Verify-SupportedWatch.ps1` is reused for a separate member-chain causal control across four variants.

Report-only regeneration, after all primary sessions have completed:

```powershell
pwsh -NoProfile -File benchmarks/HotReload/Summarize-PurityCriticalPath.ps1 `
    -ResultDirectory benchmarks/HotReload/results/2026-10-22-real-hot-reload-critical-path
```

`Summarize-PurityTrace.ps1 -SessionDirectory <diagnostic-session>` independently validates span pairing/correlation without rerunning watch. Both report helpers fail closed on incomplete or ambiguous evidence. No command above commits, pushes, changes defaults or implements a production optimization.
