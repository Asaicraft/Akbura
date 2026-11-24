using Akbura.Collections;
using Akbura.AvaloniaEdit.Completion;
using Akbura.AvaloniaEdit.Infrastructure;
using Avalonia.Threading;
using Microsoft.CodeAnalysis.Text;

namespace Akbura.AvaloniaEdit.BrowserSmoke;

internal sealed partial class SmokeView
{
    private static readonly Uri StylesUri = new("memory:///Views/Smoke.akcss");
    private AkburaEditorSession? _stylesheetSession;

    private AkburaEditorSession StylesSession =>
        _stylesheetSession ?? throw new InvalidOperationException("The stylesheet session has not been initialized.");

    private Task RequestActiveCompletionAsync() => stylesEditor.TextArea.IsFocused
        ? stylesEditor.RequestCompletionAsync()
        : editor.RequestCompletionAsync();

    private async Task VerifyAkcssAsync(Action<string> setStage)
    {
        setStage("AKCSS companion configuration");
        await ApplyAndAnalyzeAsync(() => Workspace.OpenOrChangeDocument(StylesUri, SourceText.From(AkcssSource)));
        _stylesheetSession = stylesEditor.Bind(Workspace.DefaultProject, StylesUri,
            new() { Scheduler = new AkburaInlineScheduler() });
        await StylesSession.Ready;

        setStage("built-in AKCSS metadata and import");
        const string imported = "using Avalonia.Controls;\nusing Akbura.Styles.akcss;\n<Button p />";
        await SetMarkupAsync(imported, "Button p");
        await RequestCompletionAsync();
        Require(Session.Completions.Any(static (item, expected) => !item.IsProjected && item.Kind == expected.Kind && item.Text == expected.Text, (Kind: "TailwindUtility", Text: "p-")),
            "The real Akbura.Styles.akcss module did not expose padding utilities.");

        setStage("hyphen input with an open popup");
        var beforeHyphen = imported.Replace("Button p", "Button b");
        await SetMarkupAsync(beforeHyphen, "Button b");
        await RequestCompletionAsync();
        var hyphenPosition = editor.CaretOffset;
        editor.TextArea.PerformTextInput("-");
        Require(editor.Text == beforeHyphen.Insert(hyphenPosition, "-"), "Hyphen input accepted an unrelated completion.");

        setStage("custom utility input and canonical commit");
        const string utilitySource = "using Avalonia.Controls;\n<Button sq />";
        await SetMarkupAsync(utilitySource, "Button sq");
        await RequestCompletionAsync();
        var square = NativeCompletion(Session, "square-(double size)");
        Require(square.InsertText == "square-" && square.Text == "square-", "Utility fields were not preserved.");
        Require(await Session.CommitCompletionAsync(square), "Custom utility commit was rejected.");
        Require(editor.Text.Contains("<Button square- />", StringComparison.Ordinal), "A utility signature was inserted into the source.");
        VerifyUndo(utilitySource, "AKCSS utility");
        foreach (var character in "uare-48")
        {
            var previous = editor.Text;
            var position = editor.CaretOffset;
            editor.TextArea.PerformTextInput(character.ToString());
            Require(editor.Text == previous.Insert(position, character.ToString()), "Utility punctuation changed normal typing.");
        }
        await Session.CompletionIdle;
        square = NativeCompletion(Session, "square-(double size)");
        Require(square.InsertText == "square-48", "The typed utility argument was lost.");
        Require(await Session.CommitCompletionAsync(square), "Parameterized utility commit was rejected.");

        setStage("standalone AKCSS style suggestions and undo");
        var applySource = AkcssSource + "\nButton.secondary {\n    @apply pr;\n}";
        await SetStylesAsync(applySource, "@apply pr");
        stylesEditor.TextArea.PerformTextInput("i");
        var primary = await WaitForNativeCompletionAsync(StylesSession, "primary");
        var beforeStyle = stylesEditor.Text;
        Require(await StylesSession.CommitCompletionAsync(primary), "Style completion was rejected.");
        Require(stylesEditor.Text.Contains("@apply primary;", StringComparison.Ordinal), "The style edit changed its surrounding source.");
        stylesEditor.Undo();
        Require(stylesEditor.Text == beforeStyle, "Style undo did not restore the source.");

        setStage("standalone AKCSS automatic property completion");
        const string declaration = "@using Avalonia.Controls;\nButton.card {\n    Wid;\n}";
        await SetStylesAsync(declaration, "Wid");
        stylesEditor.TextArea.PerformTextInput("t");
        var width = await WaitForNativeCompletionAsync(StylesSession, "Width");
        Require(await StylesSession.CommitCompletionAsync(width), "AKCSS property completion was rejected.");
        Require(stylesEditor.Text.Contains("Width: ;", StringComparison.Ordinal), "The canonical property edit was not applied.");
        await SetStylesAsync(AkcssSource, "Height: size;");

        setStage("inline AKCSS automatic completion");
        const string inline = "@akcss {\n@using Avalonia.Controls;\nButton.card {\n    Wid;\n}\n}\n<Button />";
        await SetMarkupAsync(inline, "Wid");
        editor.TextArea.PerformTextInput("t");
        _ = await WaitForNativeCompletionAsync(Session, "Width");

        setStage("stylesheet update without rebinding");
        const string emptyAttribute = "using Avalonia.Controls;\n<Button  />";
        await SetMarkupAsync(emptyAttribute, "Button ");
        await RequestCompletionAsync();
        var obsolete = NativeCompletion(Session, "square-(double size)");
        var session = editor.Session;
        await ApplyAndAnalyzeAsync(() => Workspace.OpenOrChangeDocument(StylesUri,
            SourceText.From(AkcssSource.Replace("square-", "round-"))));
        Require(editor.Text == emptyAttribute && ReferenceEquals(session, editor.Session), "A stylesheet update rebound or edited the markup.");
        Require(!await Session.CommitCompletionAsync(obsolete), "An obsolete stylesheet completion was accepted.");
        await RequestCompletionAsync();
        _ = NativeCompletion(Session, "round-(double size)");
        Require(!Session.Completions.Any(static (item, text) => item.DisplayText == text, "square-(double size)"), "The old stylesheet utility remained visible.");

        setStage("interactive markup and stylesheet editors");
        await ApplyAndAnalyzeAsync(() => Workspace.OpenOrChangeDocument(StylesUri, SourceText.From(AkcssSource)));
        await SetMarkupAsync(InteractiveSource, "square-48");
        await RequestCompletionAsync();
        EnsureHealthy();
        if (StylesSession.LastServiceError is { } error)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }

