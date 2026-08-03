# Profi-C for VS Code

Syntax highlighting for `.pc` programs and `.pcp` project files, the editor behavior that comes
with knowing the language — bracket matching, `Ctrl+/` inserting `#`, auto-closing quotes, and
indentation that follows `end` — and **a debugger**: breakpoints, stepping, a call stack, and a
variables pane.

The breadcrumbs, the Outline view and `Ctrl+Shift+O` all show what a file declares.

There are still no diagnostics as you type, no completion, and no hover. Those need a language
server, which is a later piece of work — an outline does not, because it is one question about
one file rather than a running analysis of all of them.

## Installing it

**This is not on the VS Code Marketplace**, and will not be for a while — Profi-C is young
enough that publishing an extension for it would be putting up a shopfront before there is
anything to sell. Installing it means putting this folder where VS Code looks, which is all the
Marketplace would do anyway. There is no build step: the grammars are declarative and the one
script is plain JavaScript with nothing to compile and no dependencies to fetch. Once it is
published, `code --install-extension` replaces all of this.

There are two ways to do it, and which one is right depends on whether the grammar is going to
change under you.

### Linking it — for anyone editing the language

The extensions directory holds a pointer to this folder, so the editor reads the very files in
the repository. A change shows up on the next window reload and there is no copy to remember.

**Windows** needs neither an elevated shell nor Developer Mode if you use a *junction*:

```powershell
$repo = "D:\Repos\Profi-C.Editors"
$dest = "$env:USERPROFILE\.vscode\extensions\profi-c"
if (Test-Path $dest) {
    if ((Get-Item $dest -Force).LinkType) { cmd /c rmdir "$dest" }
    else { cmd /c rmdir /s /q "$dest" }
}
New-Item -ItemType Junction -Path $dest -Target "$repo\vscode"
```

**Why that is four lines rather than one.** The folder no longer changes name between versions,
so this is a snippet you may well run again over something already there — and what is there
decides how to remove it. A **link** must be removed as a link: `Remove-Item -Recurse -Force` has
been known to follow a junction and delete what is on the other side of it, which here is the
repository. A **folder of copied files** has to be removed with its contents, which a bare
`rmdir` will not do. Asking `LinkType` tells the two apart, and each gets the removal it needs.

A **symbolic link** does the same job and needs an elevated shell, or Developer Mode turned on:

```powershell
New-Item -ItemType SymbolicLink -Path $dest -Target "$repo\vscode"
```

The two differ in ways that do not matter here. A junction is resolved by the file system and
works only for a directory on a local volume; a symbolic link may point at a file, at a
relative path, or across the network, and is the more general tool. Pointing one local folder
at another is exactly what a junction is for, so it is the one to reach for on Windows — the
elevation a symbolic link asks for buys nothing in this case.

**macOS and Linux** have one answer:

```bash
repo=~/Profi-C.Editors
dest=~/.vscode/extensions/profi-c
rm -rf "$dest" && ln -s "$repo/vscode" "$dest"
```

> **Removing a link, when the time comes.** On Windows, do **not** use
> `Remove-Item -Recurse -Force`: Windows PowerShell has been known to follow a junction and
> delete what is on the other side of it, which here is the repository. Remove the link alone:
>
> ```powershell
> (Get-Item "$env:USERPROFILE\.vscode\extensions\profi-c" -Force).Delete()
> ```
>
> or `cmd /c rmdir "%USERPROFILE%\.vscode\extensions\profi-c"` with no `/s`. On macOS and
> Linux, `rm` on the link removes the link.

### Copying it — for anyone who only wants to read Profi-C

Change the first line to wherever you cloned the repository, then run the rest as-is.

**Windows** (PowerShell):

```powershell
$repo = "D:\Repos\Profi-C.Editors"
$dest = "$env:USERPROFILE\.vscode\extensions\profi-c"
if (Test-Path $dest) {
    if ((Get-Item $dest -Force).LinkType) { cmd /c rmdir "$dest" }
    else { cmd /c rmdir /s /q "$dest" }
}
New-Item -ItemType Directory -Force -Path $dest | Out-Null
Copy-Item "$repo\vscode\*" $dest -Recurse -Force
```

