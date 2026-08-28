import { execFile } from 'node:child_process';
import * as fs from 'node:fs';
import * as os from 'node:os';
import * as path from 'node:path';
import { promisify } from 'node:util';

import * as vscode from 'vscode';

import { AkburaTypingController } from './editor/AkburaTypingController.js';

import {
    Executable,
    LanguageClient,
    LanguageClientOptions,
    RevealOutputChannelOn,
    ServerOptions,
    TransportKind
} from 'vscode-languageclient/node';

const execFileAsync = promisify(execFile);

const clientId = 'akbura';
const clientName = 'Akbura Language Server';

let client: LanguageClient | undefined;
let outputChannel: vscode.OutputChannel;
let activeLaunchKey: string | undefined;
let lifecycleTail: Promise<void> = Promise.resolve();
let configurationUpdatesInProgress = 0;

const validatedDotnetHosts = new Set<string>();

interface ServerLaunch {
    serverOptions: ServerOptions;
    key: string;
    description: string;
    workspaceFolder: vscode.WorkspaceFolder | undefined;
    workingDirectory: string;
    projectSelection: string;
    logLevel: string;
}

interface PathQuickPickItem extends vscode.QuickPickItem {
    uri: vscode.Uri;
}

export async function activate(
    context: vscode.ExtensionContext
): Promise<void> {
    outputChannel = vscode.window.createOutputChannel(
        'Akbura Language Server'
    );

    context.subscriptions.push(outputChannel);

    const typingController = new AkburaTypingController(
        () => client,
        outputChannel
    );

    context.subscriptions.push(
        typingController,
        vscode.commands.registerCommand(
            'type',
            argumentsValue =>
                typingController.type(
                    argumentsValue
                )
        ),
        vscode.commands.registerCommand(
            'akbura.typing.backspace',
            () => typingController.backspace()
        ),
        vscode.commands.registerCommand(
            'akbura.typing.tab',
            () => typingController.tab()
        ),
        vscode.commands.registerCommand(
            'akbura.typing.return',
            () => typingController.return()
        )
    );

    context.subscriptions.push(
        vscode.commands.registerCommand(
            'akbura.restartLanguageServer',
            () => runSafely(
                () => restartLanguageServer(
                    context,
                    true
                )
            )
        )
    );

    context.subscriptions.push(
        vscode.commands.registerCommand(
            'akbura.showLanguageServerOutput',
            () => outputChannel.show(true)
        )
    );

    context.subscriptions.push(
        vscode.commands.registerCommand(
            'akbura.selectSolution',
            () => runSafely(
                () => selectSolution(context)
            )
        )
    );

    context.subscriptions.push(
        vscode.commands.registerCommand(
            'akbura.selectProject',
            () => runSafely(
                () => selectProject(context)
            )
        )
    );

    context.subscriptions.push(
        vscode.commands.registerCommand(
            'akbura.clearProjectSelection',
            () => runSafely(
                () => clearProjectSelection(context)
            )
        )
    );

    context.subscriptions.push(
        vscode.workspace.onDidChangeConfiguration(
            event => {
                if (configurationUpdatesInProgress !== 0) {
                    return;
                }

                if (!event.affectsConfiguration(
                        'akbura.server')) {
                    return;
                }

                void runSafely(
                    () => restartLanguageServer(
                        context,
                        false
                    )
                );
            }
        )
    );

    await runSafely(
        () => restartLanguageServer(
            context,
            true
        )
    );
}

export async function deactivate(): Promise<void> {
    await enqueueLifecycle(
        async () => {
            const current = client;

            client = undefined;
            activeLaunchKey = undefined;

            if (current != null) {
                await current.stop();
            }
        }
    );
}

