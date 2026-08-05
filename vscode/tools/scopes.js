// Tokenizes lines of Profi-C with the grammar this extension ships, and prints
// the scopes each token carries.
//
// This exists because nothing else can answer the question. A test can read the
// grammar's JSON and assert what it says, and that is what the C# suite did for
// a long time — but "the file names this scope" and "a reader sees this scope"
// are different claims, and only the second one matters. Several confident
// statements about the editor turned out to be wrong in exactly that gap.
//
// The engine here is the one VS Code runs: vscode-textmate over the Oniguruma
// regex library. A rule that behaves differently here behaves differently there.
//
// Reads lines as JSON on standard input, writes JSON on standard output. The
// scope to tokenize under may be named as an argument, which is how the project
// file's own grammar is reached:
//
//     echo '["# @summary: A thing."]' | node tools/scopes.js
//     [[{"text":"# ","scopes":["source.profi-c","comment.line..."]}, ...]]
//
//     echo '["source Program.pc"]' | node tools/scopes.js source.profi-c-project

const fs = require("node:fs");
const path = require("node:path");
const oniguruma = require("vscode-oniguruma");
const textmate = require("vscode-textmate");

const here = path.dirname(__dirname);

// Every grammar the extension ships, by the scope it answers to. Both are here
// rather than only the language's, because a project file is a thing a reader
// looks at and its grammar had nothing checking what it colored.
const grammars = {
    "source.profi-c": "profi-c.tmLanguage.json",
    "source.profi-c-project": "profi-c-project.tmLanguage.json",
};

async function main() {
    const wasm = fs.readFileSync(
        require.resolve("vscode-oniguruma/release/onig.wasm"));

    await oniguruma.loadWASM(wasm.buffer);

    const registry = new textmate.Registry({
        onigLib: Promise.resolve({
            createOnigScanner: (sources) => new oniguruma.OnigScanner(sources),
            createOnigString: (text) => new oniguruma.OnigString(text),
        }),

        // A scope name that is neither returns null, so the caller sees an
        // empty result rather than a crash.
        loadGrammar: async (scope) =>
            scope in grammars
                ? textmate.parseRawGrammar(
                    fs.readFileSync(
                        path.join(here, "syntaxes", grammars[scope]), "utf8"),
                    grammars[scope])
                : null,
    });

    const grammar = await registry.loadGrammar(
        process.argv[2] ?? "source.profi-c");
    const lines = JSON.parse(fs.readFileSync(0, "utf8"));

    // State is carried from one line to the next, which is what makes a block
    // comment spanning lines tokenize the way it does in an editor.
    let state = textmate.INITIAL;
    const scanned = [];

    for (const line of lines) {
        const result = grammar.tokenizeLine(line, state);
        state = result.ruleStack;

        scanned.push(result.tokens.map(token => ({
            text: line.substring(token.startIndex, token.endIndex),
            scopes: token.scopes,
        })));
    }

    process.stdout.write(JSON.stringify(scanned));
}

main().catch(problem => {
    process.stderr.write(String(problem));
    process.exit(1);
});
