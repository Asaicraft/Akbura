namespace Akbura.LanguageServer.UnitTests;

public sealed class ProtocolMapperTests
{
    [Fact]
    public void ParseUriNormalizesEncodedWindowsDriveSeparator()
    {
        if (Path.DirectorySeparatorChar != '\\')
        {
            return;
        }

        var uri = AkburaProtocolMapper.ParseUri(
            "file:///c%3A/Users/Asanov/source/repos/Akbura");

        Assert.True(uri.IsFile);
        Assert.Equal(
            @"c:\Users\Asanov\source\repos\Akbura",
            uri.LocalPath,
            ignoreCase: true);
    }

    [Fact]
    public void ParseUriPreservesCanonicalWindowsFileUri()
    {
        if (Path.DirectorySeparatorChar != '\\')
        {
            return;
        }

        var uri = AkburaProtocolMapper.ParseUri(
            "file:///C:/Users/Asanov/source/repos/Akbura");

        Assert.Equal(
            @"C:\Users\Asanov\source\repos\Akbura",
            uri.LocalPath,
            ignoreCase: true);
    }

    [Fact]
    public void ParseUriPreservesNonFileUri()
    {
        var uri = AkburaProtocolMapper.ParseUri(
            "untitled:Untitled-1");

        Assert.Equal("untitled", uri.Scheme);
        Assert.Equal("untitled:Untitled-1", uri.AbsoluteUri);
    }
}
