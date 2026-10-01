using System.Reflection;
using System.Text;
using NUnit.Framework;
using YARG.Core.Audio;
using YARG.Core.IO;
using YARG.Core.Song;
using YARG.Core.Song.Cache;
using YARG.Core.UnitTests.Audio;
using YARG.Core.Venue;

namespace YARG.Core.UnitTests.IO;

public class SongEntryFileSystemTests
{
    // A local-looking root, so the FileInfo stats that still bypass the router (PR 2e) miss fast instead of
    // waiting on a network lookup for an unreachable UNC host.
    private static readonly string ROOT = Path.Combine(Path.GetTempPath(), "yarg-fake-" + Guid.NewGuid().ToString("N"));
    private static readonly string SONG = Path.Combine(ROOT, "song");

    private InMemoryFileSystem _fileSystem = null!;

    [SetUp]
    public void SetUp()
    {
        _fileSystem = new InMemoryFileSystem();
        YARGFileSystem.Register(ROOT, _fileSystem);
    }

    [TearDown]
    public void TearDown()
    {
        YARGFileSystem.Unregister(ROOT);
        GlobalAudioHandler.Close();
    }

    [Test]
    public void UnpackedIni_LoadMiloData_FindsMixedCaseFileInEnumeratedListing()
    {
        string milo = Path.Combine(SONG, "Venue.MILO_XBOX");
        _fileSystem.AddFile(milo, [1, 2, 3]);
        _fileSystem.AddFile(Path.Combine(SONG, "notes.chart"), [0]);

        using var data = CreateIniEntry().LoadMiloData();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(data!.ReadOnlySpan.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3 }));
            Assert.That(_fileSystem.EnumeratedDirectories, Is.EqualTo([SONG]));
            Assert.That(_fileSystem.OpenedPaths, Is.EqualTo([milo]));
        }
    }

    [Test]
    public void UnpackedIni_LoadVocData_ReadsFromRegisteredBackend()
    {
        string voc = Path.Combine(SONG, "song.voc");
        _fileSystem.AddFile(voc, [4, 5]);

        using var data = CreateIniEntry().LoadVocData();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(data!.ReadOnlySpan.ToArray(), Is.EqualTo(new byte[] { 4, 5 }));
            Assert.That(_fileSystem.OpenedPaths, Is.EqualTo([voc]));
        }
    }

    [Test]
    public void UnpackedIni_LoadMiloData_ReturnsNullWhenDirectoryIsMissing()
    {
        _fileSystem.AddDirectory(ROOT);

        Assert.That(CreateIniEntry().LoadMiloData(), Is.Null);
    }

    [Test]
    public void UnpackedIni_LoadBackground_OpensYargroundBeforeVideo()
    {
        string yarground = Path.Combine(SONG, "bg.yarground");
        _fileSystem.AddFile(yarground, [7]);
        _fileSystem.AddFile(Path.Combine(SONG, "background.mp4"), [8]);

        using var result = CreateIniEntry().LoadBackground(enableCensoring: false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result!.Type, Is.EqualTo(BackgroundType.Yarground));
            Assert.That(result.Stream!.ReadByte(), Is.EqualTo(7));
            Assert.That(_fileSystem.OpenedPaths, Is.EqualTo([yarground]));
        }
    }

    [Test]
    public void UnpackedIni_LoadBackground_PrefersCensoredVideo()
    {
        string clean = Path.Combine(SONG, "Background_Clean.WEBM");
        _fileSystem.AddFile(Path.Combine(SONG, "background.webm"), [1]);
        _fileSystem.AddFile(clean, [2]);

        using var result = CreateIniEntry().LoadBackground(enableCensoring: true, excludeYarground: true);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result!.Type, Is.EqualTo(BackgroundType.Video));
            Assert.That(_fileSystem.OpenedPaths, Is.EqualTo([clean]));
        }
    }

    [Test]
    public void UnpackedIni_LoadPreviewAudio_ProbesAndOpensThroughRegisteredBackend()
    {
        GlobalAudioHandler.Initialize<FakeAudioManager>();
        string preview = Path.Combine(SONG, "preview.ogg");
        _fileSystem.AddFile(preview, [0]);

        CreateIniEntry().LoadPreviewAudio(1f, enableCensoring: false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_fileSystem.OpenedPaths, Is.EqualTo([preview]));
            Assert.That(_fileSystem.EnumeratedDirectories, Is.Empty);
        }
    }

    [Test]
    public void UnpackedIni_LoadAudio_OpensStemsThroughRegisteredBackend()
    {
        GlobalAudioHandler.Initialize<StemAudioManager>();
        string song = Path.Combine(SONG, "song.ogg");
        string guitar = Path.Combine(SONG, "Guitar.OPUS");
        _fileSystem.AddFile(song, [0]);
        _fileSystem.AddFile(guitar, [0]);

        CreateIniEntry().LoadAudio(1f, 1.0, enableCensoring: false);

        Assert.That(_fileSystem.OpenedPaths, Is.EqualTo([song, guitar]));
    }

    [Test]
    public void UnpackedCON_LoadVocData_PrefersUpdateDirectory()
    {
        string update = Path.Combine(ROOT, "updates");
        string updateVoc = Path.Combine(update, "mysong", "mysong.voc");
        _fileSystem.AddFile(Path.Combine(ROOT, "mysong", "mysong.voc"), [1]);
        _fileSystem.AddFile(updateVoc, [2]);

        var entry = CreateUnpackedCONEntry();
        typeof(RBCONEntry)
            .GetField("_updateDirectoryAndDtaLastWrite", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(entry, new AbridgedFileInfo(update, DateTime.MinValue));

        using var data = entry.LoadVocData();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(data!.ReadOnlySpan.ToArray(), Is.EqualTo(new byte[] { 2 }));
            Assert.That(_fileSystem.OpenedPaths, Is.EqualTo([updateVoc]));
        }
    }

    [Test]
    public void UnpackedCON_LoadMiloData_ReadsFromRegisteredBackend()
    {
        string milo = Path.Combine(ROOT, "mysong", "gen", "mysong.milo_xbox");
        _fileSystem.AddFile(milo, [3]);

        using var data = CreateUnpackedCONEntry().LoadMiloData();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(data!.ReadOnlySpan.ToArray(), Is.EqualTo(new byte[] { 3 }));
            Assert.That(_fileSystem.OpenedPaths, Is.EqualTo([milo]));
        }
    }

    [Test]
    public void UnpackedCON_LoadBackground_PrefersCensoredVideo()
    {
        string clean = Path.Combine(ROOT, "mysong", "video_clean.mp4");
        _fileSystem.AddFile(Path.Combine(ROOT, "mysong", "video.mp4"), [1]);
        _fileSystem.AddFile(clean, [2]);

        using var result = CreateUnpackedCONEntry().LoadBackground(enableCensoring: true);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result!.Type, Is.EqualTo(BackgroundType.Video));
            Assert.That(_fileSystem.OpenedPaths, Is.EqualTo([clean]));
        }
    }

    [TestCase("mysong.mid", typeof(UnpackedCONEntryGroup))]
    [TestCase("mysong.mid.edat", typeof(UnpackedPKGEntryGroup))]
    public void UnpackedConsolePackageGroup_ProbesMidiThroughRegisteredBackend(string midi, Type expected)
    {
        string dta = Path.Combine(ROOT, "songs.dta");
        _fileSystem.AddFile(dta, Encoding.ASCII.GetBytes("(mysong (song (name \"songs/mysong/mysong\")))"));
        _fileSystem.AddFile(Path.Combine(ROOT, "mysong", midi), [0]);

        bool created = UnpackedConsolePackageEntryGroup.Create(ROOT, new FileInfo(dta), string.Empty, out var group);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(created, Is.True);
            Assert.That(group, Is.TypeOf(expected));
        }
        group.Dispose();
    }

    [Test]
    public void YargMoggReadStream_OpensThroughRegisteredBackend()
    {
        string mogg = Path.Combine(SONG, "song.yarg_mogg");
        _fileSystem.AddFile(mogg, new byte[20]);

        using var stream = new YargMoggReadStream(mogg);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(stream.Length, Is.EqualTo(4));
            Assert.That(_fileSystem.OpenedPaths, Is.EqualTo([mogg]));
        }
    }

    private static UnpackedIniEntry CreateIniEntry()
    {
        var constructor = typeof(UnpackedIniEntry).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance).Single();
        return (UnpackedIniEntry) constructor.Invoke([SONG, DateTime.MinValue, null, YARG.Core.Song.ChartFormat.Chart]);
    }

    private static UnpackedRBCONEntry CreateUnpackedCONEntry()
    {
        var constructor = typeof(UnpackedRBCONEntry).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance).Single();
        var entry = (UnpackedRBCONEntry) constructor.Invoke([new AbridgedFileInfo(ROOT, DateTime.MinValue), "mysong"]);
        typeof(RBCONEntry).GetField("_subName", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(entry, "mysong");
        return entry;
    }

    private sealed class StemAudioManager : FakeAudioManager
    {
        protected internal override StemMixer? CreateMixer(string name, float speed, double volume, bool clampStemVolume, bool normalize)
        {
            return new PreviewContextTests.FakeStemMixer(this, 0);
        }
    }
}
