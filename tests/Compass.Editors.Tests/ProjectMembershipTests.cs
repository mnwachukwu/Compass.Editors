using System.Diagnostics;
using System.Text.Json;

namespace Compass.Editors.Tests;

/// <summary>
/// <para>Which project the "Run project associated with this file" button actually runs.</para>
/// <para><b>Membership, not proximity.</b> A <c>.cmp</c> sitting above a file lists what it
/// builds, and a file it does not list is no more part of it than one in another folder.
/// Running the nearest project regardless would compile a program the reader is not looking at,
/// print its output, and look exactly like the button working — which is the worst kind of wrong
/// for a button somebody presses to check what they just wrote.</para>
/// <para><b>The answer is the compiler's.</b> The extension runs <c>cm project</c> rather than
/// reading a <c>.cmp</c> itself, since a second reader of that format would agree with the first
/// until it gained a word — and disagree silently, in the direction that runs a program nobody
/// was looking at. What these tests hold is that the extension asks, and does the right thing
/// with each answer.</para>
/// <para>Driven through the extension's own code rather than a copy of the rules, so that what
/// is asserted is what the button does. The editor is stubbed; nothing here needs VS Code.</para>
/// </summary>
[TestFixture]
public sealed class ProjectMembershipTests : EditorTestBase
{
    /// <summary>What the extension decided for one file.</summary>
    private sealed record Answer(
        string? Project,
        int Searched,
        bool Asked,
        string Runs,
        string? Said);

    /// <summary>
    /// Asks the extension about some files, or skips where node or a built compiler is missing —
    /// the same way tokenizing skips, and for the same reason.
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

        start.ArgumentList.Add(Path.Combine("tools", "project.js"));
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

            // Checked here rather than left to each test, because a compiler that cannot answer
            // makes every one of them fail as though the extension had decided something. It has
            // not: it asked and got nothing. A published build older than the command is the
            // usual cause, and it is the failure worth naming, having cost an evening once.
            Assert.That(
                answers.All(answer => answer.Asked),
                Is.True,
                $"'{compiler} project' answered nothing — republish it");

            return answers;
        }
    }

    /// <summary>
    /// A folder holding a project that claims one file and not another, removed however the
    /// test ends. Built here rather than taken from Compass, since the case worth testing is one
    /// the sample corpus has no reason to contain.
    /// </summary>
    private sealed class Workspace : IDisposable
    {
        public Workspace()
        {
            Folder = Path.Combine(Path.GetTempPath(), $"compass-project-{Guid.NewGuid():N}");

            Directory.CreateDirectory(Path.Combine(Folder, "app"));
            Directory.CreateDirectory(Path.Combine(Folder, "elsewhere"));

            // Two of these lines say "source" and neither is one: the project reader takes both
            // comment forms, and stops at 'end project'. They are here because anything scanning
            // lines for the word reads them as claims.
            File.WriteAllText(
                Path.Combine(Folder, "app", "app.cmp"),
                """
                project App
                    source Program.cm
                    ##
                        Left out for now.
                        source Draft.cm
                    ##
                end project
                source Stray.cm
                """);

            string[] files =
            [
                "app/Program.cm", "app/Loose.cm", "app/Draft.cm", "app/Stray.cm",
                "elsewhere/Idea.cm",
            ];

            foreach (string file in files)
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

        Answer answer = Ask(workspace.At("app/Program.cm"))[0];

        Assert.Multiple(() =>
        {
            Assert.That(answer.Project, Is.EqualTo("app.cmp"));
            Assert.That(answer.Runs, Is.EqualTo("app.cmp"));
            Assert.That(answer.Said, Is.Null, "nothing to report when it found what it wanted");
        });
    }

    /// <summary>
    /// <para>A file beside a project that does not list it runs on its own, and says so.</para>
    /// <para>The case the whole check exists for. <c>Loose.cm</c> sits in the same folder as
    /// <c>app.cmp</c>, which lists only <c>Program.cm</c> — so the project is the nearest one by
    /// every measure of distance and still the wrong answer.</para>
    /// </summary>
    [Test]
    public void AFileAProjectDoesNotListRunsOnItsOwn()
    {
        using Workspace workspace = new();

        Answer answer = Ask(workspace.At("app/Loose.cm"))[0];

        Assert.Multiple(() =>
        {
            Assert.That(answer.Project, Is.Null);
            Assert.That(answer.Runs, Is.EqualTo("Loose.cm"));

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

        Answer answer = Ask(workspace.At("elsewhere/Idea.cm"))[0];

        Assert.Multiple(() =>
        {
            Assert.That(answer.Project, Is.Null);
            Assert.That(answer.Runs, Is.EqualTo("Idea.cm"));
            Assert.That(answer.Searched, Is.Zero, "there was no project to reject");
            Assert.That(answer.Said, Does.Contain("no project found"));
        });
    }

    /// <summary>
    /// <para>A line the compiler does not read is not a claim, whatever it looks like.</para>
    /// <para>The case that decides whether asking the compiler was worth doing. A <c>source</c>
    /// inside a block comment, and one written after <c>end project</c>, both read as claims to
    /// anything scanning a project file for the word — and neither is one. Being wrong here is
    /// quiet and expensive: the button compiles and runs a program the reader is not looking at,
    /// prints its output, and looks exactly like the button working.</para>
    /// </summary>
    [TestCase("app/Draft.cm", "commented out")]
    [TestCase("app/Stray.cm", "written after the project closed")]
    public void ALineTheCompilerDoesNotReadIsNotAClaim(string file, string why)
    {
        using Workspace workspace = new();

        Answer answer = Ask(workspace.At(file))[0];

        Assert.Multiple(() =>
        {
            Assert.That(answer.Project, Is.Null, why);
            Assert.That(answer.Runs, Is.EqualTo(Path.GetFileName(file)));
        });
    }

    /// <summary>
    /// <para>The real sample projects are read the way the compiler reads them.</para>
    /// <para>Three shapes at once, and none of them a bare list of files: a source named
    /// outright, a source naming a folder, and a file reached only through another project a
    /// <c>reference</c> pulls in. Skipped where Compass is not beside this, the same as the
    /// vocabulary.</para>
    /// </summary>
    [TestCase("storefront/Program.cm", "storefront.cmp", "named outright as a source")]
    [TestCase("storefront/models/Product.cm", "storefront.cmp", "covered by a folder source")]
    [TestCase("library/books/Book.cm", "books.cmp", "reached through a reference")]
    [TestCase("fizzbuzz.cm", null, "a program of one file belongs to no project")]
    public void TheSampleProjectsAreReadAsTheCompilerReadsThem(
        string sample,
        string? expected,
        string why)
    {
        string compass = CompassOrIgnore("check out Compass beside this repository to read its samples");

        string file = Path.Combine(
            compass, "samples", sample.Replace('/', Path.DirectorySeparatorChar));

        Assert.That(Ask(file)[0].Project, Is.EqualTo(expected), why);
    }
}
