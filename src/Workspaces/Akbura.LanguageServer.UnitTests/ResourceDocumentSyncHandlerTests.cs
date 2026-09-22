using Akbura.LanguageServer.Dispatch;
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
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;

namespace Akbura.LanguageServer.UnitTests;

public sealed class ResourceDocumentSyncHandlerTests
{
    private const string DiskSource =
        "<ResourceDictionary " +
        "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\r\n" +
        "    <SolidColorBrush x:Key=\"DiskKey\">#000000</SolidColorBrush>\r\n" +
        "</ResourceDictionary>";

    private const string OpenSource =
        "<ResourceDictionary " +
        "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\r\n" +
        "    <SolidColorBrush x:Key=\"OpenKey\">#ffffff</SolidColorBrush>\r\n" +
        "</ResourceDictionary>";

    [Fact]
    public async Task UnsavedBufferOverridesProjectAndCloseRestoresDiskText()
    {
        await using var fixture = new SyncFixture();

        await fixture.OpenAsync(OpenSource, version: 1);

        Assert.Empty(fixture.State.Current.OpenDocuments);
        Assert.Equal(
            OpenSource,
            fixture.GetCurrentResourceText());

        const string changedSource =
            "<ResourceDictionary " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\r\n" +
            "    <SolidColorBrush x:Key=\"LiveKey\">#ffffff</SolidColorBrush>\r\n" +
            "</ResourceDictionary>";
        await fixture.ChangeAsync(
            oldValue: "OpenKey",
            newValue: "LiveKey",
            version: 2);

        Assert.Equal(
            changedSource,
            fixture.GetCurrentResourceText());

        await fixture.CloseAsync();

        Assert.Empty(fixture.State.Current.OpenResourceDocuments);
        Assert.Equal(
            DiskSource,
            fixture.GetCurrentResourceText());
    }

    [Fact]
    public async Task SaveMakesOpenTextThePersistedCloseFallback()
    {
        await using var fixture = new SyncFixture();
        await fixture.OpenAsync(OpenSource, version: 1);
        await fixture.SaveAsync(OpenSource);
        await fixture.CloseAsync();

        Assert.Equal(
            OpenSource,
            fixture.GetCurrentResourceText());
    }

    [Fact]
    public async Task StaleOutOfOrderChangeDoesNotReplaceNewerText()
    {
        await using var fixture = new SyncFixture();
        await fixture.OpenAsync(OpenSource, version: 1);
        await fixture.ChangeAsync(
            oldValue: "OpenKey",
            newValue: "NewerKey",
            version: 3);

        await fixture.ChangeAsync(
            oldValue: "NewerKey",
            newValue: "StaleKey",
            version: 2);

        var open = fixture.State.Current
            .OpenResourceDocuments[fixture.ResourceUri];
        Assert.Equal(3, open.Version);
        Assert.Contains("NewerKey", open.Text.ToString());
        Assert.DoesNotContain("StaleKey", open.Text.ToString());
        Assert.Contains("NewerKey", fixture.GetCurrentResourceText());
        Assert.DoesNotContain(
            "StaleKey",
            fixture.GetCurrentResourceText());
        Assert.Contains(
            "NewerKey",
            fixture.GetCurrentResourceText(fixture.SecondProjectId));
        Assert.DoesNotContain(
            "StaleKey",
            fixture.GetCurrentResourceText(fixture.SecondProjectId));
    }

    [Fact]
    public async Task CancelledMultiProjectUpdateDoesNotDivergeProjects()
    {
        await using var fixture = new SyncFixture();
        await fixture.OpenAsync(OpenSource, version: 1);
        var current = fixture.State.Current
            .OpenResourceDocuments[fixture.ResourceUri];
        var cancelled = current with
        {
            Version = 2,
            Text = SourceText.From(
                OpenSource.Replace(
                    "OpenKey",
                    "CancelledKey",
                    StringComparison.Ordinal)),
        };
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            AkburaResourceDocumentSynchronization.ApplyOpenText(
                fixture.Workspace,
                cancelled,
                changes: null,
                cancellation.Token));

