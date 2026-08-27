using Akbura.LanguageServer.Hosting;
using Akbura.LanguageServer.Protocol;
using Akbura.LanguageServer.Protocol.Serialization;
using StreamJsonRpc;
using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text.Json;

namespace Akbura.LanguageServer.IntegrationTests;

public sealed class SemanticProjectLifecycleTests
{
    private const string ComponentPrefixSource =
        "using Avalonia.Controls;\r\n" +
        "\r\n" +
        "state int count = 0;\r\n" +
        "\r\n" +
        "<B";

    private const string CountCompletionSource =
        "using Avalonia.Controls;\r\n" +
        "\r\n" +
        "state int count = 0;\r\n" +
        "\r\n" +
        "<Button Content={cou}/>";

    private const string CountDefinitionSource =
        "using Avalonia.Controls;\r\n" +
        "\r\n" +
        "state int count = 0;\r\n" +
        "\r\n" +
        "<Button Content={count}/>";

    private const string StyleSource =
        "@using Avalonia.Controls;\r\n" +
        "\r\n" +
        "Control.card {\r\n" +
        "    Ma\r\n" +
        "}";

    [Fact]
    public async Task NestedProjectProvidesSemanticLanguageFeatures()
    {
        await using var fixture = await LanguageServerFixture.CreateAsync(
            failWatcherRegistration: false,
            supportsSemanticTokensRefresh: true);
        await fixture.OpenAsync(
            fixture.ComponentUri,
            "akbura",
            ComponentPrefixSource,
            version: 1);

        var componentCompletion = await fixture.WaitForCompletionAsync(
            fixture.ComponentUri,
            PositionAfter(ComponentPrefixSource, "<B"),
            "Border");
        Assert.Contains(
            componentCompletion.Items,
            static item => item.Label == "Button");

        await fixture.ChangeAsync(
            fixture.ComponentUri,
            CountCompletionSource,
            version: 2);
        await fixture.WaitForCompletionAsync(
            fixture.ComponentUri,
            PositionAfter(CountCompletionSource, "{cou"),
            "count");

        await fixture.ChangeAsync(
            fixture.ComponentUri,
            CountDefinitionSource,
            version: 3);
        var definitions = await fixture.WaitForDefinitionAsync(
            fixture.ComponentUri,
            PositionInsideLast(CountDefinitionSource, "count"),
            fixture.ComponentUri.AbsoluteUri);
        var definition = Assert.Single(definitions);
        Assert.Equal(
            fixture.ComponentUri.AbsoluteUri,
            definition.TargetUri);
        Assert.Equal(2, definition.TargetSelectionRange.Start.Line);

        await fixture.OpenAsync(
            fixture.StylesUri,
            "akcss",
            StyleSource,
            version: 1);
        await fixture.WaitForCompletionAsync(
            fixture.StylesUri,
            PositionAfter(StyleSource, "    Ma"),
            "Margin");

        await fixture.Notifications.SemanticTokensRefreshRequested.Task
            .WaitAsync(fixture.CancellationToken);
        await fixture.Notifications.WaitForProjectLoadEndCountAsync(
            expectedCount: 1,
            fixture.CancellationToken);
        await Task.Delay(
            TimeSpan.FromMilliseconds(750),
            fixture.CancellationToken);
        Assert.Equal(
            1,
            fixture.Notifications.ProjectLoadBeginCount);
        Assert.False(
            fixture.Notifications.HasProjectLoadError);

        await fixture.NotifyWatchedFileChangedAsync(
            fixture.StylesUri);
        await fixture.Notifications.WaitForProjectLoadEndCountAsync(
            expectedCount: 2,
            fixture.CancellationToken);
        Assert.False(
            fixture.Notifications.HasProjectLoadError);

        await fixture.ShutdownAsync();
    }

    [Fact]
    public async Task WatcherRegistrationFailureDoesNotBlockProjectLoading()
    {
        await using var fixture = await LanguageServerFixture.CreateAsync(
            failWatcherRegistration: true,
            supportsSemanticTokensRefresh: false);
        await fixture.OpenAsync(
            fixture.ComponentUri,
            "akbura",
            ComponentPrefixSource,
            version: 1);

        await fixture.Notifications.RegistrationRequested.Task
            .WaitAsync(fixture.CancellationToken);
        await fixture.WaitForCompletionAsync(
            fixture.ComponentUri,
            PositionAfter(ComponentPrefixSource, "<B"),
            "Border");
        await fixture.ShutdownAsync();
    }

