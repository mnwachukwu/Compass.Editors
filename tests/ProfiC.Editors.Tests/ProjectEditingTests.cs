using System.Diagnostics;
using System.Text.Json;

namespace ProfiC.Editors.Tests;

/// <summary>
/// <para>What the project commands write into a <c>.pcp</c>.</para>
/// <para><b>These are the only place in the extension that writes one of Profi-C's own file
/// formats</b>, and everything else here is careful not to: which project claims a file, what a
/// file declares, whether a program checks — all of those ask the compiler, precisely so no
/// second reader exists to drift from the first.</para>
/// <para>What makes an exception tolerable is that none of this reads a project. Adding a source
/// puts a line in before <c>end project</c>; removing one takes out a line that names what was
/// asked about; setting the entry point replaces a line that opens with the word. So a format
/// that gains a word gains it here for free, and these hold that no more than that is assumed —
/// in particular that a line this does not recognize is left exactly as it was.</para>
/// <para>Driven through the extension's own module rather than a copy of the rules. The editing
/// functions take text and give text back, so nothing here stubs VS Code or writes a file.</para>
/// </summary>
[TestFixture]
public sealed class ProjectEditingTests : EditorTestBase
{
    /// <summary>One edit to ask for, matching what tools/editing.js reads.</summary>
    private sealed record Edit(string Op, string? Text, string? Project, string? File, string? Type);

    private static JsonElement[] Apply(params Edit[] edits)
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

        start.ArgumentList.Add(Path.Combine("tools", "editing.js"));

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
            node.StandardInput.Write(JsonSerializer.Serialize(
                edits, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

            node.StandardInput.Close();

            string answered = node.StandardOutput.ReadToEnd();
            string failed = node.StandardError.ReadToEnd();

            node.WaitForExit();

            Assert.That(node.ExitCode, Is.Zero, failed);

            return JsonSerializer.Deserialize<JsonElement[]>(answered)!;
        }
    }

    private static string? Written(Edit edit)
    {
        JsonElement answer = Apply(edit)[0];

        return answer.ValueKind == JsonValueKind.Null ? null : answer.GetString();
    }

    /// <summary>A project as somebody would have written it, with the paths this machine uses.</summary>
    private static string Project => Path.Combine(Folder, "storefront.pcp");

    private static string Folder => Path.Combine(Path.GetTempPath(), "profi-c-editing", "storefront");

    private static string Inside(params string[] parts) =>
        Path.Combine([Folder, .. parts]);

    private const string Storefront = """
        project Storefront
            reference ../core/core.pcp
            source Program.pc
            source models
        end project
        """;

    // ---- Listing a file ----------------------------------------------------------------------

    /// <summary>
    /// <para>A source goes in after the last one, and before <c>end project</c>.</para>
    /// <para>After the last rather than at the top, so the order somebody put them in survives
    /// and a <c>reference</c> stays above what needs it.</para>
    /// </summary>
    [Test]
    public void AddingASourceKeepsTheOrderAndTheIndent()
    {
        string? written = Written(
            new Edit("add", Storefront, Project, Inside("Shelf.pc"), null));

        Assert.That(
            written,
            Is.EqualTo("""
                project Storefront
                    reference ../core/core.pcp
                    source Program.pc
                    source models
                    source Shelf.pc
                end project
                """));
    }

    /// <summary>
    /// <para>A path is written with forward slashes, whatever the platform.</para>
    /// <para>A project lists paths one way, and a file listed with backslashes reads on the
    /// machine that wrote it and nowhere else — which is found by somebody else, later, on a
    /// different computer.</para>
    /// </summary>
    [Test]
    public void APathIsWrittenTheWayAProjectNamesOne()
    {
        string? written = Written(
            new Edit("add", Storefront, Project, Inside("pricing", "Tax.pc"), null));

        Assert.Multiple(() =>
        {
            Assert.That(written, Does.Contain("source pricing/Tax.pc"));
            Assert.That(written, Does.Not.Contain("\\"));
        });
    }

