import * as path from 'node:path';
import { fileURLToPath } from 'node:url';

import { runTests } from '@vscode/test-electron';

const directory = path.dirname(fileURLToPath(import.meta.url));
const extensionDevelopmentPath = path.resolve(directory, '..');
const extensionTestsPath = path.resolve(
    directory,
    'suite',
    'index.cjs'
);
const workspacePath = path.resolve(
    directory,
    'fixtures',
    'workspace'
);

try {
    await runTests({
        extensionDevelopmentPath,
        extensionTestsPath,
        launchArgs: [
            workspacePath,
            '--disable-workspace-trust',
            '--skip-welcome',
            '--skip-release-notes'
        ]
    });
} catch (error) {
    console.error('Akbura VS Code integration tests failed.');
    console.error(error);
    process.exitCode = 1;
}
