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
const palette = require('./palette');
const projects = require('./projects');

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

/**
 * How the compiler writes every diagnostic: MSBuild's canonical form.
 *
 * The same shape the problem matchers in package.json read, and deliberately the same — a build
 * and a run disagreeing about how to read one message would be two parsers to keep true.
 */
const Diagnostic = /^(.*)\((\d+),(\d+)\): (error|warning|opinion) (PC\d+): (.*)$/;

/** Where a refused run puts what the compiler said. See {@link problemsPanel}. */
let collected;

/**
 * The connection to `pc lsp`, or undefined where there is none.
 *
 * Undefined is an ordinary state rather than a failure: a compiler too old to know the command
 * has no server to connect to, and everything else in this extension goes on working without one.
 */
let client;

/**
 * What VS Code handed to `activate`, kept so the server can be started again later.
 *
 * The server is started once at activation and again whenever somebody restarts it — and the
 * only thing starting it needs the context for is registering the outline that stands in when no
 * server is there. Holding it here is what lets a command start one without being handed it.
 */
let activation;

/**
 * The collection a refused run writes into, made the first time there is something to write.
 *
 * Made on demand rather than at activation so that the function that writes to it is the one
 * that makes it, and nothing depends on the order the two happened in.
 */
function problemsPanel() {
    collected ??= vscode.languages.createDiagnosticCollection('profi-c');
    return collected;
}

function activate(context) {
    activation = context;

    context.subscriptions.push(
        problemsPanel(),
        vscode.debug.registerDebugConfigurationProvider(DebuggerType, {
            resolveDebugConfiguration: debugWhatIsOpenWhereNothingSaysOtherwise,
        }),

        // Offered in the Run and Debug list without a launch.json existing. This is what makes
        // the extension carry its own configurations instead of every folder needing a file
        // copied into it.
        vscode.debug.registerDebugConfigurationProvider(
            DebuggerType,
            { provideDebugConfigurations: offerTheUsualWaysToRun },
            vscode.DebugConfigurationProviderTriggerKind.Dynamic),

        vscode.debug.registerDebugAdapterDescriptorFactory(DebuggerType, {
            createDebugAdapterDescriptor: startTheCompilersAdapter,
        }),

        vscode.debug.onDidReceiveDebugSessionCustomEvent(answerTheProgram),

        vscode.commands.registerCommand('profi-c.runFile', file => start(file, ThisFile)),
        vscode.commands.registerCommand('profi-c.runProject', file => start(file, TheProject)),
        vscode.commands.registerCommand('profi-c.buildFile', file => build(file, ThisFile)),
        vscode.commands.registerCommand('profi-c.buildProject', file => build(file, TheProject)),
        vscode.commands.registerCommand('profi-c.newProject', newProject),
        vscode.commands.registerCommand('profi-c.addToProject', file => listFile(file, true)),
        vscode.commands.registerCommand('profi-c.removeFromProject', file => listFile(file, false)),
        vscode.commands.registerCommand('profi-c.setEntryPoint', setEntryPoint),
        vscode.commands.registerCommand('profi-c.setOutputFolder', setOutputFolder),
        vscode.commands.registerCommand('profi-c.chooseTarget', chooseTarget),
        vscode.commands.registerCommand('profi-c.useTheColors', useTheColors),
        vscode.commands.registerCommand('profi-c.stopTheServer', stopTheServer),
        vscode.commands.registerCommand('profi-c.restartTheServer', restartTheServer),

        // Offered to tasks.json as well, so a project can pin a build the way it likes and
        // Ctrl+Shift+B finds one without anybody writing the command line out.
        vscode.tasks.registerTaskProvider(DebuggerType, {
            provideTasks: offerTheUsualBuilds,
            resolveTask: fillInTheRest,
        }),

        showTheTarget(context));

    startTheServer(context);
}

/**
 * Connects to the compiler's language server, which answers about files as they are being typed.
 *
 * **Why a server rather than another command.** Everything else here runs `pc` and waits: the
 * outline, the project a file belongs to, the check before a run. Each reads the file *from
 * disk*, which is the only thing a separate process can do — so none of them can say anything
 * about the buffer in front of the reader, and half of what is in that buffer at any moment is
 * not valid Profi-C anyway. A server holds what the editor holds, and is told about each edit.
 *
 * It also removes what dominates the cost. A whole-file re-analysis is a few milliseconds;
 * starting a process to do it is a few hundred, every time.
 *
 * **Failing to start is not an error to report.** A compiler that predates the command answers
 * that it does not know it, and everything else in this extension goes on working — highlighting
 * is declarative, and running and building are their own commands. Somebody who has not upgraded
 * should not get a dialog about a feature they have never seen. The client's own output channel
 * records what happened for anyone looking.
 */
function startTheServer(context) {
    const { LanguageClient } = require('vscode-languageclient/node');

    // Both halves are the same command. A server started differently from the compiler that
    // builds would answer about a different language than the one that runs.
    const run = { command: compiler(), args: ['lsp'] };

    client = new LanguageClient(
        'profi-c',
        'Profi-C',
        { run, debug: run },
        {
            documentSelector: Debuggable.map(language => ({ scheme: 'file', language })),

            // Diagnostics for a file arrive against that file, so the panel is grouped the way
            // a reader expects even when the mistake is in something they never opened.
            diagnosticCollectionName: 'profi-c',

            // What the reader asked to be shown, sent at startup and again whenever it changes.
            // Without the second half, turning a hint off takes effect at the next restart —
            // which is nowhere, for somebody flipping the switch to see what it does.
            initializationOptions: { 'profi-c': hintSettings() },
            synchronize: { configurationSection: 'profi-c' },
        });

    client.start().catch(() => {
        client = undefined;
        context.subscriptions.push(outlineWithoutAServer());
    });
}

