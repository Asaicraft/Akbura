# Global-alias limitation

Status: **unsupported compiler-output case, excluded from successful latency summaries**. No generator fix is included in this investigation.

A disposable input contains the existing WatchFixture `Counter.akbura`/code-behind, the evaluated Gallery references and one additional C# tree:

```csharp
global using InvestigationAlias = System.Text.StringBuilder;
```

The normal non-`STATS` Debug generator, run with a fresh driver, produces a duplicate alias in `Akbura.Component.Counter…g.cs`. The compiler reports `CS1537`: the alias already appeared in that namespace. The same error occurs on the initial pass and both final fresh-driver checks. Agreement between independently created fresh drivers reproduces the limitation; it does not make erroneous output a successful Hot Reload result.

[Evidence](evidence.json) records the actual diagnostic identity/location, SDK/analyzer/input hashes and both parity checks. The [compressed raw run](production-fresh-alias.jsonl.gz) preserves the original records; its elapsed fields are not included in performance comparisons.

To reproduce, prepare the regular benchmark inputs, create a disposable copy of the two WatchFixture files, and export an input JSON using those files plus the alias tree and the evaluated Gallery `ReferencePath` set. Set its project directory to the copy and root namespace to `Demo`, then run:

```powershell
dotnet bin/CSharpInvalidationInvestigation/production-tool/HotReloadBenchmarks.dll `
  invalidation <alias-inputs.json> Counter.akbura fresh-driver off
```

This also distinguishes the issue from the valid namespace `global-using` cases in the main matrix. The original failed Gallery alias attempt remains outside the successful matrix; `../harness-update.json` records the switch to namespace imports without changing either analyzer binary.
