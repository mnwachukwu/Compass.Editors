using System.Diagnostics;
using System.Text.Json;

namespace ProfiC.Editors.Tests;

/// <summary>
/// <para>What a reader's editor actually colors, run through the engine it runs.</para>
/// <para><see cref="EditorGrammarTests"/> reads the grammar's JSON and holds what it
/// <i>says</i> to what the language is. That is worth having and it is not this. "The file
/// names this scope" and "a reader sees this scope" are different claims, and only the second
/// one reaches anybody — the gap between them is where several confident statements about the
/// editor turned out to be wrong.</para>
/// <para>The engine is vscode-textmate over Oniguruma, which is what VS Code runs. A rule
/// behaving differently here behaves differently there.</para>
/// </summary>
[TestFixture]
public sealed class TokenizationTests : EditorTestBase
{
    /// <summary>One token, and every scope it carries.</summary>
    private sealed record Token(string Text, string[] Scopes);

    /// <summary>
    /// <para>Tokenizes lines and returns what came back, or skips where the engine is not
    /// installed.</para>
    /// <para>Skipped rather than failed, because the packages are fetched rather than
    /// committed and a checkout without them is an ordinary state to be in. Restoring them is
    /// <c>npm install</c> in the extension's folder.</para>
    /// </summary>
    private static Token[][] Scopes(params string[] lines) =>
        ScopesUnder("source.profi-c", lines);