**macOS and Linux**:

```bash
repo=~/Profi-C.Editors
dest=~/.vscode/extensions/profi-c
rm -rf "$dest" && mkdir -p "$dest" && cp -R "$repo/vscode/." "$dest"
```

### Either way

Reload the window — `Ctrl+Shift+P`, "Developer: Reload Window" — and open a `.pc` file.

- **VS Code Insiders** uses `.vscode-insiders` rather than `.vscode`. Everything else is the
  same.
- **The folder needs no version in its name.** The Marketplace lays extensions out as
  `publisher.name-version`, and it is easy to assume that is required — it is not. VS Code reads
  the version from `package.json`, so a plain `profi-c` folder is enough, and a link made once
  never has to be made again.
- **A grammar edit lands on the next reload. A `package.json` edit needs a version bump.**
  Grammar files are read each time one is needed; everything under `contributes` is read once,
  at the scan, and recorded in `~/.vscode/extensions/extensions.json` with the version beside
  it. Raise `version` when you change what the manifest contributes, or the editor goes on
  serving what it recorded — silently, with the file on disk plainly saying otherwise. The
  number is what forces the rescan; where the folder sits has nothing to do with it. Deleting
  that cache and restarting forces one if a bump ever fails to take.

**How a stale copy shows itself.** The editor colors by whatever rules it has, so a construct
the copy has never heard of is colored by the rules for something else, and the symptom looks
nothing like the cause. A word the copy does not know to be a keyword is caught by the rule for
a name followed by a bracket, and reads as a function call. A block string the copy predates
reads as an empty string followed by an ordinary one, so the first `"` inside it closes a
string that was never open and the rest of the file is colored as text.

Nothing is wrong with the grammar in either case; the editor is reading an old one. Before
hunting for a bug, check the installed copy holds the rule you expect:

```powershell
Select-String delegate "$env:USERPROFILE\.vscode\extensions\profi-c\syntaxes\profi-c.tmLanguage.json"
```

A link cannot go stale, which is the whole argument for one.

## Debugging

Set a breakpoint in the margin of a `.pc` file, press `F5`, and the program stops there. Step
over, step into, and step out all work; the call stack names every function in progress and the
variables pane shows what is in scope.

### What it needs

**The compiler on your PATH.** Everything about debugging happens inside `pc debug`, which
speaks the Debug Adapter Protocol over its standard input and output. This extension starts it
and gets out of the way — it holds no second copy of the rules about where to stop.

```bash
dotnet tool install --global profi-c
```

Where `pc` is somewhere else — installed to a folder of its own, or built from the Profi-C
repository — name it in your settings:

```jsonc
"profi-c.compilerPath": "D:\\Repos\\Profi-C\\src\\ProfiC.Cli\\bin\\Debug\\net10.0\\profi-c.exe"
```

A single configuration can override it too, with `compilerPath` beside `program`, which is the
way to debug one project against a build of the compiler without changing anything globally.

### The buttons above the file

Open a `.pc` file and two buttons appear side by side at the left of the editor's title bar.

**▶ Run** is the editor's own play button — the same one a C# or Python file gets. **One click
runs**, and the chevron beside it lists the two ways:

| | |
|---|---|
| **Run this file** | Compiles the open file with the shared code beside it, and runs it |
| **Run project associated with this file** | Finds the `.pcp` that lists the open file and runs that instead |

Both debug. There is one adapter and it always stops where breakpoints are set — what differs is
only what gets compiled. Whichever you pick last becomes what a single click does next time.

**A program that will not compile does not start, and says why in the Problems panel** — the
same place a build puts them, clickable to the line, and the panel is opened for you. Warnings
and opinions appear there too and stop nothing: the compiler runs a program that has them, so an
editor that refused to would be answering a different question than the compiler does.

