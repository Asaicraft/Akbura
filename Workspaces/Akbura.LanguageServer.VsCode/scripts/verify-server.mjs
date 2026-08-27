import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import * as fs from 'node:fs';
import * as path from 'node:path';
import { fileURLToPath } from 'node:url';

import {
    createMessageConnection,
    StreamMessageReader,
    StreamMessageWriter
} from 'vscode-jsonrpc/node.js';

const scriptDirectory = path.dirname(
    fileURLToPath(import.meta.url)
);

const extensionDirectory = path.resolve(
    scriptDirectory,
    '..'
);

const serverDirectory = path.join(
    extensionDirectory,
    'server'
);

const serverAssembly = path.join(
    serverDirectory,
    'akbura-lsp.dll'
);

if (!fs.existsSync(serverAssembly)) {
    console.error(
        `[Akbura] Packaged language server was not found: ${serverAssembly}`
    );
    process.exit(1);
}

const dotnet = process.env.DOTNET_HOST_PATH || 'dotnet';

const server = spawn(
    dotnet,
    [
        serverAssembly,
        '--stdio',
        '--clientProcessId',
        String(process.pid),
        '--log-level',
        'none'
    ],
    {
        cwd: serverDirectory,
        env: {
            ...process.env,
            DOTNET_NOLOGO: '1',
            DOTNET_CLI_TELEMETRY_OPTOUT: '1',
            DOTNET_SKIP_FIRST_TIME_EXPERIENCE: '1'
        },
        windowsHide: true,
        stdio: [
            'pipe',
            'pipe',
            'pipe'
        ]
    }
);

let stderr = '';
server.stderr.setEncoding('utf8');
server.stderr.on(
    'data',
    chunk => {
        stderr += chunk;
    }
);

const connection = createMessageConnection(
    new StreamMessageReader(server.stdout),
    new StreamMessageWriter(server.stdin)
);

connection.onRequest(
    () => null
);

connection.listen();

const exit = new Promise((resolve, reject) => {
    server.once('error', reject);
    server.once(
        'exit',
        (code, signal) => resolve({
            code,
            signal
        })
    );
});

const timeout = setTimeout(
    () => {
        server.kill();
    },
    20_000
);

try {
    const initialized = await connection.sendRequest(
        'initialize',
        {
            processId: process.pid,
            rootUri: null,
            capabilities: {}
        }
    );

    assert.equal(
        initialized.serverInfo?.name,
        'Akbura Language Server'
    );

    assert.equal(
        initialized.capabilities?.positionEncoding,
        'utf-16'
    );

    await connection.sendNotification(
        'initialized',
        {}
    );

    await connection.sendRequest(
        'shutdown'
    );

    await connection.sendNotification(
        'exit'
    );

    const result = await exit;

    assert.equal(
        result.signal,
        null,
        'The language server was terminated by a signal.'
    );

    assert.equal(
        result.code,
        0,
        `The language server exited with code ${result.code}.`
    );

    assert.equal(
        stderr,
        '',
        'The language server wrote to stderr with logging disabled.'
    );

    console.log(
        '[Akbura] Packaged language server lifecycle verified.'
    );
} finally {
    clearTimeout(timeout);
    connection.dispose();

    if (server.exitCode == null &&
        server.signalCode == null) {
        server.kill();
    }
}
