using Akbura.HotReload;
using Avalonia;

namespace Akbura.UnitTests;

public sealed class ConditionalRenderValueTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SameDeclaration_ChangedBranchReplacesValueAndAbortRestoresPreviousValue(bool avalonia)
    {
        // <Owner.Value>$if (a) { <Leaf /> } $else { <Leaf /> }</Owner.Value>
        var fixture = new Fixture(avalonia);
        fixture.Begin("initial");
        fixture.State.SelectConditionalBranch(0, 0);
        var first = fixture.State.GetRequired<Leaf>(1);
        fixture.Reconcile(first);
        fixture.State.CompleteRevision();

        Assert.False(fixture.State.SelectConditionalBranch(0, 0));
        fixture.Reconcile(first);
        Assert.False(fixture.State.HasPendingRevision);
        Assert.Same(first, fixture.Value);

        fixture.State.SelectConditionalBranch(0, 1);
        var abandoned = fixture.State.GetRequired<Leaf>(2);
        fixture.Reconcile(abandoned);
        Assert.Same(abandoned, fixture.Value);
        fixture.State.AbortRevision(new InvalidOperationException("later initialization failed"));

        Assert.Same(first, fixture.Value);
        Assert.Same(first, fixture.State.GetRequired<Leaf>(1));
        Assert.Equal(0, fixture.State.GetConditionalBranch(0));

        fixture.State.SelectConditionalBranch(0, 1);
        var second = fixture.State.GetRequired<Leaf>(2);
        Assert.NotSame(abandoned, second);
        fixture.Reconcile(second);
        fixture.State.CompleteRevision();
        Assert.Same(second, fixture.Value);
        Assert.Equal(1, fixture.State.GetConditionalBranch(0));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmptyActiveContentClearsValue_OnlyRemovingDeclarationRestoresBaseline(bool avalonia)
    {
        // <Owner.Value>$if (a) { <Leaf /> }</Owner.Value>
        var fixture = new Fixture(avalonia);
        var baseline = fixture.Value;
        fixture.Begin("initial");
        fixture.State.SelectConditionalBranch(0, 0);
        fixture.Reconcile(fixture.State.GetRequired<Leaf>(1));
        fixture.State.CompleteRevision();

        fixture.State.SelectConditionalBranch(0, -1);
        fixture.Reconcile(null);
        fixture.State.CompleteRevision();
        Assert.Null(fixture.Value);
        Assert.False(fixture.State.SelectConditionalBranch(0, -1));
        fixture.Reconcile(null);
        Assert.Null(fixture.Value);

        fixture.Begin("declaration-removed", includeConditional: false);
        fixture.State.CompleteRevision();
        Assert.Same(baseline, fixture.Value);
    }

    private sealed class Fixture
    {
        private const string Slot = "property:Value";
        private readonly object _owner;

        public Fixture(bool avalonia)
        {
            _owner = avalonia ? new AvaloniaOwner() : new ClrOwner();
        }

        public AkburaRenderState State { get; } = new();

        public object? Value => _owner is AvaloniaOwner avalonia ? avalonia.Value : ((ClrOwner)_owner).Value;

        public void Begin(string revision, bool includeConditional = true)
        {
            State.BeginRevision(revision, builder =>
            {
                builder.Add(new AkburaRenderNodeDefinition(0, -1, "$root", _owner.GetType(), null, "owner"));
                if (includeConditional)
                {
                    builder.Add(new AkburaRenderNodeDefinition(1, 0, Slot, typeof(Leaf), null, "first", 0, 0));
                    builder.Add(new AkburaRenderNodeDefinition(2, 0, Slot, typeof(Leaf), null, "second", 0, 1));
                    builder.AddConditional(new AkburaRenderConditionalDefinition(0, 0, Slot, "same-declaration",
                        [new("a", "first"), new("", "second")]));
                }
            }, id => id == 0 ? _owner : new Leaf());
        }

        public void Reconcile(object? value)
        {
            if (_owner is AvaloniaOwner avalonia)
            {
                State.ReconcileConditionalAvaloniaValue(0, Slot, avalonia, AvaloniaOwner.ValueProperty,
                    "same-declaration", value);
            }
            else
            {
                State.ReconcileConditionalClrValue(0, Slot, _owner, typeof(ClrOwner), nameof(ClrOwner.Value),
                    "same-declaration", value);
            }
        }
    }

    private sealed class Leaf
    {
    }

    private sealed class ClrOwner
    {
        public object? Value { get; set; } = new object();
    }

    private sealed class AvaloniaOwner : AvaloniaObject
    {
        public static readonly StyledProperty<object?> ValueProperty =
            AvaloniaProperty.Register<AvaloniaOwner, object?>(nameof(Value));

        public AvaloniaOwner() => Value = new object();

        public object? Value
        {
            get => GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }
    }
}
