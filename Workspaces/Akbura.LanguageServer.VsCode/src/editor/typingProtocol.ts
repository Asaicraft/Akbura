export type AkburaTypingCommand =
    'type' |
    'backspace' |
    'tab' |
    'return';

export interface ProtocolPosition {
    line: number;
    character: number;
}

export interface ProtocolRange {
    start: ProtocolPosition;
    end: ProtocolPosition;
}

export interface ProtocolTextEdit {
    range: ProtocolRange;
    newText: string;
}

export interface AkburaPairSessionDto {
    kind: string;
    openingRange: ProtocolRange;
    closingRange: ProtocolRange;
    openingText: string;
    closingText: string;
    requiredDelimiterLength: number;
    outerLiteralDelimiterCount: number;
}

export interface AkburaTypingParams {
    textDocument: {
        uri: string;
        version: number;
    };
    position: ProtocolPosition;
    command: AkburaTypingCommand;
    text: string;
    options: {
        tabSize: number;
        insertSpaces: boolean;
        indentSize: number;
        newLine: string;
        autoClosingTags: boolean;
        rawStringCompletion: boolean;
    };
    session?: AkburaPairSessionDto;
}

export interface AkburaTypingResponse {
    handled: boolean;
    stale: boolean;
    version: number;
    edits: ProtocolTextEdit[];
    position: ProtocolPosition;
    session?: AkburaPairSessionDto;
    triggerCompletion: boolean;
    triggerSignatureHelp: boolean;
}
