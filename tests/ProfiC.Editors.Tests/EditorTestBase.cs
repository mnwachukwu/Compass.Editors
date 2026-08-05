using System.Text.Json;

namespace ProfiC.Editors.Tests;

/// <summary>
/// <para>Where things are, and what the language says it reserves.</para>
/// <para>The second is a fact about the other repository. Profi-C publishes it — <c>pc
/// vocabulary</c> prints every reserved word and built-in type name, and the result is committed
/// there — and this reads that file from a sibling checkout rather than keeping a copy. A copy
/// would drift, and a drifting copy is the failure the published file exists to prevent: the
/// grammar would agree with a list that was itself out of date, and nothing anywhere would
/// fail.</para>
/// </summary>
public abstract class EditorTestBase
{
    private static string? _repositoryRoot;

    /// <summary>
    /// Walks up from the test assembly until it finds the solution, so tests read the extension
    /// regardless of build configuration or working directory.
    /// </summary>
    protected static string RepositoryRoot
    {
        get
        {
            if (_repositoryRoot is not null)
            {
                return _repositoryRoot;
            }

            DirectoryInfo? directory = new(AppContext.BaseDirectory);

            // Either extension: the SDK writes .slnx now and .sln before, and which one is on
            // disk is not something these tests should have an opinion about.
            while (directory is not null && !IsRoot(directory))
            {
                directory = directory.Parent;
            }

            static bool IsRoot(DirectoryInfo directory) =>
                File.Exists(Path.Combine(directory.FullName, "Profi-C.Editors.slnx"))
                || File.Exists(Path.Combine(directory.FullName, "Profi-C.Editors.sln"));

            Assert.That(directory, Is.Not.Null, "could not locate the repository root");
            _repositoryRoot = directory!.FullName;
            return _repositoryRoot;
        }
    }

    /// <summary>The repository root, for fixtures that do not derive from this class.</summary>
    public static string RepositoryRootForTests => RepositoryRoot;

    /// <summary>The extension's folder.</summary>
    protected static string Extension => Path.Combine(RepositoryRoot, "vscode");

    /// <summary>The grammar every test here is about.</summary>
    protected static string GrammarPath =>
        Path.Combine(Extension, "syntaxes", "profi-c.tmLanguage.json");

    /// <summary>
    /// <para>Profi-C, if it is checked out beside this.</para>
    /// <para>Null where it is not, which is an ordinary state: a clone of one repository alone
    /// should still build and run everything that does not need the other.</para>
    /// </summary>
    protected static string? ProfiCRoot
    {
        get
        {
            string beside = Path.Combine(
                Directory.GetParent(RepositoryRoot)!.FullName, "Profi-C");

            return File.Exists(Path.Combine(beside, "Profi-C.sln")) ? beside : null;
        }
    }

    /// <summary>
    /// Profi-C's root, or an ignored test. <c>Assert.Ignore</c> throws but is not annotated as
    /// such, so the throw afterwards is what tells the compiler this cannot fall through.
    /// </summary>
    protected static string ProfiCOrIgnore(string why)
    {
        if (ProfiCRoot is { } root)
        {
            return root;
        }

        Assert.Ignore(why);

        throw new InvalidOperationException("unreachable: Assert.Ignore throws");
    }

    /// <summary>
    /// <para>A built <c>pc</c> to ask, or an ignored test.</para>
    /// <para>Some of what the extension does is not a decision of its own — which project claims
    /// a file is the compiler's answer, asked for rather than worked out here. Testing that the
    /// extension asks correctly therefore needs something to ask.</para>
    /// <para>Taken from the sibling checkout's <c>dist</c>, which is where publishing puts it and
    /// where a local install points, rather than from PATH: a test should hold the build beside
    /// it, not whichever compiler a machine happens to have installed. CI publishes it.</para>
    /// </summary>
    protected static string CompilerOrIgnore()
    {
        string profiC = ProfiCOrIgnore(
            "check out Profi-C beside this repository for a compiler to ask");

        string published = Path.Combine(
            profiC, "dist", OperatingSystem.IsWindows() ? "pc.exe" : "pc");

        if (!File.Exists(published))
        {
            Assert.Ignore(
                $"{published} is missing; run "
                + "'dotnet publish src/ProfiC.Cli.Alias -p:PublishProfile=dist' there");
        }

        return published;
    }

    /// <summary>
    /// <para>What a mismatch is being measured against, said out loud.</para>
    /// <para><b>These compare two repositories, so a failure has two explanations and only one of
    /// them is a fault here.</b> The vocabulary comes from a sibling checkout, and in CI that
    /// checkout is of Profi-C's default branch at the moment this job started — so a commit here
    /// that lands minutes before the language change it goes with reads a vocabulary from before
    /// it. That happened with <c>float</c>: the grammar was right, the word was reserved, and the
    /// run still failed. Naming the count turns the same failure into one a reader can tell apart
    /// at a glance, since a stale sibling is short by exactly the words it has not heard of.
    /// </para>
    /// </summary>
    protected static string Against(int words, string what) =>
        $"{what} (read against a vocabulary of {words} reserved words — if that number looks "
        + "low, the Profi-C checkout beside this one is older than the change being tested)";

    /// <summary>Every word the language reserves and every type it provides.</summary>
    protected sealed record Published(string[] ReservedWords, string[] TypeNames);

    /// <summary>
    /// <para>Reads the published vocabulary, or skips the test where Profi-C is not beside
    /// this.</para>
    /// <para>Skipped rather than failed, for the same reason the tokenization tests skip without
    /// their engine: the missing piece is fetched rather than committed, and its absence is a
    /// state of the checkout rather than a fault in the code. CI checks out both.</para>
    /// </summary>
    protected static Published Vocabulary()
    {
        string profiC = ProfiCOrIgnore(
            "check out Profi-C beside this repository to hold the grammar to its vocabulary");

        string path = Path.Combine(profiC, "docs", "vocabulary.json");

        if (!File.Exists(path))
        {
            Assert.Ignore($"{path} is missing; run 'pc vocabulary > docs/vocabulary.json' there");
        }

        using JsonDocument published = JsonDocument.Parse(File.ReadAllText(path));

        return new Published(
            Words(published.RootElement, "reservedWords"),
            Words(published.RootElement, "typeNames"));

        static string[] Words(JsonElement root, string name) =>
            [.. root.GetProperty(name).EnumerateArray().Select(word => word.GetString()!)];
    }
}