    private static Position PositionAfter(
        string source,
        string marker)
    {
        var index = source.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(index >= 0, $"Marker '{marker}' was not found.");
        return ToPosition(source, index + marker.Length);
    }

    private static Position PositionInsideLast(
        string source,
        string marker)
    {
        var index = source.LastIndexOf(marker, StringComparison.Ordinal);
        Assert.True(index >= 0, $"Marker '{marker}' was not found.");
        return ToPosition(source, index + 1);
    }

    private static Position ToPosition(string source, int offset)
    {
        var line = 0;
        var character = 0;
        for (var index = 0; index < offset; index++)
        {
            if (source[index] == '\n')
            {
                line++;
                character = 0;
            }
            else if (source[index] != '\r')
            {
                character++;
            }
        }

        return new Position
        {
            Line = line,
            Character = character,
        };
    }

    private sealed class LanguageServerFixture : IAsyncDisposable
    {
        private readonly NamedPipeServerStream _serverStream;
        private readonly NamedPipeClientStream _clientStream;
        private readonly HeaderDelimitedMessageHandler _messageHandler;
        private readonly JsonRpc _rpc;
        private readonly CancellationTokenSource _cancellation;
        private readonly Task<int> _hostTask;
        private bool _stopped;

        private LanguageServerFixture(
            DirectoryInfo repositoryRoot,
            NamedPipeServerStream serverStream,
            NamedPipeClientStream clientStream,
            HeaderDelimitedMessageHandler messageHandler,
            JsonRpc rpc,
            ClientNotifications notifications,
            CancellationTokenSource cancellation,
            Task<int> hostTask)
        {
            _serverStream = serverStream;
            _clientStream = clientStream;
            _messageHandler = messageHandler;
            _rpc = rpc;
            Notifications = notifications;
            _cancellation = cancellation;
            _hostTask = hostTask;

            var projectDirectory = Path.Combine(
                repositoryRoot.FullName,
                "Akbura.Previewer",
                "Akbura.Previewer");
            WorkspaceUri = new Uri(Path.Combine(
                repositoryRoot.FullName,
                "Akbura.Previewer"));
            ComponentUri = new Uri(Path.Combine(
                projectDirectory,
                "Component.akbura"));
            StylesUri = new Uri(Path.Combine(
                projectDirectory,
                "Styles.akcss"));
        }

        public Uri WorkspaceUri { get; }

        public Uri ComponentUri { get; }

        public Uri StylesUri { get; }

        public ClientNotifications Notifications { get; }

        public CancellationToken CancellationToken => _cancellation.Token;

        public static async Task<LanguageServerFixture> CreateAsync(
            bool failWatcherRegistration,
            bool supportsSemanticTokensRefresh)
        {
            var repositoryRoot = FindRepositoryRoot();
            var pipeName = "akbura-semantic-lsp-" +
                Guid.NewGuid().ToString("N");
            var serverStream = new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                maxNumberOfServerInstances: 1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);
            var clientStream = new NamedPipeClientStream(
                ".",
                pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);
            var connectTask = serverStream.WaitForConnectionAsync();
            await clientStream.ConnectAsync();
            await connectTask;

            var cancellation = new CancellationTokenSource(
                TimeSpan.FromSeconds(90));
            var hostTask = AkburaLanguageServerHost.RunAsync(
                serverStream,
                serverStream,
                AkburaServerOptions.Parse(
                [
                    "--stdio",
                    "--log-level",
                    "trace",
                ]),
                TextWriter.Null,
                cancellation.Token);
            var formatter = new SystemTextJsonFormatter
            {
                JsonSerializerOptions = AkburaProtocolJson.CreateOptions(),
            };
            var messageHandler = new HeaderDelimitedMessageHandler(
                clientStream,
                clientStream,
                formatter);
            var rpc = new JsonRpc(messageHandler);
            var notifications = new ClientNotifications(
                failWatcherRegistration);
            rpc.AddLocalRpcTarget(
                notifications,
                new JsonRpcTargetOptions
                {
                    UseSingleObjectParameterDeserialization = false,
                });
            rpc.StartListening();

            var fixture = new LanguageServerFixture(
                repositoryRoot,
                serverStream,
                clientStream,
                messageHandler,
                rpc,
                notifications,
                cancellation,
                hostTask);
            await rpc.InvokeWithParameterObjectAsync<InitializeResult>(
                LspMethods.Initialize,
                new InitializeParams
                {
                    RootUri = fixture.WorkspaceUri.AbsoluteUri,
                    Capabilities = new InitializeClientCapabilities
                    {
                        Workspace = new WorkspaceClientCapabilities
                        {
                            DidChangeWatchedFiles =
                                new DidChangeWatchedFilesClientCapabilities
                                {
                                    DynamicRegistration =
                                        failWatcherRegistration,
                                },
                            SemanticTokens =
                                new SemanticTokensWorkspaceClientCapabilities
                                {
                                    RefreshSupport =
                                        supportsSemanticTokensRefresh,
                                },
                        },
                    },
                },
                cancellation.Token);
            await rpc.NotifyWithParameterObjectAsync(
                LspMethods.Initialized,
                new InitializedParams());
            return fixture;
        }

