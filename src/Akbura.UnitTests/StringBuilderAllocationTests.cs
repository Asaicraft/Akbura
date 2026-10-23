using Akbura.Language;
using Akbura.Language.Binder;
using Akbura.Language.CodeGeneration;
using Akbura.Pools;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Xunit.Abstractions;

namespace Akbura.UnitTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class StringBuilderMeasurements
{
    public const string Name = "StringBuilder lifecycle measurements";
}

// Timing is observational. Assertions compare text, never performance thresholds.
[Collection(StringBuilderMeasurements.Name)]
public sealed class StringBuilderAllocationTests(ITestOutputHelper output)
{
    private static int s_sink;

    [Fact]
    public void MeasureFullLifecycles()
    {
        output.WriteLine($"{RuntimeInformation.FrameworkDescription}; {RuntimeInformation.OSDescription}; " +
            $"CPU={Environment.ProcessorCount}; ServerGC={System.Runtime.GCSettings.IsServerGC}");
        foreach (var length in new[] { 0, 8, 32, 128, 512, 1024, 4096, 16384 })
        {
            var text = new string('x', length);
            var operations = new Func<string>[]
            {
                () => Ordinary(text, false), () => Direct(text), () => Manual(text),
                () => Finally(text), () => Hinted(text), () => Ordinary(text, true),
            };
            foreach (var operation in operations) Assert.Equal(text, operation());
            Measure(["new", "direct", "manual", "finally", "hinted", "new-hinted"], operations, length,
                Math.Max(512, 262144 / (length + 1)));
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string Ordinary(string text, bool hint)
    {
        var builder = hint ? new StringBuilder(text.Length) : new StringBuilder();
        Append(builder, text);
        return builder.ToString();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string Direct(string text)
    {
        var pooled = PooledStringBuilder.GetInstance();
        Append(pooled.Builder, text);
        return pooled.ToStringAndFree();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string Manual(string text)
    {
        var pooled = PooledStringBuilder.GetInstance();
        Append(pooled.Builder, text);
        var result = pooled.Builder.ToString();
        pooled.Free();
        return result;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string Finally(string text)
    {
        var pooled = PooledStringBuilder.GetInstance();
        try { Append(pooled.Builder, text); return pooled.Builder.ToString(); }
        finally { pooled.Free(); }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string Hinted(string text)
    {
        var pooled = PooledStringBuilder.GetInstance();
        pooled.Builder.EnsureCapacity(text.Length);
        Append(pooled.Builder, text);
        return pooled.ToStringAndFree();
    }

    private static void Append(StringBuilder builder, string text)
    {
        for (var index = 0; index < text.Length; index += 8)
            builder.Append(text, index, Math.Min(8, text.Length - index));
    }

    [Fact]
    public void MeasureColdRetentionAndConcurrency()
    {
        var started = Stopwatch.GetTimestamp();
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var coldPool = PooledStringBuilder.CreatePool(32);
        var cold = coldPool.Allocate();
        cold.Builder.Append("cold-rental");
        var coldText = cold.ToStringAndFree();
        output.WriteLine($"COLD private pool + first rental: B={GC.GetAllocatedBytesForCurrentThread() - allocated}; " +
            $"ns={Stopwatch.GetElapsedTime(started).TotalNanoseconds:F1}; text={coldText}");
        foreach (var length in new[] { 32, 1024, 4096, 16384 })
        {
            var pool = PooledStringBuilder.CreatePool(32);
            var before = GC.GetTotalMemory(true);
            var references = FillPool(pool, length);
            var after = GC.GetTotalMemory(true);
            output.WriteLine($"RETAIN length={length}; live={references.Count(reference => reference.IsAlive)}; " +
                $"heapDelta={after - before}");
            GC.KeepAlive(pool);
        }

        var text = new string('x', 128);
        for (var kind = 0; kind < 2; kind++)
        {
            var pooled = kind == 1;
            Parallel.For(0, 1024, _ => { if (pooled) Direct(text); else Ordinary(text, false); });
            var bytes = GC.GetTotalAllocatedBytes(true);
            var gen0 = GC.CollectionCount(0);
            started = Stopwatch.GetTimestamp();
            var sum = 0;
            Parallel.For(0, 32768, () => 0,
                (_, _, local) => local + (pooled ? Direct(text) : Ordinary(text, false)).Length,
                local => Interlocked.Add(ref sum, local));
            var elapsed = Stopwatch.GetElapsedTime(started).TotalNanoseconds;
            output.WriteLine($"PARALLEL pooled={pooled}; B/op={(GC.GetTotalAllocatedBytes(true) - bytes) / 32768.0:F2}; " +
                $"ns/op={elapsed / 32768:F1}; gen0={GC.CollectionCount(0) - gen0}");
            Assert.Equal(32768 * text.Length, sum);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference[] FillPool(ObjectPool<PooledStringBuilder> pool, int length)
    {
        var leases = Enumerable.Range(0, 32).Select(_ => pool.Allocate()).ToArray();
        var references = leases.Select(static lease => new WeakReference(lease)).ToArray();
        foreach (var lease in leases) { lease.Builder.EnsureCapacity(length); lease.Builder.Append('x', length); lease.Free(); }
        return references;
    }

    [Fact]
    public void MeasureCompilerWorkloads()
    {
        var sanitize = typeof(AkburaSemanticModel).GetMethod("ToCSharpIdentifier", BindingFlags.Static | BindingFlags.NonPublic)!
            .CreateDelegate<Func<string, string>>();
        var signature = typeof(MarkupBinder).GetMethod("GetTailwindCompletionSignatureKey", BindingFlags.Static | BindingFlags.NonPublic)!
            .CreateDelegate<Func<TailwindUtilityLookupCandidate, string>>();
        var candidate = new TailwindUtilityLookupCandidate("padding", [
            new("horizontal", "System.Double", false), new("vertical", "System.Double", true),
        ], "Avalonia.Controls.Border");
        var fixtures = new[]
        {
            "using System.Collections.Generic; using Alias = System.Tuple<int, string>; <TextBlock />",
            "<Border>$if (Call(1, values[2]) /* } */ && text != \"(\") { <TextBlock Text=\"hello\\n世界\" /> }</Border>",
            "<StackPanel>$foreach (var item in items.Where(x => x.Text != \"}\")) { <TextBlock Text={item.Text} /> }</StackPanel>",
            "@using Avalonia.Controls; @utilities { Border.pad-4 { Padding: 4; } }",
        };
        for (var index = 0; index < fixtures.Length; index++)
        {
            var source = fixtures[index];
            Measure(["parse-" + index], [() => AkburaSyntaxTree.ParseText(source, "Fixture.akbura").GetRoot().ToFullString()], index, 512);
        }
        Measure(["identifier", "generated-name", "fingerprint", "tailwind-signature", "identifier-array"], [
            () => sanitize("9component-имя.世界"),
            () => ComponentHotReloadIdentity.CreateGeneratedName("9component-имя.世界", "state:count:System.Int32:value"),
            () => AkcssHotReloadIdentity.Create("void Apply() { target.Width = 12; target.Height = 24; }"),
            () => signature(candidate),
            () => SanitizeWithArray("9component-имя.世界"),
        ], 0, 4096);
    }

    private static string SanitizeWithArray(string value)
    {
        var characters = value.ToCharArray();
        for (var index = 0; index < characters.Length; index++)
        {
            var character = characters[index];
            if (!(index == 0
                ? Microsoft.CodeAnalysis.CSharp.SyntaxFacts.IsIdentifierStartCharacter(character)
                : Microsoft.CodeAnalysis.CSharp.SyntaxFacts.IsIdentifierPartCharacter(character)))
                characters[index] = '_';
        }
        return new string(characters);
    }

    private void Measure(string[] names, Func<string>[] operations, int size, int iterations)
    {
        var samples = new List<(double Bytes, double Ns, int Gen0)>[operations.Length];
        for (var index = 0; index < operations.Length; index++)
        {
            samples[index] = [];
            for (var warmup = 0; warmup < 32; warmup++) s_sink ^= operations[index]().Length;
        }
        for (var round = 0; round < 7; round++)
        for (var position = 0; position < operations.Length; position++)
        {
            var index = (round + position) % operations.Length;
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            var before = GC.GetAllocatedBytesForCurrentThread();
            var gen0 = GC.CollectionCount(0);
            var started = Stopwatch.GetTimestamp();
            var sum = 0;
            for (var iteration = 0; iteration < iterations; iteration++) sum ^= operations[index]().Length;
            var elapsed = Stopwatch.GetElapsedTime(started).TotalNanoseconds;
            var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            s_sink ^= sum;
            samples[index].Add((bytes / (double)iterations, elapsed / iterations, GC.CollectionCount(0) - gen0));
        }
        for (var index = 0; index < operations.Length; index++)
        {
            var sorted = samples[index].OrderBy(static sample => sample.Ns).ToArray();
            output.WriteLine($"MEASURE {names[index]} size={size}; B/op={samples[index].Average(static sample => sample.Bytes):F2}; " +
                $"ns/op={sorted[3].Ns:F1}; range={sorted[0].Ns:F1}..{sorted[^1].Ns:F1}; " +
                $"gen0={samples[index].Sum(static sample => sample.Gen0)}/7x{iterations}");
        }
    }
}
