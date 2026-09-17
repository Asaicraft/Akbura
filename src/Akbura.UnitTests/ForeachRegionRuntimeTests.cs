using Akbura.HotReload;
using Avalonia.Controls;
using Avalonia.VisualTree;
using System.Collections;
using System.Collections.Immutable;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Akbura.UnitTests;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ForeachRegionRuntimeTests
{
    [Fact]
    public void PlainEnumerable_BreakStreamsOnlyReachedPrefixAndDisposesEnumerator()
    {
        var source = new CountingEnumerable<int>([1, 2, 3, 4]);
        using var region = new AkburaForeachRegion<int, int>(() => { });
        var evaluations = 0;
        var keys = 0;
        var children = region.Render(source, "v1", AkburaForeachDependencies.MayBreak, frame =>
        {
            evaluations++;
            frame.Emit(frame.Item);
            return frame.Item == 2 ? LoopFlow.Break : LoopFlow.Next;
        }, item => { keys++; return item; }, "item-key");

        Assert.Equal(new[] { 1, 2 }, children);
        Assert.Equal(2, source.MoveCount);
        Assert.Equal(1, source.DisposeCount);
        Assert.Equal(2, evaluations);
        Assert.Equal(2, keys);
        region.Commit();
    }

    [Fact]
    public void EmptySources_IncludeNullAndDefaultImmutableArrayWithoutEvaluatingBody()
    {
        using var region = new AkburaForeachRegion<int, int>(() => { });
        static LoopFlow Unexpected(AkburaForeachFrame<int, int> frame) =>
            throw new InvalidOperationException("An empty source evaluated its body.");
        Assert.Empty(region.Render(null, "v1", AkburaForeachDependencies.None, Unexpected));
        region.Commit();
        Assert.Empty(region.Render(default(ImmutableArray<int>), "v1", AkburaForeachDependencies.None, Unexpected));
        region.Commit();
        Assert.Empty(region.Render(ImmutableArray<int>.Empty, "v1", AkburaForeachDependencies.None, Unexpected));
        region.Commit();
    }

    [Fact]
    public void ContinueAndBreak_KeepEmissionsBeforeTheJumpAndUseSourceOrdinal()
    {
        using var region = new AkburaForeachRegion<int, string>(() => { });
        var children = region.Render([10, 20, 30, 40], "v1",
            AkburaForeachDependencies.UsesSourceIndex | AkburaForeachDependencies.MayContinue |
            AkburaForeachDependencies.MayBreak, frame =>
            {
                frame.Emit($"{frame.Index}:before");
                if (frame.Item == 20)
                {
                    return LoopFlow.Continue;
                }
                frame.Emit($"{frame.Index}:after");
                return frame.Item == 30 ? LoopFlow.Break : LoopFlow.Next;
            });
        Assert.Equal(["0:before", "0:after", "1:before", "2:before", "2:after"], children);
        region.Commit();
    }

    [Fact]
    public void MutableUnobservedSource_IsEnumeratedAgainEvenWhenItsReferenceIsUnchanged()
    {
        var source = new List<int> { 1 };
        using var region = new AkburaForeachRegion<int, int>(() => { });
        static LoopFlow Emit(AkburaForeachFrame<int, int> frame)
        {
            frame.Emit(frame.Item);
            return LoopFlow.Next;
        }
        region.Render(source, "v1", AkburaForeachDependencies.None, Emit);
        region.Commit();
        source.Add(2);
        Assert.Equal([1, 2], region.Render(source, "v1", AkburaForeachDependencies.None, Emit));
        region.Commit();
    }

    [Fact]
    public async Task ObservableDeltas_ReconcileOneMixedOwnerLedgerAndPreserveUnchangedRoots()
    {
        await OnDispatcher(() =>
        {
            using var fixture = new OwnerFixture();
            var source = new CountingObservable<int>([1, 2, 3]);
            var evaluations = 0;
            LoopFlow Emit(AkburaForeachFrame<int, Control> frame)
            {
                evaluations++;
                frame.Emit(frame.GetOrCreate(0, () => new TextBlock { Text = frame.Item.ToString() }));
                frame.Emit(frame.GetOrCreate(1, () => new Border()));
                return LoopFlow.Next;
            }
            fixture.Render(source, Emit);
            var original = fixture.Region.Children.ToArray();
            Assert.Equal(1, source.EnumerationCount);
            Assert.Equal(1, source.SubscriptionCount);
            Assert.Equal(3, evaluations);
            AssertOwnerLayout(fixture, original);

            source.AddRange(1, [8, 9]);
            fixture.Render(source, Emit);
            Assert.Equal(1, source.EnumerationCount);
            Assert.Equal(5, evaluations);
            Assert.Same(original[0], fixture.Region.Children[0]);
            Assert.Same(original[2], fixture.Region.Children[6]);
            AssertOwnerLayout(fixture, fixture.Region.Children);

            source.MoveRange(1, 2, 3);
            var beforeMove = fixture.Region.Children.ToArray();
            fixture.Render(source, Emit);
            Assert.Equal(5, evaluations);
            Assert.Same(beforeMove[2], fixture.Region.Children[6]);
            Assert.Same(beforeMove[4], fixture.Region.Children[8]);
            Assert.Equal(1, source.EnumerationCount);
            AssertOwnerLayout(fixture, fixture.Region.Children);

            source.RemoveRange(3, 2);
            fixture.Render(source, Emit);
            Assert.Equal(5, evaluations);
            Assert.Equal(original, fixture.Region.Children);
            AssertOwnerLayout(fixture, original);
            Assert.All(beforeMove.Skip(2).Take(4), child => Assert.Null(child.GetVisualParent()));
        });
    }

    [Fact]
    public async Task UnchangedObservedSource_DoesNotEnumerateEvaluateOrAllocate()
    {
        await OnDispatcher(() =>
        {
            var source = new CountingObservable<int>([1, 2]);
            using var region = new AkburaForeachRegion<int, int>(() => { });
            var calls = 0;
            LoopFlow Emit(AkburaForeachFrame<int, int> frame)
            {
                calls++;
                frame.Emit(frame.Item);
                return LoopFlow.Next;
            }
            Func<AkburaForeachFrame<int, int>, LoopFlow> evaluate = Emit;
            var children = region.Render(source, "v1", AkburaForeachDependencies.None, evaluate);
            region.Commit();
            for (var i = 0; i < 1000; i++)
            {
                region.Render(source, "v1", AkburaForeachDependencies.None, evaluate);
            }
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 10000; i++)
            {
                region.Render(source, "v1", AkburaForeachDependencies.None, evaluate);
            }
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.Equal(0, allocated);
            Assert.Equal(1, source.EnumerationCount);
            Assert.Equal(2, calls);
            Assert.Same(children, region.Children);
            Assert.False(region.ChildrenChanged);
            Assert.Equal(1, region.ChildrenVersion);
            Assert.False(region.HasPendingUpdate);
            Assert.Equal(1, source.SubscriptionCount);
        });
    }

    [Fact]
    public async Task ChildrenVersion_TracksMembershipAndOrderInsteadOfPropertyOnlyUpdates()
    {
        await OnDispatcher(() =>
        {
            var source = new CountingObservable<int>([1, 2]);
            using var region = new AkburaForeachRegion<int, TextBlock>(() => { });
            var label = "first";

            LoopFlow Emit(AkburaForeachFrame<int, TextBlock> frame)
            {
                var child = frame.GetOrCreate(0, () => new TextBlock());
                child.Text = $"{frame.Item}:{label}";
                frame.Emit(child);
                return LoopFlow.Next;
            }

            region.Render(source, "body", AkburaForeachDependencies.ReadsComponentEnvironment,
                Emit, environmentRevision: 0);
            Assert.True(region.ChildrenChanged);
            Assert.Equal(1, region.ChildrenVersion);
            region.Commit();

            var original = region.Children.ToArray();
            Assert.False(region.ChildrenChanged);
            Assert.Equal(1, region.ChildrenVersion);

            region.Render(source, "body", AkburaForeachDependencies.ReadsComponentEnvironment,
                Emit, environmentRevision: 0);
            Assert.False(region.HasPendingUpdate);
            Assert.False(region.ChildrenChanged);
            Assert.Equal(1, region.ChildrenVersion);

            label = "second";
            region.Render(source, "body", AkburaForeachDependencies.ReadsComponentEnvironment,
                Emit, environmentRevision: 1);
            Assert.True(region.HasPendingUpdate);
            Assert.False(region.ChildrenChanged);
            Assert.Equal(1, region.ChildrenVersion);
            Assert.Same(original[0], region.Children[0]);
            Assert.Same(original[1], region.Children[1]);
            Assert.Equal(new[] { "1:second", "2:second" }, region.Children.Select(static child => child.Text));
            region.Commit();

            source.MoveRange(0, 1, 1);
            region.Render(source, "body", AkburaForeachDependencies.ReadsComponentEnvironment,
                Emit, environmentRevision: 1);
            Assert.True(region.ChildrenChanged);
            Assert.Equal(2, region.ChildrenVersion);
            Assert.Same(original[1], region.Children[0]);
            Assert.Same(original[0], region.Children[1]);
            region.Commit();

            source.AddRange(2, [3]);
            region.Render(source, "body", AkburaForeachDependencies.ReadsComponentEnvironment,
                Emit, environmentRevision: 1);
            Assert.True(region.ChildrenChanged);
            Assert.Equal(3, region.ChildrenVersion);
            region.Abort();

            Assert.False(region.ChildrenChanged);
            Assert.Equal(2, region.ChildrenVersion);
            Assert.Equal(2, region.Children.Count);
        });
    }

    [Fact]
    public async Task DuplicateKeylessOccurrences_HaveDistinctTokensAndMoveTheirOwnControls()
    {
        await OnDispatcher(() =>
        {
            var sameItem = new object();
            var source = new CountingObservable<object>([sameItem, sameItem]);
            using var region = new AkburaForeachRegion<object, TextBlock>(() => { });
            var tokens = new List<long>();
            LoopFlow Emit(AkburaForeachFrame<object, TextBlock> frame)
            {
                tokens.Add(frame.OccurrenceToken);
                frame.Emit(frame.GetOrCreate(0, () => new TextBlock()));
                return LoopFlow.Next;
            }
            region.Render(source, "v1", AkburaForeachDependencies.None, Emit);
            region.Commit();
            var original = region.Children.ToArray();
            Assert.NotEqual(tokens[0], tokens[1]);
            Assert.NotSame(original[0], original[1]);
            source.MoveRange(0, 1, 1);
            region.Render(source, "v1", AkburaForeachDependencies.None, Emit);
            region.Commit();
            Assert.Same(original[1], region.Children[0]);
            Assert.Same(original[0], region.Children[1]);
            Assert.Equal(2, tokens.Count);
            Assert.Equal(1, source.EnumerationCount);
        });
    }

    [Fact]
    public async Task SourceIndexDependency_RefreshesMovedAndShiftedFramesWithoutRecreatingNodes()
    {
        await OnDispatcher(() =>
        {
            var source = new CountingObservable<int>([1, 2, 3]);
            using var region = new AkburaForeachRegion<int, TextBlock>(() => { });
            var calls = 0;
            LoopFlow Emit(AkburaForeachFrame<int, TextBlock> frame)
            {
                calls++;
                var text = frame.GetOrCreate(0, () => new TextBlock());
                text.Text = $"{frame.Item}/{frame.Index}";
                frame.Emit(text);
                return LoopFlow.Next;
            }
            region.Render(source, "v1", AkburaForeachDependencies.UsesSourceIndex, Emit);
            region.Commit();
            var original = region.Children.ToArray();
            source.MoveRange(0, 1, 2);
            region.Render(source, "v1", AkburaForeachDependencies.UsesSourceIndex, Emit);
            region.Commit();
            Assert.Equal(6, calls);
            Assert.Same(original[0], region.Children[2]);
            Assert.Equal(["2/0", "3/1", "1/2"], region.Children.Select(child => child.Text));
            Assert.Equal(1, source.EnumerationCount);
        });
    }

    [Fact]
    public async Task CachedBreak_DoesNotConstructTrailingItemsUntilTheStopperIsRemoved()
    {
        await OnDispatcher(() =>
        {
            var source = new CountingObservable<int>([1, 0, 2]);
            using var region = new AkburaForeachRegion<int, TextBlock>(() => { });
            var calls = new List<int>();
            LoopFlow Emit(AkburaForeachFrame<int, TextBlock> frame)
            {
                calls.Add(frame.Item);
                if (frame.Item == 0)
                {
                    return LoopFlow.Break;
                }
                frame.Emit(frame.GetOrCreate(0, () => new TextBlock()));
                return LoopFlow.Next;
            }
            region.Render(source, "v1", AkburaForeachDependencies.MayBreak, Emit);
            region.Commit();
            var first = Assert.Single(region.Children);
            source.AddRange(3, [3]);
            region.Render(source, "v1", AkburaForeachDependencies.MayBreak, Emit);
            region.Commit();
            Assert.Equal([1, 0], calls);
            Assert.Same(first, Assert.Single(region.Children));
            source.RemoveRange(1, 1);
            region.Render(source, "v1", AkburaForeachDependencies.MayBreak, Emit);
            region.Commit();
            Assert.Equal([1, 0, 2, 3 ], calls);
            Assert.Equal(3, region.Children.Count);
            Assert.Same(first, region.Children[0]);
            Assert.Equal(1, source.EnumerationCount);
        });
    }

    [Fact]
    public async Task ResetAndUnknownIndices_ResnapshotButPreserveKeyedFrames()
    {
        await OnDispatcher(() =>
        {
            var source = new CountingObservable<int>([1, 2]);
            using var region = new AkburaForeachRegion<int, TextBlock>(() => { });
            static LoopFlow Emit(AkburaForeachFrame<int, TextBlock> frame)
            {
                frame.Emit(frame.GetOrCreate(0, () => new TextBlock()));
                return LoopFlow.Next;
            }
            static int Key(int item) => item;
            region.Render(source, "v1", AkburaForeachDependencies.None, Emit, Key, "key");
            region.Commit();
            var original = region.Children.ToArray();
            source.Reset([2, 1]);
            region.Render(source, "v1", AkburaForeachDependencies.None, Emit, Key, "key");
            region.Commit();
            Assert.Equal(2, source.EnumerationCount);
            Assert.Same(original[1], region.Children[0]);
            Assert.Same(original[0], region.Children[1]);
            source.AddWithoutIndex(3);
            region.Render(source, "v1", AkburaForeachDependencies.None, Emit, Key, "key");
            region.Commit();
            Assert.Equal(3, source.EnumerationCount);
            Assert.Same(original[1], region.Children[0]);
            Assert.Equal(3, region.Children.Count);
        });
    }

    [Fact]
    public async Task TypedKeys_UseTheSuppliedComparerAndRejectNullOrDuplicateKeysBeforeCommit()
    {
        await OnDispatcher(() =>
        {
            var source = new CountingObservable<string>(["A", "B"]);
            using var region = new AkburaForeachRegion<string, TextBlock>(() => { });
            var calls = 0;
            LoopFlow Emit(AkburaForeachFrame<string, TextBlock> frame)
            {
                calls++;
                var child = frame.GetOrCreate(0, () => new TextBlock());
                child.Text = frame.Item;
                frame.Emit(child);
                return LoopFlow.Next;
            }
            static string Key(string item) => item;
            region.Render(source, "v1", AkburaForeachDependencies.None, Emit, Key, "key", StringComparer.OrdinalIgnoreCase);
            region.Commit();
            var original = region.Children.ToArray();
            source.ReplaceRange(0, ["a"]);
            region.Render(source, "v1", AkburaForeachDependencies.None, Emit, Key, "key", StringComparer.OrdinalIgnoreCase);
            region.Commit();
            Assert.Same(original[0], region.Children[0]);
            Assert.Equal("a", region.Children[0].Text);
            source.AddRange(2, ["A"]);
            Assert.Throws<InvalidOperationException>(() => region.Render(source, "v1", AkburaForeachDependencies.None,
                Emit, Key, "key", StringComparer.OrdinalIgnoreCase));
            Assert.Equal(original, region.Children);
            Assert.Equal(3, calls);
            Assert.Throws<InvalidOperationException>(() => region.Render(source, "v1", AkburaForeachDependencies.None,
                Emit, static _ => (string?)null, "null-key"));
        });
    }

    [Fact]
    public async Task Abort_KeepsPacketsAndAppliedFramesAndReleasesNewExplicitResources()
    {
        await OnDispatcher(() =>
        {
            var source = new CountingObservable<int>([1]);
            using var region = new AkburaForeachRegion<int, TextBlock>(() => { });
            var resources = new List<OwnedResource>();
            var fail = false;
            LoopFlow Emit(AkburaForeachFrame<int, TextBlock> frame)
            {
                var resource = frame.GetOrCreate(1, () => new OwnedResource());
                frame.Own(1, resource);
                if (!resources.Contains(resource))
                {
                    resources.Add(resource);
                }
                frame.Emit(frame.GetOrCreate(0, () => new TextBlock()));
                if (fail && frame.Item == 2)
                {
                    throw new InvalidOperationException("body failure");
                }
                return LoopFlow.Next;
            }
            region.Render(source, "v1", AkburaForeachDependencies.None, Emit);
            region.Commit();
            var first = Assert.Single(region.Children);
            source.AddRange(1, [2]);
            fail = true;
            Assert.Throws<InvalidOperationException>(() => region.Render(source, "v1", AkburaForeachDependencies.None, Emit));
            Assert.Same(first, Assert.Single(region.Children));
            Assert.False(resources[0].Disposed);
            Assert.True(resources[1].Disposed);
            Assert.False(region.HasPendingUpdate);
            fail = false;
            region.Render(source, "v1", AkburaForeachDependencies.None, Emit);
            region.Commit();
            Assert.Equal(2, region.Children.Count);
            Assert.Same(first, region.Children[0]);
            Assert.Equal(1, source.EnumerationCount);
            Assert.Equal(3, resources.Count);
        });
    }

    [Fact]
    public async Task SourceRevisionAbort_RestoresPerRootOwnedValuesAndRetriesWithTheSameControls()
    {
        await OnDispatcher(() =>
        {
            using var fixture = new OwnerFixture();
            var source = new CountingObservable<int>([1, 2]);
            var revision = "v1";
            LoopFlow Emit(AkburaForeachFrame<int, Control> frame)
            {
                var state = frame.GetRenderState(0);
                state.BeginRevision(revision,
                    builder => builder.Add(new AkburaRenderNodeDefinition(0, -1, "$root", typeof(TextBlock), null, "text-root")),
                    _ => new TextBlock());
                var text = state.GetRequired<TextBlock>(0);
                state.ReconcileConditionalClrValue(0, "Text", text, typeof(TextBlock), nameof(TextBlock.Text),
                    "text-value", $"{revision}/{frame.Item}");
                frame.Emit(text);
                return LoopFlow.Next;
            }
            fixture.Render(source, Emit);
            var original = fixture.Region.Children.Cast<TextBlock>().ToArray();
            revision = "v2";
            fixture.BeginSource(revision);
            fixture.Prepare(source, Emit, revision);
            Assert.Equal(["v2/1", "v2/2"], original.Select(child => child.Text));
            fixture.State.AbortRevision(new InvalidOperationException("later setter failed"));
            Assert.Equal(["v1/1", "v1/2"], original.Select(child => child.Text));
            AssertOwnerLayout(fixture, original);
            fixture.BeginSource(revision);
            fixture.Prepare(source, Emit, revision);
            fixture.State.CompleteRevision();
            Assert.Equal(original, fixture.Region.Children);
            Assert.Equal(["v2/1", "v2/2"], original.Select(child => child.Text));
        });
    }

    [Fact]
    public async Task SourceSwapAndSuspendResume_ReplaceLeasesButRetainCompatibleKeyedRoots()
    {
        await OnDispatcher(() =>
        {
            var firstSource = new CountingObservable<int>([1, 2]);
            var secondSource = new CountingObservable<int>([2, 1]);
            using var region = new AkburaForeachRegion<int, TextBlock>(() => { });
            static LoopFlow Emit(AkburaForeachFrame<int, TextBlock> frame)
            {
                frame.Emit(frame.GetOrCreate(0, () => new TextBlock()));
                return LoopFlow.Next;
            }
            static int Key(int item) => item;
            region.Render(firstSource, "v1", AkburaForeachDependencies.None, Emit, Key, "key");
            region.Commit();
            var original = region.Children.ToArray();
            var firstEpoch = region.SourceEpoch;
            region.Render(secondSource, "v1", AkburaForeachDependencies.None, Emit, Key, "key");
            region.Commit();
            Assert.Equal(0, firstSource.SubscriptionCount);
            Assert.Equal(1, secondSource.SubscriptionCount);
            Assert.True(region.SourceEpoch > firstEpoch);
            Assert.Same(original[1], region.Children[0]);
            region.Suspend();
            Assert.Equal(0, secondSource.SubscriptionCount);
            secondSource.Reset([1, 2, 3]);
            region.Resume();
            region.Render(secondSource, "v1", AkburaForeachDependencies.None, Emit, Key, "key");
            region.Commit();
            Assert.Equal(1, secondSource.SubscriptionCount);
            Assert.Equal(3, region.Children.Count);
            Assert.Same(original[0], region.Children[0]);
            Assert.Same(original[1], region.Children[1]);
            Assert.Equal(2, secondSource.EnumerationCount);
        });
    }

    [Fact]
    public async Task NotifyingItemDependencies_RefreshAllOccurrencesAndReleaseSubscriptionsOnRemoval()
    {
        await OnDispatcher(() =>
        {
            var item = new NotifyingItem("old");
            var source = new CountingObservable<NotifyingItem>([item, item]);
            using var region = new AkburaForeachRegion<NotifyingItem, TextBlock>(() => { });
            var calls = 0;
            LoopFlow Emit(AkburaForeachFrame<NotifyingItem, TextBlock> frame)
            {
                calls++;
                var text = frame.GetOrCreate(0, () => new TextBlock());
                text.Text = frame.Item.Text;
                frame.Emit(text);
                return LoopFlow.Next;
            }
            region.Render(source, "v1", AkburaForeachDependencies.ReadsNotifyingItemProperties, Emit);
            region.Commit();
            Assert.Equal(1, item.SubscriptionCount);
            item.Text = "new";
            region.Render(source, "v1", AkburaForeachDependencies.ReadsNotifyingItemProperties, Emit);
            region.Commit();
            Assert.Equal(4, calls);
            Assert.All(region.Children, child => Assert.Equal("new", child.Text));
            Assert.Equal(1, source.EnumerationCount);
            source.RemoveRange(0, 2);
            region.Render(source, "v1", AkburaForeachDependencies.ReadsNotifyingItemProperties, Emit);
            region.Commit();
            Assert.Equal(0, item.SubscriptionCount);
        });
    }

    [Fact]
    public async Task NestedRegions_OwnIndependentOrdinalsAndRetireWhenTheirDeclarationIsSkipped()
    {
        await OnDispatcher(() =>
        {
            var source = new CountingObservable<int>([1]);
            var innerSource = new CountingObservable<int>([5, 6]);
            using var region = new AkburaForeachRegion<int, string>(() => { });
            var includeInner = true;
            LoopFlow Emit(AkburaForeachFrame<int, string> outer)
            {
                outer.Emit($"outer:{outer.Index}");
                if (includeInner)
                {
                    var nested = outer.GetForeachRegion<int, string>(1, () => { });
                    foreach (var value in nested.Render(innerSource, "v1", AkburaForeachDependencies.UsesSourceIndex, inner =>
                    {
                        inner.Emit($"{outer.Index}/{inner.Index}:{inner.Item}");
                        return LoopFlow.Next;
                    }))
                    {
                        outer.Emit(value);
                    }
                }
                return LoopFlow.Next;
            }
            region.Render(source, "v1", AkburaForeachDependencies.ReadsComponentEnvironment, Emit);
            region.Commit();
            Assert.Equal(["outer:0", "0/0:5", "0/1:6"], region.Children);
            Assert.Equal(1, innerSource.SubscriptionCount);
            includeInner = false;
            region.Render(source, "v1", AkburaForeachDependencies.ReadsComponentEnvironment, Emit);
            region.Commit();
            Assert.Equal(["outer:0"], region.Children);
            Assert.Equal(0, innerSource.SubscriptionCount);
        });
    }

    [Fact]
    public async Task OmittedLoopAndOwnerDisposal_ReleaseOwnedResourcesWithoutDisposingSourceItems()
    {
        await OnDispatcher(() =>
        {
            using var fixture = new OwnerFixture();
            var source = new CountingObservable<int>([1]);
            var resource = new OwnedResource();
            fixture.Render(source, frame =>
            {
                frame.Own(0, resource);
                frame.Emit(frame.GetOrCreate(0, () => new TextBlock()));
                return LoopFlow.Next;
            });
            var oldRoot = Assert.Single(fixture.Region.Children);
            fixture.State.BeginForeachFrame();
            fixture.State.ReconcileCollection(0, "Children", fixture.Owner.Children, [fixture.Prefix, fixture.Suffix]);
            fixture.State.CompleteForeachFrame();
            Assert.True(resource.Disposed);
            Assert.Equal(0, source.SubscriptionCount);
            Assert.Null(oldRoot.GetVisualParent());
            AssertOwnerLayout(fixture, []);
            var disposableItem = new OwnedResource();
            using var other = new AkburaForeachRegion<OwnedResource, int>(() => { });
            other.Render([disposableItem], "v1", AkburaForeachDependencies.None, frame =>
            {
                frame.Emit(frame.Index);
                return LoopFlow.Next;
            });
            other.Commit();
            other.Dispose();
            Assert.False(disposableItem.Disposed);
        });
    }

    [Fact]
    public async Task ConversionAdapters_PreserveTheRuntimeNotifierAndApplyRealNumericConversions()
    {
        await OnDispatcher(() =>
        {
            var source = new CountingObservable<int>([1]);
            using var region = new AkburaForeachRegion<double, double>(() => { });
            static LoopFlow Emit(AkburaForeachFrame<double, double> frame)
            {
                frame.Emit(frame.Item);
                return LoopFlow.Next;
            }
            region.Render(source, static value => (double)value, "v1", AkburaForeachDependencies.None, Emit);
            region.Commit();
            source.AddRange(1, [2]);
            region.Render(source, static value => (double)value, "v1", AkburaForeachDependencies.None, Emit);
            region.Commit();
            Assert.Equal([1d, 2d], region.Children);
            Assert.Equal(1, source.EnumerationCount);
            Assert.Equal(1, source.SubscriptionCount);
            using var rawRegion = new AkburaForeachRegion<int, int>(() => { });
            var raw = new ArrayList { 3, 4 };
            Assert.Equal([3, 4], rawRegion.RenderNonGeneric(raw, static value => (int)value!, "v1",
                AkburaForeachDependencies.None, frame => { frame.Emit(frame.Item); return LoopFlow.Next; }));
            rawRegion.Commit();
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(100000)]
    public void DynamicContentCursor_UsesActualWidthAcrossIndependentRegions(int width)
    {
        var cursor = new AkburaRenderContentCursor();
        cursor.AdvanceItem();
        cursor.AdvanceDynamicRegion(width);
        cursor.AdvanceRegion(8, 2);
        cursor.AdvanceDynamicRegion(3);
        cursor.AdvanceItem();
        Assert.Equal(width + 7, cursor.PhysicalCount);
        Assert.Throws<ArgumentOutOfRangeException>(() => cursor.AdvanceDynamicRegion(-1));
    }

    [Fact]
    public async Task SharedIterationNameScope_ResolvesForwardRootNamesAndAbortKeepsTheAppliedLookup()
    {
        await OnDispatcher(() =>
        {
            var source = new CountingObservable<int>([1]);
            using var region = new AkburaForeachRegion<int, Control>(() => { });
            INameScope? current = null;
            var finishNames = false;
            LoopFlow Emit(AkburaForeachFrame<int, Control> frame)
            {
                var scope = frame.GetNameScope(null);
                current = scope;
                var first = frame.GetOrCreate(0, () => new TextBlock());
                var later = frame.GetOrCreate(1, () => new TextBlock());
                Assert.Same(scope, frame.GetNameScope(null));
                Assert.False(scope.IsCompleted);
                scope.Register("first", first);
                var forward = scope.FindAsync("later");
                Assert.False(forward.IsCompleted);
                scope.Register("later", later);
                Assert.Same(later, forward.GetAwaiter().GetResult());
                frame.Emit(first);
                frame.Emit(later);
                finishNames = true;
                return LoopFlow.Next;
            }
            region.Render(source, "v1", AkburaForeachDependencies.ReadsComponentEnvironment, Emit);
            Assert.True(finishNames);
            Assert.True(current!.IsCompleted);
            region.Commit();
            var applied = current;
            var roots = region.Children.ToArray();
            region.Render(source, "v1", AkburaForeachDependencies.ReadsComponentEnvironment, Emit);
            Assert.NotSame(applied, current);
            region.Abort();
            Assert.Same(roots[0], applied.Find("first"));
            Assert.Same(roots[1], applied.Find("later"));
            Assert.Equal(roots, region.Children);
        });
    }

    [Fact]
    public async Task DynamicRootIdentity_RemountsOnlyTheChangedDeclarationAndAbortRestoresItsOwnedState()
    {
        await OnDispatcher(() =>
        {
            var source = new CountingObservable<int>([1]);
            using var region = new AkburaForeachRegion<int, Control>(() => { });
            var rootKey = 1;
            var states = new List<AkburaRenderState>();
            LoopFlow Emit(AkburaForeachFrame<int, Control> frame)
            {
                var state = frame.GetRenderState(0, rootKey);
                states.Add(state);
                state.BeginRevision("v1",
                    builder => builder.Add(new AkburaRenderNodeDefinition(0, -1, "$root", typeof(TextBlock), null, "text-root")),
                    _ => new TextBlock());
                var child = state.GetRequired<TextBlock>(0);
                state.ReconcileConditionalClrValue(0, "Text", child, typeof(TextBlock), nameof(TextBlock.Text), "text", "owned");
                frame.Emit(child);
                return LoopFlow.Next;
            }
            region.Render(source, "v1", AkburaForeachDependencies.ReadsComponentEnvironment, Emit);
            region.Commit();
            var original = Assert.IsType<TextBlock>(Assert.Single(region.Children));
            region.Render(source, "v1", AkburaForeachDependencies.ReadsComponentEnvironment, Emit);
            region.Commit();
            Assert.Same(states[0], states[1]);
            Assert.Same(original, Assert.Single(region.Children));
            rootKey = 2;
            region.Render(source, "v1", AkburaForeachDependencies.ReadsComponentEnvironment, Emit);
            var abandoned = Assert.IsType<TextBlock>(Assert.Single(region.Children));
            Assert.NotSame(original, abandoned);
            Assert.Equal("owned", original.Text);
            region.Abort();
            Assert.Same(original, Assert.Single(region.Children));
            Assert.Equal("owned", original.Text);
            Assert.Null(abandoned.Text);
            region.Render(source, "v1", AkburaForeachDependencies.ReadsComponentEnvironment, Emit);
            var replacement = Assert.IsType<TextBlock>(Assert.Single(region.Children));
            region.Commit();
            Assert.NotSame(original, replacement);
            Assert.Null(original.Text);
            Assert.Equal("owned", replacement.Text);
        });
    }

    [Fact]
    public void IndexedDestinationValidation_RejectsReadOnlyAndFixedSizeBeforeSourceEvaluation()
    {
        IList<int> readOnly = Array.AsReadOnly([1]);
        IList<int> fixedSize = new[] { 1 };
        Assert.Throws<InvalidOperationException>(() => AkburaForeachDestination.Validate(readOnly));
        Assert.Throws<InvalidOperationException>(() => AkburaForeachDestination.Validate(fixedSize));
        Assert.Throws<InvalidOperationException>(() => AkburaForeachDestination.Validate<int>(new Hashtable()));
        Assert.Throws<InvalidOperationException>(() => AkburaForeachDestination.Validate<int>(ArrayList.FixedSize([])));
        AkburaForeachDestination.Validate(new List<int>());
        AkburaForeachDestination.Validate<int>(new ArrayList());
    }

    private static Task OnDispatcher(Action action) =>
        AvaloniaHeadlessTestSession.GetSession().Dispatch(action, CancellationToken.None);

    private static void AssertOwnerLayout(OwnerFixture fixture, IEnumerable<Control> children)
    {
        Assert.Equal(new[] { fixture.Foreign, fixture.Prefix }.Concat(children).Append(fixture.Suffix), fixture.Owner.Children);
        Assert.All(fixture.Owner.Children, child => Assert.Same(fixture.Owner, child.GetVisualParent()));
    }

    private sealed class OwnerFixture : IDisposable
    {
        public AkburaRenderState State { get; } = new();
        public StackPanel Owner { get; } = new();
        public TextBlock Foreign { get; } = new();
        public TextBlock Prefix { get; } = new();
        public TextBlock Suffix { get; } = new();
        public AkburaForeachRegion<int, Control> Region { get; private set; } = null!;

        public OwnerFixture()
        {
            Owner.Children.Add(Foreign);
            BeginSource("v1");
        }

        public void BeginSource(string revision) => State.BeginRevision(revision,
            builder => builder.Add(new AkburaRenderNodeDefinition(0, -1, "$root", typeof(StackPanel), null, "panel-root")),
            _ => Owner);

        public void Prepare(IEnumerable<int> source, Func<AkburaForeachFrame<int, Control>, LoopFlow> evaluate,
            string revision = "v1")
        {
            State.BeginForeachFrame();
            Region = State.GetForeachRegion<int, Control>(0, "loop:items", () => { });
            var children = Region.Render(source, revision, AkburaForeachDependencies.None, evaluate);
            State.ReconcileCollection(0, "Children", Owner.Children, new[] { Prefix }.Concat(children).Append(Suffix).ToArray());
        }

        public void Render(IEnumerable<int> source, Func<AkburaForeachFrame<int, Control>, LoopFlow> evaluate)
        {
            Prepare(source, evaluate);
            if (State.HasPendingRevision)
            {
                State.CompleteRevision();
            }
            else
            {
                State.CompleteForeachFrame();
            }
        }

        public void Dispose() => State.Dispose();
    }

    private sealed class OwnedResource : IDisposable
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }

    private sealed class NotifyingItem(string text) : INotifyPropertyChanged
    {
        private PropertyChangedEventHandler? _changed;
        private string _text = text;
        public int SubscriptionCount { get; private set; }
        public event PropertyChangedEventHandler? PropertyChanged
        {
            add { _changed += value; SubscriptionCount++; }
            remove { _changed -= value; SubscriptionCount--; }
        }
        public string Text
        {
            get => _text;
            set { _text = value; _changed?.Invoke(this, new PropertyChangedEventArgs(nameof(Text))); }
        }
    }

    private sealed class CountingObservable<T>(IEnumerable<T> values) : IEnumerable<T>, INotifyCollectionChanged
    {
        private readonly List<T> _items = [.. values];
        private NotifyCollectionChangedEventHandler? _changed;
        public int EnumerationCount { get; private set; }
        public int SubscriptionCount { get; private set; }
        public event NotifyCollectionChangedEventHandler? CollectionChanged
        {
            add { _changed += value; SubscriptionCount++; }
            remove { _changed -= value; SubscriptionCount--; }
        }
        public IEnumerator<T> GetEnumerator()
        {
            EnumerationCount++;
            return _items.GetEnumerator();
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        public void AddRange(int index, T[] values)
        {
            _items.InsertRange(index, values);
            _changed?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, values, index));
        }
        public void RemoveRange(int index, int count)
        {
            var old = _items.GetRange(index, count).ToArray();
            _items.RemoveRange(index, count);
            _changed?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, old, index));
        }
        public void MoveRange(int index, int count, int destination)
        {
            var old = _items.GetRange(index, count).ToArray();
            _items.RemoveRange(index, count);
            _items.InsertRange(destination, old);
            _changed?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Move, old, destination, index));
        }
        public void ReplaceRange(int index, T[] values)
        {
            var old = _items.GetRange(index, values.Length).ToArray();
            for (var i = 0; i < values.Length; i++)
            {
                _items[index + i] = values[i];
            }
            _changed?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Replace, values, old, index));
        }
        public void Reset(T[] values)
        {
            _items.Clear();
            _items.AddRange(values);
            _changed?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
        public void AddWithoutIndex(T value)
        {
            _items.Add(value);
            _changed?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, value));
        }
    }

    private sealed class CountingEnumerable<T>(T[] values) : IEnumerable<T>
    {
        public int MoveCount { get; private set; }
        public int DisposeCount { get; private set; }
        public IEnumerator<T> GetEnumerator() => new Enumerator(this, values);
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        private sealed class Enumerator(CountingEnumerable<T> owner, T[] values) : IEnumerator<T>
        {
            private int _index = -1;
            public T Current => values[_index];
            object? IEnumerator.Current => Current;
            public bool MoveNext() { owner.MoveCount++; return ++_index < values.Length; }
            public void Reset() => throw new NotSupportedException();
            public void Dispose() => owner.DisposeCount++;
        }
    }
}
