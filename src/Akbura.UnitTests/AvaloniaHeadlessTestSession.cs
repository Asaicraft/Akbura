using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(Akbura.UnitTests.AvaloniaTestAppBuilder))]
[assembly: AvaloniaTestIsolation(AvaloniaTestIsolationLevel.PerTest)]

namespace Akbura.UnitTests;

internal static class AvaloniaHeadlessTestSession
{
    public static HeadlessUnitTestSession GetSession()
    {
        // The assembly owns the dispatcher session for the test process. Each
        // Dispatch still creates and tears down its own isolated application.
        return HeadlessUnitTestSession.GetOrStartForAssembly(
            typeof(AvaloniaHeadlessTestSession).Assembly);
    }
}
