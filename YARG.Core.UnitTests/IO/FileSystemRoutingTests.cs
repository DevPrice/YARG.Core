using System.Text;
using NUnit.Framework;
using YARG.Core.IO;
using YARG.Core.IO.Ini;
using YARG.Core.UnitTests.Audio;

namespace YARG.Core.UnitTests.IO;

public class FileSystemRoutingTests
{
    private const string ROOT = @"\\fake\share";

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
    }

    [Test]
    public void FixedArrayLoadFile_ReadsFromRegisteredBackend()
    {
        const string PATH = @"\\fake\share\song\data.bin";
        _fileSystem.AddFile(PATH, [9, 8, 7]);

        using var data = FixedArray.LoadFile(PATH);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(data.ReadOnlySpan.ToArray(), Is.EqualTo(new byte[] { 9, 8, 7 }));
            Assert.That(_fileSystem.OpenedPaths, Is.EqualTo([PATH]));
        }
    }

    [Test]
    public void ReadIniFile_ReadsFromRegisteredBackend()
    {
        const string PATH = @"\\fake\share\song\song.ini";
        _fileSystem.AddFile(PATH, Encoding.UTF8.GetBytes("[song]\nname = Remote\n"));
        var lookups = new Dictionary<string, Dictionary<string, IniModifierOutline>>
        {
            ["[song]"] = new() { ["name"] = new("name", ModifierType.String) },
        };

        var collections = YARGIniReader.ReadIniFile(PATH, lookups);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(collections["[song]"].Extract("name", out string name), Is.True);
            Assert.That(name, Is.EqualTo("Remote"));
            Assert.That(_fileSystem.OpenedPaths, Is.EqualTo([PATH]));
        }
    }

    // YARGImage.Load decodes through the native STB2CSharp library, which the tests don't ship;
    // LoadDXT shares its FixedArray.LoadFile path and is fully managed.
    [Test]
    public void YARGImageLoadDXT_ReadsFromRegisteredBackend()
    {
        const string PATH = @"\\fake\share\song\album.png_xbox";
        var bytes = new byte[48];
        bytes[1] = 0x04;
        BitConverter.GetBytes(0x08).CopyTo(bytes, 2);
        BitConverter.GetBytes((short) 4).CopyTo(bytes, 7);
        BitConverter.GetBytes((short) 2).CopyTo(bytes, 9);
        _fileSystem.AddFile(PATH, bytes);

        using var image = YARGImage.LoadDXT(PATH);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(image.Width, Is.EqualTo(4));
            Assert.That(image.Height, Is.EqualTo(2));
            Assert.That(image.Format, Is.EqualTo(ImageFormat.DXT1));
            Assert.That(_fileSystem.OpenedPaths, Is.EqualTo([PATH]));
        }
    }

    [Test]
    public void LoadCustomFile_OpensThroughRegisteredBackend()
    {
        const string PATH = @"\\fake\share\song\preview.ogg";
        _fileSystem.AddFile(PATH, [0]);

        var mixer = new FakeAudioManager().LoadCustomFile(PATH, 1f, 1.0, normalize: false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(mixer, Is.Null);
            Assert.That(_fileSystem.OpenedPaths, Is.EqualTo([PATH]));
        }
    }
}
