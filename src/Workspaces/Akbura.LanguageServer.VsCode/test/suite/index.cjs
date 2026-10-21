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
    editor.options = { insertSpaces: true, tabSize: 4 };
    const newLine = document.eol === vscode.EndOfLine.CRLF
        ? '\r\n'
        : '\n';

    await type('<');
    assert.equal(document.getText(), '<>');
    assert.equal(editor.selection.active.character, 1);

    for (const character of 'Button') {
        await type(character);
    }

    await type('>');
    const pairedTag = '<Button>' + newLine + '    ' + newLine + '</Button>';
    assert.equal(document.getText(), pairedTag);
    assert.equal(editor.selection.active.line, 1);
    assert.equal(editor.selection.active.character, 4);

    await vscode.commands.executeCommand('undo');
    assert.equal(document.getText(), '<Button>');
    await vscode.commands.executeCommand('redo');
    assert.equal(document.getText(), pairedTag);

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
    await verifyExpressionBindings();
}

async function verifyExpressionBindings() {
    const folder = vscode.workspace.workspaceFolders?.[0];
    assert.ok(folder);
    const uri = vscode.Uri.joinPath(folder.uri, 'Component.akbura');
    const document = await vscode.workspace.openTextDocument(uri);
    const editor = await vscode.window.showTextDocument(document);
    const prefix = 'using Avalonia.Controls;\r\ninject Gallery.ExpressionBindingViewModel Vm;\r\n';
    await replaceAll(editor, prefix + '<TextBlock Text=${x.bi} />');
    let position = document.positionAt(document.getText().indexOf('x.bi') + 4);
    let completion = await waitForCompletion(uri, position, 'x.bind');
    const binding = completion.items.find(item => item.label === 'x.bind');
    assert.ok(binding.textEdit);
    await editor.edit(edit => edit.replace(binding.textEdit.range, binding.textEdit.newText));
    assert.ok(document.getText().includes('Text=${x.bind }'));
    assert.equal(document.getText().includes('${${'), false);

    await replaceAll(editor, prefix + '<TextBlock Text=${x.bind Vm.Na' + 'me} />');
    position = document.positionAt(document.getText().indexOf('Vm.Name') + 'Vm.Na'.length);
    completion = await waitForCompletion(uri, position, 'Name');
    const name = completion.items.find(item => item.label === 'Name');
    assert.ok(name.textEdit);
    await editor.edit(edit => edit.replace(name.textEdit.range, name.textEdit.newText));
    assert.ok(document.getText().includes('${x.bind Vm.Name}'));
    assert.equal(document.getText().includes('NameName'), false);

    position = document.positionAt(document.getText().indexOf('Vm.Name') + 4);
    const hovers = await vscode.commands.executeCommand('vscode.executeHoverProvider', uri, position);
    assert.ok(hovers?.length, 'No expression-binding C# hover returned.');
    const definitions = await vscode.commands.executeCommand('vscode.executeDefinitionProvider', uri, position);
    assert.ok(definitions?.length, 'No expression-binding definition returned.');

    // Real typing requests go through the same running language server.
    await replaceAll(editor, prefix + '<TextBlock Text=${x.bind Vm.Format} />');
    position = document.positionAt(document.getText().indexOf('Vm.Format') + 'Vm.Format'.length);
    editor.selection = new vscode.Selection(position, position);
    await type('(');
    assert.ok(document.getText().includes('${x.bind Vm.Format()}'));

    await replaceAll(editor, prefix + '<TextBlock Text=${x.bind $"Hello "} />');
    position = document.positionAt(document.getText().indexOf('Hello ') + 6);
    editor.selection = new vscode.Selection(position, position);
    await type('{');
    assert.ok(document.getText().includes('${x.bind $"Hello {}"}'));
    await type('}');
    assert.equal(document.getText().includes('Hello {}}'), false);

    await vscode.commands.executeCommand('workbench.action.files.revert');
    await vscode.commands.executeCommand('workbench.action.closeActiveEditor');
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
