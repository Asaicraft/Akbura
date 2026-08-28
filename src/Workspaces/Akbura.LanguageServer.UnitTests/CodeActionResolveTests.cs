using Akbura.LanguageServer.Handlers.LanguageFeatures;
using Akbura.Workspaces.Projects;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Text.Json;

namespace Akbura.LanguageServer.UnitTests;

public sealed class CodeActionResolveTests
{
    [Fact]
    public async Task ResolveRecomputesVersionedWorkspaceEdit()
    {
        using var fixture = new ResolveFixture(documentVersion: 3);
        var data = fixture.CreateData(version: 3);
        var result = await new CodeActionResolveHandler().HandleAsync(
            new Protocol.CodeAction
            {
                Title = "Add import",
                Data = JsonSerializer.SerializeToElement(data),
            },
            fixture.Context,
            CancellationToken.None);

        Assert.NotNull(result.TypedResponse.Edit);
        Assert.Null(result.TypedResponse.Edit.Changes);
        var documentEdit = Assert.Single(
            result.TypedResponse.Edit.DocumentChanges!);
        Assert.Equal(3, documentEdit.TextDocument.Version);
        Assert.Contains(
            "using Avalonia.Controls.Primitives;",
            Assert.Single(documentEdit.Edits).NewText,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResolveRejectsStaleDocumentVersion()
    {
        using var fixture = new ResolveFixture(documentVersion: 4);
        var action = new Protocol.CodeAction
        {
            Title = "Add import",
            Data = JsonSerializer.SerializeToElement(
                fixture.CreateData(version: 3)),
        };

        var exception = await Assert.ThrowsAsync<AkburaProtocolException>(
            () => new CodeActionResolveHandler().HandleAsync(
                action,
                fixture.Context,
                CancellationToken.None));

        Assert.Equal(LspErrorCodes.ContentModified, exception.Code);
    }

    private sealed class ResolveFixture : IDisposable
    {
        private const string Source = "<TemplatedControl/>\n";

        private readonly AkburaWorkspace _workspace;
        private readonly AkburaServerLifetime _lifetime;
        private readonly NullLogger _logger;
        private readonly AkburaParentProcessMonitor _monitor;
        private readonly Uri _uri;
        private readonly string _equivalenceKey;

        public ResolveFixture(int documentVersion)
        {
            var projectContext = new ProjectContext(
                ProjectId.CreateNewId(),
                projectFilePath: string.Empty,
                projectDirectory: Environment.CurrentDirectory,
                rootNamespace: string.Empty,
                CreateCompilation(),
                ImmutableArray<ProjectReference>.Empty);
            _workspace = new AkburaWorkspace(projectContext);
            _uri = new Uri(Path.GetFullPath("ResolveView.akbura"));
            var text = SourceText.From(Source);
            var semanticContext = _workspace
                .OpenOrChangeDocumentContext(_uri, text);
            var syntacticDocument = AkburaSyntacticDocument.Parse(
                text,
                _uri.LocalPath);
            var action = Assert.Single(
                _workspace.LanguageServices.CodeActions.GetCodeActions(
                    semanticContext,
                    new TextSpan(0, Source.Length)));
            _equivalenceKey = action.EquivalenceKey;

            var openDocument = new AkburaOpenDocument(
                _uri,
                "akbura",
                documentVersion,
                text,
                syntacticDocument,
                semanticContext.Project.Id,
                semanticContext.Document.Id,
                text);
            var capabilities = new AkburaClientCapabilities(
                SupportsSnippets: true,
                SupportsCompletionResolve: true,
                SupportsCodeActionResolve: true,
                SupportsDocumentChanges: true,
                SupportsPullDiagnostics: false,
                SupportsDiagnosticRefresh: false,
                SupportsDynamicFileWatching: false,
                SupportsSemanticTokensRefresh: false);
            var snapshot = AkburaServerSnapshot.Create(_workspace) with
            {
                OpenDocuments = ImmutableDictionary
                    .Create<Uri, AkburaOpenDocument>(
                        AkburaUriComparer.Instance)
                    .Add(_uri, openDocument),
                ClientCapabilities = capabilities,
            };
            _lifetime = new AkburaServerLifetime();
            _logger = new NullLogger();
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
            Context = new AkburaRequestContext
            {
                Method = LspMethods.CodeActionResolve,
                Solution = snapshot.Solution,
                ServerSnapshot = snapshot,
                OpenDocument = null,
                SyntacticDocument = null,
                SemanticDocument = null,
                ClientCapabilities = capabilities,
                PositionEncoding = AkburaPositionEncoding.Utf16,
                Services = services,
            };
        }

        public AkburaRequestContext Context { get; }

        public AkburaCodeActionResolveData CreateData(int version) =>
            new()
            {
                Uri = _uri.AbsoluteUri,
                Version = version,
                Start = 0,
                Length = Source.Length,
                EquivalenceKey = _equivalenceKey,
            };

        public void Dispose()
        {
            _monitor.Dispose();
            _lifetime.Dispose();
            _workspace.Dispose();
            _logger.Dispose();
        }

        private static CSharpCompilation CreateCompilation()
        {
            const string source = """
                namespace Avalonia.Controls
                {
                    public class Control
                    {
                    }
                }

                namespace Avalonia.Controls.Primitives
                {
                    public sealed class TemplatedControl :
                        Avalonia.Controls.Control
                    {
                    }
                }

                namespace Akbura
                {
                    public class AkburaControl :
                        Avalonia.Controls.Control
                    {
                    }
                }
                """;
            var platformAssemblies =
                ((string?)AppContext.GetData(
                    "TRUSTED_PLATFORM_ASSEMBLIES"))?
                    .Split(Path.PathSeparator) ?? [];
            return CSharpCompilation.Create(
                "CodeActionResolveTests",
                [CSharpSyntaxTree.ParseText(source)],
                platformAssemblies.Select(static path =>
                    MetadataReference.CreateFromFile(path)),
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary));
        }
    }

    private sealed class NullClient : IAkburaLspClient
    {
        public Task NotifyAsync<TParams>(
            string method,
            TParams parameters,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<TResult?> RequestAsync<TParams, TResult>(
            string method,
            TParams parameters,
            CancellationToken cancellationToken) =>
            Task.FromResult(default(TResult));
    }

    private sealed class NullLogger : IAkburaServerLogger
    {
        public void Log(
            AkburaServerLogLevel level,
            string message,
            Exception? exception = null)
        {
        }

        public void Dispose()
        {
        }
    }
}
