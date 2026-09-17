using System;
using System.Collections.Immutable;
using System.Linq;
using Akbura.HotReload;
using Xunit;

namespace Akbura.UnitTests;

public sealed class ForeachImmutableReplayRegressionTests
{
    [Theory]
    [InlineData("environment")]
    [InlineData("template")]
    [InlineData("explicit-invalidation")]
    public void ImmutableSource_ReplaysUnreadTailWhenBreakNoLongerApplies(string reason)
    {
        var source = ImmutableArray.Create(1, 2, 3, 4, 5);
        using var region = new AkburaForeachRegion<int, ItemNode>(() => { });

        var stopAt = 3;
        var templateRevision = "body-v1";
        var environmentRevision = 0;
        var dependencies = AkburaForeachDependencies.MayBreak;

        if (reason == "environment")
        {
            dependencies |= AkburaForeachDependencies.ReadsComponentEnvironment;
        }

        LoopFlow Evaluate(AkburaForeachFrame<int, ItemNode> frame)
        {
            if (frame.Item == stopAt)
            {
                return LoopFlow.Break;
            }

            var child = frame.GetOrCreate(0, () => new ItemNode(frame.Item));
            frame.Emit(child);
            return LoopFlow.Next;
        }

        var original = region.Render(
            source,
            templateRevision,
            dependencies,
            Evaluate,
            environmentRevision: environmentRevision).ToArray();

        Assert.Equal([1, 2], [.. original.Select(child => child.Value)]);

        region.Commit();

        // Keep the same ImmutableArray backing storage. The previous streaming
        // pass stopped at item 3, so records for items 4 and 5 do not exist yet.
        stopAt = -1;

        switch (reason)
        {
            case "environment":
                environmentRevision++;
                break;

            case "template":
                templateRevision = "body-v2";

                // The new generated body may no longer contain break at all.
                // Replay therefore must not depend on the current MayBreak flag.
                dependencies = AkburaForeachDependencies.None;
                break;

            case "explicit-invalidation":
                region.Invalidate();
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(reason));
        }

        var updated = region.Render(
            source,
            templateRevision,
            dependencies,
            Evaluate,
            environmentRevision: environmentRevision).ToArray();

        // Replaying only the committed prefix would stop at [1, 2, 3].
        // A correct implementation enumerates the immutable source again.
        Assert.Equal([1, 2, 3, 4, 5], [.. updated.Select(child => child.Value)]);

        // Matching records before the old break must retain their existing nodes.
        Assert.Same(original[0], updated[0]);
        Assert.Same(original[1], updated[1]);

        region.Commit();
    }

    private sealed class ItemNode(int value)
    {
        public int Value { get; } = value;
    }
}