        public Task OpenAsync(
            Uri uri,
            string languageId,
            string source,
            int version)
        {
            return _rpc.NotifyWithParameterObjectAsync(
                LspMethods.DidOpen,
                new DidOpenTextDocumentParams
                {
                    TextDocument = new TextDocumentItem
                    {
                        Uri = uri.AbsoluteUri,
                        LanguageId = languageId,
                        Version = version,
                        Text = source,
                    },
                });
        }

        public async Task ChangeAsync(
            Uri uri,
            string source,
            int version)
        {
            var diagnosticsPublished = Notifications.ExpectDiagnostics(
                uri.AbsoluteUri,
                version);
            await _rpc.NotifyWithParameterObjectAsync(
                LspMethods.DidChange,
                new DidChangeTextDocumentParams
                {
                    TextDocument = new VersionedTextDocumentIdentifier
                    {
                        Uri = uri.AbsoluteUri,
                        Version = version,
                    },
                    ContentChanges =
                    [
                        new TextDocumentContentChangeEvent
                        {
                            Text = source,
                        },
                    ],
                });
            await diagnosticsPublished.WaitAsync(CancellationToken);
        }

        public Task NotifyWatchedFileChangedAsync(Uri uri)
        {
            return _rpc.NotifyWithParameterObjectAsync(
                LspMethods.DidChangeWatchedFiles,
                new DidChangeWatchedFilesParams
                {
                    Changes =
                    [
                        new FileEvent
                        {
                            Uri = uri.AbsoluteUri,
                            Type = 2,
                        },
                    ],
                });
        }

        public async Task<CompletionList> WaitForCompletionAsync(
            Uri uri,
            Position position,
            string expectedLabel)
        {
            CompletionList? last = null;
            while (!CancellationToken.IsCancellationRequested)
            {
                last = await _rpc.InvokeWithParameterObjectAsync<
                    CompletionList>(
                        LspMethods.Completion,
                        new CompletionParams
                        {
                            TextDocument = new TextDocumentIdentifier
                            {
                                Uri = uri.AbsoluteUri,
                            },
                            Position = position,
                            Context = new CompletionContext
                            {
                                TriggerKind = 1,
                            },
                        },
                        CancellationToken);
                if (last.Items.Any(item =>
                        item.Label.Equals(
                            expectedLabel,
                            StringComparison.Ordinal)))
                {
                    return last;
                }

                await Task.Delay(250, CancellationToken);
            }

            var labels = last == null
                ? string.Empty
                : string.Join(
                    ", ",
                    last.Items.Select(static item => item.Label));
            throw new Xunit.Sdk.XunitException(
                $"Completion '{expectedLabel}' was not returned. " +
                $"Last labels: {labels}.");
        }

