const assert = require('node:assert/strict');

const vscode = require('vscode');

async function run() {
    const extension = vscode.extensions.getExtension(
        'asaicraft.akbura-language-server'
    );

    assert.ok(extension, 'The Akbura extension was not installed.');
    assert.equal(
        extension.packageJSON.displayName,
        'Akbura Vs Code Extension'
    );
    assert.equal(extension.packageJSON.icon, 'icon.png');
    await extension.activate();

    const document = await vscode.workspace.openTextDocument({
        language: 'akbura',
        content: ''
    });
    const editor = await vscode.window.showTextDocument(document);

    await type('<');
    assert.equal(document.getText(), '<>');
    assert.equal(editor.selection.active.character, 1);

    for (const character of 'Button') {
        await type(character);
    }

    await type('>');
    assert.equal(document.getText(), '<Button></Button>');
    assert.equal(editor.selection.active.character, 8);

    await vscode.commands.executeCommand('undo');
    assert.equal(document.getText(), '<Button>');
    await vscode.commands.executeCommand('redo');
    assert.equal(document.getText(), '<Button></Button>');

    await replaceAll(editor, '');
    await type('{');
    assert.equal(document.getText(), '{}');
    assert.equal(editor.selection.active.character, 1);
    await vscode.commands.executeCommand(
        'akbura.typing.backspace'
    );
    assert.equal(document.getText(), '');

    const callPrefix = 'state object value = Call';
    await replaceAll(editor, callPrefix);
    await type('(');
    assert.equal(document.getText(), callPrefix + '()');
    assert.equal(
        editor.selection.active.character,
        callPrefix.length + 1
    );
    await type(')');
    assert.equal(document.getText(), callPrefix + '()');
    assert.equal(
        editor.selection.active.character,
        callPrefix.length + 2
    );

    await replaceAll(editor, callPrefix);
    await type('(');
    await vscode.commands.executeCommand(
        'akbura.typing.tab'
    );
    assert.equal(document.getText(), callPrefix + '()');
    assert.equal(
        editor.selection.active.character,
        callPrefix.length + 2
    );

    await replaceAll(editor, 'state string text = ');
    await type('"');
    assert.equal(
        document.getText(),
        'state string text = ""',
        `first quote: caret=${editor.selection.active.character}, ` +
            `version=${document.version}`
    );
    await type('"');
    assert.equal(
        document.getText(),
        'state string text = ""',
        `second quote: caret=${editor.selection.active.character}, ` +
            `version=${document.version}`
    );
    await type('"');
    assert.equal(
        document.getText(),
        'state string text = """"""',
        `third quote: caret=${editor.selection.active.character}, ` +
            `version=${document.version}`
    );
    assert.equal(editor.selection.active.character, 23);

    const newLine = document.eol === vscode.EndOfLine.CRLF
        ? '\r\n'
        : '\n';
    await vscode.commands.executeCommand(
        'akbura.typing.return'
    );
    assert.equal(
        document.getText(),
        'state string text = """' +
            newLine + '    ' + newLine + '"""'
    );

    await replaceAll(editor, '');
    await type('Привет');
    assert.equal(document.getText(), 'Привет');

    await replaceAll(editor, 'a\nb');
    editor.selections = [
        new vscode.Selection(0, 1, 0, 1),
        new vscode.Selection(1, 1, 1, 1)
    ];
    await type('/');
    assert.equal(
        document.getText(),
        'a/' + newLine + 'b/'
    );

    await replaceAll(editor, 'selected');
    editor.selection = new vscode.Selection(0, 0, 0, 8);
    await type('{');
    assert.equal(document.getText(), '{selected}');

    await replaceAll(editor, 'List');
    await vscode.commands.executeCommand(
        'type',
        {
            text: '<',
            replacePrevCharCnt: 1
        }
    );
    assert.equal(document.getText(), 'List<');

    await replaceAll(editor, '');
    await type('{');
    assert.equal(document.getText(), '{}');
    await vscode.commands.executeCommand(
        'akbura.restartLanguageServer'
    );
    await type('}');
    assert.equal(document.getText(), '{}}');

    await vscode.commands.executeCommand(
        'workbench.action.closeActiveEditor'
    );

    await verifyUnsavedResourceCompletion();
}

