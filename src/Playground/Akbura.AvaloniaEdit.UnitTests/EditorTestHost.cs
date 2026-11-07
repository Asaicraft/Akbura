using Akbura.Workspaces;
using Akbura.Workspaces.Projects;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Fluent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Immutable;

[assembly: AvaloniaTestApplication(typeof(Akbura.AvaloniaEdit.UnitTests.EditorTestHost))]
[assembly: AvaloniaTestIsolation(AvaloniaTestIsolationLevel.PerTest)]

namespace Akbura.AvaloniaEdit.UnitTests;

public static class EditorTestHost
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<TestApplication>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions());

    public sealed class TestApplication : Application
    {
        public override void Initialize()
        {
            Styles.Add(new FluentTheme());
            Styles.Add(new StyleInclude(new Uri("avares://Akbura.AvaloniaEdit.UnitTests/"))
                { Source = new Uri("avares://AvaloniaEdit/Themes/Fluent/AvaloniaEdit.xaml") });
            Styles.Add(new StyleInclude(new Uri("avares://Akbura.AvaloniaEdit.UnitTests/"))
                { Source = new Uri("avares://Akbura.AvaloniaEdit/Themes/EditorTheme.axaml") });
        }
    }

    internal static Task Run(Func<Task> action) => HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorTestHost).Assembly)
        .Dispatch(async () => { await action(); return true; }, CancellationToken.None);

    internal static readonly ImmutableArray<MetadataReference> References = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
        .Split(Path.PathSeparator).Select(path => (MetadataReference)MetadataReference.CreateFromFile(path)).ToImmutableArray();

    internal static ProjectContext SemanticContext(ProjectContext original, string extra = "") => new(
        original.RoslynProjectId, "", "", "Demo",
        CSharpCompilation.Create("EditorTests", [CSharpSyntaxTree.ParseText(FrameworkSource + extra)], References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)), []);

    // Types are deliberately supplied by the host context, never by the editor integration.
    private const string FrameworkSource = """
        namespace Avalonia.Controls
        {
            public class Control { public double Width { get; set; } public object Tag { get; set; } }
            public class Button : Control { public string Content { get; set; } }
            public class TextBlock : Control { public string Text { get; set; } }
            public class StackPanel : Control { }
        }
        namespace Akbura { public class AkburaControl : Avalonia.Controls.Control {} }
        """;

    internal static AkburaWorkspace Workspace(bool semantic = false)
    {
        var context = ProjectContext.CreateSyntaxOnly(rootNamespace: "Demo");
        return new AkburaWorkspace(semantic ? SemanticContext(context) : context);
    }
}
