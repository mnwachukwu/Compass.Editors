// Answers which project claims a file, using the extension's own logic.
//
// The extension is loaded rather than reimplemented, with the editor stubbed out — so what this
// reports is what the Run button will do, and not a second opinion that agrees with it today.
//
// Reads a JSON array of file paths on standard input and writes a JSON array of answers, the
// same shape as tools/scopes.js, so a test can drive it without knowing any of this.

const fs = require('fs');
const path = require('path');

const extension = path.join(__dirname, '..', 'extension.js');

const source = fs.readFileSync(extension, 'utf8')
    .replace(
        "const vscode = require('vscode');",
        'const vscode = { window: { showInformationMessage: said => messages.push(said) } };')
    .replace(
        "const palette = require('./palette');",
        `const palette = require(${JSON.stringify(path.join(__dirname, '..', 'palette.js'))});`);

const messages = [];

const api = new Function(
    'require', 'process', 'module', 'messages',
    `${source}; return { projectClaiming, withTheProjectInstead };`)(
    require, process, { exports: {} }, messages);

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
            runs: path.basename(chosen.program),
            said: messages[0] || null,
        };
    });

    process.stdout.write(JSON.stringify(answers));
});
