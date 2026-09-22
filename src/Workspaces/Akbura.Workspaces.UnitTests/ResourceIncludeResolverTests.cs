using Akbura.Workspaces.Resources;
using Microsoft.CodeAnalysis;

namespace Akbura.Workspaces.UnitTests;

public sealed class ResourceIncludeResolverTests
{
    [Theory]
    [InlineData("Colors.axaml", "Themes/Colors.axaml")]
    [InlineData("../Shared/Colors.axaml", "Shared/Colors.axaml")]
    [InlineData("/Resources/Colors.axaml", "Resources/Colors.axaml")]
    [InlineData("Resources\\Colors.axaml", "Themes/Resources/Colors.axaml")]
    [InlineData("Icons%20And%20Colors.axaml", "Themes/Icons And Colors.axaml")]
    public void LocalSourcesResolveAgainstTheContainingDictionary(string source, string expectedPath)
    {
        var application = Identity("MyApp");
        var containing = Dictionary(application, "Themes/Main.axaml");

        Assert.True(ResourceIncludeResolver.TryResolve(
            source,
            containing,
            Array.Empty<ResourceAssemblyIdentity>(),
            out var resolved));
        Assert.Equal(application, resolved.Assembly);
        Assert.Equal(expectedPath, resolved.Path.Value);
    }

    [Fact]
    public void AvaresSourceUsesTheExactReferencedAssemblyAndRootPath()
    {
        var application = Identity("MyApp");
        var firstTheme = Identity("Acme.Theme", new Version(1, 0, 0, 0));
        var containing = Dictionary(application, "App.axaml");

        Assert.True(ResourceIncludeResolver.TryResolve(
            "avares://Acme.Theme/Themes/Styles.axaml",
            containing,
            new[] { firstTheme },
            out var resolved));
        Assert.Equal(firstTheme, resolved.Assembly);
        Assert.Equal("Themes/Styles.axaml", resolved.Path.Value);
    }

    [Fact]
    public void AmbiguousSimpleAssemblyNameIsRejected()
    {
        var containing = Dictionary(Identity("MyApp"), "App.axaml");
        var candidates = new[]
        {
            Identity("Acme.Theme", new Version(1, 0, 0, 0)),
            Identity("Acme.Theme", new Version(2, 0, 0, 0)),
        };

        Assert.False(ResourceIncludeResolver.TryResolve(
            "avares://Acme.Theme/Styles.axaml",
            containing,
            candidates,
            out _));
    }

    [Theory]
    [InlineData("../../Outside.axaml")]
    [InlineData("avares://Missing.Theme/Styles.axaml")]
    [InlineData("https://example.com/Styles.axaml")]
    [InlineData("file:///C:/Styles.axaml")]
    [InlineData("C:\\Styles.axaml")]
    [InlineData("//server/share/Styles.axaml")]
    [InlineData("Styles.axaml?theme=dark")]
    [InlineData("Styles.axaml#fragment")]
    [InlineData("Themes%2FStyles.axaml")]
    [InlineData("Themes%5CStyles.axaml")]
    [InlineData("Themes%252FStyles.axaml")]
    [InlineData("Themes%Styles.axaml")]
    [InlineData("Themes%2Styles.axaml")]
    [InlineData("Themes%2GStyles.axaml")]
    [InlineData(" Styles.axaml")]
    public void UnsupportedOrAmbiguousSourcesAreRejected(string source)
    {
        var containing = Dictionary(Identity("MyApp"), "Themes/Main.axaml");

        Assert.False(ResourceIncludeResolver.TryResolve(
            source,
            containing,
            Array.Empty<ResourceAssemblyIdentity>(),
            out _));
    }

    private static ResourceAssemblyIdentity Identity(string name, Version? version = null)
    {
        return ResourceAssemblyIdentity.Create(
            new AssemblyIdentity(
                name,
                version: version ?? new Version(1, 0, 0, 0)));
    }

    private static ResourceDictionaryIdentity Dictionary(ResourceAssemblyIdentity assembly, string path)
    {
        Assert.True(ResourcePath.TryCreateExportPath(
            path,
            out var resourcePath));
        return new ResourceDictionaryIdentity(
            assembly,
            resourcePath);
    }
}
