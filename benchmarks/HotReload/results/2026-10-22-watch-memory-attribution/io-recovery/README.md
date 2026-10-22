# Memory study continuation after an atomic-publication race

Status: **superseded by the [checkpoint-channel recovery](../channel-recovery/README.md)**. This controller reached 21 complete sessions, then stopped on a transient lock in the unprotected C# response reader. The completed sessions are preserved and reused; the matched memory investigation is still in progress. The commands below describe this earlier recovery, not the active progress journal.

The initial controller completed 16 full natural-GC sessions, then failed in `natural-3-baseline-stats-no-observer` after its revision-25 checkpoint. The checkpoint JSON was saved, but replacement of `checkpoints/response.json` failed with Windows access denied. The old destination and the complete new `.tmp` payload remained intact. This is a harness handshake failure, not a compiler error or a completed failed memory series.

A focused Windows test reproduced the error by holding the destination open with `FileShare.Read` (no delete sharing), then calling the original one-attempt publisher. The corrected publisher preserves the atomic rename and retries only I/O/access exceptions carrying Win32 5, 32 or 33, at 25 ms intervals for at most ten seconds. Other errors propagate. Directory destinations are rejected immediately. The same correction protects the controller's atomic status/manifest publisher. A held-reader regression passes for both publishers; invalid destinations are rejected. No cache, compilation, observation, UI or offline graph-analysis implementation changes.

## Provenance and resume

```powershell
pwsh -NoProfile -File benchmarks/HotReload/Start-WatchMemoryAttribution.ps1 `
    -OutputDirectory bin/WatchMemory/gate-io-recovery `
    -ResultDirectory benchmarks/HotReload/results/2026-10-22-watch-memory-attribution/io-recovery `
    -ReuseMemoryEvidence bin/WatchMemory/gate
Get-Content bin/WatchMemory/gate-io-recovery/journal.log -Tail 20 -Wait
```

The original gate, source manifest, binary closures, completed session archives and failed partial attempt are preserved. The new source manifest explicitly links the previous manifest hash and admits changes only in the session/controller PowerShell files and the new publication regression test. Every other measurement, profiler and compiler input must match. Original host/profiler binaries and compiler closures are copied and hash-checked instead of being presented as newly built measurements. The original runner source is preserved under `provenance`, with an exact hash matching the first source manifest.

Successful original sessions are reused with their original completion timestamps, measurement source fingerprint, session paths and success-marker hashes. They are not reported as new runs. Each copied archive distinguishes its measurement fingerprint from the recovery controller fingerprint. The partial revision-25 attempt is excluded; that entire 100-edit session restarts from revision one. The remaining natural and forced-GC sessions then continue, followed by summary generation. No 500-edit extension or new optimization is automatically launched.

The [parent methodology](../README.md) remains applicable. This is still a staged memory study, not a completed real-world critical-path or leak-attribution report. Raw process dumps and local address indexes remain outside Git. Experimental defaults and production compiler sources are unchanged.
