using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ProfiC.Editors.Tests;

/// <summary>
/// <para>Holds the problem matchers to what the compiler actually prints.</para>
/// <para><b>This is the seam between the two repositories that nothing negotiates.</b> The
/// language server and the debugger each speak a protocol built to be asked what the other end
/// supports, so a mismatch there degrades. A build task is different: the extension hands VS Code
/// three regular expressions, VS Code runs <c>pc</c> and matches them against its output, and a
/// line that matches nothing is simply not a problem. Change the shape of that line in the other
/// repository and the Problems panel stops filling — no error, no warning, nothing in a log, and
/// both repositories' own tests still green.</para>
/// <para><b>So the lines come from the compiler rather than from here.</b> The matcher tests
/// beside this one check that the three expressions read one severity each, which is a fact about
/// the expressions and needs nothing to run it. What they cannot check is whether the thing they
/// are disjoint about resembles a compiler's output at all: their sample lines were typed by
/// somebody reading the format, and would go on passing for as long as they were left
/// alone.</para>
/// <para>Skipped where no built compiler sits beside this, like every other test that needs one.
/// CI publishes it and fails on a skip.</para>
/// </summary>
[TestFixture]
public sealed class DiagnosticFormatTests : EditorTestBase
{
    /// <summary>
    /// <para>A program written to be wrong in three different ways at once.</para>
    /// <para>One of each severity, because a matcher is contributed for each and the failure
    /// worth catching is one of them going unread. What makes each one happen is the language's
    /// business and may well change; that it is still an error, a warning and an opinion is what
    /// this depends on, and the assertion below says so when it stops being true.</para>
    /// </summary>
    private const string WrongThreeWays = """
        shared model Program

            function Main()
                integer wrong = "not a number";
                integer unread = 3;
                Console.WriteLine("");
            end function

        end model
        """;

