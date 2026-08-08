# Changelog

## 1.0.0

Initial release.

- **Syntax highlighting** for `.pc` source and `.pcp` project files.
- **Debugging**: breakpoints, stepping over and into, a call stack, variables, and a Debug
  Console a running program can be typed into.
- **Diagnostics, completion and navigation** through the compiler's own language server —
  errors and warnings as you type, completion aware of what the position expects, hover types,
  go to definition, find all references, rename across a project, and inlay hints for what a
  `let` was given.
- **Project management** for `.pcp` files: build, run, and ask which project claims a file.
- **Formatting**, matching what `pc format` does, so the editor and the command line never
  disagree about a file.

Requires `pc` on the PATH for everything but the highlighting.
