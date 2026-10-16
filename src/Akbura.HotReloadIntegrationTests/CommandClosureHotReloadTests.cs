using Avalonia.Controls;
using Avalonia.Headless;

namespace Akbura.HotReloadIntegrationTests;

public sealed class CommandClosureHotReloadTests
{
    [Fact]
    [Trait("Category", "ClrHotReload")]
    public Task ComponentCommand_SourceRevisionReconcilesOnceAndOrdinaryFramesRefreshCaptures() =>
        Run(nameof(ComponentCommand_SourceRevisionReconcilesOnceAndOrdinaryFramesRefreshCaptures), false, true);

    [Fact]
    [Trait("Category", "ClrHotReload")]
    public Task SinglePassCommand_InitialAndSourceFramesReconcileOnce() =>
        Run(nameof(SinglePassCommand_InitialAndSourceFramesReconcileOnce), true, true);

    [Fact]
    [Trait("Category", "ClrHotReload")]
    public Task ReleaseConditionalCommand_InitialAndSourceFramesReconcileOnce() =>
        Run(nameof(ReleaseConditionalCommand_InitialAndSourceFramesReconcileOnce), true, false);

    private static async Task Run(string method, bool conditional, bool debug)
    {
        var testName = typeof(CommandClosureHotReloadTests).FullName + "." + method;
        if (!HotReloadChildProcess.IsChild(testName))
        {
            await HotReloadChildProcess.RunAsync(testName);
            return;
        }

        var compilation = new AkburaCompilationSession(Source(0, conditional), OwnerSource,
            new Dictionary<string, string> { ["Brand.akbura"] = "using Avalonia.Controls; namespace Demo; command void ToggleSidebar(); <Button Command={ToggleSidebar} />" },
            debug: debug);
        using var reload = new ClrHotReloadSession(compilation.Compilation, "Demo.Counter");
        await HeadlessUnitTestSession.GetOrStartForAssembly(typeof(CommandClosureHotReloadTests).Assembly).Dispatch(() =>
        {
            var owner = Assert.IsAssignableFrom<AkburaControl>(Activator.CreateInstance(reload.ComponentType));
            var window = new Window { Content = owner };
            try
            {
                window.Show();
                var root = Assert.IsType<StackPanel>(owner.Child);
                var brand = Assert.IsAssignableFrom<AkburaControl>(root.Children[0]);
                var title = Assert.IsType<TextBlock>(root.Children[1]);
                ExecuteAndAssert(owner, brand, 10);
                for (var revision = 1; revision <= 3; revision++)
                {
                    reload.ComponentType.GetProperty("Input")!.SetValue(owner, 20 + revision);
                    owner.InvalidState();
                    ExecuteAndAssert(owner, brand, 20 + revision + revision - 1);
                    reload.ComponentType.GetProperty("Input")!.SetValue(owner, 100 + revision);
                    reload.ApplyUpdate(compilation.Update(Source(revision, conditional)));
                    if (debug)
                    {
                        reload.DispatchMetadataUpdateHandlers();
                    }
                    else
                    {
                        owner.InvalidState();
                    }
                    Assert.Same(owner, window.Content);
                    Assert.Same(root, owner.Child);
                    Assert.Same(brand, root.Children[0]);
                    Assert.Same(title, root.Children[1]);
                    Assert.Equal("dashboardTitle", title.Name);
                    Assert.Equal("Dashboard " + revision, title.Text);
                    ExecuteAndAssert(owner, brand, 100 + revision + revision);
                }
                Assert.Equal(3, reload.AppliedUpdates);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    private static void ExecuteAndAssert(AkburaControl owner, AkburaControl brand, int expected)
    {
        var command = Assert.IsAssignableFrom<IAkburaCommand>(brand.GetType().GetProperty("ToggleSidebar")!.GetValue(brand));
        command.Execute().GetAwaiter().GetResult();
        Assert.Equal(expected, owner.GetType().GetProperty("Observed")!.GetValue(owner));
    }

    private static string Source(int revision, bool conditional) =>
        "using Avalonia.Controls; using Demo; namespace Demo; var current = Input; " +
        "void ToggleSidebar() { Record(current + " + revision + "); } <StackPanel>" +
        "<Brand x.Name=\"brand\" ToggleSidebar={ToggleSidebar} /><TextBlock x.Name=\"dashboardTitle\" Text=\"Dashboard " + revision + "\" />" +
        (conditional ? "$if (Enabled) { <Border /> }" : "") + "</StackPanel>";

    private const string OwnerSource =
        """
        using Akbura;
        using Akbura.Engine;
        namespace Demo;
        public partial class Counter : AkburaControl
        {
            public Counter() : base(AkburaEngine.Empty) { }
            public int Input { get; set; } = 10;
            public int Observed { get; private set; }
            public bool Enabled => true;
            public void Record(int value) { Observed = value; }
        }
        public partial class Brand : AkburaControl
        {
            public Brand() : base(AkburaEngine.Empty) { }
        }
        """;
}
