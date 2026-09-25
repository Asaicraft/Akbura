using Akbura.LanguageServer.Handlers.Completion;
using Akbura.Workspaces.Projects;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Akbura.LanguageServer.UnitTests;

public sealed class BindingCompletionProtocolTests
{
    [Fact]
    public async Task StaleSemanticContextIsRebasedForBindingCompletion()
    {
        const string semanticSource = "namespace Gallery;\nusing Avalonia.Controls;\n<StackPanel x.DataType=\"MainViewModel\"><Border Tag=${Binding I}/></StackPanel>";
        const string currentSourceWithCaret = "namespace Gallery;\nusing Avalonia.Controls;\n<StackPanel x.DataType=\"MainViewModel\"><Border Tag=${Binding In|}/></StackPanel>";
        await using var fixture = new CompletionFixture(semanticSource, currentSourceWithCaret);

        var completion = await fixture.CompleteAsync();

        Assert.Contains(completion.Items, static item => item.Label == "IncrementCommand");
        Assert.DoesNotContain(completion.Items, static item => item.Label is "$if" or "$foreach");
        var item = Assert.Single(completion.Items, static item => item.Label == "IncrementCommand");
        Assert.Equal("IncrementCommand", fixture.Apply(item));
    }

    [Fact]
    public async Task BindingCompletionTextEditReplacesSuffixWithoutDuplicatingIt()
    {
        const string sourceWithCaret = "namespace Gallery;\nusing Avalonia.Controls;\n<Border x.DataType=\"MainViewModel\" Tag=${Binding Customer.Na|me}/>";
        await using var fixture = new CompletionFixture(sourceWithCaret.Replace("|", string.Empty, StringComparison.Ordinal), sourceWithCaret);

        var completion = await fixture.CompleteAsync();
        var item = Assert.Single(completion.Items, static item => item.Label == "Name");

        Assert.Equal("Name", fixture.GetEditedText(item));
        Assert.Contains("Binding Customer.Name", fixture.ApplyToDocument(item), StringComparison.Ordinal);
        Assert.DoesNotContain("NameName", fixture.ApplyToDocument(item), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DirectionalAttributeEditReplacesOnlyTheMemberName()
    {
        const string sourceWithCaret = "namespace Gallery;\nusing Avalonia.Controls;\nstate string text = \"\";\n<TextBox bind:T|ext={text} />";
        await using var fixture = new CompletionFixture(
            sourceWithCaret.Replace("|", string.Empty, StringComparison.Ordinal),
            sourceWithCaret);

        var completion = await fixture.CompleteAsync();
        var item = Assert.Single(completion.Items, static item => item.Label == "Text");

        Assert.Equal("Text", fixture.GetEditedText(item));
        Assert.Equal("Text", fixture.Apply(item));
        Assert.Contains("bind:Text={text}", fixture.ApplyToDocument(item), StringComparison.Ordinal);
        Assert.DoesNotContain("Textext", fixture.ApplyToDocument(item), StringComparison.Ordinal);
    }

    [Fact]
    public async Task EmptyDirectionalAttributeCreatesOneBareBindingTarget()
    {
        const string sourceWithCaret = "namespace Gallery;\nusing Avalonia.Controls;\nstate string text = \"\";\n<TextBox out:| />";
        await using var fixture = new CompletionFixture(
            sourceWithCaret.Replace("|", string.Empty, StringComparison.Ordinal),
            sourceWithCaret);

        var completion = await fixture.CompleteAsync();
        var item = Assert.Single(completion.Items, static item => item.Label == "Text");

        Assert.Equal(string.Empty, fixture.GetEditedText(item));
        Assert.Equal(2, item.InsertTextFormat);
        Assert.Equal("Text={$0\\}", fixture.Apply(item));
        Assert.Contains("out:Text={}", fixture.ApplySnippetToDocument(item), StringComparison.Ordinal);
        Assert.DoesNotContain("out:out:", fixture.ApplySnippetToDocument(item), StringComparison.Ordinal);
        Assert.DoesNotContain("\"{}\"", fixture.ApplySnippetToDocument(item), StringComparison.Ordinal);
    }

    private sealed class CompletionFixture : IAsyncDisposable
    {
        private readonly AkburaWorkspace _workspace;
        private readonly AkburaServerLifetime _lifetime = new();
        private readonly NullLogger _logger = new();
        private readonly AkburaParentProcessMonitor _monitor;
        private readonly AkburaLanguageServerServices _services;
        private readonly AkburaRequestContext _context;
        private readonly int _position;

        public CompletionFixture(string semanticSource, string currentSourceWithCaret)
        {
            _position = currentSourceWithCaret.IndexOf('|');
            Assert.True(_position >= 0);
            var currentSource = currentSourceWithCaret.Remove(_position, 1);
            var projectContext = new ProjectContext(ProjectId.CreateNewId(), "C:/Project/BindingCompletion.csproj", "C:/Project", "Gallery", CreateCompilation(), ImmutableArray<ProjectReference>.Empty);
            _workspace = new AkburaWorkspace(projectContext);
            var uri = new Uri("C:/Project/Component.akbura");
            var semanticText = SourceText.From(semanticSource);
            var semantic = _workspace.OpenOrChangeDocumentContext(_workspace.DefaultProjectId, uri, semanticText);
            var currentText = SourceText.From(currentSource);
            var syntactic = AkburaSyntacticDocument.Parse(currentText, uri.LocalPath);
            var openDocument = new AkburaOpenDocument(uri, "akbura", Version: 2, currentText, syntactic, semantic.Project.Id, semantic.Document.Id, semanticText);
            var snapshot = AkburaServerSnapshot.Create(_workspace) with
            {
                OpenDocuments = AkburaServerSnapshot.Create(_workspace).OpenDocuments.Add(uri, openDocument),
            };

            _monitor = new AkburaParentProcessMonitor(_lifetime, _logger);
            _services = new AkburaLanguageServerServices(_workspace, new NullClient(), _logger, new Utf16PositionConverter(), _lifetime, _monitor, AkburaServerOptions.Parse([]));
            _context = new AkburaRequestContext
            {
                Method = LspMethods.Completion,
                Solution = snapshot.Solution,
                ServerSnapshot = snapshot,
                OpenDocument = openDocument,
                SyntacticDocument = syntactic,
                SemanticDocument = semantic,
                ClientCapabilities = snapshot.ClientCapabilities,
                PositionEncoding = snapshot.PositionEncoding,
                Services = _services,
            };
        }

        public async Task<CompletionList> CompleteAsync()
        {
            var linePosition = _context.OpenDocument!.Text.Lines.GetLinePosition(_position);
            var result = await new CompletionHandler().HandleAsync(new CompletionParams
            {
                TextDocument = new TextDocumentIdentifier { Uri = _context.OpenDocument.Uri.AbsoluteUri },
                Position = new Position { Line = linePosition.Line, Character = linePosition.Character },
                Context = new CompletionContext { TriggerKind = 1 },
            }, _context, CancellationToken.None);
            return result.TypedResponse;
        }

        public string GetEditedText(Protocol.CompletionItem item)
        {
            var edit = Assert.IsType<TextEdit>(item.TextEdit);
            var span = _services.PositionConverter.ToTextSpan(_context.OpenDocument!.Text, edit.Range);
            return _context.OpenDocument.Text.ToString(span);
        }

        public string Apply(Protocol.CompletionItem item)
        {
            var edit = Assert.IsType<TextEdit>(item.TextEdit);
            return edit.NewText;
        }

        public string ApplyToDocument(Protocol.CompletionItem item)
        {
            var edit = Assert.IsType<TextEdit>(item.TextEdit);
            var text = _context.OpenDocument!.Text;
            var span = _services.PositionConverter.ToTextSpan(text, edit.Range);
            return text.WithChanges(new TextChange(span, edit.NewText)).ToString();
        }

        public string ApplySnippetToDocument(Protocol.CompletionItem item)
        {
            var edit = Assert.IsType<TextEdit>(item.TextEdit);
            Assert.Equal(2, item.InsertTextFormat);
            var insertedText = edit.NewText
                .Replace("$0", string.Empty, StringComparison.Ordinal)
                .Replace("\\}", "}", StringComparison.Ordinal);
            var text = _context.OpenDocument!.Text;
            var span = _services.PositionConverter.ToTextSpan(text, edit.Range);
            return text.WithChanges(new TextChange(span, insertedText)).ToString();
        }

        public ValueTask DisposeAsync()
        {
            _monitor.Dispose();
            _lifetime.Dispose();
            _workspace.Dispose();
            _logger.Dispose();
            return ValueTask.CompletedTask;
        }

        private static CSharpCompilation CreateCompilation()
        {
            const string source = """
                namespace Avalonia.Controls
                {
                    public class Control { }
                    public sealed class Border : Control { public object? Tag { get; set; } }
                    public sealed class StackPanel : Control { }
                    public sealed class TextBox : Control { public string? Text { get; set; } }
                }

                namespace Avalonia.Data
                {
                    public class Binding
                    {
                        public string? Path { get; set; }
                    }

                    public sealed class ReflectionBinding : Binding { }
                    public sealed class CompiledBinding : Binding { }
                }

                namespace Gallery
                {
                    public sealed class MainViewModel
                    {
                        public string IncrementCommand { get; } = string.Empty;
                        public Customer Customer { get; } = new();
                    }

                    public sealed class Customer
                    {
                        public string Name { get; } = string.Empty;
                    }
                }
                """;
            return CSharpCompilation.Create("BindingCompletion", [CSharpSyntaxTree.ParseText(source)], [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)], new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        }
    }

    private sealed class NullClient : IAkburaLspClient
    {
        public Task NotifyAsync<TParams>(string method, TParams parameters, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<TResult?> RequestAsync<TParams, TResult>(string method, TParams parameters, CancellationToken cancellationToken) => Task.FromResult(default(TResult));
    }

    private sealed class NullLogger : IAkburaServerLogger
    {
        public void Log(AkburaServerLogLevel level, string message, Exception? exception = null)
        {
        }

        public void Dispose()
        {
        }
    }
}
