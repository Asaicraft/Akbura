using Akbura.HotReload;
using System.Runtime.CompilerServices;

namespace Akbura.UnitTests;

public sealed class ConditionalRenderCaptureStateTests
{
    [Fact]
    public void SourceAbort_RestoresExistingCaptureAndRemovesNewCapture()
    {
        using var state = new AkburaRenderState();
        Begin(state, "initial");
        state.SetRenderCapture("render/local/text", "original");
        state.CompleteRevision();

        Begin(state, "changed-source");
        state.SetRenderCapture("render/local/text", "first edit");
        state.SetRenderCapture("render/local/text", "final edit");
        state.SetRenderCapture("render/local/added", 42);
        Assert.Equal("final edit", state.GetRenderCapture<string>("render/local/text"));
        Assert.Equal(42, state.GetRenderCapture<int>("render/local/added"));
        state.PrepareRevisionCompletion();
        state.AbortRevision(new InvalidOperationException("later render failure"));

        Assert.False(state.HasPendingRevision);
        Assert.Equal("original", state.GetRenderCapture<string>("render/local/text"));
        Assert.Throws<InvalidOperationException>(() => state.GetRenderCapture<int>("render/local/added"));
    }

    [Fact]
    public void SourceAbort_RestoresOriginalTypeAfterCaptureIsRetyped()
    {
        using var state = new AkburaRenderState();
        Begin(state, "initial");
        state.SetRenderCapture<string?>("render/local/value", null);
        state.CompleteRevision();

        Begin(state, "changed-source");
        state.SetRenderCapture("render/local/value", 7);
        state.SetRenderCapture("render/local/value", 9);
        Assert.Equal(9, state.GetRenderCapture<int>("render/local/value"));
        Assert.Throws<InvalidOperationException>(() => state.GetRenderCapture<string?>("render/local/value"));
        state.AbortRevision();

        Assert.False(state.HasPendingRevision);
        Assert.Null(state.GetRenderCapture<string?>("render/local/value"));
        Assert.Throws<InvalidOperationException>(() => state.GetRenderCapture<int>("render/local/value"));
    }

    [Fact]
    public void SourceCommit_PreservesFinalValuesForChangedAndNewCaptures()
    {
        using var state = new AkburaRenderState();
        Begin(state, "initial");
        state.SetRenderCapture("render/local/text", "original");
        state.SetRenderCapture("render/local/retyped", 1);
        state.CompleteRevision();

        Begin(state, "changed-source");
        state.SetRenderCapture("render/local/text", "updated");
        state.SetRenderCapture<string?>("render/local/retyped", "new type");
        state.SetRenderCapture("render/local/added", 42);
        state.PrepareRevisionCompletion();
        state.CompleteRevision();

        Assert.False(state.HasPendingRevision);
        Assert.Equal("updated", state.GetRenderCapture<string>("render/local/text"));
        Assert.Equal("new type", state.GetRenderCapture<string?>("render/local/retyped"));
        Assert.Equal(42, state.GetRenderCapture<int>("render/local/added"));
        Assert.Throws<InvalidOperationException>(() => state.GetRenderCapture<int>("render/local/retyped"));
    }

