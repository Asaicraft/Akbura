import * as path from 'node:path';
import { spawnSync } from 'node:child_process';
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
const projectPath = path.join(
    workspacePath,
    'Akbura.VsCode.Acceptance.csproj'
);

try {
    const restore = spawnSync(
        process.env.DOTNET_HOST_PATH || 'dotnet',
        ['restore', projectPath, '--nologo'],
        {
            cwd: workspacePath,
            encoding: 'utf8'
        }
    );
    if (restore.status !== 0) {
        throw new Error(
            'Unable to restore the VS Code acceptance fixture.\n' +
                (restore.stdout || '') +
                (restore.stderr || '')
        );
    }

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
