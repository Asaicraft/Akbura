using Akbura.ComponentTree;

namespace Akbura.UnitTests;

public sealed class StateCollectionTests
{
    [Fact]
    public void GetOrCreate_PreservesTheInstanceAndValueWithoutCallingTheFactoryAgain()
    {
        var states = new StateCollection();
        var calls = 0;
        var first = states.GetOrCreate("count", () =>
        {
            calls++;
            return new State<int>(1);
        });
        first.Value = 42;

        var second = states.GetOrCreate<int>("count", () => throw new Exception("Unexpected factory call."));

        Assert.Same(first, second);
        Assert.Equal(42, second.Value);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void GetOrCreate_UsesOrdinalKeysAndIndependentCollections()
    {
        var firstCollection = new StateCollection();
        var secondCollection = new StateCollection();
        var first = firstCollection.GetOrCreate("count", () => new State<int>(1));
        var otherKey = firstCollection.GetOrCreate("Count", () => new State<int>(2));
        var otherOwner = secondCollection.GetOrCreate("count", () => new State<int>(3));

        Assert.NotSame(first, otherKey);
        Assert.NotSame(first, otherOwner);
        Assert.Equal(2, otherKey.Value);
        Assert.Equal(3, otherOwner.Value);
    }

    [Fact]
    public void GetOrCreate_RejectsAnIncompatibleTypeWithoutReplacingTheState()
    {
        var states = new StateCollection();
        var count = states.GetOrCreate("count", () => new State<int>(1));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            states.GetOrCreate("count", () => new State<string>("new")));

        Assert.Contains("count", exception.Message);
        Assert.Contains(typeof(int).ToString(), exception.Message);
        Assert.Contains(typeof(string).ToString(), exception.Message);
        Assert.Same(count, states.GetOrCreate("count", () => new State<int>(2)));
    }

    [Fact]
    public void GetOrCreate_AllowsRetryAfterAThrowingOrNullFactory()
    {
        var states = new StateCollection();

        Assert.Throws<InvalidOperationException>(() =>
            states.GetOrCreate<int>("count", () => throw new InvalidOperationException("Creation failed.")));
        Assert.Throws<InvalidOperationException>(() => states.GetOrCreate<int>("count", () => null!));

        Assert.Equal(42, states.GetOrCreate("count", () => new State<int>(42)).Value);
    }

    [Fact]
    public void GetOrCreate_RejectsNullArguments()
    {
        var states = new StateCollection();

        Assert.Throws<ArgumentNullException>(() => states.GetOrCreate(null!, () => new State<int>(0)));
        Assert.Throws<ArgumentNullException>(() => states.GetOrCreate<int>("count", null!));
    }
}
