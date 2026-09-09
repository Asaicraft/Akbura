using Akbura.Akcss;
using Akbura.ComponentTree;
using Akbura.Engine;
using Akbura.HotReload;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless;
using System.Collections;
using System.Collections.Immutable;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Akbura.UnitTests;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class AkburaRenderStateTests
{
    private const string RootSlot = "$root";
    private const string ChildrenSlot = "Panel.Children";
    private const string ScalarSlot = "ContentControl.Content";

    [Fact]
    public void BeginRevision_SkipsAppliedRevisionAndInvalidationReusesNode()
    {
        var state = new AkburaRenderState();
        var describeCount = 0;
        var factoryCount = 0;

        Assert.True(
            state.BeginRevision(
                "revision-1",
                builder =>
                {
                    describeCount++;
                    builder.Add(
                        Definition<LeafNode>(
                            localId: 0,
                            parentId: -1,
                            RootSlot,
                            syntaxIdentity: "Leaf"));
                },
                _ =>
                {
                    factoryCount++;
                    return new LeafNode();
                }));

        var node = state.GetRequired<LeafNode>(0);
        var nodeId = state.GetNodeId(0);

        Assert.True(state.IsNew(0));
        Assert.True(state.ShouldApplyInitialValues(0));
        Assert.Equal(1, node.BeginInitCount);
        Assert.Equal(0, node.EndInitCount);

        state.CompleteRevision();

        Assert.Equal(1, node.EndInitCount);
        Assert.False(
            state.BeginRevision(
                "revision-1",
                _ => describeCount++,
                _ =>
                {
                    factoryCount++;
                    return new LeafNode();
                }));
        Assert.Equal(1, describeCount);
        Assert.Equal(1, factoryCount);
        Assert.Same(node, state.GetRequired<LeafNode>(0));

        state.Invalidate();

        Assert.True(
            state.BeginRevision(
                "revision-1",
                builder =>
                {
                    describeCount++;
                    builder.Add(
                        Definition<LeafNode>(
                            localId: 0,
                            parentId: -1,
                            RootSlot,
                            syntaxIdentity: "Leaf"));
                },
                _ =>
                {
                    factoryCount++;
                    return new LeafNode();
                }));
        Assert.False(state.IsNew(0));
        Assert.False(state.ShouldApplyInitialValues(0));
        Assert.Equal(nodeId, state.GetNodeId(0));
        Assert.Same(node, state.GetRequired<LeafNode>(0));

        state.CompleteRevision();

        Assert.Equal(2, describeCount);
        Assert.Equal(1, factoryCount);
        Assert.Equal(1, node.BeginInitCount);
        Assert.Equal(1, node.EndInitCount);
    }

    [Fact]
    public void Revision_ReordersAndRemovesChildrenWithoutReplacingInstances()
    {
        var state = new AkburaRenderState();
        var firstPlan = new[]
        {
            Spec<ContainerNode>(-1, RootSlot, "Panel"),
            Spec<LeafNode>(0, ChildrenSlot, "Text:Hello"),
            Spec<LeafNode>(0, ChildrenSlot, "Text:Hi"),
        };

        Begin(state, "revision-1", firstPlan);
        var root = state.GetRequired<ContainerNode>(0);
        var hello = state.GetRequired<LeafNode>(1);
        var hi = state.GetRequired<LeafNode>(2);
        var helloNodeId = state.GetNodeId(1);
        var hiNodeId = state.GetNodeId(2);
        state.ReconcileCollection(
            0,
            ChildrenSlot,
            root.Children,
            [hello, hi]);
        state.CompleteRevision();

        Assert.Equal(
            new object[] { root.ForeignItem, hello, hi },
            root.Children.Cast<object>());

        var reorderedPlan = new[]
        {
            Spec<ContainerNode>(-1, RootSlot, "Panel"),
            Spec<LeafNode>(0, ChildrenSlot, "Text:Hi"),
            Spec<LeafNode>(0, ChildrenSlot, "Text:Hello"),
        };

        Begin(state, "revision-2", reorderedPlan);

        Assert.Same(root, state.GetRequired<ContainerNode>(0));
        Assert.Same(hi, state.GetRequired<LeafNode>(1));
        Assert.Same(hello, state.GetRequired<LeafNode>(2));
        Assert.Equal(hiNodeId, state.GetNodeId(1));
        Assert.Equal(helloNodeId, state.GetNodeId(2));
        Assert.False(state.ShouldApplyInitialValues(1));
        Assert.False(state.ShouldApplyInitialValues(2));

        state.ReconcileCollection(
            0,
            ChildrenSlot,
            root.Children,
            [hi, hello]);
        state.CompleteRevision();

        Assert.Equal(
            new object[] { root.ForeignItem, hi, hello },
            root.Children.Cast<object>());

        var removedPlan = new[]
        {
            Spec<ContainerNode>(-1, RootSlot, "Panel"),
            Spec<LeafNode>(0, ChildrenSlot, "Text:Hi"),
        };

        Begin(state, "revision-3", removedPlan);

        Assert.Same(hi, state.GetRequired<LeafNode>(1));
        Assert.Equal(hiNodeId, state.GetNodeId(1));
        state.ReconcileCollection(
            0,
            ChildrenSlot,
            root.Children,
            [hi]);
        state.CompleteRevision();

        Assert.Equal(
            new object[] { root.ForeignItem, hi },
            root.Children.Cast<object>());
    }

    [Fact]
    public void Matcher_ReservesExactSyntaxBeforeTypeFallback()
    {
        var state = new AkburaRenderState();
        var firstPlan = new[]
        {
            Spec<ContainerNode>(-1, RootSlot, "Panel"),
            Spec<LeafNode>(0, ChildrenSlot, "A"),
            Spec<LeafNode>(0, ChildrenSlot, "B"),
        };

        Begin(state, "revision-1", firstPlan);
        var oldA = state.GetRequired<LeafNode>(1);
        var oldB = state.GetRequired<LeafNode>(2);
        state.CompleteRevision();

        var changedPlan = new[]
        {
            Spec<ContainerNode>(-1, RootSlot, "Panel"),
            Spec<LeafNode>(0, ChildrenSlot, "C"),
            Spec<LeafNode>(0, ChildrenSlot, "A"),
        };

        Begin(state, "revision-2", changedPlan);

        Assert.Same(oldB, state.GetRequired<LeafNode>(1));
        Assert.Same(oldA, state.GetRequired<LeafNode>(2));
        Assert.True(state.ShouldApplyInitialValues(1));
        Assert.False(state.ShouldApplyInitialValues(2));

        state.CompleteRevision();
    }

    [Fact]
    public void ExplicitKey_PreservesNodeWhenItsSyntaxChanges()
    {
        var state = new AkburaRenderState();
        var firstPlan = new[]
        {
            Spec<ContainerNode>(-1, RootSlot, "Panel"),
            Spec<LeafNode>(
                0,
                ChildrenSlot,
                "Text:Before",
                explicitKey: "message"),
        };

        Begin(state, "revision-1", firstPlan);
        var node = state.GetRequired<LeafNode>(1);
        var nodeId = state.GetNodeId(1);
        state.CompleteRevision();

        var changedPlan = new[]
        {
            Spec<ContainerNode>(-1, RootSlot, "Panel"),
            Spec<LeafNode>(
                0,
                ChildrenSlot,
                "Text:After",
                explicitKey: "message"),
        };

        Begin(state, "revision-2", changedPlan);

        Assert.Same(node, state.GetRequired<LeafNode>(1));
        Assert.Equal(nodeId, state.GetNodeId(1));
        Assert.False(state.IsNew(1));
        Assert.True(state.ShouldApplyInitialValues(1));

        state.CompleteRevision();
    }

    [Fact]
    public void UnchangedNode_DoesNotReplayInitialValuesWhenSiblingIsAdded()
    {
        var state = new AkburaRenderState();
        var firstPlan = new[]
        {
            Spec<ContainerNode>(-1, RootSlot, "Panel"),
            Spec<LeafNode>(0, ChildrenSlot, "Text:Generated"),
        };

        Begin(state, "revision-1", firstPlan);
        var node = state.GetRequired<LeafNode>(1);
        if (state.ShouldApplyInitialValues(1))
        {
            node.Value = "Generated";
        }

        state.CompleteRevision();
        node.Value = "User value";

        var changedPlan = new[]
        {
            Spec<ContainerNode>(-1, RootSlot, "Panel"),
            Spec<LeafNode>(0, ChildrenSlot, "Text:Generated"),
            Spec<LeafNode>(0, ChildrenSlot, "Text:Sibling"),
        };

        Begin(state, "revision-2", changedPlan);
        if (state.ShouldApplyInitialValues(1))
        {
            state.GetRequired<LeafNode>(1).Value = "Generated";
        }

        Assert.Same(node, state.GetRequired<LeafNode>(1));
        Assert.Equal("User value", node.Value);
        Assert.True(state.IsNew(2));

        state.CompleteRevision();
    }

    [Fact]
    public void UntypedCollectionPath_UsesAvaloniaMove()
    {
        var state = new AkburaRenderState();
        var firstPlan = new[]
        {
            Spec<AvaloniaContainerNode>(-1, RootSlot, "Panel"),
            Spec<LeafNode>(0, ChildrenSlot, "First"),
            Spec<LeafNode>(0, ChildrenSlot, "Second"),
        };

        Begin(state, "revision-1", firstPlan);
        var root = state.GetRequired<AvaloniaContainerNode>(0);
        var first = state.GetRequired<LeafNode>(1);
        var second = state.GetRequired<LeafNode>(2);
        state.ReconcileCollection(
            0,
            ChildrenSlot,
            (object)root.Children,
            new object[] { first, second });
        state.CompleteRevision();

        var actions = new List<NotifyCollectionChangedAction>();
        root.Children.CollectionChanged += (_, eventArgs) =>
            actions.Add(eventArgs.Action);

        var reorderedPlan = new[]
        {
            Spec<AvaloniaContainerNode>(-1, RootSlot, "Panel"),
            Spec<LeafNode>(0, ChildrenSlot, "Second"),
            Spec<LeafNode>(0, ChildrenSlot, "First"),
        };

        Begin(state, "revision-2", reorderedPlan);
        state.ReconcileCollection(
            0,
            ChildrenSlot,
            (object)root.Children,
            new object[] { second, first });
        state.CompleteRevision();

        Assert.Equal(
            new object[] { root.ForeignItem, second, first },
            root.Children);
        Assert.Equal(
            new[] { NotifyCollectionChangedAction.Move },
            actions);
    }

    [Fact]
    public void AbortRevision_RestoresCollectionsAndIsIdempotent()
    {
        var state = new AkburaRenderState();
        var firstPlan = new[]
        {
            Spec<AvaloniaContainerNode>(-1, RootSlot, "Panel"),
            Spec<LeafNode>(0, ChildrenSlot, "First"),
            Spec<LeafNode>(0, ChildrenSlot, "Second"),
        };

        Begin(state, "revision-1", firstPlan);
        var root = state.GetRequired<AvaloniaContainerNode>(0);
        var first = state.GetRequired<LeafNode>(1);
        var second = state.GetRequired<LeafNode>(2);
        state.ReconcileCollection(
            0,
            ChildrenSlot,
            (object)root.Children,
            new object[] { first, second });
        state.CompleteRevision();

        var foreignSuffix = new object();
        root.Children.Add(foreignSuffix);

        var changedPlan = new[]
        {
            Spec<AvaloniaContainerNode>(-1, RootSlot, "Panel"),
            Spec<LeafNode>(0, ChildrenSlot, "Second"),
            Spec<LeafNode>(0, ChildrenSlot, "New"),
            Spec<LeafNode>(0, ChildrenSlot, "First"),
        };

        Begin(state, "revision-2", changedPlan);
        var added = state.GetRequired<LeafNode>(2);
        state.ReconcileCollection(
            0,
            ChildrenSlot,
            (object)root.Children,
            new object[] { second, added, first });

        state.AbortRevision();
        state.AbortRevision();

        Assert.Equal(
            new object[]
            {
                root.ForeignItem,
                first,
                second,
                foreignSuffix,
            },
            root.Children);
        Assert.Same(first, state.GetRequired<LeafNode>(1));
        Assert.Same(second, state.GetRequired<LeafNode>(2));
        Assert.False(
            state.BeginRevision(
                "revision-1",
                _ => throw new InvalidOperationException(),
                _ => throw new InvalidOperationException()));
    }

    [Fact]
    public void CompleteFailure_RollsBackCollectionsAndDoesNotWedgeState()
    {
        var state = new AkburaRenderState();
        var firstPlan = new[]
        {
            Spec<AvaloniaContainerNode>(-1, RootSlot, "Panel"),
            Spec<LeafNode>(0, ChildrenSlot, "Existing"),
        };

        Begin(state, "revision-1", firstPlan);
        var root = state.GetRequired<AvaloniaContainerNode>(0);
        var existing = state.GetRequired<LeafNode>(1);
        state.ReconcileCollection(
            0,
            ChildrenSlot,
            (object)root.Children,
            new object[] { existing });
        state.CompleteRevision();

        var failingPlan = new[]
        {
            Spec<AvaloniaContainerNode>(-1, RootSlot, "Panel"),
            Spec<LeafNode>(0, ChildrenSlot, "Existing"),
            Spec<ThrowingEndInitNode>(0, ChildrenSlot, "Failing"),
        };

        Begin(state, "revision-2", failingPlan);
        var failing = state.GetRequired<ThrowingEndInitNode>(2);
        state.ReconcileCollection(
            0,
            ChildrenSlot,
            (object)root.Children,
            new object[] { existing, failing });

        var exception = Assert.Throws<InvalidOperationException>(
            state.CompleteRevision);

        Assert.Equal("EndInit failed.", exception.Message);
        Assert.Equal(
            new object[] { root.ForeignItem, existing },
            root.Children);
        state.AbortRevision();
        Assert.False(
            state.BeginRevision(
                "revision-1",
                _ => throw new InvalidOperationException(),
                _ => throw new InvalidOperationException()));
    }

    [Fact]
    public void AbortRevision_ReportsEndInitFailure()
    {
        var state = new AkburaRenderState();
        var plan = new[]
        {
            Spec<ThrowingEndInitNode>(-1, RootSlot, "Failing"),
        };

        Begin(state, "revision-1", plan);

        var exception = Assert.Throws<InvalidOperationException>(
            state.AbortRevision);

        Assert.Equal("EndInit failed.", exception.Message);
    }

    [Fact]
    public void AbortRevision_PreservesPrimaryAndRollbackFailures()
    {
        var state = new AkburaRenderState();
        var plan = new[]
        {
            Spec<ThrowingEndInitNode>(-1, RootSlot, "Failing"),
        };

        Begin(state, "revision-1", plan);
        var primaryFailure = new InvalidOperationException(
            "Generated update failed.");

        var exception = Assert.Throws<AggregateException>(
            () => state.AbortRevision(primaryFailure));

        Assert.Collection(
            exception.Flatten().InnerExceptions,
            failure => Assert.Same(primaryFailure, failure),
            failure => Assert.Equal("EndInit failed.", failure.Message));
        state.AbortRevision();
    }

    [Fact]
    public void BeginRevision_PartialBeginInitFailureAttemptsEndInit()
    {
        var state = new AkburaRenderState();
        var node = new ThrowingBeginInitNode();

        var exception = Assert.Throws<InvalidOperationException>(
            () => state.BeginRevision(
                "revision-1",
                builder => builder.Add(
                    Definition<ThrowingBeginInitNode>(
                        0,
                        -1,
                        RootSlot,
                        "Failing")),
                _ => node));

        Assert.Equal("BeginInit failed.", exception.Message);
        Assert.Equal(1, node.BeginInitCount);
        Assert.Equal(1, node.EndInitCount);
        Assert.False(node.IsInitializing);
    }

    [Fact]
    public void EmptyOwnedSlot_PreservesAnchorAcrossOmittedRevision()
    {
        var state = new AkburaRenderState();
        var firstPlan = new[]
        {
            Spec<AvaloniaContainerNode>(-1, RootSlot, "Panel"),
            Spec<LeafNode>(0, ChildrenSlot, "First"),
        };

        Begin(state, "revision-1", firstPlan);
        var root = state.GetRequired<AvaloniaContainerNode>(0);
        var first = state.GetRequired<LeafNode>(1);
        state.ReconcileCollection(
            0,
            ChildrenSlot,
            (object)root.Children,
            new object[] { first });
        state.CompleteRevision();

        var foreignSuffix = new object();
        root.Children.Add(foreignSuffix);

        Begin(
            state,
            "revision-2",
            new[]
            {
                Spec<AvaloniaContainerNode>(-1, RootSlot, "Panel"),
            });
        state.CompleteRevision();

        Assert.Equal(
            new object[] { root.ForeignItem, foreignSuffix },
            root.Children);

        var restoredPlan = new[]
        {
            Spec<AvaloniaContainerNode>(-1, RootSlot, "Panel"),
            Spec<LeafNode>(0, ChildrenSlot, "Restored"),
        };

        Begin(state, "revision-3", restoredPlan);
        var restored = state.GetRequired<LeafNode>(1);
        state.ReconcileCollection(
            0,
            ChildrenSlot,
            (object)root.Children,
            new object[] { restored });
        state.CompleteRevision();

        Assert.Equal(
            new object[]
            {
                root.ForeignItem,
                restored,
                foreignSuffix,
            },
            root.Children);
    }

    [Fact]
    public void ReconcileCollection_CompactsInterleavedForeignItemsSafely()
    {
        var state = new AkburaRenderState();
        var plan = new[]
        {
            Spec<AvaloniaContainerNode>(-1, RootSlot, "Panel"),
            Spec<LeafNode>(0, ChildrenSlot, "First"),
            Spec<LeafNode>(0, ChildrenSlot, "Second"),
        };

        Begin(state, "revision-1", plan);
        var root = state.GetRequired<AvaloniaContainerNode>(0);
        var first = state.GetRequired<LeafNode>(1);
        var second = state.GetRequired<LeafNode>(2);
        state.ReconcileCollection(
            0,
            ChildrenSlot,
            (object)root.Children,
            new object[] { first, second });
        state.CompleteRevision();

        var interleaved = new object();
        root.Children.Insert(2, interleaved);

        Begin(state, "revision-2", plan);
        state.ReconcileCollection(
            0,
            ChildrenSlot,
            (object)root.Children,
            new object[] { first, second });
        state.CompleteRevision();

        Assert.Equal(
            new object[]
            {
                root.ForeignItem,
                first,
                second,
                interleaved,
            },
            root.Children);
    }

    [Fact]
    public void CollectionReconciler_AllowsRepeatedReferenceOccurrences()
    {
        var repeated = string.Intern("Repeated render content");
        IList<object> collection = new List<object>();

        AkburaRenderCollectionReconciler.Reconcile(
            collection,
            Array.Empty<object>(),
            new object[] { repeated, repeated });

        Assert.Equal(
            new object[] { repeated, repeated },
            collection);

        AkburaRenderCollectionReconciler.Reconcile(
            collection,
            new object[] { repeated, repeated },
            new object[] { repeated });

        Assert.Equal(new object[] { repeated }, collection);

        AkburaRenderCollectionReconciler.Reconcile(
            collection,
            new object[] { repeated },
            new object[] { repeated, repeated });

        Assert.Equal(
            new object[] { repeated, repeated },
            collection);
    }

    [Fact]
    public void ClrProperty_OmissionRestoresOriginalBaseline()
    {
        var state = new AkburaRenderState();
        var plan = new[]
        {
            Spec<ScalarContainerNode>(-1, RootSlot, "Root"),
            Spec<LeafNode>(0, ScalarSlot, "Child"),
        };

        Begin(state, "revision-1", plan);
        var root = state.GetRequired<ScalarContainerNode>(0);
        var child = state.GetRequired<LeafNode>(1);
        var baseline = new object();
        root.ClrChild = baseline;
        state.ReconcileClrProperty(
            0,
            ScalarSlot,
            root,
            typeof(ScalarContainerNode),
            nameof(ScalarContainerNode.ClrChild),
            child);
        state.CompleteRevision();

        Assert.Same(child, root.ClrChild);

        var runtimeValue = new object();
        root.ClrChild = runtimeValue;
        Begin(state, "revision-2", plan);
        state.ReconcileClrProperty(
            0,
            ScalarSlot,
            root,
            typeof(ScalarContainerNode),
            nameof(ScalarContainerNode.ClrChild),
            child);
        state.CompleteRevision();

        Assert.Same(runtimeValue, root.ClrChild);

        Begin(
            state,
            "revision-3",
            new[]
            {
                Spec<ScalarContainerNode>(-1, RootSlot, "Root"),
            });
        state.CompleteRevision();

        Assert.Same(baseline, root.ClrChild);
    }

    [Fact]
    public void AvaloniaProperty_AbortRestoresAppliedChildAndOmissionClearsValue()
    {
        var state = new AkburaRenderState();
        var firstPlan = new[]
        {
            Spec<ScalarContainerNode>(-1, RootSlot, "Root"),
            Spec<LeafNode>(0, ScalarSlot, "Child"),
        };

        Begin(state, "revision-1", firstPlan);
        var root = state.GetRequired<ScalarContainerNode>(0);
        var child = state.GetRequired<LeafNode>(1);
        state.ReconcileAvaloniaProperty(
            0,
            ScalarSlot,
            root,
            ScalarContainerNode.AvaloniaChildProperty,
            child);
        state.CompleteRevision();

        var changedPlan = new[]
        {
            Spec<ScalarContainerNode>(-1, RootSlot, "Root"),
            Spec<OtherLeafNode>(0, ScalarSlot, "ChangedChild"),
        };

        Begin(state, "revision-2", changedPlan);
        var changedChild = state.GetRequired<OtherLeafNode>(1);
        state.ReconcileAvaloniaProperty(
            0,
            ScalarSlot,
            root,
            ScalarContainerNode.AvaloniaChildProperty,
            changedChild);
        Assert.Same(changedChild, root.AvaloniaChild);

        state.AbortRevision();

        Assert.Same(child, root.AvaloniaChild);
        Assert.True(root.IsSet(ScalarContainerNode.AvaloniaChildProperty));

        Begin(
            state,
            "revision-3",
            new[]
            {
                Spec<ScalarContainerNode>(-1, RootSlot, "Root"),
            });
        state.CompleteRevision();

        Assert.Null(root.AvaloniaChild);
        Assert.False(root.IsSet(ScalarContainerNode.AvaloniaChildProperty));
    }

    [Fact]
    public void ScalarProperty_CompleteFailureRollsBackAppliedChild()
    {
        var state = new AkburaRenderState();
        var firstPlan = new[]
        {
            Spec<ScalarContainerNode>(-1, RootSlot, "Root"),
            Spec<LeafNode>(0, ScalarSlot, "Child"),
        };

        Begin(state, "revision-1", firstPlan);
        var root = state.GetRequired<ScalarContainerNode>(0);
        var child = state.GetRequired<LeafNode>(1);
        state.ReconcileClrProperty(
            0,
            ScalarSlot,
            root,
            typeof(ScalarContainerNode),
            nameof(ScalarContainerNode.ClrChild),
            child);
        state.CompleteRevision();

        var failingPlan = new[]
        {
            Spec<ScalarContainerNode>(-1, RootSlot, "Root"),
            Spec<ThrowingEndInitNode>(0, ScalarSlot, "Failing"),
        };

        Begin(state, "revision-2", failingPlan);
        var failing = state.GetRequired<ThrowingEndInitNode>(1);
        state.ReconcileClrProperty(
            0,
            ScalarSlot,
            root,
            typeof(ScalarContainerNode),
            nameof(ScalarContainerNode.ClrChild),
            failing);

        Assert.Throws<InvalidOperationException>(state.CompleteRevision);

        Assert.Same(child, root.ClrChild);
        state.AbortRevision();
    }

    [Fact]
    public void ClrValue_UsesDeclarationIdentityAndOmissionRestoresBaseline()
    {
        var state = new AkburaRenderState();
        var plan = new[]
        {
            Spec<ScalarContainerNode>(-1, RootSlot, "Root"),
        };

        Begin(state, "revision-1", plan);
        var root = state.GetRequired<ScalarContainerNode>(0);
        root.ClrNumber = 7;
        state.ReconcileClrValue(
            0,
            "Number",
            root,
            typeof(ScalarContainerNode),
            nameof(ScalarContainerNode.ClrNumber),
            "Number=10",
            10);
        state.CompleteRevision();

        Assert.Equal(10, root.ClrNumber);

        root.ClrNumber = 99;
        Begin(state, "revision-2", plan);
        state.ReconcileClrValue(
            0,
            "Number",
            root,
            typeof(ScalarContainerNode),
            nameof(ScalarContainerNode.ClrNumber),
            "Number=10",
            10);
        state.CompleteRevision();

        Assert.Equal(99, root.ClrNumber);

        Begin(state, "revision-3", plan);
        state.ReconcileClrValue(
            0,
            "Number",
            root,
            typeof(ScalarContainerNode),
            nameof(ScalarContainerNode.ClrNumber),
            "Number=11",
            11);
        state.CompleteRevision();

        Assert.Equal(11, root.ClrNumber);

        Begin(state, "revision-4", plan);
        state.CompleteRevision();

        Assert.Equal(7, root.ClrNumber);
    }

    [Fact]
    public void PrepareRevisionCompletion_AppliesOmissionsAndAbortRestoresAppliedState()
    {
        var state = new AkburaRenderState();
        var plan = new[]
        {
            Spec<ScalarContainerNode>(-1, RootSlot, "Root"),
        };

        Begin(state, "revision-1", plan);
        var root = state.GetRequired<ScalarContainerNode>(0);
        root.ClrNumber = 7;
        state.ReconcileClrValue(
            0,
            "Number",
            root,
            typeof(ScalarContainerNode),
            nameof(ScalarContainerNode.ClrNumber),
            "Number=10",
            10);
        state.CompleteRevision();

        Begin(state, "revision-2", plan);
        state.PrepareRevisionCompletion();

        Assert.Equal(7, root.ClrNumber);

        state.PrepareRevisionCompletion();
        Assert.Equal(7, root.ClrNumber);

        var exception = Assert.Throws<InvalidOperationException>(
            () => state.ReconcileClrValue(
                0,
                "Number",
                root,
                typeof(ScalarContainerNode),
                nameof(ScalarContainerNode.ClrNumber),
                "Number=11",
                11));
        Assert.Contains("already been prepared", exception.Message);

        state.AbortRevision();

        Assert.Equal(10, root.ClrNumber);
        Assert.False(
            state.BeginRevision(
                "revision-1",
                _ => throw new InvalidOperationException(),
                _ => throw new InvalidOperationException()));
    }

    [Fact]
    public void AvaloniaValue_SupportsNullAndAbortRestoresRuntimeValue()
    {
        var state = new AkburaRenderState();
        var plan = new[]
        {
            Spec<ScalarContainerNode>(-1, RootSlot, "Root"),
        };

        Begin(state, "revision-1", plan);
        var root = state.GetRequired<ScalarContainerNode>(0);
        root.AvaloniaValue = "baseline";
        state.ReconcileAvaloniaValue(
            0,
            "Value",
            root,
            ScalarContainerNode.AvaloniaValueProperty,
            "Value=null",
            null);
        state.CompleteRevision();

        Assert.Null(root.AvaloniaValue);

        root.AvaloniaValue = "runtime";
        Begin(state, "revision-2", plan);
        state.ReconcileAvaloniaValue(
            0,
            "Value",
            root,
            ScalarContainerNode.AvaloniaValueProperty,
            "Value=null",
            null);
        state.CompleteRevision();

        Assert.Equal("runtime", root.AvaloniaValue);

        Begin(state, "revision-3", plan);
        state.ReconcileAvaloniaValue(
            0,
            "Value",
            root,
            ScalarContainerNode.AvaloniaValueProperty,
            "Value=changed",
            "changed");

        Assert.Equal("changed", root.AvaloniaValue);

        state.AbortRevision();
        state.AbortRevision();

        Assert.Equal("runtime", root.AvaloniaValue);

        Begin(state, "revision-4", plan);
        state.CompleteRevision();

        Assert.Equal("baseline", root.AvaloniaValue);
        Assert.True(root.IsSet(ScalarContainerNode.AvaloniaValueProperty));
    }

    [Fact]
    public void ShouldApplyOperation_TracksGranularDeclarationIdentity()
    {
        var state = new AkburaRenderState();
        var plan = new[]
        {
            Spec<LeafNode>(-1, RootSlot, "Leaf"),
        };

        Begin(state, "revision-1", plan);
        Assert.True(
            state.ShouldApplyOperation(
                0,
                "Text",
                "Text=Initial"));
        state.CompleteRevision();

        Begin(state, "revision-2", plan);
        Assert.False(
            state.ShouldApplyOperation(
                0,
                "Text",
                "Text=Initial"));
        Assert.True(
            state.ShouldApplyOperation(
                0,
                "Width",
                "Width=42"));
        state.CompleteRevision();

        Begin(state, "revision-3", plan);
        Assert.True(
            state.ShouldApplyOperation(
                0,
                "Text",
                "Text=Changed"));
        state.CompleteRevision();

        Begin(state, "revision-4", plan);
        Assert.True(
            state.ShouldApplyOperation(
                0,
                "Width",
                "Width=42"));
        state.CompleteRevision();
    }

    [Fact]
    public void OwnedClrEvent_ReplacesRollsBackAndRemovesExactHandler()
    {
        var state = new AkburaRenderState();
        var plan = new[]
        {
            Spec<EventNode>(-1, RootSlot, "Event node"),
        };
        var firstCount = 0;
        var secondCount = 0;
        var abortedCount = 0;
        EventHandler first = (_, _) => firstCount++;
        EventHandler second = (_, _) => secondCount++;
        EventHandler aborted = (_, _) => abortedCount++;

        Begin(state, "revision-1", plan);
        var node = state.GetRequired<EventNode>(0);
        Assert.True(
            state.ShouldApplyOwnedOperation(
                0,
                "event:Changed",
                "first"));
        state.ApplyClrEventOperation(
            0,
            "event:Changed",
            node,
            typeof(EventNode),
            nameof(EventNode.Changed),
            first);
        state.CompleteRevision();

        node.RaiseChanged();
        Assert.Equal(1, firstCount);

        Begin(state, "revision-2", plan);
        Assert.False(
            state.ShouldApplyOwnedOperation(
                0,
                "event:Changed",
                "first"));
        state.CompleteRevision();

        node.RaiseChanged();
        Assert.Equal(2, firstCount);

        Begin(state, "revision-3", plan);
        Assert.True(
            state.ShouldApplyOwnedOperation(
                0,
                "event:Changed",
                "second"));
        state.ApplyClrEventOperation(
            0,
            "event:Changed",
            node,
            typeof(EventNode),
            nameof(EventNode.Changed),
            second);
        state.CompleteRevision();

        node.RaiseChanged();
        Assert.Equal(2, firstCount);
        Assert.Equal(1, secondCount);

        Begin(state, "revision-4", plan);
        Assert.True(
            state.ShouldApplyOwnedOperation(
                0,
                "event:Changed",
                "aborted"));
        state.ApplyClrEventOperation(
            0,
            "event:Changed",
            node,
            typeof(EventNode),
            nameof(EventNode.Changed),
            aborted);
        state.AbortRevision();

        node.RaiseChanged();
        Assert.Equal(2, secondCount);
        Assert.Equal(0, abortedCount);

        Begin(state, "revision-5", plan);
        state.CompleteRevision();

        node.RaiseChanged();
        Assert.Equal(2, secondCount);
        Assert.Equal(0, abortedCount);
    }

    [Fact]
    public void OwnedClrEvent_PartialApplyFailureDoesNotLeakHandler()
    {
        var state = new AkburaRenderState();
        var plan = new[]
        {
            Spec<ThrowingEventNode>(-1, RootSlot, "Event node"),
        };
        var count = 0;
        EventHandler handler = (_, _) => count++;

        Begin(state, "revision-1", plan);
        var node = state.GetRequired<ThrowingEventNode>(0);
        node.ThrowAfterAdd = true;
        Assert.True(
            state.ShouldApplyOwnedOperation(
                0,
                "event:Changed",
                "throwing"));

        var exception =
            Assert.Throws<System.Reflection.TargetInvocationException>(
            () => state.ApplyClrEventOperation(
                0,
                "event:Changed",
                node,
                typeof(ThrowingEventNode),
                nameof(ThrowingEventNode.Changed),
                handler));

        Assert.Equal(
            "Event add failed.",
            Assert.IsType<InvalidOperationException>(
                exception.InnerException).Message);
        node.RaiseChanged();
        Assert.Equal(0, count);
        Assert.Equal(1, node.RemoveAttemptCount);
    }

    [Fact]
    public void OwnedClrEvent_ReportsApplyAndAutomaticRollbackFailures()
    {
        var state = new AkburaRenderState();
        var plan = new[]
        {
            Spec<ThrowingEventNode>(-1, RootSlot, "Event node"),
        };
        var count = 0;
        EventHandler handler = (_, _) => count++;

        Begin(state, "revision-1", plan);
        var node = state.GetRequired<ThrowingEventNode>(0);
        node.ThrowAfterAdd = true;
        node.ThrowAfterRemove = true;
        Assert.True(
            state.ShouldApplyOwnedOperation(
                0,
                "event:Changed",
                "throwing"));

        var exception = Assert.Throws<AggregateException>(
            () => state.ApplyClrEventOperation(
                0,
                "event:Changed",
                node,
                typeof(ThrowingEventNode),
                nameof(ThrowingEventNode.Changed),
                handler));

        Assert.Equal(2, exception.InnerExceptions.Count);
        Assert.Contains(
            exception.InnerExceptions,
            static current =>
                current is System.Reflection.TargetInvocationException
                {
                    InnerException.Message: "Event add failed.",
                });
        Assert.Contains(
            exception.InnerExceptions,
            static current =>
                current is System.Reflection.TargetInvocationException
                {
                    InnerException.Message: "Event remove failed.",
                });
        node.RaiseChanged();
        Assert.Equal(0, count);
    }

    [Fact]
    public void OwnedClrEvent_AbortRestoresPreviousWhenReplacementReleaseFails()
    {
        var state = new AkburaRenderState();
        var plan = new[]
        {
            Spec<ThrowingEventNode>(-1, RootSlot, "Event node"),
        };
        var firstCount = 0;
        var secondCount = 0;
        EventHandler first = (_, _) => firstCount++;
        EventHandler second = (_, _) => secondCount++;

        Begin(state, "revision-1", plan);
        var node = state.GetRequired<ThrowingEventNode>(0);
        Assert.True(
            state.ShouldApplyOwnedOperation(
                0,
                "event:Changed",
                "first"));
        state.ApplyClrEventOperation(
            0,
            "event:Changed",
            node,
            typeof(ThrowingEventNode),
            nameof(ThrowingEventNode.Changed),
            first);
        state.CompleteRevision();

        Begin(state, "revision-2", plan);
        Assert.True(
            state.ShouldApplyOwnedOperation(
                0,
                "event:Changed",
                "second"));
        state.ApplyClrEventOperation(
            0,
            "event:Changed",
            node,
            typeof(ThrowingEventNode),
            nameof(ThrowingEventNode.Changed),
            second);

        node.ThrowAfterRemove = true;
        var releaseException =
            Assert.Throws<System.Reflection.TargetInvocationException>(
                state.AbortRevision);
        Assert.Equal(
            "Event remove failed.",
            Assert.IsType<InvalidOperationException>(
                releaseException.InnerException).Message);
        Assert.Equal(3, node.AddAttemptCount);

        node.RaiseChanged();
        Assert.Equal(1, firstCount);
        Assert.Equal(0, secondCount);
    }

    [Fact]
    public void OwnedClrEvent_PartialReleaseFailureReappliesPreviousHandler()
    {
        var state = new AkburaRenderState();
        var plan = new[]
        {
            Spec<ThrowingEventNode>(-1, RootSlot, "Event node"),
        };
        var firstCount = 0;
        EventHandler first = (_, _) => firstCount++;
        EventHandler second = (_, _) => { };

        Begin(state, "revision-1", plan);
        var node = state.GetRequired<ThrowingEventNode>(0);
        Assert.True(
            state.ShouldApplyOwnedOperation(
                0,
                "event:Changed",
                "first"));
        state.ApplyClrEventOperation(
            0,
            "event:Changed",
            node,
            typeof(ThrowingEventNode),
            nameof(ThrowingEventNode.Changed),
            first);
        state.CompleteRevision();

        Begin(state, "revision-2", plan);
        Assert.True(
            state.ShouldApplyOwnedOperation(
                0,
                "event:Changed",
                "second"));
        node.ThrowAfterRemove = true;

        var exception =
            Assert.Throws<System.Reflection.TargetInvocationException>(
                () => state.ApplyClrEventOperation(
                    0,
                    "event:Changed",
                    node,
                    typeof(ThrowingEventNode),
                    nameof(ThrowingEventNode.Changed),
                    second));

        Assert.Equal(
            "Event remove failed.",
            Assert.IsType<InvalidOperationException>(
                exception.InnerException).Message);
        Assert.Equal(2, node.AddAttemptCount);

        node.RaiseChanged();
        Assert.Equal(1, firstCount);
    }

    [Fact]
    public void OwnedClrEvent_UsesExactDeclaringTypeForHiddenEvent()
    {
        var state = new AkburaRenderState();
        var plan = new[]
        {
            Spec<HiddenEventDerivedNode>(-1, RootSlot, "Event node"),
        };
        var count = 0;
        EventHandler handler = (_, _) => count++;

        Begin(state, "revision-1", plan);
        var node = state.GetRequired<HiddenEventDerivedNode>(0);
        Assert.True(
            state.ShouldApplyOwnedOperation(
                0,
                "event:Changed",
                "base"));
        state.ApplyClrEventOperation(
            0,
            "event:Changed",
            node,
            typeof(HiddenEventBaseNode),
            nameof(HiddenEventBaseNode.Changed),
            handler);
        state.CompleteRevision();

        node.RaiseChanged();
        Assert.Equal(0, count);

        node.RaiseBaseChanged();
        Assert.Equal(1, count);

        Begin(state, "revision-2", plan);
        state.CompleteRevision();

        node.RaiseBaseChanged();
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task OwnedBinding_ReplacesAndRemovalRestoresBaseline()
    {
        await RunOnAvaloniaThread(
            OwnedBinding_ReplacesAndRemovalRestoresBaselineCore);
    }

    private static void OwnedBinding_ReplacesAndRemovalRestoresBaselineCore()
    {
        var state = new AkburaRenderState();
        var plan = new[]
        {
            Spec<BindingNode>(-1, RootSlot, "Binding node"),
        };
        var firstSource = new BindingSource("First");
        var secondSource = new BindingSource("Second");

        Begin(state, "revision-1", plan);
        var node = state.GetRequired<BindingNode>(0);
        node.Value = "Baseline";
        Assert.True(
            state.ShouldApplyOwnedOperation(
                0,
                "property:Value",
                "first"));
        state.ApplyBindingOperation(
            0,
            "property:Value",
            node,
            BindingNode.ValueProperty,
            CreateBinding(firstSource));
        state.CompleteRevision();

        Assert.Equal("First", node.Value);
        firstSource.Value = "First changed";
        Assert.Equal("First changed", node.Value);

        Begin(state, "revision-2", plan);
        Assert.True(
            state.ShouldApplyOwnedOperation(
                0,
                "property:Value",
                "second"));
        state.ApplyBindingOperation(
            0,
            "property:Value",
            node,
            BindingNode.ValueProperty,
            CreateBinding(secondSource));
        state.CompleteRevision();

        firstSource.Value = "Detached";
        Assert.Equal("Second", node.Value);
        secondSource.Value = "Second changed";
        Assert.Equal("Second changed", node.Value);

        Begin(state, "revision-3", plan);
        state.CompleteRevision();

        secondSource.Value = "Detached too";
        Assert.Equal("Baseline", node.Value);
    }

    [Fact]
    public async Task OwnedBinding_AbortConstantTransitionRestoresGeneratedConstant()
    {
        await RunOnAvaloniaThread(
            OwnedBinding_AbortConstantTransitionRestoresGeneratedConstantCore);
    }

    private static void OwnedBinding_AbortConstantTransitionRestoresGeneratedConstantCore()
    {
        var state = new AkburaRenderState();
        var plan = new[]
        {
            Spec<BindingNode>(-1, RootSlot, "Binding node"),
        };
        var source = new BindingSource("Bound value");

        Begin(state, "revision-1", plan);
        var node = state.GetRequired<BindingNode>(0);
        state.ReconcileAvaloniaValue(
            0,
            "property:Value",
            node,
            BindingNode.ValueProperty,
            "constant",
            "Generated constant");
        state.CompleteRevision();

        Assert.Equal("Generated constant", node.Value);

        Begin(state, "revision-2", plan);
        Assert.True(
            state.ShouldApplyOwnedOperation(
                0,
                "property:Value",
                "binding"));
        state.ApplyBindingOperation(
            0,
            "property:Value",
            node,
            BindingNode.ValueProperty,
            CreateBinding(source));
        Assert.Equal("Bound value", node.Value);

        state.AbortRevision();

        Assert.Equal("Generated constant", node.Value);
        source.Value = "Detached value";
        Assert.Equal("Generated constant", node.Value);
    }

    [Fact]
    public void OwnedOperation_ChangedRegistrationWithoutResourceFailsAndKeepsPrevious()
    {
        var state = new AkburaRenderState();
        var plan = new[]
        {
            Spec<EventNode>(-1, RootSlot, "Event node"),
        };
        var count = 0;
        EventHandler handler = (_, _) => count++;

        Begin(state, "revision-1", plan);
        var node = state.GetRequired<EventNode>(0);
        Assert.True(
            state.ShouldApplyOwnedOperation(
                0,
                "event:Changed",
                "first"));
        state.ApplyClrEventOperation(
            0,
            "event:Changed",
            node,
            typeof(EventNode),
            nameof(EventNode.Changed),
            handler);
        state.CompleteRevision();

        Begin(state, "revision-2", plan);
        Assert.True(
            state.ShouldApplyOwnedOperation(
                0,
                "event:Changed",
                "changed"));

        var exception = Assert.Throws<InvalidOperationException>(
            state.CompleteRevision);

        Assert.Contains("without a runtime resource", exception.Message);
        node.RaiseChanged();
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task OwnedObservableBinding_RootReplacementSupportsAbortAndReleasesOldRoot()
    {
        await RunOnAvaloniaThread(
            OwnedObservableBinding_RootReplacementSupportsAbortAndReleasesOldRootCore);
    }

    private static void OwnedObservableBinding_RootReplacementSupportsAbortAndReleasesOldRootCore()
    {
        const string dataContextSlot =
            "property:Avalonia.StyledElement.DataContextProperty";
        var state = new AkburaRenderState();
        var source = new Border
        {
            DataContext = "Initial",
        };
        var originalPlan = new[]
        {
            Spec<Border>(-1, RootSlot, "Border"),
        };

        Begin(state, "revision-1", originalPlan);
        var originalRoot = state.GetRequired<Border>(0);
        Assert.True(
            state.ShouldApplyOwnedOperation(
                0,
                dataContextSlot,
                "implicit-root-data-context"));
        state.ApplyObservableBindingOperation(
            0,
            dataContextSlot,
            originalRoot,
            StyledElement.DataContextProperty,
            source.GetObservable(StyledElement.DataContextProperty));
        state.CompleteRevision();

        Assert.Equal("Initial", originalRoot.DataContext);
        source.DataContext = "Before replacement";
        Assert.Equal("Before replacement", originalRoot.DataContext);

        var replacementPlan = new[]
        {
            Spec<Button>(-1, RootSlot, "Button"),
        };

        Begin(state, "revision-2", replacementPlan);
        var abortedRoot = state.GetRequired<Button>(0);
        Assert.True(
            state.ShouldApplyOwnedOperation(
                0,
                dataContextSlot,
                "implicit-root-data-context"));
        state.ApplyObservableBindingOperation(
            0,
            dataContextSlot,
            abortedRoot,
            StyledElement.DataContextProperty,
            source.GetObservable(StyledElement.DataContextProperty));
        state.AbortRevision();

        source.DataContext = "After abort";
        Assert.Equal("After abort", originalRoot.DataContext);
        Assert.Null(abortedRoot.DataContext);

        Begin(state, "revision-2", replacementPlan);
        var replacementRoot = state.GetRequired<Button>(0);
        Assert.True(
            state.ShouldApplyOwnedOperation(
                0,
                dataContextSlot,
                "implicit-root-data-context"));
        state.ApplyObservableBindingOperation(
            0,
            dataContextSlot,
            replacementRoot,
            StyledElement.DataContextProperty,
            source.GetObservable(StyledElement.DataContextProperty));
        state.CompleteRevision();

        Assert.Null(originalRoot.DataContext);
        source.DataContext = "After replacement";
        Assert.Equal("After replacement", replacementRoot.DataContext);
        Assert.Null(originalRoot.DataContext);
    }

    [Fact]
    public void OwnedAkcss_RemovingDeclarationClearsCascadeAndRestoresUnsetBaseline()
    {
        var state = new AkburaRenderState();
        var plan = new[]
        {
            Spec<Border>(-1, RootSlot, "Border"),
        };
        var activator = new TrackingAkcssActivator("generated");
        ImmutableArray<AkcssStyleActivator> styles = [activator];

        Begin(state, "revision-1", plan);
        var node = state.GetRequired<Border>(0);
        state.ApplyAkcssStylesOperation(0, node, styles);
        state.CompleteRevision();

        Assert.Equal(styles, AkburaControl.GetAkcssStyles(node));
        Assert.True(node.IsSet(AkburaControl.AkcssStylesProperty));
        Assert.Equal(1, activator.SubscriptionCount);

        Begin(state, "revision-2", plan);
        state.CompleteRevision();

        Assert.Empty(AkburaControl.GetAkcssStyles(node));
        Assert.False(
            node.GetBaseValue(AkburaControl.AkcssStylesProperty).HasValue);
        Assert.Equal(0, activator.SubscriptionCount);
    }

    [Fact]
    public void OwnedAkcss_RemovingNodeDetachesItsCascade()
    {
        var state = new AkburaRenderState();
        var originalPlan = new[]
        {
            Spec<StackPanel>(-1, RootSlot, "StackPanel"),
            Spec<Border>(0, ChildrenSlot, "Border"),
        };
        var activator = new TrackingAkcssActivator("removed-node");
        ImmutableArray<AkcssStyleActivator> styles = [activator];

        Begin(state, "revision-1", originalPlan);
        var removedNode = state.GetRequired<Border>(1);
        state.ApplyAkcssStylesOperation(1, removedNode, styles);
        state.CompleteRevision();

        Assert.Equal(1, activator.SubscriptionCount);

        Begin(
            state,
            "revision-2",
            [Spec<StackPanel>(-1, RootSlot, "StackPanel")]);
        state.CompleteRevision();

        Assert.Empty(AkburaControl.GetAkcssStyles(removedNode));
        Assert.False(
            removedNode.GetBaseValue(
                AkburaControl.AkcssStylesProperty).HasValue);
        Assert.Equal(0, activator.SubscriptionCount);
    }

    [Fact]
    public void OwnedAkcss_AbortRestoresPreviousGeneratedCascade()
    {
        var state = new AkburaRenderState();
        var plan = new[]
        {
            Spec<Border>(-1, RootSlot, "Border"),
        };
        var originalActivator = new TrackingAkcssActivator("original");
        var replacementActivator = new TrackingAkcssActivator("replacement");
        ImmutableArray<AkcssStyleActivator> original = [originalActivator];
        ImmutableArray<AkcssStyleActivator> replacement = [replacementActivator];

        Begin(state, "revision-1", plan);
        var node = state.GetRequired<Border>(0);
        state.ApplyAkcssStylesOperation(0, node, original);
        state.CompleteRevision();

        Begin(state, "revision-2", plan);
        state.ApplyAkcssStylesOperation(0, node, replacement);

        Assert.Equal(replacement, AkburaControl.GetAkcssStyles(node));
        Assert.Equal(0, originalActivator.SubscriptionCount);
        Assert.Equal(1, replacementActivator.SubscriptionCount);

        state.AbortRevision();

        Assert.Equal(original, AkburaControl.GetAkcssStyles(node));
        Assert.Equal(1, originalActivator.SubscriptionCount);
        Assert.Equal(0, replacementActivator.SubscriptionCount);
    }

    [Fact]
    public void OwnedAkcss_OrdinaryObjectRestoresExistingCascade()
    {
        var state = new AkburaRenderState();
        var plan = new[]
        {
            Spec<PlainAkcssNode>(-1, RootSlot, "Plain"),
        };
        var baselineActivator = new TrackingAkcssActivator("baseline");
        var generatedActivator = new TrackingAkcssActivator("generated");
        ImmutableArray<AkcssStyleActivator> baseline = [baselineActivator];
        ImmutableArray<AkcssStyleActivator> generated = [generatedActivator];

        Begin(state, "revision-1", plan);
        var node = state.GetRequired<PlainAkcssNode>(0);
        AkburaControl.SetAkcssStyles(node, baseline);
        state.ApplyAkcssStylesOperation(0, node, generated);
        state.CompleteRevision();

        Assert.Equal(generated, AkcssRuntime.GetStyles(node));
        Assert.Equal(0, baselineActivator.SubscriptionCount);
        Assert.Equal(1, generatedActivator.SubscriptionCount);

        Begin(state, "revision-2", plan);
        state.CompleteRevision();

        Assert.Equal(baseline, AkcssRuntime.GetStyles(node));
        Assert.Equal(1, baselineActivator.SubscriptionCount);
        Assert.Equal(0, generatedActivator.SubscriptionCount);
    }

    private static Binding CreateBinding(BindingSource source)
    {
        return new Binding(nameof(BindingSource.Value))
        {
            Source = source,
        };
    }

    private static async Task RunOnAvaloniaThread(Action action)
    {
        using var session = HeadlessUnitTestSession.StartNew(
            typeof(AvaloniaTestAppBuilder));
        await session.Dispatch(
            action,
            CancellationToken.None);
    }

    [Fact]
    public void PrepareRevisionCompletion_EndsNewNodesChildrenFirstAndIsIdempotent()
    {
        var state = new AkburaRenderState();
        var endOrder = new List<int>();

        Assert.True(
            state.BeginRevision(
                "revision-1",
                builder =>
                {
                    builder.Add(
                        Definition<OrderedNode>(
                            localId: 0,
                            parentId: -1,
                            RootSlot,
                            syntaxIdentity: "Root"));
                    builder.Add(
                        Definition<OrderedNode>(
                            localId: 1,
                            parentId: 0,
                            ChildrenSlot,
                            syntaxIdentity: "Child"));
                    builder.Add(
                        Definition<OrderedNode>(
                            localId: 2,
                            parentId: 1,
                            ChildrenSlot,
                            syntaxIdentity: "Grandchild"));
                },
                localId => new OrderedNode(localId, endOrder)));

        state.PrepareRevisionCompletion();

        Assert.Equal(new[] { 2, 1, 0 }, endOrder);

        state.PrepareRevisionCompletion();
        state.CompleteRevision();

        Assert.Equal(new[] { 2, 1, 0 }, endOrder);
    }

    [Fact]
    public void GenericCollectionReconciler_UsesAvaloniaMoveAndPreservesForeignItems()
    {
        var foreign = new LeafNode();
        var first = new LeafNode();
        var second = new LeafNode();
        var collection = new AvaloniaList<LeafNode>
        {
            foreign,
            first,
            second,
        };
        var actions = new List<NotifyCollectionChangedAction>();
        collection.CollectionChanged += (_, eventArgs) =>
            actions.Add(eventArgs.Action);

        AkburaRenderCollectionReconciler.Reconcile(
            collection,
            new[] { first, second },
            new[] { second, first });

        Assert.Equal(
            new[] { foreign, second, first },
            collection);
        Assert.Equal(
            new[] { NotifyCollectionChangedAction.Move },
            actions);
    }

    [Fact]
    public void InvalidPlansAndFactoriesFailBeforeARevisionIsPublished()
    {
        var state = new AkburaRenderState();

        var gapException = Assert.Throws<ArgumentException>(
            () => state.BeginRevision(
                "gap",
                builder => builder.Add(
                    Definition<LeafNode>(
                        localId: 1,
                        parentId: -1,
                        RootSlot,
                        syntaxIdentity: "Leaf")),
                _ => new LeafNode()));
        Assert.Contains("position 0", gapException.Message);

        var duplicateKeyException = Assert.Throws<ArgumentException>(
            () => state.BeginRevision(
                "duplicate-key",
                builder =>
                {
                    builder.Add(
                        Definition<ContainerNode>(
                            localId: 0,
                            parentId: -1,
                            RootSlot,
                            syntaxIdentity: "Panel"));
                    builder.Add(
                        Definition<LeafNode>(
                            localId: 1,
                            parentId: 0,
                            ChildrenSlot,
                            explicitKey: "same",
                            syntaxIdentity: "A"));
                    builder.Add(
                        Definition<LeafNode>(
                            localId: 2,
                            parentId: 0,
                            ChildrenSlot,
                            explicitKey: "same",
                            syntaxIdentity: "B"));
                },
                _ => new LeafNode()));
        Assert.Contains("positions 1 and 2", duplicateKeyException.Message);

        var wrongTypeException = Assert.Throws<InvalidOperationException>(
            () => state.BeginRevision(
                "wrong-type",
                builder => builder.Add(
                    Definition<LeafNode>(
                        localId: 0,
                        parentId: -1,
                        RootSlot,
                        syntaxIdentity: "Leaf")),
                _ => new OtherLeafNode()));
        Assert.Contains("expected exact type", wrongTypeException.Message);

        var shared = new LeafNode();
        var duplicateInstanceException = Assert.Throws<InvalidOperationException>(
            () => state.BeginRevision(
                "duplicate-instance",
                builder =>
                {
                    builder.Add(
                        Definition<LeafNode>(
                            localId: 0,
                            parentId: -1,
                            RootSlot,
                            syntaxIdentity: "Root"));
                    builder.Add(
                        Definition<LeafNode>(
                            localId: 1,
                            parentId: 0,
                            ChildrenSlot,
                            syntaxIdentity: "Child"));
                },
                _ => shared));
        Assert.Contains("reused an instance", duplicateInstanceException.Message);
        Assert.Equal(1, shared.BeginInitCount);
        Assert.Equal(1, shared.EndInitCount);

        Assert.True(
            state.BeginRevision(
                "valid",
                builder => builder.Add(
                    Definition<LeafNode>(
                        localId: 0,
                        parentId: -1,
                        RootSlot,
                        syntaxIdentity: "Leaf")),
                _ => new LeafNode()));
        state.CompleteRevision();
    }

    [Fact]
    public void CollectionDivergenceFailsWithoutClearingForeignState()
    {
        var first = new LeafNode();
        var second = new LeafNode();
        var foreign = new LeafNode();
        IList<LeafNode> target = new List<LeafNode>
        {
            foreign,
            first,
            second,
        };

        target.Remove(first);

        var exception = Assert.Throws<InvalidOperationException>(
            () => AkburaRenderCollectionReconciler.Reconcile(
                target,
                new[] { first, second },
                new[] { second }));

        Assert.Contains("changed outside", exception.Message);
        Assert.Equal(new[] { foreign, second }, target);
    }

    [Fact]
    public void UntypedCollectionPath_PreservesNullOccurrencesAcrossAbortAndRetry()
    {
        var state = new AkburaRenderState();
        var firstPlan = new[]
        {
            Spec<ContainerNode>(-1, RootSlot, "Panel"),
            Spec<LeafNode>(0, ChildrenSlot, "First"),
            Spec<LeafNode>(0, ChildrenSlot, "Second"),
        };

        Begin(state, "revision-1", firstPlan);
        var root = state.GetRequired<ContainerNode>(0);
        var first = state.GetRequired<LeafNode>(1);
        var second = state.GetRequired<LeafNode>(2);
        state.ReconcileCollection(
            0,
            ChildrenSlot,
            (object)root.Children,
            new object[] { first, null!, second });
        state.CompleteRevision();

        Assert.Equal(
            new object?[] { root.ForeignItem, first, null, second },
            root.Children.Cast<object?>());

        var changedPlan = new[]
        {
            Spec<ContainerNode>(-1, RootSlot, "Panel"),
            Spec<LeafNode>(0, ChildrenSlot, "Second"),
        };

        Begin(state, "revision-2", changedPlan);
        var retainedSecond = state.GetRequired<LeafNode>(1);
        Assert.Same(second, retainedSecond);
        state.ReconcileCollection(
            0,
            ChildrenSlot,
            (object)root.Children,
            new object[] { null!, retainedSecond });

        Assert.Equal(
            new object?[] { root.ForeignItem, null, second },
            root.Children.Cast<object?>());

        state.AbortRevision();

        Assert.Equal(
            new object?[] { root.ForeignItem, first, null, second },
            root.Children.Cast<object?>());

        Begin(state, "revision-2", changedPlan);
        retainedSecond = state.GetRequired<LeafNode>(1);
        state.ReconcileCollection(
            0,
            ChildrenSlot,
            (object)root.Children,
            new object[] { null!, retainedSecond });
        state.CompleteRevision();

        Assert.Equal(
            new object?[] { root.ForeignItem, null, second },
            root.Children.Cast<object?>());
    }

    [Fact]
    public void GenericCollectionPath_SupportsGenericOnlyIListAndRollsBack()
    {
        var state = new AkburaRenderState();
        var firstPlan = new[]
        {
            Spec<GenericOnlyContainerNode>(-1, RootSlot, "Panel"),
            Spec<LeafNode>(0, ChildrenSlot, "First"),
            Spec<LeafNode>(0, ChildrenSlot, "Second"),
        };

        Begin(state, "revision-1", firstPlan);
        var root = state.GetRequired<GenericOnlyContainerNode>(0);
        var first = state.GetRequired<LeafNode>(1);
        var second = state.GetRequired<LeafNode>(2);
        state.ReconcileCollection(
            0,
            ChildrenSlot,
            root.Children,
            new[] { first, second });
        state.CompleteRevision();

        Assert.False(
            typeof(IList).IsAssignableFrom(typeof(GenericOnlyList<LeafNode>)));
        Assert.Equal(
            new[] { root.ForeignItem, first, second },
            root.Children);

        var changedPlan = new[]
        {
            Spec<GenericOnlyContainerNode>(-1, RootSlot, "Panel"),
            Spec<LeafNode>(0, ChildrenSlot, "Second"),
            Spec<LeafNode>(0, ChildrenSlot, "Third"),
        };

        Begin(state, "revision-2", changedPlan);
        var retainedSecond = state.GetRequired<LeafNode>(1);
        var third = state.GetRequired<LeafNode>(2);
        state.ReconcileCollection(
            0,
            ChildrenSlot,
            root.Children,
            new[] { retainedSecond, third });

        Assert.Equal(
            new[] { root.ForeignItem, second, third },
            root.Children);

        state.AbortRevision();

        Assert.Equal(
            new[] { root.ForeignItem, first, second },
            root.Children);
    }

    [Fact]
    public void GenericValueCollectionPath_ReconcilesByValueAndRollsBack()
    {
        var state = new AkburaRenderState();
        var plan = new[]
        {
            Spec<GenericOnlyValueContainerNode>(-1, RootSlot, "Values"),
        };

        Begin(state, "revision-1", plan);
        var root = state.GetRequired<GenericOnlyValueContainerNode>(0);
        state.ReconcileCollection(
            0,
            ChildrenSlot,
            root.Values,
            new[] { 1, 2 });
        state.CompleteRevision();

        Assert.False(
            typeof(IList).IsAssignableFrom(typeof(GenericOnlyList<int>)));
        Assert.Equal(new[] { root.ForeignValue, 1, 2 }, root.Values);

        Begin(state, "revision-2", plan);
        state.ReconcileCollection(
            0,
            ChildrenSlot,
            root.Values,
            new[] { 2, 3 });

        Assert.Equal(new[] { root.ForeignValue, 2, 3 }, root.Values);

        state.AbortRevision();

        Assert.Equal(new[] { root.ForeignValue, 1, 2 }, root.Values);

        Begin(state, "revision-3", plan);
        state.ReconcileCollection(
            0,
            ChildrenSlot,
            root.Values,
            new[] { 2, 3 });
        state.CompleteRevision();

        Assert.Equal(new[] { root.ForeignValue, 2, 3 }, root.Values);
    }

    [Fact]
    public void ComponentCollection_SynchronizesLogicalChildrenOnApplyAbortAndOmission()
    {
        const string contentSlot = "parameter:Content";
        var state = new AkburaRenderState();
        var firstPlan = new[]
        {
            Spec<LogicalContentComponent>(-1, RootSlot, "Component"),
            Spec<ContentLeafNode>(0, contentSlot, "First"),
        };

        Begin(state, "revision-1", firstPlan);
        var component = state.GetRequired<LogicalContentComponent>(0);
        var first = state.GetRequired<ContentLeafNode>(1);
        state.ReconcileComponentCollection(
            0,
            contentSlot,
            component,
            component.Content,
            new object[] { first });
        state.CompleteRevision();

        Assert.Equal(new object[] { first }, component.Content.Cast<object>());
        Assert.True(component.HasLogicalContent(first));
        Assert.True(component.IsActualLogicalChild(first));

        var changedPlan = new[]
        {
            Spec<LogicalContentComponent>(-1, RootSlot, "Component"),
            Spec<ContentLeafNode>(0, contentSlot, "First"),
            Spec<ContentLeafNode>(0, contentSlot, "Second"),
        };

        Begin(state, "revision-2", changedPlan);
        var retainedFirst = state.GetRequired<ContentLeafNode>(1);
        var second = state.GetRequired<ContentLeafNode>(2);
        state.ReconcileComponentCollection(
            0,
            contentSlot,
            component,
            component.Content,
            new object[] { retainedFirst, second });

        Assert.True(component.HasLogicalContent(first));
        Assert.True(component.HasLogicalContent(second));
        Assert.True(component.IsActualLogicalChild(second));

        state.AbortRevision();

        Assert.Equal(new object[] { first }, component.Content.Cast<object>());
        Assert.True(component.HasLogicalContent(first));
        Assert.False(component.HasLogicalContent(second));
        Assert.True(component.IsActualLogicalChild(first));
        Assert.False(component.IsActualLogicalChild(second));

        Begin(
            state,
            "revision-3",
            new[]
            {
                Spec<LogicalContentComponent>(-1, RootSlot, "Component"),
            });
        state.CompleteRevision();

        Assert.Empty(component.Content);
        Assert.False(component.HasLogicalContent(first));
        Assert.False(component.IsActualLogicalChild(first));
        Assert.True(component.SynchronizeCount >= 3);
    }

    [Fact]
    public void GenericOnlyComponentCollection_SynchronizesLogicalChildrenAndRollsBack()
    {
        const string contentSlot = "parameter:GenericContent";
        var state = new AkburaRenderState();
        var firstPlan = new[]
        {
            Spec<LogicalContentComponent>(-1, RootSlot, "Component"),
            Spec<ContentLeafNode>(0, contentSlot, "First"),
        };

        Begin(state, "revision-1", firstPlan);
        var component = state.GetRequired<LogicalContentComponent>(0);
        var first = state.GetRequired<ContentLeafNode>(1);
        state.ReconcileComponentCollection(
            0,
            contentSlot,
            component,
            component.GenericContent,
            new Control[] { first });
        state.CompleteRevision();

        Assert.False(
            typeof(IList).IsAssignableFrom(
                typeof(GenericOnlyList<Control>)));
        Assert.Equal(new Control[] { first }, component.GenericContent);
        Assert.True(component.HasGenericLogicalContent(first));
        Assert.True(component.IsActualLogicalChild(first));

        var changedPlan = new[]
        {
            Spec<LogicalContentComponent>(-1, RootSlot, "Component"),
            Spec<ContentLeafNode>(0, contentSlot, "First"),
            Spec<ContentLeafNode>(0, contentSlot, "Second"),
        };

        Begin(state, "revision-2", changedPlan);
        var retainedFirst = state.GetRequired<ContentLeafNode>(1);
        var second = state.GetRequired<ContentLeafNode>(2);
        state.ReconcileComponentCollection(
            0,
            contentSlot,
            component,
            component.GenericContent,
            new Control[] { retainedFirst, second });

        Assert.Equal(
            new Control[] { first, second },
            component.GenericContent);
        Assert.True(component.HasGenericLogicalContent(first));
        Assert.True(component.HasGenericLogicalContent(second));
        Assert.True(component.IsActualLogicalChild(second));

        state.AbortRevision();

        Assert.Equal(new Control[] { first }, component.GenericContent);
        Assert.True(component.HasGenericLogicalContent(first));
        Assert.False(component.HasGenericLogicalContent(second));
        Assert.True(component.IsActualLogicalChild(first));
        Assert.False(component.IsActualLogicalChild(second));
    }

    [Fact]
    public void ClrValue_DeclaringTypeSelectsHiddenBasePropertyExactly()
    {
        var state = new AkburaRenderState();
        var plan = new[]
        {
            Spec<HiddenPropertyDerivedNode>(-1, RootSlot, "Root"),
        };

        Begin(state, "revision-1", plan);
        var root = state.GetRequired<HiddenPropertyDerivedNode>(0);
        ((HiddenPropertyBaseNode)root).Value = "base baseline";
        root.Value = "derived value";
        state.ReconcileClrValue(
            0,
            "Base.Value",
            root,
            typeof(HiddenPropertyBaseNode),
            nameof(HiddenPropertyBaseNode.Value),
            "Base.Value=generated",
            "generated value");
        state.CompleteRevision();

        Assert.Equal("generated value", ((HiddenPropertyBaseNode)root).Value);
        Assert.Equal("derived value", root.Value);

        Begin(state, "revision-2", plan);
        state.CompleteRevision();

        Assert.Equal("base baseline", ((HiddenPropertyBaseNode)root).Value);
        Assert.Equal("derived value", root.Value);
    }

    private static void Begin(
        AkburaRenderState state,
        string revision,
        IReadOnlyList<NodeSpec> plan)
    {
        Assert.True(
            state.BeginRevision(
                revision,
                builder =>
                {
                    for (var localId = 0;
                        localId < plan.Count;
                        localId++)
                    {
                        var spec = plan[localId];
                        builder.Add(
                            new AkburaRenderNodeDefinition(
                                localId,
                                spec.ParentId,
                                spec.Slot,
                                spec.Type,
                                spec.ExplicitKey,
                                spec.SyntaxIdentity));
                    }
                },
                localId => Activator.CreateInstance(plan[localId].Type)!));
    }

    private static AkburaRenderNodeDefinition Definition<T>(
        int localId,
        int parentId,
        string slot,
        string syntaxIdentity,
        string? explicitKey = null)
        where T : class
    {
        return new AkburaRenderNodeDefinition(
            localId,
            parentId,
            slot,
            typeof(T),
            explicitKey,
            syntaxIdentity);
    }

    private static NodeSpec Spec<T>(
        int parentId,
        string slot,
        string syntaxIdentity,
        string? explicitKey = null)
        where T : class
    {
        return new NodeSpec(
            parentId,
            slot,
            typeof(T),
            explicitKey,
            syntaxIdentity);
    }

    private readonly record struct NodeSpec(
        int ParentId,
        string Slot,
        Type Type,
        string? ExplicitKey,
        string SyntaxIdentity);

    private class InitializableNode : ISupportInitialize
    {
        public int BeginInitCount { get; private set; }

        public int EndInitCount { get; private set; }

        public void BeginInit()
        {
            BeginInitCount++;
        }

        public void EndInit()
        {
            EndInitCount++;
        }
    }

    private sealed class ContainerNode : InitializableNode
    {
        public ContainerNode()
        {
            Children.Add(ForeignItem);
        }

        public object ForeignItem { get; } = new();

        public ArrayList Children { get; } = [];
    }

    private sealed class GenericOnlyContainerNode : InitializableNode
    {
        public GenericOnlyContainerNode()
        {
            Children.Add(ForeignItem);
        }

        public LeafNode ForeignItem { get; } = new();

        public GenericOnlyList<LeafNode> Children { get; } = new();
    }

    private sealed class GenericOnlyValueContainerNode : InitializableNode
    {
        public GenericOnlyValueContainerNode()
        {
            Values.Add(ForeignValue);
        }

        public int ForeignValue { get; } = 42;

        public GenericOnlyList<int> Values { get; } = new();
    }

    private sealed class GenericOnlyList<T> : IList<T>
    {
        private readonly List<T> _items = [];

        public T this[int index]
        {
            get => _items[index];
            set => _items[index] = value;
        }

        public int Count => _items.Count;

        public bool IsReadOnly => false;

        public void Add(T item) => _items.Add(item);

        public void Clear() => _items.Clear();

        public bool Contains(T item) => _items.Contains(item);

        public void CopyTo(T[] array, int arrayIndex) =>
            _items.CopyTo(array, arrayIndex);

        public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();

        public int IndexOf(T item) => _items.IndexOf(item);

        public void Insert(int index, T item) => _items.Insert(index, item);

        public bool Remove(T item) => _items.Remove(item);

        public void RemoveAt(int index) => _items.RemoveAt(index);

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class AvaloniaContainerNode : InitializableNode
    {
        public AvaloniaContainerNode()
        {
            Children.Add(ForeignItem);
        }

        public object ForeignItem { get; } = new();

        public AvaloniaList<object> Children { get; } = [];
    }

    private sealed class LogicalContentComponent : AkburaControl
    {
        private static readonly ImmutableArray<Parameter> s_parameters = [];
        private static readonly ImmutableArray<AvaloniaProperty<IAkburaCommand>>
            s_commands = [];
        private static readonly ImmutableArray<InjectService> s_services = [];
        private static readonly ImmutableArray<State> s_states = [];

        private readonly Border _root = new();
        private readonly List<Control> _logicalContent = [];
        private readonly List<Control> _genericLogicalContent = [];

        public LogicalContentComponent()
            : base(AkburaEngine.Empty)
        {
        }

        public ArrayList Content { get; } = [];

        public GenericOnlyList<Control> GenericContent { get; } = new();

        public int SynchronizeCount { get; private set; }

        public bool HasLogicalContent(Control control)
        {
            return _logicalContent.Contains(control);
        }

        public bool HasGenericLogicalContent(Control control)
        {
            return _genericLogicalContent.Contains(control);
        }

        public bool IsActualLogicalChild(Control control)
        {
            return LogicalChildren.Contains(control);
        }

        private void __SynchronizeContentLogicalChildren_fixture()
        {
            SynchronizeCount++;

            foreach (var oldContent in _logicalContent)
            {
                LogicalChildren.Remove(oldContent);
            }

            _logicalContent.Clear();
            foreach (var item in Content)
            {
                if (item is Control contentControl &&
                    !_logicalContent.Contains(contentControl))
                {
                    LogicalChildren.Add(contentControl);
                    _logicalContent.Add(contentControl);
                }
            }
        }

        private void __SynchronizeContentLogicalChildren_genericFixture()
        {
            SynchronizeCount++;

            foreach (var oldContent in _genericLogicalContent)
            {
                LogicalChildren.Remove(oldContent);
            }

            _genericLogicalContent.Clear();
            foreach (var item in GenericContent)
            {
                if (!_genericLogicalContent.Contains(item))
                {
                    LogicalChildren.Add(item);
                    _genericLogicalContent.Add(item);
                }
            }
        }

        protected override Control FirstUpdate()
        {
            return _root;
        }

        protected override Control Update()
        {
            return _root;
        }

        protected override ImmutableArray<Parameter> GetParameters()
        {
            return s_parameters;
        }

        protected override ImmutableArray<AvaloniaProperty<IAkburaCommand>>
            GetCommands()
        {
            return s_commands;
        }

        protected override ImmutableArray<InjectService> GetServices()
        {
            return s_services;
        }

        protected override ImmutableArray<State> GetStates()
        {
            return s_states;
        }
    }

    private sealed class ContentLeafNode : Control
    {
    }

    private class HiddenPropertyBaseNode
    {
        public string? Value { get; set; }
    }

    private sealed class HiddenPropertyDerivedNode : HiddenPropertyBaseNode
    {
        public new string? Value { get; set; }
    }

    private sealed class ScalarContainerNode : AvaloniaObject
    {
        public static readonly StyledProperty<object?> AvaloniaChildProperty =
            AvaloniaProperty.Register<ScalarContainerNode, object?>(
                nameof(AvaloniaChild));

        public static readonly StyledProperty<object?> AvaloniaValueProperty =
            AvaloniaProperty.Register<ScalarContainerNode, object?>(
                nameof(AvaloniaValue));

        public object? ClrChild { get; set; }

        public int ClrNumber { get; set; }

        public object? AvaloniaChild
        {
            get => GetValue(AvaloniaChildProperty);
            set => SetValue(AvaloniaChildProperty, value);
        }

        public object? AvaloniaValue
        {
            get => GetValue(AvaloniaValueProperty);
            set => SetValue(AvaloniaValueProperty, value);
        }
    }

    private sealed class LeafNode : InitializableNode
    {
        public string? Value { get; set; }
    }

    private sealed class EventNode : InitializableNode
    {
        public event EventHandler? Changed;

        public void RaiseChanged()
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class ThrowingEventNode : InitializableNode
    {
        private EventHandler? _changed;

        public bool ThrowAfterAdd { get; set; }

        public bool ThrowAfterRemove { get; set; }

        public int AddAttemptCount { get; private set; }

        public int RemoveAttemptCount { get; private set; }

        public event EventHandler? Changed
        {
            add
            {
                AddAttemptCount++;
                _changed += value;
                if (ThrowAfterAdd)
                {
                    throw new InvalidOperationException(
                        "Event add failed.");
                }
            }
            remove
            {
                RemoveAttemptCount++;
                _changed -= value;
                if (ThrowAfterRemove)
                {
                    ThrowAfterRemove = false;
                    throw new InvalidOperationException(
                        "Event remove failed.");
                }
            }
        }

        public void RaiseChanged()
        {
            _changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private class HiddenEventBaseNode : InitializableNode
    {
        public event EventHandler? Changed;

        public void RaiseBaseChanged()
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class HiddenEventDerivedNode : HiddenEventBaseNode
    {
        public new event EventHandler? Changed;

        public void RaiseChanged()
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class BindingNode : AvaloniaObject
    {
        public static readonly StyledProperty<string?> ValueProperty =
            AvaloniaProperty.Register<BindingNode, string?>(nameof(Value));

        public string? Value
        {
            get => GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }
    }

    private sealed class BindingSource : INotifyPropertyChanged
    {
        private string _value;

        public BindingSource(string value)
        {
            _value = value;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Value
        {
            get => _value;
            set
            {
                _value = value;
                PropertyChanged?.Invoke(
                    this,
                    new PropertyChangedEventArgs(nameof(Value)));
            }
        }
    }

    private sealed class ThrowingEndInitNode : ISupportInitialize
    {
        public void BeginInit()
        {
        }

        public void EndInit()
        {
            throw new InvalidOperationException("EndInit failed.");
        }
    }

    private sealed class ThrowingBeginInitNode : ISupportInitialize
    {
        public int BeginInitCount { get; private set; }

        public int EndInitCount { get; private set; }

        public bool IsInitializing { get; private set; }

        public void BeginInit()
        {
            BeginInitCount++;
            IsInitializing = true;
            throw new InvalidOperationException("BeginInit failed.");
        }

        public void EndInit()
        {
            EndInitCount++;
            IsInitializing = false;
        }
    }

    private sealed class OrderedNode : ISupportInitialize
    {
        private readonly int _id;
        private readonly List<int> _endOrder;

        public OrderedNode(int id, List<int> endOrder)
        {
            _id = id;
            _endOrder = endOrder;
        }

        public void BeginInit()
        {
        }

        public void EndInit()
        {
            _endOrder.Add(_id);
        }
    }

    private sealed class PlainAkcssNode
    {
    }

    private sealed class TrackingAkcssActivator : AkcssStyleActivator
    {
        private readonly TrackingObservable _observable = new();

        public TrackingAkcssActivator(string name)
            : base(new TrackingAkcssStyle(name))
        {
        }

        public int SubscriptionCount => _observable.SubscriptionCount;

        public override void Execute(object target)
        {
        }

        public override void Reset(object target)
        {
        }

        public override IObservable<object?> Watch(object target)
        {
            return _observable;
        }
    }

    private sealed class TrackingAkcssStyle : AkcssStyle
    {
        public TrackingAkcssStyle(string name)
        {
            NameCore = name;
        }
    }

    private sealed class TrackingObservable : IObservable<object?>
    {
        public int SubscriptionCount { get; private set; }

        public IDisposable Subscribe(IObserver<object?> observer)
        {
            SubscriptionCount++;
            return new TrackingSubscription(this);
        }

        private sealed class TrackingSubscription : IDisposable
        {
            private TrackingObservable? _owner;

            public TrackingSubscription(TrackingObservable owner)
            {
                _owner = owner;
            }

            public void Dispose()
            {
                var owner = _owner;
                if (owner == null)
                {
                    return;
                }

                _owner = null;
                owner.SubscriptionCount--;
            }
        }
    }

    private sealed class OtherLeafNode : InitializableNode
    {
    }
}
