using Akbura.Pools;
using System.Collections.Immutable;
using Akbura.AvaloniaEdit.Infrastructure;
using Akbura.Workspaces.Documents;
using Avalonia.Threading;
using Microsoft.CodeAnalysis.Text;

namespace Akbura.AvaloniaEdit.UnitTests;

public sealed class EditorSyntaxCacheTests
{
    [Fact]
    public Task CollidingEntriesAreEvictedAndRebuiltWithoutReturningOtherSyntax() => EditorTestHost.Run(async () =>
    {
        using var services = new EditorServiceQueue(new AkburaInlineScheduler());
        using var cache = new EditorSyntaxCache(services, CancellationToken.None, _ => true);
        var buffer = new object();
        var collision = FindCollision(buffer);
        var firstTask = cache.GetAsync(collision[0], CancellationToken.None);
        var first = await firstTask;
        Assert.Same(firstTask, cache.GetAsync(collision[0], CancellationToken.None));
        var second = await cache.GetAsync(collision[1], CancellationToken.None);
        Assert.NotSame(first, second);
        Assert.Equal(collision[1].Text.ToString(), second.Text.ToString());
        var rebuilt = await cache.GetAsync(collision[0], CancellationToken.None);
        Assert.NotSame(first, rebuilt);
        Assert.Equal(first.Text.ToString(), rebuilt.Text.ToString());
        cache.Clear();
        Assert.Null(cache.Accepted);
        Assert.NotSame(rebuilt, await cache.GetAsync(collision[0], CancellationToken.None));
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task CachedReadsRespectTheCurrentPendingSnapshot(bool returnToCachedSnapshot) => EditorTestHost.Run(async () =>
    {
        var scheduler = new ControlledScheduler();
        using var services = new EditorServiceQueue(scheduler);
        var buffer = new object();
        var firstKey = Key(buffer, 0);
        var nextKey = FindOtherBucket(buffer, firstKey);
        var current = firstKey;
        using var cache = new EditorSyntaxCache(services, CancellationToken.None, key => key == current);
        var first = await cache.GetAsync(firstKey, CancellationToken.None);
        current = nextKey;
        var held = scheduler.HoldNextResult();
        var pending = cache.GetAsync(nextKey, CancellationToken.None);
        await held.Started.Task;
        if (returnToCachedSnapshot)
        {
            current = firstKey;
        }
        Assert.Same(first, await cache.GetAsync(firstKey, CancellationToken.None));
        held.Release.TrySetResult();
        var next = await pending;
        Assert.Same(returnToCachedSnapshot ? first : next, cache.Accepted);
    });

    [Fact]
    public Task AnOlderRevisionCannotReplaceANewerEntryWhenCancellationIsIgnored() => EditorTestHost.Run(async () =>
    {
        var scheduler = new ControlledScheduler();
        using var services = new EditorServiceQueue(scheduler);
        var buffer = new object();
        var current = Key(buffer, 0);
        using var cache = new EditorSyntaxCache(services, CancellationToken.None, key => key == current);
        var held = scheduler.HoldNextResult();
        var older = cache.GetAsync(current, CancellationToken.None);
        await held.Started.Task;
        current = Key(buffer, 1);
        var newer = await cache.GetAsync(current, CancellationToken.None);
        held.Release.TrySetResult();
        await older;
        Assert.Same(newer, cache.Accepted);
        Assert.Same(newer, await cache.GetAsync(current, CancellationToken.None));
    });

    [Fact]
    public Task SharedTaskExistsBeforeAnInlineSchedulerCanReenter() => EditorTestHost.Run(async () =>
    {
        EditorSyntaxCache? cache = null;
        Task<AkburaSyntacticDocument>? shared = null;
        var key = Key(new object(), 0);
        using var services = new EditorServiceQueue(new ReentrantScheduler(() =>
            shared = cache!.GetAsync(key, CancellationToken.None)));
        using var owner = cache = new EditorSyntaxCache(services, CancellationToken.None, _ => true);
        var request = cache.GetAsync(key, CancellationToken.None);
        Assert.Same(request, shared);
        Assert.Same(await request, cache.Accepted);
    });

    [Fact]
    public Task WorkerCompletionPublishesOnUiEvenWithoutAnAmbientSynchronizationContext() => EditorTestHost.Run(async () =>
    {
        using var services = new EditorServiceQueue(new AkburaThreadPoolScheduler());
        using var cache = new EditorSyntaxCache(services, CancellationToken.None, _ =>
        {
            Assert.True(Dispatcher.UIThread.CheckAccess());
            return true;
        });
        var previous = SynchronizationContext.Current;
        Task<AkburaSyntacticDocument> request;
        try
        {
            SynchronizationContext.SetSynchronizationContext(null);
            request = cache.GetAsync(Key(new object(), 0), CancellationToken.None);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
        Assert.Same(await request, cache.Accepted);
    });

    private static ImmutableArray<EditorSyntaxCache.Key> FindCollision(object buffer)
    {
        var keys = ArrayBuilder<EditorSyntaxCache.Key>.GetInstance(EditorSyntaxCache.CacheSize + 1);
        try
        {
            for (var revision = 0; revision <= EditorSyntaxCache.CacheSize; revision++)
            {
                var key = Key(buffer, revision);
                var bucket = EditorSyntaxCache.GetCacheHash(key) & EditorSyntaxCache.CacheMask;
                foreach (var existing in keys)
                {
                    if ((EditorSyntaxCache.GetCacheHash(existing) & EditorSyntaxCache.CacheMask) == bucket)
                    {
                        using var collision = ImmutableArrayBuilder<EditorSyntaxCache.Key>.Rent(2);
                        collision.Add(existing);
                        collision.Add(key);
                        return collision.ToImmutable();
                    }
                }
                keys.Add(key);
            }
            throw new InvalidOperationException("Expected a collision in the bounded cache.");
        }
        finally
        {
            keys.Free();
        }
    }

    private static EditorSyntaxCache.Key FindOtherBucket(object buffer, EditorSyntaxCache.Key first)
    {
        var bucket = EditorSyntaxCache.GetCacheHash(first) & EditorSyntaxCache.CacheMask;
        for (var revision = 1; revision <= 1024; revision++)
        {
            var key = Key(buffer, revision);
            if ((EditorSyntaxCache.GetCacheHash(key) & EditorSyntaxCache.CacheMask) != bucket)
            {
                return key;
            }
        }
        throw new InvalidOperationException("No key for another cache bucket.");
    }

    private static EditorSyntaxCache.Key Key(object buffer, int revision) => new(buffer, revision,
        "memory:///Cache.akbura", SourceText.From($"// Привет 😀\r\nstate int Value{revision} = {revision};"));

    private sealed class ReentrantScheduler(Action before) : IAkburaEditorScheduler
    {
        public Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
        {
            before();
            return operation(cancellationToken);
        }
    }
}
