using System.Text.Json;
using System.Text.RegularExpressions;

namespace ProfiC.Editors.Tests;

/// <summary>
/// <para>What the editor considers one word.</para>
/// <para><b>More rides on this than its size suggests, and none of it announces itself.</b> The
/// pattern decides what double-clicking selects, what a rename box starts with, and — the one
/// that is invisible when wrong — the prefix a completion list is filtered against. A pattern
/// that matches nothing does not fail: the list simply comes back empty, and the server looks
/// broken while answering perfectly.</para>
/// <para>It was written once as <c>[\p{L}_][\p{L}\p{Nd}_]*</c>, which is correct in a regular
/// expression compiled with the Unicode flag and matches <b>nothing at all</b> in one compiled
/// without it. VS Code compiles this one without it. So these run the pattern the way the editor
/// runs it, and against words rather than against the pattern's own text.</para>
/// </summary>
[TestFixture]
public sealed class WordPatternTests : EditorTestBase
{
    private static Regex Configured()
    {
        using JsonDocument read = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(Extension, "language-configuration.json")));

        string pattern = read.RootElement.GetProperty("wordPattern").GetString()!;

        // No RegexOptions.ECMAScript and no Unicode flag, which is what the editor does with it.
        return new Regex(pattern);
    }

    /// <summary>
    /// <para>A name is one word, whole.</para>
    /// <para>The partial ones matter as much as the finished ones: completion is filtered against
    /// what has been typed so far, so a pattern that only recognizes a name once it is complete
    /// offers nothing while it is being written.</para>
    /// </summary>
    [TestCase("counter")]
    [TestCase("coun")]
    [TestCase("c")]
    [TestCase("_hidden")]
    [TestCase("value10")]
    public void ANameIsOneWord(string name)
    {
        Match found = Configured().Match(name);

        Assert.Multiple(() =>
        {
            Assert.That(found.Success, Is.True, name);
            Assert.That(found.Value, Is.EqualTo(name));
        });
    }

    /// <summary>
    /// A reserved word used as a name keeps its escape, since the two together are what a reader
    /// selects and what a rename would replace.
    /// </summary>
    [Test]
    public void AnEscapedReservedWordKeepsItsMark() =>
        Assert.That(Configured().Match("@integer").Value, Is.EqualTo("@integer"));

    /// <summary>
    /// <para>A name outside ASCII is a name.</para>
    /// <para>The scanner uses <c>char.IsLetter</c>, so these are legal identifiers today. A
    /// pattern listing A to Z would quietly disagree with the compiler about what a name is.
    /// </para>
    /// </summary>
    [TestCase("café")]
    [TestCase("变量")]
    public void ANameOutsideAsciiIsAWord(string name) =>
        Assert.That(Configured().Match(name).Value, Is.EqualTo(name));

    /// <summary>
    /// <para>Punctuation ends a word.</para>
    /// <para>The half that a pattern written as "anything but whitespace" would get wrong — the
    /// dot in <c>counter.Next</c> has to break it, or completing a member would be filtered
    /// against the receiver as well.</para>
    /// </summary>
    [TestCase("counter.Next", "counter", "Next")]
    [TestCase("total = total + 1;", "total", "total", "1")]
    [TestCase("Program.Twice(2)", "Program", "Twice", "2")]
    public void PunctuationEndsAWord(string line, params string[] expected) =>
        Assert.That(
            Configured().Matches(line).Select(m => m.Value),
            Is.EqualTo(expected));
}
