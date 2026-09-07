using Akbura.Diagnostics;
using Akbura.Workspaces.Projects;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Akbura.LanguageServer.UnitTests;

public sealed class DiagnosticsPublisherTests
{
    [Theory]
    [InlineData(AkburaDiagnosticPublisher.Auto, true)]
    [InlineData(AkburaDiagnosticPublisher.Generator, false)]
    [InlineData(AkburaDiagnosticPublisher.Workspace, true)]
    [InlineData(AkburaDiagnosticPublisher.Both, true)]
    [InlineData(AkburaDiagnosticPublisher.None, false)]
    public async Task PublisherPolicyOnlyControlsPresentation(
        AkburaDiagnosticPublisher publisher,
        bool shouldPublish)
    {
        using var fixture = new PublisherFixture(publisher);
        Assert.True(fixture.Workspace.CurrentSolution.TryGetDocumentContext(fixture.Uri, out var context));
        var diagnostics = fixture.Workspace.LanguageServices.Diagnostics.GetDiagnostics(
            context,
            new TextSpan(0, fixture.Text.Length));

        var report = Assert.IsType<FullDocumentDiagnosticReport>(await fixture.Publisher.GetDocumentReportAsync(
            fixture.Uri,
            previousResultId: null,
            CancellationToken.None));

        Assert.NotEmpty(diagnostics);
        Assert.Equal(shouldPublish ? diagnostics.Length : 0, report.Items.Length);
        Assert.All(report.Items, diagnostic =>
        {
            Assert.Equal("Akbura", diagnostic.Source);
            Assert.Equal("3", diagnostic.Data!.Value.GetProperty("akbura.document-version").GetString());
        });
    }

    [Fact]
    public async Task ChangingPublisherClearsAndRestoresCachedDiagnosticsAtTheSameTextVersion()
    {
        using var fixture = new PublisherFixture(AkburaDiagnosticPublisher.Workspace);
        var initial = Assert.IsType<FullDocumentDiagnosticReport>(await fixture.Publisher.GetDocumentReportAsync(
            fixture.Uri,
            previousResultId: null,
            CancellationToken.None));
        Assert.NotEmpty(initial.Items);

        fixture.ChangePublisher(AkburaDiagnosticPublisher.Generator);
        var suppressed = Assert.IsType<FullDocumentDiagnosticReport>(await fixture.Publisher.GetDocumentReportAsync(
            fixture.Uri,
            initial.ResultId,
            CancellationToken.None));
        Assert.Empty(suppressed.Items);
        Assert.NotEqual(initial.ResultId, suppressed.ResultId);

        fixture.ChangePublisher(AkburaDiagnosticPublisher.Workspace);
        var restored = Assert.IsType<FullDocumentDiagnosticReport>(await fixture.Publisher.GetDocumentReportAsync(
            fixture.Uri,
            suppressed.ResultId,
            CancellationToken.None));
        Assert.Equal(initial.Items.Select(GetLogicalId), restored.Items.Select(GetLogicalId));
        Assert.IsType<UnchangedDocumentDiagnosticReport>(await fixture.Publisher.GetDocumentReportAsync(
            fixture.Uri,
            restored.ResultId,
            CancellationToken.None));
    }

