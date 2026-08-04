// Answers which project claims a file, using the extension's own logic.
//
// The extension is loaded rather than reimplemented, with the editor stubbed out — so what this
// reports is what the Run button will do, and not a second opinion that agrees with it today.
//
// The answer itself is the compiler's: the extension runs `pc project`, because reading a `.pcp`
// here would be exactly the second reader that arrangement exists to avoid. So the compiler to
// ask is the first argument, letting a test hold a build of it rather than whatever is on PATH.
//
// Reads a JSON array of file paths on standard input and writes a JSON array of answers, the
// same shape as tools/scopes.js, so a test can drive it without knowing any of this.

const fs = require('fs');
const path = require('path');

const compilerPath = process.argv[2] || 'pc';
const extension = path.join(__dirname, '..', 'extension.js');

const source = fs.readFileSync(extension, 'utf8')
    .replace(
        "const vscode = require('vscode');",
        `const vscode = {
            window: { showInformationMessage: said => messages.push(said) },
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

const messages = [];

const api = new Function(
    'require', 'process', 'module', 'messages', 'compilerPath',
    `${source}; return { projectClaiming, withTheProjectInstead };`)(
    require, process, { exports: {} }, messages, compilerPath);

let input = '';

process.stdin.on('data', chunk => { input += chunk; });

process.stdin.on('end', () => {
    const answers = JSON.parse(input).map(file => {
        messages.length = 0;

        const found = api.projectClaiming(file);
        const chosen = api.withTheProjectInstead({ program: file, scope: 'project' });

        return {
            project: found.project ? path.basename(found.project) : null,
            searched: found.searched,

            // Whether the compiler answered at all. A test asserting on a project needs to know
            // that "no project" was a decision rather than a compiler it could not run.
            asked: found.asked,

            runs: path.basename(chosen.program),
            said: messages[0] || null,
        };
    });

    process.stdout.write(JSON.stringify(answers));
});
