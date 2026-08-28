using Akbura.LanguageServer.Hosting;
using Akbura.LanguageServer.Protocol;
using Akbura.LanguageServer.Protocol.Serialization;
using StreamJsonRpc;
using System.IO.Pipes;

namespace Akbura.LanguageServer.IntegrationTests;

public sealed class TypingLifecycleTests
{
    [Fact]
    public async Task TypingWorksSyntaxOnlyAndRejectsStaleVersions()
    {
        await using var fixture = await TypingServerFixture.CreateAsync();
        var uri = new Uri(
            Path.Combine(fixture.Root.FullName, "Component.akbura"));

        await fixture.OpenAsync(uri, version: 1, text: string.Empty);

        var pair = await fixture.Rpc.InvokeWithParameterObjectAsync<
            AkburaTypingResponse>(
                LspMethods.Typing,
                Request(uri, version: 1, line: 0, character: 0, "<"),
                fixture.Cancellation.Token);

        Assert.True(pair.Handled);
        Assert.False(pair.Stale);
        Assert.Equal("<>", Assert.Single(pair.Edits).NewText);
        Assert.Equal(1, pair.Position.Character);
        Assert.Equal("MarkupAnglePair", pair.Session?.Kind);
        Assert.True(pair.TriggerCompletion);

        var stale = await fixture.Rpc.InvokeWithParameterObjectAsync<
            AkburaTypingResponse>(
                LspMethods.Typing,
                Request(uri, version: 0, line: 0, character: 0, "{"),
                fixture.Cancellation.Token);

        Assert.True(stale.Stale);
        Assert.False(stale.Handled);
        Assert.Equal(1, stale.Version);
    }

    [Fact]
    public async Task TypingGrowsArbitraryRawStringDelimiter()
    {
        await using var fixture = await TypingServerFixture.CreateAsync();
        var uri = new Uri(
            Path.Combine(fixture.Root.FullName, "Component.akbura"));
        const string source =
            "state string text = \"\"\"\"\"\"\"\";";
        const int caret = 24;

        await fixture.OpenAsync(uri, version: 1, text: source);

        var result = await fixture.Rpc.InvokeWithParameterObjectAsync<
            AkburaTypingResponse>(
                LspMethods.Typing,
                Request(
                    uri,
                    version: 1,
                    line: 0,
                    character: caret,
                    "\""),
                fixture.Cancellation.Token);

        Assert.True(result.Handled);
        Assert.Equal(2, result.Edits.Length);
        Assert.All(result.Edits, edit => Assert.Equal("\"", edit.NewText));
        Assert.Equal("RawStringQuotes", result.Session?.Kind);
        Assert.Equal(5, result.Session?.RequiredDelimiterLength);
        Assert.Equal(caret + 1, result.Position.Character);
    }

    [Fact]
    public async Task TypingSessionSurvivesDidChangeAndCompletesTag()
    {
        await using var fixture = await TypingServerFixture.CreateAsync();
        var uri = new Uri(
            Path.Combine(fixture.Root.FullName, "Component.akbura"));

        await fixture.OpenAsync(uri, version: 1, text: string.Empty);
        var pair = await fixture.Rpc.InvokeWithParameterObjectAsync<
            AkburaTypingResponse>(
                LspMethods.Typing,
                Request(uri, version: 1, line: 0, character: 0, "<"),
                fixture.Cancellation.Token);

        Assert.NotNull(pair.Session);
        await fixture.Rpc.NotifyWithParameterObjectAsync(
            LspMethods.DidChange,
            new DidChangeTextDocumentParams
            {
                TextDocument = new VersionedTextDocumentIdentifier
                {
                    Uri = uri.AbsoluteUri,
                    Version = 2,
                },
                ContentChanges =
                [
                    new TextDocumentContentChangeEvent
                    {
                        Range = new Protocol.Range
                        {
                            Start = new Position(),
                            End = new Position(),
                        },
                        RangeLength = 0,
                        Text = "<Button>",
                    },
                ],
            });

        var session = new AkburaPairSessionDto
        {
            Kind = pair.Session.Kind,
            OpeningRange = pair.Session.OpeningRange,
            ClosingRange = new Protocol.Range
            {
                Start = new Position { Character = 7 },
                End = new Position { Character = 8 },
            },
            OpeningText = pair.Session.OpeningText,
            ClosingText = pair.Session.ClosingText,
            RequiredDelimiterLength =
                pair.Session.RequiredDelimiterLength,
            OuterLiteralDelimiterCount =
                pair.Session.OuterLiteralDelimiterCount,
        };
        var completion = await fixture.Rpc
            .InvokeWithParameterObjectAsync<AkburaTypingResponse>(
                LspMethods.Typing,
                Request(
                    uri,
                    version: 2,
                    line: 0,
                    character: 7,
                    ">",
                    session),
                fixture.Cancellation.Token);

        var edit = Assert.Single(completion.Edits);
        Assert.Equal("</Button>", edit.NewText);
        Assert.Equal(8, edit.Range.Start.Character);
        Assert.Equal(8, completion.Position.Character);
        Assert.Null(completion.Session);
    }

