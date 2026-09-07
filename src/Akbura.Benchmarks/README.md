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

## Legacy measurements

`FeatureGalleryGeneratorBenchmarks` remains available for the existing cold/no-change/EOF-whitespace and
unused-empty-C#-type scenarios. Those cases measure different work and should not be used to claim that
meaningful code generation became thousands of times faster. The diagnostic and stage suites remain separate.