/**
 * Stops the server, letting go of the compiler it is running.
 *
 * **This exists because a running server holds the file open.** On Windows a process cannot be
 * overwritten while it runs, so publishing a new `pc` over the one the server is using fails —
 * and the only way out was to close the editor, which is a poor answer for anyone working on the
 * compiler itself. Stop, publish, restart.
 *
 * Says what happened either way. A command that appears to do nothing is one somebody runs twice
 * and then stops trusting.
 */
async function stopTheServer() {
    if (!client) {
        vscode.window.showInformationMessage('Profi-C: the language server is not running.');
        return;
    }

    await client.stop();
    client = undefined;

    vscode.window.showInformationMessage(
        'Profi-C: the language server has stopped. The compiler can now be replaced.');
}

/**
 * Stops the server if it is running and starts it again, on whatever `pc` is there now.
 *
 * Started fresh rather than through the client's own restart, so that a compiler replaced since
 * the last start is the one that runs — and so that this works when nothing is running, which is
 * what somebody who has just stopped it wants.
 */
async function restartTheServer() {
    if (client) {
        await client.stop();
        client = undefined;
    }

    startTheServer(activation);

    vscode.window.showInformationMessage('Profi-C: the language server has restarted.');
}

/**
 * Breadcrumbs and the Outline view, for a compiler with no `lsp` command.
 *
 * **Registered only where the server did not start**, and that is the whole reason it still
 * exists. VS Code merges the answers of every provider for a language, so registering this
 * beside a running server would show every declaration twice.
 *
 * What it loses is what a separate process cannot have: `pc outline` reads the file from disk, so
 * a document with unsaved edits outlines as it was last saved. The server has no such limit, and
 * removing this entirely is right once no compiler in use predates `pc lsp`.
 */
function outlineWithoutAServer() {
    return vscode.languages.registerDocumentSymbolProvider(
        { language: 'profi-c' },
        { provideDocumentSymbols: outline });
}

/**
 * Stops the server when the editor closes, so it does not outlive what it was answering.
 *
 * Named `deactivate` because that is what VS Code calls: an extension that leaves a child process
 * running leaves one per window, and they are only noticed by somebody wondering what is using
 * their memory.
 */
function deactivate() {
    return client ? client.stop() : undefined;
}

/**
 * What a document declares, for breadcrumbs and the Outline view.
 *
 * Asked of the compiler rather than read here. A second parser in JavaScript would be a second
 * definition of the language, and the two would agree right up until they did not — which for an
 * outline shows as a member that quietly stops appearing, with nothing failing anywhere.
 *
 * The compiler is handed the file on disk, so a document with unsaved edits outlines as it was
 * last saved. That is a real limitation and the honest one to accept for now: passing the buffer
 * would mean a temporary file per keystroke, and the alternative that avoids both is a language
 * server.
 */
function outline(document) {
    if (document.isUntitled) {
        return [];
    }

    const asked = require('child_process').spawnSync(compiler(), ['outline', document.fileName], {
        encoding: 'utf8',
        timeout: 15000,
        windowsHide: true,
    });

    if (asked.error || asked.status !== 0) {
        // Silent on purpose. An outline is asked for constantly and by nobody in particular, so
        // a compiler that is missing or too old should not put a notice on screen every time a
        // file is opened — the run and build paths say so where somebody actually asked.
        return [];
    }

    try {
        return JSON.parse(asked.stdout).map(asSymbol);
    } catch {
        return [];
    }
}

/** One declaration, as the editor's idea of a symbol. */
function asSymbol(entry) {
    // The compiler counts lines and columns from one, as every Profi-C diagnostic does. The
    // editor counts from zero. The conversion belongs here, at the boundary, rather than in a
    // compiler that would then disagree with its own error messages.
    const range = new vscode.Range(
        entry.line - 1, entry.column - 1,
        entry.endLine - 1, entry.endColumn - 1);

    const symbol = new vscode.DocumentSymbol(
        entry.name,
        entry.detail || '',
        kindOf(entry.kind),
        range,

        // What gets revealed and selected when the entry is clicked. The start of the
        // declaration rather than its name, because the parser records where a declaration
        // begins and not where its name sits inside it. A point rather than a guess.
        new vscode.Range(entry.line - 1, entry.column - 1, entry.line - 1, entry.column - 1));

    symbol.children = (entry.children || []).map(asSymbol);

    return symbol;
}

/** What the editor draws beside each kind of declaration. */
function kindOf(kind) {
    switch (kind) {
        case 'namespace': return vscode.SymbolKind.Namespace;
        case 'model': return vscode.SymbolKind.Class;

        // A structure is held by value, which is what Struct means here as well.
        case 'structure': return vscode.SymbolKind.Struct;
        case 'enumeration': return vscode.SymbolKind.Enum;
        case 'enumMember': return vscode.SymbolKind.EnumMember;
        case 'constructor': return vscode.SymbolKind.Constructor;
        case 'field': return vscode.SymbolKind.Field;
        default: return vscode.SymbolKind.Method;
    }
}

/**
 * The platform every build targets, kept where a workspace can pin it.
 *
 * Empty means this machine, which is also what leaving `--runtime` off means — so the setting
 * says nothing rather than repeating what the compiler would work out, and a project moved
 * between machines keeps building for whichever one it is on.
 */
const TargetSetting = 'profi-c.targetPlatform';