        Assert.Equal(OpenSource, fixture.GetCurrentResourceText());
        Assert.Equal(
            OpenSource,
            fixture.GetCurrentResourceText(fixture.SecondProjectId));
    }

    [Fact]
    public void ProjectLoadCanAttachAnAlreadyOpenResourceBuffer()
    {
        using var workspace = new AkburaWorkspace();
        var uri = SyncFixture.CreateResourceUri();
        var open = new AkburaOpenResourceDocument(
            uri,
            Version: 1,
            SourceText.From(OpenSource),
            ImmutableArray<AkburaOpenResourceDocumentProject>.Empty);
        var projectId = SyncFixture.AddProjectResource(
            workspace,
            uri,
            DiskSource);

        var attached = open with
        {
            Projects = AkburaResourceDocumentSynchronization.FindProjects(
                workspace.CurrentSolution,
                uri),
        };
        AkburaResourceDocumentSynchronization.ApplyOpenText(
            workspace,
            attached,
            changes: null,
            CancellationToken.None);

        var project = workspace.CurrentSolution
            .GetRequiredProject(projectId);
        Assert.Equal(
            OpenSource,
            Assert.Single(project.ResourceDocuments.Values)
                .Text
                .ToString());
    }

    [Theory]
    [InlineData("App.axaml", true)]
    [InlineData("Theme.AXAML", true)]
    [InlineData("Component.akbura", true)]
    [InlineData("Styles.akcss", true)]
    [InlineData("Readme.md", false)]
    public void WatchedFileReloadIncludesAvaloniaResources(string fileName, bool expected)
    {
        var uri = new Uri(Path.Combine(
            Path.GetTempPath(),
            "Akbura.LanguageServer.Tests",
            fileName));

        Assert.Equal(
            expected,
            AkburaProjectLoadCoordinator.IsReloadRelevant(
                uri.AbsoluteUri));
    }

    private sealed class SyncFixture : IAsyncDisposable
    {
        private readonly AkburaWorkspace _workspace = new();
        private readonly AkburaServerLifetime _lifetime = new();
        private readonly NullLogger _logger = new();
        private readonly AkburaParentProcessMonitor _monitor;

        public SyncFixture()
        {
            ResourceUri = CreateResourceUri();
            ProjectId = AddProjectResource(
                _workspace,
                ResourceUri,
                DiskSource,
                "ResourceSyncTests.Primary");
            SecondProjectId = AddProjectResource(
                _workspace,
                ResourceUri,
                DiskSource,
                "ResourceSyncTests.Secondary");
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
            State = new AkburaServerState(
                AkburaServerSnapshot.Create(_workspace));
            Queue = new AkburaRequestExecutionQueue(
                new AkburaLspHandlerRegistry(
                [
                    new ResourceDocumentDidOpenHandler(),
                    new ResourceDocumentDidChangeHandler(),
                    new ResourceDocumentDidSaveHandler(),
                    new ResourceDocumentDidCloseHandler(),
                ]),
                new AkburaRequestContextFactory(State, services),
                State,
                _logger);
        }

        public Uri ResourceUri { get; }

        public AkburaProjectId ProjectId { get; }

        public AkburaProjectId SecondProjectId { get; }

        public AkburaWorkspace Workspace => _workspace;

        public AkburaServerState State { get; }

        public AkburaRequestExecutionQueue Queue { get; }

        public Task OpenAsync(string source, int version)
        {
            return Queue.ExecuteAsync<object?>(
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

        public Task ChangeAsync(string oldValue, string newValue, int version)
        {
            var text = State.Current
                .OpenResourceDocuments[ResourceUri]
                .Text;
            var start = text.ToString().IndexOf(
                oldValue,
                StringComparison.Ordinal);
            Assert.True(start >= 0);
            var startPosition = text.Lines.GetLinePosition(start);
            var endPosition = text.Lines.GetLinePosition(
                start + oldValue.Length);

            return Queue.ExecuteAsync<object?>(
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
                            Range = new Akbura.LanguageServer.Protocol.Range
                            {
                                Start = new Position
                                {
                                    Line = startPosition.Line,
                                    Character = startPosition.Character,
                                },
                                End = new Position
                                {
                                    Line = endPosition.Line,
                                    Character = endPosition.Character,
                                },
                            },
                            RangeLength = oldValue.Length,
                            Text = newValue,
                        },
                    ],
                },
                CancellationToken.None);
        }

        public Task SaveAsync(string source)
        {
            return Queue.ExecuteAsync<object?>(
                LspMethods.ResourceDocumentDidSave,
                new DidSaveTextDocumentParams
                {
                    TextDocument = new TextDocumentIdentifier
                    {
                        Uri = ResourceUri.AbsoluteUri,
                    },
                    Text = source,
                },
                CancellationToken.None);
        }

        public Task CloseAsync()
        {
            return Queue.ExecuteAsync<object?>(
                LspMethods.ResourceDocumentDidClose,
                new DidCloseTextDocumentParams
                {
                    TextDocument = new TextDocumentIdentifier
                    {
                        Uri = ResourceUri.AbsoluteUri,
                    },
                },
                CancellationToken.None);
        }

        public string GetCurrentResourceText()
        {
            return GetCurrentResourceText(ProjectId);
        }

        public string GetCurrentResourceText(AkburaProjectId projectId)
        {
            var project = _workspace.CurrentSolution
                .GetRequiredProject(projectId);
            return Assert.Single(project.ResourceDocuments.Values)
                .Text
                .ToString();
        }

        public async ValueTask DisposeAsync()
        {
            await Queue.DisposeAsync();
            _monitor.Dispose();
            _lifetime.Dispose();
            _workspace.Dispose();
            _logger.Dispose();
        }

        public static Uri CreateResourceUri()
        {
            return new Uri(Path.Combine(
                Path.GetTempPath(),
                "Akbura.LanguageServer.Tests",
                Guid.NewGuid().ToString("N"),
                "App.axaml"));
        }

        public static AkburaProjectId AddProjectResource(AkburaWorkspace workspace, Uri uri, string source, string assemblyName = "ResourceSyncTests")
        {
            var roslynProjectId = Microsoft.CodeAnalysis.ProjectId
                .CreateNewId("ResourceSyncTests");
            var compilation = CSharpCompilation.Create(
                assemblyName,
                options: new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary));
            var context = new ProjectContext(
                roslynProjectId,
                Path.Combine(
                    Path.GetDirectoryName(uri.LocalPath)!,
                    "ResourceSyncTests.csproj"),
                Path.GetDirectoryName(uri.LocalPath)!,
                assemblyName,
                compilation,
                ImmutableArray<ProjectReference>.Empty);
            var project = workspace.AddOrUpdateProject(context);
            var input = new ResourceDocumentInput(
                uri,
                uri.LocalPath,
                "App.axaml",
                ResourceAssemblyIdentity.Create(
                    compilation.Assembly.Identity),
                SourceText.From(source),
                VersionStamp.Create(),
                roslynProjectId.Id.ToString("N"),
                "net10.0");
            workspace.SynchronizeProjectResourceDocuments(
                project.Id,
                ImmutableArray.Create(input));
            return project.Id;
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
