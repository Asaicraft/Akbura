namespace Akbura.LanguageServer.UnitTests;

public sealed class ServerStateTests
{
    [Fact]
    public void PublishesOnlyMonotonicallyNewerSnapshots()
    {
        using var workspace = new AkburaWorkspace();
        var initial = AkburaServerSnapshot.Create(workspace);
        var state = new AkburaServerState(initial);
        var next = initial.Next(initial.Solution);

        state.Publish(next);

        Assert.Same(next, state.Current);
        Assert.Throws<InvalidOperationException>(() =>
            state.Publish(initial));
    }
}