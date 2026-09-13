using Avalonia;
using Avalonia.Threading;

namespace Akbura.UnitTests;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class AvaloniaHeadlessTestSessionTests
{
    [Fact]
    public async Task DispatchesUseOneUiQueueWithSeparateApplications()
    {
        var session = AvaloniaHeadlessTestSession.GetSession();
        var firstDispatch = session.Dispatch(
            CaptureApplication,
            CancellationToken.None);
        var secondDispatch = session.Dispatch(
            CaptureApplication,
            CancellationToken.None);

        var first = await firstDispatch;
        var second = await secondDispatch;

        Assert.NotSame(first.Application, second.Application);
        Assert.Equal(first.ThreadId, second.ThreadId);
    }

    private static (Application Application, int ThreadId) CaptureApplication()
    {
        var application = Application.Current;
        Assert.NotNull(application);
        Assert.True(Dispatcher.UIThread.CheckAccess());

        return (application, Environment.CurrentManagedThreadId);
    }
}