async function verifyUnsavedResourceCompletion() {
    const folder = vscode.workspace.workspaceFolders?.[0];
    assert.ok(folder, 'The acceptance workspace was not opened.');
    const componentUri = vscode.Uri.joinPath(
        folder.uri,
        'Component.akbura'
    );
    const appUri = vscode.Uri.joinPath(folder.uri, 'App.axaml');
    const component = await vscode.workspace.openTextDocument(
        componentUri
    );
    const marker = '${StaticResource ';
    const offset = component.getText().indexOf(marker);
    assert.ok(offset >= 0, 'Resource completion marker is missing.');
    const position = component.positionAt(offset + marker.length);

    const diskCompletion = await waitForCompletion(
        componentUri,
        position,
        'DiskResource'
    );
    assert.equal(
        diskCompletion.items.some(item => item.label === 'LiveResource'),
        false
    );

    const app = await vscode.workspace.openTextDocument(appUri);
    const appEditor = await vscode.window.showTextDocument(app);
    await new Promise(resolve => setTimeout(resolve, 500));
    const keyStart = app.getText().indexOf('DiskResource');
    assert.ok(keyStart >= 0, 'Disk resource key is missing.');
    const applied = await appEditor.edit(edit => edit.replace(
        new vscode.Range(
            app.positionAt(keyStart),
            app.positionAt(keyStart + 'DiskResource'.length)
        ),
        'LiveResource'
    ));
    assert.equal(applied, true);
    assert.equal(app.isDirty, true);

    await vscode.window.showTextDocument(component);
    const liveCompletion = await waitForCompletion(
        componentUri,
        position,
        'LiveResource',
        'DiskResource'
    );
    const liveItem = liveCompletion.items.find(
        item => item.label === 'LiveResource'
    );
    assert.ok(liveItem);
    assert.equal(
        (liveItem.commitCharacters ?? []).includes('.'),
        false
    );
    assert.equal(
        (liveItem.commitCharacters ?? []).includes('-'),
        false
    );

    await vscode.window.showTextDocument(app);
    await vscode.commands.executeCommand(
        'workbench.action.files.revert'
    );
    assert.equal(app.isDirty, false);
    await vscode.commands.executeCommand(
        'workbench.action.closeActiveEditor'
    );
    await vscode.window.showTextDocument(component);
    await vscode.commands.executeCommand(
        'workbench.action.closeActiveEditor'
    );
}

async function waitForCompletion(
    uri,
    position,
    expectedLabel,
    rejectedLabel
) {
    const deadline = Date.now() + 60_000;
    let lastLabels = [];
    while (Date.now() < deadline) {
        const result = await vscode.commands.executeCommand(
            'vscode.executeCompletionItemProvider',
            uri,
            position
        );
        lastLabels = result?.items.map(item => item.label) ?? [];
        if (lastLabels.includes(expectedLabel) &&
            (rejectedLabel == null ||
                !lastLabels.includes(rejectedLabel))) {
            return result;
        }

        await new Promise(resolve => setTimeout(resolve, 250));
    }

    assert.fail(
        `Completion '${expectedLabel}' was not returned. ` +
            `Last labels: ${lastLabels.join(', ')}`
    );
}

async function type(text) {
    await vscode.commands.executeCommand(
        'type',
        { text }
    );
}

async function replaceAll(editor, text) {
    const document = editor.document;
    const end = document.positionAt(document.getText().length);
    const applied = await editor.edit(
        edit => edit.replace(
            new vscode.Range(new vscode.Position(0, 0), end),
            text
        ),
        {
            undoStopBefore: true,
            undoStopAfter: true
        }
    );

    assert.equal(applied, true);
    const position = document.positionAt(text.length);
    editor.selection = new vscode.Selection(position, position);
}

module.exports = { run };
