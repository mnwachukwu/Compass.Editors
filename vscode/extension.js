'use strict';

// The editor's half of debugging Profi-C.
//
// Everything about *how* to debug — where to stop, what counts as one step, which names to show
// — lives in the Profi-C repository, in an adapter that speaks the Debug Adapter Protocol over
// its standard input and output. None of it is repeated here, and none of it should be: two
// implementations of the same decisions is two answers to every question about them.
//
// What is left is the little VS Code cannot do declaratively. A manifest can say a debugger
// exists, but it cannot say "run whatever `pc` the reader has installed" — the path it names is
// resolved inside the extension folder, and the compiler is not in there. So this file exists to
// answer two questions: which command to run, and what to debug when nobody has written a
// launch.json.

const vscode = require('vscode');

/**
 * The debugger's type.
 *
 * The same string is `contributes.debuggers[].type` in package.json, and the two must agree:
 * VS Code matches a launch configuration to a factory by this name. A mismatch shows up as a
 * debugger that is offered in the menu and then quietly does nothing, which is a bad way to
 * find out about a typo.
 */
const DebuggerType = 'profi-c';

/**
 * The setting naming the compiler, matching `contributes.configuration` in package.json.
 */
const CompilerPathSetting = 'profi-c.compilerPath';

/** What to run when nothing says otherwise: the command a `dotnet tool` install puts on PATH. */
const DefaultCompiler = 'pc';

/** The languages a debug session may be started from. A project file is launchable too. */
const Debuggable = ['profi-c', 'profi-c-project'];

function activate(context) {
    context.subscriptions.push(
        vscode.debug.registerDebugConfigurationProvider(DebuggerType, {
            resolveDebugConfiguration: debugWhatIsOpenWhereNothingSaysOtherwise,
        }),
        vscode.debug.registerDebugAdapterDescriptorFactory(DebuggerType, {
            createDebugAdapterDescriptor: startTheCompilersAdapter,
        }));
}

function deactivate() {
}

/**
 * Fills in a configuration for someone who pressed F5 without writing one.
 *
 * VS Code passes an empty configuration when there is no launch.json, and this is where that
 * becomes "debug the file in front of me". Worth doing rather than leaving: a teaching language
 * wants opening a program and running it to work, and requiring a launch.json first asks a
 * beginner to learn the editor before the language.
 */
function debugWhatIsOpenWhereNothingSaysOtherwise(folder, configuration) {
    if (configuration.type || configuration.request || configuration.name) {
        return configuration;
    }

    const open = vscode.window.activeTextEditor;

    if (!open || !Debuggable.includes(open.document.languageId)) {
        // Undefined stops the session without reporting anything, which is right: nothing is
        // broken, there is simply no Profi-C program in front of the reader to run.
        return undefined;
    }

    return {
        type: DebuggerType,
        request: 'launch',
        name: 'Debug the open file',

        // The path itself rather than '${file}'. VS Code would substitute that a moment later
        // and the two are the same here, but a configuration nobody wrote is also a
        // configuration nobody can read, and an unsubstituted variable reaching the compiler
        // reads as a file that does not exist.
        program: open.document.uri.fsPath,
    };
}

/**
 * Starts the compiler's own debug adapter and lets VS Code talk to it.
 *
 * `pc debug` reads the protocol on its standard input and writes it back out, which is exactly
 * the shape VS Code expects of an executable adapter. So nothing is translated here and no
 * second implementation of the protocol exists in JavaScript.
 */
function startTheCompilersAdapter(session) {
    const configured = vscode.workspace.getConfiguration().get(CompilerPathSetting);
    const command = session.configuration.compilerPath || configured || DefaultCompiler;

    return new vscode.DebugAdapterExecutable(command, ['debug'], {
        // Started where the workspace is, so that a program written as a relative path in
        // launch.json means what it looks like it means. Without this the adapter inherits
        // whatever directory the editor happened to be launched from.
        cwd: session.workspaceFolder ? session.workspaceFolder.uri.fsPath : undefined,
    });
}

module.exports = { activate, deactivate };
