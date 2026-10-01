using System.Text;
using NUnit.Framework;
using YARG.Core.IO;
using YARG.Core.Song;
using YARG.Core.Song.Cache;
using YARG.Core.UnitTests.IO;

namespace YARG.Core.UnitTests.Scanning;

/// <summary>
/// Runs the whole scanner against a song folder that exists only in a registered <see cref="InMemoryFileSystem"/>,
/// so every enumeration, stat and open the scan needs has to go through <see cref="YARGFileSystem"/>.
/// </summary>
public class FileSystemScanTests
{
    private const string ROOT = @"\\fake\scan";
    private const int MIDI_BLOCK = 0;

    private static readonly string INI_DIRECTORY = Path.Combine(ROOT, "Ini Song");
    private static readonly string INI_CHART = Path.Combine(INI_DIRECTORY, "notes.mid");
    private static readonly string SNG = Path.Combine(ROOT, "Packed", "song.sng");
    private static readonly string CON = Path.Combine(ROOT, "Cons", "pack");
    private static readonly string UNPACKED = Path.Combine(ROOT, "Unpacked", "songs");
    private static readonly string UNPACKED_MIDI = Path.Combine(UNPACKED, "consong", "consong.mid");

    private InMemoryFileSystem _fileSystem = null!;
    private string _output = null!;
    private string _cache = null!;
    private string _badSongs = null!;

    [SetUp]
    public void SetUp()
    {
        byte[] midi = File.ReadAllBytes(GetTestMidiPath());

        _fileSystem = new InMemoryFileSystem();
        _fileSystem.AddFile(Path.Combine(INI_DIRECTORY, "song.ini"),
            Encoding.UTF8.GetBytes("[song]\nname = Ini Song\nartist = Ini Artist\nsong_length = 128000\n"));
        _fileSystem.AddFile(INI_CHART, midi);
        _fileSystem.AddFile(Path.Combine(INI_DIRECTORY, "song.opus"), [0]);

        _fileSystem.AddFile(SNG, CreateSng(midi));

        _fileSystem.AddFile(CON, CreateCon(midi));

        _fileSystem.AddFile(Path.Combine(UNPACKED, "songs.dta"), Encoding.UTF8.GetBytes(CreateDta("consong", "Unpacked Song")));
        _fileSystem.AddFile(UNPACKED_MIDI, midi);
        _fileSystem.AddFile(Path.Combine(UNPACKED, "consong", "consong.mogg"), CreateMogg());

        YARGFileSystem.Register(ROOT, _fileSystem);

        _output = Path.Combine(Path.GetTempPath(), "YARG.Core.UnitTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_output);
        _cache = Path.Combine(_output, "songcache.bin");
        _badSongs = Path.Combine(_output, "badsongs.txt");
    }

    [TearDown]
    public void TearDown()
    {
        YARGFileSystem.Unregister(ROOT);
        Directory.Delete(_output, recursive: true);
    }

    [Test]
    public void FullScan_FindsEverySongTypeThroughRegisteredBackend()
    {
        var cache = CacheHandler.RunScan(false, _cache, _badSongs, false, [ROOT]);

        var entries = Flatten(cache);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(entries.Select(entry => entry.Name.ToString()),
                Is.EquivalentTo(new[] { "Ini Song", "Sng Song", "Packed Song", "Unpacked Song" }),
                File.Exists(_badSongs) ? File.ReadAllText(_badSongs) : "no badsongs.txt");
            Assert.That(entries.Single(entry => entry.Name.ToString() == "Ini Song").SubType, Is.EqualTo(EntryType.Ini));
            Assert.That(entries.Single(entry => entry.Name.ToString() == "Sng Song").SubType, Is.EqualTo(EntryType.Sng));
            Assert.That(entries.Single(entry => entry.Name.ToString() == "Packed Song").SubType, Is.EqualTo(EntryType.CON));
            Assert.That(entries.Single(entry => entry.Name.ToString() == "Unpacked Song").SubType, Is.EqualTo(EntryType.ExCON));
            Assert.That(_fileSystem.EnumeratedDirectories, Does.Contain(ROOT));
            Assert.That(_fileSystem.OpenedPaths, Does.Contain(INI_CHART));
            Assert.That(_fileSystem.OpenedPaths, Does.Contain(SNG));
            Assert.That(_fileSystem.OpenedPaths, Does.Contain(CON));
            Assert.That(_fileSystem.OpenedPaths, Does.Contain(UNPACKED_MIDI));
            Assert.That(File.Exists(_cache), Is.True);
            Assert.That(File.Exists(_badSongs), Is.False);
        }
    }