function target() {
    return vscode.workspace.getConfiguration().get(TargetSetting) || '';
}

/**
 * Puts the target in the status bar, so what a build is aiming at is visible without opening a
 * menu — and stays right when the setting changes underneath.
 */
function showTheTarget(context) {
    const item = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Left, 0);

    item.command = 'profi-c.chooseTarget';
    item.tooltip = 'The platform Profi-C builds for. Click to change.';

    const paint = () => {
        const editor = vscode.window.activeTextEditor;
        const showing = editor && Debuggable.includes(editor.document.languageId);

        item.text = `$(tools) Profi-C: ${target() || 'this machine'}`;

        if (showing) {
            item.show();
        } else {
            item.hide();
        }
    };

    context.subscriptions.push(
        item,
        vscode.window.onDidChangeActiveTextEditor(paint),
        vscode.workspace.onDidChangeConfiguration(changed => {
            if (changed.affectsConfiguration(TargetSetting)) {
                paint();
            }
        }));

    paint();

    return item;
}

/** What a configuration runs: the file itself, or the project the file belongs to. */
const ThisFile = 'file';
const TheProject = 'project';

/**
 * Reveal the Debug Console when a session starts.
 *
 * What a program prints is the whole of what most Profi-C programs do, so a run whose output
 * lands in a panel nobody opened reads as a run that did nothing. Carried on the configuration
 * rather than set once somewhere, because it belongs to the act of running rather than to the
 * editor, and a hand-written launch.json inherits it by copying a line it can see.
 *
 * It does not fight the Problems panel a refused run opens: that path never starts a session.
 */
const ShowTheConsole = 'openOnSessionStart';

/**
 * Puts the reader where a waiting program is answered.
 *
 * A program is answered in the Debug Console, on the line at the foot of it. That box is meant for
 * evaluating an expression against a stopped program, which is a thing Profi-C has no way to write
 * — so the adapter gives it to whatever is waiting to read instead, and answering a program is
 * typing directly under the question it asked.
 *
 * Which leaves this with one job: the console is where the answer goes, so the console is where
 * the cursor should be. Nothing else happens here — the adapter is already waiting, and it goes on
 * waiting until a line arrives or the session ends.
 */
async function answerTheProgram(received) {
    if (received.event !== 'profi-c/read') {
        return;
    }

    try {
        await vscode.commands.executeCommand('workbench.panel.repl.view.focus');
    } catch {
        // An editor that has moved or renamed that view still shows the console; it is simply
        // not focused for them. Failing to help is not worth failing over.
    }
}

/**
 * The configurations offered when there is no launch.json.
 *
 * Named as the editor's own Run button names things, so that somebody arriving from C# reads
 * the same words for the same act.
 */
function offerTheUsualWaysToRun() {
    return [
        {
            type: DebuggerType,
            request: 'launch',
            name: 'Run this file',
            program: '${file}',
            internalConsoleOptions: ShowTheConsole,
        },
        {
            type: DebuggerType,
            request: 'launch',
            name: 'Run project associated with this file',
            program: '${file}',
            scope: TheProject,
            internalConsoleOptions: ShowTheConsole,
        },
    ];
}

/**
 * Starts a session for the file in front of the reader.
 *
 * Both buttons debug — there is one adapter and it always stops where breakpoints are set. What
 * differs is only what gets compiled: the file, or the project that claims it.
 */
async function start(file, scope) {
    const document = file instanceof vscode.Uri
        ? file
        : vscode.window.activeTextEditor && vscode.window.activeTextEditor.document.uri;

    if (!document) {
        return;
    }

    await saveWhatWillBeCompiled();

    if (!showProblems(checked(document.fsPath, scope))) {
        // Refused, and the reasons are in the Problems panel where every other language puts
        // them. Starting the session anyway would have the adapter refuse it a second time and
        // pile the same list into a dialog.
        await vscode.commands.executeCommand('workbench.actions.view.problems');
        return;
    }

    await vscode.debug.startDebugging(vscode.workspace.getWorkspaceFolder(document), {
        type: DebuggerType,
        request: 'launch',
        name: scope === TheProject ? 'Run project associated with this file' : 'Run this file',
        program: document.fsPath,
        scope,
        internalConsoleOptions: ShowTheConsole,
    });
}

/**
 * Builds the file in front of the reader, or the project that claims it.
 *
 * Run as a task rather than as a process of the extension's own, so that the output lands in a
 * terminal like every other language's build and the diagnostics land in the Problems panel —
 * the compiler already writes them in the form a problem matcher reads.
 */
async function build(file, scope) {
    const document = file instanceof vscode.Uri
        ? file
        : vscode.window.activeTextEditor && vscode.window.activeTextEditor.document.uri;

    if (!document) {
        return;
    }

    await saveWhatWillBeCompiled();

    // The same question Run asks, answered by the same code — so "no project lists this file"
    // cannot mean one thing when running and another when building.
    const program = scope === TheProject
        ? withTheProjectInstead({ program: document.fsPath, scope }).program
        : document.fsPath;

    await vscode.tasks.executeTask(buildTask(program, scope, target()));
}

