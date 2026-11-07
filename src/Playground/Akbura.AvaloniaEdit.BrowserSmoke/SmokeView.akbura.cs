using Akbura.AvaloniaEdit.Infrastructure;
using Akbura.Engine;
using Akbura.Workspaces;
using Akbura.Workspaces.Projects;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Reflection;

namespace Akbura.AvaloniaEdit.BrowserSmoke;

internal sealed partial class SmokeView : AkburaControl
{
    private AkburaWorkspace? _workspace;
    private AkburaEditorSession? _session;

    public SmokeView()
        : base(AkburaEngine.Empty)
    {
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        _ = RunSmokeAsync();
    }

    private async Task RunSmokeAsync()
    {
        try
        {
            // Metadata is embedded by the host, read as bytes, and never discovered through Assembly.Location.
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("SmokeReferences.System.Runtime.dll")
                ?? throw new InvalidOperationException("Embedded reference is missing.");
            var reference = MetadataReference.CreateFromStream(stream);
            var context = new ProjectContext(ProjectId.CreateNewId(), "", "", "Demo",
                CSharpCompilation.Create("BrowserSmoke", [CSharpSyntaxTree.ParseText(HostTypes)], [reference],
                    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)), []);
            _workspace = new AkburaWorkspace(context);
            const string source = "using Avalonia.Controls;\nusing Demo;\nparam ViewModel Vm;\n<TextBlock Text=${x.bind Vm.Profile.} />";
            editor.Text = source;
            _session = editor.Bind(_workspace, _workspace.DefaultProjectId, new Uri("memory:///Smoke.akbura"),
                new() { Scheduler = new AkburaInlineScheduler(), CompletionGesture = null });
            _session.ServiceFailed += (_, e) => status.Text = $"FAILED: {e.Operation}: {e.Exception.Message}";
            await _session.Ready;
            if (_session.LastServiceError is { } error) throw error;
            if (_session.Classifications.IsEmpty) throw new InvalidOperationException("No classifications.");
            editor.CaretOffset = source.IndexOf("Vm.Profile.", StringComparison.Ordinal) + "Vm.Profile.".Length;
            await _session.RequestCompletionAsync();
            if (_session.LastServiceError is { } completionError) throw completionError;
            var item = _session.Completions.FirstOrDefault(item => item.IsProjected && item.DisplayText == "Name")
                ?? throw new InvalidOperationException("No projected Name completion.");
            if (!await _session.CommitCompletionAsync(item)) throw new InvalidOperationException("Commit was rejected.");
            editor.Undo();
            if (editor.Text != source) throw new InvalidOperationException("Undo did not restore the source.");
            var hover = await _session.GetHoverAsync(source.IndexOf("x.bind", StringComparison.Ordinal));
            if (hover == null) throw new InvalidOperationException("No hover information.");
            status.Text = "PASS: browser highlighting, diagnostics, projected completion, hover and undo. Single UI thread.";
        }
        catch (Exception error) { status.Text = $"FAILED: {error.GetType().Name}: {error.Message}"; }
    }

    private const string HostTypes = """
        namespace Avalonia.Controls { public class Control {} public class TextBlock : Control { public string Text { get; set; } } }
        namespace Akbura { public class AkburaControl : Avalonia.Controls.Control {} }
        namespace Demo { public class ViewModel { public Profile Profile { get; set; } } public class Profile { public string Name { get; set; } } }
        """;
}