        public async Task<LocationLink[]> WaitForDefinitionAsync(
            Uri uri,
            Position position,
            string expectedTargetUri)
        {
            using var timeout = CancellationTokenSource
                .CreateLinkedTokenSource(CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            LocationLink[]? last = null;
            try
            {
                while (!timeout.IsCancellationRequested)
                {
                    last = await _rpc.InvokeWithParameterObjectAsync<
                        LocationLink[]?>(
                            LspMethods.Definition,
                            new DefinitionParams
                            {
                                TextDocument = new TextDocumentIdentifier
                                {
                                    Uri = uri.AbsoluteUri,
                                },
                                Position = position,
                            },
                            timeout.Token);
                    var match = last?.FirstOrDefault(definition =>
                        string.Equals(
                            definition.TargetUri,
                            expectedTargetUri,
                            StringComparison.OrdinalIgnoreCase));
                    if (match != null)
                    {
                        return [match];
                    }

                    await Task.Delay(250, timeout.Token);
                }
            }
            catch (OperationCanceledException)
                when (timeout.IsCancellationRequested &&
                      !CancellationToken.IsCancellationRequested)
            {
            }

            var targets = last == null
                ? string.Empty
                : string.Join(", ", last.Select(static item => item.TargetUri));
            throw new Xunit.Sdk.XunitException(
                $"Definition '{expectedTargetUri}' was not returned. " +
                $"Last targets: {targets}.");
        }

        public async Task ShutdownAsync()
        {
            if (_stopped)
            {
                return;
            }

            await _rpc.InvokeWithCancellationAsync<object?>(
                LspMethods.Shutdown,
                Array.Empty<object>(),
                CancellationToken);
            await _rpc.NotifyAsync(
                LspMethods.Exit,
                Array.Empty<object>());
            Assert.Equal(
                0,
                await _hostTask.WaitAsync(CancellationToken));
            _stopped = true;
        }

        public async ValueTask DisposeAsync()
        {
            if (!_stopped)
            {
                _cancellation.Cancel();
                try
                {
                    await _hostTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
            }

            _rpc.Dispose();
            await _messageHandler.DisposeAsync();
            _clientStream.Dispose();
            _serverStream.Dispose();
            _cancellation.Dispose();
        }

        private static DirectoryInfo FindRepositoryRoot()
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
                 directory != null;
                 directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(
                        directory.FullName,
                        "Akbura.slnx")))
                {
                    return directory;
                }
            }

            throw new DirectoryNotFoundException(
                "The Akbura repository root was not found.");
        }
    }

    private sealed class ClientNotifications
    {
        private readonly bool _failWatcherRegistration;

        private readonly ConcurrentDictionary<
            (string Uri, int Version),
            TaskCompletionSource<bool>> _diagnosticWaiters = new();
        private readonly ConcurrentQueue<ShowMessageParams> _messages = new();
        private int _projectLoadBeginCount;
        private int _projectLoadEndCount;

        public ClientNotifications(bool failWatcherRegistration)
        {
            _failWatcherRegistration = failWatcherRegistration;
        }

        public Task ExpectDiagnostics(string uri, int version)
        {
            var completion = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            if (!_diagnosticWaiters.TryAdd((uri, version), completion))
            {
                throw new InvalidOperationException(
                    $"Diagnostics waiter already exists for " +
                    $"'{uri}' version {version}.");
            }

            return completion.Task;
        }

        public TaskCompletionSource<bool> RegistrationRequested { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool>
            SemanticTokensRefreshRequested { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int ProjectLoadBeginCount =>
            Volatile.Read(ref _projectLoadBeginCount);

        public bool HasProjectLoadError =>
            _messages.Any(static message => message.Type == 1);

        public async Task WaitForProjectLoadEndCountAsync(
            int expectedCount,
            CancellationToken cancellationToken)
        {
            while (Volatile.Read(ref _projectLoadEndCount) <
                   expectedCount)
            {
                await Task.Delay(
                        TimeSpan.FromMilliseconds(50),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        [JsonRpcMethod(
            LspMethods.RegisterCapability,
            UseSingleObjectParameterDeserialization = true)]
        public object? RegisterCapability(RegistrationParams parameters)
        {
            RegistrationRequested.TrySetResult(true);
            if (_failWatcherRegistration)
            {
                throw new InvalidOperationException(
                    "Simulated dynamic registration failure.");
            }

            return null;
        }

        [JsonRpcMethod(LspMethods.SemanticTokensRefresh)]
        public object? RefreshSemanticTokens()
        {
            SemanticTokensRefreshRequested.TrySetResult(true);
            return null;
        }

        [JsonRpcMethod(
            LspMethods.PublishDiagnostics,
            UseSingleObjectParameterDeserialization = true)]
        public void PublishDiagnostics(PublishDiagnosticsParams parameters)
        {
            if (parameters.Version is { } version &&
                _diagnosticWaiters.TryRemove(
                    (parameters.Uri, version),
                    out var completion))
            {
                completion.TrySetResult(true);
            }
        }

        [JsonRpcMethod(
            LspMethods.Progress,
            UseSingleObjectParameterDeserialization = true)]
        public void Progress(ProgressParams parameters)
        {
            if (parameters.Value.ValueKind != JsonValueKind.Object ||
                !parameters.Value.TryGetProperty(
                    "kind",
                    out var kindProperty))
            {
                return;
            }

            switch (kindProperty.GetString())
            {
                case "begin":
                    Interlocked.Increment(
                        ref _projectLoadBeginCount);
                    break;
                case "end":
                    Interlocked.Increment(
                        ref _projectLoadEndCount);
                    break;
            }
        }

        [JsonRpcMethod(
            LspMethods.ShowMessage,
            UseSingleObjectParameterDeserialization = true)]
        public void ShowMessage(ShowMessageParams parameters)
        {
            _messages.Enqueue(parameters);
        }
    }
}