**🔨 Build** sits immediately to its right:

| | |
|---|---|
| **Build this file** | Writes an assembly for the open file |
| **Build project associated with this file** | Writes an assembly for the project that lists it |
| **Choose the target platform** | Which platform every build aims at |

**Why Build's order is `-0.5`**, which looks like a typo and is not. The run button is not a slot
the editor reserves: it contributes *itself* to `editor/title` as a split button in group
`navigation` at **order −1**. The whole title bar is one sorted list — group first, `navigation`
ahead of every other, then the number after the `@` — and the run button is simply an early
entry in it.

Two things follow, and between them they rule out every ordinary number. **An entry that writes
no order sorts as zero**, and most title-bar icons write none, so `1` or `100` puts Build behind
whatever else the reader has installed. And `-1` is a *tie* with the run button, which VS Code
breaks by comparing titles — nothing a manifest can hold. What is left is the gap: anything
strictly between −1 and 0 lands after Run and ahead of the unordered field. The order is read
with JavaScript's `Number`, so a fraction is as valid as an integer.

### Which project is "associated"

**The one that lists the file, not the one nearest it.** A `.pcp` sitting above a file says what
it builds, and a file it does not list is no more part of it than one in another folder. Running
the nearest project regardless would compile a program you are not looking at, print its output,
and look exactly like the button working.

**The compiler answers it**, with `pc project`. Not "reads projects the way the compiler does" —
it is the same reader, which matters more than it sounds. A `.pcp` is read for a `source` naming
a file, a `source` naming a folder, and a `reference` to another project, but also for the two
comment forms and for `end project`; scanning the file for the word `source` gets a commented-out
one wrong, and gets it wrong in the direction that runs a program you were not looking at.

The search goes upward until a project claims the file. Where none does, the file itself is used
and you are told which happened:

- *no project found — running this file*
- *no project lists this file — running the file itself*

Two messages because they are two situations: one of you has no project, the other has one
sitting right there that does not want this file, and only the second has something to go and
look at.

### Building

The Build button compiles to a .NET assembly, into a `bin` beside the program, with a launcher
you can run without naming `dotnet`. It runs as a **task**, so the output goes to a terminal and
`Ctrl+Shift+B` finds it.

**Diagnostics land in the Problems panel**, clickable to the line. The compiler writes them in
the form every .NET tool already reads:

```
storefront/Program.pc(19,26): error PC0300: A string cannot be added to an integer.
```

Three severities are matched separately — `error` and `warning` as themselves, and **`opinion`
as information**. VS Code knows only the first two words; the third is Profi-C's. A single
matcher would file every opinion at its default severity, which would paint the panel red with
the one severity that means *this compiles fine, but*.

### Choosing what to build for

**Choose the target platform** lists the platforms this machine can actually build for, with the
current one marked, and the choice is remembered per workspace in `profi-c.targetPlatform`. It
shows in the status bar so you can see what you are aiming at without opening anything.

The list is asked of the compiler — `pc platforms` — rather than written into this extension,
because what is available depends on which launchers the SDK installed and which any project has
ever published for. A fixed list would offer platforms that cannot be built for, and finding that
out is the compiler's job:

```
pc: nothing here can build for 'freebsd-x64'. Available: linux-x64, osx-x64,
win-arm, win-arm64, win-x64, win-x86. 'dotnet publish -r freebsd-x64' on any
project fetches what is needed.
```

Every program that checks is a program that builds, so a failed build is a mistake in the
program rather than a construct the back end has not reached.

### Without a launch.json

**There is nothing to configure and no file to write.** The same two configurations appear in the
Run and Debug list, and `F5` on an open `.pc` file runs it. That is deliberate: asking a beginner
to write a launch.json first is asking them to learn the editor before the language — and asking
anyone to copy one into every folder they keep Profi-C in is asking them to do it forever.

### With one

For anything more — a fixed entry point, a project file, arguments you do not want to retype —
"create a launch.json" offers a Profi-C configuration:

```jsonc
{
  "type": "profi-c",
  "request": "launch",
  "name": "Debug the storefront",
  "program": "${workspaceFolder}/storefront/storefront.pcp"
}
```

`program` may be a `.pc` file or a `.pcp` project. Naming a `.pc` file debugs it together with
the shared code beside it, exactly as `pc run` compiles it — so stepping into a function
declared in another file works, and the call stack opens whichever file each frame is in.

### What it does not do

- **No expression evaluation.** The debug console shows what the program prints. Typing into it
  is answered with a refusal rather than a result — nothing in Profi-C evaluates an expression
  outside a running program yet.
- **No conditional breakpoints, hit counts, or log points.** A condition written on a breakpoint
  is ignored, and the breakpoint fires as an ordinary one.
- **No changing a value while stopped.** The variables pane is a view, not a control.
- **No attaching to a running program.** A session launches its own.

The adapter claims none of these, so where VS Code hides an unsupported feature it will be
hidden. Where it does not — the breakpoint menu is offered before a session exists to be asked
— setting one is allowed and then does nothing.

### When nothing happens

**Check whether the folder is trusted first.** In Restricted Mode, VS Code disables debugging
entirely and does not load extensions — so `F5` does nothing, and a `.pc` file is not even
colored. The banner saying so is easy to dismiss without reading, and a temporary folder is
never trusted, which makes this the likeliest thing to hit on the throwaway folder somebody
tries this in first. `Ctrl+Shift+P` → "Workspaces: Manage Workspace Trust".

**No color on a `.pc` file is the tell.** Highlighting is declarative and needs no extension to
run, so a file that is not colored means the extension was not loaded at all — trust, or a link
pointing at nothing. A file that *is* colored but will not debug is a different problem, and one
of these:

1. **`pc` is not on the PATH the editor sees.** VS Code inherits its environment from wherever
   it was launched, which on Windows may predate a `dotnet tool install`. Restarting the editor
   is usually enough; naming the full path in `profi-c.compilerPath` always is.
2. **The program does not compile.** Nothing starts, and the reasons are in the Problems panel
   rather than in a dialog — the panel is opened for you, and each entry clicks through to the
   line.
3. **The manifest is stale in the editor.** See the version note above; a `package.json` change
   without a bump goes on serving what was recorded at the scan.
4. **`extension.js` did not load.** Nothing says so in the editor; the debugger is simply absent
   from the menu. Output → Log (Extension Host) is where it is recorded.

## The outline

Breadcrumbs across the top, the Outline view in the sidebar, and `Ctrl+Shift+O` all draw from the
same place: `pc outline`, which prints what a file declares as JSON.

Asked of the compiler rather than read here, for the reason everything else in this repository is
— a parser written in JavaScript would be a second definition of the language, and the two would
agree until a construct was added to one of them. That failure is invisible: a member quietly
stops appearing and nothing anywhere reports it.

**It works on a file that does not compile**, which is when it matters. The outline is parsed and
nothing more, and the parser recovers — so a half-written function still shows the ones around it.

Two limits worth knowing:

- **It reads the file on disk**, so unsaved edits are not reflected until you save. Passing the
  buffer instead would mean writing a temporary file on every keystroke, and the thing that
  avoids both is a language server.
- **Clicking an entry goes to the start of the declaration**, not to its name, because the parser
  records where a declaration begins and not where its name sits inside it. A point rather than a
  guess.

## What it colors

Reserved words, the primitive types, the types the language provides, literals of every form
including fraction literals like `22|7` and floats like `3.14f`, block strings, the holes in an
interpolated string, both comment forms, and the name a declaration introduces. A closer and what it closes read as one thing, so `end function` colors together
rather than as a keyword beside a noun.

## Comments the compiler heeds

A line comment can carry an `ignore` directive, which silences a warning or an opinion:

```
# ignore opinion
Console.WriteLine("");
```