    /// <summary>Every line the compiler wrote that looks like it is about a place in a file.</summary>
    private static string[] WhatTheCompilerPrinted()
    {
        string compiler = CompilerOrIgnore();
        string folder = Directory.CreateTempSubdirectory("profi-c-diagnostics-").FullName;

        try
        {
            string program = Path.Combine(folder, "wrong.pc");
            File.WriteAllText(program, WrongThreeWays);

            ProcessStartInfo start = new()
            {
                FileName = compiler,
                WorkingDirectory = folder,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };

            start.ArgumentList.Add("build");
            start.ArgumentList.Add(program);

            using Process built = Process.Start(start)!;

            string said = built.StandardOutput.ReadToEnd() + built.StandardError.ReadToEnd();
            built.WaitForExit(60000);

            // Everything mentioning a line and column. Which lines those are is the compiler's
            // business; what matters is that each one is read by exactly one matcher.
            string[] positioned = [.. said
                .Split('\n')
                .Select(line => line.TrimEnd('\r'))
                .Where(line => Regex.IsMatch(line, @"\(\d+,\d+\):"))];

            Assert.That(
                positioned,
                Is.Not.Empty,
                $"the compiler said nothing about a position in a file, so nothing here was "
                + $"checked. It said: {said}");

            return positioned;
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>The matchers the extension contributes, by the severity each is for.</summary>
    private static Dictionary<string, string> Matchers()
    {
        using JsonDocument manifest =
            JsonDocument.Parse(File.ReadAllText(Path.Combine(Extension, "package.json")));

        return manifest.RootElement
            .GetProperty("contributes")
            .GetProperty("problemMatchers")
            .EnumerateArray()
            .ToDictionary(
                one => one.GetProperty("name").GetString()!,
                one => one.GetProperty("pattern").GetProperty("regexp").GetString()!);
    }

    /// <summary>
    /// Every line the compiler printed is read by exactly one matcher.
    ///
    /// Both halves matter. A line no matcher reads is a problem the panel never shows; a line two
    /// matchers read is one problem shown twice, at two severities.
    /// </summary>
    [Test]
    public void EveryLineTheCompilerPrintsIsReadByExactlyOneMatcher()
    {
        Dictionary<string, string> matchers = Matchers();

        Assert.Multiple(() =>
        {
            foreach (string line in WhatTheCompilerPrinted())
            {
                string[] read = [.. matchers
                    .Where(matcher => Regex.IsMatch(line, matcher.Value))
                    .Select(matcher => matcher.Key)];

                Assert.That(
                    read,
                    Has.Length.EqualTo(1),
                    read.Length == 0
                        ? $"no matcher reads this, so it never reaches the Problems panel:\n  {line}"
                        : $"{string.Join(" and ", read)} both read this, so it is shown twice:\n  {line}");
            }
        });
    }

    /// <summary>
    /// <para>All three severities are still among what a wrong program produces.</para>
    /// <para>Without this the test above passes on a program that only produces errors, having
    /// proved nothing about two of the three matchers — which is the shape of coverage that
    /// reads as thorough and is not.</para>
    /// </summary>
    [Test]
    public void AWrongProgramStillProducesAllThreeSeverities()
    {
        Dictionary<string, string> matchers = Matchers();
        string[] printed = WhatTheCompilerPrinted();

        Assert.Multiple(() =>
        {
            foreach ((string name, string pattern) in matchers)
            {
                Assert.That(
                    printed.Any(line => Regex.IsMatch(line, pattern)),
                    Is.True,
                    $"nothing {name} could read came out of a program written to be wrong three "
                    + "ways, so that matcher was not tested. Either the compiler stopped saying "
                    + "this about that program - update the program - or it stopped saying it at "
                    + "all.");
            }
        });
    }

    /// <summary>
    /// <para>What the matcher pulls out of a line is what was in it.</para>
    /// <para>A pattern can match a line and still take the wrong pieces from it, and the symptom
    /// is a problem attached to the wrong place: an off-by-one in the column, a code captured
    /// with its severity attached, a message beginning halfway through itself. VS Code shows all
    /// of that without complaint, because it has nothing to compare against.</para>
    /// <para>The manifest says which capture is which, so those numbers are read rather than
    /// assumed — a pattern rewritten with its groups in a new order and the numbers left alone
    /// would otherwise pass here and be wrong in the panel.</para>
    /// </summary>
    [Test]
    public void EachMatcherTakesTheRightPiecesOutOfTheLine()
    {
        using JsonDocument manifest =
            JsonDocument.Parse(File.ReadAllText(Path.Combine(Extension, "package.json")));

        JsonElement[] contributed = [.. manifest.RootElement
            .GetProperty("contributes")
            .GetProperty("problemMatchers")
            .EnumerateArray()];

        string[] printed = WhatTheCompilerPrinted();

        Assert.Multiple(() =>
        {
            foreach (JsonElement matcher in contributed)
            {
                JsonElement pattern = matcher.GetProperty("pattern");
                string expression = pattern.GetProperty("regexp").GetString()!;

                foreach (string line in printed.Where(one => Regex.IsMatch(one, expression)))
                {
                    Match read = Regex.Match(line, expression);
                    string named = matcher.GetProperty("name").GetString()!;

                    string file = read.Groups[pattern.GetProperty("file").GetInt32()].Value;
                    string column = read.Groups[pattern.GetProperty("column").GetInt32()].Value;
                    string code = read.Groups[pattern.GetProperty("code").GetInt32()].Value;
                    string message = read.Groups[pattern.GetProperty("message").GetInt32()].Value;

                    Assert.That(
                        file,
                        Does.EndWith("wrong.pc"),
                        $"{named} read '{file}' as the file out of:\n  {line}");

                    Assert.That(
                        read.Groups[pattern.GetProperty("line").GetInt32()].Value,
                        Does.Match(@"^\d+$"),
                        $"{named} read a line number that is not a number out of:\n  {line}");

                    Assert.That(
                        column,
                        Does.Match(@"^\d+$"),
                        $"{named} read a column that is not a number out of:\n  {line}");

                    // The identifier alone. A code that arrived with its severity still attached
                    // is what a suppression written against it would then fail to match.
                    Assert.That(
                        code,
                        Does.Match(@"^PC\d{4}$"),
                        $"{named} read '{code}' as the diagnostic code out of:\n  {line}");

                    Assert.That(
                        message,
                        Is.Not.Empty.And.Not.StartsWith(":"),
                        $"{named} read '{message}' as the message out of:\n  {line}");

                    // The whole of it. A message cut at the first colon - and several of them
                    // contain one - is a sentence the panel shows half of.
                    Assert.That(
                        line,
                        Does.EndWith(message),
                        $"{named} stopped reading the message early out of:\n  {line}");
                }
            }
        });
    }
}
