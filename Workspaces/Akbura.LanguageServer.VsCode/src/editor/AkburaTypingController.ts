import * as vscode from 'vscode';
import {
    DidChangeTextDocumentNotification,
    LanguageClient,
    State
} from 'vscode-languageclient/node';

import {
    AkburaPairSessionContext,
    AkburaPairSessionManager
} from './AkburaPairSessionManager.js';
import {
    AkburaTypingCommand,
    AkburaTypingParams,
    AkburaTypingResponse,
    ProtocolPosition,
    ProtocolRange
} from './typingProtocol.js';

const typingMethod = 'akbura/textDocument/typing';
const triggerCharacters = new Set([
    '{',
    '}',
    '(',
    ')',
    '[',
    ']',
    '"',
    '<',
    '>',
    '/'
]);

type AutomaticPairingMode =
    'syntax' |
    'basic' |
    'off';

interface TypeArguments {
    text?: string;
    replacePreviousChar?: number | boolean;
    replacePrevCharCnt?: number;
    replaceNextCharCnt?: number;
}

interface DocumentSyncWaiter extends vscode.Disposable {
    wait(): Promise<void>;
}

export class AkburaTypingController implements vscode.Disposable {
    private readonly sessions =
        new AkburaPairSessionManager();

    private activeClient: LanguageClient | undefined;

    public constructor(
        private readonly getClient:
            () => LanguageClient | undefined,
        private readonly output: vscode.OutputChannel
    ) {
    }

    public async type(
        argumentsValue: TypeArguments | undefined
    ): Promise<void> {
        const text = argumentsValue?.text ?? '';
        const editor = vscode.window.activeTextEditor;

        if (editor == null ||
            !isAkburaDocument(editor.document) ||
            text.length !== 1 ||
            hasCompositionReplacement(argumentsValue) ||
            !triggerCharacters.has(text) ||
            !isSimpleCaret(editor)) {
            await forwardType(text, argumentsValue);
            return;
        }

        const mode = getAutomaticPairingMode(
            editor.document
        );
        if (mode === 'basic') {
            await forwardType(text, argumentsValue);
            return;
        }

        if (mode === 'off' ||
            !isEditorPairingEnabled(editor, text)) {
            this.sessions.clear(editor);
            await insertPlainText(editor, text);
            return;
        }

        const handled = await this.tryHandle(
            editor,
            'type',
            text,
            true
        );
        if (!handled) {
            await forwardType(text, argumentsValue);
        }
    }

    public async backspace(): Promise<void> {
        const editor = vscode.window.activeTextEditor;
        if (!this.canUseSyntaxMode(editor)) {
            await vscode.commands.executeCommand(
                'deleteLeft'
            );
            return;
        }

        const context = this.sessions.get(editor!);
        if (context == null) {
            await vscode.commands.executeCommand(
                'deleteLeft'
            );
            return;
        }

        const handled = await this.tryHandle(
            editor!,
            'backspace',
            '',
            true,
            context
        );
        if (!handled) {
            await vscode.commands.executeCommand(
                'deleteLeft'
            );
        }
    }

    public async tab(): Promise<void> {
        const editor = vscode.window.activeTextEditor;
        if (!this.canUseSyntaxMode(editor)) {
            await vscode.commands.executeCommand('tab');
            return;
        }

        const context = this.sessions.get(editor!);
        if (context == null) {
            await vscode.commands.executeCommand('tab');
            return;
        }

        const handled = await this.tryHandle(
            editor!,
            'tab',
            '',
            true,
            context
        );
        if (!handled) {
            await vscode.commands.executeCommand('tab');
        }
    }

    public async return(): Promise<void> {
        const editor = vscode.window.activeTextEditor;
        if (!this.canUseSyntaxMode(editor) ||
            !getAkburaConfiguration(editor!.document)
                .get<boolean>(
                    'editor.rawStringCompletion',
                    true
                )) {
            await forwardReturn(editor);
            return;
        }

        const context = this.sessions.get(editor!);
        const handled = await this.tryHandle(
            editor!,
            'return',
            '',
            true,
            context
        );
        if (!handled) {
            await forwardReturn(editor);
        }
    }

    public dispose(): void {
        this.sessions.dispose();
    }

    private canUseSyntaxMode(
        editor: vscode.TextEditor | undefined
    ): editor is vscode.TextEditor {
        return editor != null &&
            isAkburaDocument(editor.document) &&
            isSimpleCaret(editor) &&
            getAutomaticPairingMode(editor.document) ===
                'syntax';
    }

