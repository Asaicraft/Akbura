using Akbura.HotReload;
using System.Collections;
using System.Runtime.CompilerServices;

namespace Akbura.UnitTests;

public sealed class ConditionalLocalScopeLifetimeTests
{
    private const string ChildrenSlot = "Owner.Children";

    [Fact]
    public void ReplacedTemplate_RetiresOnceAfterCommitButNotDuringPreparation()
    {
        using var fixture = Fixture.ApplyA();
        var lease = new Lease();
        fixture.Begin("changed-source");
        fixture.State.DeferLocalRenderScopeDisposal(lease, static () => true);
        fixture.State.DeferLocalRenderScopeDisposal(lease, static () => true);
        fixture.Reconcile(1, 3);
        fixture.State.PrepareRevisionCompletion();
        Assert.Equal(0, lease.DisposeCount);

        fixture.State.CompleteRevision();

        Assert.Equal(1, lease.DisposeCount);
    }

    [Fact]
    public void ReplacedTemplate_SourceAbortKeepsOriginalLeaseUsable()
    {
        using var fixture = Fixture.ApplyA();
        var lease = new Lease();
        fixture.Begin("abandoned-source");
        fixture.State.DeferLocalRenderScopeDisposal(lease, static () => true);
        fixture.Reconcile(1, 3);

        fixture.State.AbortRevision();
        lease.Use();
        fixture.Begin("next-source");
        fixture.Reconcile(1, 3);
        fixture.State.CompleteRevision();

        Assert.Equal(0, lease.DisposeCount);
        Assert.Equal(1, lease.UseCount);
    }

    [Fact]
    public void ReattachedTemplate_CancelsRetirementEvenWhenParentCommits()
    {
        using var fixture = Fixture.ApplyA();
        var lease = new Lease();
        var detached = true;
        fixture.Begin("changed-source");
        fixture.State.DeferLocalRenderScopeDisposal(lease, () => detached);
        detached = false;
        fixture.Reconcile(1, 3);

        fixture.State.CompleteRevision();

        Assert.Equal(0, lease.DisposeCount);
        lease.Use();
    }

    [Fact]
    public void BranchExit_ReleasesRemovedOwnerOnlyAfterSuccessfulCommit()
    {
        using var fixture = new Fixture();
        fixture.Begin("initial");
        fixture.State.SelectConditionalBranch(0, 0);
        var firstA = fixture.State.GetRequired<Owner>(1);
        var firstLease = new Lease();
        var independentLease = new Lease();
        var foreignLease = new Lease();
        fixture.State.RegisterLocalRenderScopeLifetime(firstA, firstLease);
        fixture.State.RegisterLocalRenderScopeLifetime(fixture.State.GetRequired<Owner>(3), independentLease);
        fixture.Reconcile(1, 3);
        fixture.State.CompleteRevision();

        fixture.State.SelectConditionalBranch(0, 1);
        var branchB = fixture.State.GetRequired<Owner>(2);
        var secondLease = new Lease();
        fixture.State.RegisterLocalRenderScopeLifetime(branchB, secondLease);
        fixture.Reconcile(2, 3);
        fixture.State.PrepareRevisionCompletion();

        Assert.Equal(0, firstLease.DisposeCount);
        Assert.Equal(0, secondLease.DisposeCount);
        Assert.Equal(0, independentLease.DisposeCount);
        fixture.State.CompleteRevision();
        Assert.Equal(1, firstLease.DisposeCount);
        Assert.Equal(0, secondLease.DisposeCount);
        Assert.Equal(0, independentLease.DisposeCount);
        Assert.Equal(0, foreignLease.DisposeCount);

        fixture.State.SelectConditionalBranch(0, 0);
        var nextA = fixture.State.GetRequired<Owner>(1);
        var nextLease = new Lease();
        Assert.NotSame(firstA, nextA);
        fixture.State.RegisterLocalRenderScopeLifetime(nextA, nextLease);
        fixture.Reconcile(1, 3);
        fixture.State.CompleteRevision();
        Assert.Equal(1, secondLease.DisposeCount);
        Assert.Equal(0, nextLease.DisposeCount);
        fixture.State.Dispose();
        Assert.Equal(1, nextLease.DisposeCount);
        Assert.Equal(1, independentLease.DisposeCount);
        Assert.Equal(0, foreignLease.DisposeCount);
    }

