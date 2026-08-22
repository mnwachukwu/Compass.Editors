using System.Diagnostics;
using System.Text.Json;

namespace Compass.Editors.Tests;

/// <summary>
/// <para>What the Run button does with a program that will not compile.</para>
/// <para><b>The Problems panel, not a dialog.</b> A refusal used to arrive as a failed launch,
/// which an editor has exactly one way of showing: a modal listing as many errors as fit. That
/// is the wrong shape for the thing it describes — a list of positions in a file is something to
/// click through, and a dialog is something to dismiss before you can look at the code it is
/// talking about. Every other language in the editor puts them in the panel, and so does a
/// Compass <i>build</i>; only running was different.</para>
/// <para><b>Warnings do not stop a run.</b> The compiler runs a program that has them, so an
/// editor refusing to would be answering a different question than the compiler does. They are
/// shown all the same, since a reader who asked to run something is the reader most likely to
/// want to see them.</para>
/// <para>Driven through the extension's own code rather than a copy of the rules, so what is
/// asserted is what the button does. The editor is stubbed; nothing here needs VS Code.</para>
/// </summary>
[TestFixture]
public sealed class RunRefusalTests : EditorTestBase
{
    /// <summary>One entry as it would appear in the panel.</summary>
    private sealed record Problem(
        string File,
        int Line,
        int Column,
        string Severity,
        string Code,
        string Message);

    /// <summary>What the extension decided about one program.</summary>
    private sealed record Answer(bool MayRun, bool Asked, Problem[] Problems);

    /// <summary>
    /// Asks the extension about some programs, or skips where node or a built compiler is
    /// missing — the same way the other tests skip, and for the same reason.
    /// </summary>
    private static Answer[] Ask(params string[] files)
    {
        string compiler = CompilerOrIgnore();

        ProcessStartInfo start = new()
        {
            FileName = "node",
            WorkingDirectory = Extension,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        start.ArgumentList.Add(Path.Combine("tools", "checking.js"));
        start.ArgumentList.Add(compiler);

        Process node;

        try
        {
            node = Process.Start(start)!;
        }
        catch (Exception unavailable)
        {
            Assert.Ignore($"node is needed to ask the extension: {unavailable.Message}");
            throw;
        }

        using (node)
        {
            node.StandardInput.Write(JsonSerializer.Serialize(files));
            node.StandardInput.Close();

            string answered = node.StandardOutput.ReadToEnd();
            string failed = node.StandardError.ReadToEnd();

            node.WaitForExit();

            Assert.That(node.ExitCode, Is.Zero, failed);

            Answer[] answers = JsonSerializer.Deserialize<Answer[]>(
                answered,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

            Assert.That(
                answers.All(answer => answer.Asked),
                Is.True,
                $"'{compiler} check' answered nothing — republish it");

            return answers;
        }
    }

    /// <summary>
    /// Three programs, one per outcome. Written here rather than taken from Compass's corpus:
    /// the samples that fail are meant to be read, and what is wanted here is the smallest thing
    /// that fails in one known way.
    /// </summary>
    private sealed class Workspace : IDisposable
    {
        public Workspace()
        {
            Folder = Path.Combine(Path.GetTempPath(), $"compass-refusal-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Folder);

            Write(
                "Broken.cm",
                """
                shared model Program
                    function Main()
                        integer counted = 9223372036854775808;
                        Console.WriteLine(counted);
                    end function
                end model
                """);

            // Compiles, and the compiler has an opinion about it: the parentheses on a value.
            Write(
                "Warned.cm",
                """
                shared model Program
                    function Main()
                        string name = "Ada";
                        Console.WriteLine(name.Count);
                        Console.WriteLine("");
                    end function
                end model
                """);

            Write(
                "Fine.cm",
                """
                shared model Program
                    function Main()
                        Console.WriteLine("hello");
                    end function
                end model
                """);
        }

        public string Folder { get; }

        public string At(string name) => Path.Combine(Folder, name);

        private void Write(string name, string body) =>
            File.WriteAllText(Path.Combine(Folder, name), body);

        public void Dispose() => Directory.Delete(Folder, recursive: true);
    }

    [Test]
    public void AProgramThatWillNotCompileIsRefused()
    {
        using Workspace workspace = new();

        Answer answer = Ask(workspace.At("Broken.cm"))[0];

        Assert.Multiple(() =>
        {
            Assert.That(answer.MayRun, Is.False, "a program with an error was allowed to run");

            Assert.That(answer.Problems, Has.Length.EqualTo(1));
            Assert.That(answer.Problems[0].Code, Is.EqualTo("CM0026"));
            Assert.That(answer.Problems[0].Severity, Is.EqualTo("error"));
            Assert.That(answer.Problems[0].File, Is.EqualTo("Broken.cm"));

            // Zero-based, where the compiler counts from one. Line 3, column 27 as a reader
            // sees it, which is where the number begins.
            Assert.That(answer.Problems[0].Line, Is.EqualTo(2));
            Assert.That(answer.Problems[0].Column, Is.EqualTo(26));
        });
    }

    [Test]
    public void AProgramWithOnlySomethingToSayStillRuns()
    {
        using Workspace workspace = new();

        Answer answer = Ask(workspace.At("Warned.cm"))[0];

        Assert.Multiple(() =>
        {
            Assert.That(answer.MayRun, Is.True, "something short of an error stopped a run");
            Assert.That(answer.Problems, Is.Not.Empty, "nothing was shown to the reader");

            Assert.That(
                answer.Problems.Select(problem => problem.Severity),
                Has.None.EqualTo("error"));
        });
    }

    [Test]
    public void AProgramThatCompilesLeavesThePanelEmpty()
    {
        using Workspace workspace = new();

        Answer answer = Ask(workspace.At("Fine.cm"))[0];

        Assert.Multiple(() =>
        {
            Assert.That(answer.MayRun, Is.True);
            Assert.That(answer.Problems, Is.Empty);
        });
    }
}
