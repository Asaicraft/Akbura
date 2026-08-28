import * as vscode from 'vscode';

import {
    AkburaPairSessionDto,
    ProtocolRange
} from './typingProtocol.js';

export interface AkburaPairSessionContext {
    id: number;
    session: AkburaPairSessionDto;
}

interface OffsetSpan {
    start: number;
    end: number;
}

interface TrackedPairSession {
    id: number;
    kind: string;
    opening: OffsetSpan;
    closing: OffsetSpan;
    openingText: string;
    closingText: string;
    requiredDelimiterLength: number;
    outerLiteralDelimiterCount: number;
}

export class AkburaPairSessionManager implements vscode.Disposable {
    private readonly sessions =
        new Map<string, TrackedPairSession[]>();

    private readonly subscriptions: vscode.Disposable[];

    private nextId = 1;

    public constructor() {
        this.subscriptions = [
            vscode.workspace.onDidChangeTextDocument(
                event => this.onDocumentChanged(event)
            ),
            vscode.workspace.onDidCloseTextDocument(
                document => {
                    this.sessions.delete(
                        document.uri.toString()
                    );
                }
            )
        ];
    }

    public get(
        editor: vscode.TextEditor
    ): AkburaPairSessionContext | undefined {
        const document = editor.document;
        const key = document.uri.toString();
        const stack = this.sessions.get(key);

        if (stack == null || stack.length === 0) {
            return undefined;
        }

        const caret = document.offsetAt(
            editor.selection.active
        );

        for (let index = stack.length - 1;
             index >= 0;
             index--) {
            const tracked = stack[index];

            if (tracked == null) {
                continue;
            }

            if (!this.isValid(document, tracked)) {
                stack.splice(index, 1);
                continue;
            }

            if (caret < tracked.opening.end ||
                caret > tracked.closing.end) {
                continue;
            }

            return {
                id: tracked.id,
                session: this.toProtocol(
                    document,
                    tracked
                )
            };
        }

        this.removeEmptyStack(key, stack);
        return undefined;
    }

    public complete(
        editor: vscode.TextEditor,
        context: AkburaPairSessionContext | undefined,
        session: AkburaPairSessionDto | undefined
    ): void {
        const document = editor.document;
        const key = document.uri.toString();
        const stack = this.sessions.get(key) ?? [];
        const existingIndex = context == null
            ? -1
            : stack.findIndex(
                candidate => candidate.id === context.id
            );

        if (session == null) {
            if (existingIndex >= 0) {
                stack.splice(existingIndex, 1);
            }

            this.removeEmptyStack(key, stack);
            return;
        }

        const incoming = this.fromProtocol(
            document,
            session
        );

        if (existingIndex >= 0) {
            const existing = stack[existingIndex];
            if (existing != null &&
                existing.kind === incoming.kind &&
                existing.opening.start ===
                    incoming.opening.start) {
                incoming.id = existing.id;
                stack[existingIndex] = incoming;
            } else {
                stack.push(incoming);
            }
        } else {
            stack.push(incoming);
        }

        this.sessions.set(key, stack);
    }

    public clear(editor: vscode.TextEditor): void {
        this.sessions.delete(
            editor.document.uri.toString()
        );
    }

    public clearAll(): void {
        this.sessions.clear();
    }

    public dispose(): void {
        for (const subscription of this.subscriptions) {
            subscription.dispose();
        }

        this.sessions.clear();
    }

    private onDocumentChanged(
        event: vscode.TextDocumentChangeEvent
    ): void {
        const key = event.document.uri.toString();
        const stack = this.sessions.get(key);

        if (stack == null || stack.length === 0) {
            return;
        }

        const changes = [...event.contentChanges]
            .sort(
                (left, right) =>
                    right.rangeOffset -
                    left.rangeOffset
            );

        for (let index = stack.length - 1;
             index >= 0;
             index--) {
            const session = stack[index];

            if (session == null ||
                !this.transform(session, changes) ||
                !this.isValid(event.document, session)) {
                stack.splice(index, 1);
            }
        }

        this.removeEmptyStack(key, stack);
    }