    [Fact]
    public void FailedConstructor_KeepsAppliedOwnerAndItsLocalScopeLease()
    {
        using var fixture = Fixture.ApplyA();
        var owner = fixture.State.GetRequired<Owner>(1);
        var lease = new Lease();
        fixture.State.RegisterLocalRenderScopeLifetime(owner, lease);
        fixture.ThrowOnB = true;

        Assert.Throws<InvalidOperationException>(() => fixture.State.SelectConditionalBranch(0, 1));

        Assert.False(fixture.State.HasPendingRevision);
        Assert.Equal(0, fixture.State.GetConditionalBranch(0));
        Assert.Same(owner, fixture.State.GetRequired<Owner>(1));
        Assert.Equal(0, lease.DisposeCount);
        Assert.Contains(owner, fixture.Root.Children.Cast<object>());
        fixture.State.Dispose();
        Assert.Equal(1, lease.DisposeCount);
    }

    [Fact]
    public void ActivationAbort_DisposesNewRegistrationButRetainsAppliedLease()
    {
        using var fixture = Fixture.ApplyA();
        var original = fixture.State.GetRequired<Owner>(1);
        var originalLease = new Lease();
        fixture.State.RegisterLocalRenderScopeLifetime(original, originalLease);
        fixture.State.SelectConditionalBranch(0, 1);
        var abandoned = fixture.State.GetRequired<Owner>(2);
        var abandonedLease = new Lease();
        fixture.State.RegisterLocalRenderScopeLifetime(abandoned, abandonedLease);
        fixture.Reconcile(2, 3);
        fixture.State.PrepareRevisionCompletion();

        fixture.State.AbortRevision(new InvalidOperationException("later render failure"));

        Assert.Same(original, fixture.State.GetRequired<Owner>(1));
        Assert.Equal(0, originalLease.DisposeCount);
        Assert.Equal(1, abandonedLease.DisposeCount);
        Assert.Contains(original, fixture.Root.Children.Cast<object>());
        Assert.DoesNotContain(abandoned, fixture.Root.Children.Cast<object>());
        fixture.State.Dispose();
        Assert.Equal(1, originalLease.DisposeCount);
        Assert.Equal(1, abandonedLease.DisposeCount);
    }

    [Fact]
    public void SourceAbort_NewLeaseOnRetainedOwnerDoesNotReplaceOrDisposeAppliedLease()
    {
        using var fixture = Fixture.ApplyA();
        var owner = fixture.State.GetRequired<Owner>(1);
        var appliedLease = new Lease();
        fixture.State.RegisterLocalRenderScopeLifetime(owner, appliedLease);
        fixture.Begin("changed-source");
        Assert.Same(owner, fixture.State.GetRequired<Owner>(1));
        var abandonedLease = new Lease();
        fixture.State.RegisterLocalRenderScopeLifetime(owner, abandonedLease);
        fixture.State.RegisterLocalRenderScopeLifetime(owner, appliedLease);
        fixture.Reconcile(1, 3);

        fixture.State.AbortRevision();

        Assert.Equal(0, appliedLease.DisposeCount);
        Assert.Equal(1, abandonedLease.DisposeCount);
        fixture.State.Dispose();
        Assert.Equal(1, appliedLease.DisposeCount);
        Assert.Equal(1, abandonedLease.DisposeCount);
    }

    [Fact]
    public void CompatibleSourceOwnerEdit_PreservesItsLeaseAndInstance()
    {
        using var fixture = Fixture.ApplyA();
        var owner = fixture.State.GetRequired<Owner>(1);
        var lease = new Lease();
        fixture.State.RegisterLocalRenderScopeLifetime(owner, lease);
        var nodeId = fixture.State.GetNodeId(1);
        fixture.Begin("edited-owner", editedA: true);
        fixture.State.SelectConditionalBranch(0, 0);
        fixture.Reconcile(1, 3);
        fixture.State.CompleteRevision();

        Assert.Same(owner, fixture.State.GetRequired<Owner>(1));
        Assert.Equal(nodeId, fixture.State.GetNodeId(1));
        Assert.Equal(0, lease.DisposeCount);
        fixture.State.Dispose();
        Assert.Equal(1, lease.DisposeCount);
    }