    [Fact]
    public async Task CancelledPublicationDoesNotReplaceTheLastCompleteReport()
    {
        using var fixture = new PublisherFixture(AkburaDiagnosticPublisher.Workspace);
        var initial = Assert.IsType<FullDocumentDiagnosticReport>(await fixture.Publisher.GetDocumentReportAsync(
            fixture.Uri,
            previousResultId: null,
            CancellationToken.None));
        using var cancellation = new CancellationTokenSource();
        fixture.Diagnostics.BeforeSemanticReturn = cancellation.Cancel;

        await Assert.ThrowsAsync<OperationCanceledException>(() => fixture.Publisher.PublishSemanticAsync(
            fixture.Uri,
            cancellation.Token));

        Assert.IsType<UnchangedDocumentDiagnosticReport>(await fixture.Publisher.GetDocumentReportAsync(
            fixture.Uri,
            initial.ResultId,
            CancellationToken.None));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PullRejectsStaleCompletedDiagnosticsInsteadOfReturningAnOlderCachedResult(bool workspaceReport)
    {
        using var fixture = new PublisherFixture(AkburaDiagnosticPublisher.Workspace);
        var initial = Assert.IsType<FullDocumentDiagnosticReport>(await fixture.Publisher.GetDocumentReportAsync(
            fixture.Uri,
            previousResultId: null,
            CancellationToken.None));
        Assert.NotEmpty(initial.Items);
        fixture.ChangeDocument(version: 4);
        fixture.Diagnostics.BeforeSemanticReturn = () => fixture.ChangeDocument(version: 5);
        Assert.NotNull(initial.ResultId);

        var exception = await Assert.ThrowsAsync<AkburaProtocolException>(() => workspaceReport
            ? fixture.Publisher.GetWorkspaceReportAsync(
                new Dictionary<string, string> { [fixture.Uri.AbsoluteUri] = initial.ResultId },
                CancellationToken.None)
            : fixture.Publisher.GetDocumentReportAsync(fixture.Uri, initial.ResultId, CancellationToken.None));

        Assert.Equal(LspErrorCodes.ContentModified, exception.Code);
        var current = Assert.IsType<FullDocumentDiagnosticReport>(await fixture.Publisher.GetDocumentReportAsync(
            fixture.Uri,
            initial.ResultId,
            CancellationToken.None));
        Assert.NotEmpty(current.Items);
        Assert.All(current.Items, diagnostic => Assert.Equal(
            "5",
            diagnostic.Data!.Value.GetProperty("akbura.document-version").GetString()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelledPullDoesNotReturnOrDiscardAnExistingCompleteReport(bool workspaceReport)
    {
        using var fixture = new PublisherFixture(AkburaDiagnosticPublisher.Workspace);
        var initial = Assert.IsType<FullDocumentDiagnosticReport>(await fixture.Publisher.GetDocumentReportAsync(
            fixture.Uri,
            previousResultId: null,
            CancellationToken.None));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.NotNull(initial.ResultId);

        await Assert.ThrowsAsync<OperationCanceledException>(() => workspaceReport
            ? fixture.Publisher.GetWorkspaceReportAsync(
                new Dictionary<string, string> { [fixture.Uri.AbsoluteUri] = initial.ResultId },
                cancellation.Token)
            : fixture.Publisher.GetDocumentReportAsync(fixture.Uri, initial.ResultId, cancellation.Token));

        Assert.IsType<UnchangedDocumentDiagnosticReport>(await fixture.Publisher.GetDocumentReportAsync(
            fixture.Uri,
            initial.ResultId,
            CancellationToken.None));
    }

    private static string? GetLogicalId(Protocol.Diagnostic diagnostic) =>
        diagnostic.Data!.Value.GetProperty("akbura.logical-id").GetString();

    private sealed class PublisherFixture : IDisposable
    {
        private readonly ProjectContext _project;
        private readonly AkburaServerState _state;
        private readonly AkburaServerLifetime _lifetime;
        private readonly NullLogger _logger;
        private readonly AkburaParentProcessMonitor _monitor;

        public PublisherFixture(AkburaDiagnosticPublisher publisher)
        {
            _project = new ProjectContext(
                ProjectId.CreateNewId(),
                projectFilePath: string.Empty,
                projectDirectory: Environment.CurrentDirectory,
                rootNamespace: "Diagnostics",
                CSharpCompilation.Create("Diagnostics"),
                ImmutableArray<ProjectReference>.Empty,
                publisher);
            Workspace = new AkburaWorkspace(_project);
            Uri = new Uri(Path.GetFullPath("DiagnosticView.akbura"));
            Text = SourceText.From("// 😀\r\n<UnknownControl Missing=");
            var context = Workspace.OpenOrChangeDocumentContext(Uri, Text);
            var openDocument = new AkburaOpenDocument(
                Uri,
                "akbura",
                3,
                Text,
                AkburaSyntacticDocument.Parse(Text, Uri.LocalPath),
                context.Project.Id,
                context.Document.Id,
                Text);
            var snapshot = AkburaServerSnapshot.Create(Workspace) with
            {
                OpenDocuments = ImmutableDictionary.Create<Uri, AkburaOpenDocument>(AkburaUriComparer.Instance)
                    .Add(Uri, openDocument),
                ClientCapabilities = AkburaClientCapabilities.Default with
                {
                    SupportsPullDiagnostics = true,
                },
            };
            _state = new AkburaServerState(snapshot);
            _lifetime = new AkburaServerLifetime();
            _logger = new NullLogger();
            _monitor = new AkburaParentProcessMonitor(_lifetime, _logger);
            var services = new AkburaLanguageServerServices(
                Workspace,
                new NullClient(),
                _logger,
                new Utf16PositionConverter(),
                _lifetime,
                _monitor,
                AkburaServerOptions.Parse([]));
            Diagnostics = new ControlledDiagnosticService(Workspace.LanguageServices.Diagnostics);
            Publisher = new AkburaDiagnosticsPublisher(_state, services, Diagnostics);
        }

        public AkburaWorkspace Workspace { get; }

        public AkburaDiagnosticsPublisher Publisher { get; }

        public ControlledDiagnosticService Diagnostics { get; }

        public Uri Uri { get; }

        public SourceText Text { get; }

        public void ChangeDocument(int version)
        {
            var text = SourceText.From("// Version " + version + "\r\n" + Text);
            var context = Workspace.OpenOrChangeDocumentContext(Uri, text);
            var document = _state.Current.OpenDocuments[Uri] with
            {
                Version = version,
                Text = text,
                SyntacticDocument = AkburaSyntacticDocument.Parse(text, Uri.LocalPath),
                DocumentId = context.Document.Id,
            };
            _state.Publish(_state.Current.Next(Workspace.CurrentSolution) with
            {
                OpenDocuments = _state.Current.OpenDocuments.SetItem(Uri, document),
            });
        }

        public void ChangePublisher(AkburaDiagnosticPublisher publisher)
        {
            Workspace.AddOrUpdateProject(new ProjectContext(
                _project.RoslynProjectId,
                _project.ProjectFilePath,
                _project.ProjectDirectory,
                _project.RootNamespace,
                _project.CSharpCompilation,
                _project.ProjectReferences,
                publisher));
            _state.Publish(_state.Current.Next(Workspace.CurrentSolution));
        }

        public void Dispose()
        {
            _monitor.Dispose();
            _lifetime.Dispose();
            Workspace.Dispose();
            _logger.Dispose();
        }
    }

    private sealed class ControlledDiagnosticService(IAkburaDiagnosticService inner) : IAkburaDiagnosticService
    {
        public Action? BeforeSemanticReturn { get; set; }

        public ImmutableArray<AkburaDiagnosticSpan> GetSyntacticDiagnostics(
            AkburaSyntacticDocument document,
            TextSpan requestedSpan,
            CancellationToken cancellationToken = default) =>
            inner.GetSyntacticDiagnostics(document, requestedSpan, cancellationToken);

        public ImmutableArray<AkburaDiagnosticSpan> GetDiagnostics(
            AkburaDocumentContext context,
            TextSpan requestedSpan,
            CancellationToken cancellationToken = default)
        {
            var diagnostics = inner.GetDiagnostics(context, requestedSpan, cancellationToken);
            var callback = BeforeSemanticReturn;
            BeforeSemanticReturn = null;
            callback?.Invoke();
            return diagnostics;
        }
    }

    private sealed class NullClient : IAkburaLspClient
    {
        public Task NotifyAsync<TParams>(string method, TParams parameters, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<TResult?> RequestAsync<TParams, TResult>(
            string method,
            TParams parameters,
            CancellationToken cancellationToken) =>
            Task.FromResult(default(TResult));
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
