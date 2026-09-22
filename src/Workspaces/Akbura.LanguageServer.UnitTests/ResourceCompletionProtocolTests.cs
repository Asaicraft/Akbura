using Akbura.LanguageServer.Diagnostics;
using Akbura.LanguageServer.Dispatch;
using Akbura.LanguageServer.Handlers.Completion;
using Akbura.LanguageServer.Handlers.Documents;
using Akbura.LanguageServer.Hosting;
using Akbura.LanguageServer.Projects;
using Akbura.LanguageServer.Protocol;
using Akbura.LanguageServer.State;
using Akbura.Workspaces;
using Akbura.Workspaces.Projects;
using Akbura.Workspaces.Resources;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using System.Text;

namespace Akbura.LanguageServer.UnitTests;

public sealed class ResourceCompletionProtocolTests
{
    private const string AvaloniaNamespace =
        "https://github.com/avaloniaui";
    private const string XamlNamespace =
        "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public async Task LargeResourceCatalogRemainsIncompleteAndRefilters()
    {
        var resources = new StringBuilder();
        for (var index = 0; index < 75; index++)
        {
            resources.Append("<x:String x:Key=\"Key");
            resources.Append(index.ToString("D2"));
            resources.Append("\">value</x:String>");
        }

        const string initialSource =
            "using Avalonia.Controls;\r\n" +
            "<Border Tag=${StaticResource } />";
        await using var fixture = new CompletionFixture(
            resources.ToString(),
            initialSource);

        var initial = await fixture.CompleteAsync(
            initialSource,
            triggerKind: 1);

        Assert.True(initial.IsIncomplete);
        Assert.Equal(50, initial.Items.Length);
        Assert.DoesNotContain(
            initial.Items,
            static item => item.Label == "Key74");

        const string filteredSource =
            "using Avalonia.Controls;\r\n" +
            "<Border Tag=${StaticResource Key74} />";
        await fixture.ChangeAsync(filteredSource, version: 2);

        var filtered = await fixture.CompleteAsync(
            filteredSource,
            triggerKind: 3);

        Assert.False(filtered.IsIncomplete);
        Assert.Contains(
            filtered.Items,
            static item => item.Label == "Key74");
    }

    [Fact]
    public async Task UnsavedResourceNotificationChangesCompletion()
    {
        const string source =
            "using Avalonia.Controls;\r\n" +
            "<Border Tag=${StaticResource } />";
        await using var fixture = new CompletionFixture(
            "<x:String x:Key=\"DiskResource\">disk</x:String>",
            source);

        var disk = await fixture.CompleteAsync(source, triggerKind: 1);
        Assert.Contains(
            disk.Items,
            static item => item.Label == "DiskResource");

        await fixture.OpenResourceAsync(
            CreateApplication(
                "<x:String x:Key=\"DiskResource\">disk</x:String>"),
            version: 1);
        await fixture.ChangeResourceAsync(
            CreateApplication(
                "<x:String x:Key=\"LiveResource\">live</x:String>"),
            version: 2);

        var live = await fixture.CompleteAsync(source, triggerKind: 3);
        Assert.Contains(
            live.Items,
            static item => item.Label == "LiveResource");
        Assert.DoesNotContain(
            live.Items,
            static item => item.Label == "DiskResource");
    }

    private static string CreateApplication(string resources)
    {
        return "<Application xmlns=\"" +
            AvaloniaNamespace +
            "\" xmlns:x=\"" +
            XamlNamespace +
            "\"><Application.Resources>" +
            "<ResourceDictionary>" +
            resources +
            "</ResourceDictionary>" +
            "</Application.Resources></Application>";
    }

    private sealed class CompletionFixture : IAsyncDisposable
    {
        private static readonly PortableExecutableReference
            ResourceMarkupExtensionsReference =
                CreateResourceMarkupExtensionsReference();
        private readonly AkburaWorkspace _workspace;
        private readonly AkburaServerLifetime _lifetime = new();
        private readonly NullLogger _logger = new();
        private readonly AkburaParentProcessMonitor _monitor;
        private readonly AkburaProjectLoadCoordinator _projects;
        private readonly AkburaRequestExecutionQueue _queue;