    [Fact]
    public void InsertedElseIf_RemapsOwnerOrdinalWithoutReleasingItsLease()
    {
        using var fixture = Fixture.ApplyA();
        var owner = fixture.State.GetRequired<Owner>(1);
        var independent = fixture.State.GetRequired<Owner>(3);
        var ownerLease = new Lease();
        var independentLease = new Lease();
        fixture.State.RegisterLocalRenderScopeLifetime(owner, ownerLease);
        fixture.State.RegisterLocalRenderScopeLifetime(independent, independentLease);
        var nodeId = fixture.State.GetNodeId(1);
        fixture.Begin("inserted-elseif", inserted: true);

        Assert.Equal(1, fixture.State.GetConditionalBranch(0));
        Assert.Same(owner, fixture.State.GetRequired<Owner>(2));
        Assert.Equal(nodeId, fixture.State.GetNodeId(2));
        Assert.False(fixture.State.SelectConditionalBranch(0, 1));
        fixture.Reconcile(2, 4);
        fixture.State.CompleteRevision();

        Assert.Equal(0, ownerLease.DisposeCount);
        Assert.Equal(0, independentLease.DisposeCount);
        Assert.Same(independent, fixture.State.GetRequired<Owner>(4));
        fixture.State.Dispose();
        Assert.Equal(1, ownerLease.DisposeCount);
        Assert.Equal(1, independentLease.DisposeCount);
    }

    [Fact]
    public void RemovedConditionalSource_ReleasesOnlyOmittedOwnersAfterCommit()
    {
        using var fixture = Fixture.ApplyA();
        var omittedLease = new Lease();
        var rootLease = new Lease();
        var independentLease = new Lease();
        fixture.State.RegisterLocalRenderScopeLifetime(fixture.State.GetRequired<Owner>(1), omittedLease);
        fixture.State.RegisterLocalRenderScopeLifetime(fixture.Root, rootLease);
        fixture.State.RegisterLocalRenderScopeLifetime(fixture.State.GetRequired<Owner>(3), independentLease);
        fixture.Begin("removed-chain", removed: true);
        fixture.Reconcile(1);
        fixture.State.PrepareRevisionCompletion();

        Assert.Equal(0, omittedLease.DisposeCount);
        fixture.State.CompleteRevision();
        Assert.Equal(1, omittedLease.DisposeCount);
        Assert.Equal(0, rootLease.DisposeCount);
        Assert.Equal(0, independentLease.DisposeCount);
        Assert.Equal(2, fixture.Root.Children.Count);
        Assert.Same(fixture.Root.Foreign, fixture.Root.Children[0]);
        fixture.State.Dispose();
        Assert.Equal(1, omittedLease.DisposeCount);
        Assert.Equal(1, rootLease.DisposeCount);
        Assert.Equal(1, independentLease.DisposeCount);
    }

    [Fact]
    public void WeakRegistration_DoesNotRootCallerAbandonedLease()
    {
        using var fixture = Fixture.ApplyA();
        var owner = fixture.State.GetRequired<Owner>(1);
        var weak = RegisterNeverHeldLease(fixture.State, owner);
        ForceCollection();

        Assert.False(weak.TryGetTarget(out _));
        Assert.Same(owner, fixture.State.GetRequired<Owner>(1));
    }

