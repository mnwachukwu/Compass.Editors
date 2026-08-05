// Applies the extension's own .pcp edits, so a test asserts on what the commands will write.
//
// The editing functions take text and give text back, so nothing here stubs the editor and
// nothing writes a file. Reads a JSON array of requests on standard input and writes a JSON array
// of results, the same shape as the other tools beside it.
//
// Each request is { "op": "add" | "remove" | "entry" | "output", "text", "project", "file",
// "type" }, and each result is the new text or null where the edit changes nothing. An "output"
// takes the folder in "type", since both name one thing rather than a path to resolve.

const path = require('path');
const projects = require(path.join(__dirname, '..', 'projects.js'));

let input = '';

process.stdin.on('data', chunk => { input += chunk; });

process.stdin.on('end', () => {
    const answers = JSON.parse(input).map(asked => {
        switch (asked.op) {
            case 'add':
                return projects.withSource(asked.text, asked.project, asked.file);

            case 'remove':
                return projects.withoutSource(asked.text, asked.project, asked.file);

            case 'entry':
                return projects.withEntry(asked.text, asked.type);

            case 'output':
                return projects.withOutput(asked.text, asked.type);

            case 'within':
                return projects.within(asked.project, asked.file);

            default:
                return null;
        }
    });

    process.stdout.write(JSON.stringify(answers));
});
