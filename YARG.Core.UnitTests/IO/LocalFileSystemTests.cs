using NUnit.Framework;
using YARG.Core.IO;

namespace YARG.Core.UnitTests.IO;

public class LocalFileSystemTests
{
    private static readonly DateTime WRITE_TIME = new(2023, 5, 6, 7, 8, 9, DateTimeKind.Utc);

    private string _root = null!;
    private string _file = null!;
    private string _subdirectory = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "YARG.Core.UnitTests", Guid.NewGuid().ToString("N"));
        _subdirectory = Path.Combine(_root, "Sub");
        _file = Path.Combine(_root, "Notes.chart");
        Directory.CreateDirectory(_subdirectory);
        File.WriteAllBytes(_file, [1, 2, 3, 4, 5]);
        File.SetLastWriteTimeUtc(_file, WRITE_TIME);
    }

    [TearDown]
    public void TearDown()
    {
        Directory.Delete(_root, recursive: true);
    }

    [Test]
    public void TryStat_File_ReturnsLengthAndUtcTimes()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(LocalFileSystem.Instance.TryStat(_file, out var stat), Is.True);
            Assert.That(stat.IsDirectory, Is.False);
            Assert.That(stat.Length, Is.EqualTo(5));
            Assert.That(stat.LastWriteTimeUtc, Is.EqualTo(WRITE_TIME));
            Assert.That(stat.LastWriteTimeUtc.Kind, Is.EqualTo(DateTimeKind.Utc));
            Assert.That(stat.CreationTimeUtc, Is.EqualTo(File.GetCreationTimeUtc(_file)));
            Assert.That(stat.LastWriteTimeUtc.ToLocalTime(), Is.EqualTo(new FileInfo(_file).LastWriteTime));
        }
    }

    [Test]
    public void TryStat_Directory_ReportsDirectory()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(LocalFileSystem.Instance.TryStat(_subdirectory, out var stat), Is.True);
            Assert.That(stat.IsDirectory, Is.True);
            Assert.That(stat.Length, Is.Zero);
            Assert.That(stat.LastWriteTimeUtc, Is.EqualTo(Directory.GetLastWriteTimeUtc(_subdirectory)));
        }
    }

    [Test]
    public void TryStat_Missing_ReturnsFalse()
    {
        Assert.That(LocalFileSystem.Instance.TryStat(Path.Combine(_root, "missing"), out _), Is.False);
    }

    [Test]
    public void Enumerate_ReturnsChildrenWithMetadata()
    {
        var entries = LocalFileSystem.Instance.Enumerate(_root).OrderBy(e => e.Name, StringComparer.Ordinal).ToList();

        Assert.That(entries, Has.Count.EqualTo(2));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(entries[0].Name, Is.EqualTo("Notes.chart"));
            Assert.That(entries[0].FullName, Is.EqualTo(_file));
            Assert.That(entries[0].Stat.IsDirectory, Is.False);
            Assert.That(entries[0].Stat.Length, Is.EqualTo(5));
            Assert.That(entries[0].Stat.LastWriteTimeUtc, Is.EqualTo(WRITE_TIME));
            Assert.That(entries[1].Name, Is.EqualTo("Sub"));
            Assert.That(entries[1].FullName, Is.EqualTo(_subdirectory));
            Assert.That(entries[1].Stat.IsDirectory, Is.True);
        }
    }

    [Test]
    public void OpenRead_ReturnsSeekableStreamWithLength()
    {
        using var stream = LocalFileSystem.Instance.OpenRead(_file, 1);
        var buffer = new byte[5];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(stream.CanSeek, Is.True);
            Assert.That(stream.Length, Is.EqualTo(5));
            Assert.That(stream.Read(buffer, 0, buffer.Length), Is.EqualTo(5));
            Assert.That(buffer, Is.EqualTo(new byte[] { 1, 2, 3, 4, 5 }));
        }
    }

    [Test]
    public void OpenRead_AllowsConcurrentReaders()
    {
        using var first = LocalFileSystem.Instance.OpenRead(_file);
        using var second = LocalFileSystem.Instance.OpenRead(_file);

        Assert.That(second.Length, Is.EqualTo(first.Length));
    }

    [Test]
    public void Exists_DistinguishesFilesFromDirectories()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(LocalFileSystem.Instance.FileExists(_file), Is.True);
            Assert.That(LocalFileSystem.Instance.FileExists(_subdirectory), Is.False);
            Assert.That(LocalFileSystem.Instance.DirectoryExists(_subdirectory), Is.True);
            Assert.That(LocalFileSystem.Instance.DirectoryExists(_file), Is.False);
        }
    }
}