    [Fact]
    public void CallerHeldLease_RemainsUsableAndIsReleasedExactlyOnce()
    {
        using var fixture = Fixture.ApplyA();
        var owner = fixture.State.GetRequired<Owner>(1);
        var lease = new Lease();
        fixture.State.RegisterLocalRenderScopeLifetime(owner, lease);
        fixture.State.RegisterLocalRenderScopeLifetime(owner, lease);
        ForceCollection();
        lease.Use();
        Assert.Equal(1, lease.UseCount);
        Assert.Equal(0, lease.DisposeCount);

        fixture.State.SelectConditionalBranch(0, -1);
        fixture.Reconcile(3);
        fixture.State.CompleteRevision();
        Assert.Equal(1, lease.DisposeCount);
        Assert.Throws<ObjectDisposedException>(lease.Use);
        fixture.State.Dispose();
        Assert.Equal(1, lease.DisposeCount);
    }

    [Fact]
    public void ForeignOwnerRegistration_IsRejectedAndItsUnusedLeaseReleased()
    {
        using var fixture = Fixture.ApplyA();
        var lease = new Lease();

        Assert.Throws<ArgumentException>(() =>
            fixture.State.RegisterLocalRenderScopeLifetime(fixture.Root.Foreign, lease));

        Assert.Equal(1, lease.DisposeCount);
        fixture.State.Dispose();
        Assert.Equal(1, lease.DisposeCount);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<Lease> RegisterNeverHeldLease(AkburaRenderState state, object owner)
    {
        var lease = new Lease();
        state.RegisterLocalRenderScopeLifetime(owner, lease);
        return new WeakReference<Lease>(lease);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ForceCollection()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
    }

    private sealed class Lease : IDisposable
    {
        public int DisposeCount { get; private set; }

        public int UseCount { get; private set; }

        public void Use()
        {
            ObjectDisposedException.ThrowIf(DisposeCount != 0, this);
            UseCount++;
        }

        public void Dispose() => DisposeCount++;
    }

    private sealed class Owner(string name)
    {
        public string Name { get; } = name;

        public object Foreign { get; } = new();

        public ArrayList Children { get; } = [];
    }

    private sealed class Fixture : IDisposable
    {
        public AkburaRenderState State { get; } = new();

        public Owner Root { get; } = new("root");

        public bool ThrowOnB { get; set; }

        private string[] _names = [];

        public Fixture() => Root.Children.Add(Root.Foreign);

        public static Fixture ApplyA()
        {
            var fixture = new Fixture();
            fixture.Begin("initial");
            fixture.State.SelectConditionalBranch(0, 0);
            fixture.Reconcile(1, 3);
            fixture.State.CompleteRevision();
            return fixture;
        }

        public void Begin(string revision, bool inserted = false, bool removed = false, bool editedA = false)
        {
            _names = removed ? ["independent"] : inserted ? ["inserted", "a", "b", "independent"] :
                ["a", "b", "independent"];
            State.BeginRevision(revision, builder =>
            {
                builder.Add(new AkburaRenderNodeDefinition(0, -1, "$root", typeof(Owner), null, "root"));
                for (var i = 0; i < _names.Length; i++)
                {
                    var conditional = _names[i] != "independent";
                    builder.Add(new AkburaRenderNodeDefinition(i + 1, 0, ChildrenSlot, typeof(Owner),
                        _names[i], editedA && _names[i] == "a" ? "owner/a edited" : "owner/" + _names[i],
                        conditional ? 0 : -1, conditional ? i : -1));
                }

                if (!removed)
                {
                    var branches = inserted
                        ? new AkburaRenderConditionalBranchDefinition[]
                            { new("inserted", "inserted"), new("a", "a"), new("", "b") }
                        : [new("a", "a"), new("", "b")];
                    builder.AddConditional(new AkburaRenderConditionalDefinition(0, 0, ChildrenSlot,
                        "conditional", branches));
                }
            }, Create);
        }

        public void Reconcile(params int[] localIds) =>
            State.ReconcileCollection(0, ChildrenSlot, Root.Children,
                localIds.Select(State.GetRequired<Owner>).Cast<object>().ToArray());

        private object Create(int localId)
        {
            if (localId == 0)
            {
                return Root;
            }

            if (ThrowOnB && _names[localId - 1] == "b")
            {
                throw new InvalidOperationException("constructor failure");
            }

            return new Owner(_names[localId - 1]);
        }

        public void Dispose() => State.Dispose();
    }
}
