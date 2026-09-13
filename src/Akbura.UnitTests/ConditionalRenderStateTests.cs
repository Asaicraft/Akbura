using Akbura.HotReload;
using System.Collections;
using System.ComponentModel;

namespace Akbura.UnitTests;

public sealed class ConditionalRenderStateTests
{
    private const string ChildrenSlot = "Container.Children";

    [Fact]
    public void ConditionalActivation_IsLazyAndRecreatesPreviouslyRemovedBranch()
    {
        var fixture = new Fixture();
        fixture.Begin("initial", ["a", ""]);
        var root = fixture.State.GetRequired<Container>(0);

        Assert.Empty(fixture.Created);
        Assert.Equal(-1, fixture.State.GetConditionalBranch(0));
        Assert.Throws<InvalidOperationException>(() => fixture.State.GetRequired<Leaf>(1));
        Assert.True(fixture.State.SelectConditionalBranch(0, 0));
        var firstA = fixture.State.GetRequired<Leaf>(1);
        Assert.Equal(1, firstA.BeginInitCount);
        Assert.Equal(0, firstA.EndInitCount);
        fixture.Reconcile(1);
        fixture.State.CompleteRevision();

        Assert.Equal(1, firstA.EndInitCount);
        Assert.Equal(new object[] { root.Foreign, firstA }, root.Children.Cast<object>());
        Assert.Single(fixture.Created);

        Assert.True(fixture.State.SelectConditionalBranch(0, 1));
        var branchB = fixture.State.GetRequired<Leaf>(2);
        Assert.Throws<InvalidOperationException>(() => fixture.State.GetRequired<Leaf>(1));
        fixture.Reconcile(2);
        fixture.State.CompleteRevision();
        Assert.Equal(new object[] { root.Foreign, branchB }, root.Children.Cast<object>());

        Assert.True(fixture.State.SelectConditionalBranch(0, 0));
        var secondA = fixture.State.GetRequired<Leaf>(1);
        Assert.NotSame(firstA, secondA);
        fixture.Reconcile(1);
        fixture.State.CompleteRevision();

        Assert.Equal(3, fixture.Created.Count);
        Assert.Equal(new object[] { root.Foreign, secondA }, root.Children.Cast<object>());
    }