        public CompletionFixture(string resources, string source)
        {
            var compilation = CreateApplicationCompilation();
            _workspace = CreateWorkspace(compilation);
            SynchronizeResources(
                _workspace,
                compilation,
                resources);

            DocumentUri = new Uri(Path.Combine(
                Path.GetTempPath(),
                "Akbura.LanguageServer.Tests",
                Guid.NewGuid().ToString("N"),
                "Component.akbura"));
            var text = SourceText.From(source);
            var semantic = _workspace.OpenOrChangeDocumentContext(
                _workspace.DefaultProjectId,
                DocumentUri,
                text);
            var openDocument = new AkburaOpenDocument(
                DocumentUri,
                "akbura",
                Version: 1,
                text,
                AkburaSyntacticDocument.Parse(
                    text,
                    DocumentUri.LocalPath),
                semantic.Project.Id,
                semantic.Document.Id,
                text);
            var snapshot = AkburaServerSnapshot.Create(_workspace) with
            {
                OpenDocuments = AkburaServerSnapshot
                    .Create(_workspace)
                    .OpenDocuments
                    .Add(DocumentUri, openDocument),
            };
            State = new AkburaServerState(snapshot);

            _monitor = new AkburaParentProcessMonitor(
                _lifetime,
                _logger);
            var services = new AkburaLanguageServerServices(
                _workspace,
                new NullClient(),
                _logger,
                new Utf16PositionConverter(),
                _lifetime,
                _monitor,
                AkburaServerOptions.Parse([]));
            _queue = new AkburaRequestExecutionQueue(
                new AkburaLspHandlerRegistry(
                [
                    new CompletionHandler(),
                    new DidChangeHandler(),
                    new ResourceDocumentDidOpenHandler(),
                    new ResourceDocumentDidChangeHandler(),
                ]),
                new AkburaRequestContextFactory(State, services),
                State,
                _logger);
            _projects = new AkburaProjectLoadCoordinator(
                services,
                _queue,
                new NullProjectLoader());
            services.CompleteComposition(
                new AkburaDiagnosticsPublisher(State, services),
                _projects);
        }

        public Uri DocumentUri { get; }

        public AkburaServerState State { get; }

        public Uri ResourceUri { get; } =
            new("C:/Project/App.axaml");

        public async Task<CompletionList> CompleteAsync(string source, int triggerKind)
        {
            var offset = source.IndexOf('}', StringComparison.Ordinal);
            Assert.True(offset >= 0);
            var linePosition = SourceText.From(source)
                .Lines
                .GetLinePosition(offset);
            var result = await _queue.ExecuteAsync<CompletionList>(
                LspMethods.Completion,
                new CompletionParams
                {
                    TextDocument = new TextDocumentIdentifier
                    {
                        Uri = DocumentUri.AbsoluteUri,
                    },
                    Position = new Position
                    {
                        Line = linePosition.Line,
                        Character = linePosition.Character,
                    },
                    Context = new CompletionContext
                    {
                        TriggerKind = triggerKind,
                    },
                },
                CancellationToken.None);
            return Assert.IsType<CompletionList>(result);
        }

        public Task ChangeAsync(string source, int version)
        {
            return _queue.ExecuteAsync<object?>(
                LspMethods.DidChange,
                new DidChangeTextDocumentParams
                {
                    TextDocument = new VersionedTextDocumentIdentifier
                    {
                        Uri = DocumentUri.AbsoluteUri,
                        Version = version,
                    },
                    ContentChanges =
                    [
                        new TextDocumentContentChangeEvent
                        {
                            Text = source,
                        },
                    ],
                },
                CancellationToken.None);
        }

        public Task OpenResourceAsync(string source, int version)
        {
            return _queue.ExecuteAsync<object?>(
                LspMethods.ResourceDocumentDidOpen,
                new DidOpenTextDocumentParams
                {
                    TextDocument = new TextDocumentItem
                    {
                        Uri = ResourceUri.AbsoluteUri,
                        LanguageId = "xml",
                        Version = version,
                        Text = source,
                    },
                },
                CancellationToken.None);
        }

        public Task ChangeResourceAsync(string source, int version)
        {
            return _queue.ExecuteAsync<object?>(
                LspMethods.ResourceDocumentDidChange,
                new DidChangeTextDocumentParams
                {
                    TextDocument = new VersionedTextDocumentIdentifier
                    {
                        Uri = ResourceUri.AbsoluteUri,
                        Version = version,
                    },
                    ContentChanges =
                    [
                        new TextDocumentContentChangeEvent
                        {
                            Text = source,
                        },
                    ],
                },
                CancellationToken.None);
        }