function restartLanguageServer(
    context: vscode.ExtensionContext,
    force: boolean
): Promise<void> {
    return enqueueLifecycle(
        async () => {
            /*
             * Resolve and validate the new launch before stopping the
             * currently working client. A malformed setting therefore
             * does not immediately kill an already running server.
             */
            const launch = await createServerLaunch(
                context
            );

            if (!force &&
                client != null &&
                activeLaunchKey === launch.key) {
                return;
            }

            const previous = client;

            client = undefined;
            activeLaunchKey = undefined;

            if (previous != null) {
                try {
                    await previous.stop();
                } catch (error) {
                    outputChannel.appendLine(
                        '[Akbura] Previous language server ' +
                        `could not be stopped cleanly: ${formatError(error)}`
                    );
                }
            }

            outputChannel.appendLine(
                `[Akbura] Starting: ${launch.description}`
            );

            outputChannel.appendLine(
                '[Akbura] Workspace folder: ' +
                (launch.workspaceFolder?.uri.fsPath ??
                    '<multiple or none>')
            );
            outputChannel.appendLine(
                '[Akbura] Working directory: ' +
                launch.workingDirectory
            );
            outputChannel.appendLine(
                '[Akbura] Project selection: ' +
                launch.projectSelection
            );
            outputChannel.appendLine(
                '[Akbura] Server log level: ' +
                launch.logLevel
            );

            const clientOptions =
                createLanguageClientOptions(launch);

            const next = new LanguageClient(
                clientId,
                clientName,
                launch.serverOptions,
                clientOptions
            );

            client = next;

            try {
                await next.start();
                activeLaunchKey = launch.key;

                outputChannel.appendLine(
                    '[Akbura] Language server started.'
                );
            } catch (error) {
                client = undefined;
                activeLaunchKey = undefined;
                throw error;
            }
        }
    );
}

function createLanguageClientOptions(
    launch: ServerLaunch
):
    LanguageClientOptions {
    return {
        workspaceFolder: launch.workspaceFolder,
        documentSelector: [
            {
                scheme: 'file',
                language: 'akbura'
            },
            {
                scheme: 'untitled',
                language: 'akbura'
            },
            {
                scheme: 'file',
                language: 'akcss'
            },
            {
                scheme: 'untitled',
                language: 'akcss'
            }
        ],
        diagnosticCollectionName: 'akbura',
        outputChannel,
        revealOutputChannelOn:
            RevealOutputChannelOn.Error

        /*
         * Do not add synchronize.fileEvents here.
         *
         * Akbura.LanguageServer dynamically registers its own file
         * watchers after the initialized notification. Registering
         * the same watchers here would produce duplicate
         * workspace/didChangeWatchedFiles notifications.
         */
    };
}