/**
 * Saves every Profi-C file with unsaved edits, before anything is compiled.
 *
 * **The compiler reads files, not buffers.** Without this, pressing Run after fixing a mistake
 * compiles the mistake: the diagnostics are about text the reader can no longer see, and the
 * program that runs is the one they just changed away from. For a beginner that reads as the
 * language ignoring them, which is the worst thing a Run button can do.
 *
 * Saving rather than handing the buffer over is the choice, and it buys something: what ran is
 * what is on disk, so `pc run` in a terminal gives the same answer as the button. A buffer piped
 * to the compiler would produce a result nothing else could reproduce.
 *
 * Every Profi-C file rather than the open one, because a program is a compilation: editing
 * `Shelf.pc` and pressing Run in `Program.pc` has to compile the new `Shelf.pc`. And only
 * Profi-C files, since saving somebody's unrelated notes is not what they asked for.
 *
 * A file never saved is left alone. It has no path to compile and saving it would open a dialog
 * nobody asked for; running it was never going to work.
 */
async function saveWhatWillBeCompiled() {
    const unsaved = vscode.workspace.textDocuments.filter(
        document => document.isDirty
            && !document.isUntitled
            && Debuggable.includes(document.languageId));

    await Promise.all(unsaved.map(document => document.save()));
}

/**
 * Asks the compiler to check what is about to be run, and gives back what it said.
 *
 * Run before the debug session rather than after, because the adapter's own refusal arrives as a
 * failed launch and an editor has one way to show that: a dialog. The compiler already writes
 * diagnostics in the form the problem matchers read, so asking here costs one process and puts
 * them where a reader looks for them.
 *
 * The same program the session would debug, chosen the same way, so a run that is refused and a
 * run that is not are refused about the same files.
 */
function checked(program, scope) {
    const target = scope === TheProject
        ? withTheProjectInstead({ program, scope }).program
        : program;

    const asked = require('child_process').spawnSync(compiler(), ['check', target], {
        encoding: 'utf8',
        timeout: 60000,
        windowsHide: true,
    });

    // A compiler that will not start is not a program with mistakes in it. Nothing is reported
    // here, and the debug session goes ahead so that the editor says what it always says about a
    // debugger it could not launch.
    if (asked.error) {
        return undefined;
    }

    return readDiagnostics(`${asked.stderr || ''}`);
}

/**
 * Every diagnostic in a run of compiler output, as the pieces a Problems entry is made of.
 *
 * Anything that does not match is dropped rather than shown as a mysterious entry with no
 * position: the compiler writes summaries on the same stream, and "ok, 1 file, 1 type" is not
 * something to put in a panel of problems.
 */
function readDiagnostics(output) {
    return output
        .split(/\r?\n/)
        .map(line => Diagnostic.exec(line))
        .filter(Boolean)
        .map(([, file, line, column, severity, code, message]) => ({
            file,
            line: Number(line),
            column: Number(column),
            severity,
            code,
            message,
        }));
}

/**
 * Puts what the compiler said in the Problems panel, and answers whether the run may go ahead.
 *
 * Warnings and opinions are shown as well as errors, and do not stop anything — the compiler
 * runs a program that has them, so an editor that refused to would be answering a different
 * question than the compiler does.
 *
 * Given nothing at all, the panel is left exactly as it was. That is the case where the compiler
 * could not be asked, and clearing on the way past would erase a real answer from a moment ago.
 */
function showProblems(found) {
    if (!found) {
        return true;
    }

    // The server owns the panel while it is running, and has already put these there — it
    // publishes as the reader types rather than when they press a button. Writing them a second
    // time would show every problem twice, from two owners, and clearing one would leave the
    // other. The verdict below is still this one's to give: it is what decides whether to start.
    if (client) {
        return !found.some(one => one.severity === 'error');
    }

    problemsPanel().clear();

    // Gathered by file before being set, because a collection is written a file at a time and
    // setting one twice replaces what was there rather than adding to it. A compilation reports
    // across several files, so the second file's entries would take the first file's place.
    const byFile = new Map();

    for (const one of found) {
        const uri = vscode.Uri.file(one.file);
        const gathered = byFile.get(uri.toString()) || { uri, entries: [] };

        // Positions are one-based in a diagnostic and zero-based in the editor.
        const at = new vscode.Position(Math.max(0, one.line - 1), Math.max(0, one.column - 1));

        const entry = new vscode.Diagnostic(
            new vscode.Range(at, at), one.message, severityOf(one.severity));

        entry.source = 'profi-c';
        entry.code = one.code;

        gathered.entries.push(entry);
        byFile.set(uri.toString(), gathered);
    }

    for (const { uri, entries } of byFile.values()) {
        problemsPanel().set(uri, entries);
    }

    return !found.some(one => one.severity === 'error');
}

/** The editor's severity for one of the language's three. */
function severityOf(severity) {
    if (severity === 'error') {
        return vscode.DiagnosticSeverity.Error;
    }

    // An opinion is not a warning: it says a program does what its author meant and says it in a
    // way the language would rather it were not. Information is the nearest thing the editor has
    // that does not read as "something may be wrong".
    return severity === 'warning'
        ? vscode.DiagnosticSeverity.Warning
        : vscode.DiagnosticSeverity.Information;
}

/** The two builds offered to tasks.json and to Ctrl+Shift+B. */
function offerTheUsualBuilds() {
    const open = vscode.window.activeTextEditor;

    if (!open || !Debuggable.includes(open.document.languageId)) {
        return [];
    }

    const file = open.document.uri.fsPath;

    return [
        buildTask(file, ThisFile, target()),
        buildTask(
            withTheProjectInstead({ program: file, scope: TheProject }).program,
            TheProject,
            target()),
    ];
}

/**
 * Fills in a task somebody wrote in tasks.json themselves.
 *
 * VS Code hands back the definition and asks for something runnable. What it does not do is
 * call provideTasks first, so everything the task needs has to be worked out here too.
 */
