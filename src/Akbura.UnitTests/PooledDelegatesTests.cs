using Akbura.Pools;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Akbura.UnitTests;

public sealed class PooledDelegatesTests
{
    [Fact]
    public void Functions_BindStateAfterEverySupportedArgumentList()
    {
        using var zero = PooledDelegates.GetPooledFunction(static (int state) => state + 1, 10, out var f0);
        using var one = PooledDelegates.GetPooledFunction(static (int a, int state) => a * 10 + state, 2, out var f1);
        using var two = PooledDelegates.GetPooledFunction(static (int a, int b, int state) => a * 100 + b * 10 + state, 3, out var f2);
        using var three = PooledDelegates.GetPooledFunction(static (int a, int b, int c, int state) => a * 1000 + b * 100 + c * 10 + state, 4, out var f3);

        Assert.Equal(11, f0());
        Assert.Equal(12, f1(1));
        Assert.Equal(123, f2(1, 2));
        Assert.Equal(1234, f3(1, 2, 3));
    }

    [Fact]
    public void Actions_BindStateAfterEverySupportedArgumentList()
    {
        var values = new List<int>();
        using var zero = PooledDelegates.GetPooledAction(static (List<int> state) => state.Add(0), values, out var a0);
        using var one = PooledDelegates.GetPooledAction(static (int a, List<int> state) => state.Add(a), values, out var a1);
        using var two = PooledDelegates.GetPooledAction(static (int a, int b, List<int> state) => state.Add(a * 10 + b), values, out var a2);
        using var three = PooledDelegates.GetPooledAction(static (int a, int b, int c, List<int> state) => state.Add(a * 100 + b * 10 + c), values, out var a3);

        for (var index = 0; index < 2; index++)
        {
            a0();
            a1(1);
            a2(1, 2);
            a3(1, 2, 3);
        }
        Assert.Equal([0, 1, 12, 123, 0, 1, 12, 123], values);
    }

    [Fact]
    public void ReferenceTupleAndNullState_ArePassedWithoutChangingIdentity()
    {
        var instance = new object();
        using var reference = PooledDelegates.GetPooledFunction(static (object state) => state, instance, out var identity);
        using var tuple = PooledDelegates.GetPooledFunction(static (string text, (object Item, int Offset) state) =>
            (state.Item, text.Length + state.Offset), (instance, 7), out var project);
        using var nullable = PooledDelegates.GetPooledFunction(static (string? state) => state ?? "null", null, out var nullValue);

        Assert.Same(instance, identity());
        var result = project("abc");
        Assert.Same(instance, result.Item1);
        Assert.Equal(10, result.Item2);
        Assert.Equal("null", nullValue());
    }

    [Fact]
    public void ConditionalWeakTableCallback_IsConsumedBeforeRelease()
    {
        var table = new ConditionalWeakTable<object, List<int>>();
        var key = new object();
        using var lease = PooledDelegates.GetPooledCreateValueCallback(
            static (object _, int state) => new List<int> { state }, 42, out var callback);
        var first = table.GetValue(key, callback);
        Assert.Same(first, table.GetValue(key, callback));
        Assert.Equal(42, Assert.Single(first));
    }

    [Fact]
    public void LaterRental_ReusesBoundDelegateAndObservesNewState()
    {
        Func<int> previous;
        using (PooledDelegates.GetPooledFunction(static (ReuseState state) => state.Value, new ReuseState(1), out previous))
        {
            Assert.Equal(1, previous());
        }
        for (var value = 2; value < 65; value++)
        {
            using var lease = PooledDelegates.GetPooledFunction(static (ReuseState state) => -state.Value,
                new ReuseState(value), out var current);
            Assert.Same(previous, current);
            Assert.Equal(-value, current());
        }
    }