    [Fact]
    public void UnchangedActiveBranch_DoesNotCreateTransactionOrAllocate()
    {
        var fixture = new Fixture();
        fixture.Begin("initial", ["a", ""]);
        fixture.State.SelectConditionalBranch(0, 0);
        fixture.Reconcile(1);
        fixture.State.CompleteRevision();
        var leaf = fixture.State.GetRequired<Leaf>(1);
        for (var i = 0; i < 1000; i++)
        {
            fixture.State.SelectConditionalBranch(0, 0);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        var changed = false;
        for (var i = 0; i < 10000; i++)
        {
            changed |= fixture.State.SelectConditionalBranch(0, 0);
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.False(changed);
        Assert.Equal(0, allocated);
        Assert.False(fixture.State.HasPendingRevision);
        Assert.Same(leaf, fixture.State.GetRequired<Leaf>(1));
        Assert.Single(fixture.Created);
    }

    [Fact]
    public void NoSelectedBranch_RemovesOnlyOwnedChildrenAndEventHandlers()
    {
        var fixture = new Fixture();
        fixture.Begin("initial", ["a"]);
        fixture.State.SelectConditionalBranch(0, 0);
        var leaf = fixture.State.GetRequired<Leaf>(1);
        var ownedCalls = 0;
        var foreignCalls = 0;
        leaf.Raised += (_, _) => foreignCalls++;
        EventHandler owned = (_, _) => ownedCalls++;
        Assert.True(fixture.State.ShouldApplyOwnedOperation(1, "Raised", "owned-handler"));
        fixture.State.ApplyClrEventOperation(1, "Raised", leaf, typeof(Leaf), nameof(Leaf.Raised), owned);
        fixture.Reconcile(1);
        fixture.State.CompleteRevision();
        leaf.Raise();
        Assert.Equal(1, ownedCalls);
        Assert.Equal(1, foreignCalls);

        fixture.State.SelectConditionalBranch(0, -1);
        fixture.Reconcile();
        fixture.State.CompleteRevision();
        leaf.Raise();

        Assert.Equal(1, ownedCalls);
        Assert.Equal(2, foreignCalls);
        Assert.Equal(-1, fixture.State.GetConditionalBranch(0));
        Assert.Equal(new object[] { fixture.Root.Foreign }, fixture.Root.Children.Cast<object>());
    }

    [Fact]
    public void ConstructorFailure_RollsBackToAppliedBranchAndCanRetry()
    {
        var fixture = new Fixture();
        fixture.Begin("initial", ["a", ""]);
        fixture.State.SelectConditionalBranch(0, 0);
        var first = fixture.State.GetRequired<Leaf>(1);
        fixture.Reconcile(1);
        fixture.State.CompleteRevision();
        fixture.ThrowOnLocalId = 2;

        var failure = Assert.Throws<InvalidOperationException>(() => fixture.State.SelectConditionalBranch(0, 1));
        Assert.Equal("constructor failure", failure.Message);
        Assert.False(fixture.State.HasPendingRevision);
        Assert.Equal(0, fixture.State.GetConditionalBranch(0));
        Assert.Same(first, fixture.State.GetRequired<Leaf>(1));
        Assert.Equal(new object[] { fixture.Root.Foreign, first }, fixture.Root.Children.Cast<object>());

        fixture.ThrowOnLocalId = -1;
        fixture.State.SelectConditionalBranch(0, 1);
        fixture.Reconcile(2);
        fixture.State.CompleteRevision();
        Assert.Equal(1, fixture.State.GetConditionalBranch(0));
    }

    [Fact]
    public void ActivationAbort_RestoresOwnedCollectionAndHandlers()
    {
        var fixture = new Fixture();
        fixture.Begin("initial", ["a", ""]);
        fixture.State.SelectConditionalBranch(0, 0);
        var first = fixture.State.GetRequired<Leaf>(1);
        var calls = 0;
        Assert.True(fixture.State.ShouldApplyOwnedOperation(1, "Raised", "owned-handler"));
        fixture.State.ApplyClrEventOperation(1, "Raised", first, typeof(Leaf), nameof(Leaf.Raised),
            new EventHandler((_, _) => calls++));
        fixture.Reconcile(1);
        fixture.State.CompleteRevision();

        fixture.State.SelectConditionalBranch(0, 1);
        var discarded = fixture.State.GetRequired<Leaf>(2);
        fixture.Reconcile(2);
        fixture.State.PrepareRevisionCompletion();
        fixture.State.AbortRevision(new InvalidOperationException("later update failure"));

        Assert.Equal(0, fixture.State.GetConditionalBranch(0));
        Assert.Same(first, fixture.State.GetRequired<Leaf>(1));
        Assert.Equal(new object[] { fixture.Root.Foreign, first }, fixture.Root.Children.Cast<object>());
        first.Raise();
        Assert.Equal(1, calls);
        Assert.Equal(1, discarded.EndInitCount);
    }

    [Fact]
    public void SourceRevision_InsertedElseIfDoesNotStealExistingActiveBranchIdentity()
    {
        var fixture = new Fixture();
        fixture.Begin("initial", ["a", "b", ""]);
        fixture.State.SelectConditionalBranch(0, 1);
        var existing = fixture.State.GetRequired<Leaf>(2);
        var existingNodeId = fixture.State.GetNodeId(2);
        fixture.Reconcile(2);
        fixture.State.CompleteRevision();

        fixture.Begin("inserted-elseif", ["inserted", "a", "b", ""]);

        Assert.Equal(2, fixture.State.GetConditionalBranch(0));
        Assert.Same(existing, fixture.State.GetRequired<Leaf>(3));
        Assert.Equal(existingNodeId, fixture.State.GetNodeId(3));
        Assert.False(fixture.State.SelectConditionalBranch(0, 2));
        Assert.Single(fixture.Created);
        fixture.Reconcile(3);
        fixture.State.CompleteRevision();
        Assert.Equal(new object[] { fixture.Root.Foreign, existing }, fixture.Root.Children.Cast<object>());
    }

    [Fact]
    public void SourceRevision_ConditionChangeCanRetainBranchByUniqueShape()
    {
        var fixture = new Fixture();
        fixture.Begin("initial", ["a", ""]);
        fixture.State.SelectConditionalBranch(0, 0);
        var existing = fixture.State.GetRequired<Leaf>(1);
        fixture.Reconcile(1);
        fixture.State.CompleteRevision();

        fixture.Begin("changed-condition", ["changed", ""], ["a", ""]);

        Assert.Equal(0, fixture.State.GetConditionalBranch(0));
        Assert.Same(existing, fixture.State.GetRequired<Leaf>(1));
        Assert.False(fixture.State.SelectConditionalBranch(0, 0));
        fixture.Reconcile(1);
        fixture.State.CompleteRevision();
        Assert.Single(fixture.Created);
    }

    [Fact]
    public void InactiveAncestor_DoesNotCreateNestedNodesAndReactivationCreatesFreshDescendants()
    {
        var state = new AkburaRenderState();
        var root = new Container();
        var created = new List<Leaf>();
        state.BeginRevision("nested", builder =>
        {
            builder.Add(new AkburaRenderNodeDefinition(0, -1, "$root", typeof(Container), null, "Container"));
            builder.Add(new AkburaRenderNodeDefinition(1, 0, ChildrenSlot, typeof(Leaf), null, "outer-a", 0, 0));
            builder.Add(new AkburaRenderNodeDefinition(2, 0, ChildrenSlot, typeof(Leaf), null, "inner", 1, 0));
            builder.Add(new AkburaRenderNodeDefinition(3, 0, ChildrenSlot, typeof(Leaf), null, "outer-b", 0, 1));
            builder.AddConditional(new AkburaRenderConditionalDefinition(0, 0, ChildrenSlot, "outer",
                [new("a", "outer-a"), new("", "outer-b")]));
            builder.AddConditional(new AkburaRenderConditionalDefinition(1, 0, ChildrenSlot, "inner",
                [new("inner", "inner")], 0, 0));
        }, localId =>
        {
            if (localId == 0)
            {
                return root;
            }
            var leaf = new Leaf(localId.ToString());
            created.Add(leaf);
            return leaf;
        });
        Assert.Empty(created);
        state.SelectConditionalBranch(0, 0);
        Assert.Single(created);
        state.SelectConditionalBranch(1, 0);
        var firstInner = state.GetRequired<Leaf>(2);
        state.ReconcileCollection(0, ChildrenSlot, root.Children,
            [state.GetRequired<Leaf>(1), firstInner]);
        state.CompleteRevision();

        state.SelectConditionalBranch(0, 1);
        state.ReconcileCollection(0, ChildrenSlot, root.Children, [state.GetRequired<Leaf>(3)]);
        state.CompleteRevision();
        Assert.Throws<InvalidOperationException>(() => state.SelectConditionalBranch(1, 0));
        Assert.Throws<InvalidOperationException>(() => state.GetRequired<Leaf>(2));
        Assert.Equal(3, created.Count);

        state.SelectConditionalBranch(0, 0);
        state.SelectConditionalBranch(1, 0);
        var nextInner = state.GetRequired<Leaf>(2);
        Assert.NotSame(firstInner, nextInner);
        state.ReconcileCollection(0, ChildrenSlot, root.Children,
            [state.GetRequired<Leaf>(1), nextInner]);
        state.CompleteRevision();
        Assert.Equal(5, created.Count);
        Assert.Same(root.Foreign, root.Children[0]);
    }

    [Fact]
    public void SourceRevision_ActiveChildLiteralEditRetainsCompatibleInstance()
    {
        var fixture = new Fixture();
        fixture.Begin("initial", ["a", ""]);
        fixture.State.SelectConditionalBranch(0, 0);
        var existing = fixture.State.GetRequired<Leaf>(1);
        var nodeId = fixture.State.GetNodeId(1);
        fixture.Reconcile(1);
        fixture.State.CompleteRevision();

        fixture.Begin("edited-child", ["a", ""], nodeIdentities: ["leaf:a:changed-value", "leaf:"]);

        Assert.Same(existing, fixture.State.GetRequired<Leaf>(1));
        Assert.Equal(nodeId, fixture.State.GetNodeId(1));
        Assert.False(fixture.State.IsNew(1));
        Assert.True(fixture.State.ShouldApplyInitialValues(1));
        fixture.Reconcile(1);
        fixture.State.CompleteRevision();
        Assert.Single(fixture.Created);
    }

    [Fact]
    public void SourceRevision_InactiveBranchTypeEditDoesNotConstructInactiveType()
    {
        var fixture = new Fixture();
        fixture.Begin("initial", ["a", ""]);
        fixture.State.SelectConditionalBranch(0, 0);
        var existing = fixture.State.GetRequired<Leaf>(1);
        fixture.Reconcile(1);
        fixture.State.CompleteRevision();

        fixture.Begin("edited-inactive-type", ["a", ""], types: [typeof(Leaf), typeof(OtherLeaf)]);

        Assert.Same(existing, fixture.State.GetRequired<Leaf>(1));
        Assert.Equal(0, fixture.OtherCreatedCount);
        Assert.Throws<InvalidOperationException>(() => fixture.State.GetRequired<OtherLeaf>(2));
        fixture.Reconcile(1);
        fixture.State.CompleteRevision();
        Assert.Equal(0, fixture.OtherCreatedCount);

        fixture.State.SelectConditionalBranch(0, 1);
        fixture.State.ReconcileCollection(0, ChildrenSlot, fixture.Root.Children,
            [fixture.State.GetRequired<OtherLeaf>(2)]);
        fixture.State.CompleteRevision();
        Assert.Equal(1, fixture.OtherCreatedCount);
    }

    [Fact]
    public void SourceRevision_ChainRemovalReleasesOwnedResourcesAndPreservesForeignItems()
    {
        var fixture = new Fixture();
        fixture.Begin("initial", ["a", ""]);
        fixture.State.SelectConditionalBranch(0, 0);
        var leaf = fixture.State.GetRequired<Leaf>(1);
        var ownedCalls = 0;
        var foreignCalls = 0;
        leaf.Raised += (_, _) => foreignCalls++;
        fixture.State.ShouldApplyOwnedOperation(1, "Raised", "owned-handler");
        fixture.State.ApplyClrEventOperation(1, "Raised", leaf, typeof(Leaf), nameof(Leaf.Raised),
            new EventHandler((_, _) => ownedCalls++));
        fixture.Reconcile(1);
        fixture.State.CompleteRevision();
        var appendedForeign = new object();
        fixture.Root.Children.Add(appendedForeign);

        fixture.Begin("removed-chain", []);
        fixture.State.CompleteRevision();
        leaf.Raise();

        Assert.Equal(0, ownedCalls);
        Assert.Equal(1, foreignCalls);
        Assert.Equal(new object[] { fixture.Root.Foreign, appendedForeign }, fixture.Root.Children.Cast<object>());
        Assert.Throws<ArgumentOutOfRangeException>(() => fixture.State.GetConditionalBranch(0));
        fixture.State.Dispose();
        fixture.State.Dispose();
        Assert.Equal(new object[] { fixture.Root.Foreign, appendedForeign }, fixture.Root.Children.Cast<object>());
    }

    [Fact]
    public void ScalarConditional_EmptyActiveBranchWritesNullButChainRemovalRestoresBaseline()
    {
        const string slot = "ScalarContainer.Child";
        var state = new AkburaRenderState();
        var root = new ScalarContainer();
        state.BeginRevision("conditional", builder =>
        {
            builder.Add(new AkburaRenderNodeDefinition(0, -1, "$root", typeof(ScalarContainer), null, "Scalar"));
            builder.Add(new AkburaRenderNodeDefinition(1, 0, slot, typeof(Leaf), null, "leaf", 0, 0));
            builder.AddConditional(new AkburaRenderConditionalDefinition(0, 0, slot, "conditional",
                [new("a", "leaf"), new("", "empty")]));
        }, localId => localId == 0 ? root : new Leaf("active"));
        state.SelectConditionalBranch(0, 0);
        var active = state.GetRequired<Leaf>(1);
        state.ReconcileClrProperty(0, slot, root, typeof(ScalarContainer), nameof(ScalarContainer.Child), active);
        state.CompleteRevision();
        Assert.Same(active, root.Child);

        state.SelectConditionalBranch(0, 1);
        state.ReconcileClrProperty(0, slot, root, typeof(ScalarContainer), nameof(ScalarContainer.Child), null);
        state.CompleteRevision();
        Assert.Null(root.Child);

        state.BeginRevision("removed-chain", builder => builder.Add(new AkburaRenderNodeDefinition(
            0, -1, "$root", typeof(ScalarContainer), null, "Scalar")), _ => root);
        state.CompleteRevision();
        Assert.Same(root.Foreign, root.Child);
    }

    [Fact]
    public void ContentCursor_IndependentDestinationsTrackReservedAndPhysicalWidth()
    {
        var left = new AkburaRenderContentCursor();
        var right = new AkburaRenderContentCursor();
        left.AdvanceItem();
        left.AdvanceRegion(3, 2);
        left.AdvanceItem();
        left.AdvanceRegion(2, 0);
        left.AdvanceItem();
        right.AdvanceRegion(5, 1);

        Assert.Equal(8, left.VirtualSlot);
        Assert.Equal(3, left.Delta);
        Assert.Equal(5, left.PhysicalCount);
        Assert.Equal(5, right.VirtualSlot);
        Assert.Equal(4, right.Delta);
        Assert.Equal(1, right.PhysicalCount);

        var unchangedWidth = new AkburaRenderContentCursor();
        unchangedWidth.AdvanceItem();
        unchangedWidth.AdvanceRegion(3, 2);
        Assert.Equal(1, unchangedWidth.Delta);
        Assert.Equal(3, unchangedWidth.PhysicalCount);
        Assert.Throws<ArgumentOutOfRangeException>(() => right.AdvanceRegion(1, 2));
    }

    [Fact]
    public void DynamicEventRefresh_ReplacesOwnedClosureAndActivationAbortRestoresLastAppliedHandler()
    {
        var fixture = new Fixture();
        fixture.Begin("initial", ["a", ""]);
        fixture.State.SelectConditionalBranch(0, 0);
        var leaf = fixture.State.GetRequired<Leaf>(1);
        var observed = new List<string>();
        var foreignCalls = 0;
        leaf.Raised += (_, _) => foreignCalls++;
        fixture.State.RefreshClrEventOperation(1, "Raised", "capture", leaf, typeof(Leaf), nameof(Leaf.Raised),
            new EventHandler((_, _) => observed.Add("initial")));
        fixture.Reconcile(1);
        fixture.State.CompleteRevision();

        fixture.State.RefreshClrEventOperation(1, "Raised", "capture", leaf, typeof(Leaf), nameof(Leaf.Raised),
            new EventHandler((_, _) => observed.Add("current")));
        leaf.Raise();
        Assert.Equal(new[] { "current" }, observed);

        fixture.State.SelectConditionalBranch(0, 1);
        var replacement = fixture.State.GetRequired<Leaf>(2);
        fixture.State.RefreshClrEventOperation(2, "Raised", "capture", replacement, typeof(Leaf), nameof(Leaf.Raised),
            new EventHandler((_, _) => observed.Add("discarded")));
        fixture.Reconcile(2);
        fixture.State.PrepareRevisionCompletion();
        fixture.State.AbortRevision();
        leaf.Raise();
        replacement.Raise();

        Assert.Equal(new[] { "current", "current" }, observed);
        Assert.Equal(2, foreignCalls);
        Assert.Same(leaf, fixture.State.GetRequired<Leaf>(1));
    }

    [Theory]
    [InlineData("default-branch")]
    [InlineData("wrong-owner-branch")]
    [InlineData("unrelated-parent-owner")]
    [InlineData("unconditional-child")]
    public void InvalidConditionalMetadata_IsRejectedBeforeAnyConstructor(string scenario)
    {
        var state = new AkburaRenderState();
        var constructorCalls = 0;
        Assert.Throws<ArgumentException>(() => state.BeginRevision("invalid", builder =>
        {
            builder.Add(new AkburaRenderNodeDefinition(0, -1, "$root", typeof(Container), null, "root"));
            if (scenario == "default-branch")
            {
                builder.Add(new AkburaRenderNodeDefinition(1, 0, ChildrenSlot, typeof(Leaf), null, "leaf", 0, 0));
                builder.AddConditional(new AkburaRenderConditionalDefinition(0, 0, ChildrenSlot, "region", [default]));
            }
            else if (scenario == "wrong-owner-branch")
            {
                builder.Add(new AkburaRenderNodeDefinition(1, 0, ChildrenSlot, typeof(Container), null, "owner", 0, 0));
                builder.AddConditional(new AkburaRenderConditionalDefinition(0, 0, ChildrenSlot, "outer",
                    [new("a", "a"), new("", "b")]));
                builder.AddConditional(new AkburaRenderConditionalDefinition(1, 1, ChildrenSlot, "inner",
                    [new("inner", "inner")], 0, 1));
            }
            else if (scenario == "unrelated-parent-owner")
            {
                builder.Add(new AkburaRenderNodeDefinition(1, 0, ChildrenSlot, typeof(Container), null, "left"));
                builder.Add(new AkburaRenderNodeDefinition(2, 0, ChildrenSlot, typeof(Container), null, "right"));
                builder.AddConditional(new AkburaRenderConditionalDefinition(0, 1, ChildrenSlot, "outer", [new("a", "a")]));
                builder.AddConditional(new AkburaRenderConditionalDefinition(1, 2, ChildrenSlot, "inner", [new("b", "b")], 0, 0));
            }
            else
            {
                builder.Add(new AkburaRenderNodeDefinition(1, 0, ChildrenSlot, typeof(Container), null, "owner", 0, 0));
                builder.Add(new AkburaRenderNodeDefinition(2, 1, ChildrenSlot, typeof(Leaf), null, "escaped"));
                builder.AddConditional(new AkburaRenderConditionalDefinition(0, 0, ChildrenSlot, "region", [new("a", "a")]));
            }
        }, _ =>
        {
            constructorCalls++;
            return new Container();
        }));

        Assert.Equal(0, constructorCalls);
        Assert.False(state.HasPendingRevision);
    }

    [Fact]
    public void IndependentConditionalCollections_RepairForeignInterleavingWithoutTouchingOtherSink()
    {
        var state = new AkburaRenderState();
        var root = new Container();
        state.BeginRevision("two-sinks", builder =>
        {
            builder.Add(new AkburaRenderNodeDefinition(0, -1, "$root", typeof(Container), null, "root"));
            builder.Add(new AkburaRenderNodeDefinition(1, 0, ChildrenSlot, typeof(Container), null, "left"));
            builder.Add(new AkburaRenderNodeDefinition(2, 1, ChildrenSlot, typeof(Leaf), null, "left-static"));
            builder.Add(new AkburaRenderNodeDefinition(3, 1, ChildrenSlot, typeof(Leaf), null, "left-a", 0, 0));
            builder.Add(new AkburaRenderNodeDefinition(4, 1, ChildrenSlot, typeof(Leaf), null, "left-b", 0, 1));
            builder.Add(new AkburaRenderNodeDefinition(5, 0, ChildrenSlot, typeof(Container), null, "right"));
            builder.Add(new AkburaRenderNodeDefinition(6, 5, ChildrenSlot, typeof(Leaf), null, "right-static"));
            builder.Add(new AkburaRenderNodeDefinition(7, 5, ChildrenSlot, typeof(Leaf), null, "right-a", 1, 0));
            builder.Add(new AkburaRenderNodeDefinition(8, 5, ChildrenSlot, typeof(Leaf), null, "right-b", 1, 1));
            builder.AddConditional(new AkburaRenderConditionalDefinition(0, 1, ChildrenSlot, "left",
                [new("a", "left-a"), new("", "left-b")]));
            builder.AddConditional(new AkburaRenderConditionalDefinition(1, 5, ChildrenSlot, "right",
                [new("a", "right-a"), new("", "right-b")]));
        }, localId => localId switch
        {
            0 => root,
            1 or 5 => new Container(),
            _ => new Leaf(localId.ToString()),
        });
        state.SelectConditionalBranch(0, 0);
        state.SelectConditionalBranch(1, 0);
        var left = state.GetRequired<Container>(1);
        var right = state.GetRequired<Container>(5);
        var leftStatic = state.GetRequired<Leaf>(2);
        var leftA = state.GetRequired<Leaf>(3);
        var rightStatic = state.GetRequired<Leaf>(6);
        var rightA = state.GetRequired<Leaf>(7);
        var ownedRightCalls = 0;
        state.ShouldApplyOwnedOperation(7, "Raised", "right-handler");
        state.ApplyClrEventOperation(7, "Raised", rightA, typeof(Leaf), nameof(Leaf.Raised),
            new EventHandler((_, _) => ownedRightCalls++));
        state.ReconcileCollection(0, ChildrenSlot, root.Children, [left, right]);
        state.ReconcileCollection(1, ChildrenSlot, left.Children, [leftStatic, leftA]);
        state.ReconcileCollection(5, ChildrenSlot, right.Children, [rightStatic, rightA]);
        state.CompleteRevision();
        var between = new object();
        var suffix = new object();
        left.Children.Insert(2, between);
        left.Children.Add(suffix);
        Assert.False(state.IsCollectionLayoutCurrent(1, ChildrenSlot, left.Children));
        Assert.True(state.IsCollectionLayoutCurrent(5, ChildrenSlot, right.Children));

        state.ReconcileCollection(1, ChildrenSlot, left.Children, [leftStatic, leftA]);

        Assert.False(state.HasPendingRevision);
        Assert.True(state.IsCollectionLayoutCurrent(1, ChildrenSlot, left.Children));
        Assert.Equal(new object[] { left.Foreign, leftStatic, leftA, between, suffix }, left.Children.Cast<object>());
        Assert.Equal(new object[] { right.Foreign, rightStatic, rightA }, right.Children.Cast<object>());

        state.SelectConditionalBranch(0, 1);
        var leftB = state.GetRequired<Leaf>(4);
        state.ReconcileCollection(1, ChildrenSlot, left.Children, [leftStatic, leftB]);
        state.CompleteRevision();
        rightA.Raise();

        Assert.Equal(1, ownedRightCalls);
        Assert.Same(rightA, state.GetRequired<Leaf>(7));
        Assert.Equal(new object[] { left.Foreign, leftStatic, leftB, between, suffix }, left.Children.Cast<object>());
        Assert.Equal(new object[] { right.Foreign, rightStatic, rightA }, right.Children.Cast<object>());
        state.Dispose();
        rightA.Raise();
        Assert.Equal(1, ownedRightCalls);
        Assert.Equal(new object[] { left.Foreign, between, suffix }, left.Children.Cast<object>());
        Assert.Equal(new object[] { right.Foreign }, right.Children.Cast<object>());
    }

    [Fact]
    public void ActivationCopiedOperations_SameBranchEventRefreshAbortRestoresExactlyOneHandler()
    {
        var state = new AkburaRenderState();
        var root = new Container();
        state.BeginRevision("two-regions", builder =>
        {
            builder.Add(new AkburaRenderNodeDefinition(0, -1, "$root", typeof(Container), null, "root"));
            builder.Add(new AkburaRenderNodeDefinition(1, 0, ChildrenSlot, typeof(Leaf), null, "retained", 0, 0));
            builder.Add(new AkburaRenderNodeDefinition(2, 0, ChildrenSlot, typeof(Leaf), null, "other", 1, 0));
            builder.AddConditional(new AkburaRenderConditionalDefinition(0, 0, ChildrenSlot, "retained", [new("a", "a")]));
            builder.AddConditional(new AkburaRenderConditionalDefinition(1, 0, ChildrenSlot, "other", [new("b", "b")]));
        }, localId => localId == 0 ? root : new Leaf(localId.ToString()));
        state.SelectConditionalBranch(0, 0);
        state.SelectConditionalBranch(1, 0);
        var retained = state.GetRequired<Leaf>(1);
        var other = state.GetRequired<Leaf>(2);
        var observed = new List<string>();
        var foreignCalls = 0;
        retained.Raised += (_, _) => foreignCalls++;
        state.RefreshClrEventOperation(1, "Raised", "capture", retained, typeof(Leaf), nameof(Leaf.Raised),
            new EventHandler((_, _) => observed.Add("applied")));
        state.ReconcileCollection(0, ChildrenSlot, root.Children, [retained, other]);
        state.CompleteRevision();

        state.SelectConditionalBranch(1, -1);
        Assert.False(state.SelectConditionalBranch(0, 0));
        state.RefreshClrEventOperation(1, "Raised", "capture", retained, typeof(Leaf), nameof(Leaf.Raised),
            new EventHandler((_, _) => observed.Add("discarded")));
        state.ReconcileCollection(0, ChildrenSlot, root.Children, [retained]);
        state.PrepareRevisionCompletion();
        state.AbortRevision();
        retained.Raise();

        Assert.Equal(new[] { "applied" }, observed);
        Assert.Equal(1, foreignCalls);
        Assert.Equal(new object[] { root.Foreign, retained, other }, root.Children.Cast<object>());
        Assert.Same(retained, state.GetRequired<Leaf>(1));
    }

    [Fact]
    public void InactiveGrandparent_RejectsDeepSelectionDespiteCachedImmediateParentBranch()
    {
        var state = new AkburaRenderState();
        var root = new Container();
        var constructorCalls = 0;
        state.BeginRevision("deep", builder =>
        {
            builder.Add(new AkburaRenderNodeDefinition(0, -1, "$root", typeof(Container), null, "root"));
            builder.Add(new AkburaRenderNodeDefinition(1, 0, ChildrenSlot, typeof(Leaf), null, "outer", 0, 0));
            builder.Add(new AkburaRenderNodeDefinition(2, 0, ChildrenSlot, typeof(Leaf), null, "middle", 1, 0));
            builder.Add(new AkburaRenderNodeDefinition(3, 0, ChildrenSlot, typeof(Leaf), null, "inner", 2, 0));
            builder.Add(new AkburaRenderNodeDefinition(4, 0, ChildrenSlot, typeof(Leaf), null, "alternative", 0, 1));
            builder.AddConditional(new AkburaRenderConditionalDefinition(0, 0, ChildrenSlot, "outer",
                [new("a", "a"), new("", "else")]));
            builder.AddConditional(new AkburaRenderConditionalDefinition(1, 0, ChildrenSlot, "middle",
                [new("b", "b")], 0, 0));
            builder.AddConditional(new AkburaRenderConditionalDefinition(2, 0, ChildrenSlot, "inner",
                [new("c", "c")], 1, 0));
        }, localId =>
        {
            if (localId == 0)
            {
                return root;
            }
            constructorCalls++;
            return new Leaf(localId.ToString());
        });
        state.SelectConditionalBranch(0, 0);
        state.SelectConditionalBranch(1, 0);
        state.SelectConditionalBranch(2, 0);
        state.ReconcileCollection(0, ChildrenSlot, root.Children,
            [state.GetRequired<Leaf>(1), state.GetRequired<Leaf>(2), state.GetRequired<Leaf>(3)]);
        state.CompleteRevision();
        state.SelectConditionalBranch(0, 1);
        state.ReconcileCollection(0, ChildrenSlot, root.Children, [state.GetRequired<Leaf>(4)]);
        state.CompleteRevision();

        Assert.Throws<InvalidOperationException>(() => state.SelectConditionalBranch(2, 0));
        Assert.Equal(4, constructorCalls);
        Assert.False(state.HasPendingRevision);
        Assert.Throws<InvalidOperationException>(() => state.GetRequired<Leaf>(3));
    }

    [Fact]
    public void ManyUnchangedRegions_DoNotAllocateOrMaterializeInactiveBranches()
    {
        const int regionCount = 200;
        var state = new AkburaRenderState();
        var root = new Container();
        var constructorCalls = 0;
        state.BeginRevision("many", builder =>
        {
            builder.Add(new AkburaRenderNodeDefinition(0, -1, "$root", typeof(Container), null, "root"));
            for (var i = 0; i < regionCount; i++)
            {
                builder.Add(new AkburaRenderNodeDefinition(i * 2 + 1, 0, ChildrenSlot, typeof(Leaf), null, "active:" + i, i, 0));
                builder.Add(new AkburaRenderNodeDefinition(i * 2 + 2, 0, ChildrenSlot, typeof(Leaf), null, "inactive:" + i, i, 1));
                builder.AddConditional(new AkburaRenderConditionalDefinition(i, 0, ChildrenSlot, "region:" + i,
                    [new("active:" + i, "active:" + i), new("", "inactive:" + i)]));
            }
        }, localId =>
        {
            if (localId == 0)
            {
                return root;
            }
            constructorCalls++;
            return new Leaf(localId.ToString());
        });
        var active = new object[regionCount];
        for (var i = 0; i < regionCount; i++)
        {
            state.SelectConditionalBranch(i, 0);
            active[i] = state.GetRequired<Leaf>(i * 2 + 1);
        }
        state.ReconcileCollection(0, ChildrenSlot, root.Children, active);
        state.CompleteRevision();
        for (var i = 0; i < regionCount; i++)
        {
            state.SelectConditionalBranch(i, 0);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        var changed = false;
        for (var iteration = 0; iteration < 100; iteration++)
        {
            for (var i = 0; i < regionCount; i++)
            {
                changed |= state.SelectConditionalBranch(i, 0);
            }
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.False(changed);
        Assert.Equal(0, allocated);
        Assert.Equal(regionCount, constructorCalls);
        Assert.False(state.HasPendingRevision);
        Assert.Same(active[^1], state.GetRequired<Leaf>(regionCount * 2 - 1));
    }

    [Fact]
    public void SourceRevision_AddedActiveNodeRebuildsBranchIndicesAndCreatesOnlyNewNode()
    {
        var state = new AkburaRenderState();
        var root = new Container();
        var constructorCalls = 0;
        object Create(int localId)
        {
            if (localId == 0)
            {
                return root;
            }
            constructorCalls++;
            return new Leaf(localId.ToString());
        }
        void Describe(AkburaRenderPlanBuilder builder, bool added)
        {
            builder.Add(new AkburaRenderNodeDefinition(0, -1, "$root", typeof(Container), null, "root"));
            builder.Add(new AkburaRenderNodeDefinition(1, 0, ChildrenSlot, typeof(Leaf), null, "existing", 0, 0));
            if (added)
            {
                builder.Add(new AkburaRenderNodeDefinition(2, 0, ChildrenSlot, typeof(Leaf), null, "added", 0, 0));
            }
            builder.Add(new AkburaRenderNodeDefinition(added ? 3 : 2, 0, ChildrenSlot, typeof(Leaf), null, "else", 0, 1));
            builder.AddConditional(new AkburaRenderConditionalDefinition(0, 0, ChildrenSlot, "conditional",
                [new("a", added ? "existing+added" : "existing"), new("", "else")]));
        }
        state.BeginRevision("initial", builder => Describe(builder, false), Create);
        state.SelectConditionalBranch(0, 0);
        var existing = state.GetRequired<Leaf>(1);
        state.ReconcileCollection(0, ChildrenSlot, root.Children, [existing]);
        state.CompleteRevision();
        state.BeginRevision("added-active-node", builder => Describe(builder, true), Create);

        Assert.Equal(1, constructorCalls);
        Assert.Same(existing, state.GetRequired<Leaf>(1));
        Assert.True(state.SelectConditionalBranch(0, 0));
        var added = state.GetRequired<Leaf>(2);
        state.ReconcileCollection(0, ChildrenSlot, root.Children, [existing, added]);
        state.CompleteRevision();

        Assert.Equal(2, constructorCalls);
        Assert.Equal(new object[] { root.Foreign, existing, added }, root.Children.Cast<object>());
        Assert.Throws<InvalidOperationException>(() => state.GetRequired<Leaf>(3));
    }

    private sealed class Fixture
    {
        public AkburaRenderState State { get; } = new();
        public Container Root { get; } = new();
        public List<Leaf> Created { get; } = [];
        public int ThrowOnLocalId { get; set; } = -1;
        public int OtherCreatedCount { get; private set; }
        private string[] _shapes = [];
        private Type[] _types = [];

        public void Begin(string revision, string[] conditions, string[]? shapes = null,
            string[]? nodeIdentities = null, Type[]? types = null)
        {
            _shapes = shapes ?? conditions;
            _types = types ?? Enumerable.Repeat(typeof(Leaf), conditions.Length).ToArray();
            State.BeginRevision(revision, builder =>
            {
                builder.Add(new AkburaRenderNodeDefinition(0, -1, "$root", typeof(Container), null, "Container"));
                for (var i = 0; i < conditions.Length; i++)
                {
                    builder.Add(new AkburaRenderNodeDefinition(i + 1, 0, ChildrenSlot, _types[i], null,
                        nodeIdentities?[i] ?? "leaf:" + _shapes[i], 0, i));
                }
                if (conditions.Length != 0)
                {
                    builder.AddConditional(new AkburaRenderConditionalDefinition(0, 0, ChildrenSlot, "conditional",
                        conditions.Select((condition, index) => new AkburaRenderConditionalBranchDefinition(
                            condition, "shape:" + _shapes[index])).ToArray()));
                }
            }, Create);
        }

        public void Reconcile(params int[] localIds) =>
            State.ReconcileCollection(0, ChildrenSlot, Root.Children,
                localIds.Select(State.GetRequired<Leaf>).Cast<object>().ToArray());

        private object Create(int localId)
        {
            if (localId == ThrowOnLocalId)
            {
                throw new InvalidOperationException("constructor failure");
            }
            if (localId == 0)
            {
                return Root;
            }
            if (_types[localId - 1] == typeof(OtherLeaf))
            {
                OtherCreatedCount++;
                return new OtherLeaf();
            }
            var leaf = new Leaf(_shapes[localId - 1]);
            Created.Add(leaf);
            return leaf;
        }
    }

    private sealed class Container
    {
        public object Foreign { get; } = new();
        public ArrayList Children { get; }

        public Container() => Children = [Foreign];
    }

    private sealed class Leaf(string name) : ISupportInitialize
    {
        public string Name { get; } = name;
        public int BeginInitCount { get; private set; }
        public int EndInitCount { get; private set; }
        public event EventHandler? Raised;
        public void Raise() => Raised?.Invoke(this, EventArgs.Empty);
        public void BeginInit() => BeginInitCount++;
        public void EndInit() => EndInitCount++;
    }

    private sealed class OtherLeaf
    {
    }

    private sealed class ScalarContainer
    {
        public object Foreign { get; } = new();
        public object? Child { get; set; }
        public ScalarContainer() => Child = Foreign;
    }
}
