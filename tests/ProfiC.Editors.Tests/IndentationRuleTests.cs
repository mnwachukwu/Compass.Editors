using System.Text.Json;
using System.Text.RegularExpressions;

namespace ProfiC.Editors.Tests;

/// <summary>
/// <para>Where the editor puts a line on its own, before anything is asked of the server.</para>
/// <para><b>The server places a line when Enter is pressed, and these place one when a word is
/// typed.</b> Nothing asks anybody anything on the way to <c>end</c>: the editor re-indents the
/// line the moment what is on it matches the pattern below, so a construct the pattern does not
/// know about is one the editor walks past, looking further up the file for something it does
/// recognize — and it lands on whatever it finds there, a level or two out from where the line
/// belongs.</para>
/// <para>That is not hypothetical. The patterns named <c>while</c> and <c>for</c> as openers, and
/// went on naming them after the loops were unified under <c>loop</c>, so typing <c>end</c> under
/// a loop put it level with the enclosing function. Nothing failed, because nothing here read
/// them against the language.</para>
/// </summary>
[TestFixture]
public sealed class IndentationRuleTests : EditorTestBase
{
    private static Regex Rule(string which)
    {
        using JsonDocument read = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(Extension, "language-configuration.json")));

        string pattern = read.RootElement
            .GetProperty("indentationRules")
            .GetProperty(which)
            .GetString()!;

        return new Regex(pattern);
    }

    /// <summary>
    /// <para>Every line in the corpus that opens a body is a line the editor knows opens one.
    /// </para>
    /// <para><b>Read from the samples rather than from a list written here, which is the point.
    /// </b> A list would have been written the day the pattern was, and would have gone stale
    /// beside it. The samples are laid out by hand and formatted by <c>pc format</c>, so a line
    /// with a deeper line under it is a line that opened something — whatever the language has
    /// since grown.</para>
    /// <para>Only lines nothing can be mistaken about are read: no comment mark, no quotation
    /// mark, brackets balanced, and outside any block string or block comment. A wrapped call is
    /// followed by a deeper line too, and it opens no body — so the ones that cannot be told
    /// apart cheaply are left out rather than guessed at.</para>
    /// </summary>
    [Test]
    public void EveryLineThatOpensABodyIsRecognized()
    {
        string samples = Path.Combine(
            ProfiCOrIgnore("check out Profi-C beside this repository to read its samples"),
            "samples");

        if (!Directory.Exists(samples))
        {
            Assert.Ignore($"{samples} is missing");
        }

        Regex increases = Rule("increaseIndentPattern");

        List<string> unrecognized = [];

        foreach (string file in Directory.EnumerateFiles(samples, "*.pc", SearchOption.AllDirectories))
        {
            // The programs that do not compile, which are laid out to make a point rather than to
            // be read for one — one of them writes 'function' where a type belongs.
            if (file.Contains($"{Path.DirectorySeparatorChar}negatives{Path.DirectorySeparatorChar}",
                              StringComparison.Ordinal))
            {
                continue;
            }

            unrecognized.AddRange(
                Opening(File.ReadAllText(file).ReplaceLineEndings("\n").Split('\n'))
                    .Where(line => !increases.IsMatch(line))
                    .Select(line => $"{Path.GetFileName(file)}: {line.Trim()}"));
        }

        Assert.That(
            unrecognized,
            Is.Empty,
            "these open a body in the corpus and the editor does not know they do");
    }

    /// <summary>
    /// <para>The lines of a file that open a body, as far as text alone can tell.</para>
    /// <para>A line qualifies where the next line with anything on it begins exactly one level
    /// deeper and there is nothing about the line itself to doubt. Everything doubtful is
    /// dropped: the inside of a block string or block comment is not code, a line holding a
    /// comment mark or a quotation mark may hold anything at all, and a line with a bracket left
    /// open is a wrapped one, whose next line is deeper for a reason that has nothing to do with
    /// a body.</para>
    /// </summary>
    private static IEnumerable<string> Opening(string[] lines)
    {
        const int Level = 4;

        bool inText = false;
        bool inNote = false;

        for (int at = 0; at < lines.Length; at++)
        {
            string line = lines[at];
            bool code = !inText && !inNote;

            if (Runs(line, "\"{3,}") % 2 == 1)
            {
                inText = !inText;
            }
            else if (!inText && Runs(line, "##") % 2 == 1)
            {
                inNote = !inNote;
            }

            if (!code || line.Trim().Length == 0 || line.IndexOfAny(['#', '"', '\'']) >= 0)
            {
                continue;
            }

            if (!Balanced(line, '(', ')') || !Balanced(line, '[', ']') || !Balanced(line, '{', '}'))
            {
                continue;
            }

            int indent = line.Length - line.TrimStart(' ').Length;

            if (indent % Level != 0 || line[..indent].Trim().Length > 0)
            {
                continue;
            }

            int next = at + 1;

            while (next < lines.Length && lines[next].Trim().Length == 0)
            {
                next++;
            }

            if (next < lines.Length
                && lines[next].Length - lines[next].TrimStart(' ').Length == indent + Level)
            {
                yield return line;
            }
        }

        static int Runs(string line, string of) => Regex.Matches(line, of).Count;

        static bool Balanced(string line, char open, char close) =>
            line.Count(c => c == open) == line.Count(c => c == close);
    }

    /// <summary>
    /// <para>What must not be read as opening a body.</para>
    /// <para>The corpus above says what the patterns have to catch and cannot say what they have
    /// to leave alone, since a line followed by nothing deeper proves nothing — a <c>case</c>
    /// sharing its statements with the label under it is followed by another <c>case</c> at the
    /// same level, and still opens one. So the far side is written out.</para>
    /// <para>Both forms that end in a semicolon are here because both read exactly like the form
    /// that does open a body, up to the last character.</para>
    /// </summary>
    [TestCase("namespace Shapes;", TestName = "ANamespaceClaimingTheFileOpensNoBody")]
    [TestCase("    abstract real function Area();", TestName = "AFunctionDeclaredOpensNoBody")]
    [TestCase("        Console.WriteLine(1);", TestName = "AStatementOpensNoBody")]
    [TestCase("        end if", TestName = "AnEndingOpensNoBody")]
    [TestCase("        integer counted = 0;", TestName = "ADeclarationOpensNoBody")]
    [TestCase("        Program.Show(function() let a = 1; end function);",
              TestName = "ALambdaOnOneLineOpensNoBody")]
    public void ALineThatOpensNoBodyIsLeftAlone(string line) =>
        Assert.That(Rule("increaseIndentPattern").IsMatch(line), Is.False, line);

    /// <summary>
    /// <para>Every word either pattern names is a word the language reserves.</para>
    /// <para>The other direction of the same staleness. A word dropped from the language leaves a
    /// pattern matching something nobody can write, which is harmless until the word is reused —
    /// and reads, until then, as a rule that covers a construct it does not.</para>
    /// </summary>
    [TestCase("increaseIndentPattern")]
    [TestCase("decreaseIndentPattern")]
    public void EveryWordNamedIsOneTheLanguageReserves(string which)
    {
        HashSet<string> reserved = new(Vocabulary().ReservedWords, StringComparer.Ordinal);

        string[] named = [.. Regex.Matches(Rule(which).ToString(), @"(?<=[(|])[a-z]+(?=[)|])")
            .Select(found => found.Value)
            .Distinct()];

        Assert.Multiple(() =>
        {
            Assert.That(named, Is.Not.Empty, "the words are read out of the pattern by shape");

            Assert.That(
                named.Where(word => !reserved.Contains(word)),
                Is.Empty,
                Against(reserved.Count, $"{which} names words the language does not have"));
        });
    }
}
