using NUnit.Framework;
using YARG.Core.Extensions;
using YARG.Core.IO;

namespace YARG.Core.UnitTests.IO;

public class AbridgedFileInfoTests
{
    private const string FAKE_ROOT = @"\\fake\abridged";

    private string _root = null!;
    private string _file = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "YARG.Core.UnitTests", Guid.NewGuid().ToString("N"));
        _file = Path.Combine(_root, "notes.chart");
        Directory.CreateDirectory(_root);
        File.WriteAllBytes(_file, [1, 2, 3]);
    }

    [TearDown]
    public void TearDown()
    {
        Directory.Delete(_root, recursive: true);
    }

    private static IEnumerable<(DateTime LastWriteUtc, DateTime CreationUtc)> TimePairs()
    {
        var winter = new DateTime(2024, 1, 15, 12, 30, 45, DateTimeKind.Utc).AddTicks(1234567);
        var summer = new DateTime(2024, 7, 15, 12, 30, 45, DateTimeKind.Utc).AddTicks(7654321);
        yield return (winter, summer);
        yield return (summer, winter);
        yield return (summer, summer);

        // In zones with DST, the hour repeated at the fall-back transition is the only case where local and
        // UTC ordering disagree and where ToBinary sets its ambiguous-DST bit.
        var hour = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (int i = 0; i < 366 * 24; ++i, hour = hour.AddHours(1))
        {
            if (TimeZoneInfo.Local.IsAmbiguousTime(hour.ToLocalTime()))
            {
                var first = hour.AddMinutes(50);
                yield return (first, first.AddMinutes(20));
                yield return (first.AddMinutes(20), first);
                break;
            }
        }
    }

    [Test]
    public void LocalStats_MatchFileInfoValuesBitForBit()
    {
        foreach (var (lastWriteUtc, creationUtc) in TimePairs())
        {
            File.SetCreationTimeUtc(_file, creationUtc);
            File.SetLastWriteTimeUtc(_file, lastWriteUtc);

            var info = new FileInfo(_file);
            var legacy = info.LastWriteTime > info.CreationTime ? info.LastWriteTime : info.CreationTime;
            var abridged = new AbridgedFileInfo(_file);

            Assert.That(YARGFileSystem.TryStat(_file, out var stat), Is.True);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(abridged.FullName, Is.EqualTo(info.FullName));
                Assert.That(abridged.LastWriteTime.ToBinary(), Is.EqualTo(legacy.ToBinary()), $"{lastWriteUtc:O} / {creationUtc:O}");
                Assert.That(AbridgedFileInfo.NormalizedLastWrite(in stat).ToBinary(), Is.EqualTo(legacy.ToBinary()));
                Assert.That(AbridgedFileInfo.NormalizedLastWrite(info).ToBinary(), Is.EqualTo(legacy.ToBinary()));
                Assert.That(AbridgedFileInfo.RawLastWrite(in stat).ToBinary(), Is.EqualTo(info.LastWriteTime.ToBinary()));
                Assert.That(AbridgedFileInfo.Validate(_file, in legacy), Is.True);
                Assert.That(abridged.IsStillValid(), Is.True);
            }
        }
    }

    [Test]
    public void TryParseInfo_AcceptsSerializedLegacyValueAndRejectsOthers()
    {
        var info = new FileInfo(_file);
        var legacy = info.LastWriteTime > info.CreationTime ? info.LastWriteTime : info.CreationTime;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Parse(_file, legacy, out var abridged), Is.True);
            Assert.That(abridged.FullName, Is.EqualTo(info.FullName));
            Assert.That(Parse(_file, legacy.AddTicks(1), out _), Is.False);
            Assert.That(Parse(Path.Combine(_root, "missing.chart"), legacy, out _), Is.False);
            Assert.That(Parse(_root, legacy, out _), Is.False, "A directory is not a file");
        }
    }

    [Test]
    public void ValidateAndExists_RejectMissingFilesAndDirectories()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(AbridgedFileInfo.Validate(Path.Combine(_root, "missing.chart"), DateTime.Now), Is.False);
            Assert.That(AbridgedFileInfo.Validate(_root, new AbridgedFileInfo(_root).LastWriteTime), Is.False);
            Assert.That(new AbridgedFileInfo(_file, DateTime.Now).Exists(), Is.True);
            Assert.That(new AbridgedFileInfo(_root, DateTime.Now).Exists(), Is.False);
        }
    }

    [Test]
    public void MissingFile_KeepsFileInfoFallbackTime()
    {
        string missing = Path.Combine(_root, "missing.chart");
        Assert.That(new AbridgedFileInfo(missing).LastWriteTime, Is.EqualTo(new FileInfo(missing).LastWriteTime));
    }

    [Test]
    public void RegisteredBackend_NormalizesUtcStatsToLocalTime()
    {
        string path = FAKE_ROOT + @"\song\notes.mid";
        var lastWrite = new DateTime(2024, 3, 4, 5, 6, 7, DateTimeKind.Utc);
        var creation = lastWrite.AddDays(1);
        var fileSystem = new InMemoryFileSystem();
        fileSystem.AddFile(path, [0], lastWrite, creation);

        YARGFileSystem.Register(FAKE_ROOT, fileSystem);
        try
        {
            var abridged = new AbridgedFileInfo(path);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(abridged.LastWriteTime, Is.EqualTo(creation.ToLocalTime()));
                Assert.That(abridged.LastWriteTime.Kind, Is.EqualTo(DateTimeKind.Local));
                Assert.That(abridged.IsStillValid(), Is.True);
                Assert.That(abridged.Exists(), Is.True);
                Assert.That(AbridgedFileInfo.Validate(path, lastWrite.ToLocalTime()), Is.False);
                Assert.That(AbridgedFileInfo.Validate(FAKE_ROOT + @"\song", abridged.LastWriteTime), Is.False);
                Assert.That(fileSystem.StattedPaths, Does.Contain(path));
            }
        }
        finally
        {
            YARGFileSystem.Unregister(FAKE_ROOT);
        }
    }

    private static bool Parse(string file, DateTime serialized, out AbridgedFileInfo abridged)
    {
        using var memory = new MemoryStream();
        memory.Write(serialized.ToBinary(), Endianness.Little);
        using var data = FixedArray<byte>.Alloc((int) memory.Length);
        memory.ToArray().CopyTo(data.Span);
        var stream = data.ToValueStream();
        return AbridgedFileInfo.TryParseInfo(file, ref stream, out abridged);
    }
}
