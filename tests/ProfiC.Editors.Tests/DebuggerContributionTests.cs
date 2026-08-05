using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ProfiC.Editors.Tests;

/// <summary>
/// <para>Holds the debugger's manifest to the code that implements it.</para>
/// <para>Debugging is spread across two files that VS Code never checks against each other. The
/// manifest names a type, a setting, and a set of languages; the extension registers a factory
/// against a type and reads a setting by name. Nothing anywhere fails when those names drift —
/// VS Code matches by string, finds nothing, and does nothing. The symptom is a debugger that
/// appears in the menu, starts, and ends immediately, which reads as the compiler being broken
/// rather than as a typo in a manifest.</para>
/// <para>None of this needs VS Code running. Every claim here is about two files agreeing, which
/// is a thing a test can settle on its own.</para>
/// </summary>
[TestFixture]
public sealed class DebuggerContributionTests : EditorTestBase
{
    private static string ManifestPath => Path.Combine(Extension, "package.json");

    private static string ScriptPath => Path.Combine(Extension, "extension.js");

    private static JsonDocument Manifest() =>
        JsonDocument.Parse(File.ReadAllText(ManifestPath));

    private static JsonElement Contributes() =>
        Manifest().RootElement.GetProperty("contributes");

    private static JsonElement Debugger() =>
        Contributes().GetProperty("debuggers").EnumerateArray().Single();

    /// <summary>
    /// <para>A constant the script declares, read out of the source.</para>
    /// <para>Read rather than executed, since running the script means a VS Code to run it in.
    /// The constants it must agree with the manifest about are declared plainly at the top for
    /// exactly this reason, so that what a test needs and what a reader needs are the same
    /// line.</para>
    /// </summary>
    private static string Declared(string name)
    {
        Match found = Regex.Match(
            File.ReadAllText(ScriptPath),
            $@"const\s+{Regex.Escape(name)}\s*=\s*'([^']*)'\s*;");

        Assert.That(found.Success, Is.True, $"extension.js should declare {name}");

        return found.Groups[1].Value;
    }

    [Test]
    public void TheManifestIsValidJson() =>
        Assert.DoesNotThrow(() => Manifest().Dispose());