    /// <summary>
    /// The same, under whichever grammar is named. A project file is written in the second one
    /// the extension ships, and nothing looked at what that one colors until this.
    /// </summary>
    private static Token[][] ScopesUnder(string scope, params string[] lines)
    {
        if (!Directory.Exists(Path.Combine(Extension, "node_modules")))
        {
            Assert.Ignore("run 'npm install' in the vscode folder to tokenize");
        }

        ProcessStartInfo start = new()
        {
            FileName = "node",
            WorkingDirectory = Extension,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        start.ArgumentList.Add(Path.Combine("tools", "scopes.js"));
        start.ArgumentList.Add(scope);

        using Process node = StartOrIgnore(start);

        node.StandardInput.Write(JsonSerializer.Serialize(lines));
        node.StandardInput.Close();

        string output = node.StandardOutput.ReadToEnd();
        string failed = node.StandardError.ReadToEnd();

        node.WaitForExit();

        Assert.That(node.ExitCode, Is.Zero, failed);

        return JsonSerializer.Deserialize<Token[][]>(
            output,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }

    /// <summary>Starts node, or skips where there is no node to start.</summary>
    private static Process StartOrIgnore(ProcessStartInfo start)
    {
        try
        {
            return Process.Start(start)!;
        }
        catch (Exception unavailable)
        {
            Assert.Ignore($"node is needed to tokenize: {unavailable.Message}");
            throw;
        }
    }

    /// <summary>Every scope carried by the first token whose text matches.</summary>
    private static string[] Carried(Token[][] lines, string text) =>
        lines.SelectMany(line => line)
             .First(token => token.Text.Trim() == text)
             .Scopes;

    // ---- Documentation labels ---------------------------------------------------------

    /// <summary>
    /// A label is colored whole — the mark, the name and the colon together — because the
    /// thing acting on the documentation is the word, not the punctuation around it.
    /// </summary>
    [Test]
    public void ALabelInABlockIsScopedWhole() => Assert.That(
        Carried(Scopes("##", "    @summary: One person's money.", "##"), "@summary:"),
        Does.Contain("constant.language.documentation.profi-c"));

    [Test]
    public void ALabelInALineCommentIsScopedTheSameWay() => Assert.That(
        Carried(Scopes("# @summary: Whose account this is."), "@summary:"),
        Does.Contain("constant.language.documentation.profi-c"));

    /// <summary>A label keeps the comment scope under its own, so a theme coloring all
    /// comments still colors the line it sits on.</summary>
    [Test]
    public void ALabelStaysInsideItsComment() => Assert.That(
        Carried(Scopes("##", "    @summary: A thing.", "##"), "@summary:"),
        Does.Contain("comment.block.profi-c"));

    /// <summary>
    /// <para>The case that decided the design, checked where it counts.</para>
    /// <para>Prose wraps, and a wrapped line often begins with a word and a colon. Coloring
    /// one would tell a reader the compiler is acting on a sentence it passes over.</para>
    /// </summary>
    [Test]
    public void WrappedProseIsNotALabel() => Assert.That(
        Scopes("##", "    That is why it yields an", "    optional: an answer.", "##")
            .SelectMany(line => line)
            .SelectMany(token => token.Scopes),
        Has.None.EqualTo("constant.language.documentation.profi-c"));

    // ---- Ignore directives ---------------------------------------------------------------

    [Test]
    public void ADirectiveIsScopedApartFromAnOrdinaryComment() => Assert.That(
        Carried(Scopes("# ignore opinion"), "# ignore opinion"),
        Does.Contain("comment.line.number-sign.directive.profi-c"));

    /// <summary>
    /// A remark opening with the word stays a remark, which is the rule the scanner reads by
    /// and the one a reader would have no way to check if the coloring disagreed.
    /// </summary>
    [Test]
    public void ProseBeginningWithTheWordIsNotADirective() => Assert.That(
        Carried(Scopes("# ignore the sign for now"), "# ignore the sign for now"),
        Does.Not.Contain("comment.line.number-sign.directive.profi-c"));

    // ---- Ordinary code, so the grammar is not merely quiet ---------------------------------

    /// <summary>
    /// A control against the others: a grammar matching nothing at all would pass every test
    /// above that asserts a scope is absent.
    /// </summary>
    [Test]
    public void OrdinaryCodeStillCarriesItsScopes()
    {
        Token[][] scanned = Scopes("model Account");

        Assert.Multiple(() =>
        {
            Assert.That(Carried(scanned, "model"), Does.Contain("keyword.declaration.profi-c"));
            Assert.That(Carried(scanned, "Account"), Does.Contain("entity.name.type.profi-c"));
        });
    }

    // ---- The shape a hover shows -------------------------------------------------------------

    /// <summary>
    /// <para>A declaration with nothing after it still colors its type.</para>
    /// <para><b>Not a shape a program contains, and the shape every hover has.</b> The language
    /// server answers what is under the pointer as a fragment — <c>Animal frank</c>, with no
    /// <c>;</c> and no <c>=</c> — and a hover's code block is colored by this grammar and by
    /// nothing else. A rule that waited for a terminator left every declared type in every hover
    /// the same color as the prose around it, while <c>integer counted</c> beside it lit, because
    /// <c>integer</c> is a word this grammar knows and <c>Animal</c> is not.</para>
    /// </summary>
    [TestCase("Animal frank", "Animal")]
    [TestCase("Animal[] pets", "Animal")]
    [TestCase("Suit? played", "Suit")]
    [TestCase("Shapes.Circle flat", "Circle")]
    public void ADeclarationWithNothingAfterItStillColorsItsType(string line, string type) =>
        Assert.That(Carried(Scopes(line), type), Does.Contain("entity.name.type.profi-c"));

    /// <summary>
    /// And a line that merely looks like one does not. The rule reaches the end of a line now, so
    /// what keeps it honest is every other thing it demands of the shape.
    /// </summary>
    [TestCase("loop each grade in grades", "grade")]
    [TestCase("yield total", "total")]
    public void SomethingThatIsNotADeclarationIsNotColoredAsOne(string line, string word) =>
        Assert.That(Carried(Scopes(line), word), Does.Not.Contain("entity.name.type.profi-c"));

    // ---- The project file, which is the extension's other grammar -----------------------------

    private const string ProjectScope = "source.profi-c-project";

    /// <summary>
    /// <para>Every word a project file may say is colored as one.</para>
    /// <para>Nothing looked at this grammar at all, and it had fallen three words behind the
    /// compiler: <c>entry</c> and <c>ignore</c> were as gray as a typo, and so was <c>output</c>
    /// the moment it existed. That is worse than coloring nothing — the grammar's own rule is
    /// that a word it does not know looks wrong <em>because</em> it is wrong, and a valid line
    /// reading as a mistake breaks the only signal it offers.</para>
    /// </summary>
    [TestCase("project Storefront", "project")]
    [TestCase("    source Program.pc", "source")]
    [TestCase("    reference ../books/books.pcp", "reference")]
    [TestCase("    output ../artifacts", "output")]
    [TestCase("    entry Tools.Program", "entry")]
    [TestCase("    ignore PC0410", "ignore")]
    [TestCase("end project", "end project")]
    public void EveryWordAProjectFileSaysIsColoredAsOne(string line, string word) =>
        Assert.That(
            Carried(ScopesUnder(ProjectScope, line), word),
            Does.Contain("keyword.other.profi-c"));

    /// <summary>
    /// <para>A word from some other build system is left alone, which is what the compiler says
    /// about it too. This is the claim the test above would let through if the grammar simply
    /// colored the first word of every line.</para>
    /// <para>Asked of the whole line rather than of the word, because an unmatched line comes
    /// back as one token holding all of it — there is no <c>include</c> to look up, which is
    /// itself the answer.</para>
    /// </summary>
    [TestCase("    include Program.pc")]
    [TestCase("    exclude Program.pc")]
    [TestCase("    ignore whatever")]
    public void AWordAProjectFileDoesNotSayIsLeftLookingWrong(string line) =>
        Assert.That(
            ScopesUnder(ProjectScope, line).SelectMany(one => one).SelectMany(t => t.Scopes),
            Does.Not.Contain("keyword.other.profi-c"),
            line);

    /// <summary>
    /// What follows the word is colored for what it is. Three of the lines name a place on disk
    /// and one names a type, and reading an entry as a path would say the build begins at a file.
    /// </summary>
    [Test]
    public void WhatFollowsTheWordIsColoredForWhatItIs()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                Carried(ScopesUnder(ProjectScope, "    output ../artifacts"), "../artifacts"),
                Does.Contain("string.unquoted.path.profi-c"));

            Assert.That(
                Carried(ScopesUnder(ProjectScope, "    entry Tools.Program"), "Tools.Program"),
                Does.Contain("entity.name.type.profi-c"));
        });
    }
}