    [Fact]
    public void ActiveRentals_RemainIndependentWhenAnotherIsReleased()
    {
        using var first = PooledDelegates.GetPooledFunction(static (ActiveState state) => state.Value, new ActiveState(1), out var f1);
        using (PooledDelegates.GetPooledFunction(static (ActiveState state) => state.Value, new ActiveState(2), out var f2))
        {
            Assert.NotSame(f1, f2);
            Assert.Equal(1, f1());
            Assert.Equal(2, f2());
        }
        using var third = PooledDelegates.GetPooledFunction(static (ActiveState state) => state.Value, new ActiveState(3), out var f3);
        Assert.NotSame(f1, f3);
        Assert.Equal(1, f1());
        Assert.Equal(3, f3());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ThrowingCallback_UsingReturnsWrapperAndPreservesException(bool cancel)
    {
        Exception expected = cancel ? new OperationCanceledException() : new InvalidOperationException();
        Func<int> previous = null!;
        var actual = Record.Exception(() =>
        {
            using var lease = PooledDelegates.GetPooledFunction(static (ExceptionState state) =>
                state.Error == null ? 42 : throw state.Error, new ExceptionState(expected), out previous);
            previous();
        });
        Assert.Same(expected, actual);
        using var next = PooledDelegates.GetPooledFunction(static (ExceptionState state) =>
            state.Error == null ? 42 : throw state.Error, new ExceptionState(null), out var current);
        Assert.Same(previous, current);
        Assert.Equal(42, current());
    }

    [Fact]
    public void ReentrantRental_DoesNotOverwriteOuterState()
    {
        using var outer = PooledDelegates.GetPooledFunction(Reenter, new ReentrantState(10), out var callback);
        Assert.Equal(21, callback());
        Assert.Equal(21, callback());
    }

    private static int Reenter(ReentrantState state)
    {
        if (state.Value == 11)
            return state.Value;
        using var inner = PooledDelegates.GetPooledFunction(Reenter, new ReentrantState(state.Value + 1), out var callback);
        return callback() + state.Value;
    }

    [Fact]
    public void ConcurrentRentals_NeverPublishTheSameActiveWrapper()
    {
        var active = new ConcurrentDictionary<Delegate, byte>();
        Parallel.For(0, 2048, index =>
        {
            using var lease = PooledDelegates.GetPooledFunction(static (ConcurrentState state) => state.Value,
                new ConcurrentState(index), out var callback);
            Assert.True(active.TryAdd(callback, 0));
            try
            {
                for (var invocation = 0; invocation < 8; invocation++)
                {
                    Thread.Yield();
                    Assert.Equal(index, callback());
                }
            }
            finally
            {
                Assert.True(active.TryRemove(callback, out _));
            }
        });
        Assert.Empty(active);
    }

    [Fact]
    public void Release_ClearsSyntaxTreeStateAndCompilationDelegateTarget()
    {
        var (tree, target, unbound, bound) = RentAndReleaseSyntaxState();
        for (var attempt = 0; attempt < 3 && (tree.IsAlive || target.IsAlive || unbound.IsAlive); attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        Assert.False(tree.IsAlive);
        Assert.False(target.IsAlive);
        Assert.False(unbound.IsAlive);
        // Keep the wrapper alive to prove its cleared fields cannot retain the objects.
        GC.KeepAlive(bound);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Tree, WeakReference Target, WeakReference Unbound, Func<int, int> Bound) RentAndReleaseSyntaxState()
    {
        var tree = CSharpSyntaxTree.ParseText("class C { int M() => 42; }");
        var target = new CompilationTarget(CSharpCompilation.Create("Retention", [tree]));
        Func<int, SyntaxTree, int> unbound = target.Invoke;
        using var lease = PooledDelegates.GetPooledFunction(unbound, tree, out var bound);
        Assert.Equal(43, bound(42));
        return (new WeakReference(tree), new WeakReference(target), new WeakReference(unbound), bound);
    }

    private sealed record ReuseState(int Value);
    private sealed record ActiveState(int Value);
    private sealed record ExceptionState(Exception? Error);
    private readonly record struct ReentrantState(int Value);
    private readonly record struct ConcurrentState(int Value);

    private sealed class CompilationTarget(Compilation compilation)
    {
        public int Invoke(int value, SyntaxTree tree) => value + (compilation.ContainsSyntaxTree(tree) ? 1 : 0);
    }
}