    [Test]
    public void SecondFullScan_ValidatesCachedIniEntryWithoutReparsingItsChart()
    {
        var first = Flatten(CacheHandler.RunScan(false, _cache, _badSongs, false, [ROOT]));
        byte[] firstCache = File.ReadAllBytes(_cache);
        _fileSystem.OpenedPaths.Clear();

        var second = Flatten(CacheHandler.RunScan(false, _cache, _badSongs, false, [ROOT]));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Has.Count.EqualTo(4));
            Assert.That(second.Select(entry => entry.Name.ToString()), Is.EquivalentTo(first.Select(entry => entry.Name.ToString())));
            Assert.That(_fileSystem.OpenedPaths, Does.Not.Contain(INI_CHART), "the ini entry should come from the cache");
            Assert.That(File.ReadAllBytes(_cache), Has.Length.EqualTo(firstCache.Length));
        }
    }

    [Test]
    public void QuickScan_RoundTripsTheCache()
    {
        var full = Flatten(CacheHandler.RunScan(false, _cache, _badSongs, false, [ROOT]));
        _fileSystem.OpenedPaths.Clear();
        _fileSystem.StattedPaths.Clear();

        var quick = Flatten(CacheHandler.RunScan(true, _cache, _badSongs, false, [ROOT]));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(full, Has.Count.EqualTo(4));
            Assert.That(quick.Select(entry => (entry.Name.ToString(), entry.SubType, entry.Hash)),
                Is.EquivalentTo(full.Select(entry => (entry.Name.ToString(), entry.SubType, entry.Hash))));
            Assert.That(_fileSystem.StattedPaths, Is.Empty, "a quick scan trusts the cache");
        }
    }

    [Test]
    public void FullScan_ChangedChartMtimeRescansTheEntry()
    {
        CacheHandler.RunScan(false, _cache, _badSongs, false, [ROOT]);
        _fileSystem.AddFile(INI_CHART, File.ReadAllBytes(GetTestMidiPath()), InMemoryFileSystem.DEFAULT_TIME.AddMinutes(1));
        _fileSystem.OpenedPaths.Clear();

        var second = Flatten(CacheHandler.RunScan(false, _cache, _badSongs, false, [ROOT]));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(second, Has.Count.EqualTo(4));
            Assert.That(_fileSystem.OpenedPaths, Does.Contain(INI_CHART));
        }
    }

    private static List<SongEntry> Flatten(SongCache cache)
    {
        return cache.Entries.Values.SelectMany(list => list).ToList();
    }

    private static string CreateDta(string nodeName, string songName)
    {
        return $$"""
                 ({{nodeName}}
                   (name "{{songName}}")
                   (artist "Con Artist")
                   (song
                     (name "songs/{{nodeName}}/{{nodeName}}")
                     (pans (0.0))
                     (vols (0.0))
                     (cores (0.0))
                   )
                 )
                 """;
    }

    private static byte[] CreateMogg()
    {
        byte[] mogg = new byte[CONFileStream.BYTES_PER_BLOCK];
        BitConverter.GetBytes(RBCONEntry.UNENCRYPTED_MOGG).CopyTo(mogg, 0);
        return mogg;
    }

    private static byte[] CreateSng(byte[] midi)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write("SNGPKG"u8);
        writer.Write(1u);
        // All-zero keys make the mask byte for file position i equal to (byte) i
        writer.Write(new byte[16]);

        // A known song_length keeps the scan from measuring the audio, which needs an audio backend
        var metadata = new (byte[] Key, byte[] Value)[] { ("name"u8.ToArray(), "Sng Song"u8.ToArray()), ("song_length"u8.ToArray(), "128000"u8.ToArray()) };
        long metadataLength = sizeof(ulong);
        foreach (var (key, value) in metadata)
        {
            metadataLength += sizeof(int) + key.Length + sizeof(int) + value.Length;
        }
        writer.Write(metadataLength);
        writer.Write((ulong) metadata.Length);
        foreach (var (key, value) in metadata)
        {
            writer.Write(key.Length);
            writer.Write(key);
            writer.Write(value.Length);
            writer.Write(value);
        }

        var files = new (string Name, byte[] Data)[] { ("notes.mid", midi), ("song.opus", [0]) };
        long indexLength = sizeof(ulong);
        foreach (var file in files)
        {
            indexLength += 1 + Encoding.UTF8.GetByteCount(file.Name) + 2 * sizeof(long);
        }
        writer.Write(indexLength);
        writer.Write((ulong) files.Length);

        long position = stream.Position + indexLength - sizeof(ulong) + sizeof(long);
        foreach (var file in files)
        {
            byte[] name = Encoding.UTF8.GetBytes(file.Name);
            writer.Write((byte) name.Length);
            writer.Write(name);
            writer.Write((long) file.Data.Length);
            writer.Write(position);
            position += file.Data.Length;
        }

        long dataLength = 0;
        foreach (var file in files)
        {
            dataLength += file.Data.Length;
        }
        writer.Write(dataLength);

        foreach (var file in files)
        {
            for (int i = 0; i < file.Data.Length; ++i)
            {
                writer.Write((byte) (file.Data[i] ^ i));
            }
        }

        writer.Flush();
        return stream.ToArray();
    }

    /// <summary>
    /// A CON holding songs/songs.dta and one song's midi and mogg, each stored in consecutive blocks
    /// </summary>
    private static byte[] CreateCon(byte[] midi)
    {
        byte[] dta = Encoding.UTF8.GetBytes(CreateDta("mysong", "Packed Song"));
        byte[] mogg = CreateMogg();

        int midiBlocks = BlockCount(midi.Length);
        int dtaBlock = MIDI_BLOCK + midiBlocks;
        int moggBlock = dtaBlock + BlockCount(dta.Length);
        int tableBlock = moggBlock + BlockCount(mogg.Length);

        byte[] image = new byte[CONFileStream.CalculateBlockLocation(tableBlock, 0) + CONFileStream.BYTES_PER_BLOCK];
        "CON "u8.CopyTo(image);
        // Entry ID 0xB000 selects the single-hash-table layout (shift 0)
        image[0x340] = 0x00;
        image[0x341] = 0x00;
        image[0x342] = 0xB0;
        image[0x343] = 0x00;
        image[0x37C] = 1;
        image[0x37E] = (byte) (tableBlock >> 16);
        image[0x37F] = (byte) (tableBlock >> 8);
        image[0x380] = (byte) tableBlock;

        WriteBlocks(image, MIDI_BLOCK, midi);
        WriteBlocks(image, dtaBlock, dta);
        WriteBlocks(image, moggBlock, mogg);

        var table = image.AsSpan((int) CONFileStream.CalculateBlockLocation(tableBlock, 0), CONFileStream.BYTES_PER_BLOCK);
        const int LISTING = 0x40;
        WriteListing(table[..LISTING], "songs", CONFileListing.Flag.Directory, 0, 0, -1, 0);
        WriteListing(table.Slice(LISTING, LISTING), "songs.dta", CONFileListing.Flag.Consecutive,
            BlockCount(dta.Length), dtaBlock, 0, dta.Length);
        WriteListing(table.Slice(2 * LISTING, LISTING), "mysong", CONFileListing.Flag.Directory, 0, 0, 0, 0);
        WriteListing(table.Slice(3 * LISTING, LISTING), "mysong.mid", CONFileListing.Flag.Consecutive,
            midiBlocks, MIDI_BLOCK, 2, midi.Length);
        WriteListing(table.Slice(4 * LISTING, LISTING), "mysong.mogg", CONFileListing.Flag.Consecutive,
            BlockCount(mogg.Length), moggBlock, 2, mogg.Length);
        return image;
    }

    private static int BlockCount(int length)
    {
        return (length + CONFileStream.BYTES_PER_BLOCK - 1) / CONFileStream.BYTES_PER_BLOCK;
    }

    private static void WriteBlocks(byte[] image, int firstBlock, byte[] data)
    {
        for (int offset = 0, block = firstBlock; offset < data.Length; offset += CONFileStream.BYTES_PER_BLOCK, ++block)
        {
            int count = Math.Min(CONFileStream.BYTES_PER_BLOCK, data.Length - offset);
            data.AsSpan(offset, count).CopyTo(image.AsSpan((int) CONFileStream.CalculateBlockLocation(block, 0)));
        }
    }

    private static void WriteListing(Span<byte> destination, string name, CONFileListing.Flag flags,
        int blockCount, int blockOffset, short pathIndex, int length)
    {
        byte[] nameBytes = Encoding.UTF8.GetBytes(name);
        nameBytes.CopyTo(destination);
        destination[0x28] = (byte) (((byte) flags) | nameBytes.Length);

        destination[0x29] = (byte) blockCount;
        destination[0x2A] = (byte) (blockCount >> 8);
        destination[0x2B] = (byte) (blockCount >> 16);

        destination[0x2F] = (byte) blockOffset;
        destination[0x30] = (byte) (blockOffset >> 8);
        destination[0x31] = (byte) (blockOffset >> 16);

        destination[0x32] = (byte) (pathIndex >> 8);
        destination[0x33] = (byte) pathIndex;

        destination[0x34] = (byte) (length >> 24);
        destination[0x35] = (byte) (length >> 16);
        destination[0x36] = (byte) (length >> 8);
        destination[0x37] = (byte) length;
    }

    private static string GetTestMidiPath()
    {
        return Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory,
            "../../../../Parsing/Test Charts/test.mid"));
    }
}
