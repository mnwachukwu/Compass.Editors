# Changelog

## 1.0.1

The extension is Compass, and the compiler it runs is `cm`.

- Highlighting, projects and debugging read `.cm` source and `.cmp` project files.
- The Problems panel is filled from diagnostics identified `CM0001` and upward.
- Commands are named `compass.*`, and the debug type is `compass`.

Renamed from Profi-C. It is listed as `Pluperfect.compass`.

## 1.0.0

Initial release.

- **Syntax highlighting** for `.cm` source and `.cmp` project files.
- **Debugging**: breakpoints, stepping over and into, a call stack, variables, and a Debug
  Console a running program can be typed into.
- **Diagnostics, completion and navigation** through the compiler's own language server —
  errors and warnings as you type, completion aware of what the position expects, hover types,
  go to definition, find all references, rename across a project, and inlay hints for what a
  `let` was given.
- **Project management** for `.cmp` files: build, run, and ask which project claims a file.
- **Formatting**, matching what `cm format` does, so the editor and the command line never
  disagree about a file.

Requires `cm` on the PATH for everything but the highlighting.