function fillInTheRest(task) {
    const definition = task.definition;
    const open = vscode.window.activeTextEditor;

    const program = definition.program
        || (open && Debuggable.includes(open.document.languageId) && open.document.uri.fsPath);

    if (!program) {
        return undefined;
    }

    const scope = definition.scope === TheProject ? TheProject : ThisFile;

    const chosen = scope === TheProject
        ? withTheProjectInstead({ program, scope }).program
        : program;

    return buildTask(chosen, scope, definition.runtime || target(), definition.out);
}

/** One build, as a task the editor can run and match problems from. */
function buildTask(program, scope, runtime, out) {
    const path = require('path');

    const args = ['build', program];

    if (out) {
        args.push('--out', out);
    }

    if (runtime) {
        args.push('--runtime', runtime);
    }

    const task = new vscode.Task(
        { type: DebuggerType, scope, program, runtime, out },
        vscode.TaskScope.Workspace,
        scope === TheProject
            ? `build ${path.basename(program)} (project)`
            : `build ${path.basename(program)}`,
        'profi-c',
        new vscode.ShellExecution(compiler(), args),
        ['$profi-c-error', '$profi-c-warning', '$profi-c-opinion']);

    task.group = vscode.TaskGroup.Build;

    // Cleared each time, or a mistake fixed two builds ago is still in the Problems panel.
    task.presentationOptions = { reveal: vscode.TaskRevealKind.Silent, clear: true };

    return task;
}

/**
 * Lets the reader pick what to build for, from what can actually be built for.
 *
 * A list rather than a set of menu entries, because the platforms available are discovered:
 * they depend on which launchers the SDK installed and which any project has ever published
 * for. A menu is written in the manifest and cannot know that, and offering a platform nothing
 * can build for would undo the refusal that exists to prevent exactly it.
 */
async function chooseTarget() {
    const command = compiler();
    const asked = require('child_process').spawnSync(command, ['platforms'], {
        encoding: 'utf8',
        timeout: 10000,
        windowsHide: true,
    });

    if (asked.error || asked.status !== 0) {
        vscode.window.showErrorMessage(
            `Profi-C: '${command}' could not say which platforms it can build for. `
            + 'Check the compiler is installed and current.');

        return;
    }

    let published;

    try {
        published = JSON.parse(asked.stdout);
    } catch {
        vscode.window.showErrorMessage(
            `Profi-C: '${command}' answered something unreadable when asked for its platforms.`);

        return;
    }

    const chosen = target();

    const offered = [
        {
            label: 'This machine',
            description: published.default,
            detail: 'What a build targets when nothing says otherwise.',
            rid: '',
        },
        ...published.installed.map(rid => ({
            label: rid,
            description: rid === published.default ? 'this machine' : undefined,
            rid,
        })),
    ];

    for (const item of offered) {
        // The chosen one is marked rather than merely pre-selected, so the list says which is
        // in force even after the highlight moves.
        item.label = item.rid === chosen ? `$(check) ${item.label}` : `      ${item.label}`;
    }

    const picked = await vscode.window.showQuickPick(offered, {
        title: 'Profi-C: the platform to build for',
        placeHolder: 'Only platforms a launcher is installed for are offered',
    });

    if (!picked) {
        return;
    }

    await vscode.workspace.getConfiguration().update(
        TargetSetting, picked.rid, vscode.ConfigurationTarget.Workspace);
}

/** The compiler to run, which a configuration or a setting may name. */
function compiler() {
    return vscode.workspace.getConfiguration().get(CompilerPathSetting) || DefaultCompiler;
}

/**
 * Which inlay hints the reader has asked for, as the server reads them.
 *
 * Read here rather than defaulted here: the defaults live in `contributes.configuration`, which is
 * what VS Code shows in the settings editor, and a second copy of them in this file would be the
 * one that quietly won.
 */
function hintSettings() {
    const asked = vscode.workspace.getConfiguration('profi-c.inlayHints');

    return { inlayHints: { types: asked.get('types'), parameterNames: asked.get('parameterNames') } };
}

/**
 * Writes the Profi-C colors into the reader's own settings, for every folder rather than one.
 *
 * An extension cannot impose token colors — VS Code's model is that a theme owns them, and the
 * ones offered through `configurationDefaults` are accepted and then ignored. Writing them where
 * a reader would have written them by hand is the supported way, and doing it from here is what
 * keeps the palette with the extension instead of in a file copied per project.
 *
 * Any Profi-C rule already there is replaced rather than added to, so running this twice leaves
 * one copy, and rules for other languages are left exactly as they were.
 *
 * Two settings, because there are two kinds of color. The grammar's rules say what a run of
 * characters looks like; the semantic ones say what the compiler worked out it is, and they are
 * scoped to this language so nothing here touches how any other one is painted.
 */
async function useTheColors() {
    const settings = vscode.workspace.getConfiguration();
    const current = settings.get('editor.tokenColorCustomizations') || {};

    const others = (current.textMateRules || []).filter(rule => !isProfiC(rule));

    await settings.update(
        'editor.tokenColorCustomizations',
        { ...current, textMateRules: [...others, ...palette.rules] },
        vscode.ConfigurationTarget.Global);

    const semantic = vscode.workspace.getConfiguration()
        .get('editor.semanticTokenColorCustomizations') || {};

    await settings.update(
        'editor.semanticTokenColorCustomizations',
        {
            ...semantic,
            rules: { ...(semantic.rules || {}), ...scopedToProfiC(palette.semanticRules) },
        },
        vscode.ConfigurationTarget.Global);

    // Without this the rules above can do nothing, and say nothing about why. Semantic
    // highlighting ships set to 'configuredByTheme', so a theme that does not ask for it turns
    // the whole feature off — the server answers, the colors are in settings, and the file looks
    // exactly as it did. Turned on for this language alone, so no other one is affected.
    const forProfiC = vscode.workspace.getConfiguration().get('[profi-c]') || {};

    await settings.update(
        '[profi-c]',
        { ...forProfiC, 'editor.semanticHighlighting.enabled': true },
        vscode.ConfigurationTarget.Global);

    const total = palette.rules.length + Object.keys(palette.semanticRules).length;

    vscode.window.showInformationMessage(
        `Profi-C: ${total} color rules are now in your user settings.`);
}

