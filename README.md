# Profi-C.Editors

[![CI (Windows)](https://github.com/mnwachukwu/Profi-C.Editors/actions/workflows/ci-windows.yml/badge.svg)](https://github.com/mnwachukwu/Profi-C.Editors/actions/workflows/ci-windows.yml)
[![CI (Linux)](https://github.com/mnwachukwu/Profi-C.Editors/actions/workflows/ci-linux.yml/badge.svg)](https://github.com/mnwachukwu/Profi-C.Editors/actions/workflows/ci-linux.yml)

Editor support for [Profi-C](https://github.com/mnwachukwu/Profi-C): the VS Code extension, and
the tooling that will grow around it.

The language itself — the compiler, the interpreter, the specification, the samples — lives in
the Profi-C repository. Nothing here compiles Profi-C. This repository is about what an editor
does with a `.pc` file before and while the compiler is involved.

## What is here

| | |
|---|---|
| [vscode/](vscode) | The VS Code extension: TextMate grammars for `.pc` and `.pcp`, the language configuration, the debugger, Run and Build buttons, the Outline, and the color palette |
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
[`vscode/extension.js`](vscode/extension.js) starts the adapter, and offers a configuration to
anyone who has not written a launch.json.

The split is worth keeping. Two implementations of one set of decisions is two answers to every
question about them, and the second one is always the one that is out of date.

## What is asked, and what is decided here

The extension has grown past the debugger — Run and Build buttons, a target platform, the
Outline, the palette — and the line that keeps it honest is this: **anything with a Profi-C
answer is asked of `pc` rather than worked out here.**

| Question | Asked with |
|---|---|
| What does this file declare? | `pc outline` |
| Which `.pcp` builds this file? | `pc project` |
| Which platforms can be built for? | `pc platforms` |
| What words does the language reserve? | `pc vocabulary`, read by the tests |
| Where should this stop, and what is in scope? | `pc debug`, which is the whole adapter |

Each could have been written in JavaScript, and each would then be a second definition of Profi-C
that agreed with the first until somebody added a construct. That failure is silent: a member
stops appearing in the Outline, a keyword stops being colored, and nothing reports it.

Membership was written here once, and is the reason the table has a row for it. Reading a `.pcp`
by scanning lines for the word `source` looks right and is not: it counts a `source` inside a
`##` comment, and one written after `end project`, both of which the compiler ignores. The button
would then compile and run a program the reader was not looking at — and look exactly like the
button working, which is the failure this whole arrangement exists to prevent.

**One thing here writes a format the compiler owns, and it is worth naming rather than glossing
over.** The project commands put a `source` line into a `.pcp`, take one out, and replace an
`entry` — so [projects.js](vscode/projects.js) is the only file in this repository that composes
Profi-C's own syntax.

Three things keep it from being the second reader everything above refuses. **Nothing is read to
decide anything** — which project claims a file is still `pc project`, and what a file declares is
still `pc outline`. **Nothing is rewritten wholesale**: a source goes in before `end project`, a
source comes out by the line naming it, and a line the editing does not recognize is left exactly
as it was, so a format that gains a word gains it here for free. And **the language server
validates the result**, so an edit that lands wrong is in the Problems panel before the reader has
looked away.

Anything needing more understanding of the format than that belongs in `pc` instead.

## What is planned

Nothing large. The debugger, the language server and project management are all in; what is left
is the ordinary business of using it and fixing what that turns up.

**The language server itself is done** — live diagnostics as you type, hover types, go to
definition, completion both after a dot and for a bare name, signature help, quick fixes, rename,
coloring every name for what the compiler worked out it is, marking every use of the name under
the caret, and formatting. What it does and how much of it is `pc` rather than JavaScript is in
[the extension's README](vscode/README.md).

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
repository alone is an ordinary state to be in. Everything else still runs.

CI checks out both, and then **fails if anything skipped at all**. That second half is what keeps
the skip a local convenience rather than a hole: a run that quietly omitted the tests holding the
grammar to the language would otherwise be indistinguishable from a run that passed them.

## Running the tests

```bash
dotnet test
```

The tokenization tests additionally need the TextMate engine, which is fetched rather than
committed. Install it from the extension's own folder, since npm's `--prefix` means the global
install location and not every version honors it for a local install:

```bash
cd vscode
npm install
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

## License

MIT. See [LICENSE](LICENSE). The extension carries [its own copy](vscode/LICENSE), since a
packaged `.vsix` holds only what is inside `vscode/` and the manifest declares the license there.
