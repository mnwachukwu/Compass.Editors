# The extension, in full

Everything the VS Code extension does, and how it is put together. The
[listing](../vscode/README.md) is the short version, written for somebody deciding whether to
install it; this is the one to read when something is not behaving, or when you are working on
the extension itself.

Syntax highlighting for `.pc` programs and `.pcp` project files, the editor behavior that comes
with knowing the language — bracket matching, `Ctrl+/` inserting `#`, auto-closing quotes, and
indentation that follows `end` — and **a debugger**: breakpoints, stepping, a call stack, and a
variables pane.

The breadcrumbs, the Outline view and `Ctrl+Shift+O` all show what a file declares.

Diagnostics as you type, completion, hover, go to definition, find all references and rename
come from the compiler's own language server, which the extension starts and keeps running. The
outline does not go through it, because it is one question about one file rather than a running
analysis of all of them.

## Installing it

**This is not on the VS Code Marketplace**, and will not be for a while — Profi-C is young
enough that publishing an extension for it would be putting up a shopfront before there is
anything to sell. Installing it means putting this folder where VS Code looks, which is all the
Marketplace would do anyway. There is no build step: the grammars are declarative and the one
script is plain JavaScript with nothing to compile. Once it is published,
`code --install-extension` replaces all of this.

**Fetch the one dependency first**, from this folder:

```bash
npm install --omit=dev
```

That is `vscode-languageclient`, which is how the extension talks to `pc lsp` — the compiler
answering questions about a file while it is being typed. Everything else here works without it,
so a failed install costs the live diagnostics and nothing more.

There are then two ways to install, and which one is right depends on whether the grammar is
going to change under you.

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

**Both buttons save first.** The compiler reads files rather than buffers, so pressing Run with
unsaved edits would compile the version you have just changed away from — and report mistakes
about text no longer on screen. Every Profi-C file with unsaved edits is saved before anything is
compiled, which also means what ran is what is on disk: `pc run` in a terminal gives the same
answer as the button.

## Diagnostics while you type

Open a `.pc` file and the compiler starts answering about it as you write, through `pc lsp` — a
language server that stays open and holds the file as the editor holds it, rather than reading
what was last saved.

**It waits for you to stop.** Analysis runs about a third of a second after the last keystroke,
not on every one. The reason is not cost — a thousand-line file goes through the whole front end
in single-digit milliseconds — but that diagnostics flickering while you type are worse than
none: half-written code is full of errors that are not errors, and a panel strobing red teaches a
beginner to ignore it. Opening and saving do not wait, since both are you saying you have
stopped.

**A program is a compilation, not a file.** A mistake in `Shelf.pc` is reported against
`Shelf.pc` even where you are looking at `Program.pc` and have never opened it — and it is
cleared from the panel when it is fixed, which takes an explicit message rather than silence.

The same server answers everything else about wherever the cursor is:

| | |
|---|---|
| **Hover** | What the thing under the cursor is — a local's type, or a function written out with what it yields and what it takes |
| **Go to definition** | `F12`, and it crosses files: a name declared in `Shelf.pc` is followed from `Program.pc` |
| **Breadcrumbs and the Outline** | What the file declares. This used to come from `pc outline`, which reads the disk — so it showed the file as last saved. It now follows what you are typing |
| **Completion** | After a dot, typing `word.` lists what a `string` answers, `shape.` what that model declares and inherited, `Math.` what is reached through the name. On its own, the locals and parameters in force, every type you can reach, and `this` |
| **Signature help** | What a function takes, while you are typing the arguments — which is when you cannot see the declaration, because you are looking at the call |
| **Quick fixes** | The lightbulb beside `&&` offers `and`. Also `\|\|`, `**` and `!` |
| **Rename** | `F2` changes a name everywhere it is written, across every file in the program — and refuses on a name the language owns, since renaming `Count` would edit your uses and leave the compiler's declaration where it is |
| **Coloring by what a name is** | Every name colored for what the compiler worked out it means, rather than for what it looks like on the line |
| **Every use of a name** | Put the caret on a name and the other places this file writes it are marked, with the ones that assign to it marked differently from the ones that read it |
| **Formatting** | `Shift+Alt+F`, or a selection with `Ctrl+K Ctrl+F`. It lines your code up and never moves it |

**Rename is the one answer that writes to your files, so it is the one that asks the compiler
hardest.** Every edit is an identifier the parser recorded the position of while reading it, and
which names to change is which nodes the resolver bound to the same thing — not a text search,
and not a second opinion here about what is in scope. What `F2` offers is what the compiler
believes; the two cannot drift, because there is only one of them.

