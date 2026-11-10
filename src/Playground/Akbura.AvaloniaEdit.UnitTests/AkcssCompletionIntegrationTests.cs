using Akbura.Collections;
using Akbura.AvaloniaEdit.Completion;
using Akbura.AvaloniaEdit.Infrastructure;
using Akbura.AvaloniaEdit.Highlighting;
using Akbura.Workspaces;
using Akbura.Workspaces.Completion;
using Akbura.Workspaces.Documents;
using Akbura.Workspaces.Projects;
using Avalonia.Controls;
using Avalonia.Headless;
using AvaloniaEdit.Document;
using Microsoft.CodeAnalysis.Text;

namespace Akbura.AvaloniaEdit.UnitTests;

public sealed class AkcssCompletionIntegrationTests
{
    private static readonly Lazy<AkburaReferenceSet> References = new(() => AkburaReferenceSet.FromFiles(
        File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "CompilerReferences.txt"))));

    [Theory]
    [InlineData("Main.akcss", "@|", "u", "@using")]
    [InlineData("Main.akcss", "@using Avalonia.Controls;\nButton.card {\n    @|\n}", "a", "@apply")]
    [InlineData("Main.akcss", "@using Avalonia.Controls;\nButton.card { Wid| }", "t", "Width")]
    [InlineData("Main.akbura", "@akcss {\n@|\n}\n<Button />", "u", "@using")]
    [InlineData("Main.akbura", "@akcss {\n@using Avalonia.Controls;\nButton.card { Wid| }\n}\n<Button />", "t", "Width")]
    [InlineData("Main.akcss", "@using Avalonia.Controls;\n@using Avalonia.Layout;\nButton.card {\n HorizontalAlignment: Cen|;\n}", "t", "Center")]
    public Task AutomaticAkcssCompletionPreservesSharedCandidates(string file, string markedSource, string typed, string expected) =>
        EditorTestHost.Run(async () =>
        {
            using var host = new Host(file, markedSource);
            await host.Session.Ready;
            host.Window.KeyTextInput(typed);
            await host.Session.CompletionIdle;
            Assert.Null(host.Session.LastServiceError);
            var native = host.NativeCompletions();
            Assert.Contains(native.Items, item => item.DisplayText == expected);
            Assert.Contains(host.Session.Completions, item => item.DisplayText == expected);
            Assert.NotNull(host.Session.CompletionPopup);
            host.AssertAdaptation(native);
        });

    [Theory]
    [InlineData("p", "Padding")]
    [InlineData("b", "Background")]
    public Task HyphenWithAnUnrelatedPropertySelectedIsNormalInput(string prefix, string property) => EditorTestHost.Run(async () =>
    {
        using var host = new Host("Main.akbura", $"using Avalonia.Controls;\nusing Akbura.Styles.akcss;\n<Button {prefix}| />");
        await host.Session.Ready;
        await host.Session.RequestCompletionAsync();
        Assert.NotNull(host.Session.CompletionPopup);
        var selected = Assert.Single(host.Session.Completions,
            item => !item.IsProjected && item.DisplayText == property);
        host.Session.CompletionPopup!.CompletionList.SelectedItem = selected;
        Assert.Same(selected, host.Session.CompletionPopup.CompletionList.SelectedItem);
        Assert.Same(host.Workspace.CurrentSolution, selected.Context.Solution);
        var before = host.Editor.Text;
        var position = host.Editor.CaretOffset;
        host.Window.KeyTextInput("-");
        Assert.Equal(before.Insert(position, "-"), host.Editor.Text);
        Assert.Equal(position + 1, host.Editor.CaretOffset);
        Assert.Null(host.Session.LastServiceError);
    });

    [Fact]
    public Task RealBuiltInUtilitiesRequireTheirExplicitModuleImport() => EditorTestHost.Run(async () =>
    {
        using var host = new Host("Main.akbura", "using Avalonia.Controls;\nusing Akbura.Styles.akcss;\n<Button p-| />");
        await host.Session.Ready;
        await host.Session.RequestCompletionAsync();
        var native = host.NativeCompletions();
        Assert.Contains(native.Items, item => item.Kind == AkburaCompletionKind.TailwindUtility && item.FilterText == "p-");
        host.AssertAdaptation(native);
        host.Editor.Text = host.Editor.Text.Replace("using Akbura.Styles.akcss;\n", "");
        host.Editor.CaretOffset = host.Editor.Text.IndexOf("p-", StringComparison.Ordinal) + 2;
        await host.Session.RequestCompletionAsync();
        Assert.DoesNotContain(host.Session.Completions, item => item.Native?.Kind == AkburaCompletionKind.TailwindUtility);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task CompanionUtilityKeepsItsSignatureSpanAndCanonicalInsertion(bool crlf) => EditorTestHost.Run(async () =>
    {
        var source = "// 😀\nusing Avalonia.Controls;\n<Button sq| />";
        if (crlf) source = source.ReplaceLineEndings("\r\n");
        using var host = new Host("Main.akbura", source);
        host.RegisterStyles(Styles);
        await host.Session.Ready;
        await host.Session.RequestCompletionAsync();
        var native = host.NativeCompletions();
        var completion = Assert.Single(host.Session.Completions, item => item.DisplayText == "square-(double size)");
        Assert.Equal("square-", completion.InsertText);
        Assert.Equal("square-", completion.Text);
        Assert.Equal("sq", host.Editor.Text.Substring(completion.SourceSpan.Start, completion.SourceSpan.Length));
        host.AssertAdaptation(native);
        var before = host.Editor.Text;
        Assert.True(await host.Session.CommitCompletionAsync(completion));
        Assert.Equal(before.Replace("sq /", "square- /"), host.Editor.Text);
        Assert.EndsWith("square-", host.Editor.Text[..host.Editor.CaretOffset]);
        host.Editor.Undo();
        Assert.Equal(before, host.Editor.Text);
        host.Editor.Redo();
        Assert.Contains("square-", host.Editor.Text);
    });

    [Fact]
    public Task ExistingColonReproducerMatchesTheCanonicalSharedEdit() => EditorTestHost.Run(async () =>
    {
        using var host = new Host("Main.akcss", "@using Avalonia.Controls;\nButton.card {\n    W|: 12;\n}");
        await host.Session.Ready;
        await host.Session.RequestCompletionAsync();
        var completion = Assert.Single(host.Session.Completions, item => item.DisplayText == "Width");
        Assert.True(host.Workspace.CurrentSolution.TryGetDocumentContext(host.Uri, out var context));
        var syntax = AkburaSyntacticDocument.Parse(context!.Document.Text, context.Document.FilePath);
        var change = host.Workspace.LanguageServices.Completion.GetCompletionChange(
            syntax, context, host.Editor.CaretOffset, completion.Native!);
        var canonical = context.Document.Text.WithChanges(change.Changes).ToString();
        // This reproduces a shared-service limitation, not the desired editing behavior.
        // The adapter must continue to apply canonical edits without another AKCSS parser.
        Assert.Contains("Width: : 12;", canonical);
        var before = host.Editor.Text;
        Assert.True(await host.Session.CommitCompletionAsync(completion));
        Assert.Equal(canonical, host.Editor.Text);
        Assert.Equal(change.NewPosition, host.Editor.CaretOffset);
        host.Editor.Undo();
        Assert.Equal(before, host.Editor.Text);
    });

    [Fact]
    public Task StyleValueRequestsMatchTheExistingSharedService() => EditorTestHost.Run(async () =>
    {
        using var host = new Host("Main.akbura", "using Avalonia.Controls;\n<Button class=\"pri|\" />");
        host.RegisterStyles(Styles);
        await host.Session.Ready;
        await host.Session.RequestCompletionAsync();
        var native = host.NativeCompletions();
        host.AssertAdaptation(native);
        // The current shared service has no class-value style catalog. Keep this
        // reproducer explicit rather than inventing an AvaloniaEdit-only provider.
        Assert.DoesNotContain(native.Items, item => item.DisplayText == "primary");
        Assert.DoesNotContain(host.Session.Completions, item => item.DisplayText == "primary");
    });

    [Theory]
    [InlineData(false, "primary", "AkcssStyle")]
    [InlineData(true, "primary", "AkcssStyle")]
    [InlineData(false, "square-(double size)", "TailwindUtility")]
    public Task ApplyStyleAndUtilityCandidatesReachAutomaticCompletion(bool inline, string expected, string kind) => EditorTestHost.Run(async () =>
    {
        var prefix = expected == "primary" ? "pr" : "sq";
        var source = Styles + $"\nButton.secondary {{\n    @apply {prefix}|;\n}}";
        if (inline) source = "@akcss {\n" + source + "\n}\n<Button />";
        using var host = new Host(inline ? "Main.akbura" : "Main.akcss", source);
        await host.Session.Ready;
        host.Window.KeyTextInput(expected == "primary" ? "i" : "u");
        await host.Session.CompletionIdle;
        var native = host.NativeCompletions();
        Assert.Contains(native.Items, item => item.DisplayText == expected && item.Kind.ToString() == kind);
        host.AssertAdaptation(native);
        var completion = Assert.Single(host.Session.Completions, item => item.DisplayText == expected);
        var before = host.Editor.Text;
        Assert.True(await host.Session.CommitCompletionAsync(completion));
        Assert.Contains(expected == "primary" ? "@apply primary;" : "@apply square-;", host.Editor.Text);
        host.Editor.Undo();
        Assert.Equal(before, host.Editor.Text);
    });

    [Fact]
    public Task ExplicitCompletionStillWorksWhenAutomaticCompletionIsDisabled() => EditorTestHost.Run(async () =>
    {
        using var host = new Host("Main.akcss", "@|", new()
        {
            Scheduler = new AkburaInlineScheduler(), AutomaticCompletion = false, AnalysisDelay = TimeSpan.FromHours(1),
        });
        await host.Session.Ready;
        host.Window.KeyTextInput("u");
        Assert.Null(host.Session.CompletionPopup);
        await host.Session.RequestCompletionAsync();
        Assert.Contains(host.Session.Completions, item => item.DisplayText == "@using");
        Assert.NotNull(host.Session.CompletionPopup);
    });

    [Theory]
    [InlineData("Main.akcss", "// ordinary comment |")]
    [InlineData("Main.akbura", "@akcss {\n// ordinary comment |\n}\n<Button />")]
    [InlineData("Main.akcss", "@using Avalonia.Controls;\nButton.card {\n Content: \"plain |text\";\n}")]
    public Task CommentsAndOrdinaryStringsDoNotOpenCompletion(string file, string source) => EditorTestHost.Run(async () =>
    {
        using var host = new Host(file, source);
        await host.Session.Ready;
        host.Window.KeyTextInput("z");
        await host.Session.CompletionIdle;
        Assert.Null(host.Session.LastServiceError);
        Assert.Empty(host.Session.Completions);
        Assert.Null(host.Session.CompletionPopup);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task EmbeddedCSharpInAkcssStillTriggersProjectedCompletion(bool inline) => EditorTestHost.Run(async () =>
    {
        var source = "@using Avalonia.Controls;\nButton.card {\n Width: System.Math|;\n}";
        if (inline) source = "@akcss {\n" + source + "\n}\n<Button />";
        using var host = new Host(inline ? "Main.akbura" : "Main.akcss", source);
        await host.Session.Ready;
        host.Window.KeyTextInput(".");
        await WaitUntilAsync(() => host.Session.Completions.Any(static (item, text) => item.IsProjected && item.DisplayText == text, "Abs"));
        Assert.NotNull(host.Session.CompletionPopup);
        Assert.Null(host.Session.LastServiceError);
    });

    [Fact]
    public Task ModuleDotsRemainInputAndCanonicalImportDoesNotDuplicateSemicolon() => EditorTestHost.Run(async () =>
    {
#if DEBUG
        var metrics = new Akbura.AvaloniaEdit.Infrastructure.EditorPerformance();
#endif
        using var host = new Host("Main.akcss", "@using Akbura|;", new()
        {
            Scheduler = new AkburaInlineScheduler(), AnalysisDelay = TimeSpan.FromHours(1),
#if DEBUG
            Performance = metrics,
#endif
        });
        await host.Session.Ready;
        await host.Session.RequestCompletionAsync();
        host.Session.CompletionPopup!.CompletionList.SelectedItem = Assert.Single(host.Session.Completions,
            item => item.Native?.Kind == AkburaCompletionKind.AkcssModule && item.DisplayText == "Akbura.Styles.akcss");
        host.Window.KeyTextInput(".");
        Assert.Equal("@using Akbura.;", host.Editor.Text);
        await host.Session.RequestCompletionAsync();
        Assert.Null(host.Session.LastServiceError);
        Assert.Contains(host.NativeCompletions().Items, item => item.Kind == AkburaCompletionKind.AkcssModule &&
            item.DisplayText == "Akbura.Styles.akcss");
        Assert.True(host.Workspace.CurrentSolution.TryGetDocumentContext(host.Uri, out var moduleContext));
        var cached = Assert.IsType<Akbura.Workspaces.Documents.AkburaSyntacticDocument>(host.Session.CachedSyntax);
        Assert.Equal(host.Editor.Text, cached.Text.ToString());
#if DEBUG
        Assert.Equal(1, metrics.CountOf("Parse"));
        Assert.Equal(1, metrics.CountOf("WithText"));
#endif
        Assert.Contains(host.Workspace.LanguageServices.Completion.GetCompletions(cached, moduleContext, host.Editor.CaretOffset).Items,
            item => item.DisplayText == "Akbura.Styles.akcss");
        var completion = Assert.Single(host.Session.Completions,
            item => item.Native?.Kind == AkburaCompletionKind.AkcssModule && item.DisplayText == "Akbura.Styles.akcss");
        Assert.True(await host.Session.CommitCompletionAsync(completion, ';'));
        Assert.Equal("@using Akbura.Styles.akcss;", host.Editor.Text);
        host.Editor.Undo();
        Assert.Equal("@using Akbura.;", host.Editor.Text);
    });

    [Fact]
    public Task TypingAParameterizedUtilityNeverInsertsItsSignatureOrAnotherCandidate() => EditorTestHost.Run(async () =>
    {
        using var host = new Host("Main.akbura", "using Avalonia.Controls;\n<Button sq| />");
        host.RegisterStyles(Styles);
        await host.Session.Ready;
        await host.Session.RequestCompletionAsync();
        foreach (var character in "uare-48")
        {
            var before = host.Editor.Text;
            var position = host.Editor.CaretOffset;
            host.Window.KeyTextInput(character.ToString());
            Assert.Equal(before.Insert(position, character.ToString()), host.Editor.Text);
        }
        await host.Session.RequestCompletionAsync();
        var item = Assert.Single(host.Session.Completions, item => item.DisplayText == "square-(double size)");
        Assert.Equal("square-48", item.InsertText);
        Assert.NotNull(host.Session.CompletionPopup);
        Assert.Same(item, host.Session.CompletionPopup.CompletionList.SelectedItem);
        Assert.True(await host.Session.CommitCompletionAsync(item));
        Assert.Contains("<Button square-48 />", host.Editor.Text);
        Assert.DoesNotContain("double size", host.Editor.Text);
    });

    [Fact]
    public Task StylesheetNotificationsRefreshTheSameBindingAndRejectOldItems() => EditorTestHost.Run(async () =>
    {
        using var host = new Host("Main.akbura", "using Avalonia.Controls;\n<Button | />");
        await host.Session.Ready;
        await host.UpdateAsync(() => host.RegisterStyles(Styles));
        await host.Session.RequestCompletionAsync();
        var oldItem = Assert.Single(host.Session.Completions, item => item.DisplayText == "square-(double size)");
        var source = host.Editor.Text;
        await host.UpdateAsync(() => host.RegisterStyles(Styles.Replace("square-", "round-")));
        Assert.False(await host.Session.CommitCompletionAsync(oldItem));
        await host.Session.RequestCompletionAsync();
        Assert.Contains(host.Session.Completions, item => item.DisplayText == "round-(double size)");
        Assert.DoesNotContain(host.Session.Completions, item => item.DisplayText == "square-(double size)");
        Assert.True(host.Workspace.CurrentSolution.TryGetDocument(new Uri("memory:///Views/Main.akcss"), out var styles));
        await host.UpdateAsync(() => host.Workspace.RemoveDocument(styles!.Id));
        await host.Session.RequestCompletionAsync();
        Assert.DoesNotContain(host.Session.Completions, item => item.DisplayText == "round-(double size)");
        Assert.Same(host.Session, host.Editor.Session);
        Assert.Equal(source, host.Editor.Text);
    });

    [Fact]
    public Task PendingStylesheetResultsCannotPublishOrEraseExistingHighlighting() => EditorTestHost.Run(async () =>
    {
        var scheduler = new ControlledScheduler();
        using var host = new Host("Main.akbura", "using Avalonia.Controls;\n<Button sq| />", new()
        {
            Scheduler = scheduler, AutomaticCompletion = false, AnalysisDelay = TimeSpan.FromHours(1),
        });
        await host.Session.Ready;
        await host.UpdateAsync(() => host.RegisterStyles(Styles));
        var colorizer = Assert.IsType<WorkspaceColorizer>(Assert.Single(host.Editor.TextArea.TextView.LineTransformers, static transformer => transformer is WorkspaceColorizer));
        var colors = colorizer.Spans;
        var held = scheduler.HoldNextResult(skipOperations: 1);
        var old = host.Session.RequestCompletionAsync();
        await held.Started.Task.WaitAsync(TimeSpan.FromSeconds(30));
        var analysis = scheduler.HoldNextResult(skipOperations: 1);
        var update = host.UpdateAsync(() => host.RegisterStyles(Styles.Replace("square-", "round-")));
        await analysis.Started.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(colors == colorizer.Spans);
        analysis.Release.SetResult();
        await update;
        held.Release.SetResult();
        await old;
        Assert.Empty(host.Session.Completions);
        Assert.Null(host.Session.CompletionPopup);
        Assert.NotEmpty(colorizer.Spans);
    });

    [Fact]
    public Task StylesheetRenameUsesTheNewDocumentIdentityAndExplicitImport() => EditorTestHost.Run(async () =>
    {
        using var host = new Host("Main.akbura", "using Avalonia.Controls;\n<Button sq| />");
        await host.Session.Ready;
        await host.UpdateAsync(() => host.RegisterStyles(Styles));
        Assert.True(host.Workspace.CurrentSolution.TryGetDocument(new Uri("memory:///Views/Main.akcss"), out var old));
        await host.UpdateAsync(() => host.Workspace.RemoveDocument(old!.Id));
        var renamed = new Uri("memory:///Views/Renamed.akcss");
        await host.UpdateAsync(() => host.Workspace.OpenOrChangeDocument(renamed, SourceText.From(Styles)));
        await host.Session.RequestCompletionAsync();
        Assert.DoesNotContain(host.Session.Completions, item => item.DisplayText == "square-(double size)");

        var original = host.Editor.Text;
        host.Editor.Text = "using ";
        host.Editor.CaretOffset = host.Editor.Text.Length;
        await host.Session.RequestCompletionAsync();
        // Use the existing service's logical module name, including its project/path rules.
        var module = Assert.Single(host.Session.Completions, item => item.Native?.Kind == AkburaCompletionKind.AkcssModule &&
            item.InsertText.EndsWith("Renamed.akcss", StringComparison.Ordinal));
        host.Editor.Text = $"using {module.InsertText};\n" + original;
        host.Editor.CaretOffset = host.Editor.Text.IndexOf("sq /", StringComparison.Ordinal) + 2;
        await host.Session.RequestCompletionAsync();
        Assert.Contains(host.Session.Completions, item => item.DisplayText == "square-(double size)");
        Assert.True(host.Workspace.CurrentSolution.TryGetDocument(renamed, out var current));
        Assert.NotEqual(old!.Id, current!.Id);
    });

    [Fact]
    public Task AStylesheetChangeDuringCommitKeepsTheTypedCharacterAndRejectsTheEdit() => EditorTestHost.Run(async () =>
    {
        var scheduler = new ControlledScheduler();
        using var host = new Host("Main.akbura", "using Avalonia.Controls;\n<Button sq| />", new()
        {
            Scheduler = scheduler, AutomaticCompletion = false, AnalysisDelay = TimeSpan.FromHours(1),
        });
        await host.Session.Ready;
        await host.UpdateAsync(() => host.RegisterStyles(Styles));
        await host.Session.RequestCompletionAsync();
        var completion = Assert.Single(host.Session.Completions, item => item.DisplayText == "square-(double size)");
        var before = host.Editor.Text;
        var position = host.Editor.CaretOffset;
        var held = scheduler.HoldNextResult();
        var commit = host.Session.CommitCompletionAsync(completion, ' ');
        await held.Started.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await host.UpdateAsync(() => host.RegisterStyles(Styles.Replace("square-", "round-")));
        held.Release.SetResult();
        Assert.False(await commit);
        Assert.Equal(before.Insert(position, " "), host.Editor.Text);
    });

    [Fact]
    public Task ApplyCandidatesFromDifferentModulesKeepTheirSourceDistinctions() => EditorTestHost.Run(async () =>
    {
        using var host = new Host("Main.akcss", "@using |");
        await host.Session.Ready;
        await host.UpdateAsync(() => host.Workspace.OpenOrChangeDocument(new Uri("memory:///Views/Oak.akcss"), SourceText.From(Styles)));
        await host.UpdateAsync(() => host.Workspace.OpenOrChangeDocument(new Uri("memory:///Views/Pine.akcss"), SourceText.From(Styles)));
        await host.Session.RequestCompletionAsync();
        var oak = Assert.Single(host.Session.Completions, item => item.Native?.Kind == AkburaCompletionKind.AkcssModule &&
            item.InsertText.EndsWith("Oak.akcss", StringComparison.Ordinal)).InsertText;
        var pine = Assert.Single(host.Session.Completions, item => item.Native?.Kind == AkburaCompletionKind.AkcssModule &&
            item.InsertText.EndsWith("Pine.akcss", StringComparison.Ordinal)).InsertText;
        host.Editor.Text = $"@using Avalonia.Controls;\n@using {oak};\n@using {pine};\nButton.secondary {{\n    @apply pr;\n}}";
        host.Editor.CaretOffset = host.Editor.Text.IndexOf("@apply pr", StringComparison.Ordinal) + "@apply pr".Length;
        await host.Session.RequestCompletionAsync();
        var native = host.NativeCompletions();
        Assert.Equal(2, native.Items.Count(item => item.DisplayText == "primary"));
        host.AssertAdaptation(native);
        Assert.Equal(2, host.Session.Completions.Count(item => item.DisplayText == "primary"));
    });

    [Theory]
    [InlineData("typing")]
    [InlineData("replace")]
    [InlineData("dispose")]
    public Task OldAkcssRequestsCannotReopenPopupsAfterEditingOrDisposal(string action) => EditorTestHost.Run(async () =>
    {
        var scheduler = new ControlledScheduler();
        using var host = new Host("Main.akcss", "@u|", new()
        {
            Scheduler = scheduler, AutomaticCompletion = false, AnalysisDelay = TimeSpan.FromHours(1),
        });
        await host.Session.Ready;
        var held = scheduler.HoldNextResult(skipOperations: 1);
        var old = host.Session.RequestCompletionAsync();
        await held.Started.Task.WaitAsync(TimeSpan.FromSeconds(30));
        if (action == "typing") host.Window.KeyTextInput("s");
        else if (action == "replace") host.Editor.Document = new TextDocument("// replacement 😀\r\n");
        else host.Session.Dispose();
        held.Release.SetResult();
        await old;
        Assert.Empty(host.Session.Completions);
        Assert.Null(host.Session.CompletionPopup);
    });

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (!predicate()) await Task.Delay(10, timeout.Token);
    }

    private sealed class Host : IDisposable
    {
        internal Host(string file, string markedSource, AkburaEditorOptions? options = null)
        {
            Workspace = new AkburaWorkspace();
            Workspace.DefaultProject.UseAvalonia(References.Value);
            Uri = new Uri("memory:///Views/" + file);
            var position = markedSource.IndexOf('|');
            Assert.True(position >= 0);
            Editor = new AkburaCodeEditor { Text = markedSource.Remove(position, 1) };
            Session = Editor.Bind(Workspace.DefaultProject, Uri, options ?? new()
            {
                Scheduler = new AkburaInlineScheduler(),
                AnalysisDelay = TimeSpan.FromHours(1),
            });
            Window = new Window { Content = Editor, Width = 900, Height = 500 };
            Window.Show();
            Editor.TextArea.Focus();
            Editor.CaretOffset = position;
        }

        internal AkburaWorkspace Workspace
        {
            get;
        }

        internal Uri Uri
        {
            get;
        }

        internal AkburaCodeEditor Editor
        {
            get;
        }

        internal AkburaEditorSession Session
        {
            get;
        }

        internal Window Window
        {
            get;
        }

        internal void RegisterStyles(string source) => Workspace.OpenOrChangeDocument(
            new Uri("memory:///Views/Main.akcss"), SourceText.From(source));

        internal async Task UpdateAsync(Action update)
        {
            var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Session.AnalysisUpdated += Complete;
            try
            {
                update();
                await completed.Task.WaitAsync(TimeSpan.FromSeconds(30));
            }
            finally { Session.AnalysisUpdated -= Complete; }
            void Complete(object? sender, EventArgs args) => completed.TrySetResult();
        }

        internal AkburaCompletionResult NativeCompletions()
        {
            Assert.True(Workspace.CurrentSolution.TryGetDocumentContext(Uri, out var context));
            Assert.Equal(Editor.Text, context!.Document.Text.ToString());
            var syntax = AkburaSyntacticDocument.Parse(context.Document.Text, context.Document.FilePath);
            return Workspace.LanguageServices.Completion.GetCompletions(syntax, context, Editor.CaretOffset);
        }

        internal void AssertAdaptation(AkburaCompletionResult result)
        {
            foreach (var native in result.Items)
            {
                var adapted = Assert.Single(Session.Completions, item => !item.IsProjected && item.ResolveKey == native.ResolveKey &&
                    item.SortText == native.SortText && item.Native!.Suffix == native.Suffix);
                Assert.Equal(native.DisplayText, adapted.DisplayText);
                Assert.Equal(native.FilterText, adapted.Text);
                Assert.Equal(native.InsertText, adapted.InsertText);
                Assert.Equal(native.SortText, adapted.SortText);
                Assert.Equal(result.ApplicableSpan, adapted.SourceSpan);
                Assert.Equal(100 - native.Priority, adapted.Priority);
                Assert.Equal(native.Suffix.Length > 0 ? $"{native.DisplayText}  {native.Suffix}" : native.DisplayText, adapted.Content);
                Assert.Equal(native.Description, adapted.Native!.Description);
            }
        }

        public void Dispose()
        {
            Session.Dispose();
            Window.Close();
            Workspace.Dispose();
        }
    }

    private const string Styles = """
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
}
