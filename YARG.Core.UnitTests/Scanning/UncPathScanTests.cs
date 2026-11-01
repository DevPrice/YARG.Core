using System.Text;
using NUnit.Framework;
using YARG.Core.IO;
using YARG.Core.Song;
using YARG.Core.Song.Cache;
using YARG.Core.UnitTests.IO;
using static YARG.Core.UnitTests.Scanning.FileSystemScanTests;

namespace YARG.Core.UnitTests.Scanning;

/// <summary>
/// Scans song folders given as UNC paths, spelled the ways a user or the cache can spell them.
/// </summary>
public class UncPathScanTests
{
    private const string SHARE = @"\\fake\share";
    private const string SONGS = SHARE + @"\Songs";
    private const string INI_CHART = SONGS + @"\Ini Song\notes.mid";
    private const string CON = SONGS + @"\Cons\Sub\pack";

    private InMemoryFileSystem _fileSystem = null!;
    private readonly List<string> _roots = [];
    private string _output = null!;
    private string _cache = null!;
    private string _badSongs = null!;

    [SetUp]
    public void SetUp()
    {
        byte[] midi = File.ReadAllBytes(GetTestMidiPath());

        _fileSystem = new InMemoryFileSystem();
        _fileSystem.AddFile(SONGS + @"\Ini Song\song.ini",
            Encoding.UTF8.GetBytes("[song]\nname = Ini Song\nartist = Ini Artist\nsong_length = 128000\n"));
        _fileSystem.AddFile(INI_CHART, midi);
        _fileSystem.AddFile(SONGS + @"\Ini Song\song.opus", [0]);
        _fileSystem.AddFile(SONGS + @"\Packed\song.sng", CreateSng(midi));
        _fileSystem.AddFile(CON, CreateCon(midi));
        Register(SHARE, _fileSystem);

        _output = Path.Combine(Path.GetTempPath(), "YARG.Core.UnitTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_output);
        _cache = Path.Combine(_output, "songcache.bin");
        _badSongs = Path.Combine(_output, "badsongs.txt");
    }

    [TearDown]
    public void TearDown()
    {
        foreach (string root in _roots)
        {
            YARGFileSystem.Unregister(root);
        }
        _roots.Clear();
        Directory.Delete(_output, recursive: true);
    }

    [TestCase(SHARE)]
    [TestCase(SHARE + @"\")]
    [TestCase(SONGS)]
    [TestCase(SONGS + @"\")]
    public void SongFolder_RoundTripsThroughTheCache(string folder)
    {
        var full = Names(CacheHandler.RunScan(false, _cache, _badSongs, false, [folder]));
        _fileSystem.OpenedPaths.Clear();
        var rescan = Names(CacheHandler.RunScan(false, _cache, _badSongs, false, [folder]));
        bool chartReopened = _fileSystem.OpenedPaths.Contains(INI_CHART, StringComparer.OrdinalIgnoreCase);
        var quick = Names(CacheHandler.RunScan(true, _cache, _badSongs, false, [folder]));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(full, Is.EquivalentTo(new[] { "Ini Song", "Sng Song", "Packed Song" }), BadSongs());
            Assert.That(rescan, Is.EquivalentTo(full));
            Assert.That(chartReopened, Is.False, "the ini entry should come from the cache");
            Assert.That(quick, Is.EquivalentTo(full));
        }
    }

    [Test]
    public void MixedCaseSpellingsOfOneFolder_ScanItOnce()
    {
        var entries = Flatten(CacheHandler.RunScan(false, _cache, _badSongs, false, [@"\\FAKE\share\songs", @"\\fake\Share\Songs"]));

        Assert.That(entries.Select(entry => entry.Name.ToString()), Is.EquivalentTo(new[] { "Ini Song", "Sng Song", "Packed Song" }));
    }

    [Test]
    public void FolderInsideAnotherSpelledDifferently_ScansItOnce()
    {
        var entries = Flatten(CacheHandler.RunScan(false, _cache, _badSongs, false, [SHARE, @"\\FAKE\SHARE\SONGS"]));

        Assert.That(entries.Select(entry => entry.Name.ToString()), Is.EquivalentTo(new[] { "Ini Song", "Sng Song", "Packed Song" }));
    }

    [Test]
    public void CacheWrittenUnderOneSpelling_IsReadUnderAnother()
    {
        CacheHandler.RunScan(false, _cache, _badSongs, false, [SONGS]);
        _fileSystem.OpenedPaths.Clear();

        var entries = Flatten(CacheHandler.RunScan(false, _cache, _badSongs, false, [@"\\FAKE\SHARE\SONGS"]));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(entries.Select(entry => entry.Name.ToString()), Is.EquivalentTo(new[] { "Ini Song", "Sng Song", "Packed Song" }));
            Assert.That(_fileSystem.OpenedPaths, Has.None.EqualTo(INI_CHART).IgnoreCase, "the ini entry should come from the cache");
        }
    }

    [TestCase(SHARE, false, "Sub")]
    [TestCase(SHARE + @"\", false, "Sub")]
    [TestCase(SHARE, true, @"Songs\Cons\Sub")]
    [TestCase(SHARE + @"\", true, @"Songs\Cons\Sub")]
    [TestCase(SONGS, false, "Sub")]
    [TestCase(SONGS, true, @"Cons\Sub")]
    [TestCase(SONGS + @"\", true, @"Cons\Sub")]
    [TestCase(@"\\FAKE\SHARE\SONGS", true, @"Cons\Sub")]
    public void ConPlaylist_FromTheCacheReaderMatchesTheWalk(string folder, bool fullDirectoryPlaylists, string playlist)
    {
        string walked = PackedSongPlaylist(CacheHandler.RunScan(false, _cache, _badSongs, fullDirectoryPlaylists, [folder]));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(walked, Is.EqualTo(playlist));
            Assert.That(CacheHandler.ConstructPlaylist(CON, folder, fullDirectoryPlaylists), Is.EqualTo(playlist));
        }
    }

    [TestCase(SHARE)]
    [TestCase(SHARE + @"\")]
    public void ConPlaylist_AtAShareRootIsUnknown(string folder)
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(CacheHandler.ConstructPlaylist(SHARE + @"\pack", folder, false), Is.EqualTo("Unknown Playlist"));
            Assert.That(CacheHandler.ConstructPlaylist(SHARE + @"\pack", folder, true), Is.EqualTo("Unknown Playlist"));
        }
    }

    [Test]
    [Platform("Win")]
    public void ConPlaylist_UnderADriveRoot()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(CacheHandler.ConstructPlaylist(@"C:\pack", @"C:\", true), Is.EqualTo("Unknown Playlist"));
            Assert.That(CacheHandler.ConstructPlaylist(@"C:\Cons\Sub\pack", @"C:\", true), Is.EqualTo(@"Cons\Sub"));
            Assert.That(CacheHandler.ConstructPlaylist(@"C:\Songs\Cons\pack", @"C:\Songs", true), Is.EqualTo("Cons"));
            Assert.That(CacheHandler.ConstructPlaylist(@"C:\Songs\pack", @"C:\Songs", true), Is.EqualTo("Unknown Playlist"));
        }
    }

    [TestCase(@"\\fake\songs")]
    [TestCase(@"\\fake\songs\")]
    public void ShareNamedSongs_IsNotTakenForAConsoleSongsFolder(string folder)
    {
        var songsShare = new InMemoryFileSystem();
        songsShare.AddFile(@"\\fake\songs\songs.dta", Encoding.UTF8.GetBytes(CreateDta("consong", "Unpacked Song")));
        songsShare.AddFile(@"\\fake\songs\Ini Song\song.ini",
            Encoding.UTF8.GetBytes("[song]\nname = Ini Song\nsong_length = 128000\n"));
        songsShare.AddFile(@"\\fake\songs\Ini Song\notes.mid", File.ReadAllBytes(GetTestMidiPath()));
        songsShare.AddFile(@"\\fake\songs\Ini Song\song.opus", [0]);
        Register(@"\\fake\songs", songsShare);

        var entries = Flatten(CacheHandler.RunScan(false, _cache, _badSongs, false, [folder]));

        Assert.That(entries.Select(entry => entry.Name.ToString()), Is.EqualTo(new[] { "Ini Song" }));
    }

    private void Register(string root, InMemoryFileSystem fileSystem)
    {
        YARGFileSystem.Register(root, fileSystem);
        _roots.Add(root);
    }

    private string BadSongs()
    {
        return File.Exists(_badSongs) ? File.ReadAllText(_badSongs) : "no badsongs.txt";
    }

    private static List<string> Names(SongCache cache)
    {
        return Flatten(cache).Select(entry => entry.Name.ToString()).ToList();
    }

    private static string PackedSongPlaylist(SongCache cache)
    {
        return Flatten(cache).Single(entry => entry.Name.ToString() == "Packed Song").Playlist.ToString();
    }
}