**The formatter lines your code up and never moves it.** It fixes indentation and spacing;
it does not wrap a long line, join two short ones, or reorder anything. That is a smaller promise
than most formatters make and it is one that can be kept in every case — including the two that
matter most while you are writing. **A comment cannot be lost**, because nothing is ever removed
from a line, only the whitespace in front of it. And **a file that will not compile is formatted
anyway**, because none of this needs a syntax tree.

Where a wrapped line goes is decided by the line above it. If that line ended with a comma or an
opening bracket, something new begins, and it lines up with the first thing in the bracket — so
the rows of a matrix line up with the first row. Otherwise it carries the line above on, and takes
one indent past that column, so it does not read as another argument when it is the rest of one.
A lambda written across several lines is the exception: its body is a body, and nests from the
statement rather than being pushed out to wherever the call's bracket fell.

Two things it will not touch. The inside of a block string is the string — those spaces are
characters your program holds, down to the trailing ones. The inside of a block comment is prose
you laid out.

**Marking every use is rename's question without the edit**, which is why it can be asked far more
often — it runs whenever the caret moves, rather than when you have committed to something. Two
locals in different functions both called `value` are two names and are marked separately, which
is the thing a text search gets wrong. And unlike `F2` it answers for names the language owns:
putting the caret on `Count` marks every `Count` on a *string* and leaves the ones on a set alone,
because those are different members that happen to share a spelling.

**A quick fix is only offered where one substitution does the whole job.** `x += 1` has a good
message and no button: it becomes `x = x + 1`, which needs to know what `x` is. A button that
produced `x = 1` would be worse than none.

**The coloring is two things, and the second one is new.** The highlighting that arrives with the
file is a grammar: regex over a line, which is why it is instant and why it works on a file
nothing has compiled yet. It can see that `total` is an identifier. It cannot see that `total` is
the parameter declared six lines up, because that is a question about meaning.

So the server answers that half. Every name is classified from the compiled program, and two
things follow that a grammar could never have done:

- **A parameter looks like a parameter everywhere**, not only in the signature. The grammar
  colored the name in `function Length(string word)` because of where it sat on the line and gave
  up on `word` in the body. That was always the half you spend your time reading.
- **A local is colored like a field**, because a name that holds a value is the useful thing to
  see. Telling the two apart is already the language's job — a field is written through `this.`
  and a shared one through a type name, so what kind of thing it is has been said before the name
  arrives, and a second color would say it twice.

You also get `constant` rendered as read-only and `shared` as static without anybody picking a
color, because the server describes a name in the protocol's own vocabulary and most themes
already have opinions about those.

**Run `Profi-C: Use the Profi-C colors` to get them.** It writes both sets into your own settings
— an extension cannot impose colors — and turns semantic highlighting on for `.pc` files, which
matters more than it sounds: it ships set to "whatever the theme wants", so a theme that does not
ask for it would discard all of the above and say nothing about why.

**Completion is the one question that cannot be asked of what you have written.** `word.` is not
Profi-C and never will be — there is no member yet, so there is no member access to ask about. The
server puts a name where the member will go, compiles *that*, and reads the receiver off it. So
what is offered is what the compiler would resolve, rather than a guess made by something reading
half-written syntax.

What it will not offer is a member you could not reach: a private field is left out, because a
suggestion the next keystroke refuses is worse than a shorter list.

**A bare name needs no such trick, and could not use one.** What is wanted there is not the type
of something you wrote but which names are in force where the cursor sits — so the compiler writes
that down as it resolves, and the server reads it. You get the locals and parameters actually in
scope (not the ones from a block that has closed, and not one declared further down the file),
every type you can reach by name, and `this` where there is an instance to speak of. That last one
matters more here than it would elsewhere: every field is written through `this.`, so it is the
first thing typed on a great many lines.

A scope is a stretch of the file rather than a piece of syntax, which is why this still works on
the line you are in the middle of typing — the half-written line does not have to parse for the
names around it to be known.

### Replacing the compiler while the editor is open

**`Profi-C: Stop the language server`** and **`Profi-C: Restart the language server`**.

You need the first one if you are working on the compiler itself. The server is a running copy of
`pc`, and a running program cannot be overwritten on Windows — so publishing a new one over it
fails, and the only way out used to be closing the editor. Stop it, publish, then restart:

```bash
dotnet publish src/ProfiC.Cli.Alias -p:PublishProfile=dist
```

Restarting also starts one that is not running, which is what you want after stopping. Both say
what happened, because a command that appears to do nothing gets run twice and then distrusted.

A compiler too old to know the command has no server to connect to, and nothing says so: the
highlighting is declarative, and running and building are their own commands. The outline falls
back to `pc outline` in that case, which is why it is still there. The client's output channel
records what happened for anyone looking.

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