/**
 * Narrows each semantic rule to this language, since the setting is shared by every one of them.
 *
 * A rule written as `variable` would color a local in C# and in TypeScript too. Written as
 * `variable:profi-c` it colors a local in a `.pc` file and nowhere else, which is the only
 * version of this that is polite to install into somebody's global settings.
 */
function scopedToProfiC(rules) {
    return Object.fromEntries(
        Object.entries(rules).map(([kind, color]) => [`${kind}:profi-c`, color]));
}

/** Whether a rule paints Profi-C, whichever of the two shapes its scope was written in. */
function isProfiC(rule) {
    const scopes = rule && rule.scope
        ? (Array.isArray(rule.scope) ? rule.scope : [rule.scope])
        : [];

    return scopes.some(scope => String(scope).endsWith('.profi-c'));
}

/**
 * Fills in a configuration for someone who pressed F5 without writing one.
 *
 * VS Code passes an empty configuration when there is no launch.json, and this is where that
 * becomes "debug the file in front of me". Worth doing rather than leaving: an introductory
 * language wants opening a program and running it to work, and requiring a launch.json first
 * asks a beginner to learn the editor before the language.
 */
function debugWhatIsOpenWhereNothingSaysOtherwise(folder, configuration) {
    if (!configuration.type && !configuration.request && !configuration.name) {
        const open = vscode.window.activeTextEditor;

        if (!open || !Debuggable.includes(open.document.languageId)) {
            // Undefined stops the session without reporting anything, which is right: nothing
            // is broken, there is simply no Profi-C program in front of the reader to run.
            return undefined;
        }

        configuration = {
            type: DebuggerType,
            request: 'launch',
            name: 'Run this file',

            // The path itself rather than '${file}'. VS Code would substitute that a moment
            // later and the two are the same here, but a configuration nobody wrote is also a
            // configuration nobody can read, and an unsubstituted variable reaching the
            // compiler reads as a file that does not exist.
            program: open.document.uri.fsPath,

            internalConsoleOptions: ShowTheConsole,
        };
    }

    return configuration.scope === TheProject
        ? withTheProjectInstead(configuration)
        : configuration;
}

/**
 * Points a configuration at the project that claims the file.
 *
 * "The project this file is in" is a claim about the project's contents, not about where it sits
 * on disk — a `.pcp` above a file lists what it builds, and a file it does not list is no more
 * part of it than a file in another folder. Running the nearest one regardless would compile a
 * program the reader is not looking at, print its output, and look like the button working.
 *
 * Where nothing claims the file, the file itself is run. That is the ordinary case for a program
 * of one file rather than a failure — but it is said out loud, because "run the project" quietly
 * doing something else is exactly the kind of helpfulness that wastes an afternoon.
 */
function withTheProjectInstead(configuration) {
    // A project named outright is already the answer; there is nothing to search for.
    if (typeof configuration.program === 'string' && configuration.program.endsWith('.pcp')) {
        return configuration;
    }

    const found = projectClaiming(configuration.program);

    if (found.project) {
        return { ...configuration, program: found.project };
    }

    // Nothing is known about projects when the compiler could not be asked, so there is nothing
    // true to say. Silent on purpose: the run or build about to happen reports a compiler it
    // cannot start, and two notices about one missing program is one too many.
    if (found.asked) {
        vscode.window.showInformationMessage(
            found.searched === 0
                ? 'Profi-C: no project found — running this file.'
                : 'Profi-C: no project lists this file — running the file itself.');
    }

    return configuration;
}

// ---- Working on a project --------------------------------------------------------------------

/**
 * Starts a project, by asking the compiler for one.
 *
 * `pc new --project` already writes a folder holding a `.pcp` and the program it builds, and it
 * already refuses to write over anything. Writing a second copy of that here would be a second
 * answer to what a new project looks like, and the two would drift the first time the format
 * gained a word.
 */
async function newProject() {
    const folder = vscode.workspace.workspaceFolders && vscode.workspace.workspaceFolders[0];

    if (!folder) {
        vscode.window.showInformationMessage(
            'Profi-C: open a folder first — a new project is written into one.');

        return;
    }

    const name = await vscode.window.showInputBox({
        prompt: 'Name for the project',
        placeHolder: 'storefront',
        validateInput: written => /^[A-Za-z0-9_]+$/.test(written)
            ? undefined
            : 'Letters, digits and underscores.',
    });

    if (!name) {
        return;
    }

    const made = require('child_process').spawnSync(
        compiler(),
        ['new', name, '--project'],
        { cwd: folder.uri.fsPath, encoding: 'utf8', timeout: 15000, windowsHide: true });

    if (made.error || made.status !== 0) {
        vscode.window.showErrorMessage(
            `Profi-C: ${(made.stderr || '').trim() || whyItCannotDebug(compiler())}`);

        return;
    }

    const project = vscode.Uri.joinPath(folder.uri, name, `${name}.pcp`);

    await vscode.window.showTextDocument(await vscode.workspace.openTextDocument(project));
}

