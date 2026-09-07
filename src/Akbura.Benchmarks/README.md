# FeatureGallery generator benchmarks

## Meaningful incremental work

Run from the repository root, without a debugger:

```powershell
dotnet run --project src/Akbura.Benchmarks/Akbura.Benchmarks.csproj -c Release -- --filter "*FeatureGalleryIncrementalBenchmarks*" --maxWidth 32
```

To measure only BlackSilence, use `--filter "*FeatureGalleryIncrementalBenchmarks.BlackSilence*"`.
The full comparison includes Furioso; several-second operations mean this is not a seconds-long smoke test.

| Scenario | Actual change |
| --- | --- |
| `ComponentValueEdit` | Text in the real `Pages/AkcssPage.akbura` component. |
| `ComponentBindingEdit` | A binding path in the real `Components/MarkupExtensionPrefixDemo.akbura` component. |
| `ComponentContractEdit` | Rename an exported component parameter and update its expression and consumer. |
| `SharedAkcssValueEdit` | Change a base AKCSS value used through an imported `@apply` class and a component. |
| `ReferencedCSharpRename` | Rename a used C# member and update the consuming component in the same edit. |

The complete MSBuild-loaded FeatureGallery is retained. The last three scenarios add small named virtual
`IncrementalBenchmarks` fixtures to that project. No source files on disk are edited by the benchmark.

The shared-AKCSS fixture uses the distinctive names `AkburaBenchmarkSharedStyleBaseModule`,
`AkburaBenchmarkSharedStyleImportedModule` and `AkburaBenchmarkSharedStyleConsumer`. Setup rejects
name collisions in the original gallery's source paths, source text and C# identifiers. The dependency
chain remains base module -> imported `@apply` class -> component. This isolates the intended chain:
the earlier generic `Imported.akcss` name accidentally matched the gallery's `"Imported modules"` label,
causing conservative invalidation of unrelated gallery documents. Results from that earlier fixture
are not measurements of the same isolated workload; changing the names does not narrow the generator's
dependency rules or remove the fresh/incremental correctness checks.

### What is timed

Each invocation applies the next meaningful revision, calls `RunGenerators`, checks for generator failure,
and retains the returned driver. Revisions alternate A -> B -> A: this is a **steady-state editing session**,
not a cold compilation and not an isolated first edit. Every invocation changes input, including pilot,
warmup and memory-diagnoser invocations. There is no replay of an already-applied edit or reset to an older
driver with a mutated generator-owned cache.

The same two prepared revisions are used throughout a case. Roslyn's underlying C# compilations are warm;
this does not measure construction of arbitrary new C# compilation objects. The C# rename scenario changes
both the declaration and its consumer; it is not a C#-only edit.

Project loading, preparation of source text and edit pairs, fresh-output validation and initial generation
are outside timing. `RunGeneratorsAndUpdateCompilation` and compiler diagnostic checks run only during
setup; measured operations do not include compiling the generated C# or emitting an application.
BlackSilence diagnostics are explicitly in `Publish` mode, not silently disabled for the comparison.

### Correctness before timing

For each selected generator and scenario, `GlobalSetup`:

1. Generates revision A from a fresh driver.
2. Applies A -> B and compares the result with a fresh generation of B.
3. Applies B -> A and compares the result with the original fresh generation of A.
4. Requires at least one generated source to change between A and B, and prints the changed hint names.

Comparisons include exact generated text by hint name and driver/generator/compiler diagnostics, including
warnings, hidden diagnostics, locations and multiplicity. Only the four documented diagnostic transport
properties are excluded. Every checked generated compilation must have zero errors. This is per-generator
fresh/incremental equivalence, not an assertion that Furioso and BlackSilence emit identical implementations.
A failed check aborts the case instead of reporting a fast but incorrect result.

### Measurement settings

- 4-8 warmup iterations and 15-30 measured iterations, chosen adaptively.
- Target relative error: 3% (half of the 99.9% confidence interval divided by the mean).
- Minimum iteration time: 250 ms; BenchmarkDotNet can increase the invocation count for fast operations.
- No `IterationSetup` and no fixed `InvocationCount=1` in this suite. Slow operations may still use one invocation.
- Mean, error, standard deviation, median, minimum, maximum, allocations and GC counts are reported.

The 30-iteration limit bounds runtime; it does **not** guarantee that a noisy machine will reach 3% error.
Keep the machine plugged in and avoid concurrent builds, tests and other heavy work. Inspect warnings and
the distribution, not only the ratio. A ratio rounded to `0.000` is not zero execution time.
For an intentionally longer run, override `--minIterationCount 30 --maxIterationCount 60`.

Reports and raw logs are written under `BenchmarkDotNet.Artifacts`.

## Build-only verification

```powershell
dotnet run --project src/Akbura.Benchmarks/Akbura.Benchmarks.csproj -c Release -- --verify-benchmark-build
```

This mode generates and builds the actual BenchmarkDotNet harness for the incremental suite. It never
calls the executor, benchmark methods or their setup methods. It checks compilation, not runtime scenario
correctness or performance. The runtime correctness checks above execute when the benchmark is started.

