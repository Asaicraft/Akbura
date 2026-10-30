using Akbura.Collections;
using Akbura.Language;
using Akbura.Language.Binder;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Xunit.Abstractions;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;
using SyntaxFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Akbura.UnitTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PooledDelegateMeasurements
{
    public const string Name = "Pooled delegate lifecycle measurements";
}

// Run alone in Release to compare allocations and timing. Timing is observational;
// correctness and retention are asserted separately in PooledDelegatesTests.
[Collection(PooledDelegateMeasurements.Name)]
public sealed class PooledDelegateAllocationTests
{
    private readonly ITestOutputHelper _output;

    public PooledDelegateAllocationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void MeasurePredicateLifecyclesAndConcurrentRentals()
    {
        _output.WriteLine($"{RuntimeInformation.FrameworkDescription}; {RuntimeInformation.OSDescription}; " +
            $"CPU={Environment.ProcessorCount}; ServerGC={System.Runtime.GCSettings.IsServerGC}");
        var items = ImmutableArray.Create("alpha", "beta", "gamma", "target");
        const string prefix = "tar";
        Func<int>[] variants = [
            () => CapturingAny(items, prefix) ? 1 : 0,
            () => items.Any(static (value, state) => value.StartsWith(state, StringComparison.Ordinal), prefix) ? 1 : 0,
            () => PooledAny(items, prefix) ? 1 : 0,
        ];
        Measure(["capture", "state", "pooled"], variants, 1, 100000);
        Func<int>[] concurrent = variants.Select(variant => (Func<int>)(() =>
        {
            var total = 0;
            Parallel.For(0, 4, _ =>
            {
                var subtotal = 0;
                for (var iteration = 0; iteration < 1024; iteration++)
                    subtotal += variant();
                Interlocked.Add(ref total, subtotal);
            });
            return total;
        })).ToArray();
        Measure(["capture-4workers", "state-4workers", "pooled-4workers"], concurrent, 4096, 16, 4096);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool CapturingAny(ImmutableArray<string> items, string prefix) =>
        items.Any(value => value.StartsWith(prefix, StringComparison.Ordinal));

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool PooledAny(ImmutableArray<string> items, string prefix)
    {
        using var lease = PooledDelegates.GetPooledFunction(
            static (string value, string state) => value.StartsWith(state, StringComparison.Ordinal), prefix, out var predicate);
        return items.Any(predicate);
    }

    [Fact]
    public void MeasureReplaceNodesAndActualLocalFunctionProjection()
    {
        foreach (var count in new[] { 0, 1, 8, 32 })
        {
            var text = "int Project(" + string.Join(", ", Enumerable.Range(0, count).Select(index => "int p" + index)) + ") { return 0; }";
            var tree = AkburaSyntaxTree.ParseText("state int preceding = 0;\r\n" + text, "Projection.akbura");
            var owner = tree.GetRoot().Members.OfType<CSharpStatementSyntax>().Single();
            var function = Assert.IsType<CSharp.LocalFunctionStatementSyntax>(owner.GetRawCSharpStatement());
            var body = SyntaxFactory.Block();
            var parameters = function.ParameterList;
            var offset = owner.Tokens.FullSpan.Start - function.FullSpan.Start;
            var inputNodes = function.DescendantNodesAndSelf().ToArray();
            var original = CaptureProjectedLocalFunction(owner, function, body);
            var optimized = CSharpProbeBuilder.CreateProjectedLocalFunction(owner, function, body);
            Assert.Equal(original.ToFullString(), optimized.ToFullString());
            Assert.Equal(OriginKeys(original), OriginKeys(optimized));
            Measure(["replace-capture-" + count, "replace-pooled-" + count], [
                () => CaptureReplace(parameters, offset).Parameters.Count,
                () => PooledReplace(parameters, offset).Parameters.Count,
            ], count, count < 8 ? 4096 : 1024);
            // Read the returned root without materializing additional red children.
            Measure(["original-projection-" + count, "production-projection-" + count], [
                () => CaptureProjectedLocalFunction(owner, function, body).FullSpan.Length,
                () => CSharpProbeBuilder.CreateProjectedLocalFunction(owner, function, body).FullSpan.Length,
            ], original.FullSpan.Length, count < 8 ? 4096 : 1024);
            GC.KeepAlive(inputNodes);
        }
    }

    private static (Akbura.Language.Symbols.SymbolKind Kind, string Name, Microsoft.CodeAnalysis.Text.TextSpan Span)[] OriginKeys(SyntaxNode root) =>
        root.DescendantNodesAndSelf().SelectMany(static node => node.GetAnnotations(CSharpProbeBinder.ProjectedSymbolAnnotationKind))
            .Select(static annotation =>
            {
                Assert.True(CSharpProbeSymbolOrigin.TryParse(annotation.Data, out var origin));
                return (origin.Kind, origin.Name, origin.DeclarationSpan);
            }).ToArray();

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static CSharp.ParameterListSyntax CaptureReplace(CSharp.ParameterListSyntax parameters, int offset) =>
        parameters.ReplaceNodes(parameters.Parameters, (original, rewritten) => Annotate(original, rewritten, offset));

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static CSharp.ParameterListSyntax PooledReplace(CSharp.ParameterListSyntax parameters, int offset)
    {
        using var lease = PooledDelegates.GetPooledFunction(Annotate, offset,
            out Func<CSharp.ParameterSyntax, CSharp.ParameterSyntax, CSharp.ParameterSyntax> rewrite);
        return parameters.ReplaceNodes(parameters.Parameters, rewrite);
    }

    private static CSharp.ParameterSyntax Annotate(CSharp.ParameterSyntax original, CSharp.ParameterSyntax rewritten, int offset) =>
        rewritten.WithAdditionalAnnotations(new SyntaxAnnotation(CSharpProbeBinder.ProjectedSymbolAnnotationKind,
            new CSharpProbeSymbolOrigin(Guid.NewGuid().ToString("N"), Akbura.Language.Symbols.SymbolKind.CSharpSymbol,
                original.Identifier.ValueText, new Microsoft.CodeAnalysis.Text.TextSpan(
                    offset + original.Identifier.Span.Start, original.Identifier.Span.Length)).Serialize()));

    // Keep the original production implementation as a measurement baseline so
    // both versions run on the same inputs, process and runtime conditions.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static CSharp.LocalFunctionStatementSyntax CaptureProjectedLocalFunction(
        CSharpStatementSyntax owner, CSharp.LocalFunctionStatementSyntax localFunction, CSharp.BlockSyntax body)
    {
        var hostOffset = owner.Tokens.FullSpan.Start - localFunction.FullSpan.Start;
        var parameters = localFunction.ParameterList.Parameters;
        localFunction = localFunction.ReplaceNodes(
            parameters,
            (original, rewritten) => rewritten.WithAdditionalAnnotations(
                new SyntaxAnnotation(
                    CSharpProbeBinder.ProjectedSymbolAnnotationKind,
                    new CSharpProbeSymbolOrigin(
                        Guid.NewGuid().ToString("N"),
                        Akbura.Language.Symbols.SymbolKind.CSharpSymbol,
                        original.Identifier.ValueText,
                        new Microsoft.CodeAnalysis.Text.TextSpan(
                            hostOffset + original.Identifier.Span.Start,
                            original.Identifier.Span.Length))
                    .Serialize())));
        return localFunction
            .WithBody(body)
            .WithExpressionBody(null)
            .WithSemicolonToken(default)
            .WithAdditionalAnnotations(
                new SyntaxAnnotation(
                    CSharpProbeBinder.ProjectedSymbolAnnotationKind,
                    new CSharpProbeSymbolOrigin(
                        Guid.NewGuid().ToString("N"),
                        Akbura.Language.Symbols.SymbolKind.CSharpSymbol,
                        localFunction.Identifier.ValueText,
                        new Microsoft.CodeAnalysis.Text.TextSpan(
                            hostOffset + localFunction.Identifier.Span.Start,
                            localFunction.Identifier.Span.Length))
                    .Serialize()));
    }