    private async Task SetMarkupAsync(string source, string caretAnchor)
    {
        await ApplyAndAnalyzeAsync(() => editor.Text = source);
        editor.CaretOffset = PositionAfter(source, caretAnchor);
        editor.TextArea.Focus();
        await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);
    }

    private async Task SetStylesAsync(string source, string caretAnchor)
    {
        var analysis = NextAnalysis(StylesSession);
        stylesEditor.Text = source;
        await analysis;
        stylesEditor.CaretOffset = PositionAfter(source, caretAnchor);
        stylesEditor.TextArea.Focus();
        await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);
    }

    private static AkburaEditorCompletionItem NativeCompletion(AkburaEditorSession session, string text) =>
        session.Completions.SingleOrDefault(item => !item.IsProjected && item.DisplayText == text)
        ?? throw new InvalidOperationException($"No native completion for '{text}'.");

    private static async Task<AkburaEditorCompletionItem> WaitForNativeCompletionAsync(AkburaEditorSession session, string text)
    {
        await session.CompletionIdle;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (!session.Completions.Any(static (item, name) => !item.IsProjected && item.DisplayText == name, text))
        {
            if (session.LastServiceError is { } error)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
            await Task.Delay(10, timeout.Token);
        }
        return NativeCompletion(session, text);
    }

    private const string AkcssSource = """
        @using Avalonia.Controls;

        @utilities {
            Control.square-(double size) {
                Width: size;
                Height: size;
            }
        }

        Button.primary {
            Padding: 12;
        }
        """;

    private const string InteractiveSource = """
        using Avalonia.Controls;
        using Akbura.Styles.akcss;
        using Demo;

        param ViewModel Vm;

        <StackPanel>
            <TextBlock Text=${x.bind Vm.Profile.Name} />
            <Button square-48 p-3 Content="Edit utilities or AKCSS" />
        </StackPanel>
        """;
}