    /// <summary>
    /// <para>The script parses.</para>
    /// <para>Nothing else here would notice if it did not — every other test reads it as text.
    /// And VS Code is close to silent about it: an extension whose entry point will not parse
    /// simply never activates, so the debugger is absent from a menu it is contributed to, and
    /// the reason is a stack trace in an extension host log nobody thinks to open.</para>
    /// <para>Skipped where node is not installed, the same as tokenizing: a checkout without it
    /// is an ordinary state to be in, and CI has it.</para>
    /// </summary>
    [Test]
    public void TheScriptParses()
    {
        ProcessStartInfo start = new()
        {
            FileName = "node",
            WorkingDirectory = Extension,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        start.ArgumentList.Add("--check");
        start.ArgumentList.Add(ScriptPath);

        Process node;

        try
        {
            node = Process.Start(start)!;
        }
        catch (Exception unavailable)
        {
            Assert.Ignore($"node is needed to check the script parses: {unavailable.Message}");
            throw;
        }

        using (node)
        {
            string complaint = node.StandardError.ReadToEnd();
            node.WaitForExit();

            Assert.That(node.ExitCode, Is.Zero, complaint);
        }
    }

    /// <summary>
    /// The manifest points at a script that is there. A missing entry point is not reported by
    /// the editor at load; the extension simply never activates.
    /// </summary>
    [Test]
    public void TheManifestPointsAtAScriptThatExists()
    {
        string main = Manifest().RootElement.GetProperty("main").GetString()!;
        string resolved = Path.Combine(Extension, main.TrimStart('.', '/'));

        Assert.That(File.Exists(resolved), Is.True, resolved);
    }

    /// <summary>
    /// <para>Breakpoints are allowed in a Profi-C file.</para>
    /// <para>The one contribution with no graceful failure: without it VS Code refuses to place
    /// a breakpoint in a <c>.pc</c> file at all, so the adapter is never asked to stop anywhere
    /// and every other part of this works perfectly on a program that never pauses.</para>
    /// </summary>
    [Test]
    public void ABreakpointMayBeSetInAProfiCFile() =>
        Assert.That(
            Contributes().GetProperty("breakpoints")
                         .EnumerateArray()
                         .Select(allowed => allowed.GetProperty("language").GetString()),
            Does.Contain("profi-c"));

    /// <summary>
    /// <para>A project file is launchable but holds no breakpoints.</para>
    /// <para>Not an oversight in either direction. A <c>.pcp</c> lists the files to compile and
    /// contains no statements, so there is nothing in one to stop on — while starting a session
    /// from one is the ordinary way to debug a program of several files.</para>
    /// </summary>
    [Test]
    public void AProjectFileIsLaunchableButHoldsNoBreakpoints()
    {
        string[] breakpoints =
        [
            .. Contributes().GetProperty("breakpoints")
                            .EnumerateArray()
                            .Select(allowed => allowed.GetProperty("language").GetString()!),
        ];

        string[] launchable =
        [
            .. Debugger().GetProperty("languages")
                         .EnumerateArray()
                         .Select(language => language.GetString()!),
        ];

        Assert.Multiple(() =>
        {
            Assert.That(launchable, Does.Contain("profi-c-project"));
            Assert.That(breakpoints, Does.Not.Contain("profi-c-project"));
        });
    }

    /// <summary>Every language the debugger claims is one the extension actually contributes.</summary>
    [Test]
    public void TheDebuggerOnlyClaimsLanguagesTheExtensionContributes()
    {
        string[] contributed =
        [
            .. Contributes().GetProperty("languages")
                            .EnumerateArray()
                            .Select(language => language.GetProperty("id").GetString()!),
        ];

        Assert.That(
            Debugger().GetProperty("languages").EnumerateArray().Select(l => l.GetString()),
            Is.SubsetOf(contributed));
    }

    /// <summary>
    /// The type in the manifest is the type the script registers against. VS Code matches a
    /// launch configuration to a factory by this string and reports nothing when it matches
    /// nothing.
    /// </summary>
    [Test]
    public void TheManifestAndTheScriptAgreeOnTheDebuggersType() =>
        Assert.That(
            Debugger().GetProperty("type").GetString(),
            Is.EqualTo(Declared("DebuggerType")));

    /// <summary>
    /// The setting the script reads is a setting the manifest declares. A setting read but never
    /// declared is always empty and never appears in the settings editor, so the fallback stands
    /// in silently and the reader's choice of compiler is ignored.
    /// </summary>
    [Test]
    public void TheManifestAndTheScriptAgreeOnTheCompilerSetting() =>
        Assert.That(
            Contributes().GetProperty("configuration")
                         .GetProperty("properties")
                         .EnumerateObject()
                         .Select(property => property.Name),
            Does.Contain(Declared("CompilerPathSetting")));

    /// <summary>
    /// <para>The compiler defaults to the bare command, which is what an install puts on PATH.
    /// </para>
    /// <para>Declared in both files and so worth checking in both: the manifest's default is
    /// what the settings editor shows, and the script's is what runs where the setting was never
    /// written to disk at all.</para>
    /// </summary>
    [Test]
    public void TheCompilerDefaultsToWhatAnInstallPutsOnThePath()
    {
        string declared = Contributes().GetProperty("configuration")
                                       .GetProperty("properties")
                                       .GetProperty(Declared("CompilerPathSetting"))
                                       .GetProperty("default")
                                       .GetString()!;

        Assert.That(declared, Is.EqualTo(Declared("DefaultCompiler")));
    }

    /// <summary>A launch says what to debug, and the editor is the one that should insist.</summary>
    [Test]
    public void ALaunchMustSayWhichProgram() =>
        Assert.That(
            Debugger().GetProperty("configurationAttributes")
                      .GetProperty("launch")
                      .GetProperty("required")
                      .EnumerateArray()
                      .Select(name => name.GetString()),
            Does.Contain("program"));

    /// <summary>
    /// <para>Every configuration the extension offers to write is one it can then run.</para>
    /// <para>These are what a reader gets from "create a launch.json" and from the snippet list,
    /// so a wrong type here hands somebody a file that looks official and does nothing. Checked
    /// against the script's constant rather than against a literal, so that renaming the
    /// debugger cannot leave a stale offer behind.</para>
    /// </summary>
    [Test]
    public void EveryOfferedConfigurationCarriesTheDebuggersType()
    {
        string type = Declared("DebuggerType");

        JsonElement[] offered =
        [
            .. Debugger().GetProperty("initialConfigurations").EnumerateArray(),
            .. Debugger().GetProperty("configurationSnippets")
                         .EnumerateArray()
                         .Select(snippet => snippet.GetProperty("body")),
        ];

        Assert.That(offered, Is.Not.Empty);

        Assert.Multiple(() =>
        {
            foreach (JsonElement configuration in offered)
            {
                Assert.That(configuration.GetProperty("type").GetString(), Is.EqualTo(type));
                Assert.That(configuration.GetProperty("request").GetString(), Is.EqualTo("launch"));

                Assert.That(
                    configuration.TryGetProperty("program", out JsonElement program)
                        && !string.IsNullOrEmpty(program.GetString()),
                    Is.True,
                    "an offered configuration that names no program is one that cannot run");
            }
        });
    }

    /// <summary>
    /// <para>Every launch this extension offers or starts reveals the Debug Console.</para>
    /// <para>Printing is the whole of what most Profi-C programs do, so a run whose output lands
    /// in a panel nobody opened is indistinguishable from a run that did nothing — and the first
    /// thing a reader concludes is that the language is broken rather than that a panel is
    /// closed.</para>
    /// <para>Both halves are held because a launch can be started from either. The manifest's
    /// offers are what "create a launch.json" writes; the script's are what the Run button
    /// passes, and one carrying it while the other does not is a Run button that behaves
    /// differently depending on whether a file nobody looked at exists.</para>
    /// </summary>
    [Test]
    public void EveryLaunchRevealsTheDebugConsole()
    {
        string revealing = Declared("ShowTheConsole");

        JsonElement[] offered =
        [
            .. Debugger().GetProperty("initialConfigurations").EnumerateArray(),
            .. Debugger().GetProperty("configurationSnippets")
                         .EnumerateArray()
                         .Select(snippet => snippet.GetProperty("body")),
        ];

        string script = File.ReadAllText(ScriptPath);

        Assert.Multiple(() =>
        {
            foreach (JsonElement configuration in offered)
            {
                Assert.That(
                    configuration.TryGetProperty(
                        "internalConsoleOptions", out JsonElement console)
                        && console.GetString() == revealing,
                    Is.True,
                    $"'{configuration.GetProperty("name").GetString()}' would run with the "
                    + "console left closed");
            }

            // The script writes its configurations as object literals rather than as data, so
            // they are counted rather than read: every launch it builds must carry the line.
            Assert.That(
                Regex.Matches(script, @"request:\s*'launch'").Count,
                Is.EqualTo(Regex.Matches(script, @"internalConsoleOptions:\s*ShowTheConsole").Count),
                "extension.js builds a launch that leaves the Debug Console closed");
        });
    }

    // ---- Running without writing a launch.json ----------------------------------------------

    /// <summary>
    /// <para>Every command the manifest offers is one the script registers.</para>
    /// <para>An unregistered command is offered in the palette and in the editor's title bar,
    /// and reports "command not found" when it is used. Nothing checks this but this.</para>
    /// </summary>
    [Test]
    public void EveryOfferedCommandIsRegistered()
    {
        string script = File.ReadAllText(ScriptPath);

        string[] offered =
        [
            .. Contributes().GetProperty("commands")
                            .EnumerateArray()
                            .Select(command => command.GetProperty("command").GetString()!),
        ];

        Assert.That(offered, Is.Not.Empty);

        Assert.Multiple(() =>
        {
            foreach (string command in offered)
            {
                Assert.That(
                    script,
                    Does.Contain($"registerCommand('{command}'"),
                    $"'{command}' is offered but never registered");
            }
        });
    }

    /// <summary>
    /// <para>Run is the editor's own button, and Build sorts immediately after it.</para>
    /// <para>Both facts come from one thing worth writing down, since it is the only reason any
    /// of these numbers make sense. VS Code's run button is not a fixed slot the editor reserves
    /// — it contributes itself to <c>editor/title</c> as a split-button submenu in group
    /// <c>navigation</c> at <b>order -1</b>. So it competes on exactly the terms everything else
    /// does, and the whole title bar is one sorted list.</para>
    /// <para>That is what earlier attempts were up against without knowing it. An order of 1 or
    /// 100 put Build behind every extension that writes no order at all — and a missing order
    /// sorts as zero, which is most of them. An order of -1 tied with the run button itself, and
    /// a tie is broken by comparing titles, which is not a thing this can hold.</para>
    /// <para>Anything strictly between -1 and 0 lands after Run and ahead of the unordered field.
    /// Fractions are allowed: the order is read with JavaScript's <c>Number</c>, not parsed as an
    /// integer.</para>
    /// </summary>
    [Test]
    public void RunIsTheEditorsOwnButtonAndBuildSortsRightAfterIt()
    {
        const double TheRunButton = -1;
        const double WritingNoOrder = 0;

        JsonElement menus = Contributes().GetProperty("menus");

        JsonElement[] inTitle = [.. menus.GetProperty("editor/title").EnumerateArray()];

        double order = double.Parse(
            inTitle.Single().GetProperty("group").GetString()!.Split('@')[1],
            System.Globalization.CultureInfo.InvariantCulture);

        Assert.Multiple(() =>
        {
            Assert.That(
                inTitle.Single().GetProperty("submenu").GetString(),
                Is.EqualTo("profi-c.build"),
                "Build is the only button of our own; Run belongs to the editor");

            Assert.That(order, Is.GreaterThan(TheRunButton), "or Build would sit left of Run");

            Assert.That(
                order,
                Is.LessThan(WritingNoOrder),
                "or anything contributing an icon without an order would come between them");

            Assert.That(
                inTitle.Single().GetProperty("when").GetString(),
                Does.Contain("profi-c"),
                "a button on every file of every language would be somebody else's bug");
        });
    }

    /// <summary>
    /// <para>The run button offers both ways to run, and only where they mean something.</para>
    /// <para><c>editor/title/run</c> is shared: it is the one list behind every language's play
    /// button. An entry there without a <c>when</c> is not a Profi-C button — it is a line reading
    /// "Run project associated with this file" hanging off the run button of every Python and C#
    /// file the reader opens.</para>
    /// </summary>
    [Test]
    public void TheRunButtonOffersBothWaysToRunAndOnlyForProfiC()
    {
        JsonElement[] listed =
        [
            .. Contributes().GetProperty("menus")
                            .GetProperty("editor/title/run")
                            .EnumerateArray(),
        ];

        Assert.Multiple(() =>
        {
            Assert.That(
                listed.Select(e => e.GetProperty("command").GetString()),
                Is.EqualTo(new[] { "profi-c.runFile", "profi-c.runProject" }));

            foreach (JsonElement entry in listed)
            {
                Assert.That(
                    entry.TryGetProperty("when", out JsonElement when)
                        && when.GetString()!.Contains("profi-c"),
                    Is.True,
                    $"{entry.GetProperty("command").GetString()} would show on every language");
            }
        });
    }

    /// <summary>Build's list holds what it should, and nothing has lost its home.</summary>
    [Test]
    public void BuildHoldsItsOwnCommands() =>
        Assert.That(
            Contributes().GetProperty("menus")
                         .GetProperty("profi-c.build")
                         .EnumerateArray()
                         .Select(e => e.GetProperty("command").GetString()),
            Is.EqualTo(new[]
            {
                "profi-c.buildFile", "profi-c.buildProject",
                "profi-c.setOutputFolder", "profi-c.chooseTarget",
            }));

    /// <summary>
    /// <para>The two ways to run are named as the editor names them elsewhere.</para>
    /// <para>Worth pinning rather than left to taste: a reader arriving from C# reads the same
    /// words for the same act, and the earlier names were invented here and read like nothing
    /// else in the editor.</para>
    /// </summary>
    [Test]
    public void TheCommandsAreNamedAsTheEditorNamesThem()
    {
        Dictionary<string, string> titles = Contributes()
            .GetProperty("commands")
            .EnumerateArray()
            .ToDictionary(
                c => c.GetProperty("command").GetString()!,
                c => c.GetProperty("title").GetString()!);

        Assert.Multiple(() =>
        {
            Assert.That(titles["profi-c.runFile"], Is.EqualTo("Run this file"));

            Assert.That(
                titles["profi-c.runProject"],
                Is.EqualTo("Run project associated with this file"));
        });
    }

    /// <summary>
    /// <para>The extension offers its configurations rather than waiting to be given one.</para>
    /// <para>A dynamic provider is what puts entries in the Run and Debug list when no
    /// launch.json exists anywhere. Without it the only configurations a reader ever sees are
    /// ones somebody wrote into a folder by hand, which is one file per project forever.</para>
    /// </summary>
    [Test]
    public void ConfigurationsAreOfferedWithoutALaunchJson()
    {
        string script = File.ReadAllText(ScriptPath);

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Contain("provideDebugConfigurations"));

            Assert.That(
                script,
                Does.Contain("DebugConfigurationProviderTriggerKind.Dynamic"),
                "a provider registered without this only answers when a launch.json is written");

            Assert.That(
                Manifest().RootElement.GetProperty("activationEvents")
                          .EnumerateArray()
                          .Select(e => e.GetString()),
                Does.Contain("onDebugDynamicConfigurations:profi-c"));
        });
    }

    // ---- Building ---------------------------------------------------------------------------

    /// <summary>
    /// <para>Build is its own button in the editor title, with its own list.</para>
    /// <para>A submenu rather than more entries under Run, because building and running are
    /// different acts — and because entries added to <c>editor/title/run</c> do not become
    /// buttons, they join the one the play icon already has.</para>
    /// </summary>
    [Test]
    public void BuildIsItsOwnButtonWithItsOwnList()
    {
        JsonElement contributes = Contributes();

        string[] inTitle =
        [
            .. contributes.GetProperty("menus").GetProperty("editor/title")
                          .EnumerateArray()
                          .Select(e => e.GetProperty("submenu").GetString()!),
        ];

        string[] inList =
        [
            .. contributes.GetProperty("menus").GetProperty("profi-c.build")
                          .EnumerateArray()
                          .Select(e => e.GetProperty("command").GetString()!),
        ];

        Assert.Multiple(() =>
        {
            Assert.That(inTitle, Does.Contain("profi-c.build"));

            Assert.That(
                contributes.GetProperty("submenus").EnumerateArray()
                           .Select(s => s.GetProperty("id").GetString()),
                Does.Contain("profi-c.build"),
                "a menu placed in the title must be declared as a submenu to become a button");

            Assert.That(inList, Is.EqualTo(new[]
            {
                "profi-c.buildFile", "profi-c.buildProject",
                "profi-c.setOutputFolder", "profi-c.chooseTarget",
            }));
        });
    }

    /// <summary>
    /// <para>Building and saying where a build goes are separate sections.</para>
    /// <para>Menu groups are what draw the line between them: entries sharing a group sit
    /// together, and a change of group renders a separator. Putting all four in one group would
    /// read as four equal choices, when the last two are settings the first two obey — one
    /// naming the folder the build lands in and one the machine it is built for.</para>
    /// </summary>
    [Test]
    public void WhatToBuildAndWhereItGoesAreSeparateSections()
    {
        Dictionary<string, string> groups = Contributes()
            .GetProperty("menus")
            .GetProperty("profi-c.build")
            .EnumerateArray()
            .ToDictionary(
                e => e.GetProperty("command").GetString()!,
                e => e.GetProperty("group").GetString()!.Split('@')[0]);

        Assert.Multiple(() =>
        {
            Assert.That(groups["profi-c.buildFile"], Is.EqualTo(groups["profi-c.buildProject"]));

            Assert.That(
                groups["profi-c.setOutputFolder"], Is.EqualTo(groups["profi-c.chooseTarget"]),
                "both say where a build goes, so they sit together");

            Assert.That(
                groups["profi-c.chooseTarget"],
                Is.Not.EqualTo(groups["profi-c.buildFile"]),
                "a separator is a change of group, and there is nothing else that draws one");
        });
    }

    /// <summary>
    /// <para>Three problem matchers, one per severity the compiler writes.</para>
    /// <para>VS Code's severities are error, warning and info. Profi-C's third is
    /// <c>opinion</c>, which VS Code has never heard of — so a single matcher capturing the word
    /// would fall back to its default for every one of them, painting the Problems panel red
    /// with the one severity that means "this compiles fine, but". Each severity gets a matcher
    /// anchored on its own word instead.</para>
    /// </summary>
    [Test]
    public void EachSeverityHasItsOwnMatcher()
    {
        Dictionary<string, string> matchers = Contributes()
            .GetProperty("problemMatchers")
            .EnumerateArray()
            .ToDictionary(
                m => m.GetProperty("name").GetString()!,
                m => m.GetProperty("severity").GetString()!);

        Assert.Multiple(() =>
        {
            Assert.That(matchers["profi-c-error"], Is.EqualTo("error"));
            Assert.That(matchers["profi-c-warning"], Is.EqualTo("warning"));

            Assert.That(matchers["profi-c-opinion"], Is.EqualTo("info"),
                        "an opinion is not a problem, and must not be shown as one");
        });
    }

    /// <summary>
    /// <para>Each matcher reads what the compiler actually writes.</para>
    /// <para>Checked against the real form rather than against the regex's intent, because a
    /// matcher that matches nothing is silent: the build runs, the terminal fills with
    /// diagnostics, and the Problems panel stays empty as though everything were fine.</para>
    /// </summary>
    [TestCase("error", @"samples\bank.pc(19,1): error PC0501: The model 'Book' cannot be emitted yet.")]
    [TestCase("warning", @"D:\x\warn.pc(4,9): warning PC0403: This can never be reached.")]
    [TestCase("info", @"/home/matt/noisy.pc(5,9): opinion PC0406: This loop has no condition.")]
    public void AMatcherReadsWhatTheCompilerWrites(string severity, string line)
    {
        JsonElement matcher = Contributes()
            .GetProperty("problemMatchers")
            .EnumerateArray()
            .Single(m => m.GetProperty("severity").GetString() == severity);

        JsonElement pattern = matcher.GetProperty("pattern");

        Match read = Regex.Match(line, pattern.GetProperty("regexp").GetString()!);

        Assert.Multiple(() =>
        {
            Assert.That(read.Success, Is.True, line);
            Assert.That(read.Groups[pattern.GetProperty("line").GetInt32()].Value, Is.Not.Empty);
            Assert.That(read.Groups[pattern.GetProperty("column").GetInt32()].Value, Is.Not.Empty);

            Assert.That(
                read.Groups[pattern.GetProperty("code").GetInt32()].Value,
                Does.StartWith("PC"),
                "the code is what a reader searches the diagnostics appendix for");
        });
    }

    /// <summary>
    /// A matcher only reads its own severity, or every diagnostic would be reported three times
    /// at three different severities.
    /// </summary>
    [Test]
    public void AMatcherReadsOnlyItsOwnSeverity()
    {
        string[] lines =
        [
            "a.pc(1,1): error PC0501: no.",
            "a.pc(1,1): warning PC0403: no.",
            "a.pc(1,1): opinion PC0406: no.",
        ];

        Assert.Multiple(() =>
        {
            foreach (JsonElement matcher in Contributes().GetProperty("problemMatchers").EnumerateArray())
            {
                string regex = matcher.GetProperty("pattern").GetProperty("regexp").GetString()!;

                Assert.That(
                    lines.Count(line => Regex.IsMatch(line, regex)),
                    Is.EqualTo(1),
                    $"{matcher.GetProperty("name").GetString()} should read one severity only");
            }
        });
    }

    /// <summary>
    /// <para>A build is a task, and the task type is one the script provides.</para>
    /// <para>A task rather than a process of the extension's own, so that output lands in a
    /// terminal, the matchers get a chance at it, and Ctrl+Shift+B finds a build without anybody
    /// writing a command line.</para>
    /// </summary>
    [Test]
    public void ABuildIsATaskTheScriptProvides()
    {
        string script = File.ReadAllText(ScriptPath);

        string[] types =
        [
            .. Contributes().GetProperty("taskDefinitions")
                            .EnumerateArray()
                            .Select(t => t.GetProperty("type").GetString()!),
        ];

        Assert.Multiple(() =>
        {
            Assert.That(types, Does.Contain(Declared("DebuggerType")));
            Assert.That(script, Does.Contain("registerTaskProvider"));
            Assert.That(script, Does.Contain("TaskGroup.Build"), "so Ctrl+Shift+B finds it");

            Assert.That(
                script,
                Does.Contain("$profi-c-error"),
                "the task has to name the matchers, or nothing reads its output");
        });
    }

    /// <summary>
    /// <para>The platforms offered are the ones the compiler says are installed.</para>
    /// <para>Asked rather than listed, because which are available depends on the machine —
    /// a menu written in the manifest cannot know, and offering one that cannot be built for
    /// would undo the refusal that exists to catch exactly that.</para>
    /// </summary>
    [Test]
    public void TheTargetIsChosenFromWhatTheCompilerReports()
    {
        string script = File.ReadAllText(ScriptPath);

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Contain("'platforms'"),
                        "the list comes from the compiler, not from a copy kept here");

            Assert.That(script, Does.Contain("showQuickPick"));

            Assert.That(
                Contributes().GetProperty("configuration").GetProperty("properties")
                             .EnumerateObject().Select(p => p.Name),
                Does.Contain(Declared("TargetSetting")),
                "and the choice is kept in a setting the manifest declares");
        });
    }

    /// <summary>
    /// <para>A compiler too old to debug with is reported rather than tried.</para>
    /// <para>The failure this exists for gives a reader nothing at all. An older <c>pc</c> meets
    /// <c>debug</c>, prints "unknown command" to its standard output, and exits zero — so the
    /// editor sees a process that started correctly and then finished, and pressing Run does
    /// nothing, says nothing, and writes nothing to a log. It cost an evening once.</para>
    /// <para>Checked in the script rather than by running it, since the behavior needs a
    /// compiler of the wrong vintage to demonstrate. What is held here is that the check exists
    /// and that its answer reaches the reader.</para>
    /// </summary>
    [Test]
    public void ACompilerThatCannotDebugIsReported()
    {
        string script = File.ReadAllText(ScriptPath);

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Contain("whyItCannotDebug"));

            Assert.That(
                script,
                Does.Contain("showErrorMessage"),
                "the answer has to reach the reader, or it is the same silence as before");

            Assert.That(
                script,
                Does.Contain("too old to debug"),
                "and say which of the two went wrong");
        });
    }

    /// <summary>
    /// <para>Breadcrumbs and the Outline come from the compiler, not from a parser kept here.
    /// </para>
    /// <para>A symbol provider is all the editor needs — no language server — but where the
    /// symbols come from is the decision that matters. Reading the file in JavaScript would put
    /// a second definition of Profi-C in this repository, and the two would agree until a
    /// construct was added to one of them: an outline that silently stops listing something,
    /// with nothing failing anywhere.</para>
    /// </summary>
    [Test]
    public void TheOutlineComesFromTheCompiler()
    {
        string script = File.ReadAllText(ScriptPath);

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Contain("registerDocumentSymbolProvider"));

            Assert.That(
                script,
                Does.Contain("'outline'"),
                "the tree is asked for, not worked out here");

            Assert.That(
                script,
                Does.Contain("entry.line - 1"),
                "and converted from the compiler's one-based positions at this boundary");
        });
    }

    /// <summary>
    /// <para>Every kind the compiler can report has an icon.</para>
    /// <para>The two lists are written in different repositories and nothing but this compares
    /// them. A kind the editor does not know falls to the default, so a structure would quietly
    /// show as a method — right shape, wrong picture, and no error anywhere.</para>
    /// </summary>
    [Test]
    public void EveryKindTheCompilerReportsHasAnIcon()
    {
        string script = File.ReadAllText(ScriptPath);

        string[] kinds =
        [
            "namespace", "model", "structure", "enumeration",
            "enumMember", "constructor", "field",
        ];

        Assert.Multiple(() =>
        {
            foreach (string kind in kinds)
            {
                Assert.That(
                    script,
                    Does.Contain($"case '{kind}':"),
                    $"'{kind}' is a kind the compiler writes and this does not draw");
            }
        });
    }

    /// <summary>
    /// <para>The colors travel with the extension.</para>
    /// <para>A palette in a workspace's settings colors one folder, which means copying a file
    /// into every project — the thing this command exists to replace. It writes to the reader's
    /// own settings because VS Code will not let an extension impose token colors: those offered
    /// through <c>configurationDefaults</c> are accepted into the manifest and then ignored.
    /// </para>
    /// </summary>
    [Test]
    public void ThePaletteShipsWithTheExtension()
    {
        string palette = Path.Combine(Extension, "palette.js");
        string script = File.ReadAllText(ScriptPath);

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(palette), Is.True, palette);

            Assert.That(script, Does.Contain("require('./palette')"));

            Assert.That(
                script,
                Does.Contain("ConfigurationTarget.Global"),
                "written to the reader's own settings, or it colors one folder again");
        });
    }

    /// <summary>
    /// <para>The colors the compiler decides are installed too, and can actually take effect.
    /// </para>
    /// <para>Three things have to be true together, and getting two of them right looks exactly
    /// like getting none of them right — the file simply goes on looking as it did. The rules
    /// have to be written to the semantic setting rather than the grammar's. They have to name
    /// this language, or a rule saying what a local looks like repaints every other language the
    /// reader has open. And semantic highlighting has to be turned on: it ships as
    /// <c>configuredByTheme</c>, so a theme that does not ask for it discards the whole
    /// thing.</para>
    /// </summary>
    [Test]
    public void TheColorsTheCompilerDecidesAreInstalledAndTurnedOn()
    {
        string palette = File.ReadAllText(Path.Combine(Extension, "palette.js"));
        string script = File.ReadAllText(ScriptPath);

        Assert.Multiple(() =>
        {
            Assert.That(palette, Does.Contain("semanticRules"));

            Assert.That(
                script,
                Does.Contain("editor.semanticTokenColorCustomizations"),
                "the setting the grammar's colors do not live in");

            Assert.That(
                script,
                Does.Contain(":profi-c"),
                "scoped to this language, or it repaints every other one");

            Assert.That(
                script,
                Does.Contain("editor.semanticHighlighting.enabled"),
                "or a theme that does not ask for it discards all of the above");
        });
    }

    /// <summary>
    /// <para>The version was raised alongside what the manifest contributes.</para>
    /// <para>VS Code reads <c>contributes</c> once, at the scan, and records it with the version
    /// beside it. A manifest edited without a bump goes on being served as whatever was recorded
    /// — silently, with the file on disk plainly saying otherwise — which is a whole evening
    /// lost to a debugger that is right in the repository and absent in the editor.</para>
    /// <para>This cannot check that the bump happened; nothing here knows what was there before.
    /// What it can check is that the number is a real one, and that the install instructions do
    /// not repeat it.</para>
    /// <para><b>The instructions used to name a versioned folder</b>, on the assumption that VS
    /// Code needed one — the Marketplace lays extensions out as <c>publisher.name-version</c>,
    /// and it is an easy thing to believe. It does not: the version is read from the manifest,
    /// and a plain folder works. The old advice cost a re-link on every manifest change, and
    /// left a version written in two places to drift. A version that appears nowhere in the
    /// instructions cannot go stale in them.</para>
    /// </summary>
    [Test]
    public void TheVersionIsRealAndTheInstructionsDoNotRepeatIt()
    {
        string version = Manifest().RootElement.GetProperty("version").GetString()!;
        string readme = File.ReadAllText(Path.Combine(Extension, "README.md"));

        Assert.Multiple(() =>
        {
            Assert.That(Version.TryParse(version, out _), Is.True, version);

            Assert.That(
                Regex.Matches(readme, @"profi-c-(\d+\.\d+\.\d+)").Select(f => f.Groups[1].Value),
                Is.Empty,
                "the folder to install into carries no version, so nothing there can go stale");
        });
    }
}