async function createServerLaunch(
    context: vscode.ExtensionContext
): Promise<ServerLaunch> {
    const configuration =
        vscode.workspace.getConfiguration('akbura');

    const workspaceDirectory =
        getWorkspaceDirectory(context);
    const fileWorkspaceFolders =
        vscode.workspace.workspaceFolders
            ?.filter(folder =>
                folder.uri.scheme === 'file'
            ) ?? [];
    const workspaceFolder =
        fileWorkspaceFolders.length === 1
            ? fileWorkspaceFolders[0]
            : undefined;

    const configuredServerPath =
        configuration
            .get<string>('server.path', '')
            .trim();

    const serverPath =
        configuredServerPath.length === 0
            ? context.asAbsolutePath(
                path.join(
                    'server',
                    'akbura-lsp.dll'
                )
            )
            : resolveSettingPath(
                configuredServerPath,
                workspaceDirectory,
                context.extensionPath
            );

    await assertExistingFile(
        serverPath,
        'Akbura language server'
    );

    const solutionPath = resolveOptionalPath(
        configuration.get<string>(
            'server.solution',
            ''
        ),
        workspaceDirectory,
        context.extensionPath
    );

    const projectPath = resolveOptionalPath(
        configuration.get<string>(
            'server.project',
            ''
        ),
        workspaceDirectory,
        context.extensionPath
    );

    if (solutionPath != null &&
        projectPath != null) {
        throw new Error(
            'Configure either akbura.server.solution ' +
            'or akbura.server.project, not both.'
        );
    }

    if (solutionPath != null) {
        await assertExistingFile(
            solutionPath,
            'Selected solution'
        );
    }

    if (projectPath != null) {
        await assertExistingFile(
            projectPath,
            'Selected project'
        );
    }

    const explicitProjectPath =
        solutionPath ?? projectPath;

    /*
     * Using the solution/project directory as cwd also makes
     * dotnet respect the correct global.json.
     */
    const workingDirectory =
        explicitProjectPath == null
            ? workspaceDirectory
            : path.dirname(explicitProjectPath);

    const logLevel = getServerLogLevel(
        context,
        configuration
    );

    const logFile = resolveOptionalPath(
        configuration.get<string>(
            'server.logFile',
            ''
        ),
        workingDirectory,
        context.extensionPath
    );

    const waitForDebugger =
        configuration.get<boolean>(
            'server.waitForDebugger',
            false
        );

    const serverArguments = [
        '--stdio',
        '--clientProcessId',
        String(process.pid),
        '--log-level',
        logLevel
    ];

    if (solutionPath != null) {
        serverArguments.push(
            '--solution',
            solutionPath
        );
    }

    if (projectPath != null) {
        serverArguments.push(
            '--project',
            projectPath
        );
    }

    if (logFile != null) {
        serverArguments.push(
            '--log-file',
            logFile
        );
    }

    if (waitForDebugger) {
        serverArguments.push(
            '--wait-for-debugger'
        );
    }

    const environment: NodeJS.ProcessEnv = {
        ...process.env,
        DOTNET_NOLOGO: '1',
        DOTNET_CLI_TELEMETRY_OPTOUT: '1',
        DOTNET_SKIP_FIRST_TIME_EXPERIENCE: '1'
    };

    let executable: Executable;

    if (path.extname(serverPath)
            .toLowerCase() === '.dll') {
        const configuredDotnet =
            configuration
                .get<string>(
                    'server.dotnetPath',
                    'dotnet'
                )
                .trim();

        const dotnetCommand =
            resolveCommand(
                configuredDotnet.length === 0
                    ? 'dotnet'
                    : configuredDotnet,
                workspaceDirectory,
                context.extensionPath
            );

        await ensureDotnetSdk(
            dotnetCommand,
            workingDirectory
        );

        executable = {
            command: dotnetCommand,
            args: [
                serverPath,
                ...serverArguments
            ],
            transport: TransportKind.stdio,
            options: {
                cwd: workingDirectory,
                env: environment,
                shell: false
            }
        };
    } else {
        executable = {
            command: serverPath,
            args: serverArguments,
            transport: TransportKind.stdio,
            options: {
                cwd: workingDirectory,
                env: environment,
                shell: false
            }
        };
    }

    const launchArguments =
        executable.args ?? [];

    const key = JSON.stringify({
        command: executable.command,
        args: launchArguments,
        cwd: workingDirectory
    });

    return {
        serverOptions: executable,
        key,
        workspaceFolder,
        workingDirectory,
        projectSelection:
            explicitProjectPath ?? '<automatic discovery>',
        logLevel,
        description: [
            quoteForLog(executable.command),
            ...launchArguments.map(quoteForLog)
        ].join(' ')
    };
}

function getServerLogLevel(
    context: vscode.ExtensionContext,
    configuration: vscode.WorkspaceConfiguration
): string {
    const inspected = configuration.inspect<string>(
        'server.logLevel'
    );
    const explicitlyConfigured =
        inspected?.globalValue !== undefined ||
        inspected?.workspaceValue !== undefined ||
        inspected?.workspaceFolderValue !== undefined;

    if (context.extensionMode ===
            vscode.ExtensionMode.Development &&
        !explicitlyConfigured) {
        return 'trace';
    }

    return configuration.get<string>(
        'server.logLevel',
        'warning'
    );
}

