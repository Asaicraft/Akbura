# Checkpoint-channel recovery

Status: **36-session memory matrix complete; [repaired summary and preliminary review](validated/summary/README.md) available**. All 21 completed sessions were restored and the remaining 15 were executed. The controller later stopped on an unrelated incomplete counter-sample selection in report generation. A separate read-only report repair completed successfully without rerunning measurements. The commands below describe the completed continuation, not a currently running job.

The preceding I/O recovery reached **21 complete 100-edit sessions**: all 18 natural-GC sessions and three forced-GC sessions. Its next session, `forced-1-combined-release`, stopped after its revision-one checkpoint. The application had acknowledged the update and `dotnet watch` had reported successful application of its delta. The parent benchmark client then threw `System.IO.IOException` in `File.ReadAllText(response.json)` because another process temporarily held an incompatible file handle. This is a checkpoint transport failure, not a failed compiler update or evidence of a cache leak. The partial session is excluded from aggregate measurements.

The first repair protected the PowerShell atomic publisher but left the C# reader and request publisher unprotected. Both ends now read with `FileShare.ReadWrite | FileShare.Delete`. Reads and atomic request replacement retry only I/O/access exceptions with Win32 codes 5, 32 or 33, at 25 ms intervals for at most ten seconds. A missing response/request remains pending. Malformed JSON, a matching failed response and invalid directory destinations remain errors; persistent locks still fail. Publication remains a complete temporary-file write followed by atomic replacement. Checkpoint pauses remain outside the measured file-write-to-UI interval.

## Validation

`Test-WatchMemoryChannel.ps1` compiles the exact C# checkpoint source with only logging/assertion dependencies stubbed. The preserved original source reproduces the response-read failure under an exclusive Windows handle. The repaired implementation checks blocked reads, blocked request publication, stale nonces, 50 sequential handshakes, failed and malformed responses, invalid directory paths, and a persistent read lock that must fail at its ten-second retry deadline. `Test-WatchMemoryPublication.ps1 -RequestReader` separately exercises the actual extracted PowerShell request reader, including malformed JSON rejection. A startup smoke attempt also identified the first poll before the checkpoint directory is created; that case must remain pending, just like a missing request. Its diagnostic failure is preserved locally, and the reader regression now covers it. Existing atomic-publication and report guards remain part of the background preflight.

## Resume and provenance

```powershell
pwsh -NoProfile -File benchmarks/HotReload/Start-WatchMemoryAttribution.ps1 `
    -OutputDirectory bin/WatchMemory/gate-channel-validated `
    -ResultDirectory benchmarks/HotReload/results/2026-10-22-watch-memory-attribution/channel-recovery/validated `
    -ReuseMemoryEvidence bin/WatchMemory/gate-channel-recovery
Get-Content bin/WatchMemory/gate-channel-validated/journal.log -Tail 20 -Wait
```

The 21 completed sessions retain their original paths, completion times, actual measurement fingerprints and binary inventories. Reused success markers link to the preceding marker and its hash; a second reuse preserves the first measurement fingerprint instead of relabeling it with the intermediate controller fingerprint. The previous gates, manifests, raw dumps and failed partial attempts are not overwritten.

Only the parent benchmark/checkpoint client was rebuilt for new sessions. Its source and binary change are explicitly recorded; it is not the application, compiler/watch host or managed launcher sampled by the memory study. The final startup continuation reuses that repaired client binary unchanged. All other closure binaries, including both compiler pairs and runtime dependencies, are hash-checked against the preceding closure. The offline profiler is reused byte-for-byte. The application fixture, UI verification protocol, compiler implementations, recorder and experimental defaults are unchanged. Source differences are restricted to the checkpoint transport, its controller and regression scripts.

The unfinished session restarts from revision one, followed by the remaining forced-GC sessions and summary generation. These are diagnostic memory sessions, not primary production latency results. Retained-object attribution, any justified 500-edit extension and the full heavy-component timing study still require review under the [parent methodology](../README.md). No production fix, default activation, commit, push or next optimization is performed automatically.