/**
 * Adds the file to the project that would build it, or takes it out again.
 *
 * **Which project is the compiler's answer where there is one**, so adding a file already listed
 * says so rather than listing it twice. Where nothing claims it — which is the ordinary case for
 * a file somebody has just made, and the reason this command exists — the projects in the folder
 * are offered instead, and only the ones that could list it: a `.pcp` names what it builds by a
 * path relative to itself, so one sitting elsewhere cannot.
 */
async function listFile(file, adding) {
    const path = require('path');
    const document = fileInFront(file);

    if (!document || !document.fsPath.endsWith('.pc')) {
        vscode.window.showInformationMessage('Profi-C: open a .pc file to add it to a project.');
        return;
    }

    const project = adding
        ? await projectToListIn(document.fsPath)
        : projectClaiming(document.fsPath).project;

    if (!project) {
        vscode.window.showInformationMessage(
            adding
                ? 'Profi-C: no project here could list this file.'
                : 'Profi-C: no project lists this file.');

        return;
    }

    const opened = await vscode.workspace.openTextDocument(vscode.Uri.file(project));
    const text = opened.getText();

    const written = adding
        ? projects.withSource(text, project, document.fsPath)
        : projects.withoutSource(text, project, document.fsPath);

    if (written === null) {
        // Listed already, or listed by a folder rather than by name. Either way the file is
        // built, and rewriting the folder line to take one file out of it would change what the
        // project builds far beyond what was asked.
        vscode.window.showInformationMessage(
            adding
                ? `Profi-C: ${path.basename(project)} already lists this file.`
                : `Profi-C: ${path.basename(project)} does not name this file — a folder it lists`
                  + ' brings it in.');

        return;
    }

    await write(opened, written);

    vscode.window.showInformationMessage(
        `Profi-C: ${adding ? 'added to' : 'removed from'} ${path.basename(project)}.`);
}

/**
 * Makes the file in front of the reader the one its project starts at.
 *
 * The type rather than the file, because that is what `entry` names: two files may each declare a
 * `Program` and namespaces are what tell them apart. Asked of `pc outline`, which reports what a
 * file declares — so the name written is the one the compiler will look for.
 */
async function setEntryPoint(file) {
    const path = require('path');
    const document = fileInFront(file);

    if (!document || !document.fsPath.endsWith('.pc')) {
        vscode.window.showInformationMessage(
            'Profi-C: open the .pc file whose program should start the project.');

        return;
    }

    const project = projectClaiming(document.fsPath).project;

    if (!project) {
        vscode.window.showInformationMessage(
            'Profi-C: no project lists this file, so nothing starts at it.');

        return;
    }

    const program = programIn(document.fsPath);

    if (!program) {
        vscode.window.showInformationMessage(
            'Profi-C: this file declares no Program, so a project cannot start at it.');

        return;
    }

    const opened = await vscode.workspace.openTextDocument(vscode.Uri.file(project));
    const written = projects.withEntry(opened.getText(), program);

    if (written === null) {
        vscode.window.showErrorMessage(
            `Profi-C: ${path.basename(project)} has no 'end project' to write before.`);

        return;
    }

    await write(opened, written);

    vscode.window.showInformationMessage(
        `Profi-C: ${path.basename(project)} now starts at ${program}.`);
}

/**
 * Says where a project's build should go, by picking the folder.
 *
 * Picked rather than typed, because the line is a path relative to the project file and working
 * one of those out by hand is where a reader gets it wrong — the folder dialog knows where
 * everything is and this writes down the way between them.
 *
 * Works from either kind of file. A `.pcp` is the thing being edited, and a `.pc` is what the
 * reader is more often looking at when the thought occurs, so the project claiming it is found
 * the same way every other project command finds it.
 */
async function setOutputFolder(file) {
    const path = require('path');
    const document = fileInFront(file);

    if (!document) {
        vscode.window.showInformationMessage(
            'Profi-C: open a project, or a file one lists, to say where its build goes.');

        return;
    }

    const project = document.fsPath.endsWith('.pcp')
        ? document.fsPath
        : projectClaiming(document.fsPath).project;

    if (!project) {
        vscode.window.showInformationMessage(
            'Profi-C: no project lists this file. A loose file always builds into the bin '
            + 'beside it.');

        return;
    }

    const picked = await vscode.window.showOpenDialog({
        canSelectFiles: false,
        canSelectFolders: true,
        canSelectMany: false,
        defaultUri: vscode.Uri.file(path.dirname(project)),
        openLabel: 'Build into this folder',
        title: `Where ${path.basename(project)} builds`,
    });

    if (!picked || picked.length === 0) {
        return;
    }

    const folder = projects.relativeTo(project, picked[0].fsPath);

    if (folder.length === 0) {
        vscode.window.showInformationMessage(
            'Profi-C: that is the folder the project is in. Pick one to build into, so what a '
            + 'tool made stays apart from what you wrote.');

        return;
    }

    const opened = await vscode.workspace.openTextDocument(vscode.Uri.file(project));
    const written = projects.withOutput(opened.getText(), folder);

    if (written === null) {
        vscode.window.showErrorMessage(
            `Profi-C: ${path.basename(project)} has no 'end project' to write before.`);

        return;
    }

    await write(opened, written);

    vscode.window.showInformationMessage(
        `Profi-C: ${path.basename(project)} now builds into ${folder}.`);
}

