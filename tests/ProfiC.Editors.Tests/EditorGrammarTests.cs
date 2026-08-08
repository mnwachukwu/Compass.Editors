using System.Text.Json;
using System.Text.RegularExpressions;

namespace ProfiC.Editors.Tests;

/// <summary>
/// <para>Holds the editor grammar to what the compiler actually reads.</para>
/// <para>A TextMate grammar is a second, hand-written description of the same language, and
/// nothing about adding a keyword makes it change too. The word lists have drifted the moment
/// one is added and the other is not, and the only sign is a word that stops being colored —
/// which nobody notices, because a missing color looks like an ordinary name.</para>
/// </summary>
[TestFixture]
public sealed class EditorGrammarTests : EditorTestBase
{
    private static JsonDocument Grammar() =>
        JsonDocument.Parse(File.ReadAllText(GrammarPath));

    /// <summary>
    /// Every word in every <c>match</c> the grammar holds, taken from the alternations they are
    /// written as. Reading the file rather than a list kept beside it, since a list beside it
    /// would be a third thing to drift.
    /// </summary>
    private static HashSet<string> WordsInGrammar()
    {
        HashSet<string> words = new(StringComparer.Ordinal);
        Collect(Grammar().RootElement, words);
        return words;

        static void Collect(JsonElement element, HashSet<string> into)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (JsonProperty property in element.EnumerateObject())
                    {
                        if (property.NameEquals("match") || property.NameEquals("begin"))
                        {
                            foreach (Match found in Regex.Matches(
                                         property.Value.GetString() ?? string.Empty,
                                         @"[A-Za-z][A-Za-z]*"))
                            {
                                into.Add(found.Value);
                            }
                        }

                        Collect(property.Value, into);
                    }

                    break;

                case JsonValueKind.Array:
                    foreach (JsonElement item in element.EnumerateArray())
                    {
                        Collect(item, into);
                    }

                    break;
            }
        }
    }

    [Test]
    public void TheGrammarFileIsThere() =>
        Assert.That(File.Exists(GrammarPath), Is.True, GrammarPath);

    [Test]
    public void TheGrammarIsValidJson() =>
        Assert.DoesNotThrow(() => Grammar().Dispose());

    /// <summary>
    /// <para>Every pattern in the file is a working regular expression. A broken one does not
    /// stop the editor loading the grammar; it silently colors nothing.</para>
    /// <para>An <c>end</c> pattern is compiled behind its own <c>begin</c>, because that is the
    /// only context it ever runs in: the editor substitutes what <c>begin</c> captured before
    /// matching it, so a back-reference there is checked against the groups <c>begin</c>
    /// defines rather than against nothing.</para>
    /// </summary>
    [Test]
    public void EveryPatternCompiles()
    {
        List<string> broken = [];
        Walk(Grammar().RootElement);

        Assert.That(broken, Is.Empty);

        void Walk(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    string opener = element.TryGetProperty("begin", out JsonElement begin)
                                    && begin.ValueKind == JsonValueKind.String
                        ? begin.GetString()!
                        : string.Empty;

                    foreach (JsonProperty property in element.EnumerateObject())
                    {
                        if (property.Value.ValueKind == JsonValueKind.String
                            && property.Name is "match" or "begin" or "end")
                        {
                            string pattern = property.Name == "end"
                                ? opener + property.Value.GetString()
                                : property.Value.GetString()!;

                            try
                            {
                                _ = new Regex(pattern);
                            }
                            catch (ArgumentException problem)
                            {
                                broken.Add($"{property.Value.GetString()}: {problem.Message}");
                            }
                        }

                        Walk(property.Value);
                    }

                    break;

                case JsonValueKind.Array:
                    foreach (JsonElement item in element.EnumerateArray())
                    {
                        Walk(item);
                    }

                    break;
            }
        }
    }

    /// <summary>
    /// <para>The block comment rule reads a block the way the scanner does.</para>
    /// <para>The closer takes the rest of its line, which is the rule the whole comment design
    /// rests on, and an editor that ends the block at the marks instead would color the tail
    /// of that line as code. Checked against the same awkward lines the scanner is.</para>
    /// </summary>
    [TestCase("##", false, TestName = "an opener alone stays open")]
    [TestCase("## text ##", true, TestName = "a block on one line closes")]
    [TestCase("####", true, TestName = "four marks are a whole comment")]
    [TestCase("#########", true, TestName = "a heading run is a whole comment")]
    [TestCase("## and then some", false, TestName = "an opener with text stays open")]
    public void TheBlockRuleClosesWhereTheScannerDoes(string line, bool closes)
    {
        JsonElement block = Grammar().RootElement
                                     .GetProperty("repository")
                                     .GetProperty("comment-block");

        Match opened = new Regex(block.GetProperty("begin").GetString()!).Match(line);
        Assert.That(opened.Success, Is.True, "the line opens a block");

        string rest = line[(opened.Index + opened.Length)..];

        Assert.That(
            new Regex(block.GetProperty("end").GetString()!).IsMatch(rest),
            Is.EqualTo(closes));
    }

    /// <summary>
    /// The block rule is offered before the line rule, or <c>##</c> scans as two line comments
    /// and a block spanning lines is never recognized at all.
    /// </summary>
    [Test]
    public void TheBlockRuleIsTriedBeforeTheLineRule()
    {
        string[] order = [.. Grammar().RootElement
                                      .GetProperty("patterns")
                                      .EnumerateArray()
                                      .Select(p => p.GetProperty("include").GetString()!)];

        Assert.That(
            Array.IndexOf(order, "#comment-block"),
            Is.LessThan(Array.IndexOf(order, "#comment-line")));
    }

    /// <summary>
    /// <para>Every reserved word appears somewhere in the grammar.</para>
    /// <para>This is the assertion that catches the drift: a word added to the language and
    /// not to the grammar fails here, at the moment it is added, rather than months later when
    /// somebody notices it is the wrong color.</para>
    /// </summary>
    [Test]
    public void EveryReservedWordIsInTheGrammar()
    {
        HashSet<string> words = WordsInGrammar();

        string[] reserved = Vocabulary().ReservedWords;

        string[] missing = [.. reserved.Where(word => !words.Contains(word))
                                       .OrderBy(word => word, StringComparer.Ordinal)];

        Assert.That(missing, Is.Empty, Against(reserved.Length,
            "reserved words the editor grammar does not color"));
    }


    /// <summary>
    /// And every type the language provides, which the scanner reads as an ordinary name and
    /// so cannot be caught by the word list above.
    /// </summary>
    [Test]
    public void EveryTypeTheLanguageProvidesIsInTheGrammar()
    {
        HashSet<string> words = WordsInGrammar();

        string[] provided = Vocabulary().TypeNames;

        string[] missing = [.. provided.Where(name => !words.Contains(name))
                                       .OrderBy(name => name, StringComparer.Ordinal)];

        Assert.That(missing, Is.Empty, Against(provided.Length,
            "types the editor grammar does not color"));
    }

    /// <summary>
    /// Nothing in the grammar's word lists is a word the language dropped. Catches the other
    /// direction of the same drift, where a keyword is removed and its color outlives it.
    /// </summary>
    [Test]
    public void TheGrammarColorsNoWordTheLanguageDropped()
    {
        // Read from the keyword patterns alone: the rest of the file is scope names and
        // regular-expression machinery, and neither is a claim about the language.
        JsonElement repository = Grammar().RootElement.GetProperty("repository");
        HashSet<string> claimed = new(StringComparer.Ordinal);

        foreach (string section in new[] { "keyword", "type", "constant" })
        {
            foreach (Match found in Regex.Matches(
                         repository.GetProperty(section).GetRawText(),
                         @"\\\\b\(([a-z|]+)\)\\\\b"))
            {
                foreach (string word in found.Groups[1].Value.Split('|'))
                {
                    claimed.Add(word);
                }
            }
        }

        HashSet<string> reserved = new(Vocabulary().ReservedWords, StringComparer.Ordinal);

        string[] unknown = [.. claimed
                                .Where(word => !reserved.Contains(word))
                                .OrderBy(word => word, StringComparer.Ordinal)];

        Assert.That(unknown, Is.Empty, Against(reserved.Count,
            "words the grammar colors that the language does not reserve"));
    }

    /// <summary>
    /// <para>The directive rule sets apart exactly the comments the compiler heeds.</para>
    /// <para>Two ways to be wrong, and the second is the one that matters. Missing a real
    /// directive leaves it looking like prose, which is untidy. Coloring a comment that is
    /// <i>not</i> one tells a reader the compiler is acting on a sentence it is ignoring, and
    /// they would have no way to find out otherwise. So the rows below are the scanner's own
    /// edges: the word must come first, the target must be whole, and a near miss is prose.
    /// </para>
    /// </summary>
    [TestCase("# ignore warning", true)]
    [TestCase("# ignore opinion", true)]
    [TestCase("# ignore PC0340", true)]
    [TestCase("# ignore pc0340", true, TestName = "an identifier is read whatever its case")]
    [TestCase("# ignore opinion in file", true)]
    [TestCase("# ignore opinion because the blank line is the point", true)]
    [TestCase("#ignore opinion", true, TestName = "no space after the mark")]
    [TestCase("Console.WriteLine(x); # ignore opinion", true, TestName = "after code on the line")]
    [TestCase("# ignore opinions", false, TestName = "a near miss is prose")]
    [TestCase("# ignore PC03400", false, TestName = "an identifier is four digits")]
    [TestCase("# ignore the sign for now", false)]
    [TestCase("# ignore", false, TestName = "the target is never absent")]
    [TestCase("# please ignore opinion", false, TestName = "the word comes first or not at all")]
    [TestCase("# a remark", false)]
    public void TheDirectiveRuleMatchesWhatTheScannerReads(string line, bool directive)
    {
        string pattern = Grammar().RootElement
                                  .GetProperty("repository")
                                  .GetProperty("comment-directive")
                                  .GetProperty("match")
                                  .GetString()!;

        Assert.That(new Regex(pattern).IsMatch(line), Is.EqualTo(directive));
    }

    /// <summary>
    /// The directive rule is offered before the ordinary line rule, which would otherwise take
    /// every comment first and leave the directive rule matching nothing at all.
    /// </summary>
    [Test]
    public void TheDirectiveRuleIsTriedBeforeTheLineRule()
    {
        string[] order = [.. Grammar().RootElement
                                      .GetProperty("patterns")
                                      .EnumerateArray()
                                      .Select(p => p.GetProperty("include").GetString()!)];

        Assert.That(
            Array.IndexOf(order, "#comment-directive"),
            Is.LessThan(Array.IndexOf(order, "#comment-line")));
    }

    /// <summary>
    /// <para>The extension contributes no colors, because it cannot.</para>
    /// <para>Token colors offered through <c>configurationDefaults</c> are never applied —
    /// measured, after a manifest carrying them was watched losing to a theme rule for six
    /// rounds of changing it. Carrying one anyway is worse than carrying none: it reads as a
    /// color the extension sets, so the next person to find the wrong color on screen looks
    /// at the manifest, sees the right value, and believes it.</para>
    /// <para>What a reader sees is the scope the grammar chooses and whatever their settings
    /// or theme paint it. The extension's README carries the block to paste.</para>
    /// <para><c>configurationDefaults</c> itself is allowed, and is used — a default is the right
    /// way to turn on a behavior this language wants and the editor cannot guess. Only color is
    /// refused, so the claim is about which keys are set rather than about the section being
    /// absent.</para>
    /// </summary>
    [Test]
    public void TheExtensionContributesNoColors()
    {
        using JsonDocument manifest = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(
                Extension, "package.json")));

        if (!manifest.RootElement.GetProperty("contributes")
                     .TryGetProperty("configurationDefaults", out JsonElement defaults))
        {
            Assert.Pass("the manifest sets no defaults at all");
        }

        Assert.That(
            Painting(defaults),
            Is.Empty,
            "an extension cannot set token colors; saying it does misleads");
    }

    /// <summary>
    /// Every default whose name is about color, however deeply it sits. Found by walking rather
    /// than by listing the spellings known today, so that a third cannot arrive unnoticed.
    /// </summary>
    private static IEnumerable<string> Painting(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (property.Name.Contains("color", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("token", StringComparison.OrdinalIgnoreCase))
            {
                yield return property.Name;
            }

            foreach (string deeper in Painting(property.Value))
            {
                yield return deeper;
            }
        }
    }

    /// <summary>
    /// <para>Every scope named anywhere colors are chosen is one the grammar still
    /// produces.</para>
    /// <para>Two files name these by hand — the workspace settings that color this repository
    /// while it is worked on, and the block <c>docs/the-extension.md</c> offers anyone else to
    /// copy. A scope renamed in the grammar leaves both pointing at nothing, which is silent,
    /// paints nothing, and looks exactly like a color that will not take. One did sit stale for
    /// a while, which is how this test came to exist.</para>
    /// <para>That block sat in the extension's own README until that became the Marketplace
    /// listing, which says a sentence about the palette rather than carrying it. This follows
    /// the block rather than the file it used to be in.</para>
    /// </summary>
    /// <remarks>
    /// Both files are in this repository. The palette used to live in Profi-C's workspace
    /// settings and was reached through the sibling checkout, which meant the colors only
    /// applied in that one folder — it moved here so that installing the extension is enough.
    /// </remarks>
    [TestCase("vscode/palette.js")]
    [TestCase("docs/the-extension.md")]
    public void EveryColoredScopeIsOneTheGrammarProduces(string file)
    {
        string grammar = File.ReadAllText(GrammarPath);
        string text = File.ReadAllText(Locate(file));

        string[] painted = [.. Regex
            .Matches(text, Scope)
            .SelectMany(m => Regex.Matches(m.Groups[1].Value, Quoted))
            .Select(m => m.Groups[1].Value)
            .Where(scope => scope.EndsWith(".profi-c", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)];

        Assert.Multiple(() =>
        {
            Assert.That(painted, Is.Not.Empty, $"{file} should name some scopes");

            Assert.That(
                painted.Where(scope => !grammar.Contains(scope, StringComparison.Ordinal)),
                Is.Empty,
                $"scopes {file} colors that the grammar no longer produces");
        });
    }

    /// <summary>
    /// <para>Every scope the grammar produces is one the palette paints, unless it is on the
    /// list below of scopes deliberately left to the theme.</para>
    /// <para><b>The other direction, and the one that catches a scope being added.</b> The test
    /// above finds a scope that was renamed away; nothing found a scope that was newly
    /// introduced and never given a color — which is silent in a different way. The grammar goes
    /// on tokenizing perfectly, the new token simply falls through to whatever the theme happens
    /// to say, and it reads as one kind of thing colored like another. <c>float</c> literals did
    /// exactly that: colored correctly by the grammar, named nowhere in the palette, and so
    /// painted unlike every other number.</para>
    /// <para>The allowances are a decision rather than an oversight, which is why they are
    /// written out. A theme already colors <c>invalid</c>, the operator families are offered in
    /// the README for anyone who wants them and are plainer left alone, and the root and the
    /// separators are not things to paint.</para>
    /// </summary>
    [Test]
    public void EveryScopeTheGrammarProducesIsPainted()
    {
        string[] unpainted =
        [
            "source.profi-c",
            "punctuation.separator.profi-c",
            "invalid.illegal.unknown-escape.profi-c",
            "keyword.operator.arithmetic.profi-c",
            "keyword.operator.assignment.profi-c",
            "keyword.operator.comparison.profi-c",
            "keyword.operator.optional.profi-c",
        ];

        string palette = File.ReadAllText(Locate("vscode/palette.js"));

        string[] produced = [.. Regex
            .Matches(File.ReadAllText(GrammarPath), @"[\w.\-]+\.profi-c")
            .Select(m => m.Value)
            .Distinct(StringComparer.Ordinal)
            .Except(unpainted, StringComparer.Ordinal)];

        Assert.Multiple(() =>
        {
            Assert.That(produced, Is.Not.Empty, "the grammar should produce some scopes");

            Assert.That(
                produced.Where(scope => !palette.Contains(scope, StringComparison.Ordinal)),
                Is.Empty,
                "scopes the grammar produces that no rule colors — give each one a color, or "
                + "add it to the list of scopes deliberately left to the theme");
        });
    }

    /// <summary>
    /// <para>Where a named file is, given that one of them belongs to the other repository.</para>
    /// <para>Anything beginning <c>.vscode/</c> is Profi-C's workspace settings; everything else
    /// is here. Skips rather than fails where Profi-C is not beside this.</para>
    /// </summary>
    private static string Locate(string file) => Path.Combine(RepositoryRoot, file);

    /// <summary>
    /// Settings files admit comments and a JSON reader does not, so they come out before it is
    /// asked to read one.
    /// </summary>
    private static string StripComments(string text) =>
        Regex.Replace(text, @"^\s*//.*$", string.Empty, RegexOptions.Multiline);

    /// <summary>
    /// <para>How a rule is written, in either of the two languages that hold one.</para>
    /// <para>The palette is JavaScript so that the reasoning beside each color survives — JSON
    /// admits no comments — while the README shows the same rules as JSON, which is what a
    /// reader would paste into settings. So a key may or may not be quoted and a string may use
    /// either mark, and these read both rather than each file needing its own reader.</para>
    /// </summary>
    private const string Scope = @"""?scope""?\s*:\s*(""[^""]+""|'[^']+'|\[[^\]]*\])";

    private const string Quoted = @"[""']([^""']+)[""']";

    private const string Colored =
        Scope + @"\s*,\s*""?settings""?\s*:\s*\{([^}]*)\}";

    /// <summary>Every color a file gives a Profi-C scope, read off its rules.</summary>
    private static Dictionary<string, string> Palette(string file)
    {
        Dictionary<string, string> painted = new(StringComparer.Ordinal);

        foreach (Match rule in Regex.Matches(File.ReadAllText(Locate(file)), Colored))
        {
            if (Regex.Match(rule.Groups[2].Value, @"""?foreground""?\s*:\s*[""']([^""']+)[""']")
                is { Success: true } color)
            {
                foreach (Match scope in Regex.Matches(rule.Groups[1].Value, Quoted))
                {
                    if (scope.Groups[1].Value.EndsWith(".profi-c", StringComparison.Ordinal))
                    {
                        painted[scope.Groups[1].Value] = color.Groups[1].Value;
                    }
                }
            }
        }

        return painted;
    }

    /// <summary>
    /// <para>The palette the documentation shows agrees with the one the extension applies.</para>
    /// <para>It once carried the whole thing and drifted from it on three colors, unnoticed,
    /// because nothing compared them. It now shows a few rules to give the shape and points at
    /// the palette for the rest — and the few it shows are held to what the command actually
    /// writes, since a wrong color in the one place a reader copies from is worse than no
    /// example at all.</para>
    /// </summary>
    [Test]
    public void TheDocumentedPaletteAgreesWithTheOneApplied()
    {
        Dictionary<string, string> applied = Palette(Path.Combine("vscode", "palette.js"));
        Dictionary<string, string> shown = Palette(Path.Combine("docs", "the-extension.md"));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.Not.Empty, "the palette should name some colors");
            Assert.That(shown, Is.Not.Empty, "the README should show some of the palette");

            Assert.That(
                shown.Where(rule => !applied.TryGetValue(rule.Key, out string? real)
                                    || real != rule.Value)
                     .Select(rule => $"{rule.Key}: README says {rule.Value}"),
                Is.Empty,
                "colors the README shows that the extension does not apply");
        });
    }

    /// <summary>
    /// <para>A label is scoped as a language constant, and scoped whole.</para>
    /// <para>Whole because the thing acting on the documentation is the word, not the
    /// punctuation around it, and a mark left in the prose color reads as though the label
    /// began a character late. A constant rather than a keyword because a keyword scope is
    /// painted in a theme's loudest color, which is wrong for a line addressed to tooling
    /// rather than to a reader.</para>
    /// </summary>
    [Test]
    public void ALabelIsScopedWholeAsAConstant()
    {
        JsonElement captures = Grammar().RootElement
                                        .GetProperty("repository")
                                        .GetProperty("documentation-label")
                                        .GetProperty("captures");

        Assert.Multiple(() =>
        {
            Assert.That(
                captures.EnumerateObject().Count(),
                Is.EqualTo(1),
                "the mark, the name and the colon are colored together");

            Assert.That(
                captures.GetProperty("1").GetProperty("name").GetString(),
                Does.StartWith("constant."));
        });
    }

    /// <summary>
    /// <para>The label rule picks out what the scanner reads as a label, and nothing else.</para>
    /// <para>The mark is the whole of it. Coloring a wrapped line beginning with a word and a
    /// colon would tell a reader the compiler is acting on prose it is passing over, and they
    /// would have no way to find out otherwise.</para>
    /// </summary>
    [TestCase("@summary: A thing.", true)]
    [TestCase("@remarks: At greater length.", true)]
    [TestCase("@n: how many terms.", true)]
    [TestCase("@yields: the total.", true)]
    [TestCase("optional: nothing more to read is an answer.", false, TestName = "wrapped prose")]
    [TestCase("summary: without the mark", false, TestName = "the mark is required")]
    [TestCase("@ n: a space after the mark", false)]
    [TestCase("@1st: a name cannot begin with a digit", false)]
    public void TheLabelRuleMatchesWhatTheScannerReads(string line, bool label)
    {
        string pattern = Grammar().RootElement
                                  .GetProperty("repository")
                                  .GetProperty("documentation-label")
                                  .GetProperty("match")
                                  .GetString()!;

        Assert.That(new Regex(pattern).IsMatch(line), Is.EqualTo(label));
    }

    /// <summary>
    /// Both comment forms carry labels, since one line is enough to document something and a
    /// block is what a longer one needs. A rule reaching only into blocks would leave the line
    /// form looking like prose while the compiler read it as documentation.
    /// </summary>
    [TestCase("comment-block")]
    [TestCase("comment-line")]
    public void BothCommentFormsReachTheLabelRule(string rule)
    {
        JsonElement comment = Grammar().RootElement
                                       .GetProperty("repository")
                                       .GetProperty(rule);

        Assert.That(
            comment.GetRawText(),
            Does.Contain("#documentation-label"),
            $"{rule} should pick out documentation labels");
    }
}