    private async tryHandle(
        editor: vscode.TextEditor,
        command: AkburaTypingCommand,
        text: string,
        allowRetry: boolean,
        knownContext?: AkburaPairSessionContext
    ): Promise<boolean> {
        const client = this.getClient();
        if (client !== this.activeClient) {
            this.sessions.clearAll();
            this.activeClient = client;
        }

        if (client == null) {
            return false;
        }

        const document = editor.document;
        const uri = document.uri.toString();
        const version = document.version;
        const position = editor.selection.active;
        const sessionContext = knownContext ??
            this.sessions.get(editor);
        const parameters: AkburaTypingParams = {
            textDocument: {
                uri,
                version
            },
            position: toProtocolPosition(position),
            command,
            text,
            options: createTypingOptions(editor),
            session: sessionContext?.session
        };

        const syncWaiter = createDocumentSyncWaiter(
            client,
            uri,
            version
        );
        let response: AkburaTypingResponse;

        try {
            response = await client.sendRequest<
                AkburaTypingResponse
            >(
                typingMethod,
                parameters
            );
        } catch (error) {
            syncWaiter.dispose();
            this.output.appendLine(
                '[Akbura.Typing] Request failed: ' +
                formatError(error)
            );
            return false;
        }

        if (response.stale) {
            const caretUnchanged = isSameCaret(
                editor,
                uri,
                position
            );
            if (allowRetry &&
                caretUnchanged &&
                response.version < version) {
                await syncWaiter.wait();
                syncWaiter.dispose();
                if (!isSameCaret(editor, uri, position)) {
                    return true;
                }

                return this.tryHandle(
                    editor,
                    command,
                    text,
                    false
                );
            }

            syncWaiter.dispose();
            this.output.appendLine(
                '[Akbura.Typing] Ignored stale response ' +
                `for ${uri} version ${version}.`
            );
            return !caretUnchanged;
        }

        syncWaiter.dispose();

        if (response.version !== version ||
            document.version !== version ||
            !isSameCaret(editor, uri, position)) {
            const caretUnchanged = isSameCaret(
                editor,
                uri,
                position
            );
            if (allowRetry && caretUnchanged) {
                return this.tryHandle(
                    editor,
                    command,
                    text,
                    false
                );
            }

            this.output.appendLine(
                '[Akbura.Typing] Discarded an obsolete ' +
                `${command} response for ${uri}.`
            );
            return !caretUnchanged;
        }

        if (!response.handled) {
            return false;
        }

        await applyTypingResponse(editor, response);
        this.sessions.complete(
            editor,
            sessionContext,
            response.session
        );

        if (response.triggerCompletion) {
            await vscode.commands.executeCommand(
                'editor.action.triggerSuggest'
            );
        }

        if (response.triggerSignatureHelp) {
            await vscode.commands.executeCommand(
                'editor.action.triggerParameterHints'
            );
        }

        return true;
    }
}

function createDocumentSyncWaiter(
    client: LanguageClient,
    uri: string,
    version: number
): DocumentSyncWaiter {
    let complete!: () => void;
    let completed = false;
    const completion = new Promise<void>(
        resolve => {
            complete = () => {
                if (!completed) {
                    completed = true;
                    resolve();
                }
            };
        }
    );
    const subscriptions = [
        client.getFeature(
            DidChangeTextDocumentNotification.method
        ).onNotificationSent(event => {
            if (event.params.textDocument.uri === uri &&
                event.params.textDocument.version >= version) {
                complete();
            }
        }),
        client.onDidChangeState(event => {
            if (event.newState !== State.Running) {
                complete();
            }
        })
    ];

    return {
        wait: () => completion,
        dispose: () => {
            complete();
            for (const subscription of subscriptions) {
                subscription.dispose();
            }
        }
    };
}

function hasCompositionReplacement(
    argumentsValue: TypeArguments | undefined
): boolean {
    return argumentsValue?.replacePreviousChar === true ||
        typeof argumentsValue?.replacePreviousChar === 'number' &&
            argumentsValue.replacePreviousChar > 0 ||
        (argumentsValue?.replacePrevCharCnt ?? 0) > 0 ||
        (argumentsValue?.replaceNextCharCnt ?? 0) > 0;
}

function isAkburaDocument(
    document: vscode.TextDocument
): boolean {
    return document.languageId === 'akbura' ||
        document.languageId === 'akcss';
}

function isSimpleCaret(editor: vscode.TextEditor): boolean {
    return editor.selections.length === 1 &&
        editor.selection.isEmpty;
}

function isSameDocument(
    editor: vscode.TextEditor,
    uri: string
): boolean {
    return !editor.document.isClosed &&
        editor.document.uri.toString() === uri;
}