/** The document a command was invoked on, whether from a menu or from the editor. */
function fileInFront(file) {
    return file instanceof vscode.Uri
        ? file
        : vscode.window.activeTextEditor && vscode.window.activeTextEditor.document.uri;
}

/** Replaces a document's text and saves it, so the edit is undoable like any other. */
async function write(document, text) {
    const edit = new vscode.WorkspaceEdit();

    edit.replace(
        document.uri,
        new vscode.Range(
            document.positionAt(0), document.positionAt(document.getText().length)),
        text);

    await vscode.workspace.applyEdit(edit);
    await document.save();
}

/**
 * The project to list a file in: the one that already claims it, or a choice among those that
 * could. Only projects the file sits under are offered, since that is as far as a path may reach.
 */
async function projectToListIn(filePath) {
    const claiming = projectClaiming(filePath).project;

    if (claiming) {
        return claiming;
    }

    const found = (await vscode.workspace.findFiles('**/*.pcp', '**/node_modules/**'))
        .map(uri => uri.fsPath)
        .filter(project => projects.within(project, filePath));

    if (found.length <= 1) {
        return found[0];
    }

    // The nearest first, which is the one a reader means when there are several.
    found.sort((left, right) =>
        projects.relativeTo(left, filePath).length - projects.relativeTo(right, filePath).length);

    return vscode.window.showQuickPick(found, { placeHolder: 'Which project should list it?' });
}

/**
 * The qualified name of the `Program` a file declares, or undefined for a file declaring none.
 *
 * Read from `pc outline`, which is the compiler's account of what a file declares. Looking for
 * the word in the text would find it in a comment and in a string, and would miss the namespace
 * that tells one `Program` from another.
 */
function programIn(filePath) {
    const asked = require('child_process').spawnSync(compiler(), ['outline', filePath], {
        encoding: 'utf8',
        timeout: 15000,
        windowsHide: true,
    });

    if (asked.error || asked.status !== 0) {
        return undefined;
    }

    try {
        return found(JSON.parse(asked.stdout) || [], []);
    } catch {
        return undefined;
    }
}

/**
 * The qualified name of a `Program` somewhere in an outline, or undefined where there is none.
 *
 * Walked rather than read off the top, because a namespace is an entry with the declarations
 * inside it — so a file that opens with one has its models a level down, and the namespaces on
 * the way are exactly what a qualified name is made of.
 */
function found(entries, above) {
    for (const entry of entries) {
        if (entry.kind === 'namespace') {
            const inside = found(entry.children || [], [...above, entry.name]);

            if (inside) {
                return inside;
            }

            continue;
        }

        if (entry.name === 'Program') {
            return [...above, 'Program'].join('.');
        }
    }

    return undefined;
}

/**
 * The nearest project that lists the file, and how many were read on the way.
 *
 * Asked of the compiler, because the answer depends on how a `.pcp` is read and reading one here
 * would be a second reader of that format. The two would agree until the day they did not, and
 * that disagreement is silent in the worst direction: a project claims a file it does not build,
 * and pressing Run compiles a program nobody was looking at.
 *
 * The count separates "there is no project here" from "there are projects and none of them wants
 * this file", which are different things to be told. `asked` separates both from not knowing.
 */
function projectClaiming(program) {
    if (typeof program !== 'string' || program.length === 0) {
        return { project: undefined, searched: 0, asked: false };
    }

    const asked = require('child_process').spawnSync(compiler(), ['project', program], {
        encoding: 'utf8',
        timeout: 15000,
        windowsHide: true,
    });

    if (asked.error || asked.status !== 0) {
        return { project: undefined, searched: 0, asked: false };
    }

    try {
        const answer = JSON.parse(asked.stdout);

        return {
            project: answer.project || undefined,
            searched: answer.searched,
            asked: true,
        };
    } catch {
        return { project: undefined, searched: 0, asked: false };
    }
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

    const wrong = whyItCannotDebug(command);

    if (wrong) {
        vscode.window.showErrorMessage(`Profi-C: ${wrong}`);
        return undefined;
    }

    return new vscode.DebugAdapterExecutable(command, ['debug'], {
        // Started where the workspace is, so that a program written as a relative path in
        // launch.json means what it looks like it means. Without this the adapter inherits
        // whatever directory the editor happened to be launched from.
        cwd: session.workspaceFolder ? session.workspaceFolder.uri.fsPath : undefined,
    });
}

/**
 * Why the compiler cannot be debugged with, or undefined where it can.
 *
 * Worth a subprocess before every session because of how badly the two failures present. A
 * compiler that is not there is reported by the editor, more or less. A compiler that is there
 * but too old is not reported at all: it prints "unknown command 'debug'" to its standard
 * output, exits zero, and the editor sees a process that started correctly and then finished.
 * Pressing Run does nothing, says nothing, and logs nothing — which is a long evening.
 *
 * Asked with '--help' rather than by version, so that what is checked is the thing needed
 * rather than a number that stands for it.
 */
function whyItCannotDebug(command) {
    const asked = require('child_process').spawnSync(command, ['--help'], {
        encoding: 'utf8',
        timeout: 10000,
        windowsHide: true,
    });

    if (asked.error) {
        return `'${command}' could not be started (${asked.error.code || asked.error.message}). `
            + 'Install the compiler, or name it in the profi-c.compilerPath setting.';
    }

    if (!`${asked.stdout || ''}${asked.stderr || ''}`.includes('debug')) {
        return `the compiler at '${command}' has no 'debug' command, so it is too old to debug `
            + 'with. Update it, or point profi-c.compilerPath at a newer build.';
    }

    return undefined;
}

module.exports = { activate, deactivate };