    private transform(
        session: TrackedPairSession,
        changes: readonly vscode.TextDocumentContentChangeEvent[]
    ): boolean {
        for (const change of changes) {
            const start = change.rangeOffset;
            const end = start + change.rangeLength;
            const delta = change.text.length -
                change.rangeLength;

            if (intersectsDelimiter(
                    start,
                    end,
                    session.opening) ||
                intersectsDelimiter(
                    start,
                    end,
                    session.closing)) {
                return false;
            }

            if (end <= session.opening.start) {
                shift(session.opening, delta);
                shift(session.closing, delta);
                continue;
            }

            if (start >= session.opening.end &&
                end <= session.closing.start) {
                shift(session.closing, delta);
                continue;
            }

            if (start >= session.closing.end) {
                continue;
            }

            return false;
        }

        return true;
    }

    private isValid(
        document: vscode.TextDocument,
        session: TrackedPairSession
    ): boolean {
        if (session.opening.start < 0 ||
            session.opening.end < session.opening.start ||
            session.closing.start < session.opening.end ||
            session.closing.end < session.closing.start ||
            session.closing.end > document.getText().length) {
            return false;
        }

        return document.getText(
                offsetRange(document, session.opening)
            ) === session.openingText &&
            document.getText(
                offsetRange(document, session.closing)
            ) === session.closingText;
    }

    private fromProtocol(
        document: vscode.TextDocument,
        session: AkburaPairSessionDto
    ): TrackedPairSession {
        return {
            id: this.nextId++,
            kind: session.kind,
            opening: toOffsetSpan(
                document,
                session.openingRange
            ),
            closing: toOffsetSpan(
                document,
                session.closingRange
            ),
            openingText: session.openingText,
            closingText: session.closingText,
            requiredDelimiterLength:
                session.requiredDelimiterLength,
            outerLiteralDelimiterCount:
                session.outerLiteralDelimiterCount
        };
    }

    private toProtocol(
        document: vscode.TextDocument,
        session: TrackedPairSession
    ): AkburaPairSessionDto {
        return {
            kind: session.kind,
            openingRange: toProtocolRange(
                document,
                session.opening
            ),
            closingRange: toProtocolRange(
                document,
                session.closing
            ),
            openingText: session.openingText,
            closingText: session.closingText,
            requiredDelimiterLength:
                session.requiredDelimiterLength,
            outerLiteralDelimiterCount:
                session.outerLiteralDelimiterCount
        };
    }

    private removeEmptyStack(
        key: string,
        stack: TrackedPairSession[]
    ): void {
        if (stack.length === 0) {
            this.sessions.delete(key);
        }
    }
}

function intersectsDelimiter(
    start: number,
    end: number,
    delimiter: OffsetSpan
): boolean {
    if (start === end) {
        return start > delimiter.start &&
            start < delimiter.end;
    }

    return start < delimiter.end &&
        end > delimiter.start;
}

function shift(span: OffsetSpan, delta: number): void {
    span.start += delta;
    span.end += delta;
}

function toOffsetSpan(
    document: vscode.TextDocument,
    range: ProtocolRange
): OffsetSpan {
    return {
        start: document.offsetAt(
            new vscode.Position(
                range.start.line,
                range.start.character
            )
        ),
        end: document.offsetAt(
            new vscode.Position(
                range.end.line,
                range.end.character
            )
        )
    };
}

function toProtocolRange(
    document: vscode.TextDocument,
    span: OffsetSpan
): ProtocolRange {
    const start = document.positionAt(span.start);
    const end = document.positionAt(span.end);

    return {
        start: {
            line: start.line,
            character: start.character
        },
        end: {
            line: end.line,
            character: end.character
        }
    };
}

function offsetRange(
    document: vscode.TextDocument,
    span: OffsetSpan
): vscode.Range {
    return new vscode.Range(
        document.positionAt(span.start),
        document.positionAt(span.end)
    );
}
