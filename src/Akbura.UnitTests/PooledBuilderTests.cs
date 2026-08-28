using Akbura.Pools;

namespace Akbura.UnitTests;

public sealed class PooledBuilderTests
{
    [Fact]
    public void ImmutableArrayBuilder_ClearAllowsReuse()
    {
        using var builder = ImmutableArrayBuilder<string>.Rent(1);
        builder.Add("old");

        builder.Clear();
        builder.Add("first");
        builder.Add("second");

        Assert.Equal<string>(
            ["first", "second"],
            builder.ToImmutable());
    }

    [Fact]
    public void ArrayBuilder_CountAndPoolingPreserveValues()
    {
        var builder = ArrayBuilder<string>.GetInstance(1);
        builder.Add("first");
        builder.Count = 3;
        builder[1] = "second";
        builder[2] = "third";
        builder.Count = 2;

        Assert.Equal<string>(
            ["first", "second"],
            builder.ToImmutableAndFree());

        var reused = ArrayBuilder<string>.GetInstance();
        Assert.Empty(reused);
        reused.Free();
    }
}
