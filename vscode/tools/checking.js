// Answers what the Run button would put in the Problems panel, using the extension's own logic.
//
// The extension is loaded rather than reimplemented, with the editor stubbed out — so what this
// reports is what a reader will actually see, and not a second opinion that agrees with it today.
//
// The diagnostics themselves are the compiler's: the extension runs `cm check`, so the compiler
// to ask is the first argument, letting a test hold a build of it rather than whatever is on
// PATH.
//
// Reads a JSON array of file paths on standard input and writes a JSON array of answers, the same
// shape as tools/project.js and tools/scopes.js, so a test can drive it without knowing any of
// this.

const fs = require('fs');
const path = require('path');

const compilerPath = process.argv[2] || 'cm';
const extension = path.join(__dirname, '..', 'extension.js');

// Every entry the extension handed to the collection, in the order it handed them over, with the
// editor's own types replaced by what a test can compare. Only what a reader would see: the file,
// where the squiggle sits, and what it says.
const published = [];

const source = fs.readFileSync(extension, 'utf8')
    .replace(
        "const vscode = require('vscode');",
        `const vscode = {
            Uri: { file: given => ({ fsPath: given, toString: () => given }) },
            Position: class { constructor(line, character) { this.line = line; this.character = character; } },
            Range: class { constructor(start, end) { this.start = start; this.end = end; } },
            Diagnostic: class {
                constructor(range, message, severity) {
                    this.range = range;
                    this.message = message;
                    this.severity = severity;
                }
            },
            DiagnosticSeverity: { Error: 'error', Warning: 'warning', Information: 'information' },
            languages: {
                createDiagnosticCollection: () => ({
                    clear: () => { published.length = 0; },
                    set: (uri, entries) => {
                        for (const entry of entries) {
                            published.push({
                                file: path.basename(uri.fsPath),
                                line: entry.range.start.line,
                                column: entry.range.start.character,
                                severity: entry.severity,
                                code: entry.code,
                                message: entry.message,
                            });
                        }
                    },
                    dispose: () => {},
                }),
            },
            window: { showInformationMessage: () => {} },
            workspace: {
                getConfiguration: () => ({
                    // Only the compiler is answered. Nothing else here reaches a setting, and a
                    // stub inventing values would hide one that started to.
                    get: key => (String(key).endsWith('compilerPath') ? compilerPath : ''),
                }),
            },
        };`)
    // Beside the extension rather than beside this, since `require` here resolves from tools/.
    .replace(
        "const palette = require('./palette');",
        `const palette = require(${JSON.stringify(path.join(__dirname, '..', 'palette.js'))});`)
    .replace(
        "const projects = require('./projects');",
        `const projects = require(${JSON.stringify(path.join(__dirname, '..', 'projects.js'))});`);

// Nothing is activated. The extension makes its collection the first time it writes to one, so
// the two functions below are reachable without registering commands, providers and a debugger
// against an editor that is not here.
const api = new Function(
    'require', 'process', 'module', 'published', 'compilerPath', 'path',
    `${source}; return { checked, showProblems };`)(
    require, process, { exports: {} }, published, compilerPath, path);

let input = '';

process.stdin.on('data', chunk => { input += chunk; });

process.stdin.on('end', () => {
    const answers = JSON.parse(input).map(file => {
        published.length = 0;

        const found = api.checked(file, 'file');
        const mayRun = api.showProblems(found);

        return {
            // Whether the run would go ahead. The point of the whole arrangement: a program with
            // errors is refused, and one with only warnings is not.
            mayRun,

            // Whether the compiler answered at all, so a test can tell "no problems" from "no
            // compiler".
            asked: found !== undefined,

            problems: published,
        };
    });

    process.stdout.write(JSON.stringify(answers));
});