    /// <summary>Nothing is written twice, which is what null says.</summary>
    [Test]
    public void AFileAlreadyListedIsNotListedAgain()
    {
        Assert.That(
            Written(new Edit("add", Storefront, Project, Inside("Program.pc"), null)),
            Is.Null);
    }

    /// <summary>The line naming it goes, and nothing else moves.</summary>
    [Test]
    public void RemovingASourceTakesOutOnlyThatLine()
    {
        string? written = Written(
            new Edit("remove", Storefront, Project, Inside("Program.pc"), null));

        Assert.That(
            written,
            Is.EqualTo("""
                project Storefront
                    reference ../core/core.pcp
                    source models
                end project
                """));
    }

    /// <summary>
    /// <para>A file a listed folder brings in is left alone.</para>
    /// <para>The alternative is rewriting the folder line, which changes what the project builds
    /// far beyond the one file that was asked about — and would do it silently.</para>
    /// </summary>
    [Test]
    public void AFileBroughtInByAFolderIsNotRemoved()
    {
        Assert.That(
            Written(new Edit("remove", Storefront, Project, Inside("models", "Book.pc"), null)),
            Is.Null);
    }

    /// <summary>
    /// <para>A line put into a file ends the way that file's other lines end.</para>
    /// <para><b>Found by CI on Windows and not here</b>, because the expected text in a test is
    /// whatever the checkout wrote — so the same assertion reads LF on one machine and CRLF on
    /// another, and only the second one is wrong. The fault was real either way: splitting on
    /// <c>\n</c> leaves the <c>\r</c> of a CRLF file on every line, which is what keeps the
    /// untouched ones untouched, and a line inserted without one is then the only LF line in the
    /// file — mixed endings written into somebody's project by a command they ran to add a file,
    /// and shown as a whole-file change by whatever they review it with.</para>
    /// <para>Written with the endings spelled out rather than taken from this file, so it asserts
    /// the same thing wherever it runs.</para>
    /// </summary>
    [Test]
    public void ALineEndsTheWayTheFileAlreadyDoes()
    {
        const string Windows = "project Storefront\r\n    source Program.pc\r\nend project\r\n";
        const string Unix = "project Storefront\n    source Program.pc\nend project\n";

        Assert.Multiple(() =>
        {
            Assert.That(
                Written(new Edit("add", Windows, Project, Inside("Shelf.pc"), null)),
                Is.EqualTo(
                    "project Storefront\r\n    source Program.pc\r\n    source Shelf.pc\r\n"
                    + "end project\r\n"));

            Assert.That(
                Written(new Edit("add", Unix, Project, Inside("Shelf.pc"), null)),
                Is.EqualTo(
                    "project Storefront\n    source Program.pc\n    source Shelf.pc\nend project\n"));

            // Whole, not "contains": every CRLF holds an LF, so asking for the absence of one
            // finds it in the very ending that is correct.
            Assert.That(
                Written(new Edit("entry", Windows, Project, null, "Shop.Program")),
                Is.EqualTo(
                    "project Storefront\r\n    entry Shop.Program\r\n    source Program.pc\r\n"
                    + "end project\r\n"));
        });
    }

    // ---- Where a project starts ---------------------------------------------------------------

    /// <summary>An entry point that is already written is replaced where it stands.</summary>
    [Test]
    public void AnEntryPointAlreadyWrittenIsReplacedInPlace()
    {
        const string Started = """
            project Tools
                entry Tools.Program
                source Tools.pc
                source App.pc
            end project
            """;

        Assert.That(
            Written(new Edit("entry", Started, Project, null, "App.Program")),
            Is.EqualTo("""
                project Tools
                    entry App.Program
                    source Tools.pc
                    source App.pc
                end project
                """));
    }