        public async ValueTask DisposeAsync()
        {
            await _projects.DisposeAsync();
            await _queue.DisposeAsync();
            _monitor.Dispose();
            _lifetime.Dispose();
            _workspace.Dispose();
            _logger.Dispose();
        }

        private static AkburaWorkspace CreateWorkspace(CSharpCompilation compilation)
        {
            var context = new ProjectContext(
                ProjectId.CreateNewId(),
                "C:/Project/ProtocolCompletion.csproj",
                "C:/Project",
                "ProtocolCompletion",
                compilation,
                ImmutableArray<ProjectReference>.Empty);
            return new AkburaWorkspace(context);
        }

        private static CSharpCompilation CreateApplicationCompilation()
        {
            const string source = """
                namespace Avalonia
                {
                    public class Application
                    {
                    }
                }

                namespace Avalonia.Controls
                {
                    public interface IResourceDictionary
                        : System.Collections.Generic.IDictionary<object, object?>
                    {
                    }

                    public sealed class ResourceDictionary
                        : System.Collections.Generic.Dictionary<object, object?>,
                          IResourceDictionary
                    {
                    }

                    public class Border
                    {
                        public object? Tag { get; set; }
                    }
                }

                """;
            return CSharpCompilation.Create(
                "ProtocolCompletion",
                [CSharpSyntaxTree.ParseText(source)],
                [
                    MetadataReference.CreateFromFile(
                        typeof(object).Assembly.Location),
                    ResourceMarkupExtensionsReference,
                ],
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary));
        }

        private static PortableExecutableReference CreateResourceMarkupExtensionsReference()
        {
            const string source = """
                namespace Avalonia.Markup.Xaml.MarkupExtensions;

                public sealed class StaticResourceExtension
                {
                    public StaticResourceExtension(object key)
                    {
                    }

                    public object ProvideValue() => new();
                }

                public sealed class DynamicResourceExtension
                {
                    public DynamicResourceExtension(object key)
                    {
                    }

                    public object ProvideValue() => new();
                }
                """;
            var compilation = CSharpCompilation.Create(
                "Avalonia.Markup.Xaml",
                [CSharpSyntaxTree.ParseText(source)],
                [MetadataReference.CreateFromFile(
                    typeof(object).Assembly.Location)],
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary));
            using var stream = new MemoryStream();
            var result = compilation.Emit(
                stream,
                options: new EmitOptions(
                    metadataOnly: true,
                    includePrivateMembers: false));
            if (!result.Success)
            {
                throw new InvalidOperationException(
                    string.Join(
                        Environment.NewLine,
                        result.Diagnostics));
            }

            return MetadataReference.CreateFromImage(stream.ToArray());
        }

        private static void SynchronizeResources(AkburaWorkspace workspace, CSharpCompilation compilation, string resources)
        {
            const string path = "C:/Project/App.axaml";
            var input = new ResourceDocumentInput(
                new Uri(path),
                path,
                "App.axaml",
                ResourceAssemblyIdentity.Create(
                    compilation.Assembly.Identity),
                SourceText.From(CreateApplication(resources)),
                VersionStamp.Create(),
                "project",
                "net10.0");
            workspace.SynchronizeProjectResourceDocuments(
                workspace.DefaultProjectId,
                ImmutableArray.Create(input));
        }
    }

    private sealed class NullProjectLoader : IAkburaProjectLoader
    {
        public event EventHandler<ProjectContextChangedEventArgs>? Changed
        {
            add { }
            remove { }
        }

        public Task<AkburaLoadedProject> LoadProjectAsync(string projectPath, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<ImmutableArray<AkburaLoadedProject>> LoadSolutionAsync(string solutionPath, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public void Dispose()
        {
        }
    }

    private sealed class NullClient : IAkburaLspClient
    {
        public Task NotifyAsync<TParams>(string method, TParams parameters, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task<TResult?> RequestAsync<TParams, TResult>(string method, TParams parameters, CancellationToken cancellationToken)
        {
            return Task.FromResult(default(TResult));
        }
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