    [Fact]
    public void OrdinaryRender_PublishesCurrentValueWithoutTransactionOrAllocations()
    {
        using var state = new AkburaRenderState();
        Begin(state, "initial");
        state.SetRenderCapture("render/local/count", 0);
        state.CompleteRevision();
        for (var i = 0; i < 1000; i++)
        {
            state.SetRenderCapture("render/local/count", i);
            _ = state.GetRenderCapture<int>("render/local/count");
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        var current = 0;
        for (var i = 0; i < 10000; i++)
        {
            state.SetRenderCapture("render/local/count", i);
            current = state.GetRenderCapture<int>("render/local/count");
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(9999, current);
        Assert.Equal(0, allocated);
        Assert.False(state.HasPendingRevision);
    }

    [Theory]
    [InlineData("render/local/")]
    [InlineData("conditional/")]
    public void SourceCommit_ReleasesOnlyOmittedGeneratedCapturesWhileParentAndChildRemainAlive(string prefix)
    {
        using var parent = new AkburaRenderState();
        using var child = new AkburaRenderState();
        child.SetRenderCaptureParent(parent);
        var publishedKey = prefix + "published";
        var omittedKey = prefix + "omitted";
        Begin(parent, "initial");
        var published = PublishPayload(parent, publishedKey);
        var omitted = PublishPayload(parent, omittedKey);
        parent.SetRenderCapture("manual/value", 1);
        parent.SetRenderCapture("render/locality/value", 2);
        parent.SetRenderCapture("conditionality/value", 3);
        parent.CompleteRevision();
        AssertCapturePresent(child, omittedKey);

        Begin(parent, "changed-source");
        PublishExistingPayload(parent, publishedKey);
        parent.CompleteRevision();
        CollectCaptures();

        Assert.True(IsAlive(published));
        Assert.False(IsAlive(omitted));
        AssertCapturePresent(child, publishedKey);
        Assert.Throws<InvalidOperationException>(() => child.GetRenderCapture<object>(omittedKey));
        Assert.Equal(1, child.GetRenderCapture<int>("manual/value"));
        Assert.Equal(2, child.GetRenderCapture<int>("render/locality/value"));
        Assert.Equal(3, child.GetRenderCapture<int>("conditionality/value"));
        GC.KeepAlive(parent);
        GC.KeepAlive(child);
    }

    [Theory]
    [InlineData("render/local/")]
    [InlineData("conditional/")]
    public void SourceAbort_PreservesOmittedCapturesAndRestoresRetypedParentValue(string prefix)
    {
        using var parent = new AkburaRenderState();
        using var child = new AkburaRenderState();
        child.SetRenderCaptureParent(parent);
        var changedKey = prefix + "changed";
        var omittedKey = prefix + "omitted";
        Begin(parent, "initial");
        var changed = PublishPayload(parent, changedKey);
        var omitted = PublishPayload(parent, omittedKey);
        parent.CompleteRevision();

        Begin(parent, "failed-source");
        parent.SetRenderCapture(changedKey, 42);
        Assert.Equal(42, child.GetRenderCapture<int>(changedKey));
        parent.PrepareRevisionCompletion();
        parent.AbortRevision(new InvalidOperationException("later source initialization failed"));
        CollectCaptures();

        Assert.False(parent.HasPendingRevision);
        Assert.True(IsAlive(changed));
        Assert.True(IsAlive(omitted));
        AssertCapturePresent(child, changedKey);
        AssertCapturePresent(child, omittedKey);
        Assert.Throws<InvalidOperationException>(() => child.GetRenderCapture<int>(changedKey));
        GC.KeepAlive(parent);
        GC.KeepAlive(child);
    }

    [Theory]
    [InlineData("render/local/")]
    [InlineData("conditional/")]
    public void ActivationCommitAndAbort_DoNotPruneUnpublishedCaptures(string prefix)
    {
        using var parent = new AkburaRenderState();
        using var child = new AkburaRenderState();
        child.SetRenderCaptureParent(parent);
        var changedKey = prefix + "changed";
        var omittedKey = prefix + "omitted";
        Assert.True(parent.BeginRevision("initial", static builder =>
        {
            builder.Add(new(0, -1, "$root", typeof(object), null, "root"));
            builder.AddConditional(new(0, 0, "Children", "same-if", [new("active", "a"), new("", "b")]));
        }, static _ => new object()));
        var changed = PublishPayload(parent, changedKey);
        var omitted = PublishPayload(parent, omittedKey);
        parent.SelectConditionalBranch(0, 0);
        parent.CompleteRevision();

        Assert.True(parent.SelectConditionalBranch(0, 1));
        Assert.False(parent.IsApplyingSourceRevision);
        parent.SetRenderCapture(changedKey, 42);
        parent.SetRenderCapture(prefix + "abandoned", 9);
        parent.PrepareRevisionCompletion();
        parent.AbortRevision();
        Assert.Equal(0, parent.GetConditionalBranch(0));
        AssertCapturePresent(child, changedKey);
        Assert.Throws<InvalidOperationException>(() => child.GetRenderCapture<int>(prefix + "abandoned"));

        Assert.True(parent.SelectConditionalBranch(0, 1));
        parent.CompleteRevision();
        CollectCaptures();

        Assert.False(parent.HasPendingRevision);
        Assert.Equal(1, parent.GetConditionalBranch(0));
        Assert.True(IsAlive(changed));
        Assert.True(IsAlive(omitted));
        AssertCapturePresent(child, changedKey);
        AssertCapturePresent(child, omittedKey);
        GC.KeepAlive(parent);
        GC.KeepAlive(child);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<object> PublishPayload(AkburaRenderState state, string key)
    {
        var payload = new object();
        state.SetRenderCapture(key, payload);
        return new WeakReference<object>(payload);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void PublishExistingPayload(AkburaRenderState state, string key) =>
        state.SetRenderCapture(key, state.GetRenderCapture<object>(key));

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AssertCapturePresent(AkburaRenderState state, string key) =>
        Assert.NotNull(state.GetRenderCapture<object>(key));

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool IsAlive(WeakReference<object> capture) => capture.TryGetTarget(out _);

    private static void CollectCaptures()
    {
        for (var i = 0; i < 3; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }

    private static void Begin(AkburaRenderState state, string revision)
    {
        Assert.True(state.BeginRevision(revision,
            static builder => builder.Add(new AkburaRenderNodeDefinition(0, -1, "$root", typeof(object), null, "root")),
            static _ => new object()));
    }
}
