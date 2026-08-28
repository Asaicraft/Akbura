import { spawnSync } from 'node:child_process';
import * as fs from 'node:fs';
import * as path from 'node:path';
import { fileURLToPath } from 'node:url';

const scriptDirectory = path.dirname(
    fileURLToPath(import.meta.url)
);

const extensionDirectory = path.resolve(
    scriptDirectory,
    '..'
);

const serverProject = path.resolve(
    extensionDirectory,
    '..',
    'Akbura.LanguageServer',
    'Akbura.LanguageServer.csproj'
);

const outputDirectory = path.resolve(
    extensionDirectory,
    'server'
);

const configuration = process.argv.includes('--release')
    ? 'Release'
    : 'Debug';

if (!fs.existsSync(serverProject)) {
    console.error(
        `[Akbura] Language server project was not found: ${serverProject}`
    );
    process.exit(1);
}

fs.rmSync(
    outputDirectory,
    {
        recursive: true,
        force: true
    }
);

fs.mkdirSync(
    outputDirectory,
    {
        recursive: true
    }
);

const dotnet = process.env.DOTNET_HOST_PATH || 'dotnet';

const result = spawnSync(
    dotnet,
    [
        'publish',
        serverProject,
        '--configuration',
        configuration,
        '--no-self-contained',
        '--output',
        outputDirectory,
        '--nologo',
        '-p:UseAppHost=false'
    ],
    {
        stdio: 'inherit',
        env: {
            ...process.env,
            DOTNET_NOLOGO: '1',
            DOTNET_CLI_TELEMETRY_OPTOUT: '1',
            DOTNET_SKIP_FIRST_TIME_EXPERIENCE: '1'
        }
    }
);

if (result.error) {
    console.error(result.error);
    process.exit(1);
}

if (result.status !== 0) {
    process.exit(result.status ?? 1);
}

const serverAssembly = path.join(
    outputDirectory,
    'akbura-lsp.dll'
);

if (!fs.existsSync(serverAssembly)) {
    console.error(
        `[Akbura] Publish completed, but ${serverAssembly} was not produced.`
    );
    process.exit(1);
}

console.log(
    `[Akbura] Published ${configuration} language server to ${outputDirectory}`
);