Only the comments the compiler acts on are set apart — a remark that merely begins with the
word, like `# ignore the sign for now`, stays an ordinary comment, and a `##` block never
carries a directive at all. The scope is `comment.line.number-sign.directive.profi-c`.

A comment can also document what follows it, and the label inside one is colored apart from
the prose:

```
##
    @summary: One person's money, and the rules about taking it out.
    @remarks: The longer explanation, for a hover rather than a list.
##
model Account
```

Only the label is colored — the mark, the name and the colon together — never the prose after
it. The scope is `constant.language.documentation.profi-c`.

## Where the colors come from

**This extension sets no colors, and none of the ones above come from it.** An extension can
offer token colors through `configurationDefaults`, and the editor accepts the manifest and
then ignores them. There is no error and no warning; the manifest sits there naming a color
nobody ever sees, which is why this extension no longer carries one.

So every color on a Profi-C file comes from one of two places:

1. **A `textMateRules` entry**, in your user or workspace settings — which is what
   "Profi-C: Use the Profi-C colors" writes, and what you can edit afterwards.
2. **Your theme**, for any scope no rule names.

That second case is where the confusion lives. A theme knows nothing about `.profi-c` scopes,
so it falls back on the general part of the name — `constant.language.documentation.profi-c`
is painted as whatever the theme does with `constant.language`. In several dark themes that is
the same color as `keyword`, so a scope that looks wrong may simply be a scope with no rule.

**When a color will not take, it is nearly always a missing rule rather than a broken one.**
Put the cursor on the token and run:

```
Developer: Inspect Editor Tokens and Scopes
```

The last line names the rule that won. A `.profi-c` scope means a rule is being applied; a bare
`constant.language` or `keyword` means none is, and the theme is deciding.

## Checking what the grammar really does

The scopes above are what a theme paints, so being wrong about them is easy and quiet. The
test suite runs the grammar through the engine VS Code itself uses and asserts the scopes that
come out, rather than reading the grammar file and believing it.

It needs the engine installed once:

```bash
npm install
```

After that `dotnet test` covers it. Without it those tests skip rather than fail, since a fresh
checkout not having fetched them is an ordinary state to be in. To look at the scopes on a line
by hand:

```bash
echo '["# @summary: A thing."]' | node tools/scopes.js
```

## The Profi-C palette

Everything above is already colored by whatever theme you use, because the grammar names its
scopes the way every other language does and a theme's rule for `keyword` reaches
`keyword.declaration.profi-c`. Nothing has to be installed for a `.pc` file to read properly.

What a theme cannot do is tell one Profi-C construct from another where it has no reason to.
A primitive type and a visibility word are both `storage`, so most themes paint them alike; a
documentation label inherits from `constant.language`, which several dark themes paint the same
color as a keyword. **A palette written for the language does better**, because it can separate
the things a reader of *this* language wants separated.

**That palette ships with this extension.** Run it once:

```
Profi-C: Use the Profi-C colors
```

It writes the rules into your own `settings.json` — the user one, not a workspace one — so they
apply in every folder you ever open a `.pc` file in. It applies as soon as it is written: no
reload, nothing to copy, and nothing to repeat per project. Running it twice leaves one copy,
and rules for other languages are left exactly as they were.

**Why a command rather than something the extension simply does.** VS Code's model is that a
theme owns colors and a grammar owns scopes. Token colors offered by an extension through
`configurationDefaults` are accepted into the manifest and then ignored — there is no error and
no warning, just a manifest naming colors nobody ever sees. Writing them where you would have
written them by hand is the supported way, and it is yours to undo: they are ordinary rules in
your settings, editable and deletable like any other.

The palette lives in [`palette.js`](palette.js), which is the only copy — two palettes in two
files drift, and the one in this README had already gone stale on three colors before a test was
written to hold them together. A shortened version, to show the shape:

