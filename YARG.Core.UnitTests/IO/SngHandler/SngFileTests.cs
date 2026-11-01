using System.Text;
using NUnit.Framework;
using YARG.Core.IO;
using YARG.Core.Song;

namespace YARG.Core.UnitTests.IO.SngHandler;

public class SngFileTests
{
    private const uint Version = 7;
    private const string ListingName = "notes.chart";
    private const int YargSongHeaderSize = 24;
    private const string FakeRoot = @"\\fake\songs";

    private static readonly byte[] Payload = "sng-payload"u8.ToArray();

    [Test]
    public void YARGSongFileStream_DecryptsFromMemoryStream()
    {
        byte[] plain = CreateSng();
        byte[] wrapped = WrapInYargSong(plain);

        var memory = new MemoryStream(wrapped);
        Assert.That(YARGSongFileStream.TryLoad(memory, out var yargStream), Is.True);
        using (yargStream)
        {
            Assert.That(yargStream.Length, Is.EqualTo(plain.Length));
            Assert.That(ReadAll(yargStream), Is.EqualTo(plain));
        }

        Assert.That(memory.CanRead, Is.False, "YARGSongFileStream should dispose the stream it wraps");
    }

    [Test]
    public void YARGSongFileStream_RejectsUnwrappedStreamWithoutTakingOwnership()
    {
        using var memory = new MemoryStream(CreateSng());
        Assert.That(YARGSongFileStream.TryLoad(memory, out _), Is.False);
        Assert.That(memory.CanRead, Is.True);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void TryLoadFromFile_LoadsAndReleasesFileHandle(bool yargSongWrapped)
    {
        byte[] plain = CreateSng();
        string path = CreateTempFile(yargSongWrapped ? WrapInYargSong(plain) : plain);
        try
        {
            using (var sng = SngFile.TryLoadFromFile(path, true))
            {
                Assert.That(sng.IsLoaded, Is.True);
                Assert.That(sng.Version, Is.EqualTo(Version));
                Assert.That(sng.TryGetListing(ListingName, out var listing), Is.True);
                using var data = sng.LoadAllBytes(listing);
                Assert.That(data.ReadOnlySpan.ToArray(), Is.EqualTo(Payload));
            }

            AssertNoOpenHandles(path);
            Assert.That(SngFile.ValidateMatch(path, Version), Is.True);
            AssertNoOpenHandles(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void TryLoadFromFile_ReleasesFileHandleWhenNotAnSng()
    {
        string path = CreateTempFile("not an sng file"u8.ToArray());
        try
        {
            using (var sng = SngFile.TryLoadFromFile(path, false))
            {
                Assert.That(sng.IsLoaded, Is.False);
            }
            AssertNoOpenHandles(path);

            Assert.That(SngFile.ValidateMatch(path, Version), Is.False);
            AssertNoOpenHandles(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void TryLoadFromFile_ReleasesFileHandleWhenYargSongIsTruncated()
    {
        byte[] wrapped = WrapInYargSong(CreateSng());
        string path = CreateTempFile(wrapped[..(YargSongHeaderSize + 8)]);
        try
        {
            Assert.That(() => SngFile.TryLoadFromFile(path, true), Throws.InstanceOf<EndOfStreamException>());
            AssertNoOpenHandles(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestCase(true)]
    [TestCase(false)]
    public void TryLoadFromFile_ReadsThroughRegisteredFileSystem(bool yargSongWrapped)
    {
        const string PATH = FakeRoot + @"\song.sng";
        byte[] plain = CreateSng();
        var fileSystem = new InMemoryFileSystem();
        fileSystem.AddFile(PATH, yargSongWrapped ? WrapInYargSong(plain) : plain);
        YARGFileSystem.Register(FakeRoot, fileSystem);
        try
        {
            using (var sng = SngFile.TryLoadFromFile(PATH, true))
            {
                Assert.That(sng.IsLoaded, Is.True);
                Assert.That(sng.Version, Is.EqualTo(Version));
                Assert.That(sng.TryGetListing(ListingName, out var listing), Is.True);
                using var data = sng.LoadAllBytes(listing);
                Assert.That(data.ReadOnlySpan.ToArray(), Is.EqualTo(Payload));
            }

            Assert.That(SngFile.ValidateMatch(PATH, Version), Is.True);
            Assert.That(SngFile.ValidateMatch(PATH, Version + 1), Is.False);
            Assert.That(fileSystem.OpenedPaths, Is.EqualTo([PATH, PATH, PATH]));
        }
        finally
        {
            YARGFileSystem.Unregister(FakeRoot);
        }
    }

    [Test]
    public void YARGSongFileStream_CloneReopensThroughRegisteredFileSystem()
    {
        const string PATH = FakeRoot + @"\song.sng";
        byte[] plain = CreateSng();
        var fileSystem = new InMemoryFileSystem();
        fileSystem.AddFile(PATH, WrapInYargSong(plain));
        YARGFileSystem.Register(FakeRoot, fileSystem);
        try
        {
            Assert.That(YARGSongFileStream.TryLoad(YARGFileSystem.OpenRead(PATH), PATH, out var yargStream), Is.True);
            using (yargStream)
            {
                using (var clone = yargStream.Clone())
                {
                    Assert.That(ReadAll(clone), Is.EqualTo(plain));
                }
                using (var cloneOfClone = new YARGSongFileStream(PATH, [0, 0, 0, 0]).Clone())
                {
                    Assert.That(cloneOfClone.Length, Is.EqualTo(plain.Length));
                }
                Assert.That(ReadAll(yargStream), Is.EqualTo(plain));
            }
            Assert.That(fileSystem.OpenedPaths, Is.EqualTo([PATH, PATH, PATH, PATH]));
        }
        finally
        {
            YARGFileSystem.Unregister(FakeRoot);
        }
    }

    [Test]
    public void YARGSongFileStream_CloneWithoutPathThrows()
    {
        Assert.That(YARGSongFileStream.TryLoad(new MemoryStream(WrapInYargSong(CreateSng())), out var yargStream), Is.True);
        using (yargStream)
        {
            Assert.That(() => yargStream.Clone(), Throws.InstanceOf<NotSupportedException>());
        }
    }

    [Test]
    public void SngEntrySidecars_ProbeThroughRegisteredFileSystem()
    {
        const string SNG = FakeRoot + @"\song.sng";
        byte[] mp4 = [1];
        byte[] yarground = [2];
        var fileSystem = new InMemoryFileSystem();
        fileSystem.AddFile(FakeRoot + @"\song.mov", [0]);
        fileSystem.AddFile(FakeRoot + @"\song.mp4", mp4);
        fileSystem.AddFile(FakeRoot + @"\song.yarground", yarground);
        YARGFileSystem.Register(FakeRoot, fileSystem);
        try
        {
            using (var video = SngEntry.OpenExternalVideo(SNG, "_clean"))
            {
                Assert.That(video, Is.Not.Null);
                Assert.That(ReadAll(video!), Is.EqualTo(mp4));
            }
            using (var background = SngEntry.OpenExternalYarground(SNG))
            {
                Assert.That(background, Is.Not.Null);
                Assert.That(ReadAll(background!), Is.EqualTo(yarground));
            }
            Assert.That(fileSystem.OpenedPaths, Is.EqualTo([FakeRoot + @"\song.mp4", FakeRoot + @"\song.yarground"]));

            Assert.That(SngEntry.OpenExternalVideo(FakeRoot + @"\other.sng", "_clean"), Is.Null);
            Assert.That(SngEntry.OpenExternalYarground(FakeRoot + @"\other.sng"), Is.Null);
        }
        finally
        {
            YARGFileSystem.Unregister(FakeRoot);
        }
    }

    private static void AssertNoOpenHandles(string path)
    {
        Assert.That(() => new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None).Dispose(),
            Throws.Nothing, "A handle to the sng file was left open");
    }

    private static byte[] CreateSng()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write("SNGPKG"u8);
        writer.Write(Version);
        // All-zero keys make the SngMask byte at index i equal to i.
        writer.Write(new byte[16]);

        byte[] key = "name"u8.ToArray();
        byte[] value = "Test Song"u8.ToArray();
        writer.Write((long) (sizeof(ulong) + sizeof(int) + key.Length + sizeof(int) + value.Length));
        writer.Write(1UL);
        writer.Write(key.Length);
        writer.Write(key);
        writer.Write(value.Length);
        writer.Write(value);

        byte[] name = Encoding.UTF8.GetBytes(ListingName);
        writer.Write((long) (sizeof(ulong) + 1 + name.Length + 2 * sizeof(long)));
        writer.Write(1UL);
        writer.Write((byte) name.Length);
        writer.Write(name);
        writer.Write((long) Payload.Length);
        long dataPosition = stream.Position + sizeof(long);
        writer.Write(dataPosition);

        for (int i = 0; i < Payload.Length; ++i)
        {
            writer.Write((byte) (Payload[i] ^ i));
        }

        writer.Flush();
        return stream.ToArray();
    }

    /// <summary>
    /// Builds the cipher text by searching, per position, for the byte that YARGSongFileStream decrypts to
    /// the wanted plain byte, so the test does not depend on the cipher's internals.
    /// </summary>
    private static byte[] WrapInYargSong(byte[] plain)
    {
        byte[] header = new byte[YargSongHeaderSize];
        "YARGSONG"u8.CopyTo(header);
        for (int i = 8; i < header.Length; ++i)
        {
            header[i] = (byte) (i * 37 + 11);
        }

        var decryptedByCandidate = new byte[256][];
        for (int candidate = 0; candidate < 256; ++candidate)
        {
            byte[] probe = new byte[header.Length + plain.Length];
            header.CopyTo(probe, 0);
            probe.AsSpan(header.Length).Fill((byte) candidate);

            Assert.That(YARGSongFileStream.TryLoad(new MemoryStream(probe), out var yargStream), Is.True);
            using (yargStream)
            {
                decryptedByCandidate[candidate] = ReadAll(yargStream);
            }
        }

        byte[] wrapped = new byte[header.Length + plain.Length];
        header.CopyTo(wrapped, 0);
        for (int position = 0; position < plain.Length; ++position)
        {
            int candidate = 0;
            while (decryptedByCandidate[candidate][position] != plain[position])
            {
                ++candidate;
                Assert.That(candidate, Is.LessThan(256), "No cipher byte decrypts to the wanted value");
            }
            wrapped[header.Length + position] = (byte) candidate;
        }
        return wrapped;
    }

    private static byte[] ReadAll(Stream stream)
    {
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    private static string CreateTempFile(byte[] contents)
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".sng");
        File.WriteAllBytes(path, contents);
        return path;
    }
}
