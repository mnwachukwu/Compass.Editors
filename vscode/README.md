# Compass for VS Code

Everything you need to write [Compass](https://compass.pluperfect.dev) in VS Code: it colors it,
tells you what is wrong as you type, completes what you are reaching for, and runs it under a
debugger.

Compass is an introductory programming language — exact fractions, no null, and errors that name
the fix.

## What you get

**Errors and warnings as you type.** From the compiler itself rather than a second opinion about
it, so what the editor underlines is exactly what a build would refuse. Warnings and opinions are
distinguished from errors, and what a program does not use is faded rather than flagged.

**Completion that knows where you are.** Not a list of every name in the file — what can go in
the position you are typing in. Hover for a type, go to definition, find all references, and
rename across a whole project.

**A debugger.** Breakpoints, stepping over and into, a call stack, a variables pane, and a Debug
Console that a program asking for input can be typed into.

**Projects.** Build and run a `.cmp` project from the editor, and ask which project claims the
file you are looking at.

**Formatting.** The same rules `cm format` applies, so the editor and the command line never
disagree about a file.

**Highlighting** for `.cm` programs and `.cmp` project files, with bracket matching, `Ctrl+/`
commenting, auto-closing quotes, and indentation that follows `end`.

## Before you start

**The compiler has to be on your PATH.** Everything but the highlighting goes through it — the
extension does not carry its own copy, so what you get in the editor is always what your compiler
does.

    cm --version

If that answers, you are set. If it does not, [install
Compass](https://compass.pluperfect.dev/install) first.

## Getting going

1. Open a folder with a `.cm` file in it, or make one.
2. Start typing. Highlighting and diagnostics need nothing configured.
3. Press **F5** to run it under the debugger. No `launch.json` is required for a single file.

There is a **Compass: Restart the server** command for the rare case where the compiler is
replaced while the editor is open.

## Colors

The extension ships no theme, on purpose — a theme that repaints your whole editor for one
language is a poor trade. It scopes Compass precisely enough that any theme colors it sensibly,
and there is a **Compass: Apply the palette** command that writes a Compass-only block into your
settings if you want the language's own colors.

## More

- [Everything the extension does, in full](https://github.com/mnwachukwu/Compass.Editors/blob/main/docs/the-extension.md)
- [The language](https://compass.pluperfect.dev) — a course, a playground that runs in the
  browser, samples, and the reference
- [Source](https://github.com/mnwachukwu/Compass.Editors) · [Report a
  problem](https://github.com/mnwachukwu/Compass.Editors/issues)

MIT licensed.
