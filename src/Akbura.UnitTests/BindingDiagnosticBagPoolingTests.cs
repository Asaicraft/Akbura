using Akbura.Language;
using Akbura.Language.Binder;
using Microsoft.CodeAnalysis;

namespace Akbura.UnitTests;

public sealed class BindingDiagnosticBagPoolingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClearAndFree_StartNewDeduplicationScopesAndPreserveSnapshots(bool concurrent)
    {
        var (semantic, csharp) = CreateDiagnostics();
        for (var cycle = 0; cycle < 64; cycle++)
        {
            var bag = concurrent ? BindingDiagnosticBag.GetConcurrentInstance() : BindingDiagnosticBag.GetInstance();
            try
            {
                Assert.True(bag.IsEmpty);
                bag.Add(semantic[0]);
                bag.Add(semantic[1]);
                bag.Add(semantic[0]);
                bag.AddCSharp(csharp[0]);
                bag.AddCSharp(csharp[1]);
                bag.AddCSharp(csharp[0]);
                var snapshot = bag.ToReadOnly();
                Assert.Equal<AkburaSemanticDiagnostic>(semantic, snapshot.SemanticDiagnostics);
                Assert.Equal<Diagnostic>(csharp, snapshot.CSharpDiagnostics);
                bag.Clear();
                Assert.Equal(0, bag.Count);
                bag.Add(semantic[0]);
                bag.AddCSharp(csharp[0]);
                Assert.Single(bag.ToSemanticDiagnostics());
                Assert.Single(bag.ToCSharpDiagnostics());
                Assert.Equal<AkburaSemanticDiagnostic>(semantic, snapshot.SemanticDiagnostics);
                Assert.Equal<Diagnostic>(csharp, snapshot.CSharpDiagnostics);
            }
            finally
            {
                bag.Free();
            }
        }
    }

    [Fact]
    public async Task ConcurrentInitializationAndMixedAdds_AreJoinedBeforeRelease()
    {
        var (semantic, csharp) = CreateDiagnostics();
        for (var cycle = 0; cycle < 32; cycle++)
        {
            var bag = BindingDiagnosticBag.GetConcurrentInstance();
            try
            {
                await Task.WhenAll(Enumerable.Range(0, 16).Select(index => Task.Run(() =>
                {
                    for (var repeat = 0; repeat < 32; repeat++)
                    {
                        bag.Add(semantic[index % semantic.Length]);
                        bag.AddCSharp(csharp[index % csharp.Length]);
                    }
                })));
                Assert.Equal(4, bag.Count);
                Assert.All(semantic, diagnostic => Assert.Contains(diagnostic, bag.ToSemanticDiagnostics()));
                Assert.All(csharp, diagnostic => Assert.Contains(diagnostic, bag.ToCSharpDiagnostics()));
                bag.Clear();
                bag.Add(semantic[0]);
                bag.AddCSharp(csharp[0]);
                Assert.Equal(2, bag.Count);
            }
            finally
            {
                bag.Free();
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExceptionAndCancellationUnwindBeforeFree_AndNextRentalHasNoStaleKeys(bool canceled)
    {
        var (semantic, csharp) = CreateDiagnostics();
        for (var cycle = 0; cycle < 16; cycle++)
        {
            var bag = BindingDiagnosticBag.GetInstance();
            var failure = canceled ? (Exception)new OperationCanceledException() : new InvalidOperationException();
            IEnumerable<AkburaSemanticDiagnostic> FailingSource()
            {
                yield return semantic[0];
                throw failure;
            }
            try
            {
                bag.AddCSharp(csharp[0]);
                Assert.Same(failure, Record.Exception(() => bag.AddRange(FailingSource())));
                Assert.Single(bag.ToSemanticDiagnostics());
            }
            finally
            {
                bag.Free();
            }

            var next = BindingDiagnosticBag.GetInstance();
            try
            {
                Assert.True(next.IsEmpty);
                next.Add(semantic[0]);
                next.AddCSharp(csharp[0]);
                Assert.Equal(2, next.Count);
            }
            finally
            {
                next.Free();
            }
        }
    }

    [Fact]
    public void DiscardedAndEmptyBags_StayEmptyAcrossLifecycleOperations()
    {
        var (semantic, csharp) = CreateDiagnostics();
        var discarded = BindingDiagnosticBag.GetInstance(withDiagnostics: false);
        Assert.Same(BindingDiagnosticBag.Discarded, discarded);
        discarded.Add(semantic[0]);
        discarded.AddCSharp(csharp[0]);
        discarded.Add((AkburaSemanticDiagnostic)null!);
        discarded.AddCSharp(null!);
        discarded.Clear();
        Assert.True(discarded.ToReadOnlyAndFree().IsEmpty);
        Assert.True(BindingDiagnosticBag.Discarded.IsEmpty);

        var empty = BindingDiagnosticBag.GetConcurrentInstance();
        Assert.True(empty.ToReadOnlyAndFree().IsEmpty);
    }

    private static (AkburaSemanticDiagnostic[] Semantic, Diagnostic[] CSharp) CreateDiagnostics()
    {
        var syntax = AkburaSyntaxTree.ParseText("state int value = 0;", "Pool.akbura").GetRoot().Members[0];
        var descriptor = new DiagnosticDescriptor("POOL", "Title", "Message {0}", "Test", DiagnosticSeverity.Warning, true);
        // Case differences must remain distinct in both diagnostic channels.
        return (
            [new AkburaSemanticDiagnostic(syntax, ErrorCodes.ERR_SyntaxError, ["value"]),
             new AkburaSemanticDiagnostic(syntax, ErrorCodes.ERR_SyntaxError, ["VALUE"])],
            [Diagnostic.Create(descriptor, Microsoft.CodeAnalysis.Location.None, "value"),
             Diagnostic.Create(descriptor, Microsoft.CodeAnalysis.Location.None, "VALUE")]);
    }
}