async function ensureDotnetSdk(
    dotnetCommand: string,
    workingDirectory: string
): Promise<void> {
    const key =
        `${dotnetCommand}|${workingDirectory}`;

    if (validatedDotnetHosts.has(key)) {
        return;
    }

    let version: string;

    try {
        const result = await execFileAsync(
            dotnetCommand,
            [
                '--version'
            ],
            {
                cwd: workingDirectory,
                windowsHide: true,
                encoding: 'utf8'
            }
        );

        version = String(result.stdout).trim();
    } catch (error) {
        throw new Error(
            `Unable to execute '${dotnetCommand} --version'. ` +
            'Install the .NET 10 SDK or configure ' +
            'akbura.server.dotnetPath.',
            {
                cause: error
            }
        );
    }

    const major = Number.parseInt(
        version.split('.')[0] ?? '',
        10
    );

    if (!Number.isFinite(major) ||
        major < 10) {
        throw new Error(
            'Akbura Language Server targets .NET 10, ' +
            `but '${dotnetCommand} --version' returned '${version}'.`
        );
    }

    validatedDotnetHosts.add(key);

    outputChannel.appendLine(
        `[Akbura] Using .NET SDK ${version}.`
    );
}

async function selectSolution(
    context: vscode.ExtensionContext
): Promise<void> {
    const uri = await pickWorkspaceFile(
        '**/*.{sln,slnx}',
        'Select a solution for Akbura'
    );

    if (uri == null) {
        return;
    }

    await updateProjectSelection(
        context,
        uri.fsPath,
        undefined
    );
}

async function selectProject(
    context: vscode.ExtensionContext
): Promise<void> {
    const uri = await pickWorkspaceFile(
        '**/*.csproj',
        'Select a project for Akbura'
    );

    if (uri == null) {
        return;
    }

    await updateProjectSelection(
        context,
        undefined,
        uri.fsPath
    );
}

async function clearProjectSelection(
    context: vscode.ExtensionContext
): Promise<void> {
    await updateProjectSelection(
        context,
        undefined,
        undefined
    );
}

async function updateProjectSelection(
    context: vscode.ExtensionContext,
    solutionPath: string | undefined,
    projectPath: string | undefined
): Promise<void> {
    const configuration =
        vscode.workspace.getConfiguration('akbura');

    const target =
        getConfigurationTarget();

    configurationUpdatesInProgress++;

    try {
        /*
         * Clear the opposite option first. This keeps every
         * intermediate configuration valid.
         */
        if (projectPath != null) {
            await configuration.update(
                'server.solution',
                '',
                target
            );

            await configuration.update(
                'server.project',
                projectPath,
                target
            );
        } else {
            await configuration.update(
                'server.project',
                '',
                target
            );

            await configuration.update(
                'server.solution',
                solutionPath ?? '',
                target
            );
        }
    } finally {
        configurationUpdatesInProgress--;
    }

    await restartLanguageServer(
        context,
        true
    );
}

async function pickWorkspaceFile(
    include: string,
    placeHolder: string
): Promise<vscode.Uri | undefined> {
    if ((vscode.workspace.workspaceFolders
            ?.length ?? 0) === 0) {
        await vscode.window.showWarningMessage(
            'Open a workspace folder before selecting an Akbura project.'
        );

        return undefined;
    }

    const uris = await vscode.workspace.findFiles(
        include,
        '**/{.git,.vs,node_modules,bin,obj}/**',
        200
    );

    if (uris.length === 0) {
        await vscode.window.showWarningMessage(
            `No files matching '${include}' were found.`
        );

        return undefined;
    }

    const items: PathQuickPickItem[] =
        uris
            .map(uri => ({
                label: path.basename(uri.fsPath),
                description:
                    vscode.workspace.asRelativePath(
                        uri,
                        false
                    ),
                uri
            }))
            .sort(
                (left, right) =>
                    (left.description ?? left.label)
                        .localeCompare(
                            right.description ??
                            right.label
                        )
            );

    const selected =
        await vscode.window.showQuickPick(
            items,
            {
                placeHolder,
                matchOnDescription: true
            }
        );

    return selected?.uri;
}