    [Fact]
    public void MeasureColdRentalWarmRentalAndBoundedRetention()
    {
        // The private ColdState keeps this pool distinct from all warmed pools.
        var startBytes = GC.GetAllocatedBytesForCurrentThread();
        var start = Stopwatch.GetTimestamp();
        Assert.Equal(42, ColdRental(42));
        var elapsed = Stopwatch.GetElapsedTime(start).TotalNanoseconds;
        var bytes = GC.GetAllocatedBytesForCurrentThread() - startBytes;
        _output.WriteLine($"first-closed-generic-rental: {elapsed:F1} ns, {bytes} B (includes generic initialization/JIT)");
        Measure(["warm-rental"], [() => ColdRental(42)], 42, 100000);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var before = GC.GetTotalMemory(true);
        var references = BurstAndRelease();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var retained = references.Count(static reference => reference.IsAlive);
        _output.WriteLine($"burst=80; retained wrappers={retained}; retained-memory-delta={GC.GetTotalMemory(true) - before} B (GC estimate)");
        Assert.InRange(retained, 1, 20);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int ColdRental(int value)
    {
        using var lease = PooledDelegates.GetPooledFunction(static (ColdState state) => state.Value, new ColdState(value), out var callback);
        return callback();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference[] BurstAndRelease()
    {
        var leases = new PooledDelegates.Releaser[80];
        var references = new WeakReference[80];
        var rented = 0;
        try
        {
            for (var index = 0; index < leases.Length; index++)
            {
                leases[index] = PooledDelegates.GetPooledFunction(static (RetentionState state) => state.Value,
                    new RetentionState(index), out var callback);
                rented++;
                Assert.Equal(index, callback());
                references[index] = new WeakReference(callback.Target!);
            }
        }
        finally
        {
            // Each array entry is released exactly once and never used again.
            for (var index = 0; index < rented; index++)
                leases[index].Dispose();
        }
        return references;
    }

    private void Measure(string[] names, Func<int>[] variants, int expected, int iterations, int operationsPerIteration = 1)
    {
        var samples = variants.Select(static _ => new List<(double Ns, double Bytes, int Gen0)>()).ToArray();
        foreach (var variant in variants)
            for (var warmup = 0; warmup < 64; warmup++)
                Assert.Equal(expected, variant());
        for (var round = 0; round < 7; round++)
        for (var step = 0; step < variants.Length; step++)
        {
            var index = (step + round) % variants.Length;
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var allocated = operationsPerIteration == 1 ? GC.GetAllocatedBytesForCurrentThread() : GC.GetTotalAllocatedBytes(true);
            var gen0 = GC.CollectionCount(0);
            var start = Stopwatch.GetTimestamp();
            var checksum = 0;
            for (var iteration = 0; iteration < iterations; iteration++)
                checksum += variants[index]();
            var elapsed = Stopwatch.GetElapsedTime(start).TotalNanoseconds;
            var bytes = (operationsPerIteration == 1 ? GC.GetAllocatedBytesForCurrentThread() : GC.GetTotalAllocatedBytes(true)) - allocated;
            Assert.Equal(expected * iterations, checksum);
            var operations = (double)iterations * operationsPerIteration;
            samples[index].Add((elapsed / operations, bytes / operations, GC.CollectionCount(0) - gen0));
        }
        for (var index = 0; index < names.Length; index++)
        {
            var ordered = samples[index].OrderBy(static sample => sample.Ns).ToArray();
            var median = ordered[3];
            var allocations = samples[index].OrderBy(static sample => sample.Bytes).ToArray();
            _output.WriteLine($"{names[index]}: {median.Ns:F1} ns/op [{ordered[0].Ns:F1}..{ordered[6].Ns:F1}], " +
                $"{allocations[3].Bytes:F1} B/op [{allocations[0].Bytes:F1}..{allocations[6].Bytes:F1}], " +
                $"Gen0={median.Gen0}/{iterations * operationsPerIteration}");
        }
    }

    private readonly record struct ColdState(int Value);
    private readonly record struct RetentionState(int Value);
}
