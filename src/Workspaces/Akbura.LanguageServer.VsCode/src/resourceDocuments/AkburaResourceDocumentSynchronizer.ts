import * as path from 'node:path';

import * as vscode from 'vscode';

import { LanguageClient } from 'vscode-languageclient/node';

const methods = {
    didOpen: 'akbura/resourceDocument/didOpen',
    didChange: 'akbura/resourceDocument/didChange',
    didSave: 'akbura/resourceDocument/didSave',
    didClose: 'akbura/resourceDocument/didClose'
} as const;

export class AkburaResourceDocumentSynchronizer implements vscode.Disposable {
    private readonly subscriptions: vscode.Disposable[];
    private readonly synchronizedVersions = new Map<string, number>();
    private activeClient: LanguageClient | undefined;

    public constructor(
        private readonly outputChannel: vscode.OutputChannel
    ) {
        this.subscriptions = [
            vscode.workspace.onDidOpenTextDocument(
                document => this.onDidOpen(document)
            ),
            vscode.workspace.onDidChangeTextDocument(
                event => this.onDidChange(event)
            ),
            vscode.workspace.onDidSaveTextDocument(
                document => this.onDidSave(document)
            ),
            vscode.workspace.onDidCloseTextDocument(
                document => this.onDidClose(document)
            )
        ];
    }

    public async start(client: LanguageClient): Promise<void> {
        this.activeClient = client;
        this.synchronizedVersions.clear();

        for (const document of vscode.workspace.textDocuments) {
            if (!isAvaloniaResourceDocument(document)) {
                continue;
            }

            await this.sendDidOpen(client, document);
        }
    }

    public stop(client?: LanguageClient): void {
        if (client != null && this.activeClient !== client) {
            return;
        }

        this.activeClient = undefined;
        this.synchronizedVersions.clear();
    }

    public dispose(): void {
        this.stop();

        for (const subscription of this.subscriptions) {
            subscription.dispose();
        }
    }

    private onDidOpen(document: vscode.TextDocument): void {
        const current = this.activeClient;
        if (current == null ||
            !isAvaloniaResourceDocument(document)) {
            return;
        }

        void this.sendDidOpen(current, document)
            .catch(error => this.logFailure('open', document.uri, error));
    }

    private onDidChange(
        event: vscode.TextDocumentChangeEvent
    ): void {
        const current = this.activeClient;
        const document = event.document;
        if (current == null ||
            !isAvaloniaResourceDocument(document)) {
            return;
        }

        const key = document.uri.toString();
        if (!this.synchronizedVersions.has(key)) {
            void this.sendDidOpen(current, document)
                .catch(error =>
                    this.logFailure('open', document.uri, error)
                );
            return;
        }

        this.synchronizedVersions.set(key, document.version);
        void current.sendNotification(
            methods.didChange,
            {
                textDocument: {
                    uri: key,
                    version: document.version
                },
                contentChanges: event.contentChanges.map(change => ({
                    range: {
                        start: {
                            line: change.range.start.line,
                            character: change.range.start.character
                        },
                        end: {
                            line: change.range.end.line,
                            character: change.range.end.character
                        }
                    },
                    rangeLength: change.rangeLength,
                    text: change.text
                }))
            }
        ).catch(error =>
            this.logFailure('change', document.uri, error)
        );
    }

    private onDidSave(document: vscode.TextDocument): void {
        const current = this.activeClient;
        if (current == null ||
            !isAvaloniaResourceDocument(document) ||
            !this.synchronizedVersions.has(document.uri.toString())) {
            return;
        }

        void current.sendNotification(
            methods.didSave,
            {
                textDocument: {
                    uri: document.uri.toString()
                },
                text: document.getText()
            }
        ).catch(error =>
            this.logFailure('save', document.uri, error)
        );
    }

    private onDidClose(document: vscode.TextDocument): void {
        const current = this.activeClient;
        if (current == null ||
            !isAvaloniaResourceDocument(document)) {
            return;
        }

        const key = document.uri.toString();
        if (!this.synchronizedVersions.delete(key)) {
            return;
        }

        void current.sendNotification(
            methods.didClose,
            {
                textDocument: {
                    uri: key
                }
            }
        ).catch(error =>
            this.logFailure('close', document.uri, error)
        );
    }

    private async sendDidOpen(
        client: LanguageClient,
        document: vscode.TextDocument
    ): Promise<void> {
        if (this.activeClient !== client) {
            return;
        }

        const key = document.uri.toString();
        if (this.synchronizedVersions.has(key)) {
            return;
        }

        this.synchronizedVersions.set(key, document.version);
        try {
            await client.sendNotification(
                methods.didOpen,
                {
                    textDocument: {
                        uri: key,
                        languageId: document.languageId,
                        version: document.version,
                        text: document.getText()
                    }
                }
            );
        } catch (error) {
            this.synchronizedVersions.delete(key);
            throw error;
        }
    }

    private logFailure(
        operation: string,
        uri: vscode.Uri,
        error: unknown
    ): void {
        this.outputChannel.appendLine(
            `[Akbura] Unable to synchronize resource document ` +
            `'${uri.toString()}' during ${operation}: ${String(error)}`
        );
    }
}

function isAvaloniaResourceDocument(
    document: vscode.TextDocument
): boolean {
    return document.uri.scheme === 'file' &&
        path.extname(document.uri.fsPath)
            .toLowerCase() === '.axaml';
}