Benchmark jobs pass matching `EnableAkburaStats=false`, `EnableQuickScanBenchmark=false` and
`AkburaEmbedSourceFiles=false` MSBuild properties from the harness root, including SDK-injected transitive
references. `BuildInParallel=false` also prevents concurrent output writes. No temporary environment-variable
workaround is required for the commands above.

## Opt-in ComponentValueEdit diagnostic profile

This separate command profiles only the real `Pages/AkcssPage.akbura` text edit. It does not invoke
BenchmarkDotNet and is not a statistical performance result. Run it explicitly when stage-level diagnosis
is wanted; it is never started by the ordinary benchmark or build-verification commands.

```powershell
dotnet run --project src/Akbura.Benchmarks/Akbura.Benchmarks.csproj -c ReleaseStats -p:EnableAkburaStats=true -p:EnableQuickScanBenchmark=false -p:AkburaEmbedSourceFiles=false -p:BuildInParallel=false -- --component-value-profile --roundtrips 2 --output artifacts/component-value-profile
```

`--help` works in any build; measurement requires `STATS` and otherwise throws. The defaults are one
additional warm A -> B -> A roundtrip after correctness validation, then two measured roundtrips (four
samples). Each edit advances the current driver and uses diagnostics `Publish`.

Independently fresh A and B use different virtual project paths, so the process-wide incremental parse
cache cannot supply their reference parses. Exact source and driver/generator/compiler diagnostics are
compared after relocating only those known virtual roots (including escaped and twice-escaped serialized
mapped paths); only the four documented diagnostic transport
properties are ignored. Initial A, both directions, warmup and every measured transition are checked.
Generated compilations must have no errors and each measured transition must change generated text.
Validation, `GetRunResult`, snapshot inspection and report serialization are outside timing, including
between samples; this may affect subsequent cache warmth.

`profile.json` records all STATS counters, every instrumentation stage, and regenerated/reused source and
diagnostic identities from the actual published immutable snapshots. `report.md` provides the same stage
and entry breakdown in readable form. Driver allocation totals use process-wide `GC.GetTotalAllocatedBytes(true)`.
Stage allocations use same-thread deltas, aggregated across parallel workers; cross-thread scope deltas
are excluded and reported as invalid. Stage durations and allocations are **inclusive and nonadditive**:
do not sum nested stages or treat their total as driver wall time/allocated bytes. Instrumentation overhead
is included. In particular, a parent `DocumentBatch` scope's bytes exclude allocations on parallel worker
threads, unlike its wall time; subtracting child bytes from parent bytes cannot establish exclusive
allocation attribution. Incremental step tracking is enabled for this profile, including its overhead,
without changing ordinary benchmark settings. `CSharpProbeCompilation` measures base probe construction
only; `CSharpProbeBinding` includes nested semantic-model creation and diagnostics. This command diagnoses
work; it does not optimize it.

### Repeated AKCSS work

The opt-in profile also enables per-operation tracking for nonempty AKCSS lookup layers and utility
parameter binding. The JSON Statistics.OperationMeasurements array contains every completed
key, including first-invocation and repeated inclusive time/bytes, invocation counts and invalid allocation
samples. The readable report shows total calls, unique declaration keys (ignoring owner), owner/declaration
keys, same-owner repeats, additional-owner first calls and the top ten declaration/owner keys by repeated
bytes. First totals in combined-owner rows sum each owner's first invocation, not one arbitrary first call
for the whole declaration.

Keys use operation, semantic-model owner reference, red syntax-root reference, span and item count.
OwnerId and SourceId identify references only within one measurement and must not be joined across
samples. Names and paths are labels, never identity; an empty path denotes an unlocated foreign root and
does not merge unrelated sources. Display summaries relocate only the known profile virtual root and
retain other source paths. Raw per-key rows remain available for inspection.

Repeated allocations are observed inclusive work, **not achievable cache savings or exclusive costs**.
An AKCSS lookup includes nested utility-parameter binding, so totals from those operations must not be
added. Scope/thread restrictions still apply, and operation tracking adds overhead to this diagnostic
profile only. The instrumentation does not change generator output/parity checks.

Lookup descriptor binding is now cached within one semantic-model context, keyed by the exact red
member-list node. The AkcssLookupSymbols operation measures actual descriptor construction on cache
misses; AkcssLookupRequestCount and AkcssLookupReusedCount distinguish requests from cache hits.
Fresh mutable symbol wrappers are still created on every lookup. Parameterless utilities return an empty
parameter array before the CSharpUtilityParameters stage, which therefore counts only actual probes.
The cache is not exported through reusable semantic state. Parallel cold misses may compute twice;
completed descriptors are published atomically without holding a lock during binding.

## Legacy measurements

`FeatureGalleryGeneratorBenchmarks` remains available for the existing cold/no-change/EOF-whitespace and
unused-empty-C#-type scenarios. Those cases measure different work and should not be used to claim that
meaningful code generation became thousands of times faster. The diagnostic and stage suites remain separate.
