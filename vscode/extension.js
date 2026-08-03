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

        vscode.commands.registerCommand('profi-c.runFile', file => start(file, ThisFile)),
        vscode.commands.registerCommand('profi-c.runProject', file => start(file, TheProject)),
        vscode.commands.registerCommand('profi-c.buildFile', file => build(file, ThisFile)),
        vscode.commands.registerCommand('profi-c.buildProject', file => build(file, TheProject)),
        vscode.commands.registerCommand('profi-c.chooseTarget', chooseTarget),
        vscode.commands.registerCommand('profi-c.useTheColors', useTheColors),

        // Offered to tasks.json as well, so a project can pin a build the way it likes and
        // Ctrl+Shift+B finds one without anybody writing the command line out.
        vscode.tasks.registerTaskProvider(DebuggerType, {
            provideTasks: offerTheUsualBuilds,
            resolveTask: fillInTheRest,
        }),

        // Breadcrumbs, the Outline view and Ctrl+Shift+O, all from the one provider.
        vscode.languages.registerDocumentSymbolProvider(
            { language: 'profi-c' },
            { provideDocumentSymbols: outline }),

        showTheTarget(context));
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
        },
        {
            type: DebuggerType,
            request: 'launch',
            name: 'Run project associated with this file',
            program: '${file}',
            scope: TheProject,
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

    // The same question Run asks, answered by the same code — so "no project lists this file"
    // cannot mean one thing when running and another when building.
    const program = scope === TheProject
        ? withTheProjectInstead({ program: document.fsPath, scope }).program
        : document.fsPath;

    await vscode.tasks.executeTask(buildTask(program, scope, target()));
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
 * Writes the Profi-C colors into the reader's own settings, for every folder rather than one.
 *
 * An extension cannot impose token colors — VS Code's model is that a theme owns them, and the
 * ones offered through `configurationDefaults` are accepted and then ignored. Writing them where
 * a reader would have written them by hand is the supported way, and doing it from here is what
 * keeps the palette with the extension instead of in a file copied per project.
 *
 * Any Profi-C rule already there is replaced rather than added to, so running this twice leaves
 * one copy, and rules for other languages are left exactly as they were.
 */
async function useTheColors() {
    const settings = vscode.workspace.getConfiguration();
    const current = settings.get('editor.tokenColorCustomizations') || {};

    const others = (current.textMateRules || []).filter(rule => !isProfiC(rule));

    await settings.update(
        'editor.tokenColorCustomizations',
        { ...current, textMateRules: [...others, ...palette.rules] },
        vscode.ConfigurationTarget.Global);

    vscode.window.showInformationMessage(
        `Profi-C: ${palette.rules.length} color rules are now in your user settings.`);
}

/** Whether a rule paints Profi-C, whichever of the two shapes its scope was written in. */
function isProfiC(rule) {
    const scopes = rule && rule.scope
        ? (Array.isArray(rule.scope) ? rule.scope : [rule.scope])
        : [];

    return scopes.some(scope => String(scope).endsWith('.profi-c'));
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
