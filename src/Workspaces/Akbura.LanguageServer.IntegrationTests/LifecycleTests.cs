using Akbura.LanguageServer.Hosting;
using Akbura.LanguageServer.Protocol;
using Akbura.LanguageServer.Protocol.Serialization;
using StreamJsonRpc;
using System.Diagnostics;
using System.IO.Pipes;

namespace Akbura.LanguageServer.IntegrationTests;

public sealed class LifecycleTests
{
    [Fact]
    public async Task StdioLifecycleSupportsSyncSymbolsFormattingAndTokens()
    {
        var root = Directory.CreateTempSubdirectory("akbura-lsp-tests-");
        try
        {
            var pipeName = "akbura-lsp-" + Guid.NewGuid().ToString("N");
            using var serverStream = new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                maxNumberOfServerInstances: 1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);
            using var clientStream = new NamedPipeClientStream(
                ".",
                pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);
            var connectTask = serverStream.WaitForConnectionAsync();
            await clientStream.ConnectAsync();
            await connectTask;
            using var cancellation = new CancellationTokenSource(
                TimeSpan.FromSeconds(20));
            var error = new StringWriter();
            var hostTask = AkburaLanguageServerHost.RunAsync(
                serverStream,
                serverStream,
                AkburaServerOptions.Parse(["--stdio"]),
                error,
                cancellation.Token);

            var formatter = new SystemTextJsonFormatter
            {
                JsonSerializerOptions =
                    AkburaProtocolJson.CreateOptions(),
            };
            using var handler = new HeaderDelimitedMessageHandler(
                clientStream,
                clientStream,
                formatter);
            using var rpc = new JsonRpc(handler);
            var notifications = new ClientNotifications();
            rpc.AddLocalRpcTarget(
                notifications,
                new JsonRpcTargetOptions
                {
                    UseSingleObjectParameterDeserialization = false,
                });
            rpc.StartListening();

            var initialized = await rpc
                .InvokeWithParameterObjectAsync<InitializeResult>(
                    LspMethods.Initialize,
                    new InitializeParams
                    {
                        RootUri = new Uri(root.FullName).AbsoluteUri,
                        Capabilities = new InitializeClientCapabilities(),
                    },
                    cancellation.Token);
            Assert.Equal("utf-16", initialized.Capabilities.PositionEncoding);
            Assert.True(initialized.Capabilities.DocumentSymbolProvider);
            Assert.True(initialized.Capabilities.SemanticTokensProvider
                .Full.Delta);

            await rpc.NotifyWithParameterObjectAsync(
                LspMethods.Initialized,
                new InitializedParams());

            var documentUri = new Uri(
                Path.Combine(root.FullName, "Component.akbura"));
            const string source =
                "state int count = 0;\n\n" +
                "<StackPanel>\n<Button/>\n</StackPanel>";
            await rpc.NotifyWithParameterObjectAsync(
                LspMethods.DidOpen,
                new DidOpenTextDocumentParams
                {
                    TextDocument = new TextDocumentItem
                    {
                        Uri = documentUri.AbsoluteUri,
                        LanguageId = "akbura",
                        Version = 1,
                        Text = source,
                    },
                });
            var diagnostics = await notifications.NextDiagnostics.Task
                .WaitAsync(cancellation.Token);
            Assert.Equal(documentUri.AbsoluteUri, diagnostics.Uri);
            Assert.Equal(1, diagnostics.Version);

            var symbols = await rpc.InvokeWithParameterObjectAsync<
                DocumentSymbol[]>(
                    LspMethods.DocumentSymbol,
                    new DocumentSymbolParams
                    {
                        TextDocument = new TextDocumentIdentifier
                        {
                            Uri = documentUri.AbsoluteUri,
                        },
                    },
                    cancellation.Token);
            Assert.Contains(symbols, symbol => symbol.Name == "count");
            Assert.Contains(symbols, symbol => symbol.Name == "StackPanel");

            await rpc.NotifyWithParameterObjectAsync(
                LspMethods.DidChange,
                new DidChangeTextDocumentParams
                {
                    TextDocument = new VersionedTextDocumentIdentifier
                    {
                        Uri = documentUri.AbsoluteUri,
                        Version = 2,
                    },
                    ContentChanges =
                    [
                        new TextDocumentContentChangeEvent
                        {
                            Range = new Protocol.Range
                            {
                                Start = new Position
                                {
                                    Line = 0,
                                    Character = 18,
                                },
                                End = new Position
                                {
                                    Line = 0,
                                    Character = 19,
                                },
                            },
                            RangeLength = 1,
                            Text = "1",
                        },
                    ],
                });

            await rpc.NotifyWithParameterObjectAsync(
                LspMethods.DidChange,
                new DidChangeTextDocumentParams
                {
                    TextDocument = new VersionedTextDocumentIdentifier
                    {
                        Uri = documentUri.AbsoluteUri,
                        Version = 3,
                    },
                    ContentChanges =
                    [
                        new TextDocumentContentChangeEvent
                        {
                            Range = new Protocol.Range
                            {
                                Start = new Position
                                {
                                    Line = 0,
                                    Character = 10,
                                },
                                End = new Position
                                {
                                    Line = 0,
                                    Character = 15,
                                },
                            },
                            RangeLength = 5,
                            Text = "value",
                        },
                        new TextDocumentContentChangeEvent
                        {
                            Range = new Protocol.Range
                            {
                                Start = new Position
                                {
                                    Line = 0,
                                    Character = 18,
                                },
                                End = new Position
                                {
                                    Line = 0,
                                    Character = 19,
                                },
                            },
                            RangeLength = 1,
                            Text = "2",
                        },
                    ],
                });
            await rpc.NotifyWithParameterObjectAsync(
                LspMethods.DidChange,
                new DidChangeTextDocumentParams
                {
                    TextDocument = new VersionedTextDocumentIdentifier
                    {
                        Uri = documentUri.AbsoluteUri,
                        Version = 2,
                    },
                    ContentChanges =
                    [
                        new TextDocumentContentChangeEvent
                        {
                            Text = "state int stale = 0;",
                        },
                    ],
                });

            var changedSymbols = await rpc.InvokeWithParameterObjectAsync<
                DocumentSymbol[]>(
                    LspMethods.DocumentSymbol,
                    new DocumentSymbolParams
                    {
                        TextDocument = new TextDocumentIdentifier
                        {
                            Uri = documentUri.AbsoluteUri,
                        },
                    },
                    cancellation.Token);
            Assert.Contains(
                changedSymbols,
                symbol => symbol.Name == "value");
            Assert.DoesNotContain(
                changedSymbols,
                symbol => symbol.Name == "stale");
            var edits = await rpc.InvokeWithParameterObjectAsync<TextEdit[]>(
                LspMethods.Formatting,
                new DocumentFormattingParams
                {
                    TextDocument = new TextDocumentIdentifier
                    {
                        Uri = documentUri.AbsoluteUri,
                    },
                    Options = new FormattingOptions
                    {
                        TabSize = 2,
                        InsertSpaces = true,
                    },
                },
                cancellation.Token);
            Assert.NotEmpty(edits);
            Assert.Contains("  <Button/>", edits[0].NewText);

            var tokens = await rpc.InvokeWithParameterObjectAsync<
                SemanticTokens>(
                    LspMethods.SemanticTokensFull,
                    new SemanticTokensParams
                    {
                        TextDocument = new TextDocumentIdentifier
                        {
                            Uri = documentUri.AbsoluteUri,
                        },
                    },
                    cancellation.Token);
            Assert.NotEmpty(tokens.Data);

            var rangeTokens = await rpc.InvokeWithParameterObjectAsync<
                SemanticTokens>(
                    LspMethods.SemanticTokensRange,
                    new SemanticTokensRangeParams
                    {
                        TextDocument = new TextDocumentIdentifier
                        {
                            Uri = documentUri.AbsoluteUri,
                        },
                        Range = new Protocol.Range
                        {
                            Start = new Position(),
                            End = new Position
                            {
                                Line = 1,
                                Character = 0,
                            },
                        },
                    },
                    cancellation.Token);
            Assert.NotNull(rangeTokens.ResultId);
            var delta = await rpc.InvokeWithParameterObjectAsync<
                SemanticTokensDelta>(
                    LspMethods.SemanticTokensFullDelta,
                    new SemanticTokensDeltaParams
                    {
                        TextDocument = new TextDocumentIdentifier
                        {
                            Uri = documentUri.AbsoluteUri,
                        },
                        PreviousResultId = tokens.ResultId!,
                    },
                    cancellation.Token);
            Assert.Empty(delta.Edits);

            await rpc.InvokeWithCancellationAsync<object?>(
                LspMethods.Shutdown,
                Array.Empty<object>(),
                cancellation.Token);
            await rpc.NotifyAsync(
                LspMethods.Exit,
                Array.Empty<object>());

            Assert.Equal(0, await hostTask.WaitAsync(cancellation.Token));
            Assert.DoesNotContain("Exception", error.ToString());
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ExecutableStdioLifecycleKeepsStdoutProtocolOnly()
    {
        var root = Directory.CreateTempSubdirectory("akbura-lsp-process-tests-");
        try
        {
            using var cancellation = new CancellationTokenSource(
                TimeSpan.FromSeconds(20));
            var dotnetHost = Environment.GetEnvironmentVariable(
                "DOTNET_HOST_PATH");
            var startInfo = new ProcessStartInfo
            {
                FileName = string.IsNullOrWhiteSpace(dotnetHost)
                    ? "dotnet"
                    : dotnetHost,
                WorkingDirectory = Path.GetDirectoryName(
                    typeof(AkburaLanguageServerHost).Assembly.Location)!,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            startInfo.ArgumentList.Add(
                typeof(AkburaLanguageServerHost).Assembly.Location);
            startInfo.ArgumentList.Add("--stdio");
            startInfo.ArgumentList.Add("--log-level");
            startInfo.ArgumentList.Add("none");

            using var process = Process.Start(startInfo);
            Assert.NotNull(process);
            var errorTask = process.StandardError.ReadToEndAsync(
                cancellation.Token);
            var formatter = new SystemTextJsonFormatter
            {
                JsonSerializerOptions =
                    AkburaProtocolJson.CreateOptions(),
            };
            using var handler = new HeaderDelimitedMessageHandler(
                process.StandardInput.BaseStream,
                process.StandardOutput.BaseStream,
                formatter);
            using var rpc = new JsonRpc(handler);
            rpc.StartListening();

            var initialized = await rpc
                .InvokeWithParameterObjectAsync<InitializeResult>(
                    LspMethods.Initialize,
                    new InitializeParams
                    {
                        RootUri = new Uri(root.FullName).AbsoluteUri,
                        Capabilities = new InitializeClientCapabilities(),
                    },
                    cancellation.Token);
            Assert.Equal(
                "Akbura Language Server",
                initialized.ServerInfo?.Name);

            await rpc.NotifyWithParameterObjectAsync(
                LspMethods.Initialized,
                new InitializedParams());
            await rpc.InvokeWithCancellationAsync<object?>(
                LspMethods.Shutdown,
                Array.Empty<object>(),
                cancellation.Token);
            await rpc.NotifyAsync(
                LspMethods.Exit,
                Array.Empty<object>());

            await process.WaitForExitAsync(cancellation.Token);
            Assert.Equal(0, process.ExitCode);
            Assert.Equal(string.Empty, await errorTask);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
    private sealed class ClientNotifications
    {
        public TaskCompletionSource<PublishDiagnosticsParams>
            NextDiagnostics { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        [JsonRpcMethod(
            LspMethods.PublishDiagnostics,
            UseSingleObjectParameterDeserialization = true)]
        public void PublishDiagnostics(PublishDiagnosticsParams parameters)
        {
            NextDiagnostics.TrySetResult(parameters);
        }
    }
}