```jsonc
"editor.tokenColorCustomizations": {
  "textMateRules": [
    // A line comment the compiler acts on, such as '# ignore opinion'.
    // Addressed to the compiler rather than to a reader, so it is worth
    // setting apart from the prose around it.
    { "scope": "comment.line.number-sign.directive.profi-c",
      "settings": { "foreground": "#7A7A7A" } },

    // The label in a documentation comment: '@summary:', '@yields:', or a
    // parameter's name, mark and colon together. Worth setting rather than
    // leaving to the theme, which paints a language constant the same color
    // as a keyword in several of the dark ones.
    { "scope": "constant.language.documentation.profi-c",
      "settings": { "foreground": "#00E5FF" } },

    // Both comment forms together. A comment is a comment whichever mark
    // opened it, and naming only one leaves the other whatever gray the
    // theme had in mind.
    { "scope": ["comment.block.profi-c", "comment.line.number-sign.profi-c"],
      "settings": { "foreground": "#4C9A5A" } }

    // ... and the rest, in palette.js
  ]
}
```

A workspace `settings.json` is scoped to the folder it sits in: it changes nothing about any
other project, and a color edited there applies at once with no reload. A user
`settings.json` does the same for everything you open.

**Inside an interpolated string**, the hole is `meta.interpolation`, its doubled braces are
`punctuation.section.interpolation.begin` and `.end`, and a pattern after the colon is
`constant.other.format`. What sits between the braces is code and is colored as code — a call
reads as a call, an operator as an operator. A block string written with `"""` is
`string.quoted.triple` and holds nothing else, since nothing inside one is read.

**Give `meta.interpolation` a color, even the plain one.** A hole is scanned inside the string
rule, so `string.quoted.double` stays on the scope stack while it is read, and anything in
there without a scope of its own — a local's name, a bracket, a comma — falls back to the
deepest scope that has a color. Leave `meta.interpolation` out and that is the string, so the
hole reads as the text around it. Naming it gives those tokens something nearer to fall back
to, which is what makes a hole look like code.

The full list, if you want something not above: `comment.line.number-sign`,
`string.quoted.double`, `string.quoted.single`, `constant.character.escape`,
`invalid.illegal.unknown-escape`, `constant.numeric.integer`, `constant.numeric.real`,
`constant.numeric.float`, `constant.numeric.fraction`,
`keyword.other.declaration`, `keyword.operator.comparison`, `keyword.operator.assignment`,
`keyword.operator.arithmetic`, `keyword.operator.optional` — each with `.profi-c` on the end.

A type name is `entity.name.type.profi-c` wherever it appears: after `model`, after
`extends`, after `new`, after `catch`, after `is` and `as`, and standing in front of the field,
local, or parameter it describes.

**Only the type's own name is colored.** In `Geometry.Solid.Circle`, `Circle` is the type and
`Geometry.Solid` says where to find it, so the namespace part is left plain — the same as the
name after `namespace` and after `using`, which are namespaces and nothing else. `Standard` is
left plain for the same reason, being the namespace the language provides rather than a type in
it.

Where a name is written to reach a member rather than to name a type — `Console.WriteLine`,
`Math.Pi`, `Color.Green` — the part before the dot is colored as a type. A grammar cannot tell
`Namespace.Type` from `Type.Member` when both are capitalized, and a program's own namespace in
that position will be colored as though it were a type. That is the same limit that makes a
local called `Total` look like one, and it goes away when the compiler is the thing answering.

## Keeping it honest

A TextMate grammar is a second, hand-written description of the same language, and adding a
keyword to the compiler does nothing to this file. `EditorGrammarTests` in the test project
reads this grammar and asserts that every reserved word and every type the language provides
appears in it, and that nothing it colors is a word the language dropped — so the two cannot
drift without a test failing at the moment they do.

The same tests check that the block comment rule closes where the scanner closes, including
the awkward lines: a heading run of marks, a block opened and closed on one line, and an opener
with text after it.

## GitHub

Fenced code blocks tagged `profi-c` render as plain text on GitHub today. Highlighting there
needs the language registered with [Linguist](https://github.com/github-linguist/linguist),
which asks for use across a few hundred repositories. Tagging them now costs nothing and starts
working if that ever happens.
