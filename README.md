# Profi-C.Editors

Editor support for [Profi-C](https://github.com/mnwachukwu/Profi-C): the VS Code extension, and
the tooling that will grow around it.

The language itself — the compiler, the interpreter, the specification, the samples — lives in
the Profi-C repository. Nothing here compiles Profi-C. This repository is about what an editor
does with a `.pc` file before and while the compiler is involved.

## What is here

| | |
|---|---|
| [vscode/](vscode) | The VS Code extension: TextMate grammars for `.pc` and `.pcp`, and the language configuration |
| [tests/](tests) | What the grammars actually do, run through the engine VS Code runs |

## What is planned

Roughly in the order it is wanted:

1. **A debug adapter** — breakpoints, step over, step into, a call stack, a variables pane.
   Speaks the Debug Adapter Protocol and drives the Profi-C interpreter, which already walks one
   statement at a time and carries a source span on every node.
2. **Project management** — commands for creating a `.pcp`, adding and removing files, setting
   the entry point, and running or cleaning without leaving the editor.
3. **A language server** — live diagnostics, completion, hover types, go to definition.
4. **A formatter.**

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

## Why the extension is not published yet

The grammar is Tier 1 of the plan in Profi-C's design record: syntax highlighting, which
delivers most of the perceived value for a few hours of work. Diagnostics, completion, and
go-to-definition are the language server, which is a much larger piece and is not written.
Publishing to the Marketplace before the debugger exists would set an expectation the extension
does not yet meet.