    private static AkburaTypingParams Request(
        Uri uri,
        int version,
        int line,
        int character,
        string text,
        AkburaPairSessionDto? session = null)
    {
        return new AkburaTypingParams
        {
            TextDocument = new VersionedTextDocumentIdentifier
            {
                Uri = uri.AbsoluteUri,
                Version = version,
            },
            Position = new Position
            {
                Line = line,
                Character = character,
            },
            Command = "type",
            Text = text,
            Session = session,
            Options = new FormattingOptions
            {
                TabSize = 4,
                InsertSpaces = true,
            },
        };
    }

    private sealed class TypingServerFixture : IAsyncDisposable
    {
        private readonly NamedPipeServerStream _serverStream;
        private readonly NamedPipeClientStream _clientStream;
        private readonly HeaderDelimitedMessageHandler _handler;
        private readonly Task<int> _hostTask;

        private TypingServerFixture(
            DirectoryInfo root,
            CancellationTokenSource cancellation,
            NamedPipeServerStream serverStream,
            NamedPipeClientStream clientStream,
            HeaderDelimitedMessageHandler handler,
            JsonRpc rpc,
            Task<int> hostTask)
        {
            Root = root;
            Cancellation = cancellation;
            _serverStream = serverStream;
            _clientStream = clientStream;
            _handler = handler;
            Rpc = rpc;
            _hostTask = hostTask;
        }

        public DirectoryInfo Root { get; }

        public CancellationTokenSource Cancellation { get; }

        public JsonRpc Rpc { get; }

        public static async Task<TypingServerFixture> CreateAsync()
        {
            var root = Directory.CreateTempSubdirectory(
                "akbura-typing-tests-");
            var cancellation = new CancellationTokenSource(
                TimeSpan.FromSeconds(20));
            var pipeName = "akbura-typing-" +
                Guid.NewGuid().ToString("N");
            var server = new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                maxNumberOfServerInstances: 1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);
            var client = new NamedPipeClientStream(
                ".",
                pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);
            var connectTask = server.WaitForConnectionAsync(
                cancellation.Token);
            await client.ConnectAsync(cancellation.Token);
            await connectTask;

            var hostTask = AkburaLanguageServerHost.RunAsync(
                server,
                server,
                AkburaServerOptions.Parse(["--stdio"]),
                TextWriter.Null,
                cancellation.Token);
            var formatter = new SystemTextJsonFormatter
            {
                JsonSerializerOptions =
                    AkburaProtocolJson.CreateOptions(),
            };
            var handler = new HeaderDelimitedMessageHandler(
                client,
                client,
                formatter);
            var rpc = new JsonRpc(handler);
            rpc.AddLocalRpcTarget(new NoOpClient());
            rpc.StartListening();

            await rpc.InvokeWithParameterObjectAsync<InitializeResult>(
                LspMethods.Initialize,
                new InitializeParams
                {
                    RootUri = new Uri(root.FullName).AbsoluteUri,
                    Capabilities = new InitializeClientCapabilities(),
                },
                cancellation.Token);
            await rpc.NotifyWithParameterObjectAsync(
                LspMethods.Initialized,
                new InitializedParams());

            return new TypingServerFixture(
                root,
                cancellation,
                server,
                client,
                handler,
                rpc,
                hostTask);
        }

        public Task OpenAsync(Uri uri, int version, string text)
        {
            return Rpc.NotifyWithParameterObjectAsync(
                LspMethods.DidOpen,
                new DidOpenTextDocumentParams
                {
                    TextDocument = new TextDocumentItem
                    {
                        Uri = uri.AbsoluteUri,
                        LanguageId = "akbura",
                        Version = version,
                        Text = text,
                    },
                });
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await Rpc.InvokeWithCancellationAsync<object?>(
                    LspMethods.Shutdown,
                    Array.Empty<object>(),
                    Cancellation.Token);
                await Rpc.NotifyAsync(
                    LspMethods.Exit,
                    Array.Empty<object>());
                await _hostTask.WaitAsync(Cancellation.Token);
            }
            finally
            {
                Rpc.Dispose();
                await _handler.DisposeAsync();
                _clientStream.Dispose();
                _serverStream.Dispose();
                Cancellation.Dispose();
                Root.Delete(recursive: true);
            }
        }
    }

    private sealed class NoOpClient
    {
        [JsonRpcMethod(
            LspMethods.PublishDiagnostics,
            UseSingleObjectParameterDeserialization = true)]
        public void PublishDiagnostics(PublishDiagnosticsParams parameters)
        {
        }
    }
}