function getConfigurationTarget():
    vscode.ConfigurationTarget {
    return vscode.workspace.workspaceFile != null ||
        (vscode.workspace.workspaceFolders
            ?.length ?? 0) !== 0
        ? vscode.ConfigurationTarget.Workspace
        : vscode.ConfigurationTarget.Global;
}

function getWorkspaceDirectory(
    context: vscode.ExtensionContext
): string {
    const fileFolder =
        vscode.workspace.workspaceFolders
            ?.find(folder =>
                folder.uri.scheme === 'file'
            );

    return fileFolder?.uri.fsPath ??
        context.extensionPath;
}

function resolveOptionalPath(
    value: string | undefined,
    workspaceDirectory: string,
    extensionDirectory: string
): string | undefined {
    const trimmed = value?.trim();

    return trimmed == null ||
        trimmed.length === 0
        ? undefined
        : resolveSettingPath(
            trimmed,
            workspaceDirectory,
            extensionDirectory
        );
}

function resolveSettingPath(
    value: string,
    workspaceDirectory: string,
    extensionDirectory: string
): string {
    let expanded = value
        .replaceAll(
            '${workspaceFolder}',
            workspaceDirectory
        )
        .replaceAll(
            '${extensionPath}',
            extensionDirectory
        );

    expanded = expanded.replace(
        /^~(?=$|[\\/])/,
        os.homedir()
    );

    return path.isAbsolute(expanded)
        ? path.normalize(expanded)
        : path.resolve(
            workspaceDirectory,
            expanded
        );
}

function resolveCommand(
    value: string,
    workspaceDirectory: string,
    extensionDirectory: string
): string {
    const looksLikePath =
        path.isAbsolute(value) ||
        value.includes('/') ||
        value.includes('\\') ||
        value.startsWith('~');

    return looksLikePath
        ? resolveSettingPath(
            value,
            workspaceDirectory,
            extensionDirectory
        )
        : value;
}

async function assertExistingFile(
    filePath: string,
    description: string
): Promise<void> {
    try {
        const information =
            await fs.promises.stat(filePath);

        if (!information.isFile()) {
            throw new Error(
                'The path does not identify a file.'
            );
        }
    } catch (error) {
        throw new Error(
            `${description} was not found: ${filePath}. ` +
            'Run npm run build:debug or correct the Akbura settings.',
            {
                cause: error
            }
        );
    }
}

function quoteForLog(value: string): string {
    return /\s/.test(value)
        ? JSON.stringify(value)
        : value;
}

function enqueueLifecycle(
    action: () => Promise<void>
): Promise<void> {
    const operation =
        lifecycleTail.then(
            action,
            action
        );

    lifecycleTail = operation.then(
        () => undefined,
        () => undefined
    );

    return operation;
}

async function runSafely(
    action: () => Promise<void>
): Promise<void> {
    try {
        await action();
    } catch (error) {
        reportError(error);
    }
}

function reportError(error: unknown): void {
    const message = formatError(error);

    outputChannel.appendLine(
        `[Akbura] Error: ${message}`
    );

    if (error instanceof Error &&
        error.stack != null) {
        outputChannel.appendLine(
            error.stack
        );
    }

    outputChannel.show(true);

    void vscode.window
        .showErrorMessage(
            `Akbura: ${message}`,
            'Show Output'
        )
        .then(selection => {
            if (selection === 'Show Output') {
                outputChannel.show(true);
            }
        });
}

function formatError(error: unknown): string {
    return error instanceof Error
        ? error.message
        : String(error);
}
