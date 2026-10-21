using NUnit.Framework;
using YARG.Core.Song.Cache;

namespace YARG.Core.UnitTests.Song;

public class SongPathsTests
{
    [TestCase(@"\\nas\media", @"\\NAS\Media", ExpectedResult = true)]
    [TestCase(@"\\?\UNC\nas\media", @"\\?\unc\NAS\media", ExpectedResult = true)]
    [TestCase(@"\\nas\media", @"\\nas\media2", ExpectedResult = false)]
    [TestCase(@"C:\Songs", @"C:\Songs", ExpectedResult = true)]
    [TestCase(@"C:\Songs", @"C:\songs", ExpectedResult = false)]
    [TestCase("/home/songs", "/home/Songs", ExpectedResult = false)]
    public bool AreEqual_IgnoresCaseOnlyForUncPaths(string lhs, string rhs)
    {
        bool equal = SongPaths.AreEqual(lhs, rhs);
        Assert.That(SongPaths.AreEqual(rhs, lhs), Is.EqualTo(equal));
        Assert.That(SongPaths.Comparer.Equals(lhs, rhs), Is.EqualTo(equal));
        if (equal)
        {
            Assert.That(SongPaths.Comparer.GetHashCode(lhs), Is.EqualTo(SongPaths.Comparer.GetHashCode(rhs)));
        }
        return equal;
    }

    [Test]
    public void Comparer_KeepsLocalSpellingsApartAndMergesUncOnes()
    {
        var set = new HashSet<string>(SongPaths.Comparer)
        {
            @"C:\Songs\a", @"C:\songs\a", @"\\nas\share\a", @"\\NAS\Share\A",
        };

        Assert.That(set, Has.Count.EqualTo(3));
    }

    [TestCase(@"\\nas\share\Songs\a\b", @"\\nas\share\Songs", ExpectedResult = true)]
    [TestCase(@"\\nas\share\songs\a", @"\\NAS\Share\Songs", ExpectedResult = true)]
    [TestCase(@"\\nas\share\Songs", @"\\nas\share\Songs", ExpectedResult = true)]
    [TestCase(@"\\nas\share\Songs2\a", @"\\nas\share\Songs", ExpectedResult = false)]
    [TestCase(@"\\nas\share\a", @"\\nas\share", ExpectedResult = true)]
    [TestCase(@"\\nas\share\a", @"\\nas\share\", ExpectedResult = true)]
    [TestCase(@"\\nas\share2\a", @"\\nas\share", ExpectedResult = false)]
    [TestCase(@"\\nas\share/a", @"\\nas\share", ExpectedResult = true)]
    [TestCase(@"\\nas\share\a", "", ExpectedResult = false)]
    public bool IsUnderDirectory_UncPaths(string path, string directory)
    {
        return SongPaths.IsUnderDirectory(path, directory);
    }

    [Test]
    [Platform("Win")]
    public void IsUnderDirectory_LocalWindowsPaths()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(SongPaths.IsUnderDirectory(@"C:\Songs\a", @"C:\Songs"), Is.True);
            Assert.That(SongPaths.IsUnderDirectory(@"C:\Songs", @"C:\Songs"), Is.True);
            Assert.That(SongPaths.IsUnderDirectory(@"C:\Songs2\a", @"C:\Songs"), Is.False);
            Assert.That(SongPaths.IsUnderDirectory(@"C:\songs\a", @"C:\Songs"), Is.False, "local paths keep their ordinal, case-sensitive match");
            Assert.That(SongPaths.IsUnderDirectory(@"C:\a", @"C:\"), Is.True);
        }
    }

    [TestCase(@"\\nas\share", @"\\nas\share\a\b", ExpectedResult = @"a\b")]
    [TestCase(@"\\nas\share\", @"\\nas\share\a", ExpectedResult = "a")]
    [TestCase(@"\\NAS\Share\Songs", @"\\nas\share\songs\Song", ExpectedResult = "Song")]
    [TestCase(@"\\nas\share\Songs", @"\\nas\share\Songs", ExpectedResult = "")]
    [TestCase(@"\\nas\share\Songs\", @"\\nas\share\Songs\", ExpectedResult = "")]
    public string TryGetRelativePath_SlicesBelowTheDirectory(string directory, string path)
    {
        Assert.That(SongPaths.TryGetRelativePath(directory, path, out string relative), Is.True);
        return relative;
    }

    [Test]
    public void TryGetRelativePath_RejectsPathsOutsideTheDirectory()
    {
        Assert.That(SongPaths.TryGetRelativePath(@"\\nas\share\Songs", @"\\nas\share\Songs2\a", out _), Is.False);
    }

    /// <summary>
    /// Documents what the cache relied on before UNC folders got their own slicing: .NET on Windows already
    /// handles UNC roots in Path.GetRelativePath, and SongPaths agrees with it there.
    /// </summary>
    [TestCase(@"\\nas\share", @"\\nas\share\a\b")]
    [TestCase(@"\\nas\share\", @"\\nas\share\a")]
    [TestCase(@"\\NAS\Share\Songs", @"\\nas\share\songs\Song")]
    [Platform("Win")]
    public void TryGetRelativePath_MatchesPathGetRelativePathOnWindows(string directory, string path)
    {
        Assert.That(SongPaths.TryGetRelativePath(directory, path, out string relative), Is.True);
        Assert.That(relative, Is.EqualTo(Path.GetRelativePath(directory, path)));
    }

    [TestCase(@"\\nas\share\", ExpectedResult = @"\\nas\share")]
    [TestCase(@"\\nas\share\\", ExpectedResult = @"\\nas\share")]
    [TestCase(@"\\nas\share", ExpectedResult = @"\\nas\share")]
    public string TrimEndSeparators_UncPaths(string path)
    {
        return SongPaths.TrimEndSeparators(path);
    }
}
