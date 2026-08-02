using System.Diagnostics;
using System.Text.Json;

namespace ProfiC.Editors.Tests;

/// <summary>
/// <para>Which project the "Run project associated with this file" button actually runs.</para>
/// <para><b>Membership, not proximity.</b> A <c>.pcp</c> sitting above a file lists what it
/// builds, and a file it does not list is no more part of it than one in another folder.
/// Running the nearest project regardless would compile a program the reader is not looking at,
/// print its output, and look exactly like the button working — which is the worst kind of wrong
/// for a button somebody presses to check what they just wrote.</para>
/// <para>Driven through the extension's own code rather than a copy of the rules, so that what
/// is asserted is what the button does. The editor is stubbed; nothing here needs VS Code.</para>
/// </summary>
[TestFixture]
public sealed class ProjectMembershipTests : EditorTestBase
{
    /// <summary>What the extension decided for one file.</summary>
    private sealed record Answer(string? Project, int Searched, string Runs, string? Said);

    /// <summary>
    /// Asks the extension about some files, or skips where node is not installed — the same way
    /// tokenizing skips, and for the same reason.
    /// </summary>
    private static Answer[] Ask(params string[] files)
    {
        ProcessStartInfo start = new()
        {
            FileName = "node",
            WorkingDirectory = Extension,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        start.ArgumentList.Add(Path.Combine("tools", "project.js"));

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

            return JsonSerializer.Deserialize<Answer[]>(
                answered,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        }
    }

    /// <summary>
    /// A folder holding a project that claims one file and not another, removed however the
    /// test ends. Built here rather than taken from Profi-C, since the case worth testing is one
    /// the sample corpus has no reason to contain.
    /// </summary>
    private sealed class Workspace : IDisposable
    {
        public Workspace()
        {
            Folder = Path.Combine(Path.GetTempPath(), $"profi-c-project-{Guid.NewGuid():N}");

            Directory.CreateDirectory(Path.Combine(Folder, "app"));
            Directory.CreateDirectory(Path.Combine(Folder, "elsewhere"));

            File.WriteAllText(
                Path.Combine(Folder, "app", "app.pcp"),
                """
                project App
                    source Program.pc
                end project
                """);

            foreach (string file in new[] { "app/Program.pc", "app/Loose.pc", "elsewhere/Idea.pc" })
            {
                File.WriteAllText(Path.Combine(Folder, file.Replace('/', Path.DirectorySeparatorChar)), Program);
            }
        }

        private const string Program = """
            shared model Program
                function Main()
                    Console.WriteLine("hello");
                end function
            end model
            """;

        public string Folder { get; }

        public string At(string relative) =>
            Path.Combine(Folder, relative.Replace('/', Path.DirectorySeparatorChar));

        public void Dispose() => Directory.Delete(Folder, recursive: true);
    }

    /// <summary>A file a project lists is run as that project.</summary>
    [Test]
    public void AFileAProjectListsRunsAsTheProject()
    {
        using Workspace workspace = new();

        Answer answer = Ask(workspace.At("app/Program.pc"))[0];

        Assert.Multiple(() =>
        {
            Assert.That(answer.Project, Is.EqualTo("app.pcp"));
            Assert.That(answer.Runs, Is.EqualTo("app.pcp"));
            Assert.That(answer.Said, Is.Null, "nothing to report when it found what it wanted");
        });
    }

    /// <summary>
    /// <para>A file beside a project that does not list it runs on its own, and says so.</para>
    /// <para>The case the whole check exists for. <c>Loose.pc</c> sits in the same folder as
    /// <c>app.pcp</c>, which lists only <c>Program.pc</c> — so the project is the nearest one by
    /// every measure of distance and still the wrong answer.</para>
    /// </summary>
    [Test]
    public void AFileAProjectDoesNotListRunsOnItsOwn()
    {
        using Workspace workspace = new();

        Answer answer = Ask(workspace.At("app/Loose.pc"))[0];

        Assert.Multiple(() =>
        {
            Assert.That(answer.Project, Is.Null);
            Assert.That(answer.Runs, Is.EqualTo("Loose.pc"));

            Assert.That(answer.Searched, Is.GreaterThan(0),
                        "a project was there to be rejected, which is what makes this the case");

            Assert.That(answer.Said, Does.Contain("no project lists this file"));
        });
    }

    /// <summary>
    /// <para>A file with no project above it at all runs on its own, and is told a different
    /// thing.</para>
    /// <para>Different because they are different situations: one reader has no project, the
    /// other has one that does not want their file, and the second is the one who needs to go
    /// and look at something.</para>
    /// </summary>
    [Test]
    public void AFileWithNoProjectAboveItIsToldSoDifferently()
    {
        using Workspace workspace = new();

        Answer answer = Ask(workspace.At("elsewhere/Idea.pc"))[0];

        Assert.Multiple(() =>
        {
            Assert.That(answer.Project, Is.Null);
            Assert.That(answer.Runs, Is.EqualTo("Idea.pc"));
            Assert.That(answer.Searched, Is.Zero, "there was no project to reject");
            Assert.That(answer.Said, Does.Contain("no project found"));
        });
    }

    /// <summary>
    /// <para>The real sample projects are read the way the compiler reads them.</para>
    /// <para>Three shapes at once, and none of them a bare list of files: a source named
    /// outright, a source naming a folder, and a file reached only through another project a
    /// <c>reference</c> pulls in. Skipped where Profi-C is not beside this, the same as the
    /// vocabulary.</para>
    /// </summary>
    [TestCase("storefront/Program.pc", "storefront.pcp", "named outright as a source")]
    [TestCase("storefront/models/Product.pc", "storefront.pcp", "covered by a folder source")]
    [TestCase("library/books/Book.pc", "books.pcp", "reached through a reference")]
    [TestCase("fizzbuzz.pc", null, "a program of one file belongs to no project")]
    public void TheSampleProjectsAreReadAsTheCompilerReadsThem(
        string sample,
        string? expected,
        string why)
    {
        string profiC = ProfiCOrIgnore("check out Profi-C beside this repository to read its samples");

        string file = Path.Combine(
            profiC, "samples", sample.Replace('/', Path.DirectorySeparatorChar));

        Assert.That(Ask(file)[0].Project, Is.EqualTo(expected), why);
    }
}
