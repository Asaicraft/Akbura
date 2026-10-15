using Akbura.Language.CodeGeneration;

namespace Akbura.UnitTests;

public sealed class AkcssHotReloadIdentityTests
{
    [Fact]
    public void Identity_IgnoresTriviaAndSourceMappings()
    {
        var first = AkcssHotReloadIdentity.Create("void Apply() { target.Width = 12; }");
        var formatted = AkcssHotReloadIdentity.Create("#line 99 \"Styles.akcss\"\nvoid Apply()\n{ /* comment */ target.Width=12; }\n#line default");
        Assert.Equal(first, formatted);
        Assert.Equal(64, first.Length);
        Assert.Equal(first, AkcssHotReloadIdentity.Create("void Apply() { target.Width = 12; }"));
    }

    [Theory]
    [InlineData("Apply(a); Apply(b);", "Apply(b); Apply(a);")]
    [InlineData("Apply(12);", "Apply(13);")]
    [InlineData("Apply(priority: LocalValue);", "Apply(priority: Style);")]
    [InlineData("Apply(variant: Width);", "Apply(variant: Height);")]
    [InlineData("target.Width = value;", "target.Width = value * 2;")]
    public void Identity_TracksOrderedDefinitionsArgumentsVariantsAndPriority(string first, string second)
    {
        Assert.NotEqual(AkcssHotReloadIdentity.Create(first), AkcssHotReloadIdentity.Create(second));
    }
}
