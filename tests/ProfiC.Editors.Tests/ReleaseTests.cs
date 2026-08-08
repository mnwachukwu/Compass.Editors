using System.Text.Json;

namespace ProfiC.Editors.Tests;

/// <summary>
/// <para>Holds what publishing the extension is allowed to be.</para>
/// <para><b>A published version is permanent.</b> The Marketplace refuses any version at or below
/// one already there — it can be unlisted, never replaced — so everything that could have been
/// checked has to be checked before the upload rather than after. That makes the release pipeline
/// itself worth holding to: it is the only thing standing between a failing test and a version
/// nobody can take back.</para>
/// </summary>
[TestFixture]
public sealed class ReleaseTests : EditorTestBase
{
    private static string WorkflowPath =>
        Path.Combine(RepositoryRoot, ".github", "workflows", "release.yml");

    private static string Workflow => File.ReadAllText(WorkflowPath);

    [Test]
    public void TheReleaseWorkflowIsThere() =>
        Assert.That(File.Exists(WorkflowPath), Is.True, $"{WorkflowPath} is what publishes this");

    /// <summary>
    /// A tag is what publishes, and nothing else is.
    ///
    /// The failure this prevents is a release on every push to main, which would spend the
    /// version number in the manifest the first time anybody merged anything — and spend it
    /// permanently.
    /// </summary>
    [Test]
    public void OnlyATagPublishes()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Workflow, Does.Contain("tags: ['v*']"));
            Assert.That(
                Workflow,
                Does.Not.Contain("branches:"),
                "a release built on a push to a branch is a release nobody chose to make");
        });
    }

    /// <summary>
    /// <para>Nothing is published that CI has not passed, on every system CI covers.</para>
    /// <para>Called rather than repeated, so that the bar for a release is the bar for a merge
    /// and stays that way when the bar moves. A release job that built and tested inline would
    /// look equivalent and quietly check one operating system.</para>
    /// </summary>
    [Test]
    public void NothingIsPublishedUntilCiHasPassedOnBothSystems()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Workflow, Does.Contain("uses: ./.github/workflows/build.yml"));
            Assert.That(Workflow, Does.Contain("os: ubuntu-latest"));
            Assert.That(Workflow, Does.Contain("os: windows-latest"));
            Assert.That(
                Workflow,
                Does.Contain("needs: [verify-linux, verify-windows, token]"),
                "the publish job does not wait for all three, so one failing publishes anyway");
        });
    }

    /// <summary>
    /// <para>Whether the token still works is asked before anything is built.</para>
    /// <para>It expires, and an expired one refuses with a bare 401 naming no cause. Asked in the
    /// publish step, that answer arrives after two full CI runs and against a tag that has
    /// already been pushed — and a tag cannot be pushed twice. Asked alongside them, it arrives
    /// in about ten seconds, next to the reason.</para>
    /// </summary>
    [Test]
    public void TheTokenIsCheckedBeforeAnythingIsBuilt()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Workflow, Does.Contain("vsce verify-pat"));
            Assert.That(
                Workflow,
                Does.Not.Contain("needs: [verify-linux, verify-windows]\n"),
                "the token job runs after the build, which is the waiting this exists to avoid");
        });
    }

    /// <summary>
    /// The file that was tested is the file that is uploaded.
    ///
    /// Packaging once and publishing that package, rather than letting `vsce publish` build its
    /// own: two builds of the same commit should be identical, and where they are not, the one
    /// on the Marketplace would be the one nothing had looked at.
    /// </summary>
    [Test]
    public void WhatIsPublishedIsWhatWasPackaged()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Workflow, Does.Contain("vsce package --out profi-c.vsix"));
            Assert.That(Workflow, Does.Contain("vsce publish --packagePath profi-c.vsix"));
        });
    }

    /// <summary>
    /// <para>The version being published has something in the changelog.</para>
    /// <para>A version bump is one line in a manifest and easy to make on its own. The reader who
    /// then finds the release has nothing to read about what changed, and by then it is
    /// published.</para>
    /// </summary>
    [Test]
    public void TheChangelogNamesTheVersionInTheManifest()
    {
        string changelog = Path.Combine(Extension, "CHANGELOG.md");

        Assert.That(File.Exists(changelog), Is.True, "a release with no changelog says nothing");

        using JsonDocument manifest =
            JsonDocument.Parse(File.ReadAllText(Path.Combine(Extension, "package.json")));

        string version = manifest.RootElement.GetProperty("version").GetString()!;

        Assert.That(
            File.ReadAllText(changelog),
            Does.Contain(version),
            $"{version} is about to be published and the changelog does not mention it");
    }
}
