# Profi-C.Editors

Editor support for [Profi-C](https://github.com/mnwachukwu/Profi-C): the VS Code extension, and
the tooling that will grow around it.

The language itself — the compiler, the interpreter, the specification, the samples — lives in
the Profi-C repository. Nothing here compiles Profi-C. This repository is about what an editor
does with a `.pc` file before and while the compiler is involved.

## What is here

| | |
|---|---|
| [vscode/](vscode) | The VS Code extension: TextMate grammars for `.pc` and `.pcp`, the language configuration, and the debugger |
| [tests/](tests) | What the grammars actually do, run through the engine VS Code runs, and what the manifest and the extension agree about |

## Debugging, and where its two halves live

Breakpoints, stepping, a call stack and a variables pane all work. **Almost none of it is in
this repository**, and that is the design rather than an accident.

Everything about *how* to debug — where to stop, what counts as one step, which names are worth
showing — is in Profi-C, in an adapter that speaks the Debug Adapter Protocol over its standard
input and output. It is reached with `pc debug` and it works with any client that speaks the
protocol, VS Code being one of them.

What is here is the little that must be. VS Code can be told declaratively that a debugger
exists, but not to run whatever `pc` the reader has installed — the path a manifest names is
resolved inside the extension folder, and the compiler is not in there. So
[`vscode/extension.js`](vscode/extension.js) answers two questions and no others: which command
to start, and what to debug when nobody has written a launch.json.

The split is worth keeping. Two implementations of one set of decisions is two answers to every
question about them, and the second one is always the one that is out of date.

## What is planned

Roughly in the order it is wanted:

1. **Project management** — commands for creating a `.pcp`, adding and removing files, setting
   the entry point, and running or cleaning without leaving the editor.
2. **A language server** — live diagnostics, completion, hover types, go to definition.
3. **A formatter.**

## The vocabulary, and why the tests need Profi-C beside them

The grammars claim to color every word the language reserves. That claim is worth checking, and
checking it means knowing what the language reserves — which is a fact about the *other*
repository.

Profi-C publishes it. `pc vocabulary` prints every reserved word and every built-in type name as
JSON, and the result is committed there as `docs/vocabulary.json`. The tests here read that file
**from a sibling checkout**:

```
D:\Repos\
    Profi-C\           <- the language
    Profi-C.Editors\   <- this
```

A copy was deliberately not taken. A copy drifts, and a drifting copy is exactly the failure the
published file exists to prevent — the grammar would agree with a list that was itself out of
date, and nothing anywhere would fail.

**Where Profi-C is not beside this, those tests skip rather than fail**, since a checkout of one
repository alone is an ordinary state to be in. Everything else still runs. CI checks out both,
so the skip is a local convenience rather than a hole.

## Running the tests

```bash
dotnet test
```

The tokenization tests additionally need the TextMate engine, which is fetched rather than
committed:

```bash
npm install --prefix vscode
```

Without it those tests skip and say so.

Node is also what checks that `extension.js` parses, and that test skips the same way. It is
worth having because nothing else would notice: VS Code says nothing useful about an entry point
it cannot load — the extension simply never activates, and a debugger contributed in the
manifest is absent from the menu for a reason recorded only in an extension host log.

## Why the extension is not published yet

The grammar is Tier 1 of the plan in Profi-C's design record: syntax highlighting, which
delivers most of the perceived value for a few hours of work. The debugger is Tier 2 and is now
here. Diagnostics, completion, and go-to-definition are the language server — Tier 3, a much
larger piece, and not written.

What holds up publishing is the compiler rather than the extension: debugging needs `pc` on the
reader's PATH, and Profi-C is not on NuGet yet. An extension that installs cleanly and then
cannot start anything is worse than one nobody has.
