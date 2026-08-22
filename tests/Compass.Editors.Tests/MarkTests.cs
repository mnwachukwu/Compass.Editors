using System.Buffers.Binary;
using System.Text.Json;

namespace Compass.Editors.Tests;

/// <summary>
/// <para>Holds the language's mark to being one drawing.</para>
/// <para><c>icon.svg</c> here is the only drawing of it anywhere. The website reads this file
/// before every build and uses the one copy for its favicon, in its header, and on the card a
/// shared link becomes — so the mark cannot disagree with itself, because there is nothing for it
/// to disagree with.</para>
/// <para><c>icon.png</c> is the exception and cannot be avoided: the Marketplace accepts PNG
/// only. It is a rasterization rather than a second drawing, made by
/// <c>vscode/tools/draw-icon.js</c>. That leaves one way for the two to drift — changing the SVG
/// and not redrawing — which nothing here can see, since telling the pictures apart would mean
/// rendering one, and a browser does not render the same bytes twice across versions. What these
/// tests do cover is every other way it goes wrong: a PNG that is missing, truncated, the wrong
/// size, or no longer the file the Marketplace is pointed at.</para>
/// </summary>
[TestFixture]
public sealed class MarkTests : EditorTestBase
{
    private static string DrawingPath => Path.Combine(Extension, "icon.svg");

    private static string PicturePath => Path.Combine(Extension, "icon.png");

    /// <summary>The size a Marketplace listing asks for, and what the drawing script writes.</summary>
    private const int Size = 128;

    [Test]
    public void TheDrawingIsThere() =>
        Assert.That(File.Exists(DrawingPath), Is.True, $"{DrawingPath} is the mark itself");

    /// <summary>
    /// The manifest names the picture rather than the drawing. Pointing at the SVG is the mistake
    /// worth catching by hand, because it looks right here and fails only at publish: the
    /// Marketplace rejects an SVG icon outright.
    /// </summary>
    [Test]
    public void TheManifestPointsAtThePicture()
    {
        using JsonDocument manifest =
            JsonDocument.Parse(File.ReadAllText(Path.Combine(Extension, "package.json")));

        Assert.That(manifest.RootElement.GetProperty("icon").GetString(), Is.EqualTo("icon.png"));
    }

    /// <summary>
    /// <para>The picture is a PNG of the size a listing wants.</para>
    /// <para>Read out of the header rather than by decoding: the first eight bytes say it is a
    /// PNG, and the IHDR chunk that must follow them says how big. That is enough to tell a real
    /// picture from an empty file, a half-written one, or a screenshot taken at the wrong window
    /// size — which is what a failed run of the drawing script leaves behind.</para>
    /// </summary>
    [Test]
    public void ThePictureIsAPngOfTheSizeAListingWants()
    {
        Assert.That(File.Exists(PicturePath), Is.True, "run 'node vscode/tools/draw-icon.js'");

        byte[] bytes = File.ReadAllBytes(PicturePath);

        Assert.That(bytes.Length, Is.GreaterThan(24), "too short to be a PNG at all");

        byte[] signature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

        Assert.Multiple(() =>
        {
            Assert.That(bytes[..8], Is.EqualTo(signature), "not a PNG");
            Assert.That(bytes[12..16], Is.EqualTo("IHDR"u8.ToArray()));
            Assert.That(BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16, 4)), Is.EqualTo(Size));
            Assert.That(BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(20, 4)), Is.EqualTo(Size));
        });
    }

    /// <summary>
    /// <para>The picture ships and the drawing does not.</para>
    /// <para>A .vsix is downloaded by everybody who installs, and the drawing is of no use to any
    /// of them. The failure worth catching is the other way round: excluding the picture leaves
    /// the manifest naming a file the package does not contain, which is a broken listing rather
    /// than a missing one.</para>
    /// </summary>
    [Test]
    public void ThePackageCarriesThePictureAndNotTheDrawing()
    {
        string[] excluded = [.. File.ReadAllLines(Path.Combine(Extension, ".vscodeignore"))
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))];

        Assert.Multiple(() =>
        {
            Assert.That(excluded, Does.Contain("icon.svg"));
            Assert.That(excluded, Does.Not.Contain("icon.png"));
        });
    }

    /// <summary>
    /// <para>The drawing says how to redraw the picture from it, and names something that is
    /// there.</para>
    /// <para>This is the one guard against the drift nothing else can see. A reader who changes
    /// the mark finds the instruction in the file they changed; a comment naming a script that
    /// has moved would send them nowhere, silently.</para>
    /// </summary>
    [Test]
    public void TheDrawingSaysHowToRedrawThePicture()
    {
        string drawing = File.ReadAllText(DrawingPath);

        Assert.Multiple(() =>
        {
            Assert.That(drawing, Does.Contain("vscode/tools/draw-icon.js"));
            Assert.That(
                File.Exists(Path.Combine(Extension, "tools", "draw-icon.js")),
                Is.True,
                "icon.svg names a drawing script that is not there");
        });
    }
}