function isSameCaret(
    editor: vscode.TextEditor,
    uri: string,
    position: vscode.Position
): boolean {
    return isSameDocument(editor, uri) &&
        editor.selection.isEmpty &&
        editor.selection.active.isEqual(position);
}

function getAutomaticPairingMode(
    document: vscode.TextDocument
): AutomaticPairingMode {
    return getAkburaConfiguration(document)
        .get<AutomaticPairingMode>(
            'editor.automaticPairing',
            'syntax'
        );
}

function getAkburaConfiguration(
    document: vscode.TextDocument
): vscode.WorkspaceConfiguration {
    return vscode.workspace.getConfiguration(
        'akbura',
        document.uri
    );
}

function isEditorPairingEnabled(
    editor: vscode.TextEditor,
    character: string
): boolean {
    const editorConfiguration =
        vscode.workspace.getConfiguration(
            'editor',
            editor.document.uri
        );

    if (character === '"') {
        return editorConfiguration.get<string>(
            'autoClosingQuotes',
            'languageDefined'
        ) !== 'never';
    }

    if ('{}()[]<>'.includes(character)) {
        return editorConfiguration.get<string>(
            'autoClosingBrackets',
            'languageDefined'
        ) !== 'never';
    }

    return true;
}

function createTypingOptions(
    editor: vscode.TextEditor
): AkburaTypingParams['options'] {
    const editorConfiguration =
        vscode.workspace.getConfiguration(
            'editor',
            editor.document.uri
        );
    const akburaConfiguration =
        getAkburaConfiguration(editor.document);
    const tabSize = toPositiveInteger(
        editor.options.tabSize,
        4
    );
    const configuredIndentSize =
        editorConfiguration.get<number | string>(
            'indentSize',
            'tabSize'
        );
    const indentSize = configuredIndentSize ===
            'tabSize'
        ? tabSize
        : toPositiveInteger(
            configuredIndentSize,
            tabSize
        );

    return {
        tabSize,
        indentSize,
        insertSpaces:
            editor.options.insertSpaces !== false,
        newLine: editor.document.eol ===
                vscode.EndOfLine.CRLF
            ? '\r\n'
            : '\n',
        autoClosingTags: akburaConfiguration
            .get<boolean>(
                'editor.autoClosingTags',
                true
            ),
        rawStringCompletion: akburaConfiguration
            .get<boolean>(
                'editor.rawStringCompletion',
                true
            )
    };
}

function toPositiveInteger(
    value: number | string | undefined,
    fallback: number
): number {
    const numeric = typeof value === 'number'
        ? value
        : Number.parseInt(value ?? '', 10);

    return Number.isFinite(numeric) && numeric > 0
        ? Math.trunc(numeric)
        : fallback;
}

async function applyTypingResponse(
    editor: vscode.TextEditor,
    response: AkburaTypingResponse
): Promise<void> {
    if (response.edits.length !== 0) {
        const applied = await editor.edit(
            edit => {
                for (const change of response.edits) {
                    edit.replace(
                        toVsCodeRange(change.range),
                        change.newText
                    );
                }
            },
            {
                undoStopBefore: true,
                undoStopAfter: true
            }
        );

        if (!applied) {
            throw new Error(
                'VS Code rejected the Akbura typing edit.'
            );
        }
    }

    const position = new vscode.Position(
        response.position.line,
        response.position.character
    );
    editor.selection = new vscode.Selection(
        position,
        position
    );
}

async function insertPlainText(
    editor: vscode.TextEditor,
    text: string
): Promise<void> {
    const position = editor.selection.active;
    const applied = await editor.edit(
        edit => edit.insert(position, text),
        {
            undoStopBefore: true,
            undoStopAfter: true
        }
    );

    if (applied) {
        const next = position.translate(
            0,
            text.length
        );
        editor.selection = new vscode.Selection(
            next,
            next
        );
    }
}

async function forwardType(
    text: string,
    argumentsValue?: TypeArguments
): Promise<void> {
    await vscode.commands.executeCommand(
        'default:type',
        argumentsValue ?? { text }
    );
}

async function forwardReturn(
    editor: vscode.TextEditor | undefined
): Promise<void> {
    const newLine = editor?.document.eol ===
            vscode.EndOfLine.CRLF
        ? '\r\n'
        : '\n';
    await forwardType(newLine);
}

function toProtocolPosition(
    position: vscode.Position
): ProtocolPosition {
    return {
        line: position.line,
        character: position.character
    };
}

function toVsCodeRange(
    range: ProtocolRange
): vscode.Range {
    return new vscode.Range(
        range.start.line,
        range.start.character,
        range.end.line,
        range.end.character
    );
}

function formatError(error: unknown): string {
    return error instanceof Error
        ? error.message
        : String(error);
}
