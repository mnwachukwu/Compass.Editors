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
    /// <para>The version was raised alongside what the manifest contributes.</para>
    /// <para>VS Code reads <c>contributes</c> once, at the scan, and records it with the version
    /// beside it. A manifest edited without a bump goes on being served as whatever was recorded
    /// — silently, with the file on disk plainly saying otherwise — which is a whole evening
    /// lost to a debugger that is right in the repository and absent in the editor.</para>
    /// <para>This cannot check that the bump happened; nothing here knows what was there before.
    /// What it can check is that the number is a real one and that the install instructions name
    /// the same folder, since those go stale together.</para>
    /// </summary>
    [Test]
    public void TheVersionIsRealAndTheInstructionsNameIt()
    {
        string version = Manifest().RootElement.GetProperty("version").GetString()!;
        string readme = File.ReadAllText(Path.Combine(Extension, "README.md"));

        Assert.Multiple(() =>
        {
            Assert.That(Version.TryParse(version, out _), Is.True, version);

            Assert.That(readme, Does.Contain($"profi-c-{version}"),
                        "the folder the README installs into carries the version");

            Assert.That(
                Regex.Matches(readme, @"profi-c-(\d+\.\d+\.\d+)")
                     .Select(found => found.Groups[1].Value)
                     .Distinct(),
                Is.EqualTo(new[] { version }),
                "and names no other, or one of them is wrong");
        });
    }
}
