using System.Collections;

namespace Akbura.UnitTests;

public sealed class EmptyEnumeratorTests
{
    [Fact]
    public void CachedEmptyEnumerator_ImplementsGenericAndNonGenericEnumerationAndDisposal()
    {
        using var enumerator = EmptyEnumerator.For<int>();
        Assert.False(enumerator.MoveNext());
        Assert.Same(enumerator, EmptyEnumerator.For<int>());
        ((IEnumerator)enumerator).Reset();
        Assert.False(enumerator.MoveNext());
        enumerator.Dispose();
        Assert.False(enumerator.MoveNext());
    }
}
