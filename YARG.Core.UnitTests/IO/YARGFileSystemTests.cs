using NUnit.Framework;
using YARG.Core.IO;

namespace YARG.Core.UnitTests.IO;

public class YARGFileSystemTests
{
    private const string MEDIA = @"\\nas\media";
    private const string ROCK = @"\\nas\media\rock";

    private readonly InMemoryFileSystem _media = new();
    private readonly InMemoryFileSystem _rock = new();

    [TearDown]
    public void TearDown()
    {
        YARGFileSystem.Unregister(MEDIA);
        YARGFileSystem.Unregister(ROCK);
    }

    [Test]
    public void Resolve_WithNoBackends_ReturnsLocal()
    {
        Assert.That(YARGFileSystem.Resolve(@"\\nas\media\song.ini"), Is.SameAs(LocalFileSystem.Instance));
    }

    [TestCase(@"\\nas\media")]
    [TestCase(@"\\nas\media\song\notes.chart")]
    [TestCase(@"\\nas\media/song/notes.chart")]
    [TestCase(@"\\NAS\Media\song\notes.chart")]
    public void Resolve_PathAtOrUnderRoot_ReturnsBackend(string path)
    {
        YARGFileSystem.Register(MEDIA, _media);

        Assert.That(YARGFileSystem.Resolve(path), Is.SameAs(_media));
    }

    [TestCase(@"\\nas\media2\song.ini")]
    [TestCase(@"\\nas\medi")]
    [TestCase(@"\\nas\mediasong.ini")]
    [TestCase(@"C:\nas\media\song.ini")]
    public void Resolve_PathOutsideRoot_ReturnsLocal(string path)
    {
        YARGFileSystem.Register(MEDIA, _media);

        Assert.That(YARGFileSystem.Resolve(path), Is.SameAs(LocalFileSystem.Instance));
    }

    [Test]
    public void Register_IgnoresTrailingSeparator()
    {
        YARGFileSystem.Register(MEDIA + @"\", _media);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(YARGFileSystem.Resolve(@"\\nas\media\song.ini"), Is.SameAs(_media));
            Assert.That(YARGFileSystem.Resolve(@"\\nas\media2\song.ini"), Is.SameAs(LocalFileSystem.Instance));
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Resolve_PrefersLongestRoot(bool registerLongestFirst)
    {
        if (registerLongestFirst)
        {
            YARGFileSystem.Register(ROCK, _rock);
            YARGFileSystem.Register(MEDIA, _media);
        }
        else
        {
            YARGFileSystem.Register(MEDIA, _media);
            YARGFileSystem.Register(ROCK, _rock);
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(YARGFileSystem.Resolve(@"\\nas\media\rock\song.ini"), Is.SameAs(_rock));
            Assert.That(YARGFileSystem.Resolve(@"\\nas\media\rockabilly\song.ini"), Is.SameAs(_media));
            Assert.That(YARGFileSystem.Resolve(@"\\nas\media\pop\song.ini"), Is.SameAs(_media));
        }
    }

    [Test]
    public void Register_SameRoot_ReplacesBackend()
    {
        YARGFileSystem.Register(MEDIA, _media);
        YARGFileSystem.Register(@"\\NAS\MEDIA", _rock);

        Assert.That(YARGFileSystem.Resolve(@"\\nas\media\song.ini"), Is.SameAs(_rock));
    }

    [Test]
    public void Unregister_RestoresLocal()
    {
        YARGFileSystem.Register(MEDIA, _media);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(YARGFileSystem.Unregister(@"\\NAS\media\"), Is.True);
            Assert.That(YARGFileSystem.Unregister(MEDIA), Is.False);
            Assert.That(YARGFileSystem.Resolve(@"\\nas\media\song.ini"), Is.SameAs(LocalFileSystem.Instance));
        }
    }

    [Test]
    public void Unregister_LeavesOtherRoots()
    {
        YARGFileSystem.Register(MEDIA, _media);
        YARGFileSystem.Register(ROCK, _rock);

        YARGFileSystem.Unregister(ROCK);

        Assert.That(YARGFileSystem.Resolve(@"\\nas\media\rock\song.ini"), Is.SameAs(_media));
    }

    [TestCase("")]
    [TestCase(@"\")]
    [TestCase("/")]
    public void Register_EmptyRoot_Throws(string root)
    {
        Assert.Throws<ArgumentException>(() => YARGFileSystem.Register(root, _media));
    }

    [Test]
    public void StaticMembers_ForwardToResolvedBackend()
    {
        _media.AddFile(@"\\nas\media\song\song.ini", [1, 2, 3]);
        YARGFileSystem.Register(MEDIA, _media);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(YARGFileSystem.FileExists(@"\\nas\media\song\song.ini"), Is.True);
            Assert.That(YARGFileSystem.DirectoryExists(@"\\nas\media\song"), Is.True);
            Assert.That(YARGFileSystem.TryStat(@"\\nas\media\song\song.ini", out var stat), Is.True);
            Assert.That(stat.Length, Is.EqualTo(3));
            Assert.That(YARGFileSystem.Enumerate(@"\\nas\media\song").Select(e => e.Name), Is.EqualTo(["song.ini"]));
            using var stream = YARGFileSystem.OpenRead(@"\\nas\media\song\song.ini");
            Assert.That(stream.Length, Is.EqualTo(3));
        }
    }
}