    /// <summary>And one written for the first time goes above the sources, where it reads as a heading.</summary>
    [Test]
    public void AnEntryPointGoesAboveTheSources()
    {
        Assert.That(
            Written(new Edit("entry", Storefront, Project, null, "Shop.Program")),
            Is.EqualTo("""
                project Storefront
                    reference ../core/core.pcp
                    entry Shop.Program
                    source Program.pc
                    source models
                end project
                """));
    }

    // ---- Where a project's build goes -----------------------------------------------------------

    /// <summary>
    /// <para>An output goes at the foot, below the sources.</para>
    /// <para>The opposite end from <c>entry</c>, and the order the samples are written in: what a
    /// build is made of comes first and where it lands comes last.</para>
    /// </summary>
    [Test]
    public void AnOutputGoesBelowTheSources()
    {
        Assert.That(
            Written(new Edit("output", Storefront, Project, null, "../artifacts/storefront")),
            Is.EqualTo("""
                project Storefront
                    reference ../core/core.pcp
                    source Program.pc
                    source models
                    output ../artifacts/storefront
                end project
                """));
    }

    /// <summary>
    /// One already written is replaced where it stands rather than joined by a second. A project
    /// is written to one place, so two <c>output</c> lines is `PC0629` — an edit that added one
    /// would break the file it was asked to change.
    /// </summary>
    [Test]
    public void AnOutputAlreadyWrittenIsReplacedInPlace()
    {
        const string Somewhere = """
            project Storefront
                source Program.pc
                output bin
                source models
            end project
            """;

        Assert.That(
            Written(new Edit("output", Somewhere, Project, null, "../artifacts")),
            Is.EqualTo("""
                project Storefront
                    source Program.pc
                    output ../artifacts
                    source models
                end project
                """));
    }

    /// <summary>
    /// A project with no <c>end project</c> is one this cannot write into, and says so by
    /// answering nothing rather than by guessing where the foot of it is.
    /// </summary>
    [Test]
    public void AnOutputNeedsSomewhereToGo() => Assert.That(
        Written(new Edit("output", "project Storefront\n    source Program.pc", Project, null, "bin")),
        Is.Null);

    // ---- What is left alone --------------------------------------------------------------------

    /// <summary>
    /// <para>A word this does not know is not a word it touches.</para>
    /// <para>The whole basis for editing a format the compiler owns: nothing is rewritten
    /// wholesale, so a project carrying something added after this was written keeps it.</para>
    /// </summary>
    [Test]
    public void AWordItDoesNotKnowSurvivesAnEdit()
    {
        const string Later = """
            project Storefront
                ignore opinion
                somethingAddedLater whatever it takes
                source Program.pc
            end project
            """;

        string? written = Written(new Edit("add", Later, Project, Inside("Shelf.pc"), null));

        Assert.Multiple(() =>
        {
            Assert.That(written, Does.Contain("ignore opinion"));
            Assert.That(written, Does.Contain("somethingAddedLater whatever it takes"));
            Assert.That(written, Does.Contain("source Shelf.pc"));
        });
    }

    /// <summary>
    /// <para>A project may only list what sits under it, so nothing else is offered one.</para>
    /// <para>A <c>.pcp</c> names what it builds by a path relative to itself. A file above it
    /// would be listed as <c>../..</c> and up, which no project in the corpus does and which
    /// reads as a mistake wherever it appears.</para>
    /// </summary>
    [Test]
    public void OnlyAFileUnderTheProjectCanBeListedInIt()
    {
        JsonElement[] answers = Apply(
            new Edit("within", null, Project, Inside("Shelf.pc"), null),
            new Edit("within", null, Project, Inside("models", "Book.pc"), null),
            new Edit(
                "within",
                null,
                Project,
                Path.Combine(Path.GetTempPath(), "profi-c-editing", "elsewhere", "Other.pc"),
                null));

        Assert.Multiple(() =>
        {
            Assert.That(answers[0].GetBoolean(), Is.True, "beside it");
            Assert.That(answers[1].GetBoolean(), Is.True, "under it");
            Assert.That(answers[2].GetBoolean(), Is.False, "outside it");
        });
    }
}
